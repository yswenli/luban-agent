/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： ThinkingMessageView
*版本号： V1.0.0.0
*唯一标识：思考过程消息视图
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：AI 思考过程消息显示控件
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// AI 思考过程消息视图
/// </summary>
public partial class ThinkingMessageView : UserControl
{
    public ThinkingMessageView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
