/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Services.Management.Models
*文件名： WikiSearchScope
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：LLM Wiki 检索范围（对应 <c>/llmwiki -search</c> 的三个开关）。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：LLM Wiki 检索范围（对应 <c>/llmwiki -search</c> 的三个开关）。
*
*****************************************************************************/

namespace LubanAgentCore.Services.Management.Models;

/// <summary>LLM Wiki 检索范围（对应 <c>/llmwiki -search</c> 的三个开关）。</summary>
public enum WikiSearchScope
{
    /// <summary>wiki 优先并回落原始资料（默认，无开关）。</summary>
    WikiThenRaw,

    /// <summary>仅 wiki（<c>--wiki</c>）。</summary>
    WikiOnly,

    /// <summary>仅原始资料（<c>--raw</c>）。</summary>
    RawOnly,
}