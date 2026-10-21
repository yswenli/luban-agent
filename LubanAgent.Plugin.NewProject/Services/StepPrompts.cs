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
            "本步只做编译检查：只编译、不修复。禁止修改任何源码 / 配置 / 项目文件；"
                + "即使编译报错也不要去改代码——把错误如实报出来，由用户修完后再重跑本步。",
            "编译产物（bin / obj / node_modules / dist）留在原处即可，不要删除、不要清理。",
            "依赖未装就先装（dotnet restore / npm install / pnpm install 等），再编译；每个端最多重试 1 次。",
            "命令首词必须是白名单内的可执行文件：dotnet / git / node / npm / npx / pnpm / yarn / python / pip。"
                + "一次一条命令，可用 && 串联（每一段的首词同样受白名单约束）；不要用管道与重定向。"
        });

        builder.AppendLine("【待编译的端】（每个端的 name 标识必须原样使用，插件按它把结论对回具体的端）");
        foreach (var repository in targets)
        {
            builder.AppendLine($"- name = {repository.End}（{EndName(repository.End)}）；本地目录 = {repository.LocalPath}；"
                               + $"仓库 = {repository.RepositoryName}");
        }
        builder.AppendLine();

        builder.AppendLine("【执行要求】");
        builder.AppendLine("1) 每个端先看清目录里有什么（.sln / .csproj / package.json / pubspec.yaml 等），"
                           + "再自行判断该用什么命令编译——编译方式由你按工程类型决定。");
        builder.AppendLine("2) 后端一般是 .NET 解决方案：在仓库目录里 dotnet build 对应的 .sln（或 .csproj）。");
        builder.AppendLine("3) 前端（Vite/Vue 等）一般需要先装依赖再构建："
                           + "npm install / pnpm install 之后 npm run build / pnpm run build。");
        builder.AppendLine("4) 每个端得出「通过 / 失败」与一句话 detail；失败时附关键错误（错误码 / 文件名 / 行号，最多 3 条）。");
        builder.AppendLine();

        builder.AppendLine("把下面的 JSON 写入结论文件（用 WriteFileAsync 或 Python 写出均可，UTF-8）：");
        builder.AppendLine("```json");
        builder.AppendLine("{");
        builder.AppendLine("  \"step\": 5,");
        builder.AppendLine("  \"ok\": false,   // 仅当所有端都通过时才写 true");
        builder.AppendLine("  \"checks\": [");
        builder.AppendLine("    { \"name\": \"Service\", \"ok\": true,  \"detail\": \"编译通过\", \"command\": \"dotnet build xxx.sln\" },");
        builder.AppendLine("    { \"name\": \"Admin\",   \"ok\": false, \"detail\": \"2 个错误：…\", \"command\": \"pnpm run build\" }");
        builder.AppendLine("  ],");
        builder.AppendLine("  \"message\": \"<一句话总结>\"");
        builder.AppendLine("}");
        builder.AppendLine("```");
        builder.AppendLine("注意：checks 必须逐端齐全；漏写某个端会被判为「该端未给结论」＝本步失败。");
        builder.AppendLine();

        AppendResultContract(builder, resultFile, "DONE 或 FAILED: <失败端与原因>");

        return builder.ToString();
    }

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