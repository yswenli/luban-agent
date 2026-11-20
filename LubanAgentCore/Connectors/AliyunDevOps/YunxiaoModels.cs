/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： YunxiaoModels
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效 API 请求/响应模型
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

#region 通用响应

/// <summary>
/// 云效 API 通用响应包装（<c>{ errorCode, errorMessage, result, requestId }</c>）。
/// </summary>
/// <remarks>
/// <b>仅作参考模型保留</b>：真机取证（2026-10-22）表明 <c>/oapi/v1</c> 下的<b>成功响应并不套这层外壳</b>
/// （列表＝裸数组、详情＝裸对象、创建＝裸标量），故客户端<b>不再</b>用它拆包
/// ——解析统一走 <see cref="YunxiaoResponseParser"/>。失败时才出现类似的错误包裹，
/// 且 <c>errorCode</c> 是<b>字符串</b>（如 <c>InvalidPipeline.createOrUpdateError</c>），与本类型的 <c>int</c> 不一致。
/// </remarks>
public class YunxiaoApiResponse<T>
{
    /// <summary>
    /// 错误码（0 表示成功）
    /// </summary>
    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 响应数据
    /// </summary>
    [JsonPropertyName("result")]
    public T? Result { get; set; }

    /// <summary>
    /// 请求 ID
    /// </summary>
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
}

/// <summary>
/// 分页结果（codeup / projex 侧共用的<b>合成载体</b>：接口本身不返回这个外壳——
/// <c>CodeupClient.SearchRepositoriesAsync</c> 与 <c>ProjexClient.SearchProjectsAsync</c>
/// 都是「响应体＝裸数组 + <c>X-Total</c>/<c>X-Page</c>/<c>X-Per-Page</c> 响应头」，
/// 由客户端把两者合成此对象）。
/// </summary>
/// <remarks>
/// <b>不要</b>用于 <c>/flow</c> 侧：流水线列表接口返回裸数组，分页信息不在响应头里
/// （<c>FlowClient.ListPipelinesAsync</c> 直接返回 <c>List&lt;YunxiaoPipeline&gt;</c>）。
/// </remarks>
public class YunxiaoPagedResult<T>
{
    /// <summary>
    /// 数据列表
    /// </summary>
    [JsonPropertyName("result")]
    public List<T>? Items { get; set; }

    /// <summary>
    /// 总条数
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// 当前页码
    /// </summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>
    /// 每页条数
    /// </summary>
    [JsonPropertyName("perPage")]
    public int PerPage { get; set; }
}

/// <summary>
/// Flow 侧 Token 翻页容器（<c>/flow/organizations/{orgId}/pipelines</c>）。
/// </summary>
/// <remarks>
/// 云效 Flow 列表接口用 <c>maxResults</c> + <c>nextToken</c> Token 翻页（不是
/// Codeup/Projex 的 <c>page</c>/<c>perPage</c> 页码翻页）；响应体是对象
/// <c>{"pipelines":[...], "nextToken":"xxx", "totalCount":50}</c>（不是裸数组，
/// <see cref="YunxiaoResponseParser.DeserializeList{T}"/> 只认裸数组，需要单独容器）。
/// </remarks>
public class FlowPipelinePage<T>
{
    /// <summary>流水线数组（JSON 字段 <c>pipelines</c>）。</summary>
    [JsonPropertyName("pipelines")]
    public List<T>? Pipelines { get; set; }

    /// <summary>下一页 Token（<c>null</c> / 空串 = 最后一页）。</summary>
    [JsonPropertyName("nextToken")]
    public string? NextToken { get; set; }

    /// <summary>总数。</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
}

#endregion

#region 代码仓库（Codeup）

/// <summary>
/// 云效代码仓库
/// </summary>
public class YunxiaoRepository
{
    /// <summary>
    /// 仓库 ID（云效返回 JSON 数字，故必须用整型；
    /// 用 <see cref="long"/> 而非 <see cref="int"/> 以容纳云效未来增长）。
    /// </summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>
    /// 仓库名称
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 仓库路径（路径标识）
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// HTTP 克隆地址
    /// </summary>
    [JsonPropertyName("httpUrlToRepo")]
    public string? HttpUrlToRepo { get; set; }

    /// <summary>
    /// SSH 克隆地址
    /// </summary>
    [JsonPropertyName("sshUrlToRepo")]
    public string? SSHUrlToRepo { get; set; }

    /// <summary>
    /// 可见性：<c>private</c> / <c>internal</c>（组织内公开）/ <c>public</c>。
    /// </summary>
    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 默认分支
    /// </summary>
    [JsonPropertyName("defaultBranch")]
    public string? DefaultBranch { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    [JsonPropertyName("lastActivityAt")]
    public string? LastActivityAt { get; set; }
}

/// <summary>
/// 创建仓库请求
/// </summary>
public class YunxiaoCreateRepositoryRequest
{
    /// <summary>
    /// 仓库名称
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 仓库路径（路径标识）
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// 可见性：private（私有）/ internal（组织内公开，默认）/ public（公开）
    /// </summary>
    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    /// <summary>
    /// 可见性级别（数字）。
    /// </summary>
    /// <remarks>
    /// <b>云效「创建代码库」文档未定义该字段</b>（只定义 <see cref="Visibility"/>），
    /// 保留仅为向后兼容：默认 <c>null</c> 时不会进入请求体，请勿设置，否则请求体出现未定义参数名会导致 400。
    /// </remarks>
    [JsonPropertyName("visibilityLevel")]
    public int? VisibilityLevel { get; set; }

    /// <summary>
    /// 组织下的分组 / 命名空间 ID（不传则建在组织根下）。
    /// </summary>
    /// <remarks>云效返回 JSON 数字，故用整型；字符串会被判为参数类型错误。</remarks>
    [JsonPropertyName("namespaceId")]
    public long? NamespaceId { get; set; }

    /// <summary>
    /// <c>path</c> 含多级分组时是否自动创建父路径。
    /// </summary>
    /// <remarks>
    /// <b>该参数是查询串（query）而不是请求体字段</b>（云效文档「创建代码库」：
    /// <c>POST .../repositories?createParentPath=true</c>），故此处 <c>JsonIgnore</c>，
    /// 由 <see cref="CodeupClient.CreateRepositoryAsync"/> 拼进 URL；放进请求体既无效也可能被判未定义参数名。
    /// </remarks>
    [JsonIgnore]
    public bool? CreateParentPath { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 模板项目（从模板仓创建时使用）
    /// </summary>
    [JsonPropertyName("templateProject")]
    public YunxiaoTemplateProject? TemplateProject { get; set; }
}

/// <summary>
/// 模板项目参数（从模板仓创建仓库）
/// </summary>
public class YunxiaoTemplateProject
{
    /// <summary>
    /// 模板仓 ID（如 TJCES-Service/Admin/Mobile 对应的仓库 ID；云效返回 JSON 数字）。
    /// </summary>
    [JsonPropertyName("templateProjectId")]
    public long? TemplateProjectId { get; set; }

    /// <summary>
    /// 模板类型：1 自定义模板 / 2 系统预置模板
    /// </summary>
    [JsonPropertyName("templateType")]
    public int TemplateType { get; set; } = 1;

    /// <summary>
    /// 是否同步模板全部分支（建议 true，否则新仓可能无分支）
    /// </summary>
    [JsonPropertyName("syncAllBranches")]
    public bool SyncAllBranches { get; set; } = true;
}

/// <summary>
/// 更新仓库请求
/// </summary>
public class YunxiaoUpdateRepositoryRequest
{
    /// <summary>
    /// 仓库名称
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 可见性
    /// </summary>
    [JsonPropertyName("visibilityLevel")]
    public int? VisibilityLevel { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 默认分支
    /// </summary>
    [JsonPropertyName("defaultBranch")]
    public string? DefaultBranch { get; set; }
}

#endregion

#region 分支 / 模板库 / 代码组（Codeup）

/// <summary>
/// 云效代码分支（<c>GET .../repositories/{id}/branches</c>）。
/// </summary>
public class YunxiaoBranch
{
    /// <summary>
    /// 分支名（如 <c>develop</c> / <c>master</c>）。
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 是否为仓库默认分支。
    /// </summary>
    [JsonPropertyName("defaultBranch")]
    public bool DefaultBranch { get; set; }

    /// <summary>
    /// 是否受保护分支。
    /// </summary>
    [JsonPropertyName("protected")]
    public bool Protected { get; set; }

    /// <summary>
    /// 分支 Web 地址。
    /// </summary>
    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    /// <summary>
    /// 分支 HEAD 提交。
    /// </summary>
    [JsonPropertyName("commit")]
    public YunxiaoBranchCommit? Commit { get; set; }
}

/// <summary>
/// 分支 HEAD 提交（云效分支接口内嵌对象）。
/// </summary>
public class YunxiaoBranchCommit
{
    /// <summary>完整提交 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>短提交 ID。</summary>
    [JsonPropertyName("shortId")]
    public string? ShortId { get; set; }

    /// <summary>提交标题。</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>提交完整信息。</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>作者名。</summary>
    [JsonPropertyName("authorName")]
    public string? AuthorName { get; set; }

    /// <summary>作者时间（ISO8601 字符串）。</summary>
    [JsonPropertyName("authoredDate")]
    public string? AuthoredDate { get; set; }

    /// <summary>提交时间（ISO8601 字符串）。</summary>
    [JsonPropertyName("committedDate")]
    public string? CommittedDate { get; set; }
}

/// <summary>
/// 云效模板代码库（<c>GET .../repositories/templates</c>）。
/// </summary>
/// <remarks>
/// 这是云效的「模板库」正式概念：只有登记在模板库中的仓库才能作为
/// <see cref="YunxiaoTemplateProject.TemplateProjectId"/> 来源。比按名搜索普通仓库更可靠。
/// </remarks>
public class YunxiaoTemplateRepository
{
    /// <summary>模板库对应的仓库 ID（即创建新仓时的 <c>templateProjectId</c>）。</summary>
    [JsonPropertyName("projectId")]
    public long ProjectId { get; set; }

    /// <summary>仓库名（如 <c>TJCES-Admin</c>）。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>全路径（如 <c>tjc/TJCES/TJCES-Admin</c>）。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>图标地址。</summary>
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// 云效代码组 / 命名空间（<c>GET .../namespaces</c>、<c>POST .../groups</c>）。
/// </summary>
public class YunxiaoNamespace
{
    /// <summary>代码组 ID（即创建仓库时的 <c>namespaceId</c>）。</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>代码组名（如 <c>TJCES</c>）。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>本层级路径段（如 <c>TJCES</c>）。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>含父级的完整路径（如 <c>tjc/TJCES</c>）。</summary>
    [JsonPropertyName("pathWithNamespace")]
    public string? PathWithNamespace { get; set; }

    /// <summary>完整路径（部分接口返回，语义同 <see cref="PathWithNamespace"/>）。</summary>
    [JsonPropertyName("fullPath")]
    public string? FullPath { get; set; }

    /// <summary>含父级的展示名（如 <c>tjc / TJCES</c>）。</summary>
    [JsonPropertyName("nameWithNamespace")]
    public string? NameWithNamespace { get; set; }

    /// <summary>父级 ID；<c>null</c> 表示直接挂在组织根下。</summary>
    [JsonPropertyName("parentId")]
    public long? ParentId { get; set; }

    /// <summary>类型（<c>group</c> / <c>project</c>）。</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>可见性（<c>private</c> / <c>internal</c> / <c>public</c>）。</summary>
    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    /// <summary>Web 地址。</summary>
    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }
}

/// <summary>
/// 创建代码组请求（<c>POST .../groups</c>）。
/// </summary>
/// <remarks>
/// 云效文档「CreateGroup」：<c>name</c> 与 <c>path</c> 必填；<c>parentId</c> 为空即在组织路径下创建。
/// </remarks>
public class YunxiaoCreateNamespaceRequest
{
    /// <summary>代码组名（显示名，必填）。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>代码组路径段（必填，通常与 <see cref="Name"/> 一致）。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>父级代码组 ID；<c>null</c> 表示建在组织根下。</summary>
    [JsonPropertyName("parentId")]
    public long? ParentId { get; set; }

    /// <summary>可见性（<c>private</c> / <c>internal</c>）。</summary>
    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>图标地址。</summary>
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
}

#endregion

#region 流水线（Flow）

/// <summary>
/// 云效流水线
/// </summary>
/// <remarks>
/// <b>真机口径（2026-10-22 取证）</b>：<b>两个接口字段名并不一致</b>——
/// <c>GET /pipelines/{id}</c>（详情）的名称字段是 <c>name</c>、id 字段是 <c>id</c>；
/// <c>GET /pipelines</c>（列表）的名称字段是 <c>pipelineName</c>、id 字段是 <c>pipelineId</c>、另有 <c>createAccountId</c>。
/// 两处的 id <b>都是 JSON 数字</b>，故必须用整型（与 <see cref="YunxiaoRepository.Id"/> 同口径）：
/// 用 <c>string?</c> 反序列化数字会抛 <c>JsonException</c>（<c>JsonOpts</c> 虽带了
/// <c>NumberHandling.AllowReadingFromString</c>，但那只管「字符串 → 数字」，不管「数字 → 字符串」），
/// 这正是 2026-10-22 复审修掉的一个隐患。
/// 取用一律走 <see cref="DisplayName"/> / <see cref="DisplayId"/>，屏蔽两个接口的拼写差异。
/// <c>pipelineConfig</c> 里 tb 形态流水线带 <c>settings</c>（JSON 串，含 <c>globalParams</c> / <c>caches</c>）与
/// <c>sources</c>；<b>新版 YAML 形态也有 <c>settings</c></b>（真机核对：不含 <c>globalParams</c> / <c>caches</c>），
/// 其变量 / 缓存写在新版 YAML 的顶层 <c>variables</c> / <c>cache</c> 里，随 <c>flow</c> 原样返回。
/// </remarks>
public class YunxiaoPipeline
{
    /// <summary>
    /// 流水线 ID（<b>详情</b>接口字段；云效返回 JSON 数字，故用整型）。
    /// </summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>
    /// 流水线 ID（<b>列表</b>接口字段；云效返回 JSON 数字，故用整型）。
    /// </summary>
    [JsonPropertyName("pipelineId")]
    public long? PipelineId { get; set; }

    /// <summary>
    /// 流水线名称（<b>详情</b>接口字段）。
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 流水线名称（<b>列表</b>接口字段）。
    /// </summary>
    [JsonPropertyName("pipelineName")]
    public string? PipelineName { get; set; }

    /// <summary>
    /// 流水线配置
    /// </summary>
    [JsonPropertyName("pipelineConfig")]
    public YunxiaoPipelineConfig? PipelineConfig { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [JsonPropertyName("createTime")]
    public long? CreateTime { get; set; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    [JsonPropertyName("modifyTime")]
    public long? ModifyTime { get; set; }

    /// <summary>
    /// 状态
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// 流水线归属分组 id（图标展示用；孤儿清单定位参考）。
    /// </summary>
    [JsonPropertyName("groupId")]
    public long? GroupId { get; set; }

    /// <summary>
    /// 创建人账号 id（列表接口字段；真机有此值但<b>无运行状态</b>）。
    /// </summary>
    [JsonPropertyName("createAccountId")]
    public string? CreateAccountId { get; set; }

    /// <summary>
    /// 统一取用名称：优先<b>详情</b>字段 <see cref="Name"/>，退化<b>列表</b>字段 <see cref="PipelineName"/>。
    /// </summary>
    [JsonIgnore]
    public string? DisplayName => string.IsNullOrWhiteSpace(Name) ? PipelineName : Name;

    /// <summary>
    /// 统一取用 id：优先<b>详情</b>字段 <see cref="Id"/>，退化<b>列表</b>字段 <see cref="PipelineId"/>，
    /// 统一输出为<b>不变文化</b>的十进制字符串（消费点只认字符串，如档案 <c>PipelineId</c> / Web 链接）。
    /// </summary>
    [JsonIgnore]
    public string? DisplayId => (Id ?? PipelineId)?.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// 流水线配置
/// </summary>
/// <remarks>
/// <c>settings</c> 为 <b>JSON 字符串</b>（不是对象）：tb 形态流水线的变量与缓存在此，
/// 例如 <c>{"globalParams":[{"key":"APP_PORT","value":"7000",...}],"caches":[{"directory":"/root/.m2","disable":false}],"runnerCacheMode":"local",...}</c>。
/// <b>新版 YAML 形态同样有这段</b>（真机核对：字段存在但不含 <c>globalParams</c> / <c>caches</c>——
/// 变量 / 缓存已改由新版 YAML 顶层 <c>variables</c> / <c>cache</c> 承载，随 <c>flow</c> 返回），
/// 故「有/无 <c>settings</c>」<b>不能</b>用来判断流水线是新版还是 tb 形态（判 <c>type</c> / <c>flow</c> 结构）。
/// 云效 OpenAPI <b>没有</b>写 <c>settings</c> 的入口，故本字段只读核对。
/// </remarks>
public class YunxiaoPipelineConfig
{
    /// <summary>
    /// 流水线 YAML 内容（tb 形态为控制台私有结构；新版 YAML 形态为 <c>stages:</c> 顶层可提交内容）。
    /// </summary>
    [JsonPropertyName("flow")]
    public string? Flow { get; set; }

    /// <summary>
    /// 变量与缓存配置（JSON 串；<b>只读核对</b>，OpenAPI 无法写入）。
    /// </summary>
    /// <remarks>
    /// tb 形态的变量 / 缓存在这里（<c>globalParams</c> / <c>caches</c> / <c>runnerCacheMode</c>）；
    /// 新版 YAML 形态也<b>会有</b>本字段，但不含这些键（变量 / 缓存已由 YAML 顶层 <c>variables</c> / <c>cache</c> 承载）。
    /// </remarks>
    [JsonPropertyName("settings")]
    public string? Settings { get; set; }

    /// <summary>
    /// 流水线源数组（原样 JSON，只读核对；写入走 <c>flow</c> 里的 <c>sources:</c>）。
    /// </summary>
    [JsonPropertyName("sources")]
    public JsonElement? Sources { get; set; }

    /// <summary>
    /// 流水线配置 id（版本视图用）。
    /// </summary>
    [JsonPropertyName("pipelineConfigId")]
    public long? PipelineConfigId { get; set; }

    /// <summary>
    /// 流水线配置版本号。
    /// </summary>
    [JsonPropertyName("version")]
    public long? Version { get; set; }
}

/// <summary>
/// 创建流水线请求
/// </summary>
/// <remarks>
/// <b>真机口径</b>：请求体字段名是 <c>name</c>（<b>不是</b> <c>pipelineName</c>），
/// <c>content</c> 必须是顶层为 <c>stages:</c> 的<b>新版 YAML</b>；喂 tb 形态 <c>flow</c> 原文必报
/// <c>1209300 yaml校验失败…至少要包含一个stage</c>。成功时<b>响应体就是裸整数 id</b>（无 <c>result</c> 外壳），
/// 由 <see cref="YunxiaoResponseParser.ParseId"/> 归一化为字符串。
/// </remarks>
public class YunxiaoCreatePipelineRequest
{
    /// <summary>
    /// 流水线名称（JSON 字段名 <c>name</c>）
    /// </summary>
    [JsonPropertyName("name")]
    public string? PipelineName { get; set; }

    /// <summary>
    /// 流水线 YAML 内容（新版 YAML：顶层 <c>stages:</c>）
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>
/// 更新流水线请求
/// </summary>
/// <remarks>字段口径同 <see cref="YunxiaoCreatePipelineRequest"/>（<c>name</c> + <c>content</c>）。</remarks>
public class YunxiaoUpdatePipelineRequest
{
    /// <summary>
    /// 流水线名称（JSON 字段名 <c>name</c>；更新会一并覆盖名称，须回传现有名称以免被改名）
    /// </summary>
    [JsonPropertyName("name")]
    public string? PipelineName { get; set; }

    /// <summary>
    /// 流水线 YAML 内容（新版 YAML：顶层 <c>stages:</c>）
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>
/// 运行流水线请求
/// </summary>
public class YunxiaoRunPipelineRequest
{
    /// <summary>
    /// 分支或标签
    /// </summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    /// <summary>
    /// 环境变量
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Environment { get; set; }
}

/// <summary>
/// 流水线运行结果
/// </summary>
public class YunxiaoPipelineRun
{
    /// <summary>
    /// 运行 ID
    /// </summary>
    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    /// <summary>
    /// 流水线 ID
    /// </summary>
    [JsonPropertyName("pipelineId")]
    public string? PipelineId { get; set; }

    /// <summary>
    /// 状态
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// 触发者
    /// </summary>
    [JsonPropertyName("triggeredBy")]
    public string? TriggeredBy { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    [JsonPropertyName("endTime")]
    public string? EndTime { get; set; }
}

#endregion

#region 主机组 / 部署目标（Flow）

/// <summary>
/// 云效主机组（流水线部署阶段的目标机器组）
/// </summary>
/// <remarks>
/// <para>
/// <b>真机口径（2026-10-22 取证）</b>：<c>GET /flow/organizations/{orgId}/hostGroups</c> 成功响应是<b>裸数组</b>；
/// 元素字段为 <c>id</c>（<b>JSON 数字</b>，故用整型）、<c>uuid</c>（<b>部署阶段 <c>machineGroup</c> 要填的就是它</b>）、
/// <c>name</c> / <c>type</c> / <c>ecsType</c> / <c>hostNum</c>（机器台数，可能为 <c>0</c>）/
/// <c>aliyunRegion</c>（可能为 <c>null</c>）/ <c>serviceConnectionId</c>（ECS 服务连接，可能为 <c>null</c>）/ <c>description</c>。
/// </para>
/// <para>
/// <b>列表里 <c>hostInfos</c> 恒为 <c>null</c></b>——机器明细只能另调
/// <c>GET /flow/organizations/{orgId}/hostGroups/{数字id}</c>（<see cref="YunxiaoHostInfo"/>）。
/// 该详情接口的<b>路径键必须是数字 <c>id</c></b>：真机用 <c>uuid</c> 当路径键会 400
/// （<c>NumberFormatException</c>）；也不存在 <c>/hostGroups/{id}/hosts</c> 之类子资源（404）。
/// </para>
/// <para>
/// 列表接口的 <c>perPage</c> / 名称过滤等查询参数<b>真机不生效</b>（一律返回全量），故调用方不要指望服务端过滤。
/// </para>
/// </remarks>
public class YunxiaoHostGroup
{
    /// <summary>
    /// 主机组数字 ID（云效返回 JSON 数字，故用整型）；
    /// <b>详情接口的路径键就是它</b>（不是 <see cref="Uuid"/>）。
    /// </summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>
    /// 主机组 UUID（<b>部署阶段 YAML 里 <c>machineGroup</c> 要填的值</b>；真机为 16 位大小写混合串）。
    /// </summary>
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    /// <summary>
    /// 主机组名称（如「移动云DEV主机组」）。
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 主机组类型（真机观测为 <c>ECS</c>）。
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// ECS 类型（真机观测为 <c>ECS_ALIYUN</c>）。
    /// </summary>
    [JsonPropertyName("ecsType")]
    public string? EcsType { get; set; }

    /// <summary>
    /// 机器台数（真机观测有 <c>0</c> 值，即空组可用但无机器）。
    /// </summary>
    [JsonPropertyName("hostNum")]
    public int? HostNum { get; set; }

    /// <summary>
    /// 地域（如 <c>cn-shanghai</c>；真机观测可为 <c>null</c>）。
    /// </summary>
    [JsonPropertyName("aliyunRegion")]
    public string? AliyunRegion { get; set; }

    /// <summary>
    /// ECS 服务连接 ID（数字；真机观测可为 <c>null</c>）。
    /// </summary>
    [JsonPropertyName("serviceConnectionId")]
    public long? ServiceConnectionId { get; set; }

    /// <summary>
    /// 描述（真机观测为 <c>null</c>）。
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 机器明细（<b>仅详情接口返回</b>；列表接口恒为 <c>null</c>）。
    /// </summary>
    [JsonPropertyName("hostInfos")]
    public List<YunxiaoHostInfo>? HostInfos { get; set; }
}

/// <summary>
/// 主机组内的机器明细
/// </summary>
/// <remarks>
/// <b>真机口径（2026-10-22 取证）</b>：只在主机组<b>详情</b>接口
/// <c>GET /flow/organizations/{orgId}/hostGroups/{数字id}</c> 的 <c>hostInfos</c> 里出现；
/// 元素含 <c>instanceName</c>（实例名）/ <c>ip</c> / <c>status</c>（真机 <c>ok</c>）/
/// <c>machineSn</c> / <c>objectType</c>（<c>MachineInfo</c>）/ <c>aliyunRegionId</c>（如 <c>PUBLIC</c>）。
/// 用于创建流水线之前让用户确认「部署到底打哪台机器」。
/// </remarks>
public class YunxiaoHostInfo
{
    /// <summary>
    /// 实例名（真机如 <c>tjc-sh-100-dev-all-in-one</c>）。
    /// </summary>
    [JsonPropertyName("instanceName")]
    public string? InstanceName { get; set; }

    /// <summary>
    /// 内网 IP（真机如 <c>192.0.2.10</c>）。
    /// </summary>
    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    /// <summary>
    /// 机器状态（真机观测为 <c>ok</c>）。
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// 机器序列号（真机如 <c>s-a488bcdf54aa4745ae33</c>）。
    /// </summary>
    [JsonPropertyName("machineSn")]
    public string? MachineSn { get; set; }

    /// <summary>
    /// 对象类型（真机观测为 <c>MachineInfo</c>）。
    /// </summary>
    [JsonPropertyName("objectType")]
    public string? ObjectType { get; set; }

    /// <summary>
    /// 云效侧地域标识（真机观测为 <c>PUBLIC</c>）。
    /// </summary>
    [JsonPropertyName("aliyunRegionId")]
    public string? AliyunRegionId { get; set; }
}

#endregion

#region 研发项目（Projex）

/// <summary>
/// 云效研发项目（Projex 项目域）。
/// </summary>
/// <remarks>
/// <b>真机口径（2026-11-18 取证）</b>：<c>projects:search</c> 与 <c>GET .../projects/{id}</c>
/// 的元素字段一致——<c>id</c> 为 24 位十六进制<b>字符串</b>；项目标识字段是 <c>customCode</c>
/// （不是 <c>identifier</c>）；时间为 <c>gmtCreate</c> / <c>gmtModified</c>（<b>epoch 毫秒</b>，
/// <c>gmtModified</c> 可为 <c>null</c>）；<c>status</c> / <c>creator</c> / <c>modifier</c> 为内嵌对象。
/// </remarks>
public class YunxiaoProject
{
    /// <summary>
    /// 项目 ID（24 位十六进制字符串，真机取证确认非数字）。
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// 项目名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 项目描述。
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 项目标识（编号，真机字段名 <c>customCode</c>）。
    /// </summary>
    [JsonPropertyName("customCode")]
    public string? CustomCode { get; set; }

    /// <summary>
    /// 可见范围（真机观测为 <c>public</c>）。
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// 项目图标地址。
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// 逻辑状态（真机观测为 <c>NORMAL</c>）。
    /// </summary>
    [JsonPropertyName("logicalStatus")]
    public string? LogicalStatus { get; set; }

    /// <summary>
    /// 项目状态（内嵌对象：<c>name</c> 如「进行中」、<c>id</c> 为状态标识）。
    /// </summary>
    [JsonPropertyName("status")]
    public YunxiaoProjectStatus? Status { get; set; }

    /// <summary>
    /// 创建人（内嵌对象）。
    /// </summary>
    [JsonPropertyName("creator")]
    public YunxiaoProjectUser? Creator { get; set; }

    /// <summary>
    /// 修改人（内嵌对象；可为 <c>null</c>）。
    /// </summary>
    [JsonPropertyName("modifier")]
    public YunxiaoProjectUser? Modifier { get; set; }

    /// <summary>
    /// 创建时间（epoch 毫秒）。
    /// </summary>
    [JsonPropertyName("gmtCreate")]
    public long? GmtCreate { get; set; }

    /// <summary>
    /// 修改时间（epoch 毫秒；真机观测可为 <c>null</c>，取用走 <see cref="DisplayUpdateTime"/>）。
    /// </summary>
    [JsonPropertyName("gmtModified")]
    public long? GmtModified { get; set; }

    /// <summary>
    /// 统一取用项目标识：<see cref="CustomCode"/>。
    /// </summary>
    [JsonIgnore]
    public string? DisplayIdentifier => CustomCode;

    /// <summary>
    /// 统一取用更新时间（epoch 毫秒）：优先 <see cref="GmtModified"/>，为 <c>null</c> 时退化 <see cref="GmtCreate"/>。
    /// </summary>
    [JsonIgnore]
    public long? DisplayUpdateTime => GmtModified ?? GmtCreate;
}

/// <summary>
/// 研发项目状态（<c>status</c> 内嵌对象）。
/// </summary>
public class YunxiaoProjectStatus
{
    /// <summary>状态名（如「进行中」）。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>状态标识。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

/// <summary>
/// 研发项目用户（<c>creator</c> / <c>modifier</c> 内嵌对象）。
/// </summary>
public class YunxiaoProjectUser
{
    /// <summary>用户名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>用户 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

#endregion

#region Projex 迭代

/// <summary>
/// 云效迭代（Sprint）。
/// </summary>
public class YunxiaoSprint
{
    /// <summary>迭代 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>迭代名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>迭代描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>状态：<c>TODO</c> 未开始 / <c>DOING</c> 进行中 / <c>ARCHIVED</c> 已完成。</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>开始日期。</summary>
    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    /// <summary>结束日期。</summary>
    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }

    /// <summary>工时容量（小时）。</summary>
    [JsonPropertyName("capacityHours")]
    public int? CapacityHours { get; set; }

    /// <summary>是否锁定。</summary>
    [JsonPropertyName("locked")]
    public bool? Locked { get; set; }

    /// <summary>创建人。</summary>
    [JsonPropertyName("creator")]
    public YunxiaoUserRef? Creator { get; set; }

    /// <summary>修改人。</summary>
    [JsonPropertyName("modifier")]
    public YunxiaoUserRef? Modifier { get; set; }

    /// <summary>负责人列表。</summary>
    [JsonPropertyName("owners")]
    public List<YunxiaoUserRef>? Owners { get; set; }

    /// <summary>创建时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("gmtCreate")]
    public long? GmtCreate { get; set; }

    /// <summary>修改时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("gmtModified")]
    public long? GmtModified { get; set; }
}

/// <summary>
/// 云效用户引用（<c>{ id, name }</c>）。
/// </summary>
public class YunxiaoUserRef
{
    /// <summary>用户 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>用户名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

#endregion

#region Projex 工作项

/// <summary>
/// 云效工作项（列表 / 详情同形）。
/// </summary>
public class YunxiaoWorkitem
{
    /// <summary>工作项 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>编号（如 <c>DSDD-123</c>）。</summary>
    [JsonPropertyName("serialNumber")]
    public string? SerialNumber { get; set; }

    /// <summary>标题。</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>描述格式：<c>RICHTEXT</c> / <c>MARKDOWN</c>。</summary>
    [JsonPropertyName("formatType")]
    public string? FormatType { get; set; }

    /// <summary>分类：<c>Req</c> / <c>Bug</c> / <c>Task</c> 等。</summary>
    [JsonPropertyName("categoryId")]
    public string? CategoryId { get; set; }

    /// <summary>逻辑状态：<c>normal</c> / <c>archived</c>。</summary>
    [JsonPropertyName("logicalStatus")]
    public string? LogicalStatus { get; set; }

    /// <summary>父工作项 ID。</summary>
    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    /// <summary>状态。</summary>
    [JsonPropertyName("status")]
    public YunxiaoWorkitemStatus? Status { get; set; }

    /// <summary>所属迭代。</summary>
    [JsonPropertyName("sprint")]
    public YunxiaoWorkitemRef? Sprint { get; set; }

    /// <summary>所属项目。</summary>
    [JsonPropertyName("space")]
    public YunxiaoWorkitemRef? Space { get; set; }

    /// <summary>负责人。</summary>
    [JsonPropertyName("assignedTo")]
    public YunxiaoUserRef? AssignedTo { get; set; }

    /// <summary>创建人。</summary>
    [JsonPropertyName("creator")]
    public YunxiaoUserRef? Creator { get; set; }

    /// <summary>修改人。</summary>
    [JsonPropertyName("modifier")]
    public YunxiaoUserRef? Modifier { get; set; }

    /// <summary>工作项类型。</summary>
    [JsonPropertyName("workitemType")]
    public YunxiaoWorkitemRef? WorkitemType { get; set; }

    /// <summary>创建时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("gmtCreate")]
    public long? GmtCreate { get; set; }

    /// <summary>修改时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("gmtModified")]
    public long? GmtModified { get; set; }

    /// <summary>状态更新时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("updateStatusAt")]
    public long? UpdateStatusAt { get; set; }

    /// <summary>自定义字段值（详情接口返回；用于读取「验收标准」等字段）。</summary>
    [JsonPropertyName("customFieldValues")]
    public List<YunxiaoCustomFieldValue>? CustomFieldValues { get; set; }
}

/// <summary>
/// 工作项自定义字段值（<c>{ fieldId, fieldName, values[] }</c>）。
/// </summary>
public class YunxiaoCustomFieldValue
{
    /// <summary>字段 ID。</summary>
    [JsonPropertyName("fieldId")]
    public string? FieldId { get; set; }

    /// <summary>字段名称（如「验收标准」）。</summary>
    [JsonPropertyName("fieldName")]
    public string? FieldName { get; set; }

    /// <summary>字段格式（如 <c>RICHTEXT</c> / <c>LIST</c>）。</summary>
    [JsonPropertyName("fieldFormat")]
    public string? FieldFormat { get; set; }

    /// <summary>字段取值。</summary>
    [JsonPropertyName("values")]
    public List<YunxiaoCustomFieldValueItem>? Values { get; set; }
}

/// <summary>
/// 自定义字段取值项（<c>{ identifier, displayValue }</c>）。
/// </summary>
public class YunxiaoCustomFieldValueItem
{
    /// <summary>取值标识。</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; set; }

    /// <summary>展示值。</summary>
    [JsonPropertyName("displayValue")]
    public string? DisplayValue { get; set; }
}

/// <summary>
/// 工作项内嵌引用（<c>{ id, name }</c>，用于 <c>sprint</c> / <c>space</c> / <c>workitemType</c>）。
/// </summary>
public class YunxiaoWorkitemRef
{
    /// <summary>ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// 工作项状态。
/// </summary>
public class YunxiaoWorkitemStatus
{
    /// <summary>状态 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>状态名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>显示名称。</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>英文名称（如 <c>TODO</c>）。</summary>
    [JsonPropertyName("nameEn")]
    public string? NameEn { get; set; }
}

/// <summary>
/// 工作项类型。
/// </summary>
public class YunxiaoWorkitemType
{
    /// <summary>类型 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>类型名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>类型英文名称。</summary>
    [JsonPropertyName("nameEn")]
    public string? NameEn { get; set; }

    /// <summary>分类：<c>Req</c> / <c>Bug</c> / <c>Task</c>。</summary>
    [JsonPropertyName("categoryId")]
    public string? CategoryId { get; set; }

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>是否启用。</summary>
    [JsonPropertyName("enable")]
    public bool? Enable { get; set; }

    /// <summary>是否默认类型。</summary>
    [JsonPropertyName("defaultType")]
    public bool? DefaultType { get; set; }

    /// <summary>是否系统默认类型。</summary>
    [JsonPropertyName("systemDefault")]
    public bool? SystemDefault { get; set; }
}

/// <summary>
/// 工作项工作流（状态集合与列序的真源）。
/// </summary>
public class YunxiaoWorkitemWorkflow
{
    /// <summary>工作流 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>工作流名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>默认状态 ID。</summary>
    [JsonPropertyName("defaultStatusId")]
    public string? DefaultStatusId { get; set; }

    /// <summary>状态列表（顺序即看板列序）。</summary>
    [JsonPropertyName("statuses")]
    public List<YunxiaoWorkitemStatus>? Statuses { get; set; }
}

/// <summary>
/// 工作项评论。
/// </summary>
public class YunxiaoWorkitemComment
{
    /// <summary>评论 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>评论内容。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>内容格式：<c>RICHTEXT</c> / <c>MARKDOWN</c>。</summary>
    [JsonPropertyName("contentFormat")]
    public string? ContentFormat { get; set; }

    /// <summary>父评论 ID。</summary>
    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    /// <summary>是否置顶。</summary>
    [JsonPropertyName("top")]
    public bool? Top { get; set; }

    /// <summary>置顶时间（epoch 毫秒；接口返回数字；待真机确认类型后最终定稿）。</summary>
    [JsonPropertyName("topTime")]
    public long? TopTime { get; set; }

    /// <summary>创建时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("gmtCreate")]
    public long? GmtCreate { get; set; }

    /// <summary>修改时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("gmtModified")]
    public long? GmtModified { get; set; }

    /// <summary>评论人。</summary>
    [JsonPropertyName("user")]
    public YunxiaoUserRef? User { get; set; }
}

/// <summary>
/// 工作项附件。
/// </summary>
public class YunxiaoWorkitemAttachment
{
    /// <summary>附件记录 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>文件 ID（取文件详情用）。</summary>
    [JsonPropertyName("fileId")]
    public string? FileId { get; set; }

    /// <summary>文件名称。</summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    /// <summary>文件大小（字节）。</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }

    /// <summary>文件后缀。</summary>
    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    /// <summary>临时下载地址（有时效）。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>创建时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("gmtCreate")]
    public long? GmtCreate { get; set; }

    /// <summary>修改时间（epoch 毫秒；接口返回数字）。</summary>
    [JsonPropertyName("gmtModified")]
    public long? GmtModified { get; set; }

    /// <summary>上传人。</summary>
    [JsonPropertyName("creator")]
    public YunxiaoUserRef? Creator { get; set; }

    /// <summary>修改人。</summary>
    [JsonPropertyName("modifier")]
    public YunxiaoUserRef? Modifier { get; set; }
}

/// <summary>
/// 工作项文件（临时下载信息）。
/// </summary>
public class YunxiaoWorkitemFile
{
    /// <summary>文件 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>文件名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>文件大小（字节）。</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }

    /// <summary>文件后缀。</summary>
    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    /// <summary>临时下载地址（有时效）。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>
/// 工作项搜索条件（服务端过滤，对应 <c>workitems:search</c> 的 conditions）。
/// </summary>
public class YunxiaoWorkitemSearchCriteria
{
    /// <summary>项目 ID（<c>spaceId</c>）。</summary>
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>工作项分类：<c>Req</c> / <c>Bug</c> / <c>Task</c>。</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>迭代 ID（服务端过滤，为空不过滤）。</summary>
    public string? SprintId { get; init; }

    /// <summary>状态 ID，逗号分隔（为空不过滤）。</summary>
    public string? Status { get; init; }

    /// <summary>标题包含的关键字（为空不过滤）。</summary>
    public string? Subject { get; init; }

    /// <summary>负责人 userId，逗号分隔（为空不过滤）。</summary>
    public string? AssignedTo { get; init; }

    /// <summary>工作项类型 ID，逗号分隔（为空不过滤）。</summary>
    public string? WorkitemTypeId { get; init; }

    /// <summary>排序字段（默认 <c>gmtCreate</c>）。</summary>
    public string OrderBy { get; init; } = "gmtCreate";

    /// <summary>排序方向：<c>desc</c> / <c>asc</c>。</summary>
    public string Sort { get; init; } = "desc";

    /// <summary>页码（从 1 开始）。</summary>
    public int Page { get; init; } = 1;

    /// <summary>每页条数（1–200）。</summary>
    public int PerPage { get; init; } = 20;
}

/// <summary>
/// 创建工作项请求。
/// </summary>
public class YunxiaoCreateWorkitemRequest
{
    /// <summary>项目 ID（<c>spaceId</c>，必填）。</summary>
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>标题（必填）。</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>工作项类型 ID（必填，取自工作项类型列表）。</summary>
    public string WorkitemTypeId { get; init; } = string.Empty;

    /// <summary>负责人 userId（必填）。</summary>
    public string AssignedTo { get; init; } = string.Empty;

    /// <summary>所属迭代 ID（可选）。</summary>
    public string? SprintId { get; init; }

    /// <summary>描述（可选）。</summary>
    public string? Description { get; init; }

    /// <summary>描述格式：<c>RICHTEXT</c> / <c>MARKDOWN</c>（可选）。</summary>
    public string? FormatType { get; init; }

    /// <summary>父工作项 ID（可选）。</summary>
    public string? ParentId { get; init; }

    /// <summary>标签 ID 列表（可选）。</summary>
    public IReadOnlyList<string>? Labels { get; init; }

    /// <summary>自定义字段值（可选，键为字段 ID）。</summary>
    public IReadOnlyDictionary<string, object?>? CustomFieldValues { get; init; }
}

/// <summary>
/// 云效「创建即返回 id」类接口的响应体（<c>{ "id": "..." }</c>）。
/// </summary>
internal sealed class YunxiaoIdResult
{
    /// <summary>新建对象 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

#endregion

#region 平台用户

/// <summary>
/// 云效当前用户（<c>GET /oapi/v1/platform/user</c>）。
/// </summary>
public class YunxiaoCurrentUser
{
    /// <summary>用户 ID（工作项 <c>assignedTo</c> 用该值）。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>登录账号。</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>姓名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>昵称。</summary>
    [JsonPropertyName("nickName")]
    public string? NickName { get; set; }

    /// <summary>邮箱。</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

#endregion

#region 测试（Testhub）

/// <summary>
/// 云效测试用例库（<c>GET .../testhub/organizations/{orgId}/testRepo/list</c>，响应为裸数组）。
/// </summary>
public class YunxiaoTestRepo
{
    /// <summary>用例库标识（创建用例时的 <c>testRepoId</c>）。</summary>
    [JsonPropertyName("testRepoIdentifier")]
    public string? TestRepoIdentifier { get; set; }

    /// <summary>用例库名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>作用域。</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>创建时间（epoch 毫秒；接口返回数字，声明为字符串会导致反序列化失败）。</summary>
    [JsonPropertyName("gmtCreate")]
    public long? GmtCreate { get; set; }
}

/// <summary>
/// 创建测试用例请求。
/// </summary>
/// <remarks>
/// 目标接口 <c>POST .../testRepos/{testRepoId}/testcases</c>；<see cref="TestRepoId"/> 只用于拼 URL，不进请求体。
/// </remarks>
public class YunxiaoCreateTestcaseRequest
{
    /// <summary>用例库 ID（必填，取自用例库列表的 <c>testRepoIdentifier</c>）。</summary>
    public string TestRepoId { get; init; } = string.Empty;

    /// <summary>用例标题（必填）。</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>负责人 userId（可选）。</summary>
    public string? AssignedTo { get; init; }

    /// <summary>目录 ID（可选）。</summary>
    public string? DirectoryId { get; init; }

    /// <summary>前置条件（可选）。</summary>
    public string? PreCondition { get; init; }

    /// <summary>标签（可选）。</summary>
    public IReadOnlyList<string>? Labels { get; init; }

    /// <summary>自定义字段值（可选）。</summary>
    public IReadOnlyDictionary<string, object?>? CustomFieldValues { get; init; }

    /// <summary>测试步骤（可选，按云效原始结构透传）。</summary>
    public IReadOnlyList<object>? TestSteps { get; init; }
}

/// <summary>
/// 云效测试用例（创建响应最小集）。
/// </summary>
public class YunxiaoTestcase
{
    /// <summary>用例 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>用例标题。</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }
}

/// <summary>
/// 创建测试计划请求。
/// </summary>
/// <remarks>
/// <b>未真机验证</b>：官方 MCP 包未提供「创建测试计划」工具，本请求按云效文档路径
/// <c>POST .../testhub/organizations/{orgId}/testPlans</c> 实现，仅传最小字段集；
/// 若云效调整字段名，调用会以错误回执暴露，不影响已创建的用例与工作项。
/// </remarks>
public class YunxiaoCreateTestPlanRequest
{
    /// <summary>计划名称（必填）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>关联项目 ID（<c>spaceIdentifier</c>，可选）。</summary>
    public string? ProjectId { get; init; }

    /// <summary>关联迭代 ID（<c>sprintIdentifier</c>，可选）。</summary>
    public string? SprintId { get; init; }

    /// <summary>开始日期（<c>yyyy-MM-dd</c>，可选）。</summary>
    public string? StartDate { get; init; }

    /// <summary>结束日期（<c>yyyy-MM-dd</c>，可选）。</summary>
    public string? EndDate { get; init; }

    /// <summary>负责人 userId 列表（可选）。</summary>
    public IReadOnlyList<string>? Managers { get; init; }

    /// <summary>纳入计划的用例 ID 列表（可选）。</summary>
    public IReadOnlyList<string>? CaseIds { get; init; }
}

/// <summary>
/// 云效测试计划（创建响应最小集）。
/// </summary>
public class YunxiaoTestPlan
{
    /// <summary>计划标识（部分响应返回该字段）。</summary>
    [JsonPropertyName("testPlanIdentifier")]
    public string? TestPlanIdentifier { get; set; }

    /// <summary>计划 ID（部分响应返回该字段）。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>计划名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

#endregion
