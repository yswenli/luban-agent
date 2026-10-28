/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCli.App.Models
*文件名： AttachmentDraft
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/18
*描述：输入区待发送附件草稿。
*
*=================================================
*修改标记
*修改时间：2026/9/18
*修改人： yswenli
*版本号： V1.0.0.0
*描述：输入区待发送附件草稿。
*
*****************************************************************************/

namespace LubanAgentCli.App.Models;

/// <summary>输入区待发送附件草稿。</summary>
/// <param name="Path">文件绝对路径。</param>
/// <param name="FileName">文件名。</param>
public sealed record AttachmentDraft(string Path, string FileName);
