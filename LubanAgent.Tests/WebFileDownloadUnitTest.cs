/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebFileDownloadUnitTest
*描述：工作区文件下载路由：参数与返回签名必须与前端 fileDownloadUrl 对齐
*
*****************************************************************************/

using System.Reflection;
using LubanAgentWebHost.Controllers;

namespace LubanAgent.Tests;

[TestClass]
public class WebFileDownloadUnitTest
{
    /// <summary>
    /// 前端按 <c>/file/download?workspaceId=…&amp;path=…</c> 拼地址；动作名或参数名被改动时前端会静默落到 404，
    /// 这里把契约钉住。
    /// </summary>
    [TestMethod]
    public void DownloadRouteKeepsWorkspaceAndPathParameters()
    {
        var method = typeof(FileController).GetMethod("Download");

        Assert.IsNotNull(method, "FileController 缺少 Download 动作，前端 /file/download 将 404。");
        Assert.IsTrue(
            method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>),
            "Download 必须是异步动作（返回 Task<>）。");

        var parameters = method.GetParameters().Select(parameter => parameter.Name).ToArray();
        CollectionAssert.Contains(parameters, "workspaceId");
        CollectionAssert.Contains(parameters, "path");
    }
}