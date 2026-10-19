/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AliyunCredentialKeyAliasesUnitTest
*描述：验证云效凭据键别名表（历史拼写 → 规范键、历史环境变量名），
*      确保框架去厂商化后旧安装的凭据仍可被读到
*****************************************************************************/

using LubanAgent.Connectors.AliyunDevOps;
using LuBan.AIAgent.Plugins.Security;

namespace LubanAgent.Tests;

[TestClass]
public class AliyunCredentialKeyAliasesUnitTest
{
    private static ICredentialKeyAliases Aliases => AliyunCredentialKeyAliases.Instance;

    [TestMethod]
    public void CanonicalKeys_UseConnectorConvention()
    {
        Assert.AreEqual("connector:yunxiao:token", AliyunCredentialKeyAliases.DefaultTokenKey);
        Assert.AreEqual("connector:yunxiao:organizationid", AliyunCredentialKeyAliases.DefaultOrganizationIdKey);
    }

    [DataTestMethod]
    [DataRow("yunxiao_pat")]
    [DataRow("yunxiao:pat")]
    [DataRow("yunxiao_token")]
    [DataRow("codeup_pat")]
    [DataRow("codeup:pat")]
    [DataRow("codeup:token")]
    [DataRow("YUNXIAO_PAT")]
    [DataRow("  yunxiao_pat  ")]
    public void Resolve_TokenSpellings_MapToCanonicalTokenKey(string legacy)
    {
        Assert.AreEqual(AliyunCredentialKeyAliases.DefaultTokenKey, Aliases.Resolve(legacy));
    }

    [DataTestMethod]
    [DataRow("yunxiao_organization_id")]
    [DataRow("yunxiao_org_id")]
    [DataRow("yunxiao:org")]
    [DataRow("codeup_org_id")]
    [DataRow("codeup:org")]
    [DataRow("YUNXIAO_ORGANIZATION_ID")]
    public void Resolve_OrganizationSpellings_MapToCanonicalOrganizationKey(string legacy)
    {
        Assert.AreEqual(AliyunCredentialKeyAliases.DefaultOrganizationIdKey, Aliases.Resolve(legacy));
    }

    [TestMethod]
    public void Resolve_CanonicalKey_IsIdempotent()
    {
        Assert.AreEqual(AliyunCredentialKeyAliases.DefaultTokenKey, Aliases.Resolve(AliyunCredentialKeyAliases.DefaultTokenKey));
        Assert.AreEqual(AliyunCredentialKeyAliases.DefaultOrganizationIdKey, Aliases.Resolve(AliyunCredentialKeyAliases.DefaultOrganizationIdKey));
    }

    [TestMethod]
    public void Resolve_UnknownKey_FallsBackToNormalize()
    {
        Assert.AreEqual("connector:other:token", Aliases.Resolve("connector:Other:Token"));
        Assert.AreEqual(string.Empty, Aliases.Resolve("   "));
    }

    [TestMethod]
    public void GetLegacyAliases_ReturnsHistory_ForCanonicalKeysOnly()
    {
        var tokenAliases = Aliases.GetLegacyAliases(AliyunCredentialKeyAliases.DefaultTokenKey);
        CollectionAssert.Contains(tokenAliases.ToList(), "yunxiao_pat");
        CollectionAssert.Contains(tokenAliases.ToList(), "codeup:pat");

        var orgAliases = Aliases.GetLegacyAliases(AliyunCredentialKeyAliases.DefaultOrganizationIdKey);
        CollectionAssert.Contains(orgAliases.ToList(), "yunxiao_organization_id");

        Assert.AreEqual(0, Aliases.GetLegacyAliases("connector:other:token").Count);
    }

    [TestMethod]
    public void GetLegacyEnvironmentVariableName_ReturnsHistoricalVariableNames()
    {
        Assert.AreEqual("LUBAN_YUNXIAO_PAT", Aliases.GetLegacyEnvironmentVariableName(AliyunCredentialKeyAliases.DefaultTokenKey));
        Assert.AreEqual("LUBAN_YUNXIAO_ORGANIZATION_ID", Aliases.GetLegacyEnvironmentVariableName(AliyunCredentialKeyAliases.DefaultOrganizationIdKey));
        Assert.IsNull(Aliases.GetLegacyEnvironmentVariableName("connector:other:token"));
    }

    [TestMethod]
    public void GenericEnvironmentVariableName_FollowsFrameworkConvention()
    {
        Assert.AreEqual("LUBAN_CONNECTOR_YUNXIAO_TOKEN", CredentialKeys.GetEnvironmentVariableName(AliyunCredentialKeyAliases.DefaultTokenKey));
        Assert.AreEqual("LUBAN_CONNECTOR_YUNXIAO_ORGANIZATIONID", CredentialKeys.GetEnvironmentVariableName(AliyunCredentialKeyAliases.DefaultOrganizationIdKey));
    }
}