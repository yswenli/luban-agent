/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： WorkspaceController
*描述：工作区接口：列表 / 当前 / 创建 / 切换 / 删除 / 重命名
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 工作区接口。
/// </summary>
/// <remarks>
/// 只做路由与参数透传，业务在 <see cref="WebWorkspaceService"/>。
/// </remarks>
public sealed class WorkspaceController : Controller
{
    /// <summary>当前用户的工作区列表。</summary>
    /// <returns>工作区列表。</returns>
    [HttpGet]
    public Task<ActionResult> List() => WebWorkspaceService.ListAsync();

    /// <summary>当前工作区。</summary>
    /// <returns>工作区；未设置时 data 为 null。</returns>
    [HttpGet]
    public Task<ActionResult> Current() => WebWorkspaceService.CurrentAsync();

    /// <summary>按根目录取工作区。</summary>
    /// <param name="rootPath">根目录路径。</param>
    /// <returns>工作区；不存在时 data 为 null。</returns>
    [HttpGet]
    public Task<ActionResult> ByRootPath(string? rootPath) => WebWorkspaceService.ByRootPathAsync(rootPath);

    /// <summary>根目录是否已被任一用户登记为工作区（跨用户判重）。</summary>
    /// <param name="rootPath">根目录路径。</param>
    /// <returns><c>{used:true|false}</c>。</returns>
    [HttpGet]
    public Task<ActionResult> RootUsed(string? rootPath) => WebWorkspaceService.RootUsedAsync(rootPath);

    /// <summary>按名称片段模糊搜索工作区。</summary>
    /// <param name="keyword">名称关键字。</param>
    /// <returns>匹配的工作区列表。</returns>
    [HttpGet]
    public Task<ActionResult> Search(string? keyword) => WebWorkspaceService.SearchAsync(keyword);

    /// <summary>创建工作区（S2d §13.2 ensure 幂等语义：rootPath 命中既有工作区即复用）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceCreateRequest"/> JSON。</param>
    /// <returns>工作区（含 reused 标记）。</returns>
    [HttpPost]
    public Task<ActionResult> Create(string payload) => WebWorkspaceService.CreateAsync(payload);

    /// <summary>允许的工作区根目录（登录用户可读）。</summary>
    /// <returns><c>{roots:[...]}</c>。</returns>
    [HttpGet]
    public Task<ActionResult> Roots() => WebWorkspaceService.ListRootsAsync();

    /// <summary>保存工作区根目录（仅管理员，机器级共享）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceRootsRequest"/> JSON。</param>
    /// <returns>实际写入的根目录。</returns>
    [HttpPost]
    public Task<ActionResult> SaveRoots(string payload) => WebWorkspaceService.SaveRootsAsync(payload);

    /// <summary>
    /// 浏览本机目录（只读，S2d §13.1）：列指定路径的子目录（单层、不递归、不列文件）；
    /// path 空/缺省列盘符根。越界/不存在/无权限 → 400/404 语义 message。
    /// </summary>
    /// <param name="path">目录路径（绝对）；空/缺省列盘符。</param>
    /// <returns><c>{dirs:[{name,path}]}</c>。</returns>
    [HttpGet]
    public Task<ActionResult> Dirs(string? path) => WebWorkspaceService.ListDirsAsync(path);

    /// <summary>切换工作区。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>切换后的工作区。</returns>
    [HttpPost]
    public Task<ActionResult> Switch(string payload) => WebWorkspaceService.SwitchAsync(payload);

    /// <summary>删除工作区（级联清理会话与知识库索引）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>删除结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebWorkspaceService.DeleteAsync(payload);

    /// <summary>重命名工作区（仅改显示名）。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceRenameRequest"/> JSON。</param>
    /// <returns>重命名结果。</returns>
    [HttpPost]
    public Task<ActionResult> Rename(string payload) => WebWorkspaceService.RenameAsync(payload);

    /// <summary>
    /// 确认并持久化工作区授权。
    /// </summary>
    /// <remarks>
    /// 授权是一次人工确认：客户端弹窗得到用户同意后调本接口落库。
    /// 服务端无人值守，故此处直接放行提示，只负责持久化。
    /// </remarks>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>授权后的工作区。</returns>
    [HttpPost]
    public Task<ActionResult> Authorize(string payload) => WebWorkspaceService.AuthorizeAsync(payload);

    /// <summary>确保工作区配置目录已初始化。</summary>
    /// <param name="payload">包装后的 <see cref="WorkspaceIdRequest"/> JSON。</param>
    /// <returns>工作区。</returns>
    [HttpPost]
    public Task<ActionResult> EnsureConfig(string payload) => WebWorkspaceService.EnsureConfigAsync(payload);
}