/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： YunxiaoConnector
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效连接器门面类
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效连接器门面类 - 聚合 CodeupClient 和 FlowClient，统一 PAT 取用
/// </summary>
/// <remarks>
/// 继承框架 <see cref="ConnectorBase"/>：连接器是<b>唯一声明单元</b>，其 MCP 暴露面
/// （默认实例为 <c>yunxiao</c>）由 <see cref="McpExposures"/> 派生，供宿主按配置决定是否注册。
/// 幂等初始化 / 释放语义由基类提供（厂商知识只在本类）。
/// </remarks>
public class YunxiaoConnector : ConnectorBase, IDisposable
{
    /// <summary>提供方键（连接器实现归属）。</summary>
    public const string ProviderKey = "aliyunDevOps";

    /// <summary>默认实例名。</summary>
    public const string DefaultName = "yunxiao";

    private readonly YunxiaoApiOptions _options;
    private readonly IYunxiaoCredentialProvider _credentialProvider;
    private readonly ProcessRunner? _processRunner;
    private readonly PluginPermissionGuard _guard;

    private CodeupClient? _codeupClient;
    private FlowClient? _flowClient;

    /// <summary>
    /// 创建云效连接器
    /// </summary>
    /// <param name="options">API 配置</param>
    /// <param name="credentialProvider">凭据提供者</param>
    /// <param name="processRunner">进程执行器（用于 Git 操作，可选）</param>
    /// <param name="permissionGuard">插件权限门禁（可选；不传则用进程级兜底实例）。</param>
    public YunxiaoConnector(
        YunxiaoApiOptions options,
        IYunxiaoCredentialProvider credentialProvider,
        ProcessRunner? processRunner = null,
        PluginPermissionGuard? permissionGuard = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _processRunner = processRunner;
        _guard = permissionGuard ?? PluginPermissionGuard.Default;

        // 实例名由连接器工厂按配置注入；未注入时为默认名（与历史行为一致）。
        Name = DefaultName;
    }

    /// <inheritdoc />
    public override string Key => ProviderKey;

    /// <inheritdoc />
    public override string DisplayName => "云效（阿里云 DevOps）";

    /// <inheritdoc />
    /// <remarks>
    /// MCP 名<b>跟随实例名</b>：默认实例即 <c>yunxiao</c>（与历史行为一致）；同一提供方的多实例
    /// 因此各自暴露独立 MCP 名与独立凭据键，互不覆盖。
    /// </remarks>
    public override IReadOnlyList<ConnectorMcpExposure> McpExposures => new[]
    {
        new ConnectorMcpExposure(
            Name,
            "云效代码管理与流水线工具",
            (_, scope) => new Mcp.YunxiaoMCPClient(this) { PermissionScope = scope })
    };

    /// <summary>
    /// 获取代码管理客户端
    /// </summary>
    public CodeupClient GetCodeupClient()
    {
        ThrowIfDisposed();

        return _codeupClient ??= new CodeupClient(_options, _credentialProvider, _processRunner, _guard, Name);
    }

    /// <summary>
    /// 获取流水线客户端
    /// </summary>
    public FlowClient GetFlowClient()
    {
        ThrowIfDisposed();

        return _flowClient ??= new FlowClient(_options, _credentialProvider, _guard, Name);
    }

    /// <summary>
    /// 获取凭据提供者
    /// </summary>
    public IYunxiaoCredentialProvider GetCredentialProvider() => _credentialProvider;

    /// <inheritdoc />
    /// <remarks>同步释放口（兼容旧调用方）；实际清理在 <see cref="OnDisposeAsync"/>。</remarks>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    protected override ValueTask OnDisposeAsync()
    {
        _codeupClient?.Dispose();
        _flowClient?.Dispose();
        return ValueTask.CompletedTask;
    }
}