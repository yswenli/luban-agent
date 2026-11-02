/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Hosting
*文件名： CoreSessionRuntimeInitializer
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/26
*描述：补齐产品特有的会话上下文字段：框架只认 <see cref="DefaultContextState"/>， 产品侧按用户隔离的服务读 <see cref="UserContextState"/>，故需把身份与工作区同步…
*
*=================================================
*修改标记
*修改时间：2026/10/26
*修改人： yswenli
*版本号： V1.0.0.0
*描述：补齐产品特有的会话上下文字段：框架只认 <see cref="DefaultContextState"/>， 产品侧按用户隔离的服务读 <see cref="UserContextState"/>，故需把身份与工作区同步…
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 补齐产品特有的会话上下文字段：框架只认 <see cref="DefaultContextState"/>，
/// 产品侧按用户隔离的服务读 <see cref="UserContextState"/>，故需把身份与工作区同步过去。
/// </summary>
public sealed class CoreSessionRuntimeInitializer(
    DefaultContextState state,
    DiContainer services) : ISessionRuntimeInitializer
{
    /// <inheritdoc />
    public Task InitializeAsync(SessionRuntimeSeed seed, CancellationToken ct = default)
    {
        state.WorkspaceId = seed.WorkspaceId;

        var productState = services.GetService<UserContextState>();
        if (productState is not null && !string.IsNullOrWhiteSpace(state.UserId))
            productState.UserId = state.UserId;

        return Task.CompletedTask;
    }
}