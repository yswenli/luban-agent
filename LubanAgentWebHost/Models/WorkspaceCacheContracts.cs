/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： WorkspaceCacheContracts
*描述：工作区缓存扫描与回收的请求 / 响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

/// <summary>单个工作区的缓存占用。</summary>
/// <param name="WorkspaceId">工作区标识。</param>
/// <param name="Name">显示名。</param>
/// <param name="RootPath">工作区根目录。</param>
/// <param name="Type">工作区类型（Normal / LlmWiki）。</param>
/// <param name="TempPath">缓存目录绝对路径。</param>
/// <param name="Exists">缓存目录当前是否存在。</param>
/// <param name="Error">扫描失败原因；成功为 null。</param>
/// <param name="TempBytes">非上传类临时文件字节数。</param>
/// <param name="TempFiles">非上传类临时文件数量。</param>
/// <param name="UploadBytes">上传附件总字节数。</param>
/// <param name="UploadFiles">上传附件总数量。</param>
/// <param name="ReferencedBytes">被历史消息引用的附件字节数（默认保留）。</param>
/// <param name="ReferencedFiles">被历史消息引用的附件数量。</param>
/// <param name="ReclaimableBytes">按当前保留期可回收字节数（临时文件 + 未被引用的附件）。</param>
/// <param name="ReclaimableFiles">可回收文件数量。</param>
public sealed record WorkspaceCacheItemDto(
    string WorkspaceId,
    string Name,
    string RootPath,
    string Type,
    string TempPath,
    bool Exists,
    string? Error,
    long TempBytes,
    int TempFiles,
    long UploadBytes,
    int UploadFiles,
    long ReferencedBytes,
    int ReferencedFiles,
    long ReclaimableBytes,
    int ReclaimableFiles);

/// <summary>工作区缓存扫描结果。</summary>
/// <param name="KeepDays">本次扫描采用的保留天数（0 表示不保留）。</param>
/// <param name="Items">各工作区占用。</param>
/// <param name="TotalBytes">全部工作区占用字节数。</param>
/// <param name="ReclaimableBytes">全部工作区可回收字节数。</param>
public sealed record WorkspaceCacheScanDto(
    int KeepDays,
    IReadOnlyList<WorkspaceCacheItemDto> Items,
    long TotalBytes,
    long ReclaimableBytes);

/// <summary>清理工作区缓存的请求。</summary>
/// <param name="WorkspaceId">目标工作区；与 <paramref name="AllWorkspaces"/> 二选一。</param>
/// <param name="AllWorkspaces">是否清理当前用户全部工作区。</param>
/// <param name="KeepDays">保留最近 N 天修改的文件；空按 7 天。</param>
/// <param name="IncludeTempFiles">是否删除临时中间文件。</param>
/// <param name="IncludeOrphanUploads">是否删除未被会话引用的上传附件。</param>
/// <param name="IncludeReferencedUploads">是否删除已被会话引用的上传附件（历史会话将无法预览）。</param>
public sealed record WorkspaceCacheCleanRequest(
    string? WorkspaceId,
    bool AllWorkspaces,
    int? KeepDays,
    bool IncludeTempFiles,
    bool IncludeOrphanUploads,
    bool IncludeReferencedUploads);

/// <summary>清理结果。</summary>
/// <param name="FreedBytes">已释放字节数。</param>
/// <param name="FreedFiles">已删除文件数。</param>
/// <param name="FailedFiles">删除失败的文件数（被占用 / 无权限）。</param>
/// <param name="Workspaces">实际处理的工作区数量。</param>
public sealed record WorkspaceCacheCleanDto(
    long FreedBytes,
    int FreedFiles,
    int FailedFiles,
    int Workspaces);