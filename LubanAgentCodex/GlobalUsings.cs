/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex
*文件名： GlobalUsings
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：全局 using 引用，统一引入项目各层及第三方库的命名空间
*
*****************************************************************************/

// Avalonia
global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Input;
global using Avalonia.Layout;
global using Avalonia.Markup.Xaml;
global using Avalonia.Media;
global using Avalonia.Threading;
// CommunityToolkit
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;

global using LuBan.AIAgent.A2A;
global using LuBan.AIAgent.A2A.Abstractions;
// LuBan
global using LuBan.AIAgent.Abstractions;
global using LuBan.AIAgent.Configuration;
global using LuBan.AIAgent.Errors;
global using LuBan.AIAgent.MCP;
global using LuBan.AIAgent.Orchestration;
global using LuBan.AIAgent.Orchestration.Models;
global using LuBan.AIAgent.Rules;
global using LuBan.AIAgent.Sessions;
global using LuBan.AIAgent.Skills;
global using LuBan.Orm;

global using LubanAgentA2A.Configuration;
global using LubanAgentA2A.Runtime;

global using LubanAgentCodex.ViewModels.Messages;
global using LubanAgentCodex.Views;

global using LuBan.AIAgent.Hosting.Commands;
// LubanAgentCore
global using LubanAgentCore.Configuration;
global using LubanAgentCore.Hosting;
global using LubanAgentCore.Infrastructure;
global using LubanAgentCore.Models;
global using LubanAgentCore.Plugins;
global using LubanAgentCore.Repositories;
global using LubanAgentCore.Security;
global using LubanAgentCore.Services;
global using LubanAgentCore.Services.Management;
global using LubanAgentCore.Services.Management.Models;
global using LubanAgentCore.Utils;
// Microsoft
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
// .NET
global using System;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.Collections.Specialized;
global using System.IO;
global using System.Linq;
global using System.Text;
global using System.Text.Json;
global using System.Threading;
global using System.Threading.Tasks;

// 由代码文件上移的命名空间（2026-10-28 规范化）
global using Avalonia.Controls.ApplicationLifetimes;
global using Avalonia.Controls.Primitives;
global using Avalonia.Data.Converters;
global using Avalonia.Input.Platform;
global using Avalonia.Interactivity;
global using Avalonia.Media.Imaging;
global using Avalonia.Platform.Storage;
global using LuBan.AIAgent;
global using LuBan.AIAgent.Configuration.Providers;
global using LuBan.AIAgent.Hosting;
global using LuBan.AIAgent.Plugins;
global using LuBan.AIAgent.Plugins.Lifecycle;
global using LuBan.AIAgent.Plugins.Loading;
global using LuBan.AIAgent.Plugins.Security;
global using LuBan.AIAgent.Retrieval;
global using LuBan.AIAgent.Wiki;
global using LuBan.Common.IO;
global using LuBan.Logging;
global using LubanAgentA2A;
global using LubanAgentCodex.Security;
global using LubanAgentCodex.Services;
global using LubanAgentCodex.ViewModels;
global using LubanAgentCodex.ViewModels.Settings;
global using LubanAgentCodex.Views.Controls;
global using LubanAgentCore.Connectors.AliyunDevOps;
global using LubanAgentCore.Entities;
global using LubanAgentCore.Retrieval;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using System.Collections.Concurrent;
global using System.ComponentModel;
global using System.Globalization;
global using System.Runtime.CompilerServices;
global using System.Text.Encodings.Web;
