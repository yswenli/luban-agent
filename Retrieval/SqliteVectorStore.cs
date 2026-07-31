/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Retrieval
*文件名： SqliteVectorStore
*版本号：V1.0.0.0
*唯一标识：SQLite 向量存储实现
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：SQLite 向量存储实现，通过 WorkspaceManager.Current 实现工作区隔离
*
*****************************************************************************/
namespace LubanAgent.Retrieval;

/// <summary>
/// SQLite 向量存储实现，通过 WorkspaceManager.Current 实现工作区隔离
/// </summary>
public class SqliteVectorStore : IVectorStore
{
    private readonly RagFileRepository _files = new();
    private readonly RagChunkRepository _chunks = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// 当前工作区ID（通过静态访问器获取，避免 DI 循环依赖；空字符串表示无工作区上下文）
    /// </summary>
    private static string CurrentWorkspaceId => WorkspaceManager.Current?.WorkspaceId ?? "";

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedFile>> GetFilesAsync(string? pathPrefix = null)
    {
        var wsId = CurrentWorkspaceId;
        var q = _files.AsQueryable().Where(f => !f.IsDelete && f.WorkspaceId == wsId);
        if (!string.IsNullOrEmpty(pathPrefix)) q = q.Where(f => f.FilePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));
        var list = await q.ToListAsync();
        return list.Select(f => new IndexedFile { Id = f.Id, FilePath = f.FilePath, FileHash = f.FileHash, Language = f.Language }).ToList();
    }

    /// <inheritdoc />
    public async Task<long> UpsertFileAsync(string filePath, string fileHash, string language, int chunkCount)
    {
        await _writeGate.WaitAsync();
        try
        {
            var wsId = CurrentWorkspaceId;
            var existing = await _files.GetByFilePathAsync(filePath, wsId);
            if (existing != null)
            {
                await _files.UpdateAsync(f => new DbRagFile
                {
                    FileHash = fileHash, Language = language, ChunkCount = chunkCount,
                    IndexedTime = DateTime.Now, UpdateTime = DateTime.Now
                }, f => f.Id == existing.Id);
                return existing.Id;
            }
            var entity = new DbRagFile
            {
                FilePath = filePath, FileHash = fileHash, Language = language,
                ChunkCount = chunkCount, IndexedTime = DateTime.Now,
                CreateTime = DateTime.Now, IsDelete = false,
                WorkspaceId = wsId
            };
            await _files.InsertAsync(entity);
            return entity.Id;
        }
        finally { _writeGate.Release(); }
    }

    /// <inheritdoc />
    public async Task SoftDeleteFileAsync(long fileId)
    {
        await _writeGate.WaitAsync();
        try
        {
            await _files.UpdateAsync(f => new DbRagFile { IsDelete = true, UpdateTime = DateTime.Now }, f => f.Id == fileId);
            await _chunks.UpdateAsync(c => new DbRagChunk { IsDelete = true, UpdateTime = DateTime.Now }, c => c.FileId == fileId);
        }
        finally { _writeGate.Release(); }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredChunk>> GetFileChunksAsync(long fileId)
    {
        var wsId = CurrentWorkspaceId;
        var list = await _chunks.AsQueryable().Where(c => c.FileId == fileId && !c.IsDelete && c.WorkspaceId == wsId).ToListAsync();
        return list.Select(c => new StoredChunk { Id = c.Id, ChunkIndex = c.ChunkIndex, ContentHash = c.ContentHash, Vector = VectorMath.ToFloats(c.Vector) }).ToList();
    }

    /// <inheritdoc />
    public async Task ReplaceFileChunksAsync(long fileId, string modelId, IReadOnlyList<ChunkVectorPair> chunks)
    {
        await _writeGate.WaitAsync();
        try
        {
            var wsId = CurrentWorkspaceId;
            using var scope = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
            await _chunks.DeleteAsync(c => c.FileId == fileId);
            foreach (var p in chunks)
            {
                var entity = new DbRagChunk
                {
                    FileId = fileId,
                    ChunkIndex = p.Chunk.ChunkIndex,
                    StartLine = p.Chunk.StartLine,
                    EndLine = p.Chunk.EndLine,
                    ChunkType = p.Chunk.ChunkType,
                    SymbolName = p.Chunk.SymbolName,
                    Content = p.Chunk.Content,
                    ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Chunk.Content))),
                    Vector = VectorMath.ToBytes(p.Vector),
                    ModelId = modelId,
                    CreateTime = DateTime.Now,
                    IsDelete = false,
                    WorkspaceId = wsId
                };
                await _chunks.InsertAsync(entity);
            }
            scope.Complete();
        }
        finally { _writeGate.Release(); }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VectorEntry>> LoadVectorsAsync(string? pathPrefix = null, string? language = null, int maxResults = int.MaxValue)
    {
        var wsId = CurrentWorkspaceId;
        var q = _chunks.Context.Queryable<DbRagChunk>()
            .InnerJoin<DbRagFile>((c, f) => c.FileId == f.Id)
            .Where((c, f) => !c.IsDelete && !f.IsDelete && f.WorkspaceId == wsId);
        if (!string.IsNullOrEmpty(pathPrefix)) q = q.Where((c, f) => f.FilePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(language)) q = q.Where((c, f) => f.Language == language);
        if (maxResults < int.MaxValue) q = q.Take(maxResults);
        var list = await q.Select((c, f) => new { c.Id, c.Vector }).ToListAsync();
        return list.Select(x => new VectorEntry { ChunkId = x.Id, Vector = VectorMath.ToFloats(x.Vector) }).ToList();
    }

    /// <inheritdoc />
    public async Task<Dictionary<long, CodeChunk>> GetChunksAsync(IReadOnlyList<long> chunkIds)
    {
        var wsId = CurrentWorkspaceId;
        var q = _chunks.Context.Queryable<DbRagChunk>()
            .InnerJoin<DbRagFile>((c, f) => c.FileId == f.Id)
            .Where((c, f) => chunkIds.Contains(c.Id) && !c.IsDelete && f.WorkspaceId == wsId)
            .Select((c, f) => new { Chunk = c, f.FilePath, f.Language });
        var list = await q.ToListAsync();
        return list.ToDictionary(x => x.Chunk.Id, x => new CodeChunk
        {
            FilePath = x.FilePath, Language = x.Language, ChunkIndex = x.Chunk.ChunkIndex,
            StartLine = x.Chunk.StartLine, EndLine = x.Chunk.EndLine, ChunkType = x.Chunk.ChunkType,
            SymbolName = x.Chunk.SymbolName, Content = x.Chunk.Content
        });
    }

    /// <inheritdoc />
    public async Task<StoreStats> GetStatsAsync()
    {
        var wsId = CurrentWorkspaceId;
        var fileCount = await _files.AsQueryable().Where(f => !f.IsDelete && f.WorkspaceId == wsId).CountAsync();
        var chunkCount = await _chunks.AsQueryable().Where(c => !c.IsDelete && c.WorkspaceId == wsId).CountAsync();
        var first = await _chunks.AsQueryable().Where(c => !c.IsDelete && c.WorkspaceId == wsId).OrderBy(c => c.Id).FirstAsync();
        return new StoreStats
        {
            FileCount = fileCount,
            ChunkCount = chunkCount,
            ModelId = first?.ModelId,
            Dimension = first != null ? first.Vector.Length / sizeof(float) : 0
        };
    }
}
