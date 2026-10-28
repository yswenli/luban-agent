/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgent.Plugin.NewProject
*文件名： GlobalUsings
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/28
*描述：项目级全局 using 声明，集中上移各代码文件的公共命名空间引用
*
*=================================================
*修改标记
*修改时间：2026/10/28
*修改人： yswenli
*版本号： V1.0.0.0
*描述：项目级全局 using 声明，集中上移各代码文件的公共命名空间引用
*
*****************************************************************************/



// 由代码文件上移的命名空间（2026-10-28 规范化）
global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Controls.Primitives;
global using Avalonia.Input;
global using Avalonia.Interactivity;
global using Avalonia.Layout;
global using Avalonia.Media;
global using Avalonia.Platform.Storage;
global using LuBan.AIAgent.Abstractions;
global using LuBan.AIAgent.Hosting;
global using LuBan.AIAgent.Infrastructure;
global using LuBan.AIAgent.Plugins;
global using LuBan.AIAgent.Plugins.Base;
global using LuBan.AIAgent.Plugins.Lifecycle;
global using LuBan.AIAgent.Plugins.Security;
global using LuBan.AIAgent.Sessions;
global using LubanAgent.Plugin.NewProject.Abstractions;
global using LubanAgent.Plugin.NewProject.Configuration;
global using LubanAgent.Plugin.NewProject.Serialization;
global using LubanAgent.Plugin.NewProject.Services;
global using LubanAgent.Plugin.NewProject.Ui;
global using LubanAgentCore.Configuration;
global using LubanAgentCore.Connectors.AliyunDevOps;
global using LubanAgentCore.Hosting;
global using LubanAgentCore.Infrastructure;
global using LubanAgentCore.Models;
global using LubanAgentCore.Services;
global using LubanAgentCore.Utils;
global using Microsoft.Extensions.DependencyInjection;
global using System.Diagnostics;
global using System.Globalization;
global using System.Reflection;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Text.RegularExpressions;
