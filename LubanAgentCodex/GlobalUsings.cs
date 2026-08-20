/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex
*文件名： GlobalUsings
*版本号： V1.0.0.0
*唯一标识：全局 using 引用
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
global using Avalonia.Controls.Primitives;
global using Avalonia.Input;
global using Avalonia.Layout;
global using Avalonia.Markup.Xaml;
global using Avalonia.Media;
global using Avalonia.Threading;

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

// CommunityToolkit
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;

// LuBan
global using LuBan.AIAgent;
global using LuBan.AIAgent.Abstractions;
global using LuBan.AIAgent.Configuration;
global using LuBan.AIAgent.MCP;
global using LuBan.AIAgent.Rules;
global using LuBan.AIAgent.Skills;
global using LuBan.Orm;

// LubanAgentCore
global using LubanAgentCore.Configuration;
global using LubanAgentCore.Hosting;
global using LubanAgentCore.Infrastructure;
global using LubanAgentCore.Models;
global using LubanAgentCore.Repositories;
global using LubanAgentCore.Services;

// Microsoft
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
