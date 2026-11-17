/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： GlobalUsings
*描述：测试项目级全局 using 声明，集中上移各测试文件的公共命名空间引用
*
*****************************************************************************/

// Global using 引用 - 框架依赖
global using LuBan.AIAgent.A2A;
global using LuBan.AIAgent.A2A.Abstractions;
global using LuBan.AIAgent.A2A.Models;
global using LuBan.AIAgent.Abstractions;
global using LuBan.AIAgent.Attachments;
global using LuBan.AIAgent.Configuration;
global using LuBan.AIAgent.Connectors;
global using LuBan.AIAgent.Hosting;
global using LuBan.AIAgent.MCP;
global using LuBan.AIAgent.Plugins;
global using LuBan.AIAgent.Plugins.Security;
global using LuBan.AIAgent.Retrieval;
global using LuBan.AIAgent.Sessions;
global using LuBan.AIAgent.Skills;
global using LuBan.AIAgent.Skills.BuiltIn;
global using LuBan.AIAgent.Tools.McpManage;
global using LuBan.AIAgent.Wiki;
global using LuBan.DI;

// Global using 引用 - 产品内部命名空间
global using LubanAgent.Plugin.NewProject.Abstractions;
global using LubanAgent.Plugin.NewProject.Configuration;
global using LubanAgent.Plugin.NewProject.Services;
global using LubanAgentCodex.ViewModels;
global using LubanAgentCodex.ViewModels.Messages;
global using LubanAgentCodex.ViewModels.Settings;
global using LubanAgentCodex.Views;
global using LubanAgentCore.A2A.Runtime;
global using LubanAgentCore.Client;
global using LubanAgentCore.Configuration;
global using LubanAgentCore.Connectors.AliyunDevOps;
global using LubanAgentCore.Connectors.AliyunDevOps.Mcp;
global using LubanAgentCore.Entities;
global using LubanAgentCore.Hosting;
global using LubanAgentCore.Models;
global using LubanAgentCore.Plugins;
global using LubanAgentCore.Protocol;
global using LubanAgentCore.Protocol.Dtos;
global using LubanAgentCore.Protocol.Events;
global using LubanAgentCore.Repositories;
global using LubanAgentCore.Services;
global using LubanAgentCore.Services.Management;
global using LubanAgentCore.Services.Management.Models;
global using LubanAgentCore.Utils;

// Global using 引用 - 三方与 BCL
global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using System.IO.Compression;
global using System.Reflection;
global using System.Text.Json;
global using System.Text.Json.Serialization;