/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： YunxiaoOrganizationIdUnitTest
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/23
*描述：云效组织 ID 解析口径回归——显式空白必须回落连接器配置
*
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgent.Tests;

[TestClass]
public class YunxiaoOrganizationIdUnitTest
{
    /// <summary>
    /// 真机故障回归（2026-10-23 Step 3）：助手把「不知道的组织 ID」写成空串显式传给 MCP，
    /// 旧口径 <c>organizationId ?? options.OrganizationId</c> 让空串绕过兜底，最终抛「未配置组织 ID」。
    /// </summary>
    [TestMethod]
    public void Resolve_显式空白视为未提供并回落配置()
    {
        Assert.AreEqual("cfg-org", YunxiaoOrganizationId.Resolve(null, "cfg-org"));
        Assert.AreEqual("cfg-org", YunxiaoOrganizationId.Resolve(string.Empty, "cfg-org"));
        Assert.AreEqual("cfg-org", YunxiaoOrganizationId.Resolve("   ", "cfg-org"));
        Assert.AreEqual("cfg-org", YunxiaoOrganizationId.Resolve("\t", "cfg-org"));
    }

    /// <summary>显式给了值就以显式值为准（配置只在「未提供」时兜底）。</summary>
    [TestMethod]
    public void Resolve_显式有值时优先于配置()
    {
        Assert.AreEqual("explicit-org", YunxiaoOrganizationId.Resolve("explicit-org", "cfg-org"));
    }

    /// <summary>两处都没有值时保持为空——由调用方抛「未配置组织 ID」，不要伪造默认值。</summary>
    [TestMethod]
    public void Resolve_都没有值时返回空()
    {
        Assert.IsNull(YunxiaoOrganizationId.Resolve(null, null));
        Assert.AreEqual(string.Empty, YunxiaoOrganizationId.Resolve("  ", string.Empty));
        Assert.IsNull(YunxiaoOrganizationId.Resolve("  ", null));
    }
}