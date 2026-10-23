/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Serialization
*文件名： PluginJsonContext
*创建时间：2026/10/16
*描述：插件侧 JSON 序列化上下文（源生成）。插件持久化的所有类型必须登记在此。
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Configuration;

namespace LubanAgent.Plugin.NewProject.Serialization;

/// <summary>
/// 插件侧 JSON 序列化上下文（<c>System.Text.Json</c> 源生成）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须走源生成（硬约束）</b>：插件的 JSON 读写<b>不得</b>使用反射式
/// <c>JsonSerializer.Deserialize&lt;T&gt;</c> / <c>Serialize&lt;T&gt;</c> —— 反射路径会在**默认 ALC**
/// 里以「插件类型」为键缓存反射元数据（<c>JsonTypeInfo</c> / 成员访问委托），从而永久钉住插件 ALC，
/// 使第 ⑦ 步的 GC 弱引用验证必然失败（降级 <c>RequiresRestart</c>），M1 的「卸载即真回收」保证被破坏。
/// </para>
/// <para>
/// 源生成把 <c>JsonTypeInfo</c> 建在**插件自身程序集**内，不触碰默认 ALC 的类型缓存（已实测：
/// 反射路径钉住 ALC、源生成与 DOM 读写均可回收）。同理，插件内也应避免自建
/// <c>JsonSerializerOptions</c>；读取用 <c>JsonDocument</c>、写入用 <c>Utf8JsonWriter</c> 亦可。
/// </para>
/// <para>
/// 新增需要持久化的模型时，**必须**在此登记 <see cref="JsonSerializableAttribute"/>，
/// 否则运行期会抛 <c>NotSupportedException</c>（源生成不会为未登记类型生成元数据）。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(NewProjectSettings))]
[JsonSerializable(typeof(ProjectSpec))]
[JsonSerializable(typeof(StepReport))]
// v0.3 扩 schema：结论文件新增的持久化类型必须逐个登记，否则运行期 NotSupportedException（C18）。
[JsonSerializable(typeof(StepReportArtifact))]
[JsonSerializable(typeof(ArtifactExtra))]
[JsonSerializable(typeof(GroupArtifactExtra))]
[JsonSerializable(typeof(RepositoryArtifactExtra))]
[JsonSerializable(typeof(PipelineArtifactExtra))]
[JsonSerializable(typeof(StepReportInput))]
[JsonSerializable(typeof(StepReportInputOption))]
[JsonSerializable(typeof(StepReportInputDetails))]
internal partial class PluginJsonContext : JsonSerializerContext
{
}