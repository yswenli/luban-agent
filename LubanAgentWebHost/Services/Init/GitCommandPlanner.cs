/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： GitCommandPlanner
*描述：云效 S2 项目初始化 git 指令序列规划纯函数（设计 §4.2 + S2c §12.1.2/§12.2）——
*      PlanCloneRenamePush：clone 母版 → 批量 mv 改名 → 固定文案 commit → remote set-url 新库 → push；
*      PlanLocalCheckout：无源码本地检出（clone 目标库 → 切 develop）；
*      PlanSwitchDevelopLocal：新建库尾部切 develop（远端无 develop）。
*      （展示与执行同一份序列，防展示≠执行；凭据永不进序列）
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.2 + §12
*
*****************************************************************************/

namespace LubanAgentWebHost.Services.Init;

/// <summary>
/// 项目初始化 git 指令序列规划（纯函数）：产出「克隆母版 → 子目录改名 → 提交 →
/// 切换远端 → 推送」的有序步骤，预检面板展示与执行侧下发共用同一份序列。
/// </summary>
/// <remarks>
/// 脱敏红线：URL 一律用调用方给的<b>裸 URL</b>（凭据不进 URL、不进序列），
/// PAT 明文零落盘零日志——执行侧由会话通路以环境变量 / askpass 注入凭据。
/// </remarks>
public static class GitCommandPlanner
{
    /// <summary>
    /// 规划「克隆母版 → 子目录改名 → 提交 → 切换远端到新库 → 推送」序列。
    /// </summary>
    /// <remarks>
    /// 步骤顺序固定：
    /// ① <c>git clone --branch {branch}</c> 到 <c>{workspaceRoot}/{newCode}-Mixed</c>；
    /// ② 逐目录 <c>git mv {templateCode}-* {newCode}-*</c>（源名 = 目标名把 newCode 前缀换回 templateCode）；
    /// ③ dirs 非空时固定文案 commit；④ <c>remote set-url origin {newRepoUrl}</c>；⑤ <c>push origin {branch}</c>。
    /// </remarks>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="templateRepoUrl">母版库裸 URL。</param>
    /// <param name="newRepoUrl">新库裸 URL。</param>
    /// <param name="templateCode">母版代号。</param>
    /// <param name="newCode">新代号。</param>
    /// <param name="dirNames">改名目标目录名（形如 <c>{newCode}-Admin</c>）；空列表则跳过 mv 与 commit。</param>
    /// <param name="branch">分支名（clone 与 push 共用）。</param>
    /// <returns>有序步骤（Label 可读文案 + Command 指令串）。</returns>
    public static IReadOnlyList<GitStep> PlanCloneRenamePush(
        string workspaceRoot,
        string templateRepoUrl,
        string newRepoUrl,
        string templateCode,
        string newCode,
        IReadOnlyList<string> dirNames,
        string branch)
    {
        ArgumentNullException.ThrowIfNull(dirNames);

        var repoDir = $"{workspaceRoot}/{newCode}-Mixed";
        var steps = new List<GitStep>
        {
            new($"克隆母版库 {templateCode}-Mixed", $"git clone --branch {branch} {templateRepoUrl} {repoDir}"),
        };

        foreach (var dirName in dirNames)
        {
            var sourceDir = dirName.StartsWith(newCode, StringComparison.Ordinal)
                ? templateCode + dirName[newCode.Length..]
                : dirName;

            steps.Add(new GitStep(
                $"改名 {sourceDir} → {dirName}",
                $"git -C \"{repoDir}\" mv {sourceDir} {dirName}"));
        }

        if (dirNames.Count > 0)
        {
            steps.Add(new GitStep(
                "提交子目录改名",
                $"git -C \"{repoDir}\" commit -m \"chore: 重命名子目录 {templateCode}-* → {newCode}-*（项目初始化派生）\""));
        }

        steps.Add(new GitStep(
            "远端切换到新库",
            $"git -C \"{repoDir}\" remote set-url origin {newRepoUrl}"));

        steps.Add(new GitStep(
            $"推送 {branch} 分支",
            $"git -C \"{repoDir}\" push origin {branch}"));

        return steps;
    }

    /// <summary>
    /// 规划「无源码本地检出」序列（S2c §12.1.2）：clone 目标库 → 切 <c>develop</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用于复用库（远端已有、本地 <c>{workspaceRoot}/{repoName}</c> 缺失/为空）的自动检出，
    /// 与新建库的 <see cref="PlanCloneRenamePush"/> 互补。步骤：
    /// ① <c>git clone --branch {branch} {repoUrl} {workspaceRoot}/{repoName}</c>；
    /// ② 切 <c>develop</c>：跟踪远端 <c>git -C "{dir}" checkout -b develop origin/develop</c>；
    /// ③ 本地新建 <c>develop</c> 兜底 <c>git -C "{dir}" checkout -b develop</c>。
    /// </para>
    /// <para>
    /// 分支口径（S2c §12.2）：远端有 <c>develop</c> 则跟踪（②），无则本地新建（③）。
    /// 本方法<b>两个切分支步骤都出</b>，Label 显式标注「跟踪远端 develop / 本地新建 develop（上一步失败时）」，
    /// 由会话执行侧按序尝试、②失败才落③——展示与执行同一份序列，避免「展示≠执行」。
    /// （备选口径：按参数 <c>remoteHasDevelop</c> 二选一，因预检期无远端分支真值，统一出两步更稳，故不采用。）
    /// </para>
    /// <para>
    /// 脱敏红线同 <see cref="PlanCloneRenamePush"/>：<paramref name="repoUrl"/> 为<b>裸 URL</b>，
    /// 无 <c>pt-</c> 明文、无 <c>@</c> 凭据形态；凭据由会话通路注入。
    /// </para>
    /// </remarks>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="repoUrl">目标库裸 URL（复用库真实克隆地址，无凭据）。</param>
    /// <param name="repoName">目标库名（落位目录 <c>{workspaceRoot}/{repoName}</c>）。</param>
    /// <param name="branch">clone 分支（与后续切 develop 无关，用入参）。</param>
    /// <returns>有序步骤（clone → 跟踪远端 develop → 本地新建 develop 兜底）。</returns>
    public static IReadOnlyList<GitStep> PlanLocalCheckout(
        string workspaceRoot,
        string repoUrl,
        string repoName,
        string branch)
    {
        if (string.IsNullOrWhiteSpace(repoName))
            throw new ArgumentException("库名不能为空。", nameof(repoName));

        var repoDir = $"{workspaceRoot}/{repoName}";
        return new List<GitStep>
        {
            new($"检出库 {repoName}", $"git clone --branch {branch} {repoUrl} {repoDir}"),
            new("跟踪远端 develop", $"git -C \"{repoDir}\" checkout -b develop origin/develop"),
            new("本地新建 develop（上一步失败时）", $"git -C \"{repoDir}\" checkout -b develop"),
        };
    }

    /// <summary>
    /// 新建库序列尾部「本地切 <c>develop</c>」单步（S2c §12.2）：推完 <c>master</c> 后自 <c>master</c> 本地新建 <c>develop</c>。
    /// </summary>
    /// <remarks>
    /// 新建库远端无 <c>develop</c>，故只需本地 <c>checkout -b develop</c>（不带 <c>origin/develop</c>）。
    /// 供 <c>PlanCloneRenamePush</c> 产物<b>尾部追加</b>，保持展示与执行同一份序列（无凭据）。
    /// </remarks>
    /// <param name="repoDir">已 clone 并落位的库目录。</param>
    /// <returns>切 <c>develop</c> 步骤。</returns>
    public static GitStep PlanSwitchDevelopLocal(string repoDir)
        => new("本地新建 develop（自 master）", $"git -C \"{repoDir}\" checkout -b develop");
}

/// <summary>
/// git 指令步骤：可读标签 + 实际指令串（展示与执行同源）。
/// </summary>
/// <param name="Label">步骤可读标签。</param>
/// <param name="Command">实际执行的 git 指令（裸 URL，无凭据）。</param>
public record GitStep(string Label, string Command);
