namespace Snex;

public static class Prompt
{
    public static string BuildSystemPrompt(string personality, EmojiConfig emojis)
    {
        var emojiList = string.Join(", ", emojis.Emoji.Keys.Select(k => $"[emoji:{k}]"));
        var gifList = string.Join(", ", emojis.Gifs.Keys.Select(k => $"[gif:{k}]"));

        return $"""
            {personality}

            Доступные эмодзи (вставляй тегом прямо в текст): {emojiList}
            Доступные гифки (вставляй тегом, если хочешь отправить гифку): {gifList}

            Пример использования: "это было неожиданно [emoji:surprised]"
            """;
    }
}
