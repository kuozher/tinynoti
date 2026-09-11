using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TinyNoti.App;

public sealed class WindowsNotificationPayloadReader : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connStr;

    public WindowsNotificationPayloadReader(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Notifications\wpndatabase.db");

        _connStr = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<string?> TryGetPayloadAsync(uint notificationId, int retryCount = 2, int delayMs = 30)
    {
        if (!File.Exists(_dbPath))
        {
            return null;
        }

        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                var payload = QueryPayload(notificationId);
                if (payload is not null)
                {
                    return payload;
                }
            }
            catch
            {
                // Fallback / retry on concurrency
            }

            if (attempt < retryCount)
            {
                await Task.Delay(delayMs);
            }
        }

        return null;
    }

    private string? QueryPayload(uint notificationId)
    {
        using var conn = new SqliteConnection(_connStr);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Payload FROM Notification WHERE Id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", (long)notificationId);

        var result = cmd.ExecuteScalar();
        if (result is byte[] bytes && bytes.Length > 0)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return null;
    }

    public void Dispose()
    {
    }
}
