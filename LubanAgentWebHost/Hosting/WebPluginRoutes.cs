/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebPluginRoutes
*描述：Web 插件管理接口与页面镜像的路径常量
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 插件管理接口路径。
/// </summary>
/// <remarks>
/// 控制器由 SAEA 按 <c>/{controller}/{action}</c> 派发，这里的常量用于前端与单测对齐，
/// 不进客户端 SDK 的路由表（这些接口只服务浏览器）。
/// </remarks>
public static class WebPluginRoutes
{
    /// <summary>接口前缀。</summary>
    public const string Prefix = "/plugin";

    /// <summary>列出 Web 插件根下的插件。</summary>
    public const string List = "/plugin/list";

    /// <summary>安装插件。</summary>
    public const string Install = "/plugin/install";

    /// <summary>升级插件。</summary>
    public const string Upgrade = "/plugin/upgrade";

    /// <summary>启用插件。</summary>
    public const string Enable = "/plugin/enable";

    /// <summary>停用插件。</summary>
    public const string Disable = "/plugin/disable";

    /// <summary>卸载插件。</summary>
    public const string Uninstall = "/plugin/uninstall";

    /// <summary>页面镜像 URL 前缀。</summary>
    public const string WebPrefix = WebPluginPaths.MirrorUrlPrefix;
}