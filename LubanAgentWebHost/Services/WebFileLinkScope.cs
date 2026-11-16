/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebFileLinkScope
*描述：一轮对话内的宿主基地址（工具在后台线程执行，拿不到请求上下文）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 一轮对话内的宿主基地址作用域。
/// </summary>
/// <remarks>
/// <para>
/// 文件链接工具由模型在回合中途调用，此时 HTTP 请求可能早已返回（轮次归属会话，
/// 连接只是订阅者），<c>HttpContextBase.Current</c> 已不可用。基地址在回合开始时从
/// 请求头解析出来，用 <see cref="AsyncLocal{T}"/> 挂在本次回合的调用链上：
/// 工具在被调用的那一刻读它，既拿得到正确地址，也不会串到并发回合。
/// </para>
/// </remarks>
public static class WebFileLinkScope
{
    private static readonly AsyncLocal<string?> Address = new();

    /// <summary>当前回合的宿主基地址，形如 <c>http://192.168.3.215:18080</c>。</summary>
    public static string? CurrentBaseAddress => Address.Value;

    /// <summary>
    /// 进入作用域并设置基地址；释放时恢复进入前的值。
    /// </summary>
    /// <param name="baseAddress">宿主基地址；null 表示未取到。</param>
    /// <returns>用于退出作用域的令牌。</returns>
    public static IDisposable Begin(string? baseAddress)
    {
        var previous = Address.Value;
        Address.Value = string.IsNullOrWhiteSpace(baseAddress) ? null : baseAddress.Trim();
        return new RestoreToken(previous);
    }

    /// <summary>恢复进入作用域前的基地址。</summary>
    private sealed class RestoreToken(string? previous) : IDisposable
    {
        private int _disposed;

        /// <summary>还原上一个值；重复调用无副作用。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            Address.Value = previous;
        }
    }
}