namespace LubanAgentCore.Models;

/// <summary>
/// 正文中出现的「裸图片路径」命中项（未加 <c>@</c> 前缀）。
/// 真机缺陷：用户输入 <c>D:\...\Marcus.png 看看这个图里面有什么</c> 时，
/// 路径未进入附件通道，模型只能拿到文本路径并改用 ReadFile 读二进制。
/// </summary>
/// <param name="Path">归一化后的绝对路径。</param>
/// <param name="Text">命中片段原文（已去首尾空白与尾随标点）。</param>
/// <param name="Index">命中片段在正文中的起始位置。</param>
/// <param name="Length">命中片段长度，用于从正文剥离。</param>
public record BareImageMention(string Path, string Text, int Index, int Length);