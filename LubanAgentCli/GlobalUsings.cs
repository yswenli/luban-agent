/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli
*文件名： GlobalUsings
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：全局 using 引用，统一引入项目各层及第三方库的命名空间
*
*****************************************************************************/
global using LuBan.AIAgent;
global using LuBan.AIAgent.Abstractions;
global using LuBan.AIAgent.Configuration;
global using LuBan.AIAgent.Errors;
global using LuBan.AIAgent.MCP;
global using LuBan.AIAgent.Orchestration.Models;
global using LuBan.AIAgent.Retrieval;
global using LuBan.AIAgent.Rules;
global using LuBan.AIAgent.Sessions;
global using LuBan.AIAgent.Skills;
global using LuBan.Common;
global using LuBan.DI;

global using LubanAgentCli.App;
global using LubanAgentCli.App.Models;
global using LubanAgentCli.App.Models.Blocks;
global using LubanAgentCli.App.Views;
global using LubanAgentCli.Commands;
global using LubanAgentCli.Infrastructure;

global using LubanAgentCore.Agents;
global using LubanAgentCore.Configuration;
global using LubanAgentCore.Entities;
global using LubanAgentCore.Infrastructure;
global using LubanAgentCore.Models;
global using LubanAgentCore.Repositories;
global using LubanAgentCore.Retrieval;
global using LubanAgentCore.Services;
global using LubanAgentCore.Services.Management;
global using LubanAgentCore.Services.Management.Models;
global using LubanAgentCore.Utils;

global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;

global using Spectre.Console;

global using SqlSugar;

global using System.Data;
global using System.Text;
global using System.Text.RegularExpressions;

global using Terminal.Gui.App;
global using Terminal.Gui.Drawing;
global using Terminal.Gui.Input;
global using Terminal.Gui.ViewBase;
global using Terminal.Gui.Views;

// 由代码文件上移的命名空间（2026-10-28 规范化）
global using LuBan.AIAgent.A2A;
global using LuBan.AIAgent.A2A.Abstractions;
global using LuBan.AIAgent.Configuration.Providers;
global using LuBan.AIAgent.Defaults;
global using LuBan.AIAgent.Hosting;
global using LuBan.AIAgent.Hosting.Commands;
global using LuBan.AIAgent.Orchestration;
global using LuBan.AIAgent.Wiki;
global using LuBan.AIAgent.Wiki.Extractors;
global using LuBan.Common.IO;
global using LubanAgentA2A;
global using LubanAgentA2A.Configuration;
global using LubanAgentA2A.Runtime;
global using LubanAgentCli.App.Dialogs;
global using LubanAgentCli.App.Services;
global using LubanAgentCli.App.ViewModels;
global using LubanAgentCore.Context;
global using LubanAgentCore.Hosting;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using System.Collections.Concurrent;
global using System.Collections.ObjectModel;
global using System.Diagnostics;
global using System.Runtime.InteropServices;
global using Terminal.Gui.Text;
