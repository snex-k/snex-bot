using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Snex;

public class Handler
{
    private const int HistoryLimit = 15;
    private const int FactsLimit = 10;

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
        if (message.Author.IsBot)
        {
            return;
        }

        var channelId = message.ChannelId.ToString();
        var authorId = message.Author.Id.ToString();
        var authorName = message.Author.Username;
        var saveTask = TrySaveAsync(() => _db.SaveMessageAsync(channelId, authorId, authorName, message.Content));
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

        var historyTask = TryGetAsync(() => _db.RecentMessagesAsync(channelId, HistoryLimit), []);
        var factsTask = TryGetAsync(() => _db.UserFactsAsync(authorId, FactsLimit), []);
        await Task.WhenAll(saveTask, historyTask, factsTask);

        var history = await historyTask;
        var facts = await factsTask;

        var contextMessage = Prompt.BuildContextMessage(history, facts, authorName, message.Content);

        string rawReply;
        try
        {
            rawReply = await _groq.AskAsync(_systemPrompt, contextMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Groq API ошибка: {ex.Message}");
            await ErrorMessage.SendAsync(client, message.ChannelId, "не смог получить ответ от ИИ");
            return;
        }

        var parsed = Response.Parse(rawReply, _config.Emojis);

        if (!string.IsNullOrWhiteSpace(parsed.Text))
        {
            try
            {
                await client.Rest.SendMessageAsync(message.ChannelId, new MessageProperties
                {
                    Content = parsed.Text,
                });
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
