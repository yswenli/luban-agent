using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using LuBan.AIAgent;
using LuBan.AIAgent.Abstractions;
using System;
using System.Linq;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLuBanAgent(config);

// Check if OrchestrationToolPlugin is already registered
var sp = services.BuildServiceProvider();
var plugins = sp.GetServices<ILuBanToolPlugin>().ToList();
Console.WriteLine($"Total ILuBanToolPlugin count: {plugins.Count}");
foreach (var p in plugins)
{
    Console.WriteLine($"  - {p.GetType().FullName}");
}

var orchCount = plugins.Count(p => p is LuBan.AIAgent.Tools.Orchestration.OrchestrationToolPlugin);
Console.WriteLine($"OrchestrationToolPlugin count: {orchCount}");
