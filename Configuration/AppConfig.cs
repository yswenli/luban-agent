using LuBan.AIAgent.Configuration;

namespace LubanAgent.Configuration;

public class AppConfig
{
    public List<ProviderConfig> Providers { get; set; } = new();
    public string? SelectedModel { get; set; }
    public List<CustomSkillConfig> CustomSkills { get; set; } = new();
    public List<CustomRuleConfig> CustomRules { get; set; } = new();
    public List<McpServerConfig> McpServers { get; set; } = new();
    public List<string> DisabledBuiltinSkills { get; set; } = new();
    public List<string> DisabledBuiltinRules { get; set; } = new();
    public List<string> DisabledBuiltinMcpClients { get; set; } = new();
}
