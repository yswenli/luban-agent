/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： WikiFileInfo
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：已索引文件条目（<c>/llmwiki -list</c> 的一行）。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：已索引文件条目（<c>/llmwiki -list</c> 的一行）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management.Models;

/// <summary>已索引文件条目（<c>/llmwiki -list</c> 的一行）。</summary>
/// <param name="FilePath">文件路径（索引时的原始路径）。</param>
/// <param name="Language">语言标识；未知为 null。</param>
/// <param name="ChunkCount">切块数。</param>
/// <param name="IndexedTime">索引时间。</param>
public sealed record WikiFileInfo(
    string FilePath,
    string? Language,
    int ChunkCount,
    DateTime IndexedTime);