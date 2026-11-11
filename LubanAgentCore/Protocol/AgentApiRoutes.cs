/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol
*文件名： AgentApiRoutes
*描述：LubanAgentWebHost 的 HTTP 路由表（客户端 SDK 与服务端控制器共用同一组常量）
*
*****************************************************************************/

namespace LubanAgentCore.Protocol;

/// <summary>
/// WebHost 路由表。SAEA.MVC 按 <c>/{controller}/{action}</c> 派发，
/// 故全部路由均为「控制器名小写 / 动作名小写」。
/// </summary>
public static class AgentApiRoutes
{
    /// <summary>对话相关接口。</summary>
    public static class Agent
    {
        /// <summary>非流式对话（不装配工具）。</summary>
        public const string Send = "/agent/send";

        /// <summary>流式对话（SSE，装配工具）。</summary>
        public const string Chat = "/agent/chat";

        /// <summary>回传工具确认结果。</summary>
        public const string Confirm = "/agent/confirm";

        /// <summary>取消指定会话正在进行的回合。</summary>
        public const string Cancel = "/agent/cancel";

        /// <summary>丢弃服务端该会话已缓存的 Agent（配置变更后重建）。</summary>
        public const string Reset = "/agent/reset";

        /// <summary>探活：该会话是否有正在进行的回合。</summary>
        public const string Active = "/agent/active";

        /// <summary>接上该会话正在进行的回合（SSE）；无活跃回合返回 204。</summary>
        public const string Attach = "/agent/attach";
    }

    /// <summary>会话相关接口。</summary>
    public static class Session
    {
        /// <summary>会话列表。</summary>
        public const string List = "/session/list";

        /// <summary>指定工作区下的会话列表。</summary>
        public const string ListByWorkspace = "/session/listByWorkspace";

        /// <summary>批量取各会话首条用户消息预览。</summary>
        public const string Previews = "/session/previews";

        /// <summary>按标识取会话。</summary>
        public const string Get = "/session/get";

        /// <summary>新建会话。</summary>
        public const string Create = "/session/create";

        /// <summary>删除会话。</summary>
        public const string Delete = "/session/delete";

        /// <summary>重命名会话。</summary>
        public const string Rename = "/session/rename";

        /// <summary>会话消息。</summary>
        public const string Messages = "/session/messages";

        /// <summary>会话最近 N 条消息。</summary>
        public const string LatestMessages = "/session/latestMessages";

        /// <summary>会话活跃消息（未压缩）。</summary>
        public const string ActiveMessages = "/session/activeMessages";

        /// <summary>追加一条消息。</summary>
        public const string AddMessage = "/session/addMessage";

        /// <summary>标记消息为已压缩。</summary>
        public const string MarkCompacted = "/session/markCompacted";

        /// <summary>会话统计。</summary>
        public const string Stats = "/session/stats";

        /// <summary>全局统计。</summary>
        public const string GlobalStats = "/session/globalStats";

        /// <summary>清空会话消息。</summary>
        public const string ClearMessages = "/session/clearMessages";

        /// <summary>物理删除全部会话。</summary>
        public const string ClearAll = "/session/clearAll";

        /// <summary>切换当前会话。</summary>
        public const string Switch = "/session/switch";

        /// <summary>当前会话（服务端回传，客户端用于镜像会话状态）。</summary>
        public const string Current = "/session/current";

        /// <summary>会话绑定工作区标识。</summary>
        public const string WorkspaceId = "/session/workspaceId";

        /// <summary>会话摘要列表（含工作区与模型，浏览器端首屏用）。</summary>
        public const string ListDetailed = "/session/listDetailed";
    }

    /// <summary>工作区相关接口。</summary>
    public static class Workspace
    {
        /// <summary>当前用户工作区列表。</summary>
        public const string List = "/workspace/list";

        /// <summary>当前工作区。</summary>
        public const string Current = "/workspace/current";

        /// <summary>创建工作区。</summary>
        public const string Create = "/workspace/create";

        /// <summary>切换工作区。</summary>
        public const string Switch = "/workspace/switch";

        /// <summary>删除工作区。</summary>
        public const string Delete = "/workspace/delete";

        /// <summary>重命名工作区。</summary>
        public const string Rename = "/workspace/rename";

        /// <summary>确认并持久化工作区授权（客户端弹窗确认后调用）。</summary>
        public const string Authorize = "/workspace/authorize";

        /// <summary>确保工作区配置目录（<c>.luban-agent</c>）已初始化。</summary>
        public const string EnsureConfig = "/workspace/ensureConfig";

        /// <summary>按根目录取工作区。</summary>
        public const string ByRootPath = "/workspace/byRootPath";

        /// <summary>按名称片段模糊搜索工作区。</summary>
        public const string Search = "/workspace/search";
    }

    /// <summary>系统接口。</summary>
    public static class System
    {
        /// <summary>健康检查（客户端启动探测用）。</summary>
        public const string Health = "/system/health";

        /// <summary>首屏引导数据（浏览器端一次性取全）。</summary>
        public const string Bootstrap = "/system/bootstrap";

        /// <summary>斜杠命令清单（浏览器端）。</summary>
        public const string Commands = "/system/commands";
    }

    /// <summary>附件接口（浏览器端上传，源码见宿主）。</summary>
    public static class Attachment
    {
        /// <summary>上传附件（JSON + Base64）。</summary>
        public const string Upload = "/attachment/upload";
    }

    /// <summary>供应商与模型接口（浏览器端对话页的模型下拉用它读写全局选择）。</summary>
    public static class Provider
    {
        /// <summary>读取全局模型选择。</summary>
        public const string Selection = "/provider/selection";

        /// <summary>切换全局模型选择。</summary>
        public const string Select = "/provider/select";

        /// <summary>按供应商拉取可用模型（有 API Key 时向远端刷新，失败回落到本地列表）。</summary>
        public const string Models = "/provider/models";

        /// <summary>供应商列表（API Key 掩码）。</summary>
        public const string List = "/provider/list";

        /// <summary>新建或更新供应商（API Key 留空表示不修改）。</summary>
        public const string Save = "/provider/save";

        /// <summary>删除供应商。</summary>
        public const string Delete = "/provider/delete";
    }

    /// <summary>技能设置接口（浏览器端，文件型条目）。</summary>
    public static class Skill
    {
        /// <summary>列出文件型技能。</summary>
        public const string List = "/skill/list";

        /// <summary>读取技能内容。</summary>
        public const string Read = "/skill/read";

        /// <summary>保存技能。</summary>
        public const string Save = "/skill/save";

        /// <summary>删除技能。</summary>
        public const string Delete = "/skill/delete";
    }

    /// <summary>规则设置接口（浏览器端，文件型条目）。</summary>
    public static class Rule
    {
        /// <summary>列出文件型规则。</summary>
        public const string List = "/rule/list";

        /// <summary>读取规则内容。</summary>
        public const string Read = "/rule/read";

        /// <summary>保存规则。</summary>
        public const string Save = "/rule/save";

        /// <summary>删除规则。</summary>
        public const string Delete = "/rule/delete";
    }

    /// <summary>MCP 设置接口（浏览器端，文件型条目）。</summary>
    public static class Mcp
    {
        /// <summary>列出文件型 MCP 配置。</summary>
        public const string List = "/mcp/list";

        /// <summary>读取 MCP 配置。</summary>
        public const string Read = "/mcp/read";

        /// <summary>保存 MCP 配置。</summary>
        public const string Save = "/mcp/save";

        /// <summary>删除 MCP 配置。</summary>
        public const string Delete = "/mcp/delete";
    }

    /// <summary>模型设置接口（浏览器端，按供应商维护模型清单）。</summary>
    /// <remarks>全局模型选择走 <see cref="Provider.Select"/>，向远端刷新走 <see cref="Provider.Models"/>，此处不重复。</remarks>
    public static class Model
    {
        /// <summary>列出供应商下的全部模型。</summary>
        public const string List = "/model/list";

        /// <summary>新增自定义模型。</summary>
        public const string Add = "/model/add";

        /// <summary>重命名自定义模型。</summary>
        public const string Rename = "/model/rename";

        /// <summary>删除自定义模型。</summary>
        public const string Delete = "/model/delete";
    }

    /// <summary>LLM Wiki 知识库接口（对应 <c>/llmwiki</c> 命令与 Codex 知识库窗口）。</summary>
    public static class LlmWiki
    {
        /// <summary>创建知识库工作区。</summary>
        public const string Create = "/llmwiki/create";

        /// <summary>删除知识库工作区并级联清理。</summary>
        public const string Delete = "/llmwiki/delete";

        /// <summary>列出已索引文件。</summary>
        public const string ListFiles = "/llmwiki/listFiles";

        /// <summary>同步索引原始资料。</summary>
        public const string Index = "/llmwiki/index";

        /// <summary>后台索引原始资料。</summary>
        public const string StartIndex = "/llmwiki/startIndex";

        /// <summary>检索知识库。</summary>
        public const string Search = "/llmwiki/search";

        /// <summary>读取知识库统计。</summary>
        public const string Status = "/llmwiki/status";

        /// <summary>客观项 lint。</summary>
        public const string Lint = "/llmwiki/lint";

        /// <summary>同步重建 wiki 向量索引。</summary>
        public const string Rebuild = "/llmwiki/rebuild";

        /// <summary>后台重建 wiki 向量索引。</summary>
        public const string StartRebuild = "/llmwiki/startRebuild";

        /// <summary>同步提取 ingest 源文件。</summary>
        public const string PrepareIngest = "/llmwiki/prepareIngest";

        /// <summary>后台提取 ingest 源文件。</summary>
        public const string StartIngest = "/llmwiki/startIngest";

        /// <summary>清空文件与切块索引。</summary>
        public const string Clear = "/llmwiki/clear";
    }

    /// <summary>A2A 协作接口。</summary>
    /// <remarks>A2A 运行时只在 WebHost 启动，客户端能读到的只有状态（启停由宿主进程负责）。</remarks>
    public static class A2A
    {
        /// <summary>读取 A2A 运行状态。</summary>
        public const string Status = "/a2a/status";

        /// <summary>读取工作区服务端配置。</summary>
        public const string GetServer = "/a2a/getServer";

        /// <summary>保存工作区服务端配置。</summary>
        public const string SaveServer = "/a2a/saveServer";

        /// <summary>列出工作区客户端配置。</summary>
        public const string ListClients = "/a2a/listClients";

        /// <summary>读取单个客户端配置。</summary>
        public const string GetClient = "/a2a/getClient";

        /// <summary>保存客户端配置。</summary>
        public const string SaveClient = "/a2a/saveClient";

        /// <summary>删除 A2A 配置（<c>server</c> 或客户端文件名）。</summary>
        public const string DeleteClient = "/a2a/deleteClient";
    }
}