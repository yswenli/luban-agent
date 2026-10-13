/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： AgentHostServices
*版本号： V1.0.0.0
*创建时间：2026/10/13
*描述：应用级服务提供者，持有根容器与进程常驻用户作用域
*
*****************************************************************************/
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCore.Hosting;

/// <summary>
/// 应用级服务提供者：持有根容器与**进程常驻用户作用域**，对外解析委托到用户作用域。
/// CLI/Codex 把它当作原来的 <c>IServiceProvider Services</c> 使用。
/// </summary>
public sealed class AgentHostServices : IServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _root;
    private readonly IServiceScope _userScope;
    private int _disposed;

    /// <summary>创建应用级服务提供者。</summary>
    /// <param name="root">根容器。</param>
    /// <param name="userScope">进程常驻用户作用域。</param>
    public AgentHostServices(IServiceProvider root, IServiceScope userScope)
    {
        _root = root;
        _userScope = userScope;
    }

    /// <summary>根容器（身份无关单例所在）。</summary>
    public IServiceProvider Root => _root;

    /// <summary>用户作用域服务提供者。</summary>
    public IServiceProvider UserServices => _userScope.ServiceProvider;

    /// <inheritdoc />
    public object? GetService(Type serviceType) => _userScope.ServiceProvider.GetService(serviceType);

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _userScope.Dispose();
        if (_root is IAsyncDisposable ad)
            await ad.DisposeAsync();
        else
            (_root as IDisposable)?.Dispose();
    }
}