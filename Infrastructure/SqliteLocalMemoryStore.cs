/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*Author：yswenli
*命名空间：LubanAgent.Infrastructure
*文件名： SqliteLocalMemoryStore
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：基于 SQLite 的本地记忆存储实现
*
*****************************************************************************/
namespace LubanAgent.Infrastructure;

/// <summary>
/// 基于 SQLite 的本地记忆存储实现，支持记忆条目的增删改查、过期清理及向量存储
/// </summary>
public class SqliteLocalMemoryStore : ILocalMemoryStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// 创建 SqliteLocalMemoryStore 实例，自动创建数据库目录并初始化表结构
    /// </summary>
    /// <param name="dbPath">SQLite 数据库文件路径</param>
    public SqliteLocalMemoryStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connectionString = $"Data Source={dbPath};Pooling=false;Foreign Keys=false;";
        EnsureSchemaAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 创建新的数据库连接
    /// </summary>
    /// <returns>SQLite 连接实例</returns>
    private SqliteConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// 初始化表结构，补充缺失的列并回填 ContentHash
    /// </summary>
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

    /// <summary>
    /// 新增或更新一条记忆记录（按 WorkspaceId 与 ContentHash 判断是否已存在），并保存其向量数据
    /// </summary>
    /// <param name="entry">记忆条目</param>
    /// <param name="vectorBytes">向量字节数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已持久化的记忆条目（更新时返回合并后的条目）</returns>
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

    /// <summary>
    /// 按主键删除记忆记录
    /// </summary>
    /// <param name="id">记忆主键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除成功时返回 true，记录不存在时返回 false</returns>
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

    /// <summary>
    /// 删除所有已过期的记忆记录
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>被删除的记录数</returns>
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

    /// <summary>
    /// 按分类与工作空间查询未过期的记忆记录，按更新时间倒序返回
    /// </summary>
    /// <param name="category">分类，为空时不过滤</param>
    /// <param name="workspaceId">工作空间标识</param>
    /// <param name="limit">返回的最大条数，默认 100</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆条目列表</returns>
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

    /// <summary>
    /// 加载全部未过期的记忆记录及其向量数据
    /// </summary>
    /// <param name="category">分类，为空时不过滤</param>
    /// <param name="workspaceId">工作空间标识</param>
    /// <param name="includeAllWorkspaces">是否包含全部工作空间的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆条目与其向量数据的元组列表</returns>
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

    /// <summary>
    /// 按主键列表批量加载记忆记录及其向量数据
    /// </summary>
    /// <param name="ids">记忆主键集合</param>
    /// <param name="workspaceId">工作空间标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆条目与其向量数据的元组列表</returns>
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

    /// <summary>
    /// 构建 SELECT 查询语句，附加过滤条件与排序分页
    /// </summary>
    /// <param name="columns">查询的列</param>
    /// <param name="category">分类过滤条件</param>
    /// <param name="workspaceId">工作空间过滤条件</param>
    /// <param name="includeAllWorkspaces">是否包含全部工作空间的数据</param>
    /// <param name="orderLimit">是否追加按更新时间倒序并限制条数</param>
    /// <returns>构建完成的 SELECT 语句</returns>
    private static string BuildSelect(string columns, string? category, string? workspaceId, bool includeAllWorkspaces, bool orderLimit)
    {
        var where = BuildWhere(category, workspaceId, includeAllWorkspaces);
        var suffix = orderLimit ? " ORDER BY UpdatedAt DESC LIMIT @limit" : "";
        return $"{columns} WHERE 1=1{where}{suffix}";
    }

    /// <summary>
    /// 构建 WHERE 过滤条件（分类、工作空间、过期时间）
    /// </summary>
    /// <param name="category">分类过滤条件</param>
    /// <param name="workspaceId">工作空间过滤条件</param>
    /// <param name="includeAllWorkspaces">是否包含全部工作空间的数据</param>
    /// <returns>WHERE 条件子句</returns>
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

    /// <summary>
    /// 为过滤条件绑定 SQL 参数
    /// </summary>
    /// <param name="cmd">SQLite 命令</param>
    /// <param name="category">分类过滤条件</param>
    /// <param name="workspaceId">工作空间过滤条件</param>
    private static void AddFilterParams(SqliteCommand cmd, string? category, string? workspaceId)
    {
        if (category != null)
            cmd.Parameters.AddWithValue("@category", category);
        if (category != MemoryCategories.Global)
            cmd.Parameters.AddWithValue("@ws", (object?)workspaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
    }

    /// <summary>
    /// 从数据读取器中读取一条记忆记录
    /// </summary>
    /// <param name="reader">数据读取器</param>
    /// <returns>记忆条目</returns>
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

    /// <summary>
    /// 释放资源，释放信号量并清空 SQLite 连接池
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
