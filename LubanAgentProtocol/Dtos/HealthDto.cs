/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Protocol.Dtos
*文件名： HealthDto
*描述：健康检查载荷（客户端启动探测与就绪轮询共用）
*
*****************************************************************************/

namespace LubanAgent.Protocol.Dtos;

/// <summary>健康检查结果。</summary>
/// <param name="Status">状态，固定 <c>ok</c>。</param>
/// <param name="Version">宿主版本号。</param>
/// <param name="UserId">宿主解析出的用户标识。</param>
/// <param name="ServerTime">服务端时间。</param>
public sealed record HealthDto(
    string Status,
    string? Version,
    string? UserId,
    DateTime ServerTime);