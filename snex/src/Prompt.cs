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

    public static string BuildContextMessage(
        List<ChatMessage> history,
        List<UserFact> facts,
        string currentAuthor,
        string currentContent)
    {
        var parts = new List<string>();

        if (facts.Count > 0)
        {
            var factsText = string.Join("\n", facts.Select(f => $"- {f.Fact}"));
            parts.Add($"Известные факты о пользователе {currentAuthor}:\n{factsText}");
        }

        if (history.Count > 0)
        {
            var historyText = string.Join("\n", history.Select(m => $"{m.AuthorName}: {m.Content}"));
            parts.Add($"Недавняя переписка в канале:\n{historyText}");
        }

        parts.Add($"Новое сообщение от {currentAuthor}: {currentContent}");

        return string.Join("\n\n", parts);
    }
}
