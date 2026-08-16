using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Snex;

public class Handler
{
    private const int HistoryLimit = 15;
    private const int FactsLimit = 8;

    private readonly Config _config;
    private readonly Groq _groq;
    private readonly string _systemPrompt;
    private readonly Database _db;
    private readonly Random _random = new();

    public Handler(Config config, Groq groq, string systemPrompt, Database db)
    {
        _config = config;
        _groq = groq;
        _systemPrompt = systemPrompt;
        _db = db;
    }

    public async ValueTask HandleAsync(Message message, GatewayClient client)
    {
        // Не отвечаем сами себе и другим ботам
        if (message.Author.IsBot)
        {
            return;
        }

        var channelId = message.ChannelId.ToString();
        var authorId = message.Author.Id.ToString();
        var authorName = message.Author.Username;

        // Сохраняем сообщение в историю канала независимо от того,
        // ответит бот или нет — так собирается контекст на будущее.
        // Не ждём завершения — не блокирует остальную обработку.
        var saveTask = TrySaveAsync(() => _db.SaveMessageAsync(channelId, authorId, authorName, message.Content));

        // На упоминание отвечаем всегда. Остальные сообщения —
        // с рандомным шансом, чтобы бот иногда вклинивался в чат сам,
        // но не отвечал на каждую реплику подряд.
        var isMentioned = message.MentionedUsers.Any(u => u.Id == client.Cache.User!.Id);

        if (!isMentioned)
        {
            var roll = _random.NextDouble();
            if (roll > _config.RandomReplyChance)
            {
                await saveTask;
                return;
            }
        }

        // Подтягиваем историю канала и известные факты о человеке
        // параллельно — они не зависят друг от друга.
        var dbStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var historyTask = TryGetAsync(() => _db.RecentMessagesAsync(channelId, HistoryLimit), []);
        var factsTask = TryGetAsync(() => _db.UserFactsAsync(authorId, FactsLimit), []);
        await Task.WhenAll(saveTask, historyTask, factsTask);
        dbStopwatch.Stop();
        Console.WriteLine($"[timing] БД (сохранение+история+факты): {dbStopwatch.ElapsedMilliseconds}ms");

        var history = await historyTask;
        var facts = await factsTask;

        var contextMessage = Prompt.BuildContextMessage(history, facts, authorName, message.Content);

        string rawReply;
        var groqStopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            rawReply = await _groq.AskAsync(_systemPrompt, contextMessage);
            groqStopwatch.Stop();
            Console.WriteLine($"[timing] Groq API: {groqStopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Groq API ошибка: {ex.Message}");
            await ErrorMessage.SendAsync(client, message.ChannelId, "не смог получить ответ от ИИ");
            return;
        }

        var parsed = Response.Parse(rawReply, _config.Emojis);

        var sendStopwatch = System.Diagnostics.Stopwatch.StartNew();
        if (!string.IsNullOrWhiteSpace(parsed.Text))
        {
            try
            {
                await client.Rest.SendMessageAsync(message.ChannelId, new MessageProperties
                {
                    Content = parsed.Text,
                });
                sendStopwatch.Stop();
                Console.WriteLine($"[timing] Отправка в Discord: {sendStopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"не удалось отправить сообщение: {ex.Message}");
                await ErrorMessage.SendAsync(client, message.ChannelId, "не удалось отправить сообщение");
            }
        }

        foreach (var gifUrl in parsed.GifUrls)
        {
            try
            {
                await client.Rest.SendMessageAsync(message.ChannelId, new MessageProperties
                {
                    Content = gifUrl,
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"не удалось отправить гифку: {ex.Message}");
            }
        }

        // Отдельным запросом проверяем, не сказал ли человек что-то,
        // что стоит запомнить на будущее. Не блокирует ответ пользователю —
        // запускается в фоне после того, как сообщение уже отправлено.
        _ = Task.Run(async () =>
        {
            try
            {
                var fact = await _groq.ExtractFactAsync(message.Content);
                if (fact is not null)
                {
                    await _db.SaveFactAsync(authorId, fact);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ошибка при извлечении факта: {ex.Message}");
            }
        });
    }

    private static async Task TrySaveAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"не удалось сохранить сообщение: {ex.Message}");
        }
    }

    private static async Task<T> TryGetAsync<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка запроса к базе: {ex.Message}");
            return fallback;
        }
    }
}