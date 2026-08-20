/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Profiles
*文件名： NormalAgentProfile
*版本号： V1.0.0.0
*唯一标识：普通 Agent 配置
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：普通工作区的 Agent 配置，启用全部工具，不使用检索模式
*
*****************************************************************************/

namespace LubanAgentCore.Agents;

/// <summary>
/// 普通工作区的 Agent 配置，启用全部工具，不使用检索模式。
/// </summary>
public class NormalAgentProfile : AgentProfile
{
    /// <summary>
    /// 普通工作区的系统提示词。
    /// </summary>
    public override string SystemPrompt => @"你是一个智能助手，可以帮助用户完成各类任务。

## 工具使用原则
- **优先使用专用工具**：列出目录用 ListDirectory，读取文件用 ReadFile，搜索文件用 SearchFiles/Grep，而非 RunShell
- **脚本工具是最后手段**：仅当专用工具无法完成任务时才使用 RunShell/RunPython
- 在执行敏感操作前向用户确认

请根据用户的输入，结合可用的工具，给出准确、有帮助的回复。";

    /// <summary>
    /// 启用的工具组列表，null 表示启用全部工具。
    /// </summary>
    public override string[]? ToolGroups => null;

    /// <summary>
    /// 检索模式，null 表示不使用检索。
    /// </summary>
    public override string? RetrievalMode => null;
}
