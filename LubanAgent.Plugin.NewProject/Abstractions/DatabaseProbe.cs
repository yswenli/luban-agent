/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： DatabaseProbe
*创建时间：2026/10/21
*描述：Step 4「数据库探活」参数（PRD §4.3 Step 4，v0.22 口径）
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// Step 4「数据库探活」参数：服务器 / 端口 / 库名 / 账号 / 口令，全部在 Step 4 界面采集。
/// </summary>
/// <remarks>
/// <para>
/// <b>v0.22 口径（用户定案）</b>：本步<b>只探活</b>——不写任何项目文件、不改 <c>appsettings*.json</c>、
/// 不建库、不执行任何 SQL；判定只看「能连上且该库存在」，结论<b>不阻断</b>流程。
/// </para>
/// <para>
/// <b>口令只在内存</b>：本对象<b>不落档案</b>（<see cref="ProjectSpec.Database"/> 只记非敏感项）、
/// 不写日志、操作记录里一律以 <c>***</c> 呈现（PRD §4.8 #6/#7/#11）。
/// </para>
/// <para>
/// <b>库名与账号逐项目单独给</b>：不使用任何模板默认库名；库由用户手工创建，插件不承担建库职责。
/// </para>
/// </remarks>
public sealed class DatabaseProbeRequest
{
    /// <summary>数据库服务器（主机名或 IP）。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>端口（默认 MySQL 3306）。</summary>
    public int Port { get; set; } = 3306;

    /// <summary>库名（<b>必须已存在</b>，插件不建库）。</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>账号。</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>口令（只在内存；允许空口令以覆盖本机免密实例）。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 校验参数是否可用于探活。
    /// </summary>
    /// <param name="error">未通过时的可操作提示。</param>
    /// <returns>通过返回 <c>true</c>。</returns>
    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            error = "请填写数据库服务器（主机名或 IP）。";
            return false;
        }

        if (Port is < 1 or > 65535)
        {
            error = "端口必须在 1~65535 之间。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Database))
        {
            error = "请填写库名（库需由你先行手工创建，插件不建库）。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(User))
        {
            error = "请填写数据库账号。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 生成可安全记录的参数摘要（<b>口令一律脱敏</b>）。
    /// </summary>
    /// <returns>摘要文案。</returns>
    public string Describe()
        => $"{User}@{Host}:{Port}/{Database}（口令 ***）";
}