/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： NamingDeriver
*描述：云效 S2 项目初始化命名派生纯函数（设计 §4.1 / S2b §11.1 修订）——
*      模板代号解析（首个 - 前段）/ 代号前缀替换改名 / 流水线分组前缀解析 /
*      三段派生（仓库名前缀替换 + 子目录按需派生 + 流水线名只替换代号段）
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.1 + §11.1
*
*****************************************************************************/

using System.Text.RegularExpressions;

namespace LubanAgentWebHost.Services.Init;

/// <summary>
/// 项目初始化命名派生（纯函数）：由母版代号 + 新代号 + 母版流水线名派生
/// 新库名 / 新子目录名 / 新流水线名。
/// </summary>
/// <remarks>
/// <para>
/// 命名口径（设计 §4.1，S2b §11.1 修订）：
/// 模板名代号 = <b>首个 <c>-</c> 之前的段</b>（<c>TPLX-Mixed</c>/<c>TPLX-Service</c> 均得 <c>TPLX</c>）；
/// 新名 = 代号前缀替换（<c>TPLX-Mixed → NEWX-Mixed</c>、<c>TPLX-Service → NEWX-Service</c>，
/// 见 <see cref="RenameByCode"/>）；仓内子目录 <c>{code}-Admin</c> / <c>{code}-Mobile</c> /
/// <c>{code}-Service</c> 仅综合库（模板名以 <c>-Mixed</c> 结尾）派生；
/// 流水线 <c>{prefix}_DEV_Deploy_{code}-{end}</c>（prefix 与 <c>_DEV_Deploy_</c> 段取自母版流水线名，
/// <b>只替换代号段</b>，前缀与端后缀原样保留）。
/// </para>
/// <para>
/// 本类不做 IO、不改写入参；派生结果为<b>默认值</b>，调用方可逐段改写（改后以传回值为准）。
/// </para>
/// </remarks>
public static class NamingDeriver
{
    /// <summary>综合库模板名后缀：以该后缀结尾才派生三子目录改名候选。</summary>
    private const string MixedSuffix = "-Mixed";

    /// <summary>流水线名分段标记：分组前缀在其前，代号段 + 端后缀在其后。</summary>
    private const string PipelineMarker = "_DEV_Deploy_";

    /// <summary>代号合法形态：字母开头、仅字母数字（不含量下划线与连字符）。</summary>
    private static readonly Regex CodePattern = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled);

    /// <summary>
    /// 模板名 → 模板代号（首个 <c>-</c> 之前的段）：<c>"TPLX-Mixed" → "TPLX"</c>、<c>"TPLX-Service" → "TPLX"</c>。
    /// </summary>
    /// <param name="templateRepoName">模板名（含单端与综合模板）。</param>
    /// <returns>
    /// 首个 <c>-</c> 之前的段；无 <c>-</c>、段为空或输入空白时为 <c>null</c>（前端要求手填）。
    /// </returns>
    public static string? ParseTemplateCode(string templateRepoName)
    {
        if (string.IsNullOrWhiteSpace(templateRepoName))
            return null;

        var index = templateRepoName.IndexOf('-');
        if (index < 0)
            return null;

        var code = templateRepoName[..index];
        return code.Length == 0 ? null : code;
    }

    /// <summary>
    /// 代号前缀替换改名：<c>"TPLX-Mixed" + TPLX→NEWX = "NEWX-Mixed"</c>、
    /// <c>"TPLX-Service" + TPLX→NEWX = "NEWX-Service"</c>（原代号后的全部后缀保留）。
    /// </summary>
    /// <param name="name">模板名（或任意带代号前缀的名）。</param>
    /// <param name="templateCode">模板代号（非空、<c>^[A-Za-z][A-Za-z0-9]*$</c>）。</param>
    /// <param name="newCode">新代号（同上）。</param>
    /// <returns>前缀替换后的新名。</returns>
    /// <exception cref="ArgumentException">代号为空或不匹配合法形态，或 <paramref name="name"/> 不以 <c>{templateCode}-</c> 开头（含后缀为空）。</exception>
    public static string RenameByCode(string name, string templateCode, string newCode)
    {
        ValidateCode(templateCode, nameof(templateCode));
        ValidateCode(newCode, nameof(newCode));

        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("名称不能为空。", nameof(name));

        var prefix = templateCode + "-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length == prefix.Length)
            throw new ArgumentException($"名称 \"{name}\" 不以 \"{prefix}\" 开头，无法按代号前缀替换。", nameof(name));

        return newCode + name[templateCode.Length..];
    }

    /// <summary>
    /// 母版流水线名 → 分组前缀：<c>"ACME_DEV_Deploy_DEMO-Mobile" → "ACME"</c>。
    /// </summary>
    /// <param name="templatePipelineName">母版流水线名。</param>
    /// <returns>首个 <c>_DEV_Deploy_</c> 之前的部分；不含该段或输入空白时为 <c>null</c>。</returns>
    public static string? ParsePipelinePrefix(string templatePipelineName)
    {
        if (string.IsNullOrWhiteSpace(templatePipelineName))
            return null;

        var index = templatePipelineName.IndexOf(PipelineMarker, StringComparison.Ordinal);
        return index < 0 ? null : templatePipelineName[..index];
    }

    /// <summary>
    /// 三段派生（默认值）：仓库名 / 子目录改名对（按需）/ 流水线改名对。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 仓库名走 <see cref="RenameByCode"/> 口径（综合库形态 <c>{templateCode}-Mixed</c> →
    /// <c>{newCode}-Mixed</c>；单端模板的库名由调用方以真实模板名调
    /// <see cref="RenameByCode"/> 覆盖）。
    /// </para>
    /// <para>
    /// 流水线只替换代号段：代号段 = <c>_DEV_Deploy_</c> 之后到首个 <c>-</c> 之前
    /// （代号按合法性约定不含 <c>-</c>，端后缀 <c>-Mobile</c>/<c>-Admin</c>/<c>-Service</c> 以 <c>-</c> 起）；
    /// 母版流水线名解析不出分组前缀时整条跳过。
    /// </para>
    /// </remarks>
    /// <param name="templateCode">母版代号（非空、<c>^[A-Za-z][A-Za-z0-9]*$</c>）。</param>
    /// <param name="newCode">新代号（同上）。</param>
    /// <param name="templatePipelineNames">母版流水线名列表（可空列表）。</param>
    /// <param name="includeDirs">是否派生三子目录改名对（综合库形态传 <c>true</c>；单端模板无目录改名段传 <c>false</c>）。</param>
    /// <returns>派生命名方案。</returns>
    /// <exception cref="ArgumentException">代号为空或不匹配合法形态。</exception>
    public static NamingPlan Derive(
        string templateCode,
        string newCode,
        IReadOnlyList<string> templatePipelineNames,
        bool includeDirs = true)
    {
        ValidateCode(templateCode, nameof(templateCode));
        ValidateCode(newCode, nameof(newCode));
        ArgumentNullException.ThrowIfNull(templatePipelineNames);

        var dirs = includeDirs
            ? new List<NamePair>
            {
                new($"{templateCode}-Admin", $"{newCode}-Admin"),
                new($"{templateCode}-Mobile", $"{newCode}-Mobile"),
                new($"{templateCode}-Service", $"{newCode}-Service"),
            }
            : new List<NamePair>();

        var pipelines = new List<NamePair>();
        foreach (var name in templatePipelineNames)
        {
            var prefix = ParsePipelinePrefix(name);
            if (prefix is null)
                continue;

            var markerIndex = name!.IndexOf(PipelineMarker, StringComparison.Ordinal);
            var remainder = name[(markerIndex + PipelineMarker.Length)..];

            // 代号段不含 '-'，端后缀以 '-' 起：切首个 '-' 即得「代号段之后的后缀」。
            var dashIndex = remainder.IndexOf('-');
            var endSuffix = dashIndex >= 0 ? remainder[dashIndex..] : string.Empty;

            pipelines.Add(new NamePair(name, prefix + PipelineMarker + newCode + endSuffix));
        }

        return new NamingPlan(RenameByCode($"{templateCode}{MixedSuffix}", templateCode, newCode), dirs, pipelines);
    }

    private static void ValidateCode(string code, string paramName)
    {
        if (string.IsNullOrEmpty(code) || !CodePattern.IsMatch(code))
            throw new ArgumentException($"代号必须匹配 ^[A-Za-z][A-Za-z0-9]*$，实际为 \"{code}\"。", paramName);
    }
}

/// <summary>
/// 改名对：母版名（From）→ 新名（To）。
/// </summary>
/// <param name="From">母版侧原名。</param>
/// <param name="To">派生后新名。</param>
public record NamePair(string From, string To);

/// <summary>
/// 命名派生方案：仓库名 + 子目录改名对 + 流水线改名对（均为默认值，可逐段改写）。
/// </summary>
/// <param name="RepoName">新库名（代号前缀替换口径；综合库为 <c>{newCode}-Mixed</c>）。</param>
/// <param name="Dirs">子目录改名对（<c>-Admin</c>/<c>-Mobile</c>/<c>-Service</c> 三条；单端模板 <c>includeDirs:false</c> 时为空）。</param>
/// <param name="Pipelines">流水线改名对（按母版流水线名顺序，无分组前缀的条目已跳过）。</param>
public record NamingPlan(string RepoName, IReadOnlyList<NamePair> Dirs, IReadOnlyList<NamePair> Pipelines);
