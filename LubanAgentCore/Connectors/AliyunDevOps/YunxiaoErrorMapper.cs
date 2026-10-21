/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： YunxiaoErrorMapper
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效 API 错误码映射
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效 API 错误码映射
/// </summary>
public static class YunxiaoErrorMapper
{
    /// <summary>
    /// 映射 HTTP 状态码到用户友好的错误信息
    /// </summary>
    /// <param name="statusCode">HTTP 状态码</param>
    /// <param name="errorMessage">原始错误信息</param>
    /// <returns>用户友好的错误信息</returns>
    public static string MapHttpStatus(int statusCode, string? errorMessage = null)
    {
        return statusCode switch
        {
            401 => "个人访问令牌无效或已过期，请检查 PAT 配置",
            403 => $"权限不足：{errorMessage ?? "缺少必要权限，请检查 PAT 权限范围"}",
            404 => $"资源不存在：{errorMessage ?? "请求的资源未找到"}",
            429 => "请求频率过高，已触发限流，请稍后重试",
            >= 500 => $"云效服务端错误（{statusCode}），请联系云效支持",
            _ => $"请求失败（{statusCode}）：{errorMessage}"
        };
    }

    /// <summary>
    /// 映射业务错误码到用户友好的错误信息
    /// </summary>
    /// <param name="errorCode">业务错误码</param>
    /// <param name="errorMessage">原始错误信息</param>
    /// <returns>用户友好的错误信息</returns>
    public static string MapBusinessError(int errorCode, string? errorMessage = null)
    {
        return errorCode switch
        {
            0 => string.Empty,
            400 => $"请求参数错误：{errorMessage}",
            401 => "个人访问令牌无效或已过期",
            403 => $"权限不足：{errorMessage}",
            404 => $"资源不存在：{errorMessage}",
            409 => $"资源冲突：{errorMessage}",
            429 => "请求频率过高，已触发限流",
            500 => "云效服务内部错误，请稍后重试",
            _ => $"云效 API 错误（{errorCode}）：{errorMessage}"
        };
    }

    /// <summary>
    /// 判断是否需要重试
    /// </summary>
    /// <param name="statusCode">HTTP 状态码</param>
    /// <returns>是否应该重试</returns>
    public static bool ShouldRetry(int statusCode)
    {
        // 429（限流）和 5xx 错误可以重试
        return statusCode == 429 || statusCode >= 500;
    }

    /// <summary>
    /// 获取建议的重试延迟（毫秒）
    /// </summary>
    /// <param name="statusCode">HTTP 状态码</param>
    /// <param name="retryAttempt">重试次数</param>
    /// <returns>建议的延迟毫秒数</returns>
    public static int GetRetryDelay(int statusCode, int retryAttempt)
    {
        // 指数退避：1s, 2s, 4s, 8s...
        var baseDelay = 1000 * (int)Math.Pow(2, retryAttempt);
        
        // 429 限流时额外增加延迟
        if (statusCode == 429)
        {
            baseDelay *= 2;
        }
        
        return baseDelay;
    }
}