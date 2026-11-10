/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebHostGate
*描述：把访问门禁的请求上下文类型换进 SAEA.MVC（替代其默认 HttpContext）
*
*****************************************************************************/

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 门禁安装器。
/// </summary>
/// <remarks>
/// <para>
/// <c>SAEAMvcApplication</c> 在构造时把 <c>typeof(SAEA.MVC.HttpContext)</c> 传给内部 <c>WebHost</c>，
/// 之后每个请求都按该类型 <c>Activator.CreateInstance</c>。它没有公开替换入口，
/// 只能改两个私有字段：<c>SAEAMvcApplication._webHost</c> 与 <c>WebHost._httpContentType</c>。
/// </para>
/// <para>
/// 这两个名字来自 SAEA 26.10.8.1；升级 SAEA 后字段改名会让下面的代码抛异常而不是静默不鉴权，
/// 此时按新版本字段名同步本类即可。安装必须在 <c>Start()</c> 之前完成。
/// </para>
/// </remarks>
public static class WebHostGate
{
    /// <summary>
    /// 把 <see cref="GatedHttpContext"/> 换进 SAEA.MVC。
    /// </summary>
    /// <param name="application">尚未启动的 MVC 应用。</param>
    /// <exception cref="InvalidOperationException">SAEA 内部结构不符（多为升级后私有字段改名）。</exception>
    public static void Install(SAEAMvcApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var webHostField = typeof(SAEAMvcApplication).GetField(
            "_webHost", BindingFlags.Instance | BindingFlags.NonPublic);
        var webHost = webHostField?.GetValue(application) as SAEA.Http.WebHost
            ?? throw new InvalidOperationException(
                "无法取得 SAEA.MVC 的内部 WebHost（私有字段 _webHost），访问门禁无法安装；请核对 SAEA.MVC 版本内部结构。");

        var contentTypeField = typeof(SAEA.Http.WebHost).GetField(
            "_httpContentType", BindingFlags.Instance | BindingFlags.NonPublic);
        if (contentTypeField is null)
        {
            throw new InvalidOperationException(
                "无法取得 SAEA.Http.WebHost 的私有字段 _httpContentType，访问门禁无法安装；请核对 SAEA.Http 版本内部结构。");
        }

        contentTypeField.SetValue(webHost, typeof(GatedHttpContext));
    }
}