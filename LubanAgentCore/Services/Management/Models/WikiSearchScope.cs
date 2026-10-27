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