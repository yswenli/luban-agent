/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SessionToolCallHistoryUnitTest
*创建时间：2026/10/23
*描述：会话历史工具调用回放的单测。
*     锁定真机现场：插件在项目会话里跑完的回合，切换会话后看不到任何工具卡片，
*     因为历史回填只读 content、丢弃了 tool_calls。本文件锁死「落库形态 → 回放记录」的解析口径。
*
*****************************************************************************/

using LubanAgentCore.Utils;

namespace LubanAgent.Tests;

[TestClass]
public class SessionToolCallHistoryUnitTest
{
    /// <summary>真机落库的形态（字段小写、arguments 是 JSON 字符串）。</summary>
    private const string RealSample =
        "[{\"name\":\"ReadFile\",\"callId\":\"call_96976136c86a4bb4b29a0ec9\","
        + "\"arguments\":\"{\\u0022path\\u0022:\\u0022D:\\\\\\\\ws\\\\\\\\org-reference.md\\u0022}\"},"
        + "{\"name\":\"RunShell\",\"callId\":\"call_1c5050efc1884a1fa666455e\","
        + "\"arguments\":\"{\\u0022command\\u0022:\\u0022dotnet build x.sln\\u0022,\\u0022workingDirectory\\u0022:\\u0022D:\\\\\\\\ws\\\\\\\\repo\\u0022}\"}]";

    [TestMethod]
    public void Parse_按落库顺序还原工具名与调用ID()
    {
        var calls = SessionToolCallHistory.Parse(RealSample);

        Assert.AreEqual(2, calls.Count);
        Assert.AreEqual("ReadFile", calls[0].Name);
        Assert.AreEqual("call_96976136c86a4bb4b29a0ec9", calls[0].CallId);
        Assert.AreEqual("RunShell", calls[1].Name);
        Assert.AreEqual("call_1c5050efc1884a1fa666455e", calls[1].CallId);
    }

    [TestMethod]
    public void Parse_参数是JSON字符串时按对象展开()
    {
        var calls = SessionToolCallHistory.Parse(RealSample);

        // arguments 里是 JSON 字符串，必须二次解析成对象；字符串值取原值（不带引号）
        Assert.AreEqual(@"D:\ws\org-reference.md", calls[0].Arguments["path"]);

        var shell = calls[1].Arguments;
        Assert.AreEqual("dotnet build x.sln", shell["command"]);
        Assert.AreEqual(@"D:\ws\repo", shell["workingDirectory"]);
    }

    [TestMethod]
    public void Parse_参数缺失或非法时返回空表而不抛异常()
    {
        const string json =
            "[{\"name\":\"A\",\"callId\":\"c1\"},"
            + "{\"name\":\"B\",\"callId\":\"c2\",\"arguments\":\"not-json\"},"
            + "{\"name\":\"C\",\"callId\":\"c3\",\"arguments\":null}]";

        var calls = SessionToolCallHistory.Parse(json);

        Assert.AreEqual(3, calls.Count);
        foreach (var call in calls)
            Assert.AreEqual(0, call.Arguments.Count, $"{call.Name} 的参数应为空表");
    }

    [TestMethod]
    public void Parse_缺工具名的记录被跳过其余照常回放()
    {
        const string json =
            "[{\"callId\":\"c1\",\"arguments\":\"{\\\"path\\\":\\\"a\\\"}\"},"
            + "{\"name\":\"  \",\"callId\":\"c2\"},"
            + "{\"name\":\"Keep\",\"callId\":\"c3\"}]";

        var calls = SessionToolCallHistory.Parse(json);

        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual("Keep", calls[0].Name);
    }

    [TestMethod]
    public void Parse_输入为空或不是数组时返回空列表()
    {
        Assert.AreEqual(0, SessionToolCallHistory.Parse(null).Count);
        Assert.AreEqual(0, SessionToolCallHistory.Parse("").Count);
        Assert.AreEqual(0, SessionToolCallHistory.Parse("   ").Count);
        Assert.AreEqual(0, SessionToolCallHistory.Parse("{}").Count);
        Assert.AreEqual(0, SessionToolCallHistory.Parse("[]").Count);
        Assert.AreEqual(0, SessionToolCallHistory.Parse("not-json").Count);
    }

    [TestMethod]
    public void ParseArguments_数字布尔与嵌套结构取原始文本()
    {
        var args = SessionToolCallHistory.ParseArguments(
            "{\"count\":3,\"enabled\":true,\"disabled\":false,\"none\":null,\"nested\":{\"a\":1},\"list\":[1,2]}");

        Assert.AreEqual("3", args["count"]);
        Assert.AreEqual(true, args["enabled"]);
        Assert.AreEqual(false, args["disabled"]);
        Assert.IsNull(args["none"]);
        Assert.AreEqual("{\"a\":1}", args["nested"]);
        Assert.AreEqual("[1,2]", args["list"]);
    }

    [TestMethod]
    public void ParseArguments_非对象JSON返回空表()
    {
        Assert.AreEqual(0, SessionToolCallHistory.ParseArguments("[1,2]").Count);
        Assert.AreEqual(0, SessionToolCallHistory.ParseArguments("\"text\"").Count);
        Assert.AreEqual(0, SessionToolCallHistory.ParseArguments("null").Count);
    }
}