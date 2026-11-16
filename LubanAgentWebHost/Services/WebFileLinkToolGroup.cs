/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebFileLinkToolGroup
*描述：取文件链接工具：把工作区内文件转成浏览器可打开的绝对地址
*
*****************************************************************************/

using System.ComponentModel;

namespace LubanAgentWebHost.Services;

/// <summary>
/// 取文件链接工具实现。
/// </summary>
/// <remarks>
/// 模型只看得见工作区根目录，看不到宿主监听地址与工作区标识，过去只能自造
/// <c>file:///D:/...</c> 或裸相对路径——网页里要么被浏览器拦下，要么点开 404。
/// 本工具把这些信息都在宿主侧补齐，模型只需给出文件路径。
/// </remarks>
public sealed class WebFileLinkToolGroup(IWorkspaceContext workspaceContext)
{
    /// <summary>
    /// 取工作区文件的可预览绝对地址。
    /// </summary>
    /// <param name="path">文件路径：相对工作区根，或工作区内的绝对路径。</param>
    /// <returns>绝对地址，或不可预览的原因。</returns>
    [Description("把工作区内的文件转换成浏览器可直接打开的绝对地址（含 Host 与 workspaceId）。回答里要给出可点击的文件链接时（.md / .html / .txt / 图片等）必须调用本工具取地址，不要自己拼相对路径、file:/// 地址或猜测 URL；拿到地址后原样放进 Markdown 链接。工作区外的文件无法生成可预览地址，此时如实告知用户。")]
    public Task<ToolResult<string>> GetFileLinkAsync(
        [Description("工作区内的文件路径，相对工作区根（如 entities/LubanAgentCore.md、wiki/overview.md）或工作区内的绝对路径")]
        string path)
    {
        if (!WebFileLink.TryBuild(
                WebFileLinkScope.CurrentBaseAddress,
                workspaceContext.WorkspaceId,
                workspaceContext.RootPath,
                path,
                out var link,
                out var relative,
                out var error))
        {
            return Task.FromResult(ToolResult.Fail<string>(error));
        }

        return Task.FromResult(ToolResult.Ok(
            link,
            $"可预览地址：{link}（工作区路径：{relative}）。请把这个地址原样放进 Markdown 链接，用户点击后在页面内弹层预览。"));
    }
}