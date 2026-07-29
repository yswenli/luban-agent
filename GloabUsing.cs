/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent
*文件名： GloabUsing
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：GloabUsing
*
*****************************************************************************/
global using LuBan.AIAgent;
global using LuBan.AIAgent.Configuration;
global using LuBan.AIAgent.MCP;
global using LuBan.AIAgent.Retrieval;
global using LuBan.AIAgent.Rules;
global using LuBan.AIAgent.Services;
global using LuBan.AIAgent.Sessions;
global using LuBan.AIAgent.Skills;
global using LuBan.Common;
global using LuBan.Orm;
global using LuBan.Orm.Models;

global using LubanAgent.Commands;
global using LubanAgent.Entities;
global using LubanAgent.Infrastructure;
global using LubanAgent.Repositories;
global using LubanAgent.Retrieval;
global using LubanAgent.Services;

global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Microsoft.ML.OnnxRuntime;
global using Microsoft.ML.OnnxRuntime.Tensors;
global using Microsoft.ML.Tokenizers;

global using Spectre.Console;

global using SqlSugar;

global using System.IO.Compression;
global using System.Security.Cryptography;
global using System.Text;
