# 文件类工具「目录当文件」路径问题 — 修复记录（2026-09-17）

## 现象

CLI 冒烟（AGENTS.md 生成后让 Agent 读文件）中：`ListDirectory` 明确列出 `AGENTS.md`，但 `ReadFile` 反复失败，模型自述「ReadFile 似乎有路径问题」，最后改用 `RunShell` 绕过。

## 证据

`LubanAgentCli\bin\Debug\net10.0\logs\error.txt`（17:10–17:14，14 次同错）：

```
文件读取异常：文件不存在
params: ["D:\WORKBENCH\WALLE\LUBAN\LUBAN-AGENT\LUBANAGENTCLI\BIN\DEBUG\NET10.0"]
FileNotFoundException: Could not find file 'D:\...\NET10.0'
  at LuBan.AIAgent.Tools.FileSystem.FileSystemToolGroup.ReadFileAsync(String path) FileSystemToolPlugin.cs:338
```

模型思考记录（用户提供）：先拿工作区根目录（绝对路径）当 `path`，又试相对路径 `"."`，两者都是目录；每次都被报「未找到文件」，无法自纠。另记录「GetWorkspaceOverview 也没显示 AGENTS.md，很可疑」。

## 根因

1. **`ReadFileAsync` 缺目录检测**：`new FileInfo(目录).Length` 抛 `FileNotFoundException` → 被 `:382-385` 捕获 → 返回误导性「未找到文件: …。请检查路径是否正确」→ 模型死循环。同文件 `GetFileInfoAsync`（`File.Exists`/`Directory.Exists` 双分支）早有正确约定，ReadFile 漏了。
2. **加重混淆**：`GetWorkspaceOverviewAsync` 的「关键文件」白名单 `KeyFileNames` 不含 `agents.md`；bin 目录文件极多，目录树每层只取 10 文件/20 子目录，AGENTS.md 被截断隐藏。
3. **排除**：`AIFunctionFactoryHelper` 的 `path` 兜底注入本次未参与（日志 `params` 是模型显式传入的绝对根路径，非空）。

## 修复（已落地）

| 文件 | 改动 |
|---|---|
| `Tools/FileSystem/FileSystemToolPlugin.cs` | 新增 `DirectoryPathMessage(path, requirement)`；`ReadFileAsync`/`WriteFileAsync`/`DeleteFileAsync`/`CopyFileAsync`/`MoveFileAsync` 增加目录检测并返回可操作文案；`KeyFileNames` 增加 `agents.md` |
| `MCP/BuiltIn/FileSystemMCPClient.cs` | `ReadFileAsync`/`WriteFileAsync` 同等目录检测（`list_directory` 措辞） |
| `LuBan.XTestProject/FileSystemToolPathUnitTest.cs`（新增） | 8 个用例：5 个插件目录用例 + 1 个真实文件正例 + 2 个 MCP 用例 |

统一文案：`路径是目录: {path}。<按工具>，请改为传入目录下的具体文件（可用 ListDirectory 查看目录内容）。`

验证：`FileSystemToolPath` 8/8 通过；`AgentApiError` 39/39 通过；`LuBan.AIAgent` 构建 0 警告 0 错误。

## 后续项（本次已全部处理）

1. ~~Copy/Move 的目标是目录未检测~~ → 已补目标目录检测（`复制目标是文件路径（含文件名），不能是目录` / `移动目标…`），新增 2 个用例。
2. ~~`FileSystemMCPClient.CallToolAsync` try/catch 未 await~~ → 已改为 `async` + 在 try 内 `await` 三个工具方法，异步异常改为返回 `Fail(ex.Message)`（新增回归用例：写不存在目录时不再穿透异常）。
3. ~~写/复制/移动测试未断言各自 requirement 文案~~ → 已补断言，防止文案互换回归。
4. ~~ClassInitialize 失败时 ClassCleanup 不执行~~ → Setup 失败先 `TryDeleteRoot()` 再 rethrow；实测无临时目录残留。
5. ~~ACL 受限目录仍报「未找到文件」~~ → `ReadFileAsync` 的 FileNotFound/DirectoryNotFound 分支追加「先用 ListDirectory 查看目录内容，再传入具体文件路径」指引。

验证：`FileSystemToolPath` 12/12、`AgentApiError` 39/39、构建 0 警告 0 错误；复核 Ready to merge。

### 已知未做（复核 Minor，范围外）

- `FileSystemMCPClient` 的 `cancellationToken` 接收但未下传；catch 会把取消也转成 `Fail`（若将来传 token 需先 rethrow 取消）。
- `FileSystemMCPClient` 缺文件分支、`GetFileInfoAsync` 未加 ListDirectory 指引（如需彻底消除误导循环可后续统一）。