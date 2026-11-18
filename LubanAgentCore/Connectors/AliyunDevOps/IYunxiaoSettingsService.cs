/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： IYunxiaoSettingsService
*描述：云效连接配置读写门面接口（公开面与 YunxiaoSettingsService 一字不差，供单测替换）
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效连接配置读写门面接口 —— 公开面与 <see cref="YunxiaoSettingsService"/> 一字不差。
/// </summary>
/// <remarks>
/// 存在理由：<see cref="YunxiaoSettingsService"/> 是具体类且方法非 virtual，工作台服务
/// （<c>YunxiaoWorkbenchService</c>）的单测无法替换它；提取本接口作测试缝合点。
/// 生产注册兼容：既有消费方（Codex 设置页、<c>Program.cs</c>）仍按具体类注册不变，
/// 容器以 <c>TryAddSingleton&lt;IYunxiaoSettingsService&gt;</c> 桥接到同一实例。
/// </remarks>
public interface IYunxiaoSettingsService
{
    /// <summary>
    /// 读取当前生效的组织 ID（优先运行时选项，其次凭据存储）。
    /// </summary>
    string GetOrganizationId();

    /// <summary>
    /// 判断是否已配置 PAT（不回显明文）。
    /// </summary>
    bool HasPat();

    /// <summary>
    /// 保存云效连接配置。
    /// </summary>
    /// <param name="organizationId">组织 ID；空字符串表示清空。</param>
    /// <param name="pat">PAT；<c>null</c> 表示<b>保持原值不变</b>（设置页留空语义）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveAsync(
        string? organizationId,
        string? pat = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把持久化的组织 ID 回填到运行时选项（应用启动、构建容器后调用一次）。
    /// </summary>
    void ApplyPersistedOrganizationId();
}
