/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： NamingDeriverUnitTest
*描述：云效 S2 项目初始化命名派生纯函数单测（NamingDeriver）——
*      代号解析（首个 - 前段）/ 代号前缀替换改名 / 流水线分组前缀解析（首个 _DEV_Deploy_ 之前）/
*      三段派生（仓库名前缀替换 + 子目录按需派生 + 流水线名只替换代号段）
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.1 + §11.1
*夹具：一律合成名（TPLX-Mixed / TPLX-Service / ACME_DEV_Deploy_DEMO-Mobile / NEWX），禁止组织专有真值
*****************************************************************************/

using LubanAgentWebHost.Services.Init;

namespace LubanAgent.Tests;

/// <summary>
/// <see cref="NamingDeriver"/> 纯函数契约单测：把「代号解析 / 前缀替换改名 / 前缀解析 / 三段派生」的口径钉死。
/// </summary>
/// <remarks>
/// 这是 S2 项目初始化的命名地基（设计 §4.1 + S2b §11.1，TDD 重灾区）：
/// 模板名首个 <c>-</c> 前段为代号（<c>TPLX-Mixed</c>/<c>TPLX-Service</c> 均得 <c>TPLX</c>），
/// 新名按代号前缀替换（<c>TPLX-Mixed → NEWX-Mixed</c>、<c>TPLX-Service → NEWX-Service</c>）；
/// 母版流水线名 <c>{prefix}_DEV_Deploy_{code}-{end}</c> 解析出分组前缀，
/// 流水线派生<b>只替换代号段</b>（前缀与端后缀原样保留）；三子目录仅综合库场景派生。
/// 夹具全部为合成名，杜绝组织专有真值。
/// </remarks>
[TestClass]
public class NamingDeriverUnitTest
{
    // ── ParseTemplateCode：首个 '-' 之前的段（S2b §11.1）────────────────────

    [TestMethod]
    public void ParseTemplateCode_WithMixedSuffix_StripsSuffixAndReturnsCode()
    {
        Assert.AreEqual("TPLX", NamingDeriver.ParseTemplateCode("TPLX-Mixed"));
    }

    [TestMethod]
    public void ParseTemplateCode_WithSingleEndSuffix_ReturnsCodeBeforeFirstDash()
    {
        Assert.AreEqual("TPLX", NamingDeriver.ParseTemplateCode("TPLX-Service"));
        Assert.AreEqual("TPLX", NamingDeriver.ParseTemplateCode("TPLX-Admin"));
        Assert.AreEqual("TPLX", NamingDeriver.ParseTemplateCode("TPLX-Repo"));
    }

    [TestMethod]
    public void ParseTemplateCode_WithoutDash_ReturnsNull()
    {
        Assert.IsNull(NamingDeriver.ParseTemplateCode("TPLX"));
        Assert.IsNull(NamingDeriver.ParseTemplateCode("NOTMIXED"));
    }

    [TestMethod]
    public void ParseTemplateCode_BlankOrNullInput_ReturnsNull()
    {
        Assert.IsNull(NamingDeriver.ParseTemplateCode(null!));
        Assert.IsNull(NamingDeriver.ParseTemplateCode(""));
        Assert.IsNull(NamingDeriver.ParseTemplateCode("   "));
    }

    [TestMethod]
    public void ParseTemplateCode_EmptySegmentBeforeDash_ReturnsNull()
    {
        Assert.IsNull(NamingDeriver.ParseTemplateCode("-Mixed"));
    }

    // ── RenameByCode：代号前缀替换（S2b §11.1）──────────────────────────────

    [TestMethod]
    public void RenameByCode_MixedTemplate_ReplacesCodePrefixKeepsSuffix()
    {
        Assert.AreEqual("NEWX-Mixed", NamingDeriver.RenameByCode("TPLX-Mixed", "TPLX", "NEWX"));
    }

    [TestMethod]
    public void RenameByCode_SingleEndTemplate_ReplacesCodePrefixKeepsSuffix()
    {
        Assert.AreEqual("NEWX-Service", NamingDeriver.RenameByCode("TPLX-Service", "TPLX", "NEWX"));
        Assert.AreEqual("NEWX-Admin", NamingDeriver.RenameByCode("TPLX-Admin", "TPLX", "NEWX"));
    }

    [TestMethod]
    public void RenameByCode_PrefixMismatch_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => NamingDeriver.RenameByCode("OTHER-Mixed", "TPLX", "NEWX"));
        Assert.ThrowsExactly<ArgumentException>(
            () => NamingDeriver.RenameByCode("TPLX", "TPLX", "NEWX"));
        Assert.ThrowsExactly<ArgumentException>(
            () => NamingDeriver.RenameByCode("TPLX-", "TPLX", "NEWX"));
        Assert.ThrowsExactly<ArgumentException>(
            () => NamingDeriver.RenameByCode("", "TPLX", "NEWX"));
    }

    [TestMethod]
    public void RenameByCode_InvalidCode_ThrowsArgumentExceptionWithParamName()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => NamingDeriver.RenameByCode("TPLX-Mixed", "TP-LX", "NEWX"));
        Assert.AreEqual("templateCode", ex.ParamName);

        ex = Assert.ThrowsExactly<ArgumentException>(
            () => NamingDeriver.RenameByCode("TPLX-Mixed", "TPLX", "1NEWX"));
        Assert.AreEqual("newCode", ex.ParamName);
    }

    // ── ParsePipelinePrefix：首个 _DEV_Deploy_ 之前的部分 ───────────────────

    [TestMethod]
    public void ParsePipelinePrefix_WithDeploySegment_ReturnsPrefixBeforeFirstMarker()
    {
        Assert.AreEqual("ACME", NamingDeriver.ParsePipelinePrefix("ACME_DEV_Deploy_DEMO-Mobile"));
    }

    [TestMethod]
    public void ParsePipelinePrefix_WithoutDeploySegment_ReturnsNull()
    {
        Assert.IsNull(NamingDeriver.ParsePipelinePrefix("ACME_Deploy_DEMO-Mobile"));
        Assert.IsNull(NamingDeriver.ParsePipelinePrefix("TPLX-Mobile"));
    }

    [TestMethod]
    public void ParsePipelinePrefix_BlankOrNullInput_ReturnsNull()
    {
        Assert.IsNull(NamingDeriver.ParsePipelinePrefix(null!));
        Assert.IsNull(NamingDeriver.ParsePipelinePrefix(""));
        Assert.IsNull(NamingDeriver.ParsePipelinePrefix("   "));
    }

    // ── Derive：三段派生默认值 ─────────────────────────────────────────────

    [TestMethod]
    public void Derive_WithoutPipelines_ReturnsRepoNameAndThreeDirPairs()
    {
        var plan = NamingDeriver.Derive("TPLX", "NEWX", Array.Empty<string>());

        Assert.AreEqual("NEWX-Mixed", plan.RepoName);

        Assert.AreEqual(3, plan.Dirs.Count);
        Assert.AreEqual("TPLX-Admin", plan.Dirs[0].From);
        Assert.AreEqual("NEWX-Admin", plan.Dirs[0].To);
        Assert.AreEqual("TPLX-Mobile", plan.Dirs[1].From);
        Assert.AreEqual("NEWX-Mobile", plan.Dirs[1].To);
        Assert.AreEqual("TPLX-Service", plan.Dirs[2].From);
        Assert.AreEqual("NEWX-Service", plan.Dirs[2].To);
    }

    [TestMethod]
    public void Derive_EmptyPipelineList_YieldsEmptyPipelinesWithoutThrowing()
    {
        var plan = NamingDeriver.Derive("TPLX", "NEWX", new List<string>());

        Assert.IsNotNull(plan.Pipelines);
        Assert.AreEqual(0, plan.Pipelines.Count);
    }

    [TestMethod]
    public void Derive_IncludeDirsFalse_YieldsEmptyDirs()
    {
        var plan = NamingDeriver.Derive("TPLX", "NEWX", Array.Empty<string>(), includeDirs: false);

        Assert.AreEqual("NEWX-Mixed", plan.RepoName);
        Assert.AreEqual(0, plan.Dirs.Count);
    }

    [TestMethod]
    public void Derive_RepoName_FollowsRenameByCodeSemantics()
    {
        // 综合库形态：{templateCode}-Mixed → {newCode}-Mixed（与 RenameByCode 口径一致）
        Assert.AreEqual(
            NamingDeriver.RenameByCode("TPLX-Mixed", "TPLX", "NEWX"),
            NamingDeriver.Derive("TPLX", "NEWX", Array.Empty<string>(), includeDirs: false).RepoName);
    }

    // ── Derive：流水线名只替换代号段（前缀与端后缀保留） ───────────────────

    [TestMethod]
    public void Derive_Pipelines_ReplaceOnlyCodeSegment_KeepPrefixAndEndSuffix()
    {
        var plan = NamingDeriver.Derive("TPLX", "NEWX", new[]
        {
            "ACME_DEV_Deploy_TPLX-Mobile",
            "ACME_DEV_Deploy_TPLX-Admin",
            "ACME_DEV_Deploy_TPLX-Service",
        });

        Assert.AreEqual(3, plan.Pipelines.Count);

        Assert.AreEqual("ACME_DEV_Deploy_TPLX-Mobile", plan.Pipelines[0].From);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", plan.Pipelines[0].To);
        Assert.AreEqual("ACME_DEV_Deploy_TPLX-Admin", plan.Pipelines[1].From);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Admin", plan.Pipelines[1].To);
        Assert.AreEqual("ACME_DEV_Deploy_TPLX-Service", plan.Pipelines[2].From);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service", plan.Pipelines[2].To);
    }

    [TestMethod]
    public void Derive_PipelineWithoutDeploySegment_IsSkipped()
    {
        var plan = NamingDeriver.Derive("TPLX", "NEWX", new[]
        {
            "ACME_DEV_Deploy_TPLX-Mobile",
            "ACME-Deploy-TPLX-Service",
        });

        Assert.AreEqual(1, plan.Pipelines.Count);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", plan.Pipelines[0].To);
    }

    // ── Derive：代号合法性校验 ─────────────────────────────────────────────

    [TestMethod]
    public void Derive_InvalidNewCode_ThrowsArgumentExceptionWithParamName()
    {
        foreach (var bad in new[] { "", "  ", "1NEWX", "NEW-X", "NEW_X", "NEW X" })
        {
            var ex = Assert.ThrowsExactly<ArgumentException>(
                () => NamingDeriver.Derive("TPLX", bad, Array.Empty<string>()));
            Assert.AreEqual("newCode", ex.ParamName);
        }
    }

    [TestMethod]
    public void Derive_InvalidTemplateCode_ThrowsArgumentExceptionWithParamName()
    {
        foreach (var bad in new[] { "", "1TPLX", "TPL-X", "TPL_X" })
        {
            var ex = Assert.ThrowsExactly<ArgumentException>(
                () => NamingDeriver.Derive(bad, "NEWX", Array.Empty<string>()));
            Assert.AreEqual("templateCode", ex.ParamName);
        }
    }

    [TestMethod]
    public void Derive_LowercaseCode_IsAccepted()
    {
        var plan = NamingDeriver.Derive("tplx", "newx", Array.Empty<string>());

        Assert.AreEqual("newx-Mixed", plan.RepoName);
        Assert.AreEqual("tplx-Admin", plan.Dirs[0].From);
    }

    // ── S2c §12.3：后端派生名增 (端口) 后缀 ─────────────────────────────

    [TestMethod]
    public void Derive_BackendPipeline_GainsPortSuffix()
    {
        var plan = NamingDeriver.Derive("TPLX", "NEWX",
        [
            "ACME_DEV_Deploy_TPLX-Mobile",
            "ACME_DEV_Deploy_TPLX-Service",
        ], backendPort: 7001);

        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", plan.Pipelines[0].To);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service(7001)", plan.Pipelines[1].To);
    }

    [TestMethod]
    public void Derive_BackendPortSuffix_ReplacesTemplatePortSuffix()
    {
        // 模板真名自带 (端口) 后缀 → 派生名按端口盒值重写端口段（夹具端口一律合成值 7001/9000）
        var plan = NamingDeriver.Derive("TPLX", "NEWX",
        [
            "ACME_DEV_Deploy_TPLX-Service(7001)",
        ], backendPort: 9000);

        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service(9000)", plan.Pipelines[0].To);
    }

    [TestMethod]
    public void Derive_BackendWithoutPortParam_OmitsPortSuffix()
    {
        var plan = NamingDeriver.Derive("TPLX", "NEWX",
        [
            "ACME_DEV_Deploy_TPLX-Service(7001)",
        ]);

        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service", plan.Pipelines[0].To);
    }

    // ── S2c §12.3：ParsePipelinePrefix 对 (端口) 后缀免疫 ───────────────

    [TestMethod]
    public void ParsePipelinePrefix_PortSuffixImmune_ReturnsPrefixBeforeMarker()
    {
        Assert.AreEqual("ACME", NamingDeriver.ParsePipelinePrefix("ACME_DEV_Deploy_TPLX-Service(7001)"));
        Assert.AreEqual("ACME", NamingDeriver.ParsePipelinePrefix("ACME_DEV_Deploy_DEMO-Mobile"));
    }

    // ── S2c §12.3：StripPortSuffix / ParsePortSuffix ─────────────────────

    [TestMethod]
    public void StripPortSuffix_RemovesTrailingParenPort()
    {
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service", NamingDeriver.StripPortSuffix("ACME_DEV_Deploy_NEWX-Service(7001)"));
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service", NamingDeriver.StripPortSuffix("ACME_DEV_Deploy_NEWX-Service(9000)"));
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", NamingDeriver.StripPortSuffix("ACME_DEV_Deploy_NEWX-Mobile"));
    }

    [TestMethod]
    public void ParsePortSuffix_ParsesTrailingNumericPort()
    {
        Assert.AreEqual(7001, NamingDeriver.ParsePortSuffix("ACME_DEV_Deploy_NEWX-Service(7001)"));
        Assert.AreEqual(9000, NamingDeriver.ParsePortSuffix("ACME_DEV_Deploy_NEWX-Service(9000)"));
        Assert.IsNull(NamingDeriver.ParsePortSuffix("ACME_DEV_Deploy_NEWX-Service"));
        Assert.IsNull(NamingDeriver.ParsePortSuffix("ACME_DEV_Deploy_NEWX-Service(abc)"));
    }

    // ── S2d §13.3：后缀段比对（前缀容忍 + 端口容忍，前缀不写死） ────────────

    [TestMethod]
    public void PipelineSuffixSegment_StripsPortAndPrefix_ReturnsMarkerTail()
    {
        // 前缀从名字自身解析（ACME / 空前缀取段后一致）；(端口) 先剥离
        Assert.AreEqual("_DEV_Deploy_NEWX-Mobile", NamingDeriver.PipelineSuffixSegment("ACME_DEV_Deploy_NEWX-Mobile"));
        Assert.AreEqual("_DEV_Deploy_NEWX-Mobile", NamingDeriver.PipelineSuffixSegment("_DEV_Deploy_NEWX-Mobile"));
        Assert.AreEqual("_DEV_Deploy_NEWX-Service", NamingDeriver.PipelineSuffixSegment("ACME_DEV_Deploy_NEWX-Service(7001)"));
        // 无 _DEV_Deploy_ 标记 → 退化为剥离端口后的全名
        Assert.AreEqual("CUSTOM-PIPE", NamingDeriver.PipelineSuffixSegment("CUSTOM-PIPE"));
    }

    [TestMethod]
    public void MatchesPipelineName_空前缀与ACME真名_互认()
    {
        // §13.3 根因回归钉子：表单空前缀 vs 真名 ACME_DEV_Deploy_* 双向互认
        Assert.IsTrue(NamingDeriver.MatchesPipelineName("_DEV_Deploy_NEWX-Mobile", "ACME_DEV_Deploy_NEWX-Mobile"));
        Assert.IsTrue(NamingDeriver.MatchesPipelineName("ACME_DEV_Deploy_NEWX-Mobile", "_DEV_Deploy_NEWX-Mobile"));
    }

    [TestMethod]
    public void MatchesPipelineName_前缀容忍含端口差异组合_互认()
    {
        // (7001) vs (9000) + 前缀差异组合（S2c §12.3 端口容忍并入 §13.3 后缀段口径）
        Assert.IsTrue(NamingDeriver.MatchesPipelineName("_DEV_Deploy_NEWX-Service(9000)", "ACME_DEV_Deploy_NEWX-Service(7001)"));
        Assert.IsTrue(NamingDeriver.MatchesPipelineName("ACME_DEV_Deploy_NEWX-Service(7001)", "_DEV_Deploy_NEWX-Service(9000)"));
    }

    [TestMethod]
    public void MatchesPipelineName_不同代号或端_不命中_空名不命中()
    {
        Assert.IsFalse(NamingDeriver.MatchesPipelineName("_DEV_Deploy_NEWX-Mobile", "ACME_DEV_Deploy_OTHX-Mobile"));
        Assert.IsFalse(NamingDeriver.MatchesPipelineName("_DEV_Deploy_NEWX-Mobile", "ACME_DEV_Deploy_NEWX-Admin"));
        Assert.IsFalse(NamingDeriver.MatchesPipelineName("", "ACME_DEV_Deploy_NEWX-Mobile"));
        Assert.IsFalse(NamingDeriver.MatchesPipelineName("_DEV_Deploy_NEWX-Mobile", null));
    }

    // ── DerivePipelineName：单端派生（S2e §14 修复一：真名可空、永不前导下划线）──

    [TestMethod]
    public void DerivePipelineName_反查到真名_带真实前缀只换代号段()
    {
        // From = ACME 真名 → To 保留 ACME_ 真实前缀与端后缀，只换代号段（前缀不写死，取自真名自身）
        Assert.AreEqual(
            "ACME_DEV_Deploy_NEWX-Mobile",
            NamingDeriver.DerivePipelineName("ACME_DEV_Deploy_TPLX-Mobile", "NEWX", "-Mobile"));
        Assert.AreEqual(
            "GRP_DEV_Deploy_NEWX-Admin",
            NamingDeriver.DerivePipelineName("GRP_DEV_Deploy_TPLX-Admin", "NEWX", "-Admin"));
    }

    [TestMethod]
    public void DerivePipelineName_反查不到_兜底无前导下划线()
    {
        // From = null → DEV_Deploy_{代号}-{端}（无前导下划线，根因：'' + "_DEV_Deploy_" 拼接产生 _DEV_Deploy_*）
        Assert.AreEqual("DEV_Deploy_NEWX-Mobile", NamingDeriver.DerivePipelineName(null, "NEWX", "-Mobile"));
        Assert.AreEqual("DEV_Deploy_NEWX-Admin", NamingDeriver.DerivePipelineName("", "NEWX", "-Admin"));
        Assert.AreEqual("DEV_Deploy_NEWX-Service", NamingDeriver.DerivePipelineName("   ", "NEWX", "-Service"));
    }

    [TestMethod]
    public void DerivePipelineName_空前缀真名或无标记段_同样兜底无前导下划线()
    {
        // 真名空前缀（_DEV_Deploy_*）与无标记段（CUSTOM-PIPE）都退化为兜底形态，不得带前导下划线
        var fromEmptyPrefix = NamingDeriver.DerivePipelineName("_DEV_Deploy_TPLX-Mobile", "NEWX", "-Mobile");
        Assert.AreEqual("DEV_Deploy_NEWX-Mobile", fromEmptyPrefix);
        Assert.IsFalse(fromEmptyPrefix.StartsWith("_", StringComparison.Ordinal));

        var fromNoMarker = NamingDeriver.DerivePipelineName("CUSTOM-PIPE", "NEWX", "-Mobile");
        Assert.AreEqual("DEV_Deploy_NEWX-Mobile", fromNoMarker);
        Assert.IsFalse(fromNoMarker.StartsWith("_", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DerivePipelineName_后端端口后缀含port()
    {
        // -Service 后缀按 backendPort 重加 (端口)（S2c §12.3 同口径）；母版自带 (端口) 先剥离重派生
        Assert.AreEqual(
            "ACME_DEV_Deploy_NEWX-Service(9000)",
            NamingDeriver.DerivePipelineName("ACME_DEV_Deploy_TPLX-Service(7001)", "NEWX", "-Service", 9000));
        Assert.AreEqual(
            "DEV_Deploy_NEWX-Service(7001)",
            NamingDeriver.DerivePipelineName(null, "NEWX", "-Service(7001)", 7001));
        // 非后端端不受端口参数影响；后端无端口参数不加端口段
        Assert.AreEqual(
            "ACME_DEV_Deploy_NEWX-Mobile",
            NamingDeriver.DerivePipelineName("ACME_DEV_Deploy_TPLX-Mobile", "NEWX", "-Mobile", 9000));
        Assert.AreEqual(
            "ACME_DEV_Deploy_NEWX-Service",
            NamingDeriver.DerivePipelineName("ACME_DEV_Deploy_TPLX-Service(7001)", "NEWX", "-Service"));
    }

    [TestMethod]
    public void DerivePipelineName_非法代号_抛ArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => NamingDeriver.DerivePipelineName(null, "NEW-X", "-Mobile"));
        Assert.ThrowsExactly<ArgumentException>(() => NamingDeriver.DerivePipelineName(null, "", "-Mobile"));
    }
}
