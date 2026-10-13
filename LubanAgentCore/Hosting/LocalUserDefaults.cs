/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： LocalUserDefaults
*版本号： V1.0.0.0
*创建时间：2026/10/13
*描述：本地单用户宿主的默认身份约定
*
*****************************************************************************/
namespace LubanAgentCore.Hosting;

/// <summary>本地单用户宿主的默认身份约定。</summary>
public static class LocalUserDefaults
{
    /// <summary>
    /// 默认本地用户 ID。可用环境变量 <c>LUBAN_USER_ID</c> 覆盖，缺省 <c>yswenli</c>。
    /// </summary>
    public static string DefaultUserId
    {
        get
        {
            var v = Environment.GetEnvironmentVariable("LUBAN_USER_ID");
            return string.IsNullOrWhiteSpace(v) ? "yswenli" : v.Trim();
        }
    }

    /// <summary>历史单用户数据使用的归属值（迁移时归一化到 <see cref="DefaultUserId"/>）。</summary>
    public const string LegacyUserId = "default";
}