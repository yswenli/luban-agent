/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： ConnectorTestResult
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：连接器探测结果。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：连接器探测结果。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management.Models;

/// <summary>连接器探测结果。</summary>
/// <param name="Success">凭据与端点是否可用。</param>
/// <param name="McpServerCount">连接器暴露的 MCP 服务器数量。</param>
/// <param name="Message">结果消息（失败时为原因）。</param>
public sealed record ConnectorTestResult(bool Success, int McpServerCount, string Message);