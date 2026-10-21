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
*描述：云效 MCP 工具定义（10 个，对应 PRD §4.11.3）
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
    /// 复制代码到本地（Git clone）
    /// </summary>
    public static readonly MCPTool CodeupClone = new()
    {
        Name = "codeup_clone",
        Description = "把云效代码仓库克隆到本地（Git over HTTPS，鉴权 oauth2:&lt;PAT&gt;@）。httpsUrl 需取自仓库详情的 httpUrlToRepo。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                httpsUrl = new { type = "string", description = "仓库 HTTPS 克隆地址（取自仓库详情的 httpUrlToRepo）" },
                localPath = new { type = "string", description = "本地目标目录" },
                branch = new { type = "string", description = "要克隆的分支或标签（对应 git clone -b，可选）" }
            },
            required = new[] { "httpsUrl", "localPath" }
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
    /// 上传代码到代码仓库（Git push）
    /// </summary>
    public static readonly MCPTool CodeupPush = new()
    {
        Name = "codeup_push",
        Description = "把本地改动推送到云效代码仓库（Git over HTTPS，鉴权 oauth2:&lt;PAT&gt;@）。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                localPath = new { type = "string", description = "本地仓库工作目录" },
                remoteUrl = new { type = "string", description = "远端仓库 HTTPS 地址" },
                branch = new { type = "string", description = "要推送的分支（可选，默认当前分支）" },
                force = new { type = "boolean", description = "是否强制推送（默认 false）", @default = false }
            },
            required = new[] { "localPath", "remoteUrl" }
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
        Description = "在云效创建代码组（命名空间），返回含 id / pathWithNamespace。注意：本工具直接创建、不做重名查重；先查后建由向导后端负责，聊天路径请自行先确认同名组不存在。parentId 留空表示建在组织根下。",
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
        Description = "查找云效流水线。注意：返回体不含创建人与运行状态，需要时再查流水线详情。",
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
    /// 复制流水线（读取源 YAML → 替换 → 创建）
    /// </summary>
    public static readonly MCPTool FlowClonePipeline = new()
    {
        Name = "flow_clone_pipeline",
        Description = "从参考流水线克隆（内部：读取源 YAML → 按 substitutions 替换 名称/端口/仓库地址/流水线参数 → 创建）。新流水线部署阶段默认禁用，并返回待人工绑定清单。",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                sourcePipelineId = new { type = "string", description = "源流水线 ID（如 TJCES 参考流水线）" },
                newName = new { type = "string", description = "新流水线名称" },
                substitutions = new
                {
                    type = "object",
                    description = "变量替换映射（YAML 占位符 → 实际值，如 {\"PROJECT_NAME\": \"demo\"}）",
                    additionalProperties = new { type = "string" }
                },
                disableDeployStage = new { type = "boolean", description = "是否默认禁用部署阶段（默认 true，见 PRD §6 #14）", @default = true },
                organizationId = new { type = "string", description = "组织 ID（中心版必带）" }
            },
            required = new[] { "sourcePipelineId", "newName" }
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
    /// 获取所有工具列表（PRD §4.11.3 定义的 10 个）
    /// </summary>
    public static IEnumerable<MCPTool> GetAllTools()
    {
        yield return CodeupSearchRepos;
        yield return CodeupClone;
        yield return CodeupCreateRepo;
        yield return CodeupPush;
        yield return CodeupListBranches;
        yield return CodeupCreateGroup;
        yield return FlowListPipelines;
        yield return FlowClonePipeline;
        yield return FlowUpdatePipeline;
        yield return FlowRunPipeline;
    }
}