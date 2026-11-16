/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebFileLinkToolPlugin
*描述：取文件链接工具组（Web 宿主专属：只有浏览器页面才有“可点击的宿主地址”）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 取文件链接工具组插件。
/// </summary>
/// <remarks>
/// <para>
/// 只在 WebHost 注册：CLI / 桌面端不需要浏览器地址，注册进来只会多一个模型可能误用的工具。
/// 工具无副作用（只解析路径并拼地址），因此不走确认流程。
/// </para>
/// <para>
/// 本组没有独立配置项，宿主注册即启用；忽略 <c>toolsOptions</c>（该参数按组整组替换，
/// 本组不参与宿主配置，收到非 null 也无成员可覆盖）。
/// </para>
/// </remarks>
public sealed class WebFileLinkToolPlugin : ILuBanToolPlugin
{
    /// <inheritdoc />
    public string GroupName => "webfile";

    /// <inheritdoc />
    public string? Description => "取文件链接：把工作区内的文件转成浏览器可打开的绝对地址（页内弹层预览）";

    /// <inheritdoc />
    public IReadOnlyList<AIFunction> GetTools(DiContainer sp, ToolGroupOptions? toolsOptions = null)
    {
        // 工作区上下文按调用时的作用域解析：插件可能在别的会话作用域里被取用，不能提前固化
        var group = new WebFileLinkToolGroup(sp.GetRequiredService<IWorkspaceContext>());
        // 按 MethodInfo 注册：方法组委托会丢掉 [Description]，模型就看不懂参数怎么填
        var method = typeof(WebFileLinkToolGroup)
            .GetMethod(nameof(WebFileLinkToolGroup.GetFileLinkAsync), BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("取文件链接工具方法缺失");

        return
        [
            AIFunctionFactory.Create(
                method,
                group,
                new AIFunctionFactoryOptions { Name = "GetFileLink" })
        ];
    }

    /// <inheritdoc />
    public bool IsEnabled(LuBanAgentOptions options) => true;
}