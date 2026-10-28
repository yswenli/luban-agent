/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： SystemMessageView
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：系统消息显示控件
*
*****************************************************************************/

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 系统消息视图
/// </summary>
public partial class SystemMessageView : UserControl
{
    /// <summary>
    /// 初始化系统消息视图并加载 XAML 定义，用于展示系统提示、命令输出与错误信息。
    /// </summary>
    public SystemMessageView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
