/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Security
*文件名： ICredentialProtector
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：凭据保护：把明文密钥加密后写入配置，读取时还原。 Windows 走 DPAPI（当前用户作用域），其他平台 <see cref="IsAvailable"/> 为 <c>false</c>。
*
*=================================================
*修改标记
*修改时间：2026/10/27
*修改人： yswenli
*版本号： V1.0.0.0
*描述：凭据保护：把明文密钥加密后写入配置，读取时还原。 Windows 走 DPAPI（当前用户作用域），其他平台 <see cref="IsAvailable"/> 为 <c>false</c>。
*
*****************************************************************************/

namespace LubanAgentCore.Security;

/// <summary>
/// 凭据保护：把明文密钥加密后写入配置，读取时还原。
/// Windows 走 DPAPI（当前用户作用域），其他平台 <see cref="IsAvailable"/> 为 <c>false</c>。
/// </summary>
public interface ICredentialProtector
{
    /// <summary>当前平台是否支持加密存储（Windows DPAPI 可用时为 <c>true</c>）。</summary>
    bool IsAvailable { get; }

    /// <summary>加密明文。返回值带存储标记，可直接落配置文件。</summary>
    /// <param name="plaintext">明文凭据。</param>
    /// <exception cref="InvalidOperationException">当前平台不支持加密存储。</exception>
    string Protect(string plaintext);

    /// <summary>解密 <see cref="Protect"/> 的返回值；非加密值原样返回。</summary>
    /// <param name="protectedValue">配置中的凭据值（可能已是明文）。</param>
    /// <exception cref="InvalidOperationException">当前平台不支持加密存储，但值确实被加密过。</exception>
    string Unprotect(string protectedValue);
}