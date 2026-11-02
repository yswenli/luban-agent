/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Hosting
*文件名： AgentHostServices
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/13
*描述：应用级服务提供者，持有根容器与进程常驻用户作用域
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 应用级服务提供者：持有根容器与进程常驻用户作用域，对外解析委托到用户作用域。
/// </summary>
public sealed class AgentHostServices : DiContainer
{
    private readonly DiContainer _root;
    private readonly DiContainer _userScope;
    private int _disposed;

    /// <summary>创建应用级服务提供者。</summary>
    /// <param name="root">根容器。</param>
    /// <param name="userScope">进程常驻用户作用域。</param>
    public AgentHostServices(DiContainer root, DiContainer userScope)
        : base((IServiceProvider)userScope)   // 解析全部转发到用户作用域（必须显式转接口，否则与集合重载二义）
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(userScope);
        _root = root;
        _userScope = userScope;
    }

    /// <summary>根容器（身份无关单例所在）。</summary>
    public DiContainer Root => _root;

    /// <summary>用户作用域容器。</summary>
    public DiContainer UserServices => _userScope;

    /// <inheritdoc />
    public override void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        // 先释放用户作用域，再释放根（与原实现顺序一致；重复释放由各自幂等保证）
        await _userScope.DisposeAsync().ConfigureAwait(false);
        await _root.DisposeAsync().ConfigureAwait(false);
    }
}