/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： YunxiaoWorkbenchService
*描述：云效工作台业务编排（可测实例类）：设置回显、只读探活、先校验后保存（D7）、项目列表
*
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;
using LubanAgentWebHost.Services.Init;
using System.Globalization;

namespace LubanAgentWebHost.Services;

/// <summary>
/// 工作台业务结果：壳层（<see cref="WebYunxiaoService"/>）据此映射响应壳。
/// </summary>
/// <typeparam name="T">成功载荷类型。</typeparam>
/// <param name="Ok">是否成功。</param>
/// <param name="Data">成功载荷；失败为 <c>default</c>。</param>
/// <param name="Code">失败语义码（400 格式非法 / 401 凭据 / 409 探活未过 / 429 限流 / 502 不可用；成功恒 200）。</param>
/// <param name="Message">失败原因（已脱敏，不含 PAT 明文）；成功为 <c>null</c>。</param>
/// <remarks>
/// <b>壳层映射 ApiEnvelope 的口径</b>：<see cref="Ok"/> 为 true → <c>WebApi.OkJson(Data)</c>
/// （<c>ApiEnvelope.Ok</c>，即 <c>{ success:true, code:200, message:null, data:… }</c>）；
/// 为 false → <c>WebApi.FailJson(Code, Message)</c>（<c>ApiEnvelope.Fail</c>，
/// 即 <c>{ success:false, code:Code, message:Message, data:null }</c>，HTTP 状态码同 <see cref="Code"/>）。
/// </remarks>
public sealed record YunxiaoWorkbenchResult<T>(bool Ok, T? Data, int Code, string? Message)
{
    /// <summary>成功结果（code 恒 200）。</summary>
    /// <param name="data">载荷。</param>
    /// <returns>结果。</returns>
    public static YunxiaoWorkbenchResult<T> Success(T data) => new(true, data, 200, null);

    /// <summary>失败结果（壳层映射 <c>WebApi.FailJson(code, message)</c>）。</summary>
    /// <param name="code">语义码。</param>
    /// <param name="message">脱敏原因。</param>
    /// <returns>结果。</returns>
    public static YunxiaoWorkbenchResult<T> Failure(int code, string message) => new(false, default, code, message);
}

/// <summary>
/// 云效工作台业务编排：设置回显、测试连接（只读探活）、保存（先校验后落盘）、项目列表。
/// </summary>
/// <remarks>
/// <para>
/// 与壳层 <see cref="WebYunxiaoService"/> 的分工：本类承载<b>全部时序与校验逻辑</b>且不碰
/// <c>WebApi</c> 静态运行时（可单测）；壳层只做守卫、payload 解析与
/// <see cref="YunxiaoWorkbenchResult{T}"/> → ApiEnvelope 的机械映射。
/// </para>
/// <para>
/// 脱敏红线：任何返回值、异常文案都不得含 PAT 明文；探活失败只回分类文案
/// （不回云效原始错误体）。
/// </para>
/// </remarks>
public sealed class YunxiaoWorkbenchService
{
    /// <summary>组织 ID 无效（空）。</summary>
    private const string OrgIdInvalid = "组织 ID 无效";

    /// <summary>PAT 格式非法（给了值但为空白串）。</summary>
    private const string PatInvalid = "个人访问令牌格式非法";

    /// <summary>组织地址解析不出组织 ID。</summary>
    private const string AddressInvalid = "组织地址无效";

    /// <summary>无可用令牌（输入缺省且未存）。</summary>
    private const string TokenMissing = "令牌无效或权限不足";

    /// <summary>探活 401/403 分类文案。</summary>
    private const string TokenRejected = "令牌无效或权限不足";

    /// <summary>探活 429 分类文案。</summary>
    private const string RateLimited = "云效接口限流";

    /// <summary>探活网络/5xx/未分类失败文案。</summary>
    private const string ServiceUnavailable = "云效服务暂不可用";

    /// <summary>保存前探活未通过。</summary>
    private const string ProbeRejected = "连接校验未通过，未保存";

    private readonly IYunxiaoSettingsService _settings;
    private readonly IProjexClient _projects;
    private readonly IYunxiaoCredentialProvider _credentials;
    private readonly ProjectInitStore? _initStore;

    /// <summary>
    /// 创建云效工作台业务服务。
    /// </summary>
    /// <param name="settings">云效连接配置读写门面。</param>
    /// <param name="projects">Projex 客户端（接口注入，单测可替换）。</param>
    /// <param name="credentials">云效凭据提供者（探活 pat 缺省时取已存 PAT）。</param>
    /// <param name="initStore">初始化完成登记（可空：单测不接线时项目列表恒为「待初始化」）。</param>
    public YunxiaoWorkbenchService(
        IYunxiaoSettingsService settings,
        IProjexClient projects,
        IYunxiaoCredentialProvider credentials,
        ProjectInitStore? initStore = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _initStore = initStore;
    }

    /// <summary>
    /// 读取连接设置：<c>{ organizationId, hasPat }</c>。
    /// </summary>
    /// <remarks>
    /// <b>绝不回 PAT 明文</b>；组织地址由前端按
    /// <c>https://devops.aliyun.com/orgs/{organizationId}</c> 回显，服务端不落地址。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设置回显。</returns>
    public Task<YunxiaoWorkbenchResult<YunxiaoSettingsDto>> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var data = new YunxiaoSettingsDto(_settings.GetOrganizationId() ?? string.Empty, _settings.HasPat());
        return Task.FromResult(YunxiaoWorkbenchResult<YunxiaoSettingsDto>.Success(data));
    }

    /// <summary>
    /// 测试连接（<b>只读探活</b>）：以本次输入的 organizationId + PAT 建一发
    /// <c>SearchProjects(page=1, perPage=1)</c>，顺带取 total 作 projectCount；<b>不写任何配置</b>。
    /// </summary>
    /// <remarks>
    /// pat 省略（<c>null</c>）时经 <see cref="IYunxiaoCredentialProvider.GetPatAsync(CancellationToken)"/>
    /// 取已存 PAT；输入与已存都没有 → 按凭据类失败（401）。
    /// 探活失败分类：401/403 →「令牌无效或权限不足」（401）、429 →「云效接口限流」、
    /// 网络/5xx/其它 →「云效服务暂不可用」（502）——文案固定，不回云效原始错误体。
    /// </remarks>
    /// <param name="organizationId">组织 ID（必填）。</param>
    /// <param name="pat">PAT；<c>null</c> 表示用已存 PAT。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>探活结论。</returns>
    public Task<YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>> TestConnectionAsync(
        string organizationId,
        string? pat,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return Task.FromResult(YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>.Failure(400, OrgIdInvalid));
        }

        if (pat is not null && string.IsNullOrWhiteSpace(pat))
        {
            return Task.FromResult(YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>.Failure(400, PatInvalid));
        }

        return ProbeAsync(organizationId, pat, cancellationToken);
    }

    /// <summary>
    /// 保存连接配置：<b>先只读探活，探活通过才落盘</b>（探活失败一次都不调 SaveAsync）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 时序（D7）：① 格式校验（organizationId 非空、pat 若非 null 则非空串）→ 400 语义；
    /// ② organizationAddress 非空时从中解析/校正 organizationId（末段覆盖，解析不出 → 400 语义）；
    /// ③ 先探活（与 <see cref="TestConnectionAsync"/> 同一口径，pat 省略用已存）→ 失败 409 语义
    /// 「连接校验未通过，未保存」且<b>零落盘</b>；
    /// ④ <see cref="IYunxiaoSettingsService.SaveAsync(string?, string?, CancellationToken)"/> 落盘
    /// （pat 缺省传 <c>null</c>=保持原值，禁止把 null 当清除）；⑤ 回 <c>{ saved: true }</c>。
    /// </para>
    /// <para>全程日志/结果不含 PAT 明文；组织地址只用于解析，不落库。</para>
    /// </remarks>
    /// <param name="organizationAddress">组织地址（可选，如 <c>https://devops.aliyun.com/orgs/{id}</c>）。</param>
    /// <param name="organizationId">组织 ID（必填，可被地址末段校正）。</param>
    /// <param name="pat">PAT；<c>null</c> 保持原值，空白串非法。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存结果。</returns>
    public async Task<YunxiaoWorkbenchResult<YunxiaoSaveSettingsDto>> SaveSettingsAsync(
        string? organizationAddress,
        string organizationId,
        string? pat,
        CancellationToken cancellationToken = default)
    {
        // ① 格式校验（400 语义）
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return YunxiaoWorkbenchResult<YunxiaoSaveSettingsDto>.Failure(400, OrgIdInvalid);
        }

        if (pat is not null && string.IsNullOrWhiteSpace(pat))
        {
            return YunxiaoWorkbenchResult<YunxiaoSaveSettingsDto>.Failure(400, PatInvalid);
        }

        // ② 组织地址解析/校正（400 语义；地址只用于解析，不落库）
        var orgId = organizationId.Trim();
        if (!string.IsNullOrWhiteSpace(organizationAddress))
        {
            if (!TryResolveOrganizationId(organizationAddress, out var parsed) || parsed is null)
            {
                return YunxiaoWorkbenchResult<YunxiaoSaveSettingsDto>.Failure(400, AddressInvalid);
            }

            orgId = parsed;
        }

        // ③ 先探活（与 TestConnectionAsync 同一口径）；失败 409 语义且一次都不调 SaveAsync
        var probe = await ProbeAsync(orgId, pat, cancellationToken).ConfigureAwait(false);
        if (!probe.Ok)
        {
            return YunxiaoWorkbenchResult<YunxiaoSaveSettingsDto>.Failure(409, ProbeRejected);
        }

        // ④ 落盘（pat=null 保持原值——与设置页留空语义一致，禁止把 null 当清除）
        await _settings.SaveAsync(orgId, pat, cancellationToken).ConfigureAwait(false);

        // ⑤ 成功
        return YunxiaoWorkbenchResult<YunxiaoSaveSettingsDto>.Success(new YunxiaoSaveSettingsDto(true));
    }

    /// <summary>
    /// 项目列表：<c>{ items, total, page, perPage }</c>，透传 Projex 搜索。
    /// </summary>
    /// <remarks>
    /// 默认 page=1、perPage=20；perPage 上限 100（越界收敛到 [1,100]，page 下限 1）。
    /// 鉴权走客户端已存凭据；调用失败直接抛出，由壳层兜底（不在本层做探活分类）。
    /// </remarks>
    /// <param name="page">页码（缺省 1）。</param>
    /// <param name="perPage">每页条数（缺省 20，上限 100）。</param>
    /// <param name="keyword">检索关键字（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页列表。</returns>
    public async Task<YunxiaoWorkbenchResult<YunxiaoProjectListDto>> ListProjectsAsync(
        int? page,
        int? perPage,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page ?? 1, 1);
        var safePerPage = Math.Clamp(perPage ?? 20, 1, 200);

        var result = await _projects.SearchProjectsAsync(
            _settings.GetOrganizationId() ?? string.Empty,
            safePage,
            safePerPage,
            keyword,
            cancellationToken).ConfigureAwait(false);

        // 初始化完成登记（项目 ID / 标识反查）：列表项标注「已初始化」
        var initRecords = _initStore is null
            ? (IReadOnlyList<ProjectInitRecord>)Array.Empty<ProjectInitRecord>()
            : await _initStore.GetAllAsync().ConfigureAwait(false);

        var items = (result?.Items ?? (IReadOnlyList<YunxiaoProject>)Array.Empty<YunxiaoProject>())
            .Select(p => ToProjectItem(p, IsInitialized(p, initRecords)))
            .ToList();

        var data = new YunxiaoProjectListDto(
            items,
            result?.Total ?? 0,
            safePage,
            safePerPage);

        return YunxiaoWorkbenchResult<YunxiaoProjectListDto>.Success(data);
    }

    /// <summary>
    /// 是否已完成初始化：登记按 项目 ID 优先匹配，ID 缺失时按项目标识匹配。
    /// </summary>
    /// <param name="project">Projex 项目。</param>
    /// <param name="records">初始化完成登记。</param>
    /// <returns>命中任一登记为 <c>true</c>。</returns>
    private static bool IsInitialized(YunxiaoProject project, IReadOnlyList<ProjectInitRecord> records)
    {
        if (records.Count == 0)
        {
            return false;
        }

        var id = project.Id;
        var identifier = project.DisplayIdentifier;

        return records.Any(r =>
            (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(r.ProjectId)
                && string.Equals(r.ProjectId, id, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(identifier) && !string.IsNullOrWhiteSpace(r.Identifier)
                && string.Equals(r.Identifier, identifier, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Projex 项目 → 页面扁平投影：标识取 <c>customCode</c>，
    /// 更新时间取 <c>gmtModified</c>（为空退 <c>gmtCreate</c>）并格式化为 <c>yyyy-MM-dd HH:mm:ss</c>。
    /// </summary>
    /// <param name="project">Projex 项目（真机字段形态）。</param>
    /// <param name="initialized">是否已完成初始化。</param>
    /// <returns>页面投影。</returns>
    private static YunxiaoProjectItemDto ToProjectItem(YunxiaoProject project, bool initialized)
    {
        var ms = project.DisplayUpdateTime;
        var updateTime = ms is null
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        return new YunxiaoProjectItemDto(
            project.Id,
            project.Name,
            project.DisplayIdentifier,
            project.Description,
            updateTime,
            initialized);
    }

    // ---------------- 内部：探活 ----------------

    /// <summary>
    /// 只读探活核心（调用方保证 organizationId / pat 已过格式校验）：
    /// 解析探活 PAT（输入优先，缺省取已存）→ 以该 PAT 建 <c>page=1, perPage=1</c> 一发搜索。
    /// </summary>
    private async Task<YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>> ProbeAsync(
        string organizationId,
        string? pat,
        CancellationToken cancellationToken)
    {
        // 探活 PAT：本次输入优先，缺省取已存（不写任何配置）
        var probePat = pat;
        if (probePat is null)
        {
            probePat = await _credentials.GetPatAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(probePat))
        {
            return YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>.Failure(401, TokenMissing);
        }

        try
        {
            // 只读探活一发：顺带拿 total 作 projectCount
            var result = await _projects.SearchProjectsWithPatAsync(
                organizationId.Trim(),
                page: 1,
                perPage: 1,
                keyword: null,
                probePat.Trim(),
                cancellationToken).ConfigureAwait(false);

            return YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>.Success(
                new YunxiaoTestConnectionDto(Ok: true, ProjectCount: result?.Total ?? 0));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ClassifyProbeFailure(ex);
        }
    }

    /// <summary>
    /// 探活失败分类（不回云效原始错误体，脱敏文案固定）：
    /// 401/403 → 401「令牌无效或权限不足」、429 → 429「云效接口限流」、网络/5xx/其它 → 502「云效服务暂不可用」。
    /// </summary>
    private static YunxiaoWorkbenchResult<YunxiaoTestConnectionDto> ClassifyProbeFailure(Exception ex)
    {
        if (ex is YunxiaoApiException api)
        {
            return api.ErrorCode switch
            {
                401 or 403 => YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>.Failure(401, TokenRejected),
                429 => YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>.Failure(429, RateLimited),
                _ => YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>.Failure(502, ServiceUnavailable)
            };
        }

        return YunxiaoWorkbenchResult<YunxiaoTestConnectionDto>.Failure(502, ServiceUnavailable);
    }

    /// <summary>
    /// 从组织地址解析组织 ID：取 <c>https://devops.aliyun.com/orgs/{id}</c> 形态地址的<b>末段</b>；
    /// 非 http/https 绝对地址、或路径末段为空 → 解析不出（false）。
    /// </summary>
    /// <param name="organizationAddress">组织地址（调用方保证非空白）。</param>
    /// <param name="organizationId">解析出的组织 ID。</param>
    /// <returns>是否解析成功。</returns>
    private static bool TryResolveOrganizationId(string? organizationAddress, out string? organizationId)
    {
        organizationId = null;
        var address = organizationAddress?.Trim();
        if (string.IsNullOrEmpty(address))
        {
            return false;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var id = Uri.UnescapeDataString(segments[^1]).Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        organizationId = id;
        return true;
    }
}
