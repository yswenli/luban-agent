/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： ModelSelectDialog
*版本号： V1.0.0.0
*唯一标识：模型选择对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/2
*描述：模型选择对话框，用于选择某个 Provider 的模型作为默认模型
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LubanAgentCodex.Views;

/// <summary>
/// 模型选择对话框
/// </summary>
public partial class ModelSelectDialog : Window
{
    private ListBox? _modelList;

    /// <summary>用户选中的模型名称</summary>
    public string? SelectedModel { get; private set; }

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）
    /// </summary>
    public ModelSelectDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 带参构造函数
    /// </summary>
    /// <param name="models">可选模型列表</param>
    /// <param name="currentModel">当前已选模型，用于标记"已选"</param>
    public ModelSelectDialog(IList<string> models, string? currentModel = null) : this()
    {
        if (_modelList != null)
        {
            _modelList.ItemsSource = models.Select(m =>
            {
                var isCurrent = currentModel != null && m == currentModel;
                return new ModelItem { Name = m, Display = isCurrent ? $"{m} (已选)" : m };
            }).ToList();
            _modelList.SelectionChanged += (s, e) =>
            {
                if (_modelList.SelectedItem is ModelItem item)
                    SelectedModel = item.Name;
            };
        }

        if (this.FindControl<Button>("OkButton") is { } okBtn)
            okBtn.Click += (_, _) =>
            {
                if (string.IsNullOrEmpty(SelectedModel)) return;
                Close(SelectedModel);
            };
        if (this.FindControl<Button>("CancelButton") is { } cancelBtn)
            cancelBtn.Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _modelList = this.FindControl<ListBox>("ModelList");
    }
}

/// <summary>
/// 模型列表项
/// </summary>
public class ModelItem
{
    /// <summary>模型名称</summary>
    public string Name { get; set; } = "";

    /// <summary>列表显示文本</summary>
    public string Display { get; set; } = "";
}
