/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent
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
global using LuBan.AIAgent.LocalMemory;
global using LuBan.AIAgent.MCP;
global using LuBan.AIAgent.Retrieval;
global using LuBan.AIAgent.Rules;
global using LuBan.AIAgent.Sessions;
global using LuBan.AIAgent.Skills;
global using LuBan.AIAgent.Utils.Text;
global using LuBan.Common;
global using LuBan.Logging;
global using LuBan.Orm;
global using LuBan.Orm.Models;

global using LubanAgentCli.App;
global using LubanAgentCli.App.Models;
global using LubanAgentCli.App.Models.Blocks;
global using LubanAgentCli.App.Views;
global using LubanAgentCli.Commands;
global using LubanAgentCore.Configuration;
global using LubanAgentCore.Entities;
global using LubanAgentCli.Infrastructure;
global using LubanAgentCore.Agents;
global using LubanAgentCore.Repositories;
global using LubanAgentCore.Retrieval;
global using LubanAgentCore.Services;
global using LubanAgentCore.Utils;
global using LubanAgentCore.Infrastructure;

global using Microsoft.Data.Sqlite;
global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Microsoft.ML.OnnxRuntime;
global using Microsoft.ML.OnnxRuntime.Tensors;
global using Microsoft.ML.Tokenizers;

global using Spectre.Console;

global using SqlSugar;

global using System.Data;
global using System.IO.Compression;
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.RegularExpressions;

global using Terminal.Gui.App;
global using Terminal.Gui.Drawing;
global using Terminal.Gui.Input;
global using Terminal.Gui.ViewBase;
global using Terminal.Gui.Views;
