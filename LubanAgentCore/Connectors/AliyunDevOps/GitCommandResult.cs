/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： GitCommandResult
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/19
*描述：本地 Git 命令执行结果 - 在成功标志之外保留退出码与失败摘要
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 本地 Git 命令执行结果（克隆 / 推送）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CodeupClient.CloneToLocalAsync"/> 与 <see cref="CodeupClient.PushAsync"/> 原先只返回 <c>bool</c>，
/// 把 git 的原始报错（如 <c>Remote branch develop not found in upstream origin</c>）丢在
/// <c>ProcessResult.StandardError</c> 里，调用方只能给出「猜出来」的失败文案。
/// 本类型把退出码与标准错误（<b>已脱敏 URL 内嵌凭据</b>）一路带到用户可见的失败说明。
/// </para>
/// <para>
/// 诊断信息可能含仓库地址；构造本类型时一律经 <c>SecretMasker.MaskUrlCredentials</c> 脱敏，禁止写入明文 PAT。
/// </para>
/// </remarks>
/// <param name="Success">命令是否成功（退出码 <c>0</c>）。</param>
/// <param name="ExitCode">退出码；进程无法启动或超时为 <c>-1</c>。</param>
/// <param name="StandardOutput">标准输出（换行已归一化，可能为空）。</param>
/// <param name="StandardError">标准错误（换行已归一化、凭据已脱敏，可能为空）。</param>
/// <param name="TimedOut">是否超时。</param>
public sealed record GitCommandResult(
    bool Success,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut)
{
    /// <summary>成功结果（供无外部进程的前置短路返回使用）。</summary>
    /// <param name="standardOutput">标准输出摘要。</param>
    /// <returns>成功结果。</returns>
    public static GitCommandResult Ok(string standardOutput = "")
        => new(true, 0, standardOutput, string.Empty, false);

    /// <summary>失败结果（不依赖外部进程时使用，如前置校验未通过）。</summary>
    /// <param name="message">失败原因（写入标准错误字段，供 <see cref="FailureSummary"/> 使用）。</param>
    /// <returns>失败结果。</returns>
    public static GitCommandResult Fail(string message)
        => new(false, -1, string.Empty, message, false);

    /// <summary>
    /// 失败原因单行摘要：优先取标准错误首行，其次标准输出首行，最后回退到退出码描述。
    /// </summary>
    /// <returns>单行摘要（可能为空字符串）。</returns>
    public string FailureSummary()
    {
        var line = FirstLine(StandardError);
        if (line.Length == 0)
            line = FirstLine(StandardOutput);
        if (line.Length == 0)
            line = TimedOut ? $"git 命令超时（exitCode={ExitCode}）" : $"git 命令失败（exitCode={ExitCode}）";

        return line;
    }

    /// <summary>取文本中第一段非空行（去首尾空白）。</summary>
    /// <param name="text">原始文本。</param>
    /// <returns>首段非空行；无内容时为空字符串。</returns>
    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        foreach (var raw in text.Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return string.Empty;
    }
}