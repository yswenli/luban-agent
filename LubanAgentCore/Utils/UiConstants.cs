/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Utils
*文件名： UiConstants
*版本号： V1.0.0.0
*唯一标识：UI 共享常量
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/14
*描述：CLI / GUI 共用的展示与节流常量，避免两端取值分叉
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// CLI / GUI 共用的展示与节流常量。集中定义以避免两端出现语义分叉
/// （历史条数 20 vs 50、节流 50ms vs 100ms）。
/// </summary>
public static class UiConstants
{
    /// <summary>会话历史初始加载条数（两端统一）。</summary>
    public const int HistoryLoadLimit = 50;

    /// <summary>单会话消息流在内存中保留的最大条数（超出裁剪最旧项，数据库仍保留全量）。</summary>
    public const int MaxMessageCount = 50;

    /// <summary>流式正文刷新的节流间隔（毫秒）。</summary>
    public const int StreamFlushIntervalMs = 50;
}