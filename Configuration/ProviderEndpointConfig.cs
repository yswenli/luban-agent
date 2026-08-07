namespace LubanAgent.Configuration;

public class ProviderEndpointInfo
{
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class ExtendedProviderInfo
{
    public string DisplayName { get; set; } = string.Empty;
    public List<ProviderEndpointInfo> Endpoints { get; set; } = new();
    public List<string> Models { get; set; } = new();
}
