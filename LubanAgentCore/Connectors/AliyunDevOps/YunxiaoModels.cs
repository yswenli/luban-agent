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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

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
/// 分页结果（<b>仅 codeup 侧</b>使用：<c>CodeupClient.SearchRepositoriesAsync</c> 把裸数组与
/// <c>x-total</c> 响应头合成此对象）。
/// </summary>
/// <remarks>
/// <b>不要</b>用于 <c>/flow</c> 侧：流水线列表接口返回裸数组，分页信息不在响应体里
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
    /// 创建人账号 id（列表接口字段；真机有此值但<b>无运行状态</b>，见 PRD §4.11.3 注⑦）。
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