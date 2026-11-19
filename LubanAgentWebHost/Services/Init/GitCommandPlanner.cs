/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： GitCommandPlanner
*描述：云效 S2 项目初始化 git 指令序列规划纯函数（设计 §4.2）——
*      clone 母版 → 批量 mv 改名 → 固定文案 commit → remote set-url 新库 → push
*      （展示与执行同一份序列，防展示≠执行；凭据永不进序列）
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.2
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
}

/// <summary>
/// git 指令步骤：可读标签 + 实际指令串（展示与执行同源）。
/// </summary>
/// <param name="Label">步骤可读标签。</param>
/// <param name="Command">实际执行的 git 指令（裸 URL，无凭据）。</param>
public record GitStep(string Label, string Command);
