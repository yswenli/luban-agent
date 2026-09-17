/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： IAgentContextReloadTrigger
*版本号： V1.0.0.0
*唯一标识：Agent 上下文重载契约
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/17
*描述：宿主 Agent 上下文重载契约：工作区上下文文件（AGENTS.md）变更后，
*由命令层通知宿主在下一次对话前重建 Agent，重新读取工作区上下文
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 宿主 Agent 上下文重载契约。
/// 工作区 AGENTS.md 等上下文文件变更后，由命令层通知宿主重建 Agent，
/// 使新内容在下一次对话的系统提示词中生效。宿主运行中请求会被延迟到本轮结束后执行。
/// </summary>
public interface IAgentContextReloadTrigger
{
    /// <summary>
    /// 请求重载 Agent 上下文（重新读取工作区 AGENTS.md）。
    /// </summary>
    void RequestReload();
}