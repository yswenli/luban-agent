namespace LubanAgentCli.App.Models;

/// <summary>输入区待发送附件草稿。</summary>
/// <param name="Path">文件绝对路径。</param>
/// <param name="FileName">文件名。</param>
public sealed record AttachmentDraft(string Path, string FileName);
