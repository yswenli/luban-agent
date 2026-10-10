/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： GitProcess
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：git 命令薄封装，统一参数拼接、编码与错误处理
*
*****************************************************************************/
using LuBan.AIAgent.Infrastructure;

namespace LubanAgentCore.Services;

/// <summary>
/// git 命令薄封装：基于 <see cref="ProcessRunner"/> 执行 git，统一 UTF-8 编码与失败抛错。
/// </summary>
public sealed class GitProcess(ProcessRunner runner)
{
    private readonly ProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>
    /// 在指定目录执行 git，返回原始结果（不因非零退出码抛错）。
    /// </summary>
    /// <param name="workingDir">工作目录。</param>
    /// <param name="args">git 子命令与参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进程执行结果。</returns>
    public Task<ProcessResult> RunRawAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct = default)
        => _runner.RunAsync(
            "git",
            arguments: "",
            workingDir: workingDir,
            stdin: null,
            timeoutMs: 120000,
            cancellationToken: ct,
            argumentList: args,
            outputEncoding: Encoding.UTF8);

    /// <summary>
    /// 在指定目录执行 git，非零退出码时抛 <see cref="InvalidOperationException"/>，返回标准输出。
    /// </summary>
    /// <param name="workingDir">工作目录。</param>
    /// <param name="args">git 子命令与参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>标准输出（未裁剪）。</returns>
    public async Task<string> RunAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var result = await RunRawAsync(workingDir, args, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} 失败（exit={result.ExitCode}）：{detail.Trim()}");
        }
        return result.StandardOutput;
    }
}