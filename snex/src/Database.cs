using Npgsql;

namespace Snex;

public record ChatMessage(string AuthorName, string Content);
public record UserFact(string Fact);

public class Database
{
    private readonly string _connectionString;

    private Database(string connectionString)
    {
        _connectionString = connectionString;
    }

    public static Database Connect(string databaseUrl)
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Require,
            MaxAutoPrepare = 0,
            MaxPoolSize = 5,
            ConnectionIdleLifetime = 30,
            Timeout = 15,
            CommandTimeout = 15,
        };

        return new Database(builder.ConnectionString);
    }

    private NpgsqlConnection OpenConnection()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task SaveMessageAsync(string channelId, string authorId, string authorName, string content)
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "insert into messages (channel_id, author_id, author_name, content) values ($1, $2, $3, $4)",
            conn);
        cmd.Parameters.AddWithValue(channelId);
        cmd.Parameters.AddWithValue(authorId);
        cmd.Parameters.AddWithValue(authorName);
        cmd.Parameters.AddWithValue(content);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ChatMessage>> RecentMessagesAsync(string channelId, int limit)
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "select author_name, content from messages where channel_id = $1 order by created_at desc limit $2",
            conn);
        cmd.Parameters.AddWithValue(channelId);
        cmd.Parameters.AddWithValue(limit);

        var results = new List<ChatMessage>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ChatMessage(reader.GetString(0), reader.GetString(1)));
        }

        results.Reverse();
        return results;
    }

    public async Task SaveFactAsync(string userId, string fact)
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "insert into user_facts (user_id, fact) values ($1, $2)",
            conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(fact);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<UserFact>> UserFactsAsync(string userId, int limit)
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "select fact from user_facts where user_id = $1 order by created_at desc limit $2",
            conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(limit);

        var results = new List<UserFact>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new UserFact(reader.GetString(0)));
        }

        return results;
    }
}
