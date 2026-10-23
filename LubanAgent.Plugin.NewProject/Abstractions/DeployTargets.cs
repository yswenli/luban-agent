/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： DeployTargets
*描述：Step 7「部署目标」（云效主机组）选项（v0.32）：创建流水线**之前**先把目标机器摊给用户选
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// 部署目标主机组（v0.32）：<b>创建流水线之前</b>供用户选择的部署落点。
/// </summary>
/// <remarks>
/// <para>
/// 来源＝云效只读主机组接口（<c>GET /flow/organizations/{orgId}/hostGroups</c> 列表 +
/// <c>GET …/hostGroups/{数字id}</c> 详情），由能力层转成插件侧 DTO，界面不碰连接器类型。
/// </para>
/// <para>
/// <b>写进 YAML 的是 <see cref="Uuid"/>，不是 <see cref="HostGroupId"/></b>：真机核 <c>VMDockerDeploy.machineGroup</c>
/// 只认主机组 uuid（tb 形态的数字 id 226948 会被拒，报「该机器组uuid[226948]不存在」）；
/// 数字 id 的用途只有一个——调详情接口拿机器明细（路径键必须是数字）。
/// </para>
/// </remarks>
public sealed class DeployHostGroup
{
    /// <summary>主机组数字 ID（详情接口的路径键；<b>不</b>进 YAML）。</summary>
    public long HostGroupId { get; set; }

    /// <summary>主机组 UUID（<b>写进 <c>machineGroup</c> 的就是它</b>）。</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>主机组名称（如「移动云DEV主机组」）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>机器台数（真机观测有 <c>0</c>：空组可用但无机器）。</summary>
    public int HostNum { get; set; }

    /// <summary>地域（如 <c>cn-shanghai</c>；真机观测可为 <c>null</c>）。</summary>
    public string? Region { get; set; }

    /// <summary>
    /// 机器明细（<b>惰性拉取</b>：列表接口不带，选中后在详情接口取，取到后缓存在此实例上）。
    /// </summary>
    public IReadOnlyList<DeployHostMachine> Machines { get; set; } = Array.Empty<DeployHostMachine>();

    /// <summary>下拉里的一行文案（名称 · 台数 · 地域；uuid 缺失时明确标出不可用）。</summary>
    public string DisplayLabel
    {
        get
        {
            var parts = new List<string>
            {
                string.IsNullOrWhiteSpace(Name) ? $"主机组 {HostGroupId}" : Name,
                $"{HostNum} 台"
            };

            if (!string.IsNullOrWhiteSpace(Region))
                parts.Add(Region!);

            if (string.IsNullOrWhiteSpace(Uuid))
                parts.Add("uuid 缺失（不可用于部署）");

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// 部署主机组里的单台机器（v0.32：让用户在创建流水线之前就看清「到底打哪台机器」）。
/// </summary>
/// <remarks>
/// 真机口径：字段来自主机组详情的 <c>hostInfos</c>（<c>instanceName</c> / <c>ip</c> / <c>status</c>）。
/// </remarks>
public sealed class DeployHostMachine
{
    /// <summary>实例名（真机如 <c>tjc-sh-100-dev-all-in-one</c>）。</summary>
    public string? InstanceName { get; set; }

    /// <summary>内网 IP（真机如 <c>10.10.0.214</c>）。</summary>
    public string? Ip { get; set; }

    /// <summary>机器状态（真机观测为 <c>ok</c>）。</summary>
    public string? Status { get; set; }

    /// <summary>明细里的一行文案。</summary>
    public string DisplayLabel
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(InstanceName) ? "（未命名实例）" : InstanceName!;
            var ip = string.IsNullOrWhiteSpace(Ip) ? "（无 IP）" : Ip!;
            var status = string.IsNullOrWhiteSpace(Status) ? "状态未知" : Status!;
            return $"{name} · {ip} · {status}";
        }
    }
}