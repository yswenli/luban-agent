/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： ICurrentSessionNotifier
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/4
*描述：当前活动会话变更通知（本地实现与远程 HTTP 实现共用）
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 当前活动会话变更通知。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LuBan.AIAgent.Sessions.ISessionManager"/> 只暴露 <c>CurrentSession</c>，没有变更事件；
/// 界面层需要「会话被创建 / 切换 / 由服务端回填」后重绑视图，故单列本接口。
/// </para>
/// <para>
/// 本地实现 <see cref="SessionManager"/> 与远程实现 <c>LubanAgentCore.Client.HttpSessionManager</c>
/// 都实现本接口，调用方按接口订阅即可，不必写死具体类型（远程模式下容器里注入的是 HTTP 实现）。
/// </para>
/// </remarks>
public interface ICurrentSessionNotifier
{
    /// <summary>当前活动会话变更（参数为新的会话标识）。</summary>
    event Action<string>? CurrentSessionChanged;
}