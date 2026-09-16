/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Infrastructure
*文件名： WorkspaceIdMigrator
*版本号： V1.0.0.0
*唯一标识：工作区ID迁移器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/16
*描述：将历史随机 GUID 工作区ID 迁移为路径派生ID，并同步主库关联表与共享长期记忆库
*
*****************************************************************************/
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.LocalMemory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LubanAgentCore.Infrastructure;

/// <summary>
/// 工作区ID迁移器。
/// <para>
/// 历史版本工作区ID 为随机 GUID，且根路径未归一化（尾斜杠/大小写差异），
/// 导致同一物理目录在不同宿主数据库中登记为多个工作区，按 WorkspaceId 隔离的
/// 长期记忆因此互相不可见。本迁移器把已有数据统一改写到
/// <see cref="WorkspaceIdGenerator"/> 派生的稳定ID 上。
/// </para>
/// <para>
/// 幂等：主库通过 ai_schema_version 表打标记，仅在成功后写入；
/// 记忆库迁移天然幂等（旧ID 迁走后无残留）。
/// </para>
/// </summary>
public static class WorkspaceIdMigrator
{
    /// <summary>
    /// 幂等标记版本号。
    /// </summary>
    private const string MigrationVersion = "workspace-id-derived-v1";

    /// <summary>
    /// 执行标志位，0=未执行，1=已执行（异常时复位以便重试）。
    /// </summary>
    private static int _done;

    /// <summary>
    /// 需要同步重指 workspace_id 的业务表。
    /// </summary>
    private static readonly string[] WorkspaceScopedTables = ["ai_session", "rag_file", "rag_chunk"];

    /// <summary>
    /// 解析本地记忆库路径：优先使用配置项，留空则回退到用户数据目录默认路径。
    /// </summary>
    /// <param name="options">本地记忆配置选项，可为 null。</param>
    /// <returns>记忆库绝对路径。</returns>
    public static string ResolveMemoryDbPath(LocalMemoryOptions? options)
    {
        var dbPath = options?.DatabasePath;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dbPath = Path.Combine(appData, "LuBan", "AIAgent", "localmemory.db");
        }
        return Path.GetFullPath(dbPath);
    }

    /// <summary>
    /// 执行工作区ID 迁移（进程内只执行一次）。
    /// </summary>
    /// <param name="sp">服务提供者，用于解析记忆库配置并触发记忆库建表。</param>
    /// <returns>迁移过程提示信息。</returns>
    public static IReadOnlyList<string> Migrate(IServiceProvider sp)
    {
        var messages = new List<string>();
        ArgumentNullException.ThrowIfNull(sp);

        if (Interlocked.CompareExchange(ref _done, 1, 0) != 0) return messages;

        try
        {
            // 触发记忆库建表与 ContentHash 回填，保证后续重指/去重可用
            _ = sp.GetService<ILocalMemoryStore>();
            var options = sp.GetService<IOptions<LocalMemoryOptions>>();

            var dbPath = DatabaseInitializer.GetDatabasePath();
            if (!File.Exists(dbPath))
            {
                messages.Add("未找到工作区数据库，跳过工作区ID 迁移。");
                return messages;
            }

            var idMap = MigrateWorkspaceDatabase(dbPath, messages);

            var memoryDbPath = ResolveMemoryDbPath(options?.Value);
            MigrateMemoryDatabase(memoryDbPath, idMap, messages);

            return messages;
        }
        catch (Exception ex)
        {
            // 复位标志位，允许下次启动重试
            Interlocked.Exchange(ref _done, 0);
            Logger.Error("工作区ID 迁移失败", ex);
            messages.Add($"工作区ID 迁移失败: {ex.Message}");
            return messages;
        }
    }

    /// <summary>
    /// 迁移工作区主库：把随机 GUID 工作区ID 改为路径派生ID，合并同路径重复工作区，
    /// 并同步业务表的 workspace_id。
    /// </summary>
    /// <param name="dbPath">主库路径。</param>
    /// <param name="messages">提示信息收集器。</param>
    /// <returns>旧ID → 新ID 的映射（仅包含发生变化的项）。</returns>
    public static IReadOnlyDictionary<string, string> MigrateWorkspaceDatabase(string dbPath, List<string>? messages = null)
    {
        messages ??= [];
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);

        using var conn = OpenConnection(dbPath);

        if (IsMigrated(conn))
        {
            messages.Add("工作区ID 迁移已完成，跳过。");
            return idMap;
        }

        var rows = ReadWorkspaces(conn);
        if (rows.Count == 0)
        {
            MarkMigrated(conn, null);
            return idMap;
        }

        using var tran = conn.BeginTransaction();
        var merged = 0;

        foreach (var group in rows.Where(r => !string.IsNullOrWhiteSpace(r.RootPath))
                                  .GroupBy(r => WorkspaceIdGenerator.Normalize(r.RootPath)))
        {
            var normalized = group.Key;
            if (normalized.Length == 0) continue;

            var derived = WorkspaceIdGenerator.Compute(normalized);
            if (derived.Length == 0) continue;

            var list = group.ToList();
            // 若已有未删除行使用派生ID，则以该行为保留者，避免出现重复 workspace_id；
            // 已软删的历史行不可当选 winner，否则会把活行一并软删导致工作区凭空消失
            var winner = list.FirstOrDefault(r => !r.IsDelete && string.Equals(r.WorkspaceId, derived, StringComparison.Ordinal))
                         ?? list.Where(r => !r.IsDelete)
                                .OrderByDescending(r => r.LastActiveAt ?? r.CreateTime)
                                .FirstOrDefault()
                         ?? list.OrderByDescending(r => r.LastActiveAt ?? r.CreateTime).First();

            foreach (var row in list)
            {
                if (ReferenceEquals(row, winner))
                {
                    if (!string.Equals(row.WorkspaceId, derived, StringComparison.Ordinal))
                    {
                        RedirectChildren(conn, tran, row.WorkspaceId, derived, idMap, true);
                        UpdateWorkspace(conn, tran, row.Id, derived, normalized);
                    }
                    else
                    {
                        NormalizeRootPath(conn, tran, row.Id, normalized);
                    }
                    continue;
                }

                RedirectChildren(conn, tran, row.WorkspaceId, derived, idMap, row.WorkspaceId != derived);
                SoftDeleteWorkspace(conn, tran, row.Id);
                merged++;
            }
        }

        MarkMigrated(conn, tran);
        tran.Commit();

        if (idMap.Count > 0)
            messages.Add($"工作区ID 迁移完成：重写 {idMap.Count} 个工作区ID，合并 {merged} 个重复工作区。");
        else
            messages.Add("工作区ID 迁移完成：无需变更。");

        return idMap;
    }

    /// <summary>
    /// 迁移共享长期记忆库：把旧工作区ID 的记忆重指到派生ID，并对同工作区同内容哈希去重。
    /// </summary>
    /// <param name="dbPath">记忆库路径。</param>
    /// <param name="idMap">旧ID → 新ID 映射。</param>
    /// <param name="messages">提示信息收集器。</param>
    public static void MigrateMemoryDatabase(string dbPath, IReadOnlyDictionary<string, string> idMap, List<string>? messages = null)
    {
        messages ??= [];
        if (idMap.Count == 0) return;
        if (!File.Exists(dbPath))
        {
            messages.Add("未找到本地记忆库，跳过记忆迁移。");
            return;
        }

        using var conn = OpenConnection(dbPath);
        // 记忆库文件存在但尚无 LocalMemory 表（空库/旧格式）时直接跳过，避免 UPDATE 抛异常
        if (!TableExists(conn, null, "LocalMemory"))
        {
            messages.Add("本地记忆库无 LocalMemory 表，跳过记忆迁移。");
            return;
        }

        using var tran = conn.BeginTransaction();

        var updated = 0;
        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (oldId, newId) in idMap)
        {
            using var cmd = CreateCommand(conn, tran, "UPDATE LocalMemory SET WorkspaceId = @new WHERE WorkspaceId = @old");
            cmd.Parameters.AddWithValue("@new", newId);
            cmd.Parameters.AddWithValue("@old", oldId);
            updated += cmd.ExecuteNonQuery();
            touched.Add(newId);
        }

        var removed = DeduplicateMemory(conn, tran, touched);
        tran.Commit();

        if (updated > 0 || removed > 0)
            messages.Add($"本地记忆迁移完成：重指 {updated} 条，去重删除 {removed} 条。");
    }

    /// <summary>
    /// 打开 SQLite 连接并设置忙等待超时，避免与 ORM 连接争用导致立即失败。
    /// </summary>
    /// <param name="dbPath">数据库文件路径。</param>
    /// <returns>已打开的连接。</returns>
    private static SqliteConnection OpenConnection(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath};Pooling=false;");
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <summary>
    /// 创建已绑定当前事务的命令（Microsoft.Data.Sqlite 要求显式绑定活动事务）。
    /// </summary>
    private static SqliteCommand CreateCommand(SqliteConnection conn, SqliteTransaction? tran, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tran;
        cmd.CommandText = sql;
        return cmd;
    }

    /// <summary>
    /// 读取 ai_workspace 全部行（含已软删，用于合并同路径重复工作区）。
    /// </summary>
    private static List<WorkspaceRow> ReadWorkspaces(SqliteConnection conn)
    {
        var rows = new List<WorkspaceRow>();
        using var cmd = CreateCommand(conn, null,
            "SELECT Id, workspace_id, name, root_path, last_active_at, create_time, is_delete FROM ai_workspace");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new WorkspaceRow
            {
                Id = reader.GetInt64(0),
                WorkspaceId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                RootPath = reader.IsDBNull(3) ? "" : reader.GetString(3),
                LastActiveAt = reader.IsDBNull(4) ? null : ParseDateTime(reader.GetValue(4)),
                CreateTime = reader.IsDBNull(5) ? null : ParseDateTime(reader.GetValue(5)),
                IsDelete = !reader.IsDBNull(6) && Convert.ToInt64(reader.GetValue(6)) != 0
            });
        }
        return rows;
    }

    /// <summary>
    /// 解析数据库中可能以文本或数值形式存储的时间值。
    /// </summary>
    private static DateTime? ParseDateTime(object value)
    {
        try
        {
            return value switch
            {
                DateTime dt => dt,
                string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed) => parsed,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把业务表中旧的 workspace_id 重指为派生ID，并记录映射。
    /// </summary>
    /// <param name="conn">数据库连接。</param>
    /// <param name="tran">事务。</param>
    /// <param name="oldId">旧工作区ID。</param>
    /// <param name="newId">派生工作区ID。</param>
    /// <param name="idMap">映射收集器。</param>
    /// <param name="recordMap">是否记录映射（仅当新旧ID 不同）。</param>
    private static void RedirectChildren(SqliteConnection conn, SqliteTransaction tran, string oldId, string newId,
        Dictionary<string, string> idMap, bool recordMap)
    {
        if (string.IsNullOrWhiteSpace(oldId) || string.Equals(oldId, newId, StringComparison.Ordinal)) return;

        foreach (var table in WorkspaceScopedTables)
        {
            if (!TableExists(conn, tran, table)) continue;
            using var cmd = CreateCommand(conn, tran, $"UPDATE {table} SET workspace_id = @new WHERE workspace_id = @old");
            cmd.Parameters.AddWithValue("@new", newId);
            cmd.Parameters.AddWithValue("@old", oldId);
            cmd.ExecuteNonQuery();
        }

        if (recordMap) idMap[oldId] = newId;
    }

    /// <summary>
    /// 更新保留工作区的派生ID 与归一化根路径。
    /// </summary>
    private static void UpdateWorkspace(SqliteConnection conn, SqliteTransaction tran, long id, string workspaceId, string rootPath)
    {
        using var cmd = CreateCommand(conn, tran, "UPDATE ai_workspace SET workspace_id = @ws, root_path = @root WHERE Id = @id");
        cmd.Parameters.AddWithValue("@ws", workspaceId);
        cmd.Parameters.AddWithValue("@root", rootPath);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 仅归一化保留工作区的根路径（派生ID 已正确时）。
    /// </summary>
    private static void NormalizeRootPath(SqliteConnection conn, SqliteTransaction tran, long id, string rootPath)
    {
        using var cmd = CreateCommand(conn, tran, "UPDATE ai_workspace SET root_path = @root WHERE Id = @id AND root_path <> @root");
        cmd.Parameters.AddWithValue("@root", rootPath);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 软删除被合并的工作区。
    /// </summary>
    private static void SoftDeleteWorkspace(SqliteConnection conn, SqliteTransaction tran, long id)
    {
        using var cmd = CreateCommand(conn, tran, "UPDATE ai_workspace SET is_delete = 1 WHERE Id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 对指定工作区按 (WorkspaceId, ContentHash) 去重，保留 UpdatedAt 最新的一条。
    /// </summary>
    /// <param name="conn">数据库连接。</param>
    /// <param name="tran">事务。</param>
    /// <param name="workspaceIds">限定去重范围的工作区ID。</param>
    /// <returns>删除的条数。</returns>
    private static int DeduplicateMemory(SqliteConnection conn, SqliteTransaction tran, IReadOnlyCollection<string> workspaceIds)
    {
        if (workspaceIds.Count == 0) return 0;
        if (!TableExists(conn, tran, "LocalMemory")) return 0;

        var placeholders = string.Join(",", workspaceIds.Select((_, i) => $"@ws{i}"));
        using var cmd = CreateCommand(conn, tran, $"""
            DELETE FROM LocalMemory
            WHERE WorkspaceId IN ({placeholders})
              AND ContentHash IS NOT NULL AND ContentHash <> ''
              AND Id NOT IN (
                  SELECT Id FROM (
                      SELECT Id, ROW_NUMBER() OVER (PARTITION BY WorkspaceId, ContentHash ORDER BY UpdatedAt DESC) AS rn
                      FROM LocalMemory
                      WHERE WorkspaceId IN ({placeholders})
                        AND ContentHash IS NOT NULL AND ContentHash <> ''
                  ) WHERE rn = 1
              )
            """);
        var i = 0;
        foreach (var ws in workspaceIds)
            cmd.Parameters.AddWithValue($"@ws{i++}", ws);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 判断指定表是否存在。
    /// </summary>
    private static bool TableExists(SqliteConnection conn, SqliteTransaction? tran, string table)
    {
        using var cmd = CreateCommand(conn, tran, "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@t LIMIT 1");
        cmd.Parameters.AddWithValue("@t", table);
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>
    /// 判断主库是否已完成本版本迁移。
    /// </summary>
    private static bool IsMigrated(SqliteConnection conn)
    {
        EnsureVersionTable(conn, null);
        using var cmd = CreateCommand(conn, null, "SELECT 1 FROM ai_schema_version WHERE version = @v LIMIT 1");
        cmd.Parameters.AddWithValue("@v", MigrationVersion);
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>
    /// 写入迁移完成标记。
    /// </summary>
    private static void MarkMigrated(SqliteConnection conn, SqliteTransaction? tran)
    {
        EnsureVersionTable(conn, tran);
        using var cmd = CreateCommand(conn, tran, "INSERT OR REPLACE INTO ai_schema_version (version, applied_at) VALUES (@v, @t)");
        cmd.Parameters.AddWithValue("@v", MigrationVersion);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 确保幂等标记表存在。
    /// </summary>
    private static void EnsureVersionTable(SqliteConnection conn, SqliteTransaction? tran)
    {
        using var cmd = CreateCommand(conn, tran,
            "CREATE TABLE IF NOT EXISTS ai_schema_version (version TEXT PRIMARY KEY, applied_at TEXT NOT NULL)");
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// ai_workspace 行快照。
    /// </summary>
    private sealed class WorkspaceRow
    {
        public long Id { get; init; }
        public string WorkspaceId { get; init; } = "";
        public string Name { get; init; } = "";
        public string RootPath { get; init; } = "";
        public DateTime? LastActiveAt { get; init; }
        public DateTime? CreateTime { get; init; }
        public bool IsDelete { get; init; }
    }
}