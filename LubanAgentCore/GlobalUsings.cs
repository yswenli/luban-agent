// Global using 引用 - 外部依赖
global using SqlSugar;
global using LuBan.AIAgent;
global using LuBan.AIAgent.Abstractions;
global using LuBan.AIAgent.Configuration;
global using LuBan.AIAgent.LocalMemory;
global using LuBan.AIAgent.Skills;
global using LuBan.AIAgent.Rules;
global using LuBan.AIAgent.MCP;
global using LuBan.AIAgent.Sessions;
global using LuBan.AIAgent.Retrieval;
global using LuBan.Orm;
global using LuBan.Orm.Models;
global using LuBan.Common.IO;
global using LuBan.Common;
global using LuBan.DI;
global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Microsoft.ML.Tokenizers;
global using Microsoft.ML.OnnxRuntime;
global using Microsoft.ML.OnnxRuntime.Tensors;
global using Microsoft.Data.Sqlite;
global using System.Text;
global using System.Text.Json;
global using System.IO.Compression;
global using System.Security.Cryptography;
global using System.Data;
global using OpenAI;

// Global using 引用 - 内部命名空间
global using LubanAgentCore.Repositories;
global using LubanAgentCore.Entities;
global using LubanAgentCore.Services;
global using LubanAgentCore.Configuration;
global using LubanAgentCore.Retrieval;
global using LubanAgentCore.Infrastructure;