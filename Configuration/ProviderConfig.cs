namespace LubanAgent.Configuration;

public class ProviderConfig
{
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string? DisplayName { get; set; }
    public List<string> SupportedModels { get; set; } = new();
    public List<string> CustomModels { get; set; } = new();
    public int? NetworkTimeoutSeconds { get; set; }
}
