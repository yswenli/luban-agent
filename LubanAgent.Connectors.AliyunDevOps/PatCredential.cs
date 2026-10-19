/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Connectors.AliyunDevOps
*文件名： PatCredential
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效 PAT 封装 - 明文仅驻内存，不落盘、不入库
*
*****************************************************************************/

namespace LubanAgent.Connectors.AliyunDevOps;

/// <summary>
/// 云效 PAT 凭据封装 - 明文仅驻内存，不落盘、不入库、不回显日志
/// </summary>
public sealed class PatCredential : IDisposable
{
    private readonly string _pat;
    private bool _disposed;

    /// <summary>
    /// 创建 PAT 凭据
    /// </summary>
    /// <param name="pat">个人访问令牌</param>
    public PatCredential(string pat)
    {
        if (string.IsNullOrWhiteSpace(pat))
            throw new ArgumentException("PAT 不能为空", nameof(pat));
        
        _pat = pat;
    }

    /// <summary>
    /// 获取 PAT 明文（仅内部使用）
    /// </summary>
    internal string GetPlainText()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PatCredential));
        
        return _pat;
    }

    /// <summary>
    /// 获取 Git 鉴权 URL（oauth2:PAT@codeup.aliyun.com/...）
    /// </summary>
    /// <param name="repositoryUrl">仓库原始 URL（如 https://codeup.aliyun.com/org/repo.git）</param>
    /// <returns>带鉴权的 URL</returns>
    public string GetAuthenticatedGitUrl(string repositoryUrl)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PatCredential));
        
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            throw new ArgumentException("仓库 URL 不能为空", nameof(repositoryUrl));

        // https://codeup.aliyun.com/... → https://oauth2:PAT@codeup.aliyun.com/...
        var builder = new UriBuilder(repositoryUrl);
        builder.UserName = "oauth2";
        builder.Password = _pat;
        return builder.Uri.ToString();
    }

    /// <summary>
    /// 析构函数 - 确保清理
    /// </summary>
    ~PatCredential()
    {
        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 重写 ToString 以避免意外泄露 PAT
    /// </summary>
    public override string ToString() => "PatCredential(***)";
}