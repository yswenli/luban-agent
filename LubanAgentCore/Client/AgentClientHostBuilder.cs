/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： AgentClientHostBuilder
*描述：建立到 WebHost 的连接（必要时拉起子进程）
*
*****************************************************************************/

namespace LubanAgentCore.Client;

/// <summary>
/// 客户端连接入口。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentClientOptions.BaseAddress"/> 有值时直连，否则按
/// <see cref="AgentClientOptions.AutoLaunch"/> 拉起子进程（空闲端口 + 一次性令牌）。
/// </para>
/// <para>
/// 连接成功后先用 <c>/system/health</c> 探活并刷新当前工作区镜像，
/// 让 <c>IWorkspaceManager.IsWithinWorkspace</c> 这类同步接口可用。
/// </para>
/// </remarks>
public static class AgentClientHostBuilder
{
    /// <summary>
    /// 建立连接。
    /// </summary>
    /// <param name="options">客户端参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>连接；调用方负责 <see cref="AgentClientConnection.DisposeAsync"/>。</returns>
    /// <exception cref="AgentServerException">服务不可用或未找到 WebHost 可执行文件。</exception>
    public static async Task<AgentClientConnection> ConnectAsync(
        AgentClientOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        ServerProcessLauncher? launcher = null;
        Uri address;
        string? token;

        if (options.BaseAddress is not null)
        {
            address = options.BaseAddress;
            token = string.IsNullOrWhiteSpace(options.Token) ? null : options.Token.Trim();
        }
        else if (options.AutoLaunch)
        {
            launcher = new ServerProcessLauncher(options);
            try
            {
                address = await launcher.StartAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await launcher.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            token = launcher.Token;
        }
        else
        {
            throw new AgentServerException(
                503, "未指定 BaseAddress 且 AutoLaunch=false：没有可连接的服务地址");
        }

        AgentServerClient? client = null;
        try
        {
            client = new AgentServerClient(address, token);
            await client.GetHealthAsync(ct).ConfigureAwait(false);

            var sessions = new HttpSessionManager(client);
            var workspaces = new HttpWorkspaceManager(client, sessions);
            await workspaces.RefreshAsync(ct).ConfigureAwait(false);
            // 活动会话在服务端：连接时就同步一次镜像，避免界面在首次切换前读到空值
            await sessions.RefreshCurrentAsync(ct).ConfigureAwait(false);

            return new AgentClientConnection(client, sessions, workspaces, launcher);
        }
        catch
        {
            client?.Dispose();
            if (launcher is not null)
            {
                await launcher.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }
}