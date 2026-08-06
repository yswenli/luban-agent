using System.Data;
using System.Data.SQLite;
using LuBan.AIAgent.LocalMemory;
using LuBan.AIAgent.Utils.Text;

namespace LubanAgent.Infrastructure;

public class SqliteLocalMemoryStore : ILocalMemoryStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SqliteLocalMemoryStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connectionString = $"Data Source={dbPath};Pooling=false;Foreign Keys=false;";
        EnsureSchemaAsync().GetAwaiter().GetResult();
    }

    private SQLiteConnection CreateConnection() => new(_connectionString);

    private async Task EnsureSchemaAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS LocalMemory (
                    Id TEXT PRIMARY KEY,
                    Content TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    VectorDimension INTEGER NOT NULL,
                    Vector BLOB NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(LocalMemory)";
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    existing.Add(reader.GetString(1));
            }
            foreach (var col in new[] { "WorkspaceId", "ContentHash", "ExpiresAt" })
            {
                if (!existing.Contains(col))
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = col switch
                    {
                        "WorkspaceId" => "ALTER TABLE LocalMemory ADD COLUMN WorkspaceId TEXT",
                        "ContentHash" => "ALTER TABLE LocalMemory ADD COLUMN ContentHash TEXT",
                        _ => "ALTER TABLE LocalMemory ADD COLUMN ExpiresAt TEXT"
                    };
                    await alter.ExecuteNonQueryAsync();
                }
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Content FROM LocalMemory WHERE ContentHash IS NULL OR ContentHash = ''";
            using var reader = await cmd.ExecuteReaderAsync();
            var pending = new List<(string Id, string Content)>();
            while (await reader.ReadAsync())
                pending.Add((reader.GetString(0), reader.GetString(1)));
            reader.Close();
            foreach (var (id, content) in pending)
            {
                using var update = conn.CreateCommand();
                update.CommandText = "UPDATE LocalMemory SET ContentHash = @hash WHERE Id = @id";
                update.Parameters.AddWithValue("@hash", TextUtils.ComputeContentHash(content));
                update.Parameters.AddWithValue("@id", id);
                await update.ExecuteNonQueryAsync();
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_localmemory_category ON LocalMemory(Category);
                CREATE INDEX IF NOT EXISTS idx_localmemory_updated ON LocalMemory(UpdatedAt DESC);
                CREATE INDEX IF NOT EXISTS idx_localmemory_ws_hash ON LocalMemory(WorkspaceId, ContentHash);
                """;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<MemoryEntry> UpsertAsync(MemoryEntry entry, byte[] vectorBytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(vectorBytes);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            MemoryEntry? existing = null;
            using (var find = conn.CreateCommand())
            {
                find.CommandText = "SELECT Id, CreatedAt, Content, Category FROM LocalMemory WHERE WorkspaceId IS @ws AND ContentHash = @hash";
                find.Parameters.AddWithValue("@ws", (object?)entry.WorkspaceId ?? DBNull.Value);
                find.Parameters.AddWithValue("@hash", entry.ContentHash);
                using var reader = await find.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync())
                    existing = new MemoryEntry
                    {
                        Id = reader.GetString(0),
                        CreatedAt = DateTime.ParseExact(reader.GetString(1), "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                        Content = reader.GetString(2),
                        Category = reader.GetString(3)
                    };
            }

            if (existing != null)
            {
                using var update = conn.CreateCommand();
                update.CommandText = """
                    UPDATE LocalMemory
                    SET Content = @content, Category = @category, UpdatedAt = @updatedAt,
                        ExpiresAt = @expiresAt, VectorDimension = @dimension, Vector = @vector
                    WHERE Id = @id
                    """;
                update.Parameters.AddWithValue("@content", entry.Content);
                update.Parameters.AddWithValue("@category", entry.Category);
                update.Parameters.AddWithValue("@updatedAt", entry.UpdatedAt.ToString("O"));
                update.Parameters.AddWithValue("@expiresAt", (object?)entry.ExpiresAt?.ToString("O") ?? DBNull.Value);
                update.Parameters.AddWithValue("@dimension", entry.VectorDimension);
                update.Parameters.AddWithValue("@vector", vectorBytes);
                update.Parameters.AddWithValue("@id", existing.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);

                existing.Content = entry.Content;
                existing.Category = entry.Category;
                existing.UpdatedAt = entry.UpdatedAt;
                existing.ExpiresAt = entry.ExpiresAt;
                existing.VectorDimension = entry.VectorDimension;
                existing.WorkspaceId = entry.WorkspaceId;
                existing.ContentHash = entry.ContentHash;
                return existing;
            }

            using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO LocalMemory (Id, Content, Category, CreatedAt, UpdatedAt, VectorDimension, Vector, WorkspaceId, ContentHash, ExpiresAt)
                VALUES (@id, @content, @category, @createdAt, @updatedAt, @dimension, @vector, @ws, @hash, @expiresAt)
                """;
            insert.Parameters.AddWithValue("@id", entry.Id);
            insert.Parameters.AddWithValue("@content", entry.Content);
            insert.Parameters.AddWithValue("@category", entry.Category);
            insert.Parameters.AddWithValue("@createdAt", entry.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("@updatedAt", entry.UpdatedAt.ToString("O"));
            insert.Parameters.AddWithValue("@dimension", entry.VectorDimension);
            insert.Parameters.AddWithValue("@vector", vectorBytes);
            insert.Parameters.AddWithValue("@ws", (object?)entry.WorkspaceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hash", entry.ContentHash);
            insert.Parameters.AddWithValue("@expiresAt", (object?)entry.ExpiresAt?.ToString("O") ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM LocalMemory WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM LocalMemory WHERE ExpiresAt IS NOT NULL AND ExpiresAt < @now";
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            return await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryEntry>> ListAsync(string? category = null, string? workspaceId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var results = new List<MemoryEntry>();
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect("SELECT Id, Content, Category, CreatedAt, UpdatedAt, VectorDimension, WorkspaceId, ContentHash, ExpiresAt FROM LocalMemory", category, workspaceId, includeAllWorkspaces: false, orderLimit: true);
        AddFilterParams(cmd, category, workspaceId);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadEntry(reader));
        return results;
    }

    public async Task<IReadOnlyList<(MemoryEntry Entry, byte[] VectorBytes)>> LoadAllAsync(string? category = null, string? workspaceId = null, bool includeAllWorkspaces = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var results = new List<(MemoryEntry, byte[])>();
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect("SELECT Id, Content, Category, CreatedAt, UpdatedAt, VectorDimension, WorkspaceId, ContentHash, ExpiresAt, Vector FROM LocalMemory", category, workspaceId, includeAllWorkspaces, orderLimit: false);
        AddFilterParams(cmd, category, workspaceId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entry = ReadEntry(reader);
            var vector = (byte[])reader["Vector"];
            results.Add((entry, vector));
        }
        return results;
    }

    public async Task<IReadOnlyList<(MemoryEntry Entry, byte[] VectorBytes)>> LoadByIdsAsync(IEnumerable<string> ids, string? workspaceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return Array.Empty<(MemoryEntry, byte[])>();

        var results = new List<(MemoryEntry, byte[])>();
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        var placeholders = string.Join(",", idList.Select((_, i) => $"@id{i}"));
        var where = BuildWhere(category: null, workspaceId, includeAllWorkspaces: false);
        cmd.CommandText = $"SELECT Id, Content, Category, CreatedAt, UpdatedAt, VectorDimension, WorkspaceId, ContentHash, ExpiresAt, Vector FROM LocalMemory WHERE Id IN ({placeholders}){where}";
        for (var i = 0; i < idList.Count; i++)
            cmd.Parameters.AddWithValue($"@id{i}", idList[i]);
        AddFilterParams(cmd, category: null, workspaceId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entry = ReadEntry(reader);
            var vector = (byte[])reader["Vector"];
            results.Add((entry, vector));
        }
        return results;
    }

    private static string BuildSelect(string columns, string? category, string? workspaceId, bool includeAllWorkspaces, bool orderLimit)
    {
        var where = BuildWhere(category, workspaceId, includeAllWorkspaces);
        var suffix = orderLimit ? " ORDER BY UpdatedAt DESC LIMIT @limit" : "";
        return $"{columns} WHERE 1=1{where}{suffix}";
    }

    private static string BuildWhere(string? category, string? workspaceId, bool includeAllWorkspaces)
    {
        var sb = new System.Text.StringBuilder();
        if (category != null)
            sb.Append(" AND Category = @category");
        if (!includeAllWorkspaces)
        {
            if (category == MemoryCategories.Global)
                sb.Append(" AND WorkspaceId IS NULL");
            else
                sb.Append(" AND (WorkspaceId IS @ws OR WorkspaceId IS NULL)");
        }
        sb.Append(" AND (ExpiresAt IS NULL OR ExpiresAt > @now)");
        return sb.ToString();
    }

    private static void AddFilterParams(SQLiteCommand cmd, string? category, string? workspaceId)
    {
        if (category != null)
            cmd.Parameters.AddWithValue("@category", category);
        if (category != MemoryCategories.Global)
            cmd.Parameters.AddWithValue("@ws", (object?)workspaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
    }

    private static MemoryEntry ReadEntry(IDataRecord reader)
    {
        var entry = new MemoryEntry
        {
            Id = reader.GetString(0),
            Content = reader.GetString(1),
            Category = reader.GetString(2),
            CreatedAt = DateTime.ParseExact(reader.GetString(3), "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.ParseExact(reader.GetString(4), "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            VectorDimension = reader.GetInt32(5)
        };
        entry.WorkspaceId = reader.IsDBNull(6) ? null : reader.GetString(6);
        entry.ContentHash = reader.IsDBNull(7) ? "" : reader.GetString(7);
        entry.ExpiresAt = reader.IsDBNull(8) ? null
            : DateTime.ParseExact(reader.GetString(8), "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        return entry;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        SQLiteConnection.ClearAllPools();
    }
}
