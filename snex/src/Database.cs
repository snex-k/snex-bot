sing Npgsql;

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

    public static async Task<Database> ConnectAsync(string databaseUrl)
    {
        // Npgsql не понимает URI-формат (postgresql://user:pass@host/db)
        // напрямую — нужно самим разобрать его на составляющие и собрать
        // classic ADO.NET connection string.
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);

        // Supabase pooler сейчас отдаёт IPv4-адрес, а на Render иногда
        // нестабильно работает резолвинг/маршрутизация через IPv6 —
        // резолвим хост в IPv4 явно, чтобы не ловить обрывы соединения.
        var addresses = await System.Net.Dns.GetHostAddressesAsync(
            uri.Host, System.Net.Sockets.AddressFamily.InterNetwork);
        var host = addresses.Length > 0 ? addresses[0].ToString() : uri.Host;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Require,
            // Supabase connection pooler (порт 6543, Transaction mode) не
            // поддерживает server-side prepared statements между разными
            // соединениями пула — отключаем их в Npgsql явно, иначе можно
            // получить "prepared statement already exists" (как было с sqlx).
            MaxAutoPrepare = 0,
            // Транзакционный pooler закрывает соединения агрессивнее, чем
            // обычный Postgres — держим пул небольшим и не переиспользуем
            // соединения слишком долго, чтобы не ловить оборванные стримы.
            MaxPoolSize = 5,
            MinPoolSize = 1,
            Pooling = true,
            ConnectionIdleLifetime = 300,
            ConnectionPruningInterval = 60,
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

    /// Сохраняет сообщение в историю канала (используется для контекста диалога).
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

    /// Возвращает последние N сообщений канала в хронологическом порядке
    /// (старые → новые), для использования как контекст диалога.
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

    /// Сохраняет новый факт о пользователе.
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

    /// Возвращает известные факты о пользователе (последние N).
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