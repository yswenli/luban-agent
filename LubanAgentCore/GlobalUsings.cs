/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore
*文件名： GlobalUsings
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：项目级全局 using 声明，集中上移各代码文件的公共命名空间引用
*
*=================================================
*修改标记
*修改时间：2026/8/20
*修改人： yswenli
*版本号： V1.0.0.0
*描述：项目级全局 using 声明，集中上移各代码文件的公共命名空间引用
*
*****************************************************************************/

// Global using 引用 - 外部依赖
global using LuBan.AIAgent;
global using LuBan.AIAgent.Abstractions;
global using LuBan.AIAgent.Attachments;
global using LuBan.AIAgent.Attachments.Ingestion;
global using LuBan.AIAgent.Configuration;
global using LuBan.AIAgent.LocalMemory;
global using LuBan.AIAgent.MCP;
global using LuBan.AIAgent.Orchestration.Models;
global using LuBan.AIAgent.Retrieval;
global using LuBan.AIAgent.Rules;
global using LuBan.AIAgent.Sessions;
global using LuBan.AIAgent.Skills;
global using LuBan.Common;
global using LuBan.Common.IO;
global using LuBan.DI;
global using LuBan.Orm;
global using LuBan.Orm.Models;

// Global using 引用 - 内部命名空间
global using LubanAgentCore.Connectors.AliyunDevOps;

global using LubanAgentCore.A2A.Client;
global using LubanAgentCore.A2A.Configuration;
global using LubanAgentCore.A2A.Executor;
global using LubanAgentCore.A2A.InProcess;
global using LubanAgentCore.A2A.Mapping;
global using LubanAgentCore.A2A.Runtime;
global using LubanAgentCore.A2A.Security;
global using LubanAgentCore.A2A.Server;
global using LubanAgentCore.A2A.Storage;

global using LubanAgentCore.Client;
global using LubanAgentCore.Protocol;
global using LubanAgentCore.Protocol.Dtos;
global using LubanAgentCore.Protocol.Events;

global using LubanAgentCore.Agents;
global using LubanAgentCore.Configuration;
global using LubanAgentCore.Context;
global using LubanAgentCore.Entities;
global using LubanAgentCore.Infrastructure;
global using LubanAgentCore.Models;
global using LubanAgentCore.Repositories;
global using LubanAgentCore.Retrieval;
global using LubanAgentCore.Security;
global using LubanAgentCore.Services;
global using LubanAgentCore.Services.Management;
global using LubanAgentCore.Services.Management.Models;

global using Microsoft.Data.Sqlite;
global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Microsoft.ML.OnnxRuntime;
global using Microsoft.ML.OnnxRuntime.Tensors;
global using Microsoft.ML.Tokenizers;

global using OpenAI;

global using SqlSugar;

global using System.Data;
global using System.IO.Compression;
global using System.Runtime.CompilerServices;
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.Json;

// 由代码文件上移的命名空间（2026-10-28 规范化）
global using LuBan.AIAgent.A2A;
global using LuBan.AIAgent.A2A.Abstractions;
global using LuBan.AIAgent.A2A.Models;
global using LuBan.AIAgent.Abstractions.Models;
global using LuBan.AIAgent.Configuration.Providers;
global using LuBan.AIAgent.Connectors;
global using LuBan.AIAgent.Defaults;
global using LuBan.AIAgent.Errors;
global using LuBan.AIAgent.Hosting;
global using LuBan.AIAgent.Infrastructure;
global using LuBan.AIAgent.Orchestration;
global using LuBan.AIAgent.Plugins;
global using LuBan.AIAgent.Plugins.Lifecycle;
global using LuBan.AIAgent.Plugins.Loading;
global using LuBan.AIAgent.Plugins.Security;
global using LuBan.AIAgent.Retrieval.Chunkers;
global using LuBan.AIAgent.Tools.McpManage;
global using LuBan.AIAgent.Utils.Text;
global using LuBan.AIAgent.Wiki;
global using LuBan.AIAgent.Wiki.Extractors;
global using LuBan.Logging;
global using LubanAgentCore.Hosting;
global using LubanAgentCore.Plugins;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Logging;
global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Globalization;
global using System.Net;
global using System.Net.Http;
global using System.Net.Http.Headers;
global using System.Net.Sockets;
global using System.Runtime.ExceptionServices;
global using System.Text.Encodings.Web;
global using System.Text.Json.Serialization;
global using System.Text.RegularExpressions;

global using A2A.AspNetCore;

global using Microsoft.Agents.AI;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Hosting.Server;
global using Microsoft.AspNetCore.Hosting.Server.Features;
global using Microsoft.AspNetCore.Http;
