/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStepPromptsUnitTest
*创建时间：2026/10/21
*描述：Step 4 / Step 5 提示词单测——锁死 M4-P4 定案：
*      「口令内联（方案 A）」「禁止回头调用 newproject-* 技能与服务（同会话回合自等死锁）」
*      「结论文件路径原样下发」「Step 5 逐端清单与端标识」。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectStepPromptsUnitTest
{
    private const string ResultFile = @"D:\ws\jnms\.luban-agent\plugin-newproject\results\step4-20261021-101010-123.json";

    /// <summary>造一份最小可用档案。</summary>
    private static ProjectSpec Spec() => new()
    {
        Name = "江南管理系统",
        Code = "jnms",
        LocalPath = @"D:\ws\jnms",
        Ends = new List<ProjectEnd> { ProjectEnd.Service, ProjectEnd.Admin }
    };

    [TestMethod]
    public void BuildDatabaseProbePrompt_失败时口令明文内联且不复述约束到位()
    {
        var request = new DatabaseProbeRequest
        {
            Host = "10.10.0.214",
            Port = 3306,
            Database = "tjc_dev_jnms",
            User = "testuser",
            Password = "S3cr3t-Pwd"
        };

        var prompt = StepPrompts.BuildDatabaseProbePrompt(Spec(), request, ResultFile);

        // 方案 A（用户定案）：口令内联进提示词；同时要求「不写文件 / 不回显」
        StringAssert.Contains(prompt, "password = S3cr3t-Pwd");
        StringAssert.Contains(prompt, "不要写进任何文件");
        StringAssert.Contains(prompt, "不要回显");

        // 参数齐全，助手无需再猜
        StringAssert.Contains(prompt, "server   = 10.10.0.214");
        StringAssert.Contains(prompt, "database = tjc_dev_jnms");
        StringAssert.Contains(prompt, "user     = testuser");

        // 结论文件路径原样下发
        StringAssert.Contains(prompt, ResultFile);
    }

    [TestMethod]
    public void BuildDatabaseProbePrompt_只探活不建库不写文件不阻断()
    {
        var prompt = StepPrompts.BuildDatabaseProbePrompt(Spec(), new DatabaseProbeRequest
        {
            Host = "h",
            Database = "d",
            User = "u"
        }, ResultFile);

        StringAssert.Contains(prompt, "只做一件事");            // 只探活
        StringAssert.Contains(prompt, "禁止建库");
        StringAssert.Contains(prompt, "禁止执行任何 SQL");
        StringAssert.Contains(prompt, "CREATE");                 // 明确点名 DDL 家族
        StringAssert.Contains(prompt, "DROP");
        StringAssert.Contains(prompt, "禁止修改项目里的任何文件");
        StringAssert.Contains(prompt, "不阻断");                  // 失败只记录、不拦流程
    }

    [TestMethod]
    public void BuildDatabaseProbePrompt_禁止回头调用插件技能与服务()
    {
        // 同会话回合串行：助手若回头调 newproject-* / 插件服务会自等死锁
        var prompt = StepPrompts.BuildDatabaseProbePrompt(Spec(), new DatabaseProbeRequest
        {
            Host = "h",
            Database = "d",
            User = "u"
        }, ResultFile);

        StringAssert.Contains(prompt, "newproject-*");
        StringAssert.Contains(prompt, "同会话回合");
        StringAssert.Contains(prompt, "自等死锁");
        StringAssert.Contains(prompt, "机器判定一律以下面的结论文件为准");
    }

    [TestMethod]
    public void BuildCompilePrompt_逐端清单带本地目录与端标识()
    {
        var targets = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "jnms-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" },
            new() { End = ProjectEnd.Admin, RepositoryName = "jnms-Admin", LocalPath = @"D:\ws\jnms\csxmy-Admin" }
        };

        var prompt = StepPrompts.BuildCompilePrompt(Spec(), targets, ResultFile);

        StringAssert.Contains(prompt, "name = Service（后端）");
        StringAssert.Contains(prompt, "本地目录 = D:\\ws\\jnms\\Csxmy-Service");
        StringAssert.Contains(prompt, "name = Admin（管理端）");
        StringAssert.Contains(prompt, "本地目录 = D:\\ws\\jnms\\csxmy-Admin");
        StringAssert.Contains(prompt, ResultFile);
    }

    [TestMethod]
    public void BuildCompilePrompt_编译方式交助手判断且不改业务源码()
    {
        var targets = new List<EndRepository>
        {
            new() { End = ProjectEnd.Admin, RepositoryName = "jnms-Admin", LocalPath = @"D:\ws\jnms\csxmy-Admin" }
        };

        var prompt = StepPrompts.BuildCompilePrompt(Spec(), targets, ResultFile);

        // PRD §4.3 Step 5（v0.22）：编译方式由助手按工程类型决定
        StringAssert.Contains(prompt, "编译方式由你按工程类型决定");
        StringAssert.Contains(prompt, "dotnet build");

        // v0.28：本组织已知构建姿势（管理端：develop + Node16 + npm run build:dev）
        StringAssert.Contains(prompt, "本组织已知构建姿势");
        StringAssert.Contains(prompt, "不要用 pnpm");
        StringAssert.Contains(prompt, "develop");
        StringAssert.Contains(prompt, "fnm exec --using=16 -- npm.cmd install --registry=https://registry.npmmirror.com");
        StringAssert.Contains(prompt, "fnm exec --using=16 -- npm.cmd run build:dev");
        StringAssert.Contains(prompt, "不是 npm run build");
        StringAssert.Contains(prompt, "Failed to resolve entry for package");

        // v0.29：半装包只能删目录重装，rebuild 无效（上一轮真机失败的对策纠正）
        StringAssert.Contains(prompt, "删掉该包目录再装回来");
        StringAssert.Contains(prompt, "rmSync('node_modules/X'");
        StringAssert.Contains(prompt, "不要用 npm rebuild X");

        // v0.28 兜底：环境缺件时如实上报、不自行装全局工具链、不改仓库配置
        StringAssert.Contains(prompt, "不要**自行安装全局工具链");
        StringAssert.Contains(prompt, "由用户补好环境后重跑");

        // v0.28：边界由「只编译、不修复」放宽为「只编译、不改业务」——可修依赖树与环境，禁改业务源码与工程配置
        StringAssert.Contains(prompt, "允许修依赖树与环境");
        StringAssert.Contains(prompt, "禁止修改仓库里的业务源码与工程配置");

        // 命令白名单（与 AssistantToolPolicy 同口径）
        StringAssert.Contains(prompt, "白名单");
        StringAssert.Contains(prompt, "dotnet");
        StringAssert.Contains(prompt, "pip");
        StringAssert.Contains(prompt, "fnm");
        StringAssert.Contains(prompt, "cnpm");
    }

    [TestMethod]
    public void BuildCompilePrompt_半装包按删目录重装且必须重跑构建()
    {
        var targets = new List<EndRepository>
        {
            new() { End = ProjectEnd.Admin, RepositoryName = "jnms-Admin", LocalPath = @"D:\ws\jnms\csxmy-Admin" }
        };

        var prompt = StepPrompts.BuildCompilePrompt(Spec(), targets, ResultFile);

        // 判定半装看入口文件是否缺失，而不是盲信 npm install 的 exit 0
        StringAssert.Contains(prompt, "es/index.mjs");
        StringAssert.Contains(prompt, "只核对包目录是否存在，不校验文件完整性");

        // 修法：删单包目录后重装；整树重建只在多包残缺时用（可能超 2 分钟）
        StringAssert.Contains(prompt, "rmSync('node_modules/X'");
        StringAssert.Contains(prompt, "npm.cmd ci");

        // 装依赖不能再被管道截断（上一轮 npm install 就是中途被杀，留下半装包）
        StringAssert.Contains(prompt, "装依赖必须独占一条命令");
        StringAssert.Contains(prompt, "| Select-Object / | Select-String / | head");

        // 动过依赖必须重跑构建，不许拿修复前的报错收尾
        StringAssert.Contains(prompt, "修了依赖就必须重跑构建验证");

        // 重试次数从 1 放宽到 2（第二次必须含上面的半装修复动作）
        StringAssert.Contains(prompt, "每个端最多重试 2 次");
    }

    [TestMethod]
    public void BuildCompilePrompt_漏端即失败的口径写进提示词()
    {
        var targets = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "jnms-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" }
        };

        var prompt = StepPrompts.BuildCompilePrompt(Spec(), targets, ResultFile);

        StringAssert.Contains(prompt, "checks 必须逐端齐全");
        StringAssert.Contains(prompt, "漏写某个端");
        StringAssert.Contains(prompt, "本步失败");
        StringAssert.Contains(prompt, "\"step\": 5");
    }

    [TestMethod]
    public void BuildDatabaseProbePrompt_结论文件schema写死step4()
    {
        var prompt = StepPrompts.BuildDatabaseProbePrompt(Spec(), new DatabaseProbeRequest
        {
            Host = "h",
            Database = "d",
            User = "u"
        }, ResultFile);

        StringAssert.Contains(prompt, "\"step\": 4");
        StringAssert.Contains(prompt, "\"checks\"");
        StringAssert.Contains(prompt, "\"ok\"");
        StringAssert.Contains(prompt, "pymysql");
    }

    /// <summary>Step 3 准备：仓库目录术语必须明确「已含仓库名」，避免助手再拼一次仓库名。</summary>
    [TestMethod]
    public void BuildRepositoryPreparePrompt_仓库目录术语与只读边界()
    {
        var plan = new List<EndRepository>
        {
            new()
            {
                End = ProjectEnd.Service,
                RepositoryName = "Csxmy-Service",
                LocalPath = @"D:\ws\jnms\Csxmy-Service",
                TemplateRepository = "TJCES-Service"
            }
        };

        var prompt = StepPrompts.BuildRepositoryPreparePrompt(Spec(), plan, ResultFile);

        StringAssert.Contains(prompt, "仓库目录 = D:\\ws\\jnms\\Csxmy-Service（已含仓库名，勿再拼一次）");
        StringAssert.Contains(prompt, "mcp_yunxiao_codeup_list_template_repos");
        StringAssert.Contains(prompt, "mcp_yunxiao_codeup_get_repo");
        StringAssert.Contains(prompt, "本阶段只读");
    }

    /// <summary>Step 3 执行：git 调用必须带 workingDirectory，且暂存全部不能传 -A。</summary>
    [TestMethod]
    public void BuildRepositoryExecutePrompt_git调用带工作目录且禁用A()
    {
        var plan = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "Csxmy-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" }
        };

        var prompt = StepPrompts.BuildRepositoryExecutePrompt(Spec(), plan, ResultFile, ResultFile);

        StringAssert.Contains(prompt, "GitCloneAsync(url=<httpUrlToRepo>, branch=<pin>, directory=<仓库目录>)");
        StringAssert.Contains(prompt, "workingDirectory=<仓库目录>");
        StringAssert.Contains(prompt, "GitAddAsync(paths=[\".\"], …)");
        StringAssert.Contains(prompt, "-A");
        StringAssert.Contains(prompt, "syncAllBranches=true");
        StringAssert.Contains(prompt, "至少写一项");
    }

    /// <summary>
    /// 方案 D（2026-10-23 真机：助手克隆完 Service 就写 failed 结论，Admin 未克隆、两端未替换占位符）：
    /// 提示词必须锁死「逐端闭环 + 收尾自检 + 禁止用结论文件代替执行」。
    /// </summary>
    [TestMethod]
    public void BuildRepositoryExecutePrompt_逐端闭环且禁止用结论文件代替执行()
    {
        var plan = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "Csxmy-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" },
            new() { End = ProjectEnd.Admin, RepositoryName = "Csxmy-Admin", LocalPath = @"D:\ws\jnms\Csxmy-Admin" }
        };

        var prompt = StepPrompts.BuildRepositoryExecutePrompt(Spec(), plan, ResultFile, ResultFile);

        // 逐端闭环：拒绝「先把所有端建库、再统一克隆」
        StringAssert.Contains(prompt, "逐端闭环");
        StringAssert.Contains(prompt, "一端没走完不许收尾");
        StringAssert.Contains(prompt, "禁止");
        StringAssert.Contains(prompt, "统一克隆");

        // 禁止写半成品结论收尾
        StringAssert.Contains(prompt, "任一端未闭环时禁止写结论文件收尾");
        StringAssert.Contains(prompt, "不得");
        StringAssert.Contains(prompt, "代替执行");
        StringAssert.Contains(prompt, "单端失败不豁免其它端");

        // 每端六项收尾自检
        StringAssert.Contains(prompt, "收尾自检");
        StringAssert.Contains(prompt, "六项全绿");
        StringAssert.Contains(prompt, "placeholderSources` **非空**");

        // 逐端 checks 完备性 + message 不得写待办
        StringAssert.Contains(prompt, "`checks[]` 必须**逐端一条**");
        StringAssert.Contains(prompt, "需要继续执行");
    }

    /// <summary>
    /// 方案 D：插件只落账整体通过的轮次，所以计划里的 Cloned=true 必须作为「已完成端」下发，
    /// 助手跳过建库/克隆只补占位符与复核（否则每次重跑都从第 1 步重来，长链路易再次半途收尾）。
    /// </summary>
    [TestMethod]
    public void BuildRepositoryExecutePrompt_已克隆端只补占位符与复核()
    {
        var plan = new List<EndRepository>
        {
            new()
            {
                End = ProjectEnd.Service,
                RepositoryName = "Csxmy-Service",
                LocalPath = @"D:\ws\jnms\Csxmy-Service",
                Cloned = true
            },
            new()
            {
                End = ProjectEnd.Admin,
                RepositoryName = "Csxmy-Admin",
                LocalPath = @"D:\ws\jnms\Csxmy-Admin",
                Cloned = false
            }
        };

        var prompt = StepPrompts.BuildRepositoryExecutePrompt(Spec(), plan, ResultFile, ResultFile);

        StringAssert.Contains(prompt, "【已落账的端（上一次已建库并克隆，本轮只补缺失）】");
        StringAssert.Contains(prompt, "跳过 1)~5)");
        StringAssert.Contains(prompt, "只做 6) 占位符替换 + 7) 复核");
        StringAssert.Contains(prompt, "状态 = 已克隆（跳过建库与克隆，只补占位符替换与复核）");
        StringAssert.Contains(prompt, "状态 = 未克隆（1)~5) 全做）");
    }

    /// <summary>Step 6：分支不写死 develop、git 调用带 workingDirectory、暂存用 paths=["."]。</summary>
    [TestMethod]
    public void BuildPushPrompt_推送分支与git参数口径正确()
    {
        var targets = new List<EndRepository>
        {
            new()
            {
                End = ProjectEnd.Service,
                RepositoryName = "Csxmy-Service",
                LocalPath = @"D:\ws\jnms\Csxmy-Service",
                CloneUrl = "https://codeup.example/Csxmy-Service.git",
                DefaultBranch = "develop"
            }
        };

        var prompt = StepPrompts.BuildPushPrompt(Spec(), targets, ResultFile);

        StringAssert.Contains(prompt, "本次目标分支 = develop");
        StringAssert.Contains(prompt, "GitStatusAsync(workingDirectory=<仓库目录>)");
        StringAssert.Contains(prompt, "GitAddAsync(paths=[\".\"], workingDirectory=<仓库目录>)");
        StringAssert.Contains(prompt, "GitPushAsync(remote=\"origin\", branch=<目标分支>, setUpstream=true, workingDirectory=<仓库目录>)");
        Assert.IsFalse(prompt.Contains("GitAddAsync(paths=[\"-A\"])", StringComparison.Ordinal));
    }

    /// <summary>Step 7：准备必须给出可直接提交的 uuid；执行必须带 pipelineId / name / content 三个键名。</summary>
    [TestMethod]
    public void BuildPipelinePrompts_部署目标与flow参数口径正确()
    {
        var targets = new List<EndRepository>
        {
            new()
            {
                End = ProjectEnd.Service,
                RepositoryName = "Csxmy-Service",
                LocalPath = @"D:\ws\jnms\Csxmy-Service",
                CloneUrl = "https://codeup.example/Csxmy-Service.git",
                DefaultBranch = "develop"
            }
        };

        var prepare = StepPrompts.BuildPipelinePreparePrompt(Spec(), targets, ResultFile);
        StringAssert.Contains(prepare, "flow_list_host_groups");
        StringAssert.Contains(prepare, "不能留占位符");
        StringAssert.Contains(prepare, "recommendedDeployTargetUuid");
        StringAssert.Contains(prepare, "本项目推送分支 = develop");

        var execute = StepPrompts.BuildPipelineExecutePrompt(Spec(), targets, ResultFile, ResultFile, null, null, false);
        StringAssert.Contains(execute, "`pipelineId`=命中 id、`name`=该流水线现名、`content`=完整 YAML");
        StringAssert.Contains(execute, "推送分支 = develop");
        StringAssert.Contains(execute, "留空或留占位符");
    }

    /// <summary>v0.34：组织 ID 必须注入 Step 3 / Step 7 的准备与执行四段提示词（不得让助手猜 / 传空串）。</summary>
    [TestMethod]
    public void BuildRepositoryAndPipelinePrompts_注入组织ID并禁止空串()
    {
        const string Org = "648c2f9b44b3d9a1d1684d9";
        var spec = Spec();
        spec.OrganizationId = Org;
        var plan = new List<EndRepository>
        {
            new()
            {
                End = ProjectEnd.Service,
                RepositoryName = "Csxmy-Service",
                LocalPath = @"D:\ws\jnms\Csxmy-Service",
                CloneUrl = "https://codeup.example/Csxmy-Service.git"
            }
        };

        var prompts = new[]
        {
            StepPrompts.BuildRepositoryPreparePrompt(spec, plan, ResultFile),
            StepPrompts.BuildRepositoryExecutePrompt(spec, plan, ResultFile, ResultFile),
            StepPrompts.BuildPipelinePreparePrompt(spec, plan, ResultFile),
            StepPrompts.BuildPipelineExecutePrompt(spec, plan, ResultFile, ResultFile, null, null, false)
        };

        foreach (var prompt in prompts)
        {
            StringAssert.Contains(prompt, "【云效组织 ID（运行时配置，照抄，不要另找）】");
            StringAssert.Contains(prompt, $"`organizationId` = `{Org}`");
            StringAssert.Contains(prompt, "每次调用 `mcp_yunxiao_*` 都必须显式传这个值");
            StringAssert.Contains(prompt, "不要传空串");
        }
    }

    /// <summary>v0.34：档案没有组织 ID 时，提示词明确禁止调用云效 MCP（避免助手瞎猜或传空串）。</summary>
    [TestMethod]
    public void BuildRepositoryPreparePrompt_缺组织ID时禁止调用云效MCP()
    {
        var plan = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "Csxmy-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" }
        };

        var prompt = StepPrompts.BuildRepositoryPreparePrompt(Spec(), plan, ResultFile);

        StringAssert.Contains(prompt, "**未配置**");
        StringAssert.Contains(prompt, "本步禁止调用任何 `mcp_yunxiao_*` 工具");
        StringAssert.Contains(prompt, "设置 → 云效连接");
    }

    /// <summary>v0.34：Step 3 执行提示词锁死脚本口径（目录判定 ListDirectoryAsync、扫描/替换 RunPythonAsync）。</summary>
    [TestMethod]
    public void BuildRepositoryExecutePrompt_脚本口径避免被白名单拒()
    {
        var plan = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "Csxmy-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" }
        };

        var prompt = StepPrompts.BuildRepositoryExecutePrompt(Spec(), plan, ResultFile, ResultFile);

        StringAssert.Contains(prompt, "`ListDirectoryAsync` 判断");
        StringAssert.Contains(prompt, "`RunPythonAsync`");
        StringAssert.Contains(prompt, "名单外的可执行文件");
    }

    /// <summary>
    /// 方案 A：Step 3 准备结论用 phase=prepare + status=ready|blocked，且提示词明确「不要写 done」。
    /// </summary>
    [TestMethod]
    public void BuildRepositoryPreparePrompt_准备阶段用ready或blocked且禁止写done()
    {
        var plan = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "Csxmy-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" }
        };

        var prompt = StepPrompts.BuildRepositoryPreparePrompt(Spec(), plan, ResultFile);

        StringAssert.Contains(prompt, "\"phase\": \"prepare\"");
        StringAssert.Contains(prompt, "\"status\": \"ready\"");
        StringAssert.Contains(prompt, "ready / blocked / needsInput");
        StringAssert.Contains(prompt, "不要写 `done`");
        StringAssert.Contains(prompt, "READY 或 BLOCKED");
    }

    /// <summary>
    /// 方案 A：执行提示词必须显式声明「准备结论≠本步完成」，否则助手读一份 ready 就回 DONE。
    /// </summary>
    [TestMethod]
    public void BuildRepositoryExecutePrompt_声明准备结论不代表本步已完成()
    {
        var plan = new List<EndRepository>
        {
            new() { End = ProjectEnd.Service, RepositoryName = "Csxmy-Service", LocalPath = @"D:\ws\jnms\Csxmy-Service" }
        };

        var prompt = StepPrompts.BuildRepositoryExecutePrompt(Spec(), plan, ResultFile, ResultFile);

        StringAssert.Contains(prompt, "\"phase\": \"execute\"");
        StringAssert.Contains(prompt, "不代表本步已完成");
        StringAssert.Contains(prompt, "直接回 `DONE` 而不执行会被判为「未执行」");
    }

    /// <summary>
    /// 方案 A：所有引用知识正文的提示词都必须带上「读取失败即写失败结论、不得凭记忆执行」的硬约束。
    /// </summary>
    [TestMethod]
    public void BuildPrompts_知识正文不可读即失败硬约束()
    {
        var plan = new List<EndRepository>
        {
            new()
            {
                End = ProjectEnd.Service,
                RepositoryName = "Csxmy-Service",
                LocalPath = @"D:\ws\jnms\Csxmy-Service",
                CloneUrl = "https://codeup.example/Csxmy-Service.git"
            }
        };
        var execute = new[]
        {
            StepPrompts.BuildRepositoryExecutePrompt(Spec(), plan, ResultFile, ResultFile),
            StepPrompts.BuildPushPrompt(Spec(), plan, ResultFile),
            StepPrompts.BuildPipelinePreparePrompt(Spec(), plan, ResultFile),
            StepPrompts.BuildPipelineExecutePrompt(Spec(), plan, ResultFile, ResultFile, null, null, false)
        };

        foreach (var prompt in execute)
        {
            StringAssert.Contains(prompt, "【知识正文是硬前置】");
            StringAssert.Contains(prompt, "立即停止");
            StringAssert.Contains(prompt, "知识正文不可读");
            StringAssert.Contains(prompt, "不得");
        }

        // 准备阶段读不到文档写 blocked（不是 failed）
        var prepare = StepPrompts.BuildRepositoryPreparePrompt(Spec(), plan, ResultFile);
        StringAssert.Contains(prepare, "【知识正文是硬前置】");
        StringAssert.Contains(prepare, $"`status=\"{StepReportStatus.Blocked}\"`");
    }
}