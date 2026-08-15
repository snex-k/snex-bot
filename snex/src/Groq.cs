using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Snex;

public class Groq
{
    private const string GroqUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string Model = "openai/gpt-oss-120b";

    private readonly HttpClient _http;

    public Groq(string apiKey)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }
    
    public async Task<string> AskAsync(string systemPrompt, string userMessage)
    {
        var request = new ChatRequest
        {
            Model = Model,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userMessage },
            ],
        };

        var response = await _http.PostAsJsonAsync(GroqUrl, request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>();
        return result?.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
    }

    public async Task<string?> ExtractFactAsync(string message)
    {
        const string systemPrompt = """
            Ты анализируешь сообщение пользователя из чата. Если в нём есть
            конкретный факт о человеке, стоящий запоминания (имя, увлечение,
            профессия, место жительства, предпочтение и т.п.) — выведи его
            одной короткой фразой на русском, без пояснений. Если ничего
            примечательного нет — выведи ровно слово NONE.
            """;

        var raw = await AskAsync(systemPrompt, message);
        var trimmed = raw.Trim();

        return trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) || trimmed.Length == 0
            ? null
            : trimmed;
    }

    private class ChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required List<ChatMessage> Messages { get; init; }
    }

    private class ChatMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; init; } = [];
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public required ChatMessage Message { get; init; }
    }
}
