using Tomlyn;
using Tomlyn.Model;

namespace Snex;

public class EmojiConfig
{
    public Dictionary<string, string> Emoji { get; init; } = new();
    public Dictionary<string, string> Gifs { get; init; } = new();
}

public class Config
{
    public required string DiscordToken { get; init; }
    public required string GroqApiKey { get; init; }
    public required string Personality { get; init; }
    public required EmojiConfig Emojis { get; init; }
    public double RandomReplyChance { get; init; } = 0.15;
    public required string DatabaseUrl { get; init; }

    public static Config Load()
    {
        DotNetEnv.Env.Load();

        var discordToken = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
            ?? throw new InvalidOperationException("DISCORD_TOKEN не найден в .env");
        var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY")
            ?? throw new InvalidOperationException("GROQ_API_KEY не найден в .env");

        var personality = File.ReadAllText("config/personality.txt");

        var emojisRaw = File.ReadAllText("config/emojis.toml");
        var emojis = ParseEmojiConfig(emojisRaw);

        var chanceRaw = Environment.GetEnvironmentVariable("RANDOM_REPLY_CHANCE");
        var chance = double.TryParse(chanceRaw, out var parsed) ? parsed : 0.15;

        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("DATABASE_URL не найден в .env");

        return new Config
        {
            DiscordToken = discordToken,
            GroqApiKey = groqApiKey,
            Personality = personality,
            Emojis = emojis,
            RandomReplyChance = chance,
            DatabaseUrl = databaseUrl,
        };
    }

    private static EmojiConfig ParseEmojiConfig(string toml)
    {
        var model = Toml.ToModel(toml);
        var emoji = new Dictionary<string, string>();
        var gifs = new Dictionary<string, string>();

        if (model["emoji"] is TomlTable emojiTable)
        {
            foreach (var kv in emojiTable)
            {
                emoji[kv.Key] = kv.Value?.ToString() ?? string.Empty;
            }
        }

        if (model["gifs"] is TomlTable gifsTable)
        {
            foreach (var kv in gifsTable)
            {
                gifs[kv.Key] = kv.Value?.ToString() ?? string.Empty;
            }
        }

        return new EmojiConfig { Emoji = emoji, Gifs = gifs };
    }
}
