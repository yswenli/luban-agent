/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepPrompts
*创建时间：2026/10/21
*描述：Step 4 / Step 5 交给助手执行的提示词构造（M4-P4 定案）
*****************************************************************************/

using System.Text;
using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// Step 4 / Step 5 的提示词构造（纯函数，供单测锁死「边界与结论契约」）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么由插件构造提示词</b>：这两个步骤的判定必须机器可读，所以结论契约（结论文件路径 + JSON schema +
/// 端标识）由插件固定，助手只负责「怎么干」。技能正文（<c>skills/newproject-*.md</c>）写的是同一条口径的
/// 自然语言版，供用户在会话里手工驱动时使用。
/// </para>
/// <para>
/// <b>共用硬边界（两条提示词都必须写死）</b>：
/// ①只做本步该做的事，不写项目文件（唯一例外＝结论文件）；
/// ②禁止调用 <c>newproject-*</c> 技能与「新建项目」插件的工具/服务——本轮提示词正是由插件在<b>同一会话</b>发起的，
/// 助手若回头调用插件服务，就会形成「同会话回合串行」的自等死锁；
/// ③正文只回一行结论，机器判定一律以结论文件为准。
/// </para>
/// </remarks>
public static class StepPrompts
{
    /// <summary>
    /// 构造 Step 4「数据库探活」提示词。
    /// </summary>
    /// <remarks>
    /// <b>口令内联</b>：按用户定案，口令以明文内联进本轮提示词（已知残余影响：该轮 user 原文会被框架
    /// <c>SessionChatHistoryProvider</c> 落进会话库，故提示词里追加「不要写进任何文件 / 不要回显」的约束，
    /// 尽量缩小扩散面）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    /// <param name="request">探活参数（含明文口令）。</param>
    /// <param name="resultFile">结论文件绝对路径。</param>
    /// <returns>提示词。</returns>
    public static string BuildDatabaseProbePrompt(ProjectSpec spec, DatabaseProbeRequest request, string resultFile)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"你是本项目（{Display(spec)}）的项目助手。");
        builder.AppendLine();
        builder.AppendLine("本次由「新建项目」向导的 Step 4 驱动你执行一次【数据库探活】。请严格按下面的边界执行。");
        builder.AppendLine();

        AppendHardBoundaries(builder, new[]
        {
            "本步只做一件事：连接数据库并确认【该库已存在】。",
            "禁止执行任何 SQL（CREATE / DROP / ALTER / TRUNCATE / INSERT / UPDATE / DELETE 一律禁止）；"
                + "禁止建库、禁止改数据库配置、禁止修改项目里的任何文件。",
            "库不存在就是「未通过」，如实报告即可——本步结果【不阻断】后续流程，不要试图替用户建库。",
            "只用脚本工具里的 Python（runpython）一条命令完成；不要用 shell、不要用管道、不要分多次执行。"
        });

        builder.AppendLine("【探活参数】（口令仅本次使用：不要写进任何文件、不要回显、不要出现在 detail 里）");
        builder.AppendLine($"server   = {request.Host}");
        builder.AppendLine($"port     = {request.Port}");
        builder.AppendLine($"database = {request.Database}");
        builder.AppendLine($"user     = {request.User}");
        builder.AppendLine($"password = {request.Password}");
        builder.AppendLine();

        builder.AppendLine("【执行要求】");
        builder.AppendLine("1) 用 runpython 执行下面这段脚本（按上面的参数替换后原样执行）：");
        builder.AppendLine("```python");
        builder.AppendLine("import json, pymysql");
        builder.AppendLine("result = {\"step\": 4, \"ok\": False, \"checks\": [], \"message\": \"\"}");
        builder.AppendLine("try:");
        builder.AppendLine($"    conn = pymysql.connect(host=\"{request.Host}\", port={request.Port},");
        builder.AppendLine($"                            user=\"{request.User}\", password=\"{request.Password}\",");
        builder.AppendLine($"                            database=\"{request.Database}\", connect_timeout=5)");
        builder.AppendLine("    conn.close()");
        builder.AppendLine("    result[\"ok\"] = True");
        builder.AppendLine("    result[\"checks\"].append({\"name\": \"connect\", \"ok\": True, \"detail\": \"已连接且库存在\"})");
        builder.AppendLine("    result[\"message\"] = \"探活通过：能连上且库存在。\"");
        builder.AppendLine("except Exception as ex:");
        builder.AppendLine("    result[\"checks\"].append({\"name\": \"connect\", \"ok\": False, \"detail\": str(ex)})");
        builder.AppendLine("    result[\"message\"] = \"探活未通过：\" + str(ex)");
        builder.AppendLine($"with open(r\"{resultFile}\", \"w\", encoding=\"utf-8\") as f:");
        builder.AppendLine("    json.dump(result, f, ensure_ascii=False, indent=2)");
        builder.AppendLine("print(result[\"message\"])");
        builder.AppendLine("```");
        builder.AppendLine("2) 若 pymysql 未安装：先安装（python -m pip install pymysql）再重试一次，最多一次。");
        builder.AppendLine("3) 结论要可操作：失败时把原始错误写进 message 与 detail（例如「1049 Unknown database 'xxx'，请先手工建库」）。");
        builder.AppendLine();

        AppendResultContract(builder, resultFile, "DONE 或 FAILED: <一句话原因>");

        return builder.ToString();
    }

    /// <summary>
    /// 构造 Step 5「逐端编译检查」提示词。
    /// </summary>
    /// <remarks>
    /// v0.28：边界由「只编译、不修复」放宽为「只编译、不改业务」——允许修依赖树与环境（`fnm` 切 Node 16、
    /// npm / cnpm 装依赖、重建 node_modules），仍禁止改业务源码与工程配置；
    /// 并按端写入【本组织已知构建姿势】（管理端：develop 分支 + Node 16 + `npm run build:dev`）。
    /// v0.29：纠正「包入口缺失」的处置——半装包（如 element-plus 缺 <c>es/index.mjs</c>）只能删包目录重装，
    /// <c>npm rebuild</c> 无效；并要求装依赖独占一条命令跑完（管道截断会让子进程半途被杀）。
    /// v0.30：管理端改为固定的 4 步流程（先构建 → 失败再核 Node 16 → <c>cnpm i</c> 修依赖 → 再构建），
    /// 与真机验证过的成功路径一致；「全局装 vite」由用户明确授权。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    /// <param name="targets">本次要编译的端（按端仓库，已带本地目录）。</param>
    /// <param name="resultFile">结论文件绝对路径。</param>
    /// <returns>提示词。</returns>
    public static string BuildCompilePrompt(ProjectSpec spec, IReadOnlyList<EndRepository> targets, string resultFile)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"你是本项目（{Display(spec)}）的项目助手。");
        builder.AppendLine();
        builder.AppendLine("本次由「新建项目」向导的 Step 5 驱动你执行【逐端编译检查】。请严格按下面的边界执行。");
        builder.AppendLine();

        AppendHardBoundaries(builder, new[]
        {
            "本步的边界是「只编译、不改业务」：**允许修依赖树与环境**（装依赖、切 Node 版本、删掉半装的单个依赖目录后重装、"
                + "必要时重建 node_modules），但**禁止修改仓库里的业务源码与工程配置**（业务代码、package.json、"
                + "vite.config、.csproj / .sln 等一律不改；锁文件不要手工编辑，包管理器装依赖时自动更新属正常副作用）。"
                + "编译错误若是源码问题，如实报出来，由用户修完后再重跑本步。",
            "编译产物（bin / obj / dist）留在原处即可，**不要整体清理 node_modules**；唯一例外是修半装包时"
                + "按【本组织已知构建姿势】第 5) 条删掉出问题的那一个包目录（如 node_modules/element-plus）。",
            "依赖未装就先装（dotnet restore / npm install / cnpm install / pnpm install 等），再编译；每个端最多重试 2 次。"
                + "**装依赖必须独占一条命令并等它正常退出**：不要接管道截断输出，也不要在它跑的时候并行做别的事——"
                + "中途被杀会留下「目录在、文件不全」的半装包，比没装更坑。",
            "命令首词必须是白名单内的可执行文件：dotnet / git / node / npm / npx / pnpm / yarn / fnm / cnpm / python / pip。"
                + "一次一条命令，可用 && 串联（每一段的首词同样受白名单约束）；不要用管道与重定向。"
        });

        builder.AppendLine("【待编译的端】（每个端的 name 标识必须原样使用，插件按它把结论对回具体的端）");
        foreach (var repository in targets)
        {
            builder.AppendLine($"- name = {repository.End}（{EndName(repository.End)}）；本地目录 = {repository.LocalPath}；"
                               + $"仓库 = {repository.RepositoryName}");
        }
        builder.AppendLine();

        builder.AppendLine("【本组织已知构建姿势】（本组织各端仓库的既有做法；与你按工程类型的默认判断冲突时，以这里为准）");
        builder.AppendLine("- 管理端（Admin，Vite + Vue）：Node 必须是 16，用 npm 系客户端（**不要用 pnpm**）。"
                           + "固定按下面 4 步走，不要自创流程：");
        builder.AppendLine("  1) 先直接跑构建：fnm exec --using=16 -- npm.cmd run build:dev"
                           + "（本仓库的构建脚本叫 build:dev，**不是 npm run build**）。exitCode=0 即该端通过，收工。");
        builder.AppendLine("  2) 构建失败时先核对 Node 版本：fnm exec --using=16 -- node -v 应为 v16.x；"
                           + "不是 16 就一律用 fnm exec --using=16 -- 把命令包一层（Windows 下 fnm 直接 spawn npm 会报 program not found，"
                           + "必须写成 npm.cmd / cnpm.cmd；不要用 fnm env | Invoke-Expression 这类管道 / 求值写法）。"
                           + "同时确认当前在 develop 分支（工作树干净时才 git checkout develop）。");
        builder.AppendLine("  3) 装依赖：先 fnm exec --using=16 -- cnpm.cmd install -g vite（全局 vite 兜底），"
                           + "再在仓库目录 fnm exec --using=16 -- cnpm.cmd i（补全/修复项目依赖）。"
                           + "**装依赖必须独占一条命令并等它正常退出**：不要接管道截断输出（| Select-Object / | Select-String / | head），"
                           + "也不要并行做别的——中途被杀会留下「目录在、文件不全」的半装包，比没装更坑；"
                           + "若被中断，重跑同一命令直到正常退出。");
        builder.AppendLine("  4) 再跑一次构建：fnm exec --using=16 -- npm.cmd run build:dev，**以这一次的输出定结论**。");
        builder.AppendLine("  补充：若第 4 步仍报 Failed to resolve entry for package \"X\"：判「半装」——包目录存在，但 package.json 里 "
                           + "module / main / exports 指向的入口文件不存在（例：element-plus 缺 node_modules/element-plus/es/index.mjs）。"
                           + "修法是删掉该包目录再装回来：node -e \"require('fs').rmSync('node_modules/X',{recursive:true,force:true})\" "
                           + "后重跑 cnpm.cmd i。**不要用 npm rebuild X**（对预构建包无效），也**不要**因安装几秒 exit 0 就认为修好——"
                           + "包管理器只核对目录，不校验文件完整性。");
        builder.AppendLine("- 后端（Service，.NET）：dotnet build <解决方案>.sln。");
        builder.AppendLine("- 其它端（移动端等）：按工程类型自行判断。");
        builder.AppendLine("（兜底：若机器上确实没有 fnm / Node 16 / cnpm，**不要**自行安装除 vite 之外的全局工具链、也不要改仓库配置——"
                           + "把缺失项如实写进该端结论的 detail，本步按失败上报，由用户补好环境后重跑。）");
        builder.AppendLine();

        builder.AppendLine("【执行要求】");
        builder.AppendLine("1) 每个端先看清目录里有什么（.sln / .csproj / package.json / pubspec.yaml 等）——"
                           + "**命令一律在该端的「本地目录」（＝该端仓库目录）执行**（先 cd 或用工具的工作目录参数，"
                           + "不要在项目工作区根执行）；编译方式由你按工程类型决定。");
        builder.AppendLine("2) 后端一般是 .NET 解决方案：在仓库目录里 dotnet build 对应的 .sln（或 .csproj）。");
        builder.AppendLine("3) 前端（Vite/Vue 等）先读仓库 package.json 的 scripts 再选命令：本组织管理端先直接跑 npm run build:dev，"
                           + "失败再按【本组织已知构建姿势】的 4 步流程修环境；不要凭默认猜 npm run build，也不要用 pnpm。");
        builder.AppendLine("4) **修了依赖就必须重跑构建验证**：本轮里凡动过 node_modules（装 / 重装 / 删包重装），"
                           + "都要在修复后**再跑一次该端的构建命令**，以最后一次构建的真实输出定结论；不许拿修复前的报错收尾，"
                           + "也不许只跑 npm install 就宣布修好（install 的 exit 0 不代表包完整）。");
        builder.AppendLine("5) 每个端得出「通过 / 失败」与一句话 detail；失败时附关键错误（错误码 / 文件名 / 行号，最多 3 条），"
                           + "并在 detail 里写清「哪种修法试过、为什么没修好」，不要给用户无效建议（如对预构建包用 npm rebuild）。");
        builder.AppendLine();

        builder.AppendLine("把下面的 JSON 写入结论文件（用 WriteFileAsync 或 Python 写出均可，UTF-8）：");
        builder.AppendLine("```json");
        builder.AppendLine("{");
        builder.AppendLine("  \"step\": 5,");
        builder.AppendLine("  \"ok\": false,   // 仅当所有端都通过时才写 true");
        builder.AppendLine("  \"checks\": [");
        builder.AppendLine("    { \"name\": \"Service\", \"ok\": true,  \"detail\": \"编译通过\", \"command\": \"dotnet build xxx.sln\" },");
        builder.AppendLine("    { \"name\": \"Admin\",   \"ok\": false, \"detail\": \"2 个错误：…\", \"command\": \"fnm exec --using=16 -- npm.cmd run build:dev\" }");
        builder.AppendLine("  ],");
        builder.AppendLine("  \"message\": \"<一句话总结>\"");
        builder.AppendLine("}");
        builder.AppendLine("```");
        builder.AppendLine("注意：checks 必须逐端齐全；漏写某个端会被判为「该端未给结论」＝本步失败。");
        builder.AppendLine();

        AppendResultContract(builder, resultFile, "DONE 或 FAILED: <失败端与原因>");

        return builder.ToString();
    }

    // ── Step 3 / 6 / 7（v0.3 助手驱动）────────────────────────────────────────

    /// <summary>
    /// 构造 Step 3「建库前准备（只读）」提示词（一次只读准备下发）。
    /// </summary>
    /// <remarks>
    /// 本阶段<b>只读</b>：只拉模板库 / 分支 / 代码组 / 同名仓库的事实，写进结论文件的 <c>artifacts[]</c>，
    /// 面板据此渲染确认界面；建组、建库、克隆都发生在用户确认后的执行续跑（
    /// <see cref="BuildRepositoryExecutePrompt"/>）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    /// <param name="plan">建库计划（按端）。</param>
    /// <param name="resultFile">结论文件绝对路径。</param>
    /// <returns>提示词。</returns>
    public static string BuildRepositoryPreparePrompt(
        ProjectSpec spec,
        IReadOnlyList<EndRepository> plan,
        string resultFile)
    {
        var docs = DocsDirectory(spec);
        var builder = new StringBuilder();

        builder.AppendLine($"你是本项目（{Display(spec)}）的项目助手。");
        builder.AppendLine();
        builder.AppendLine("本次由「新建项目」向导的 Step 3 驱动你执行【建库前准备（只读）】。");
        builder.AppendLine("本阶段**只读**：只把事实查清楚写进结论文件，面板据此给用户确认；"
                           + "**建组 / 建库 / 克隆 / 改文件都不在本阶段**（唯一例外＝结论文件）。");
        builder.AppendLine();

        AppendKnowledgeBounds(builder, docs, StepReportStatus.Blocked);

        AppendHardBoundaries(builder, new[]
        {
            "本阶段只读：**禁止**任何写入动作——云效侧禁止 codeup_create_group / codeup_create_repo / "
                + "flow_create_pipeline / flow_update_pipeline / flow_run_pipeline；git 侧禁止 GitCloneAsync / "
                + "GitAddAsync / GitCommitAsync / GitPushAsync；文件侧禁止写入 / 删除项目文件。",
            $"先读知识正文再干活：`{docs}\\org-reference.md`（组织专有值与会话约定）与 "
                + $"`{docs}\\step3-repositories.md`（本步口径与失败处置）。**不得**凭记忆臆造组织专有值。",
            "云效只读工具用 MCP：mcp_yunxiao_codeup_list_template_repos / mcp_yunxiao_codeup_list_branches / "
                + "mcp_yunxiao_codeup_list_groups / mcp_yunxiao_codeup_search_repos / mcp_yunxiao_codeup_get_repo。"
        });

        AppendOrganization(builder, spec);

        builder.AppendLine("【建库计划】（用户已在面板确认；仓库名与可见性以这里为准）");
        foreach (var repository in plan)
        {
            builder.AppendLine($"- end = {repository.End}（{EndName(repository.End)}）· 仓库名 = {repository.RepositoryName}"
                               + $" · 可见性 = {repository.Visibility} · 仓库目录 = {repository.LocalPath}"
                               + "（已含仓库名，勿再拼一次）"
                               + $" · 模板仓 = {(string.IsNullOrWhiteSpace(repository.TemplateRepository) ? "（未登记，按 docs 按端名称约定匹配）" : repository.TemplateRepository)}");
        }
        builder.AppendLine($"项目缩写（code）= {spec.Code}；项目名 = {spec.Name}。");
        builder.AppendLine();

        builder.AppendLine("【要查清的事实】（逐端）");
        builder.AppendLine("1) 模板库：`mcp_yunxiao_codeup_list_template_repos`（templateType=1、perPage=100）**全量拉取**后"
                           + "在本地按各端名称约定匹配（该接口**没有 search 入参**，别指望服务端按名过滤）；取 templateProjectId；");
        builder.AppendLine("2) 模板仓分支：`mcp_yunxiao_codeup_list_branches`（repositoryId=templateProjectId），"
                           + "按 docs 的 pin 择优顺序（develop → master → main → 模板默认分支）解析 pin；");
        builder.AppendLine("3) 代码组：`mcp_yunxiao_codeup_list_groups`（search=组名）查目标代码组是否已存在（决定「新建 / 复用」）；");
        builder.AppendLine("4) 同名仓库：`mcp_yunxiao_codeup_search_repos`（search=仓库名）；**若命中，再调 "
                           + "`mcp_yunxiao_codeup_get_repo`（repositoryId=命中项 id）取 httpUrlToRepo 与 defaultBranch**"
                           + "（列表接口不含克隆地址；仓库页面地址由克隆地址去掉尾部 .git 推导）；");
        builder.AppendLine("5) 仓库目录：检查 `<仓库目录>`（上面各端的完整目录，**已含仓库名**，不要再拼一次）是否已存在且非空"
                           + "（只读判断，不要删）。**目录判定一律用 `ListDirectoryAsync`**（或用 `RunPythonAsync` 读文件系统），"
                           + "不要用 `RunShellAsync` 写 PowerShell `if` / `foreach`（按段首词白名单会拒「名单外的可执行文件」）。");
        builder.AppendLine();

        AppendArtifactSchema(builder, 3, @"{
  ""step"": 3,
  ""phase"": ""prepare"",
  ""status"": ""ready"",
  ""ok"": true,
  ""artifacts"": [
    { ""kind"": ""group"", ""name"": ""<代码组名>"", ""externalId"": ""<组 id 或 null>"", ""webUrl"": ""<链接或 null>"",
      ""extra"": { ""kind"": ""group"", ""exists"": false, ""parentId"": null, ""organizationId"": ""<组织 ID>"" } },
    { ""kind"": ""repository"", ""end"": ""Service"", ""name"": ""<仓库名>"", ""externalId"": null,
      ""webUrl"": ""<仓库页面链接或 null>"", ""cloneUrl"": ""<已存在时的 httpUrlToRepo 或 null>"",
      ""localPath"": ""<仓库目录>"", ""templateBranch"": ""<pin 分支>"",
      ""extra"": { ""kind"": ""repository"", ""templateRepository"": ""<模板仓名>"", ""templateProjectId"": 123,
        ""templatePath"": ""<模板库全路径>"", ""templateBranches"": [""develop"", ""master""],
        ""resolvedBranchSource"": ""<pin 来源说明>"", ""repositoryExists"": false, ""localPathOccupied"": false,
        ""visibility"": ""internal"", ""error"": null } }
  ],
  ""checks"": [ { ""name"": ""Service"", ""ok"": true, ""detail"": ""模板与 pin 已解析"" } ],
  ""message"": ""<一句话总结>""
}", "ready / blocked / needsInput");

        builder.AppendLine("**准备阶段只写 `ready`（事实查清、可执行）或 `blocked`（条件不满足、不可执行，必须写明原因）；"
                           + "不要写 `done`** —— `done` 是执行轮（建组 / 建库 / 克隆完成）专用，"
                           + "准备阶段写 `done` 会被执行提示词误判为「本步已完成」。");
        builder.AppendLine();

        builder.AppendLine("要点：`end` 必须用端标识字符串（Service / Admin / Mobile，**不是**数字）；"
                           + "每端一条 `kind=repository` 产物；`extra.kind` 必须与 `kind` 一致（多态判别属性）；"
                           + "某端查不通时把原因写进该端 `extra.error` 并把 `checks` 里该项记为 ok=false，**不要**编造事实。");
        builder.AppendLine();

        AppendResultContract(builder, resultFile, "READY 或 BLOCKED: <一句话原因>");

        return builder.ToString();
    }

    /// <summary>
    /// 构造 Step 3「执行续跑」提示词（用户确认后，在同一会话里落地下发）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="plan">用户确认后的建库计划（按端）。</param>
    /// <param name="prepareResultFile">准备阶段结论文件路径（事实来源）。</param>
    /// <param name="resultFile">本次结论文件绝对路径。</param>
    /// <param name="resolvedInputs">已解决的交互答复（如「Service 的克隆冲突＝reclone」；可为空）。</param>
    /// <returns>提示词。</returns>
    public static string BuildRepositoryExecutePrompt(
        ProjectSpec spec,
        IReadOnlyList<EndRepository> plan,
        string prepareResultFile,
        string resultFile,
        IReadOnlyList<string>? resolvedInputs = null)
    {
        var docs = DocsDirectory(spec);
        var builder = new StringBuilder();

        builder.AppendLine($"你是本项目（{Display(spec)}）的项目助手。");
        builder.AppendLine();
        builder.AppendLine("本次由「新建项目」向导的 Step 3 驱动你执行【建组建库并克隆】。用户已在面板确认下面的计划。");
        builder.AppendLine();

        AppendKnowledgeBounds(builder, docs, StepReportStatus.Failed);

        AppendHardBoundaries(builder, new[]
        {
            "云效侧写入用 MCP：mcp_yunxiao_codeup_create_group（组不存在时）/ mcp_yunxiao_codeup_create_repo"
                + "（同名仓库已存在则**跳过创建直接复用**，重跑幂等）；只读事实仍用 codeup_list_* / codeup_get_repo。",
            "git 侧一律用**框架 git 工具组**（云效 MCP **已不再提供** git 类工具）：克隆用 GitCloneAsync(url, branch, directory)，"
                + "变换分支用 GitCheckoutAsync，复核用 GitStatusAsync / GitBranchListAsync。**不要**用 codeup_clone / codeup_push（已删除）。"
                + "**每个 git 调用都必须显式带 `workingDirectory=<该端仓库目录>`**（不传时框架在项目工作区根执行，会操作到别的目录 / 报「不是 git 仓库」）；"
                + "需要暂存全部时传 `GitAddAsync(paths=[\".\"], …)`，**不要**传 `-A`（工具会拼成 `git add -- -A` 当 pathspec，必然失败）。",
            $"先读知识正文：`{docs}\\step3-repositories.md`（建库 / 克隆 / 占位符 / 三选一 / 失败处置）与 "
                + $"`{docs}\\org-reference.md`（组织专有值）。",
            "**建库后模板分支是服务端异步同步的**（实证 10–17s）：必须按 docs 轮询 `mcp_yunxiao_codeup_list_branches` "
                + "等 pin 分支出现（上限 120s / 间隔 3s）后才克隆，否则 git 会在目标目录留下 `.git/HEAD = ref: refs/heads/.invalid` 的残骸。",
            "git 凭据由用户在**外部环境**自行配置（credential helper / 系统凭据管理器）：克隆地址用 "
                + "`httpUrlToRepo`（**不要**把令牌拼进 URL）。凭据缺失时把 git 原文报错带出来，并提示"
                + "「请先在外部环境配好 codeup HTTPS 凭据」。",
            "目录是否存在 / 是否非空用 `ListDirectoryAsync` 判断；占位符扫描 / 替换 / 清理一律用 `RunPythonAsync`"
                + "（`RunShellAsync` 只认白名单可执行文件，`if` / `foreach` 这类 PowerShell 控制流会被判为「名单外的可执行文件」拒绝）。"
        });

        AppendOrganization(builder, spec);

        builder.AppendLine("【事实来源：准备阶段结论文件】");
        builder.AppendLine(prepareResultFile);
        builder.AppendLine("（模板仓 / pin / 代码组是否存在 / 仓库是否已存在，都以它为准；需要复核可再调只读 MCP。）");
        builder.AppendLine("**它只是准备（只读）阶段的结论（`phase=prepare` / `status=ready`），代表「事实已查清」，"
                           + "不代表本步已完成**：建组 / 建库 / 克隆都还没做。本步必须真正执行并把结果写进**本次**执行结论"
                           + "（`step3-<时间戳>.json`，`status=done`）；直接回 `DONE` 而不执行会被判为「未执行」。");
        builder.AppendLine();

        builder.AppendLine("【本次建库计划（用户已确认）】");
        foreach (var repository in plan)
        {
            builder.AppendLine($"- end = {repository.End}（{EndName(repository.End)}）· 仓库名 = {repository.RepositoryName}"
                               + $" · 可见性 = {repository.Visibility} · 仓库目录 = {repository.LocalPath}"
                               + "（已含仓库名，勿再拼一次）"
                               + $" · 模板仓 = {repository.TemplateRepository}"
                               + $" · pin = {(string.IsNullOrWhiteSpace(repository.TemplateBranch) ? "（按 docs 现场解析）" : repository.TemplateBranch)}"
                               + (repository.Cloned ? " · 状态 = 已克隆（跳过建库与克隆，只补占位符替换与复核）" : " · 状态 = 未克隆（1)~5) 全做）"));
        }
        builder.AppendLine($"项目缩写（code）= {spec.Code}；项目名 = {spec.Name}。");
        builder.AppendLine();

        // 断点续跑：插件只把「整体通过」的轮次落账，所以 Cloned=true 的端一定已过本地复核——
        // 直接告诉助手别重复建库/克隆，否则它每次重跑都从第 1 步重来，长链路易再次半途收尾。
        var clonedEnds = plan.Where(repository => repository.Cloned).ToList();
        if (clonedEnds.Count > 0)
        {
            builder.AppendLine("【已落账的端（上一次已建库并克隆，本轮只补缺失）】");
            foreach (var repository in clonedEnds)
            {
                builder.AppendLine($"- end = {repository.End}（{EndName(repository.End)}）· 仓库目录 = {repository.LocalPath}："
                                   + "**跳过 1)~5)**（不要重复建库、不要重新克隆，目录非空即复用），只做 6) 占位符替换 + 7) 复核，"
                                   + "并把该端照常写进本次结论的 `artifacts[]` / `checks[]`。");
            }
            builder.AppendLine("其余端（状态 = 未克隆）照常走完整 1)→7)。");
            builder.AppendLine();
        }

        if (resolvedInputs is { Count: > 0 })
        {
            builder.AppendLine("【用户对本次交互项的答复】（据此继续，不要重复询问）");
            foreach (var line in resolvedInputs)
                builder.AppendLine($"- {line}");
            builder.AppendLine();
        }

        builder.AppendLine("【执行口径：逐端闭环，一端没走完不许收尾】");
        builder.AppendLine("本步是多端批次，但**必须一端一端闭环**：对清单里的每一端，独立完整地走完 1)→7)，"
                           + "把该端写进 `artifacts[]` / `checks[]` 之后再进入下一端。"
                           + "**禁止**把多端拆成「先把所有端建库、再统一克隆 / 统一替换」的批处理顺序。");
        builder.AppendLine("**任一端未闭环时禁止写结论文件收尾**：1)~7) 只要有一条没做（或占位符残余 ≠ 0），"
                           + "就必须继续调用工具做完，**不得**用 `status=failed` 的结论文件代替执行、"
                           + "也不得写「执行中断 / 需要继续执行」这类收尾语。"
                           + "确实遇到 docs 失败对照表里不可恢复的失败（401/403/pin 120s 未就绪/凭据缺失）时，"
                           + "才能在该端 `checks[]` 写明**失败环节 + 原始报错**并把整步写 `failed`；"
                           + "此时仍要把**尚未尝试的端照常做完**（单端失败不豁免其它端）。");
        builder.AppendLine("**收尾自检（每端六项全绿才算闭环，任一不绿就继续做）**："
                           + "①仓库 id 已取到 ②`httpUrlToRepo` 已取到 ③pin 分支已在远端就位 ④本地目录已克隆且 `.git`/`refs/heads/<pin>` 有效 "
                           + "⑤占位符残余 = 0 且 `extra.placeholderSources` **非空** ⑥`extra.cloned=true` 且 `extra.error` 为空。");
        builder.AppendLine();

        builder.AppendLine("【执行步骤】（对**每一端**依次执行；不要跨端交错）");
        builder.AppendLine("1) 代码组：查不到就 `mcp_yunxiao_codeup_create_group` 建（父级按 docs），查到就复用；");
        builder.AppendLine("2) 仓库：同名已存在则复用其 id；否则 `mcp_yunxiao_codeup_create_repo`（以模板创建、落在项目代码组下、"
                           + "`syncAllBranches=true`；参数名按工具 schema 原样小驼峰，写错即静默走默认值）；"
                           + "建完**必须**再 `mcp_yunxiao_codeup_get_repo` 取 `httpUrlToRepo` 与 `defaultBranch`；");
        builder.AppendLine("3) 等 pin 分支就绪（轮询 list_branches，上限 120s / 间隔 3s，超时即该端失败）；");
        builder.AppendLine("4) 目标目录已存在且非空：按 docs 的三选一处置——能自行判定就用脚本工具清理"
                           + "（**清理只读 pack 文件**：用 `RunPythonAsync` 的 `shutil.rmtree(..., onerror=清只读后重试)`，"
                           + "不要用裸删）；**无法自行判定时**写 `status=\"needsInput\"` 并在 `inputs[]` 里给出 "
                           + "`id=\"clone-conflict\"`（`kind=\"choice\"`、`options[].value` 取 reuse / reclone / abort）后**停止**；");
        builder.AppendLine("5) 克隆：`GitCloneAsync(url=<httpUrlToRepo>, branch=<pin>, directory=<仓库目录>)`"
                           + "（`<仓库目录>` 就是上面给的完整目录，**不要再拼仓库名**）；"
                           + "失败时若目录是本次新建的可清理残骸（用同一 Python 删法）；");
        builder.AppendLine("6) 占位符替换：按 docs 的规则表把模板占位符替换成项目命名空间 / 项目名 / 缩写（区分大小写、幂等），"
                           + "替换后残余必须为 0；把**实际替换的来源令牌**写进该端 `extra.placeholderSources`"
                           + "（令牌取值见 docs，逐字写实际命中的那几个，例如 `[\"<令牌原文>\", …]`；"
                           + "**至少写一项**——插件要按它独立扫描残余，空列表会被判为「占位符替换未完成＝该端未完成」）；");
        builder.AppendLine("7) 复核（**全部带 `workingDirectory=<仓库目录>`**）："
                           + "`GitStatusAsync(workingDirectory=<仓库目录>)` / "
                           + "`GitBranchListAsync(workingDirectory=<仓库目录>)` 确认工作树可用且 `refs/heads/<pin>` 存在，"
                           + "再写 `extra.cloned=true`、`extra.defaultBranch`；");
        builder.AppendLine("8) 该端闭环后，才进入下一端（回到 1)）。全部端闭环后才写结论文件。");
        builder.AppendLine();

        AppendArtifactSchema(builder, 3, @"{
  ""step"": 3,
  ""phase"": ""execute"",
  ""status"": ""done"",
  ""ok"": true,
  ""artifacts"": [
    { ""kind"": ""group"", ""name"": ""<代码组名>"", ""externalId"": ""<组 id>"", ""webUrl"": ""<链接>"",
      ""extra"": { ""kind"": ""group"", ""exists"": true, ""parentId"": null, ""organizationId"": ""<组织 ID>"" } },
    { ""kind"": ""repository"", ""end"": ""Service"", ""name"": ""<仓库名>"", ""externalId"": ""<仓库 id>"",
      ""webUrl"": ""<仓库页面链接>"", ""cloneUrl"": ""<httpUrlToRepo>"", ""localPath"": ""<仓库目录>"",
      ""templateBranch"": ""<pin 分支>"",
      ""extra"": { ""kind"": ""repository"", ""templateRepository"": ""<模板仓名>"", ""templateProjectId"": 123,
        ""templatePath"": ""<模板库全路径>"", ""templateBranches"": [""develop""], ""resolvedBranchSource"": ""<pin 来源>"",
        ""repositoryExists"": false, ""localPathOccupied"": false, ""visibility"": ""internal"", ""error"": null,
        ""placeholderSources"": [""<令牌原文>"", ""<令牌原文>""], ""cloned"": true, ""defaultBranch"": ""<默认分支>"" } }
  ],
  ""checks"": [ { ""name"": ""Service"", ""ok"": true, ""detail"": ""已建库并克隆，占位符残余 0"" } ],
  ""message"": ""<一句话总结>""
}");

        builder.AppendLine("注意：`checks[]` 必须**逐端一条**（`name` 取端标识 Service / Admin / Mobile），"
                           + "`artifacts[]` 里 `kind=repository` 也必须每端一条；漏端会被判为「该端未给结论」＝本步失败。"
                           + "`message` 只写一句话总结，**不要**在里面写「需要继续执行」这类待办——待办意味着本步还没做完，"
                           + "此时不该写结论文件。");
        builder.AppendLine();

        builder.AppendLine("需要用户答复时（无法自行判定的克隆冲突）用这个形态，并**停止**不要继续：");
        builder.AppendLine("```json");
        builder.AppendLine("{ \"step\": 3, \"status\": \"needsInput\", \"ok\": false,");
        builder.AppendLine("  \"inputs\": [ { \"id\": \"clone-conflict\", \"kind\": \"choice\", \"prompt\": \"目标目录已存在且非空，请选择处置方式\",");
        builder.AppendLine("      \"ends\": [\"Service\"], \"options\": [");
        builder.AppendLine("        { \"value\": \"reuse\", \"label\": \"复用已克隆\" },");
        builder.AppendLine("        { \"value\": \"reclone\", \"label\": \"清空后重新克隆\" },");
        builder.AppendLine("        { \"value\": \"abort\", \"label\": \"中止本步\" } ],");
        builder.AppendLine("      \"details\": { \"path\": \"<目标目录>\", \"entryCount\": 12 } } ],");
        builder.AppendLine("  \"message\": \"等待用户选择目录处置方式\" }");
        builder.AppendLine("```");
        builder.AppendLine();

        AppendResultContract(builder, resultFile, "DONE / NEEDS_INPUT / FAILED: <一句话原因>");

        return builder.ToString();
    }

    /// <summary>
    /// 构造 Step 6「推送改动」提示词（git 工具组，一次下发完成）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="targets">本次要推送的端（已克隆且带本地目录）。</param>
    /// <param name="resultFile">结论文件绝对路径。</param>
    /// <returns>提示词。</returns>
    public static string BuildPushPrompt(ProjectSpec spec, IReadOnlyList<EndRepository> targets, string resultFile)
    {
        var docs = DocsDirectory(spec);
        var builder = new StringBuilder();

        builder.AppendLine($"你是本项目（{Display(spec)}）的项目助手。");
        builder.AppendLine();
        builder.AppendLine("本次由「新建项目」向导的 Step 6 驱动你执行【推送改动到自有仓库】。");
        builder.AppendLine();

        AppendKnowledgeBounds(builder, docs, StepReportStatus.Failed);

        AppendHardBoundaries(builder, new[]
        {
            "只做推送相关动作：分支整理 / 暂存 / 提交 / 推送；**不要**改业务源码、不要建库、不要动云效流水线。",
            "git 动作一律用**框架 git 工具组**：GitStatusAsync / GitCheckoutAsync / GitAddAsync / GitCommitAsync / "
                + "GitPushAsync / GitLogAsync（云效 MCP 已无 git 类工具；**不要**用 codeup_push）。"
                + "**每个 git 调用都必须显式带 `workingDirectory=<该端仓库目录>`**（不传时框架在项目工作区根执行，会报「不是 git 仓库」或操作到别的目录）。",
            $"先读 `{docs}\\step6-push.md`（分支口径、提交身份、提交信息格式、**报错→处置对照表**、"
                + "限流退避：线性 10/20/30s 最多 3 次）。",
            "**git 凭据由用户在外部环境配置**（插件与技能都不提供凭据通路）：推送失败是凭据问题时，"
                + "把 git 原文报错带进结论，并固定提示「请先在外部环境配好 codeup HTTPS 凭据」；"
                + "**不要**把令牌拼进 URL、不要尝试改写 remote。",
            "禁止 `--force` / `git reset --hard` / `git clean -fd`；不要动其它分支、不要建标签。"
        });

        builder.AppendLine("【待推送的端】（目标分支按 docs 第 2 节解析：配置 `pushBranch` 有值用它，留空退回该端仓库默认分支）");
        foreach (var repository in targets)
        {
            var branch = string.IsNullOrWhiteSpace(repository.DefaultBranch)
                ? "（未配置，按 docs 解析该端仓库默认分支）"
                : repository.DefaultBranch;
            builder.AppendLine($"- end = {repository.End}（{EndName(repository.End)}）· 仓库 = {repository.RepositoryName}"
                               + $" · 仓库目录 = {repository.LocalPath} · 克隆地址 = {repository.CloneUrl}"
                               + $" · 本次目标分支 = {branch}");
        }
        builder.AppendLine();

        builder.AppendLine("【执行步骤】（逐端；下面所有 git 调用都要带 `workingDirectory=<该端仓库目录>`）");
        builder.AppendLine("1) `GitStatusAsync(workingDirectory=<仓库目录>)` 看工作树；无改动就跳过提交"
                           + "（如实报告，不产生空提交），仍继续推送；");
        builder.AppendLine("2) 切分支：已存在则 `GitCheckoutAsync(target=<目标分支>, workingDirectory=<仓库目录>)`；"
                           + "否则 `GitCheckoutAsync(target=<目标分支>, createBranch=true, workingDirectory=<仓库目录>)`"
                           + "（对应 `git checkout -b`）；");
        builder.AppendLine("3) `GitAddAsync(paths=[\".\"], workingDirectory=<仓库目录>)` 暂存全部改动"
                           + "（**不要**传 `-A`：工具会拼成 `git add -- -A` 当 pathspec，必然失败）；");
        builder.AppendLine("4) `GitCommitAsync(message=<消息>, workingDirectory=<仓库目录>)`（提交信息格式与提交身份按 docs："
                           + "优先本机 git config，缺失用 docs 的兜底身份）；");
        builder.AppendLine("5) `GitPushAsync(remote=\"origin\", branch=<目标分支>, setUpstream=true, workingDirectory=<仓库目录>)`；"
                           + "限流类失败按 docs 线性退避重试（10/20/30s，最多 3 次），其余失败不重试；");
        builder.AppendLine("6) 推送后取本次提交 id（`GitLogAsync(maxCount=1, workingDirectory=<仓库目录>)`），写进 `extra.commitId`。");
        builder.AppendLine();

        AppendArtifactSchema(builder, 6, @"{
  ""step"": 6,
  ""status"": ""done"",
  ""ok"": true,
  ""artifacts"": [
    { ""kind"": ""repository"", ""end"": ""Service"", ""name"": ""<仓库名>"", ""externalId"": ""<仓库 id>"",
      ""webUrl"": ""<仓库页面链接>"", ""cloneUrl"": ""<httpUrlToRepo>"", ""localPath"": ""<仓库目录>"",
      ""templateBranch"": ""<实际推送的分支>"",
      ""extra"": { ""kind"": ""repository"", ""cloned"": true, ""commitId"": ""<本次提交 id>"", ""error"": null } }
  ],
  ""checks"": [ { ""name"": ""Service"", ""ok"": true, ""detail"": ""已推送 <分支>（提交 <短 id>）"" } ],
  ""message"": ""<一句话总结>""
}");

        builder.AppendLine("注意：本步每端都要有 `kind=repository` 产物与同名 `checks` 项；"
                           + "`templateBranch` 填**实际推送的分支**；漏端会被判为「该端未给结论」＝本步失败。");
        builder.AppendLine();

        AppendResultContract(builder, resultFile, "DONE 或 FAILED: <失败端与原因>");

        return builder.ToString();
    }

    /// <summary>
    /// 构造 Step 7「流水线准备（只读）」提示词：渲染将要提交的 YAML 与待绑定清单，并列出部署目标候选。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="targets">本次要建流水线的端（已克隆）。</param>
    /// <param name="resultFile">结论文件绝对路径。</param>
    /// <returns>提示词。</returns>
    public static string BuildPipelinePreparePrompt(
        ProjectSpec spec,
        IReadOnlyList<EndRepository> targets,
        string resultFile)
    {
        var docs = DocsDirectory(spec);
        var builder = new StringBuilder();

        builder.AppendLine($"你是本项目（{Display(spec)}）的项目助手。");
        builder.AppendLine();
        builder.AppendLine("本次由「新建项目」向导的 Step 7 驱动你执行【流水线准备（只读）】：把「将要提交的 YAML」"
                           + "与「待人工绑定清单」算出来给用户复核。**本阶段不创建 / 不更新云效流水线**。");
        builder.AppendLine();

        AppendKnowledgeBounds(builder, docs, StepReportStatus.Failed);

        AppendHardBoundaries(builder, new[]
        {
            "本阶段只读：**禁止** flow_create_pipeline / flow_update_pipeline / flow_run_pipeline；"
                + "只允许只读的 flow_list_pipelines / flow_list_host_groups / flow_get_host_group。",
            $"先读 `{docs}\\pipeline-conventions.md`（名称格式 / 待人工绑定清单 / 覆盖更新口径 / 哪端有部署阶段）与"
                + $" 三端模板 `{docs}\\pipeline-service.yml.md` / `pipeline-admin.yml.md` / `pipeline-mobile.yml.md`。"
                + "**YAML 正文以这些 md 里的 `yaml` 代码块为准**，按 docs 的占位符表把项目值填进去。",
            "部署目标只在**后端**（含独立部署阶段）需要：用 `mcp_yunxiao_flow_list_host_groups` 列出候选，"
                + "把候选**同时带数字 id 与 uuid**写进 `inputs[]`（数字 id 用于拉机器明细，uuid 才是写进 YAML machineGroup 的值）。"
        });

        AppendOrganization(builder, spec);

        builder.AppendLine("【待建流水线的端】（`{branch}` 按 docs 解析：配置 `pushBranch` 有值用它，留空退回该端仓库默认分支）");
        foreach (var repository in targets)
        {
            var branch = string.IsNullOrWhiteSpace(repository.DefaultBranch)
                ? "（未配置，按 docs 解析该端仓库默认分支）"
                : repository.DefaultBranch;
            builder.AppendLine($"- end = {repository.End}（{EndName(repository.End)}）· 仓库 = {repository.RepositoryName}"
                               + $" · 克隆地址 = {repository.CloneUrl} · 本项目推送分支 = {branch}");
        }
        builder.AppendLine($"项目缩写（code）= {spec.Code}；项目名 = {spec.Name}。");
        builder.AppendLine();

        builder.AppendLine("【要做的事】");
        builder.AppendLine("1) 按 docs 的三端模板，为每端生成将要提交的完整新版 YAML（顶层 `stages:` 形态），"
                           + "把项目值（代码源地址 = 克隆地址、分支 = 上面的推送分支、镜像 / 桶 / 目录前缀等）按 docs 占位符表替换；");
        builder.AppendLine("2) 列出该端的**待人工绑定清单**（已展开占位符，逐条写清「在哪绑、绑什么」）；");
        builder.AppendLine("3) 后端：列出部署主机组候选（flow_list_host_groups），写进 `inputs[]` 的 `deploy-target` 项；"
                           + "按 docs 的默认口径（或唯一合理候选）选一个作为推荐值，在 `details.defaultValue` 给数字 id，"
                           + "并把该候选的 uuid 填进 `extra.recommendedDeployTargetUuid`、写进 YAML 的 `machineGroup`"
                           + "（后端 YAML 必须能直接提交，不能留占位符）；用户在面板改选后，由执行续跑按新 uuid 覆盖改写；");
        builder.AppendLine("4) `flow_list_pipelines`（pipelineName=本端流水线名）看是否已存在，已存在则填 `externalId`。");
        builder.AppendLine();

        AppendArtifactSchema(builder, 7, @"{
  ""step"": 7,
  ""status"": ""done"",
  ""ok"": true,
  ""artifacts"": [
    { ""kind"": ""pipeline"", ""end"": ""Service"", ""name"": ""<流水线名>"", ""externalId"": ""<已存在的流水线 id 或 null>"",
      ""webUrl"": ""<链接或空>"", ""yaml"": ""<将要提交的完整 YAML 正文>"",
      ""extra"": { ""kind"": ""pipeline"", ""yamlIncludesDeployStage"": true,
        ""pendingBindings"": [""<待绑定项 1>"", ""<待绑定项 2>""],
        ""recommendedDeployTargetHostGroupId"": 123, ""recommendedDeployTargetUuid"": ""<uuid>"" } }
  ],
  ""inputs"": [
    { ""id"": ""deploy-target"", ""kind"": ""select"", ""prompt"": ""请为后端选择部署主机组"", ""ends"": [""Service""],
      ""options"": [ { ""value"": ""123"", ""uuid"": ""<uuid>"", ""label"": ""<主机组名 · 台数 · 地域>"" } ],
      ""details"": { ""defaultValue"": ""123"", ""required"": true } }
  ],
  ""checks"": [ { ""name"": ""Service"", ""ok"": true, ""detail"": ""YAML 已渲染，含部署阶段"" } ],
  ""message"": ""<一句话总结>""
}");

        builder.AppendLine("要点：`yaml` 必须是**完整可提交**的正文（不是片段、不是占位符残留）；"
                           + "`extra.pendingBindings` 逐条可操作；只有后端给部署目标输入项（前端端 `yamlIncludesDeployStage=false`）。");
        builder.AppendLine();

        AppendResultContract(builder, resultFile, "DONE 或 FAILED: <一句话原因>");

        return builder.ToString();
    }

    /// <summary>
    /// 构造 Step 7「执行续跑」提示词：按用户确认（含手工微调与部署目标）创建 / 覆盖更新流水线。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="targets">本次要建流水线的端（已克隆）。</param>
    /// <param name="prepareResultFile">准备阶段结论文件路径。</param>
    /// <param name="resultFile">本次结论文件绝对路径。</param>
    /// <param name="yamlOverrides">按端手工微调的 YAML（<c>null</c>／缺项＝用准备阶段的 YAML）。</param>
    /// <param name="deployTargets">按端选定的部署目标（<c>null</c>／缺项＝用准备阶段的推荐值）。</param>
    /// <param name="update"><c>true</c>＝显式覆盖更新（按档案里的流水线 id 直改）。</param>
    /// <returns>提示词。</returns>
    public static string BuildPipelineExecutePrompt(
        ProjectSpec spec,
        IReadOnlyList<EndRepository> targets,
        string prepareResultFile,
        string resultFile,
        IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides,
        IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets,
        bool update)
    {
        var docs = DocsDirectory(spec);
        var builder = new StringBuilder();

        builder.AppendLine($"你是本项目（{Display(spec)}）的项目助手。");
        builder.AppendLine();
        builder.AppendLine(update
            ? "本次由「新建项目」向导的 Step 7 驱动你执行【覆盖更新云效流水线】。"
            : "本次由「新建项目」向导的 Step 7 驱动你执行【创建云效流水线】。");
        builder.AppendLine();

        AppendKnowledgeBounds(builder, docs, StepReportStatus.Failed);

        AppendHardBoundaries(builder, new[]
        {
            $"先读 `{docs}\\pipeline-conventions.md` 与 `{docs}\\pipeline-service.yml.md` / `pipeline-admin.yml.md` / "
                + "`pipeline-mobile.yml.md`（模板与覆盖更新口径）。",
            "写入只允许 flow_create_pipeline / flow_update_pipeline；**禁止** flow_run_pipeline（建完不自动运行）。",
            "创建前按流水线名精确检索（flow_list_pipelines）：**命中同名即对该条覆盖更新本次 YAML**（不要「命中即复用旧条」——"
                + "旧流水线可能缺部署段或残留旧占位符）；未命中才创建。**显式覆盖更新**则直接改档案里那条 id，不检索。",
            "需要部署阶段的端（后端）若没有可用目标 uuid，**不要**把占位符提交到云效：写 `status=\"failed\"` 并说明缺部署目标。"
        });

        AppendOrganization(builder, spec);

        builder.AppendLine("【事实来源：准备阶段结论文件】");
        builder.AppendLine(prepareResultFile);
        builder.AppendLine("（YAML 正文 / 待绑定清单 / 推荐部署目标以它为准；用户手工微调的端以本轮内联的微调正文优先。）");
        builder.AppendLine();

        builder.AppendLine("【本次要处理的端】");
        foreach (var repository in targets)
        {
            var overrideYaml = TryGetOverride(yamlOverrides, repository.End);
            var target = TryGetTarget(deployTargets, repository.End);

            builder.AppendLine($"- end = {repository.End}（{EndName(repository.End)}）· 仓库 = {repository.RepositoryName}"
                               + $" · 克隆地址 = {repository.CloneUrl}"
                               + $" · 推送分支 = {(string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "（按 docs 解析该端默认分支）" : repository.DefaultBranch)}");
            if (target is not null)
                builder.AppendLine($"　部署目标（用户已选）：名称 = {target.Name} · uuid = {target.Uuid} · 数字 id = {target.HostGroupId}"
                                   + $"（写进 YAML machineGroup 的是 **uuid**）");
            if (overrideYaml is not null)
            {
                builder.AppendLine("　用户手工微调后的 YAML（**以此为准**，原样提交）：");
                builder.AppendLine("```yaml");
                builder.AppendLine(overrideYaml);
                builder.AppendLine("```");
            }
        }
        builder.AppendLine();

        builder.AppendLine("【执行步骤】（逐端）");
        builder.AppendLine("1) 定 YAML：用户微调则用微调正文，否则用准备阶段结论里该端的 `yaml`；"
                           + "后端：用户已选部署目标就用它的 uuid 覆盖 `machineGroup`，未选则沿用准备阶段 YAML 里的 uuid"
                           + "（**不得**留空或留占位符；两者都没有 ⇒ 按硬边界写 `failed`），缩进与键名按 docs；");
        builder.AppendLine("2) 创建入口：`flow_list_pipelines`（pipelineName=流水线名）→ 命中则 `flow_update_pipeline`"
                           + "（`pipelineId`=命中 id、`name`=该流水线现名、`content`=完整 YAML；三个键名按 schema 原样，"
                           + "**漏 `name` 会被拒、键名写错取不到值**），未命中则 `flow_create_pipeline`（`name` + `content`）；"
                           + "覆盖更新入口：直接 `flow_update_pipeline`（`pipelineId`=档案里的现 id、`name`=现名、`content`=YAML）；");
        builder.AppendLine("3) 取流水线 id 与页面链接（由 id 推导），写进产物 `externalId` / `webUrl`；"
                           + "`extra.yamlIncludesDeployStage` 与 `extra.pendingBindings` 照准备阶段口径；");
        builder.AppendLine("4) **建完不自动运行**（不要调 flow_run_pipeline）。");
        builder.AppendLine();

        AppendArtifactSchema(builder, 7, @"{
  ""step"": 7,
  ""status"": ""done"",
  ""ok"": true,
  ""artifacts"": [
    { ""kind"": ""pipeline"", ""end"": ""Service"", ""name"": ""<流水线名>"", ""externalId"": ""<流水线 id>"",
      ""webUrl"": ""<流水线链接>"", ""yaml"": ""<本次提交的完整 YAML>"",
      ""extra"": { ""kind"": ""pipeline"", ""yamlIncludesDeployStage"": true,
        ""pendingBindings"": [""<待绑定项>""], ""recommendedDeployTargetUuid"": ""<uuid>"" } }
  ],
  ""checks"": [ { ""name"": ""Service"", ""ok"": true, ""detail"": ""已创建（含部署阶段；2 项待人工绑定）"" } ],
  ""message"": ""<一句话总结>""
}");

        AppendResultContract(builder, resultFile, "DONE 或 FAILED: <失败端与原因>");

        return builder.ToString();
    }

    /// <summary>追加结论文件的 <c>artifacts[]</c> / <c>status</c> 契约与 JSON 骨架。</summary>
    /// <param name="builder">缓冲。</param>
    /// <param name="step">步骤号。</param>
    /// <param name="sample">JSON 骨架（原文）。</param>
    /// <param name="statuses">本阶段允许的 <c>status</c> 取值（默认执行阶段三态）。</param>
    private static void AppendArtifactSchema(StringBuilder builder, int step, string sample, string? statuses = null)
    {
        builder.AppendLine("【结论文件 JSON 契约】（把下面的 JSON 写入结论文件，UTF-8；用 WriteFileAsync 或 Python 写出均可）");
        builder.AppendLine("```json");
        builder.AppendLine(sample);
        builder.AppendLine("```");
        builder.AppendLine($"字段说明：`status` ∈ {statuses ?? "done / needsInput / failed"}；"
                           + "`ok` 表示本阶段结论是否通过（准备阶段：`ready` 为 true、`blocked` 为 false；"
                           + "执行阶段：仅 `status=done` 且全部通过时为 true）；"
                           + "`artifacts[].extra` 是**具名类型**（由 `extra.kind` 判别，只有 group / repository / pipeline 三选一）；"
                           + "`checks[]` 逐项记录（Step 6/7 的 `name` 用端标识 Service / Admin / Mobile）；"
                           + "`inputs[]` 仅在 `status=needsInput` 时给。");
        builder.AppendLine();
    }

    /// <summary>
    /// 追加「知识正文是硬前置」的边界（方案 A）：文档不可读即停止并写失败结论，禁止凭记忆执行。
    /// </summary>
    /// <param name="builder">缓冲。</param>
    /// <param name="docs">知识正文目录。</param>
    /// <param name="failureStatus">读不到文档时本阶段应写的 <c>status</c>（准备阶段 <c>blocked</c>，其余 <c>failed</c>）。</param>
    private static void AppendKnowledgeBounds(StringBuilder builder, string docs, string failureStatus)
    {
        builder.AppendLine("【知识正文是硬前置】");
        builder.AppendLine($"本轮下发前插件已校验并铺装 `{docs}`。你必须先完整读取下文列出的知识正文再动手；"
                           + "若读取失败（文件不存在 / 不可读 / 内容为空），**立即停止**，按结论文件契约写 "
                           + $"`status=\"{failureStatus}\"`、`ok=false`、`checks[]` 记一项「知识正文不可读：<文件名>」并结束本轮，"
                           + "**不得**凭记忆、猜测或通用经验继续执行本步（组织专有口径只能来自知识正文）。");
        builder.AppendLine();
    }

    /// <summary>docs 知识正文在运行期的目录（Step 2 已复制进项目工作区）。</summary>
    /// <param name="spec">项目档案。</param>
    /// <returns>目录绝对路径（工作区未就绪时给出占位提示）。</returns>
    private static string DocsDirectory(ProjectSpec spec)
        => string.IsNullOrWhiteSpace(spec.LocalPath)
            ? @"<项目工作区>\.luban-agent\plugin-newproject\docs"
            : Path.Combine(spec.LocalPath, ".luban-agent", "plugin-newproject", "docs");

    /// <summary>取某端的手工微调 YAML（空白视为未微调）。</summary>
    /// <param name="yamlOverrides">按端微调表（可为 <c>null</c>）。</param>
    /// <param name="end">端。</param>
    /// <returns>微调原文；未提供返回 <c>null</c>。</returns>
    private static string? TryGetOverride(IReadOnlyDictionary<ProjectEnd, string>? yamlOverrides, ProjectEnd end)
        => yamlOverrides is not null && yamlOverrides.TryGetValue(end, out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    /// <summary>取某端选定的部署目标（uuid 空视为未选）。</summary>
    /// <param name="deployTargets">按端目标表（可为 <c>null</c>）。</param>
    /// <param name="end">端。</param>
    /// <returns>目标；未选返回 <c>null</c>。</returns>
    private static DeployHostGroup? TryGetTarget(IReadOnlyDictionary<ProjectEnd, DeployHostGroup>? deployTargets, ProjectEnd end)
        => deployTargets is not null
           && deployTargets.TryGetValue(end, out var target)
           && !string.IsNullOrWhiteSpace(target.Uuid)
            ? target
            : null;

    /// <summary>追加三条共用硬边界。</summary>
    /// <param name="builder">缓冲。</param>
    /// <param name="stepSpecific">本步特有的边界（放在最前）。</param>
    private static void AppendHardBoundaries(StringBuilder builder, IReadOnlyList<string> stepSpecific)
    {
        builder.AppendLine("【硬边界】");
        var index = 1;
        foreach (var item in stepSpecific)
        {
            builder.AppendLine($"{index}) {item}");
            index++;
        }

        builder.AppendLine($"{index}) 不要调用任何 newproject-* 技能，也不要调用「新建项目」插件的任何工具或服务"
                           + "（本轮提示词正是由该插件在同一会话发起的，回头调用会形成同会话回合的自等死锁）。");
        index++;
        builder.AppendLine($"{index}) 回复正文只回一行结论，不要展开过程；机器判定一律以下面的结论文件为准。");
        builder.AppendLine();
    }

    /// <summary>
    /// 追加云效组织 ID（运行时配置，不是知识文档里的值）：要求每次 <c>mcp_yunxiao_*</c> 调用显式传同一个值。
    /// </summary>
    /// <remarks>
    /// 为什么要写死在提示词里：<c>organizationId</c> 是云效中心版必带参数，但它来自 codex「设置 → 云效连接」，
    /// <c>docs/org-reference.md</c> 刻意不存。此前提示词不给值、schema 只留 <c>&lt;组织 ID&gt;</c> 占位符，
    /// 助手只能写 <c>null</c>，执行时再传空串——空串会绕过客户端「未提供则回落配置」的兜底并抛「未配置组织 ID」。
    /// </remarks>
    /// <param name="builder">缓冲。</param>
    /// <param name="spec">项目档案（<see cref="ProjectSpec.OrganizationId"/> 由服务端在准备 / 执行前回填）。</param>
    private static void AppendOrganization(StringBuilder builder, ProjectSpec spec)
    {
        builder.AppendLine("【云效组织 ID（运行时配置，照抄，不要另找）】");
        if (string.IsNullOrWhiteSpace(spec.OrganizationId))
        {
            builder.AppendLine("**未配置**：本步禁止调用任何 `mcp_yunxiao_*` 工具，直接写 `status=\"failed\"` 并说明"
                               + "「请先到 codex「设置 → 云效连接」填写组织 ID」。");
        }
        else
        {
            builder.AppendLine($"`organizationId` = `{spec.OrganizationId}`。**每次调用 `mcp_yunxiao_*` 都必须显式传这个值**"
                               + "（不要留空、不要传空串、不要从知识文档里另找一个）；结论产物 `extra.organizationId` 写同值。");
        }

        builder.AppendLine();
    }

    /// <summary>追加结论文件约定与收尾要求。</summary>
    /// <param name="builder">缓冲。</param>
    /// <param name="resultFile">结论文件绝对路径。</param>
    /// <param name="lastLine">正文收尾格式。</param>
    private static void AppendResultContract(StringBuilder builder, string resultFile, string lastLine)
    {
        builder.AppendLine("【结论文件（必须原样使用这个路径，不要改动、不要另存到别处）】");
        builder.AppendLine(resultFile);
        builder.AppendLine();
        builder.AppendLine("写完结论文件后，回复正文只回一行：" + lastLine);
        builder.AppendLine();
        builder.AppendLine("补充：除结论文件外，不要新建 / 修改 / 删除项目里的任何文件。");
    }

    /// <summary>项目展示名（名称 + 缩写）。</summary>
    /// <param name="spec">项目档案。</param>
    /// <returns>展示名。</returns>
    private static string Display(ProjectSpec spec)
        => string.IsNullOrWhiteSpace(spec.Name) ? spec.Code : $"{spec.Name}，缩写 {spec.Code}";

    /// <summary>端的中文名。</summary>
    /// <param name="end">端。</param>
    /// <returns>中文名。</returns>
    private static string EndName(ProjectEnd end) => end switch
    {
        ProjectEnd.Service => "后端",
        ProjectEnd.Admin => "管理端",
        ProjectEnd.Mobile => "移动端",
        _ => end.ToString()
    };
}