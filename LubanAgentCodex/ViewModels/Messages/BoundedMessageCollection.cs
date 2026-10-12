/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.ViewModels.Messages
*文件名： BoundedMessageCollection
*版本号： V1.0.0.0
*唯一标识：有界消息集合
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/12
*描述：有界消息集合：插入后若超过上限，从最旧端裁剪，避免会话消息流无界增长。
*
*****************************************************************************/
using System.Collections.ObjectModel;

namespace LubanAgentCodex.ViewModels.Messages;

/// <summary>
/// 有界消息集合：插入后若超过上限，从最旧端裁剪。<see cref="KeepPredicate"/> 命中时
/// （如正在流式写入的当前项、运行中的编排节点）不会被剪掉，防止运行态项被移出集合。
/// </summary>
public sealed class BoundedMessageCollection : ObservableCollection<MessageItemBase>
{
    private readonly int _maxCount;

    /// <summary>
    /// 创建有界集合。
    /// </summary>
    /// <param name="maxCount">集合容量上限（&lt;=0 表示不限制）。</param>
    public BoundedMessageCollection(int maxCount)
    {
        _maxCount = maxCount > 0 ? maxCount : int.MaxValue;
    }

    /// <summary>
    /// 裁剪保护判定：返回 <c>true</c> 的项不会随旧项被移除。
    /// </summary>
    public Func<MessageItemBase, bool>? KeepPredicate { get; set; }

    /// <inheritdoc />
    protected override void InsertItem(int index, MessageItemBase item)
    {
        base.InsertItem(index, item);
        Trim();
    }

    private void Trim()
    {
        while (Count > _maxCount)
        {
            // 移除最旧的未受保护项；若全部受保护则停止，避免移出运行态项。
            var victim = -1;
            for (var i = 0; i < Count; i++)
            {
                if (KeepPredicate?.Invoke(this[i]) != true)
                {
                    victim = i;
                    break;
                }
            }

            if (victim < 0)
                return;

            RemoveAt(victim);
        }
    }
}