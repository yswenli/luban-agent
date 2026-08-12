/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent
*文件名： Program
*版本号： V1.0.0.0
*唯一标识：程序主入口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：程序主入口，启动 TUI 应用
*
*****************************************************************************/
namespace LubanAgent;

/// <summary>
/// 程序入口
/// </summary>
class Program
{
    /// <summary>
    /// 程序主入口
    /// </summary>
    static int Main(string[] args)
    {
        if (!TerminalGuiApp.CanRunInteractive())
        {
            Console.Error.WriteLine("luban-agent-cli 需要可交互终端运行，检测到输入/输出被重定向或无终端窗口。");
            return 1;
        }

        var app = new TerminalGuiApp();
        app.Run(args);
        return 0;
    }
}
