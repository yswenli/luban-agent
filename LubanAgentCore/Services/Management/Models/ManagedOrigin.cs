namespace LubanAgentCore.Services.Management.Models;

/// <summary>
/// 管理项的来源（决定写回哪个后端）。
/// </summary>
public enum ManagedOrigin
{
    /// <summary>硬编码内置项（DI 注册），只能启停，不能改内容。</summary>
    Builtin,

    /// <summary>
    /// 文件型：<c>~/.luban-agent/skills|rules</c>（全局）或
    /// <c>&lt;工作区&gt;/.luban-agent/skills|rules</c>（工作区）。
    /// </summary>
    File,

    /// <summary>config.json 自定义项（<c>CustomSkills</c> / <c>CustomRules</c>）。</summary>
    Config,
}