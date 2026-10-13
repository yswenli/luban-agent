/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Converters
*文件名： FileSizeConverter
*版本号： V1.0.0.0
*唯一标识：附件文件大小格式化转换器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/18
*描述：把字节数格式化为 B / KB / MB 文本，供附件预览条显示。
*
*****************************************************************************/
using LuBan.Common.IO;
using Avalonia.Data.Converters;
using System.Globalization;

namespace LubanAgentCodex.Converters;

/// <summary>
/// 字节数 → 人类可读大小文本。
/// </summary>
public sealed class FileSizeConverter : IValueConverter
{
    /// <summary>共享实例，供 XAML 静态引用。</summary>
    public static readonly FileSizeConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 非 long 返回空串（保留原契约）；负值由 FormatFileSize 归一为 "0B"。
        if (value is not long size)
            return string.Empty;

        return FileUtil.FormatFileSize(size);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}