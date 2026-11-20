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
*描述：云效 MCP 工具定义
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

    #region 代码仓库工具（Codeup）· SDK-only 包装

    /// <summary>
    /// 更新代码仓库（B2：SDK 有 UpdateRepositoryAsync，MCP 层包装）
    /// </summary>
    public static readonly MCPTool CodeupUpdateRepo = new()
    {
        Name = "codeup_update_repository",
        Description = "更新云效代码仓库属性。只传要改的字段（name / visibilityLevel / description / defaultBranch），不传的字段保持原值。visibilityLevel：0 私有 / 1 组织内公开 / 2 公开。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                repositoryId = new { type = "string", description = "仓库 ID（数字 id，必填）" },
                name = new { type = "string", description = "新仓库名称（可选）" },
                visibilityLevel = new { type = "integer", description = "可见性等级：0 私有 / 1 组织内公开 / 2 公开（可选）" },
                description = new { type = "string", description = "仓库描述（可选）" },
                defaultBranch = new { type = "string", description = "默认分支名（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "repositoryId" }
        }.ToJson()
    };

    #endregion

    #region 研发项目工具（Projex）

    /// <summary>
    /// 查找研发项目（Projex projects:search）
    /// </summary>
    public static readonly MCPTool ProjexListProjects = new()
    {
        Name = "projex_list_projects",
        Description = "查找云效 DevOps 研发项目。注意：name 检索走 conditions（不是 keyword 字段），keyword 为空时返回全量。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（默认 20，上限 200）", @default = 20 },
                keyword = new { type = "string", description = "项目名称关键字（可选，模糊过滤）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            }
        }.ToJson()
    };

    /// <summary>
    /// 查询研发项目详情（Projex GetProjectInfo）
    /// </summary>
    public static readonly MCPTool ProjexGetProject = new()
    {
        Name = "projex_get_project",
        Description = "按项目 ID 查询云效 DevOps 研发项目详情（名称、标识、描述、创建/修改时间、状态）。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId" }
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

    #region 流水线工具（Flow）· SDK-only 包装

    /// <summary>
    /// 查询流水线详情（B1：SDK 有 GetPipelineAsync，MCP 层包装；返回 pipelineConfig.flow YAML）
    /// </summary>
    public static readonly MCPTool FlowGetPipeline = new()
    {
        Name = "flow_get_pipeline",
        Description = "查询云效流水线详情，返回流水线 YAML（pipelineConfig.flow）、名称、状态。FR-3 复制流水线前必读 YAML。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                pipelineId = new { type = "string", description = "流水线 ID（必填）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "pipelineId" }
        }.ToJson()
    };

    #endregion

    #region 迭代工具（Projex Sprints）

    /// <summary>
    /// 获取迭代列表（ListSprints）
    /// </summary>
    public static readonly MCPTool ProjexListSprints = new()
    {
        Name = "projex_list_sprints",
        Description = "获取云效项目的迭代列表。状态取值 TODO（未开始）/ DOING（进行中）/ ARCHIVED（已完成），不传则返回全部。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                status = new { type = "string", description = "状态过滤，逗号分隔，如 TODO,DOING（可选）" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（1–200，默认 50）", @default = 50 },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId" }
        }.ToJson()
    };

    /// <summary>
    /// 获取迭代详情（GetSprint）
    /// </summary>
    public static readonly MCPTool ProjexGetSprint = new()
    {
        Name = "projex_get_sprint",
        Description = "按迭代 ID 获取云效迭代详情（名称 / 状态 / 起止日期 / 负责人）。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                sprintId = new { type = "string", description = "迭代 ID（必填，取自迭代列表）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId", "sprintId" }
        }.ToJson()
    };

    /// <summary>
    /// 创建迭代（CreateSprint）
    /// </summary>
    public static readonly MCPTool ProjexCreateSprint = new()
    {
        Name = "projex_create_sprint",
        Description = "在云效项目下创建迭代。注意：云效要求 owners（负责人 userId 列表）至少一项，不能留空。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                name = new { type = "string", description = "迭代名称（必填）" },
                owners = new { type = "array", items = new { type = "string" }, description = "负责人 userId 列表（必填，至少一项）" },
                startDate = new { type = "string", description = "开始日期（可选，如 2026-11-20）" },
                endDate = new { type = "string", description = "结束日期（可选）" },
                description = new { type = "string", description = "迭代描述（可选）" },
                capacityHours = new { type = "integer", description = "工时容量（小时，可选）" },
                operatorId = new { type = "string", description = "操作者 userId（可选，个人 token 下无效）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId", "name", "owners" }
        }.ToJson()
    };

    #endregion

    #region 工作项工具（Projex Workitems）

    /// <summary>
    /// 获取项目工作项类型（ListWorkitemTypes）
    /// </summary>
    public static readonly MCPTool WorkitemListTypes = new()
    {
        Name = "workitem_list_types",
        Description = "获取云效项目下指定分类的工作项类型（id / name）。创建需求 / 任务、查询状态与工作流所需的 workitemTypeId 均来自本工具。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                category = new { type = "string", description = "分类（必填）：Req 需求 / Bug 缺陷 / Task 任务", @enum = new[] { "Req", "Bug", "Task" } },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId", "category" }
        }.ToJson()
    };

    /// <summary>
    /// 获取工作项状态列表（GetWorkitemWorkflow 的 statuses 视图）
    /// </summary>
    public static readonly MCPTool WorkitemListStatuses = new()
    {
        Name = "workitem_list_statuses",
        Description = "获取某工作项类型的状态列表与默认状态。注意：云效**没有**独立的状态接口，状态与列序真源是工作流接口，本工具是其 statuses 视图；需要工作流 id / 名称时用 workitem_list_workflow。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                workitemTypeId = new { type = "string", description = "工作项类型 ID（必填，取自 workitem_list_types）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId", "workitemTypeId" }
        }.ToJson()
    };

    /// <summary>
    /// 获取工作项工作流（GetWorkitemWorkflow）
    /// </summary>
    public static readonly MCPTool WorkitemListWorkflow = new()
    {
        Name = "workitem_list_workflow",
        Description = "获取某工作项类型的工作流（工作流 id / 名称 / 默认状态 / 状态列表）。statuses 的返回顺序即看板列序。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                workitemTypeId = new { type = "string", description = "工作项类型 ID（必填，取自 workitem_list_types）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId", "workitemTypeId" }
        }.ToJson()
    };

    /// <summary>
    /// 查询需求列表（SearchWorkitems，category=Req）
    /// </summary>
    public static readonly MCPTool WorkitemListRequirements = new()
    {
        Name = "workitem_list_requirements",
        Description = "按条件查询云效项目下的需求（category=Req），服务端过滤。分页窗口上限 page*perPage ≤ 10000（超出云效返回 400）。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                sprintId = new { type = "string", description = "迭代 ID 过滤（可选）" },
                status = new { type = "string", description = "状态 ID，逗号分隔（可选，取自 workitem_list_statuses）" },
                subject = new { type = "string", description = "标题包含关键字（可选）" },
                assignedTo = new { type = "string", description = "负责人 userId，逗号分隔（可选）" },
                workitemTypeId = new { type = "string", description = "工作项类型 ID，逗号分隔（可选）" },
                orderBy = new { type = "string", description = "排序字段（默认 gmtCreate）", @default = "gmtCreate" },
                sort = new { type = "string", description = "排序方向：desc / asc（默认 desc）", @default = "desc" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（1–200，默认 50）", @default = 50 },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId" }
        }.ToJson()
    };

    /// <summary>
    /// 创建需求（CreateWorkitem）
    /// </summary>
    public static readonly MCPTool WorkitemCreateRequirement = new()
    {
        Name = "workitem_create_requirement",
        Description = "在云效项目下创建需求。workitemTypeId 由 workitem_list_types(category=Req) 取得；assignedTo 为负责人 userId（云效必填）。返回新建工作项 id。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                subject = new { type = "string", description = "标题（必填）" },
                workitemTypeId = new { type = "string", description = "工作项类型 ID（必填，取自 workitem_list_types）" },
                assignedTo = new { type = "string", description = "负责人 userId（必填）" },
                sprintId = new { type = "string", description = "所属迭代 ID（可选）" },
                description = new { type = "string", description = "描述（可选）" },
                formatType = new { type = "string", description = "描述格式：RICHTEXT / MARKDOWN（可选，默认 RICHTEXT）" },
                parentId = new { type = "string", description = "父工作项 ID（可选）" },
                labels = new { type = "array", items = new { type = "string" }, description = "标签 ID 列表（可选）" },
                priority = new { type = "string", description = "优先级字段值（可选；无 fieldId 口径，非标准字段请用 customFieldValues）" },
                customFieldValues = new { type = "object", description = "自定义字段值，形如 {\"字段ID\":\"值\"}（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId", "subject", "workitemTypeId", "assignedTo" }
        }.ToJson()
    };

    /// <summary>
    /// 查询任务列表（SearchWorkitems，category=Task）
    /// </summary>
    public static readonly MCPTool WorkitemListTasks = new()
    {
        Name = "workitem_list_tasks",
        Description = "按条件查询云效项目下的任务（category=Task），服务端过滤。分页窗口上限 page*perPage ≤ 10000。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                sprintId = new { type = "string", description = "迭代 ID 过滤（可选）" },
                status = new { type = "string", description = "状态 ID，逗号分隔（可选）" },
                subject = new { type = "string", description = "标题包含关键字（可选）" },
                assignedTo = new { type = "string", description = "负责人 userId，逗号分隔（可选）" },
                workitemTypeId = new { type = "string", description = "工作项类型 ID，逗号分隔（可选）" },
                orderBy = new { type = "string", description = "排序字段（默认 gmtCreate）", @default = "gmtCreate" },
                sort = new { type = "string", description = "排序方向：desc / asc（默认 desc）", @default = "desc" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（1–200，默认 50）", @default = 50 },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId" }
        }.ToJson()
    };

    /// <summary>
    /// 创建任务（CreateWorkitem）
    /// </summary>
    public static readonly MCPTool WorkitemCreateTask = new()
    {
        Name = "workitem_create_task",
        Description = "在云效项目下创建任务。workitemTypeId 由 workitem_list_types(category=Task) 取得；assignedTo 为负责人 userId（云效必填）。返回新建工作项 id。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                subject = new { type = "string", description = "标题（必填）" },
                workitemTypeId = new { type = "string", description = "工作项类型 ID（必填，取自 workitem_list_types）" },
                assignedTo = new { type = "string", description = "负责人 userId（必填）" },
                sprintId = new { type = "string", description = "所属迭代 ID（可选）" },
                description = new { type = "string", description = "描述（可选）" },
                formatType = new { type = "string", description = "描述格式：RICHTEXT / MARKDOWN（可选）" },
                parentId = new { type = "string", description = "父工作项 ID（可选）" },
                labels = new { type = "array", items = new { type = "string" }, description = "标签 ID 列表（可选）" },
                customFieldValues = new { type = "object", description = "自定义字段值，形如 {\"字段ID\":\"值\"}（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId", "subject", "workitemTypeId", "assignedTo" }
        }.ToJson()
    };

    /// <summary>
    /// 更新任务 / 工作项（UpdateWorkitem）
    /// </summary>
    public static readonly MCPTool WorkitemUpdateTask = new()
    {
        Name = "workitem_update_task",
        Description = "更新云效工作项（任务 / 需求通用）。只传要改的字段：status 用状态 ID（取自 workitem_list_statuses），assignedTo 用 userId，sprintId 换迭代；非标准字段放进 customFieldValues。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                workitemId = new { type = "string", description = "工作项 ID（必填）" },
                subject = new { type = "string", description = "新标题（可选）" },
                description = new { type = "string", description = "新描述（可选）" },
                formatType = new { type = "string", description = "描述格式：RICHTEXT / MARKDOWN（可选）" },
                status = new { type = "string", description = "新状态 ID（可选，取自 workitem_list_statuses）" },
                assignedTo = new { type = "string", description = "新负责人 userId（可选）" },
                sprintId = new { type = "string", description = "新迭代 ID（可选）" },
                parentId = new { type = "string", description = "新父工作项 ID（可选）" },
                labels = new { type = "array", items = new { type = "string" }, description = "标签 ID 列表（可选，整体替换）" },
                customFieldValues = new { type = "object", description = "自定义字段值，形如 {\"字段ID\":\"值\"}（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "workitemId" }
        }.ToJson()
    };

    /// <summary>
    /// 看板按迭代取工作项（SearchWorkitems 的迭代视图）
    /// </summary>
    public static readonly MCPTool BoardListWorkitemsBySprint = new()
    {
        Name = "board_list_workitems_by_sprint",
        Description = "看板数据源：按迭代拉取云效工作项（服务端按 sprint 过滤），可按状态 ID 列表过滤以渲染各列。列序请结合 workitem_list_workflow 的 statuses。分页窗口上限 page*perPage ≤ 10000。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "string", description = "项目 ID（必填）" },
                sprintId = new { type = "string", description = "迭代 ID（必填）" },
                status = new { type = "string", description = "状态 ID 列表过滤，逗号分隔（可选）" },
                category = new { type = "string", description = "分类：Req / Bug / Task（可选，默认 Req）", @default = "Req" },
                assignedTo = new { type = "string", description = "负责人 userId，逗号分隔（可选）" },
                orderBy = new { type = "string", description = "排序字段（默认 gmtCreate）", @default = "gmtCreate" },
                sort = new { type = "string", description = "排序方向：desc / asc（默认 desc）", @default = "desc" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（1–200，默认 100）", @default = 100 },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "projectId", "sprintId" }
        }.ToJson()
    };

    /// <summary>
    /// 获取工作项评论列表（ListWorkitemComments）
    /// </summary>
    public static readonly MCPTool WorkitemListComments = new()
    {
        Name = "workitem_list_comments",
        Description = "获取云效工作项的评论列表（内容 / 格式 / 评论人 / 是否置顶），按云效返回顺序。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                workitemId = new { type = "string", description = "工作项 ID（必填）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "workitemId" }
        }.ToJson()
    };

    /// <summary>
    /// 获取工作项详情（GetWorkitem）
    /// </summary>
    public static readonly MCPTool WorkitemGet = new()
    {
        Name = "workitem_get",
        Description = "按工作项 ID 获取云效工作项详情（标题 / 描述 / 状态 / 迭代 / 负责人 / 类型 / 编号）。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                workitemId = new { type = "string", description = "工作项 ID（必填）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "workitemId" }
        }.ToJson()
    };

    /// <summary>
    /// 获取工作项附件列表（ListWorkitemAttachments）
    /// </summary>
    public static readonly MCPTool WorkitemListAttachments = new()
    {
        Name = "workitem_list_attachments",
        Description = "获取云效工作项的附件列表（fileId / 文件名 / 大小 / 后缀 / 临时下载地址）。临时地址有时效，需要稳定下载请再用 workitem_get_file。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                workitemId = new { type = "string", description = "工作项 ID（必填）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "workitemId" }
        }.ToJson()
    };

    /// <summary>
    /// 获取工作项文件下载信息（GetWorkitemFile）
    /// </summary>
    public static readonly MCPTool WorkitemGetFile = new()
    {
        Name = "workitem_get_file",
        Description = "获取云效工作项文件的下载信息（文件名 / 大小 / 后缀 / 临时下载地址）。fileId 取自 workitem_list_attachments 的 fileId。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                workitemId = new { type = "string", description = "工作项 ID（必填）" },
                fileId = new { type = "string", description = "文件 ID（必填，取自附件列表的 fileId）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "workitemId", "fileId" }
        }.ToJson()
    };

    /// <summary>
    /// 创建工作项关系（CreateWorkitemRelation）
    /// </summary>
    public static readonly MCPTool WorkitemCreateRelation = new()
    {
        Name = "workitem_create_relation",
        Description = "在两个云效工作项之间建立关系。relationType：PARENT（目标为父）/ SUB（目标为子）/ ASSOCIATED（关联）/ DEPEND_ON（依赖目标）/ DEPENDED_BY（被目标依赖）。返回新建关系 id。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                workitemId = new { type = "string", description = "源工作项 ID（必填）" },
                targetWorkitemId = new { type = "string", description = "目标工作项 ID（必填）" },
                relationType = new { type = "string", description = "关系类型：PARENT / SUB / ASSOCIATED / DEPEND_ON / DEPENDED_BY（必填）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "workitemId", "targetWorkitemId", "relationType" }
        }.ToJson()
    };

    #endregion

    #region 测试用例 / 测试计划工具（Testhub）

    /// <summary>
    /// 查询测试用例库列表（ListTestRepos）
    /// </summary>
    public static readonly MCPTool TestRepoList = new()
    {
        Name = "testrepo_list",
        Description = "列出云效测试用例库（testRepoIdentifier / name）。创建测试用例前必须先从这里取 testRepoIdentifier。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "用例库名称过滤（可选）" },
                page = new { type = "integer", description = "页码（从 1 开始，默认 1）", @default = 1 },
                perPage = new { type = "integer", description = "每页条数（1–100，默认 100）", @default = 100 },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            }
        }.ToJson()
    };

    /// <summary>
    /// 创建测试用例（CreateTestcase）
    /// </summary>
    public static readonly MCPTool TestcaseCreate = new()
    {
        Name = "testcase_create",
        Description = "在云效测试用例库下创建测试用例。testRepoId 取自 testrepo_list 的 testRepoIdentifier。返回新建用例 id。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                testRepoId = new { type = "string", description = "用例库 ID（必填，取自 testrepo_list）" },
                subject = new { type = "string", description = "用例标题（必填）" },
                assignedTo = new { type = "string", description = "负责人 userId（可选）" },
                directoryId = new { type = "string", description = "目录 ID（可选）" },
                preCondition = new { type = "string", description = "前置条件（可选）" },
                labels = new { type = "array", items = new { type = "string" }, description = "标签列表（可选）" },
                customFieldValues = new { type = "object", description = "自定义字段值（可选）" },
                testSteps = new { type = "array", items = new { type = "object" }, description = "测试步骤（可选，按云效原始结构透传）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "testRepoId", "subject" }
        }.ToJson()
    };

    /// <summary>
    /// 创建测试计划（CreateTestPlan）
    /// </summary>
    public static readonly MCPTool TestplanCreate = new()
    {
        Name = "testplan_create",
        Description = "创建云效测试计划。注意：该接口字段未真机验证（官方 MCP 包未提供此工具），仅传最小字段集；失败会返回错误信息，不影响已创建的需求 / 任务 / 用例。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "计划名称（必填）" },
                projectId = new { type = "string", description = "关联项目 ID（可选）" },
                sprintId = new { type = "string", description = "关联迭代 ID（可选）" },
                startDate = new { type = "string", description = "开始日期 yyyy-MM-dd（可选）" },
                endDate = new { type = "string", description = "结束日期 yyyy-MM-dd（可选）" },
                managers = new { type = "array", items = new { type = "string" }, description = "负责人 userId 列表（可选）" },
                caseIds = new { type = "array", items = new { type = "string" }, description = "纳入计划的用例 ID 列表（可选）" },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "name" }
        }.ToJson()
    };

    #endregion

    /// <summary>
    /// 获取所有工具列表（共 37 个：Codeup 8 + Projex 19 + Testhub 3 + Flow 7）。
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
        yield return CodeupUpdateRepo;
        yield return ProjexListProjects;
        yield return ProjexGetProject;
        yield return ProjexListSprints;
        yield return ProjexGetSprint;
        yield return ProjexCreateSprint;
        yield return WorkitemListTypes;
        yield return WorkitemListStatuses;
        yield return WorkitemListWorkflow;
        yield return WorkitemListRequirements;
        yield return WorkitemCreateRequirement;
        yield return WorkitemListTasks;
        yield return WorkitemCreateTask;
        yield return WorkitemUpdateTask;
        yield return BoardListWorkitemsBySprint;
        yield return WorkitemListComments;
        yield return WorkitemGet;
        yield return WorkitemListAttachments;
        yield return WorkitemGetFile;
        yield return WorkitemCreateRelation;
        yield return TestRepoList;
        yield return TestcaseCreate;
        yield return TestplanCreate;
        yield return FlowListPipelines;
        yield return FlowListHostGroups;
        yield return FlowGetHostGroup;
        yield return FlowCreatePipeline;
        yield return FlowUpdatePipeline;
        yield return FlowRunPipeline;
        yield return FlowGetPipeline;
    }
}