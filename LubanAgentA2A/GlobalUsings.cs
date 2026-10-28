/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentA2A
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
global using A2A.AspNetCore;
global using LuBan.AIAgent;
global using LuBan.AIAgent.A2A;
global using LuBan.AIAgent.A2A.Abstractions;
global using LuBan.AIAgent.A2A.Models;
global using LuBan.AIAgent.Abstractions;
global using LuBan.AIAgent.Orchestration;
global using LuBan.AIAgent.Orchestration.Models;
global using LubanAgentA2A.Client;
global using LubanAgentA2A.Configuration;
global using LubanAgentA2A.Executor;
global using LubanAgentA2A.InProcess;
global using LubanAgentA2A.Mapping;
global using LubanAgentA2A.Runtime;
global using LubanAgentA2A.Security;
global using LubanAgentA2A.Server;
global using LubanAgentA2A.Storage;
global using LubanAgentCore.Entities;
global using LubanAgentCore.Repositories;
global using Microsoft.Agents.AI;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Hosting.Server;
global using Microsoft.AspNetCore.Hosting.Server.Features;
global using Microsoft.AspNetCore.Http;
global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using System.Collections.Concurrent;
global using System.Net;
global using System.Net.Sockets;
global using System.Runtime.CompilerServices;
global using System.Runtime.ExceptionServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
