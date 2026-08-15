using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Snex;

public class Handler
{
    private readonly Config _config;
    private readonly Groq _groq;
    private readonly string _systemPrompt;
    private readonly Random _random = new();

    public Handler(Config config, Groq groq, string systemPrompt)
    {
        _config = config;
        _groq = groq;
        _systemPrompt = systemPrompt;
    }

    public async ValueTask HandleAsync(Message message, GatewayClient client)
    {
        if (message.Author.IsBot)
        {
            return;
        }

        var isMentioned = message.MentionedUsers.Any(u => u.Id == client.Cache.User!.Id);

        if (!isMentioned)
        {
            var roll = _random.NextDouble();
            if (roll > _config.RandomReplyChance)
            {
                return;
            }
        }

        string rawReply;
        try
        {
            rawReply = await _groq.AskAsync(_systemPrompt, message.Content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Groq API ошибка: {ex.Message}");
            await SendErrorAsync(message, client, "не смог получить ответ от ИИ");
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
                await SendErrorAsync(message, client, "не удалось отправить сообщение");
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
    }

    private static async Task SendErrorAsync(Message message, GatewayClient client, string errorText)
    {
        try
        {
            await client.Rest.SendMessageAsync(message.ChannelId, new MessageProperties
            {
                Content = $"`ошибка: {errorText}`",
            });
        }
        catch
        {
        }
    }
}
