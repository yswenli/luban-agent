/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectDatabaseProbeUnitTest
*创建时间：2026/10/21
*描述：Step 4 探活参数与默认值单测——锁死 M4-P4 定案：
*      ①参数校验口径；②Describe 口令脱敏；③连接串解析「绝不回填口令」；
*      ④appsettings.json 容错（注释 / 尾随逗号 / 缺键）；⑤结论文件路径约定。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectDatabaseProbeUnitTest
{
    private readonly List<string> _tempDirectories = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响断言结果
            }
        }

        _tempDirectories.Clear();
    }

    /// <summary>建一个临时目录（自动回收）。</summary>
    private string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "luban-np-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }

    // ── 参数校验 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void TryValidate_参数齐全_通过()
    {
        var request = new DatabaseProbeRequest
        {
            Host = "10.10.0.214",
            Port = 3306,
            Database = "tjc_dev_jnms",
            User = "testuser",
            Password = "testuser"
        };

        Assert.IsTrue(request.TryValidate(out var error));
        Assert.AreEqual(string.Empty, error);
    }

    [TestMethod]
    public void TryValidate_允许空口令_便于本机免密实例()
    {
        var request = new DatabaseProbeRequest { Host = "localhost", Database = "d", User = "root" };

        Assert.IsTrue(request.TryValidate(out _), "空口令应放行（是否连得上由探活结果说话）。");
    }

    [TestMethod]
    [DataRow("", 3306, "d", "u", "服务器")]
    [DataRow("h", 0, "d", "u", "端口")]
    [DataRow("h", 70000, "d", "u", "端口")]
    [DataRow("h", 3306, "", "u", "库名")]
    [DataRow("h", 3306, "d", "", "账号")]
    public void TryValidate_缺项_给出可操作提示(string host, int port, string database, string user, string expected)
    {
        var request = new DatabaseProbeRequest
        {
            Host = host,
            Port = port,
            Database = database,
            User = user
        };

        Assert.IsFalse(request.TryValidate(out var error));
        StringAssert.Contains(error, expected);
    }

    [TestMethod]
    public void Describe_口令一律脱敏()
    {
        var request = new DatabaseProbeRequest
        {
            Host = "10.10.0.214",
            Port = 3306,
            Database = "tjc_dev_jnms",
            User = "testuser",
            Password = "S3cr3t-Pwd"
        };

        var text = request.Describe();

        StringAssert.Contains(text, "***");
        StringAssert.Contains(text, "testuser@10.10.0.214:3306/tjc_dev_jnms");
        Assert.IsFalse(text.Contains("S3cr3t-Pwd"), "操作记录里不得出现口令原文。");
    }

    // ── 连接串解析（严格不含口令）──────────────────────────────────────────

    [TestMethod]
    public void ParseMySqlConnectionString_模板连接串_拆出四项且口令丢弃()
    {
        const string ConnectionString =
            "server=10.10.0.214;user id=testuser; password=testuser; Port=3306;database=tjc_dev_csxmy; "
            + "pooling=true; max pool size=80;min pool size=10;connection timeout=15;sslmode=none;";

        var request = DatabaseProbeDefaults.ParseMySqlConnectionString(ConnectionString);

        Assert.IsNotNull(request);
        Assert.AreEqual("10.10.0.214", request!.Host);
        Assert.AreEqual(3306, request.Port);
        Assert.AreEqual("tjc_dev_csxmy", request.Database);
        Assert.AreEqual("testuser", request.User);
        Assert.AreEqual(string.Empty, request.Password, "口令绝不从文件回填。");
    }

    [TestMethod]
    [DataRow("pwd=secret")]
    [DataRow("password=secret")]
    [DataRow("Password = secret")]
    public void ParseMySqlConnectionString_任何口令键_都不回填(string passwordPart)
    {
        var request = DatabaseProbeDefaults.ParseMySqlConnectionString($"server=h;database=d;uid=u;{passwordPart};");

        Assert.IsNotNull(request);
        Assert.AreEqual(string.Empty, request!.Password);
    }

    [TestMethod]
    public void ParseMySqlConnectionString_键名大小写与别名_都能识别()
    {
        var request = DatabaseProbeDefaults.ParseMySqlConnectionString(
            "Data Source=db.local;Initial Catalog=tjc_dev_x;User Id=root;Port=3307;Password=x");

        Assert.IsNotNull(request);
        Assert.AreEqual("db.local", request!.Host);
        Assert.AreEqual("tjc_dev_x", request.Database);
        Assert.AreEqual("root", request.User);
        Assert.AreEqual(3307, request.Port);
    }

    [TestMethod]
    public void ParseMySqlConnectionString_缺端口_默认3306()
    {
        var request = DatabaseProbeDefaults.ParseMySqlConnectionString("server=h;database=d;user=u");

        Assert.IsNotNull(request);
        Assert.AreEqual(3306, request!.Port);
    }

    [TestMethod]
    public void ParseMySqlConnectionString_端口非法_退回3306()
    {
        var request = DatabaseProbeDefaults.ParseMySqlConnectionString("server=h;port=abc;database=d;user=u");

        Assert.IsNotNull(request);
        Assert.AreEqual(3306, request!.Port);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("database=d;user=u")]          // 没有服务器＝没法探活
    public void ParseMySqlConnectionString_无服务器_返回null(string? connectionString)
    {
        Assert.IsNull(DatabaseProbeDefaults.ParseMySqlConnectionString(connectionString));
    }

    // ── appsettings.json 容错读取 ──────────────────────────────────────────

    [TestMethod]
    public void ReadConnectionString_带注释与尾随逗号_仍能读出()
    {
        const string AppSettings = """
        {
          // 模板注释：库连接配置
          "DbConnectionOptions": {
            "EnableInitDb": false,
            "ConnectionConfigs": [
              {
                //"DbType": "Sqlite",
                "DbType": "MySql",
                "ConnectionString": "server=10.10.0.214;user id=testuser; password=testuser; Port=3306;database=tjc_dev_csxmy;",
              },
            ],
          },
        }
        """;

        var file = Path.Combine(NewTempDirectory(), "appsettings.json");
        File.WriteAllText(file, AppSettings);

        var connectionString = DatabaseProbeDefaults.ReadConnectionString(file);

        Assert.IsNotNull(connectionString);
        StringAssert.Contains(connectionString!, "tjc_dev_csxmy");
    }

    [TestMethod]
    public void ReadConnectionString_首条缺连接串_取到下一条()
    {
        const string AppSettings = """
        {
          "DbConnectionOptions": {
            "ConnectionConfigs": [
              { "DbType": "Sqlite" },
              { "DbType": "MySql", "ConnectionString": "server=h;database=d;user=u" }
            ]
          }
        }
        """;

        var file = Path.Combine(NewTempDirectory(), "appsettings.json");
        File.WriteAllText(file, AppSettings);

        Assert.AreEqual("server=h;database=d;user=u", DatabaseProbeDefaults.ReadConnectionString(file));
    }

    [TestMethod]
    [DataRow("{}")]                                                    // 没有 DbConnectionOptions
    [DataRow("{ \"DbConnectionOptions\": {} }")]                       // 没有 ConnectionConfigs
    [DataRow("{ \"DbConnectionOptions\": { \"ConnectionConfigs\": [] } }")]
    [DataRow("这不是 JSON")]
    public void ReadConnectionString_读不到_返回null不抛(string content)
    {
        var file = Path.Combine(NewTempDirectory(), "appsettings.json");
        File.WriteAllText(file, content);

        Assert.IsNull(DatabaseProbeDefaults.ReadConnectionString(file));
    }

    [TestMethod]
    public void ReadConnectionString_文件不存在_返回null()
    {
        Assert.IsNull(DatabaseProbeDefaults.ReadConnectionString(Path.Combine(NewTempDirectory(), "missing.json")));
        Assert.IsNull(DatabaseProbeDefaults.ReadConnectionString(null));
    }

    // ── 默认值整体装配 ────────────────────────────────────────────────────

    [TestMethod]
    public void FromBackendRepository_WebHost布局_给出四项默认值()
    {
        var root = NewTempDirectory();
        var webHost = Path.Combine(root, "WebHost");
        Directory.CreateDirectory(webHost);
        File.WriteAllText(
            Path.Combine(webHost, "appsettings.json"),
            """
            {
              "DbConnectionOptions": {
                "ConnectionConfigs": [
                  { "DbType": "MySql", "ConnectionString": "server=10.10.0.214;user id=testuser; password=testuser; Port=3306;database=tjc_dev_csxmy;" }
                ]
              }
            }
            """);

        var request = DatabaseProbeDefaults.FromBackendRepository(root);

        Assert.AreEqual("10.10.0.214", request.Host);
        Assert.AreEqual(3306, request.Port);
        Assert.AreEqual("tjc_dev_csxmy", request.Database);
        Assert.AreEqual("testuser", request.User);
        Assert.AreEqual(string.Empty, request.Password);
    }

    [TestMethod]
    public void FromBackendRepository_目录不存在_退化为空参数且端口3306()
    {
        var request = DatabaseProbeDefaults.FromBackendRepository(Path.Combine(Path.GetTempPath(), "luban-np-tests", "不存在的目录"));

        Assert.AreEqual(string.Empty, request.Host);
        Assert.AreEqual(3306, request.Port);
        Assert.AreEqual(string.Empty, request.Password);
    }

    [TestMethod]
    public void FindAppSettingsFile_优先WebHost再根目录()
    {
        var root = NewTempDirectory();
        Assert.IsNull(DatabaseProbeDefaults.FindAppSettingsFile(root), "两个候选都没有时返回 null。");

        var rootFile = Path.Combine(root, "appsettings.json");
        File.WriteAllText(rootFile, "{}");
        Assert.AreEqual(rootFile, DatabaseProbeDefaults.FindAppSettingsFile(root));

        var webHost = Path.Combine(root, "WebHost");
        Directory.CreateDirectory(webHost);
        var webHostFile = Path.Combine(webHost, "appsettings.json");
        File.WriteAllText(webHostFile, "{}");

        Assert.AreEqual(webHostFile, DatabaseProbeDefaults.FindAppSettingsFile(root), "WebHost/appsettings.json 优先。");
    }

    // ── 结论文件路径 ──────────────────────────────────────────────────────

    [TestMethod]
    public void BuildPath_落工作区内且不在任何git仓库里()
    {
        const string Root = @"D:\ws\jnms";

        var path = StepResultFile.BuildPath(Root, 4);

        StringAssert.StartsWith(path, Path.Combine(Root, StepResultFile.RelativeDirectory));
        StringAssert.StartsWith(Path.GetFileName(path), "step4-");
        StringAssert.EndsWith(path, ".json");

        var step5 = StepResultFile.BuildPath(Root, 5);
        StringAssert.StartsWith(Path.GetFileName(step5), "step5-");

        // 目录约定：{localPath}\.luban-agent\plugin-newproject\results
        Assert.AreEqual(
            Path.Combine(".luban-agent", "plugin-newproject", "results"),
            StepResultFile.RelativeDirectory);
    }

    [TestMethod]
    public void EnsureDirectory_建出父目录()
    {
        var root = NewTempDirectory();
        var file = StepResultFile.BuildPath(root, 4);

        StepResultFile.EnsureDirectory(file);

        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(file)));
    }
}