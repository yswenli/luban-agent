/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps.Mcp
*文件名： YunxiaoToolDefinitions
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效 MCP 工具定义（13 个，对应 PRD §4.11.3）
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps.Mcp;

/// <summary>
/// 云效 MCP 工具定义。
/// </summary>
/// <remarks>
/// 工具名使用 <b>base 名</b>（如 <c>codeup_search_repos</c>）。对 Agent 暴露时由
/// <c>MCPToolPlugin</c> 统一加 <c>mcp_{client}_{tool}</c> 前缀，最终呈现为 <c>mcp_yunxiao_codeup_search_repos</c>。
/// <b>切勿</b>在此处再写 <c>mcp_yunxiao_</c> 前缀，否则会出现二次前缀。
/// </remarks>
public static class YunxiaoToolDefinitions
{
    #region 代码仓库工具（Codeup）

    /// <summary>
    /// 查找代码仓库（ListRepositories）
    /// </summary>
    public static readonly MCPTool CodeupSearchRepos = new()
    {
        Name = "codeup_search_repos",
        Description = "查找云效代码仓库（模糊匹配仓库名称/路径）。注意：返回体不含 HTTPS 克隆地址，需再查仓库详情。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                search = new { type = "string", description = "搜索关键字（仓库名称或路径，可选）" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（默认 20）", @default = 20 },
                orderBy = new { type = "string", description = "排序字段（可选，如 name / created_at）" },
                sort = new { type = "string", description = "排序方向：asc / desc（可选）" },
                archived = new { type = "boolean", description = "是否包含已归档仓库（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            }
        }.ToJson()
    };

    /// <summary>
    /// 查询模板代码库列表（ListTemplateRepositories）
    /// </summary>
    public static readonly MCPTool CodeupListTemplateRepos = new()
    {
        Name = "codeup_list_template_repos",
        Description = "列出云效「模板库」条目（id / name / path / description）。只有登记在模板库中的仓库才能作为建库的 templateProjectId。注意：本接口**没有 search 入参、不支持按名过滤**，请全量拉取（perPage=100）后在客户端按 name 匹配；模板库 ≠ 普通仓库，勿用 codeup_search_repos 代替。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                templateType = new { type = "integer", description = "模板类型：1 自定义模板（组织登记，默认）/ 2 系统预置模板", @default = 1 },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（默认 100）", @default = 100 },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            }
        }.ToJson()
    };

    /// <summary>
    /// 查询仓库详情（GetRepository，返回 httpUrlToRepo）
    /// </summary>
    public static readonly MCPTool CodeupGetRepo = new()
    {
        Name = "codeup_get_repo",
        Description = "按仓库 ID 查询云效仓库详情，返回 httpUrlToRepo（HTTPS 克隆地址，git 克隆用）/ sshUrlToRepo / defaultBranch / id / name / path / visibility，并额外返回由克隆地址推导的 webUrl（页面地址）。注意：只接受 repositoryId（数字 id），没有「命名空间 + 路径」重载；新建仓库后若响应缺 httpUrlToRepo，也用本工具按建库返回的 id 补查。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                repositoryId = new { type = "string", description = "仓库 ID（数字 id，取自建库响应或 codeup_search_repos 命中项）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "repositoryId" }
        }.ToJson()
    };

    /// <summary>
    /// 创建新代码仓库（以指定模板仓为模板）
    /// </summary>
    public static readonly MCPTool CodeupCreateRepo = new()
    {
        Name = "codeup_create_repo",
        Description = "在云效创建新代码仓库（可从模板仓创建）。返回体不含 HTTPS 克隆地址，本工具已自动补查仓库详情返回 httpUrlToRepo。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "仓库名称（必填）" },
                path = new { type = "string", description = "仓库路径标识（必填，通常与 name 相同）" },
                templateProjectId = new { type = "integer", description = "模板仓 ID（整数，如 TJCES-Service/Admin/Mobile 对应的仓库 ID，可选）" },
                templateType = new { type = "integer", description = "模板类型：1 自定义模板 / 2 系统预置模板（默认 1）", @default = 1 },
                syncAllBranches = new { type = "boolean", description = "是否同步模板全部分支（建议 true，否则新仓可能无分支，默认 true）", @default = true },
                visibility = new { type = "string", description = "可见性：internal（组织内公开，默认）/ private（私有）/ public（公开）", @default = "internal" },
                namespaceId = new { type = "integer", description = "所属代码组 / 命名空间 ID（整数，可选，不传则建在组织根下）" },
                createParentPath = new { type = "boolean", description = "path 含多级分组时是否自动创建父路径（建议 true）" },
                description = new { type = "string", description = "仓库描述（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "name", "path" }
        }.ToJson()
    };

    /// <summary>
    /// 查询代码组 / 命名空间列表（ListNamespaces）
    /// </summary>
    public static readonly MCPTool CodeupListGroups = new()
    {
        Name = "codeup_list_groups",
        Description = "查询云效代码组 / 命名空间列表（返回 id / name / path / pathWithNamespace / parentId）。用于建库前「先查后建」判断目标代码组是否已存在；不传 search 时列出全部。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                search = new { type = "string", description = "代码组名关键字（可选）" },
                parentId = new { type = "integer", description = "父级代码组 ID 过滤（可选）" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（默认 100）", @default = 100 },
                orderBy = new { type = "string", description = "排序字段（可选）" },
                sort = new { type = "string", description = "排序方向：asc / desc（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            }
        }.ToJson()
    };

    /// <summary>
    /// 查询仓库分支列表（ListBranches）
    /// </summary>
    public static readonly MCPTool CodeupListBranches = new()
    {
        Name = "codeup_list_branches",
        Description = "查询云效代码仓库的分支列表（含 defaultBranch 标记）。用于确认模板仓可用分支、核对新仓的模板分支是否已同步就绪。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                repositoryId = new { type = "integer", description = "仓库 ID（整数，必填；取自仓库详情或建库返回）" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（默认 100）", @default = 100 },
                sort = new { type = "string", description = "排序方向：asc / desc（可选）" },
                search = new { type = "string", description = "分支名关键字（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "repositoryId" }
        }.ToJson()
    };

    /// <summary>
    /// 创建代码组（CreateNamespace）
    /// </summary>
    public static readonly MCPTool CodeupCreateGroup = new()
    {
        Name = "codeup_create_group",
        Description = "在云效创建代码组（命名空间），返回含 id / pathWithNamespace。注意：本工具直接创建、不做重名查重；先查后建由调用方（助手）负责——请先用 codeup_list_groups 确认同名组不存在。parentId 留空表示建在组织根下。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "代码组名（显示名，必填）" },
                path = new { type = "string", description = "代码组路径段（必填，通常与 name 相同，勿含 \"/\"）" },
                parentId = new { type = "integer", description = "父级代码组 ID（整数，可选；不传则建在组织根下）" },
                visibility = new { type = "string", description = "可见性：internal（组织内公开，默认）/ private（私有）", @default = "internal" },
                description = new { type = "string", description = "代码组描述（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "name", "path" }
        }.ToJson()
    };

    #endregion

    #region 流水线工具（Flow）

    /// <summary>
    /// 查找流水线（ListPipelines）
    /// </summary>
    public static readonly MCPTool FlowListPipelines = new()
    {
        Name = "flow_list_pipelines",
        Description = "查找云效流水线。注意：返回体含创建人（createAccountId）但**不含运行状态**，需要时再查流水线详情。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                pipelineName = new { type = "string", description = "流水线名称过滤（可选）" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（默认 20）", @default = 20 },
                statusList = new { type = "string", description = "状态过滤（可选，逗号分隔）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            }
        }.ToJson()
    };

    /// <summary>
    /// 列出主机组（部署目标，ListHostGroups）
    /// </summary>
    public static readonly MCPTool FlowListHostGroups = new()
    {
        Name = "flow_list_host_groups",
        Description = "列出云效主机组（流水线部署阶段的目标机器组）。注意：返回体只有 id（数字）/ uuid / 名称 / 机器台数 / 地域，**不含**具体机器（hostInfos 恒为 null），要机器明细请再用 flow_get_host_group；列表接口的 perPage / 名称过滤等参数真机不生效，一律返回全量。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            }
        }.ToJson()
    };

    /// <summary>
    /// 查询主机组详情（GetHostGroup，含机器明细）
    /// </summary>
    public static readonly MCPTool FlowGetHostGroup = new()
    {
        Name = "flow_get_host_group",
        Description = "查询云效主机组详情，返回该组机器明细（instanceName / ip / status / machineSn）。注意：hostGroupId 必须是**数字 id**（flow_list_host_groups 返回的 id），传 uuid 会报 400。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                hostGroupId = new { type = "integer", description = "主机组数字 id（必填，不是 uuid）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "hostGroupId" }
        }.ToJson()
    };

    /// <summary>
    /// 创建流水线（CreatePipeline，提交新版 YAML）
    /// </summary>
    public static readonly MCPTool FlowCreatePipeline = new()
    {
        Name = "flow_create_pipeline",
        Description = "创建云效流水线：提交新版 YAML（顶层 stages:）与名称。注意 content 必须是新版 YAML，控制台 tb 形态的 flow 原文不可提交（会报 1209300 校验失败）。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "流水线名称（必填）" },
                content = new { type = "string", description = "流水线新版 YAML 内容（必填，顶层须为 stages:）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "name", "content" }
        }.ToJson()
    };

    /// <summary>
    /// 编辑流水线（UpdatePipeline）
    /// </summary>
    public static readonly MCPTool FlowUpdatePipeline = new()
    {
        Name = "flow_update_pipeline",
        Description = "更新云效流水线。注意：更新会一并覆盖流水线名称，须回传现有名称以免被改名。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                pipelineId = new { type = "string", description = "流水线 ID（必填）" },
                name = new { type = "string", description = "流水线名称（必填，须回传现有名称）" },
                content = new { type = "string", description = "流水线 YAML 内容（必填）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "pipelineId", "name", "content" }
        }.ToJson()
    };

    /// <summary>
    /// 运行流水线（触发整条运行或单个任务）
    /// </summary>
    public static readonly MCPTool FlowRunPipeline = new()
    {
        Name = "flow_run_pipeline",
        Description = "运行云效流水线：不传 pipelineRunId/jobId 时触发整条流水线运行（CreatePipelineRun）；传入时执行某个运行实例中的单个任务。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                pipelineId = new { type = "string", description = "流水线 ID（必填）" },
                pipelineRunId = new { type = "string", description = "运行实例 ID（运行单个任务时必填）" },
                jobId = new { type = "string", description = "任务 ID（运行单个任务时必填）" },
                branch = new { type = "string", description = "分支或标签（可选）" },
                env = new
                {
                    type = "object",
                    description = "环境变量（可选）",
                    additionalProperties = new { type = "string" }
                },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "pipelineId" }
        }.ToJson()
    };

    #endregion

    /// <summary>
    /// 获取所有工具列表（共 13 个：PRD §4.11.3 定义 10 个 + v0.32 新增 2 个只读部署目标工具，
    /// 本次删 <c>codeup_clone</c> / <c>codeup_push</c> 2 个、补 <c>codeup_list_template_repos</c> /
    /// <c>codeup_get_repo</c> / <c>codeup_list_groups</c> 3 个）。
    /// </summary>
    public static IEnumerable<MCPTool> GetAllTools()
    {
        yield return CodeupSearchRepos;
        yield return CodeupListTemplateRepos;
        yield return CodeupGetRepo;
        yield return CodeupCreateRepo;
        yield return CodeupListBranches;
        yield return CodeupCreateGroup;
        yield return CodeupListGroups;
        yield return FlowListPipelines;
        yield return FlowListHostGroups;
        yield return FlowGetHostGroup;
        yield return FlowCreatePipeline;
        yield return FlowUpdatePipeline;
        yield return FlowRunPipeline;
    }
}