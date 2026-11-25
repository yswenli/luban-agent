/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： ContextBudget
*描述：会话上下文预算解析结果——选中档位与留白后的实际触发阈值
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 会话上下文预算的解析结果。
/// </summary>
/// <remarks>
/// 两者不能互相推导：<see cref="Selected"/> 是用户在菜单里挑的档位（也是存档值），
/// <see cref="Threshold"/> 还要被窗口留白压低（见 <see cref="CompactionOptions.ResolveThreshold"/>），
/// 大档位可能一并压到同一个阈值上，因此勾选状态必须用前者、提示文案用后者。
/// </remarks>
/// <param name="Selected">选中档位（token，未过窗口留白）。</param>
/// <param name="Threshold">实际触发压缩的阈值（token，已按窗口留白收敛）。</param>
public readonly record struct ContextBudget(int Selected, int Threshold);