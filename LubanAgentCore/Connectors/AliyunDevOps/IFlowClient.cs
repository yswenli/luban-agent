/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： IFlowClient
*描述：云效流水线（Flow）客户端接口（供工作台服务单测替换，FlowClient 实现之）
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效流水线（Flow）客户端接口。
/// </summary>
/// <remarks>
/// 存在理由（同 <see cref="IProjexClient"/> 先例）：<see cref="FlowClient"/> 的方法非 virtual，
/// S2 工作台服务（项目初始化）的单测无法替换它；提取本接口作测试缝合点。
/// 生产注册兼容：仍按 <see cref="FlowClient"/> 具体类注册，
/// <c>TryAddSingleton&lt;IFlowClient&gt;</c> 桥接到同一实例。公开面与 <see cref="FlowClient"/> 逐签名一致。
/// </remarks>
public interface IFlowClient
{
    /// <summary>列出流水线。</summary>
    /// <param name="pipelineName">流水线名检索（可选）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数。</param>
    /// <param name="statusList">状态过滤（可选）。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流水线列表；响应体为空时为 <c>null</c>。</returns>
    Task<List<YunxiaoPipeline>?> ListPipelinesAsync(
        string? pipelineName = null,
        int page = 1,
        int perPage = 20,
        string? statusList = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>获取流水线详情。</summary>
    /// <param name="pipelineId">流水线 ID。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流水线详情；响应体为空时为 <c>null</c>。</returns>
    Task<YunxiaoPipeline?> GetPipelineAsync(
        string pipelineId,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>创建流水线（YAML 内容建线）。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新流水线 ID；无法解析时为 <c>null</c>。</returns>
    Task<string?> CreatePipelineAsync(
        YunxiaoCreatePipelineRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>更新流水线（不抛异常即视为成功）。</summary>
    /// <param name="pipelineId">流水线 ID。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否成功。</returns>
    Task<bool> UpdatePipelineAsync(
        string pipelineId,
        YunxiaoUpdatePipelineRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>运行流水线。</summary>
    /// <param name="pipelineId">流水线 ID。</param>
    /// <param name="request">运行请求（可选）。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果；响应体为空时为 <c>null</c>。</returns>
    Task<YunxiaoPipelineRun?> RunPipelineAsync(
        string pipelineId,
        YunxiaoRunPipelineRequest? request = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>执行流水线人工卡点任务（继续 / 终止）。</summary>
    /// <param name="pipelineId">流水线 ID。</param>
    /// <param name="pipelineRunId">运行实例 ID。</param>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否执行成功。</returns>
    Task<bool> ExecutePipelineJobRunAsync(
        string pipelineId,
        string pipelineRunId,
        string jobId,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>列出主机组。</summary>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>主机组列表；响应体为空时为 <c>null</c>。</returns>
    Task<List<YunxiaoHostGroup>?> ListHostGroupsAsync(
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>获取主机组详情（含 <c>hostInfos</c>）。</summary>
    /// <param name="hostGroupId">主机组 ID（数字）。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>主机组详情；响应体为空时为 <c>null</c>。</returns>
    Task<YunxiaoHostGroup?> GetHostGroupAsync(
        long hostGroupId,
        string? organizationId = null,
        CancellationToken cancellationToken = default);
}
