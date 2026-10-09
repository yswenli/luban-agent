/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Runtime
*文件名： IA2ALifecycle
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A 运行时生命周期抽象，由宿主在工作区确定后显式驱动启动/停止
*
*****************************************************************************/

namespace LubanAgentA2A.Runtime;

/// <summary>
/// A2A 运行时生命周期。宿主（CLI / Codex）在工作区确定后调用 <see cref="StartAsync"/>，
/// 由实现解析工作区 <c>.luban-agent/a2a/*.json</c> 与 appsettings 的并集，注册本地 Agent、
/// 启动服务端角色并接入远端客户端角色。
/// <para>之所以不在 DI 注册期完成，是因为工作区只有在宿主切换当前工作区之后才确定。</para>
/// </summary>
public interface IA2ALifecycle
{
    /// <summary>
    /// 按指定工作区启动 A2A（解析有效配置并注册角色）。重复调用应幂等。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径；为空则不扫描工作区文件（仅按 appsettings 基线）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>启动任务。</returns>
    Task StartAsync(string? workspaceRoot, CancellationToken ct = default);

    /// <summary>停止 A2A（停服务端、释放远端客户端、清空运行时执行器）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>停止任务。</returns>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>是否已启动。</summary>
    bool IsRunning { get; }
}