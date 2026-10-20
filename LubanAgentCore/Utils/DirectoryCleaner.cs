/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Utils
*文件名： DirectoryCleaner
*创建时间：2026/10/20
*描述：目录强制删除——先递归清除只读属性再删，规避 Windows 上 git pack 只读属性挡删除的坑
*
*****************************************************************************/

namespace LubanAgentCore.Utils;

/// <summary>
/// 目录删除工具：删除前递归清除只读属性，并做有限次重试。
/// </summary>
/// <remarks>
/// <para>
/// <b>存在理由（真机缺陷 RC5）</b>：<c>git</c> 在 Windows 上会把
/// <c>.git/objects/pack/*.pack|.idx|.rev</c> 标为<b>只读</b>，
/// <see cref="Directory.Delete(string, bool)"/> 遇到只读文件必抛
/// <see cref="UnauthorizedAccessException"/>（文案即 “Access to the path '…idx' is denied.”）。
/// </para>
/// <para>
/// 更糟的是：它在抛错<b>前</b>已删掉了其余文件，会把目录留成「只剩只读文件」的残缺态——
/// 既删不干净，也无法再被 <c>git</c> 识别为工作树，于是「新建项目」Step 3 的三选一
/// （复用已重克隆 / 清空后重克隆）两条路同时失效，直接走进死路。
/// </para>
/// <para>
/// 故凡「整目录删除」一律走本方法：<b>先清除整棵树的只读属性，再删除</b>，
/// 从根上消除「部分删除 + 半成品残骸」的可能。
/// </para>
/// </remarks>
public static class DirectoryCleaner
{
    /// <summary>删除尝试次数（含首次）。</summary>
    private const int MaxAttempts = 3;

    /// <summary>重试间隔基数（第 n 次失败后等待 <c>n × 基数</c>，逐次退避）。</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 强制删除目录（含只读文件）；目录不存在时直接返回（幂等）。
    /// </summary>
    /// <param name="path">目标目录。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空。</exception>
    /// <exception cref="IOException">
    /// 清除只读属性后重试 <see cref="MaxAttempts"/> 次仍失败时抛出；
    /// 消息已带手工处置命令，可直接作为操作记录 / 失败文案呈现给用户。
    /// </exception>
    public static void DeleteForce(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("目录路径不能为空。", nameof(path));

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // 目录已不存在即视为「已删除」（上一次尝试可能刚好删完最后一项）
            if (!Directory.Exists(path))
                return;

            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt < MaxAttempts)
                    Thread.Sleep(RetryDelay * attempt);
            }
        }

        throw new IOException(
            $"无法删除目录：{path} —— {lastError?.Message}"
            + $"（已清除只读属性并重试 {MaxAttempts} 次）。"
            + $"请先关闭可能占用该目录的程序后重试，或手工执行："
            + $"attrib -r /s /d \"{path}\"，然后 rd /s /q \"{path}\"",
            lastError);
    }

    /// <summary>
    /// 递归清除整棵目录树的只读属性（含隐藏项）。
    /// </summary>
    /// <param name="path">目标目录。</param>
    /// <remarks>
    /// <b>必须显式置空 <see cref="EnumerationOptions.AttributesToSkip"/></b>：
    /// 其默认值为 <c>Hidden | System</c>，而 <c>.git</c> 整棵树都是隐藏目录——按默认值枚举
    /// 会把要清理的 pack 文件全部跳过（这正是本缺陷最隐蔽的一环）。
    /// </remarks>
    private static void ClearReadOnlyAttributes(string path)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // 个别受限子项跳过，避免一处不可访问打断整次清理
            IgnoreInaccessible = true,
            // 默认 Hidden | System 会跳过 .git 整棵树，必须置空（见 remarks）
            AttributesToSkip = FileAttributes.None,
            ReturnSpecialDirectories = false,
        };

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
                TryClearReadOnly(entry);

            // 目录自身也可能带只读属性
            TryClearReadOnly(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 此处不报错：交给 Directory.Delete 暴露真实失败原因，再由 DeleteForce 统一包装成可读文案
            Logger.Warn($"[DirectoryCleaner] 清除只读属性失败（仍继续尝试删除）：{path}", ex);
        }
    }

    /// <summary>清除单个条目（文件或目录）的只读属性。</summary>
    /// <param name="entry">条目路径。</param>
    private static void TryClearReadOnly(string entry)
    {
        try
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn($"[DirectoryCleaner] 清除只读属性失败：{entry}", ex);
        }
    }
}