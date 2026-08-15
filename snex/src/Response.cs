namespace Snex;

public record ParsedResponse(string Text, List<string> GifUrls);

public static class Response
{
    public static ParsedResponse Parse(string raw, EmojiConfig emojis)
    {
        var text = raw;
        var gifUrls = new List<string>();

        foreach (var (key, emoji) in emojis.Emoji)
        {
            text = text.Replace($"[emoji:{key}]", emoji);
        }

        foreach (var (key, url) in emojis.Gifs)
        {
            var tag = $"[gif:{key}]";
            if (text.Contains(tag))
            {
                gifUrls.Add(url);
                text = text.Replace(tag, "").Trim();
            }
        }

        return new ParsedResponse(text, gifUrls);
    }
}
