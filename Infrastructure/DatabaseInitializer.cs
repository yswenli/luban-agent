/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Infrastructure
*文件名： DatabaseInitializer
*版本号： V1.0.0.0
*唯一标识：数据库初始化器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：数据库初始化器
*
*****************************************************************************/

namespace LubanAgent.Infrastructure;

/// <summary>
/// 数据库初始化器
/// </summary>
public static class DatabaseInitializer
{
    private static int _initialized;

    /// <summary>
    /// 初始化数据库
    /// </summary>
    public static void Initialize()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0) return;
        FixRelativeConnectionString();
        MigrateLegacyDatabase();
        LuBanOrm.Init();
        EnsureIsCompactedColumn();
        EnsureWorkspaceIdColumns();
        var dbPath = GetDatabasePath();
    }

    /// <summary>
    /// 将 SQLite 连接字符串中的相对路径修正为基于程序所在目录的绝对路径，
    /// 确保从其他目录启动时数据库文件仍然位于程序目录下。
    /// </summary>
    private static void FixRelativeConnectionString()
    {
        var options = LuBanOrm.DbConnectionOptions;
        if (options?.ConnectionConfigs == null) return;
        var baseDir = AppContext.BaseDirectory;
        foreach (var config in options.ConnectionConfigs)
        {
            if (config.DbType != SqlSugar.DbType.Sqlite) continue;
            var connStr = config.ConnectionString;
            if (string.IsNullOrEmpty(connStr)) continue;

            // 解析 Data Source 值
            var parts = connStr.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var kv = parts[i].Split(['='], 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim();
                var val = kv[1].Trim();
                if (!key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("DataSource", StringComparison.OrdinalIgnoreCase)) continue;

                // 仅修正相对路径（以 ./ 或 .. 开头，或不含目录分隔符的纯文件名）
                if (val.StartsWith("./") || val.StartsWith(".\\") ||
                    val.StartsWith("../") || val.StartsWith("..\\") ||
                    (!Path.IsPathRooted(val) && !val.Contains(':')))
                {
                    var fileName = Path.GetFileName(val);
                    var absPath = Path.Combine(baseDir, fileName);
                    parts[i] = $"{key}={absPath}";
                }
            }
            config.ConnectionString = string.Join(";", parts);
        }
    }

    /// <summary>
    /// 兜底迁移：ai_session_message 新增 IsCompacted 列
    /// </summary>
    private static void EnsureIsCompactedColumn()
    {
        try
        {
            new Repositories.SessionMessageRepository().Context.Ado
                .ExecuteCommand("ALTER TABLE ai_session_message ADD COLUMN IsCompacted INTEGER NOT NULL DEFAULT 0");
        }
        catch
        {
            // 列已存在（CodeFirst 已迁移或重复执行），忽略
        }
    }

    /// <summary>
    /// 兜底迁移：ai_session、rag_file、rag_chunk 新增 WorkspaceId 列
    /// </summary>
    private static void EnsureWorkspaceIdColumns()
    {
        try
        {
            new Repositories.SessionRepository().Context.Ado
                .ExecuteCommand("ALTER TABLE ai_session ADD COLUMN WorkspaceId TEXT");
        }
        catch { /* 列已存在，忽略 */ }

        try
        {
            new Repositories.RagFileRepository().Context.Ado
                .ExecuteCommand("ALTER TABLE rag_file ADD COLUMN WorkspaceId TEXT NOT NULL DEFAULT ''");
        }
        catch { /* 列已存在，忽略 */ }

        try
        {
            new Repositories.RagChunkRepository().Context.Ado
                .ExecuteCommand("ALTER TABLE rag_chunk ADD COLUMN WorkspaceId TEXT NOT NULL DEFAULT ''");
        }
        catch { /* 列已存在，忽略 */ }
    }

    /// <summary>
    /// 获取数据库路径
    /// </summary>
    public static string GetDatabasePath()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "luban-ai-agent.db");
        return Path.GetFullPath(dbPath);
    }

    private static void MigrateLegacyDatabase()
    {
        var legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_sessions.db");
        var current = GetDatabasePath();
        if (File.Exists(legacy) && !File.Exists(current))
        {
            try
            {
                File.Move(legacy, current);
                Console.WriteLine($"数据库已从 {Path.GetFileName(legacy)} 更名为 {Path.GetFileName(current)}");
            }
            catch (Exception ex)
            {
                Logger.Error("数据库初始化异常", ex);
                Console.WriteLine($"数据库更名失败: {ex.Message}");
            }
        }
    }
}
