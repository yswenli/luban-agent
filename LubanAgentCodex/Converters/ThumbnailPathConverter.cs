/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Converters
*文件名： ThumbnailPathConverter
*版本号： V1.0.0.0
*唯一标识：附件缩略图路径转换器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/18
*描述：把附件缩略图本地路径转换为 Avalonia 位图，供 AttachmentBar / 消息区渲染。
*      按路径缓存，避免列表刷新时重复解码同一文件。
*
*****************************************************************************/
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System.Collections.Concurrent;
using System.Globalization;

namespace LubanAgentCodex.Converters;

/// <summary>
/// 本地图片路径 → <see cref="Bitmap"/>。路径为空或文件不存在时返回 null。
/// </summary>
public sealed class ThumbnailPathConverter : IValueConverter
{
    /// <summary>共享实例，供 XAML 静态引用。</summary>
    public static readonly ThumbnailPathConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Cache.TryGetValue(path, out var cached))
            return cached;

        try
        {
            if (!File.Exists(path))
                return null;
            var bitmap = new Bitmap(path);
            Cache[path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}