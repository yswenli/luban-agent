/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
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
*描述：SQLite 向量存储实现，通过 WorkspaceManager 实例实现工作区隔离
*
*****************************************************************************/
namespace LubanAgentCore.Retrieval;

/// <summary>
/// SQLite 向量存储实现，通过 <see cref="IWorkspaceManager"/> 实现工作区隔离
/// </summary>
public class SqliteVectorStore : IVectorStore, IVectorTopKSearch
{
    private readonly LlmWikiFileRepository _files = new();
    private readonly LlmWikiChunkRepository _chunks = new();
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IWorkspaceContext? _workspaceContext;
    // 写操作信号量，确保并发写入时的数据一致性
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// 创建 SQLite 向量存储。
    /// </summary>
    /// <param name="workspaceManager">工作区管理器（单例实例；用于未显式传入 workspaceId 时回退当前工作区）。</param>
    /// <param name="workspaceContext">会话级工作区上下文（优先于全局当前工作区，避免跨会话串扰）。</param>
    public SqliteVectorStore(IWorkspaceManager workspaceManager, IWorkspaceContext? workspaceContext = null)
    {
        _workspaceManager = workspaceManager;
        _workspaceContext = workspaceContext;
    }

    /// <summary>
    /// 解析工作区ID：显式传入优先；其次本会话绑定的工作区；最后回退全局当前工作区（空字符串表示无工作区上下文）
    /// </summary>
    private string ResolveWorkspaceId(string? workspaceId)
        => workspaceId
           ?? _workspaceContext?.WorkspaceId
           ?? (_workspaceManager.CurrentWorkspace?.WorkspaceId ?? "");

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedFile>> GetFilesAsync(string? pathPrefix = null, string? workspaceId = null)
    {
        var wsId = ResolveWorkspaceId(workspaceId);
        var q = _files.AsQueryable().Where(f => !f.IsDelete && f.WorkspaceId == wsId);
        if (!string.IsNullOrEmpty(pathPrefix)) q = q.Where(f => f.FilePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));
        var list = await q.ToListAsync();
        return list.Select(f => new IndexedFile { Id = f.Id, FilePath = f.FilePath, FileHash = f.FileHash, Language = f.Language }).ToList();
    }

    /// <inheritdoc />
    public async Task<long> UpsertFileAsync(string filePath, string fileHash, string language, int chunkCount, string? workspaceId = null)
    {
        await _writeGate.WaitAsync();
        try
        {
            var wsId = ResolveWorkspaceId(workspaceId);
            var existing = await _files.GetByFilePathAsync(filePath, wsId);
            if (existing != null)
            {
                await _files.UpdateAsync(f => new DbLlmWikiFile
                {
                    FileHash = fileHash, Language = language, ChunkCount = chunkCount,
                    IndexedTime = DateTime.Now, UpdateTime = DateTime.Now
                }, f => f.Id == existing.Id);
                return existing.Id;
            }
            var entity = new DbLlmWikiFile
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
    public async Task SoftDeleteFileAsync(long fileId, string? workspaceId = null)
    {
        await _writeGate.WaitAsync();
        try
        {
            await _files.UpdateAsync(f => new DbLlmWikiFile { IsDelete = true, UpdateTime = DateTime.Now }, f => f.Id == fileId);
            await _chunks.UpdateAsync(c => new DbLlmWikiChunk { IsDelete = true, UpdateTime = DateTime.Now }, c => c.FileId == fileId);
        }
        finally { _writeGate.Release(); }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredChunk>> GetFileChunksAsync(long fileId, string? workspaceId = null)
    {
        var wsId = ResolveWorkspaceId(workspaceId);
        var list = await _chunks.AsQueryable().Where(c => c.FileId == fileId && !c.IsDelete && c.WorkspaceId == wsId).ToListAsync();
        return list.Select(c => new StoredChunk { Id = c.Id, ChunkIndex = c.ChunkIndex, ContentHash = c.ContentHash, Vector = VectorMath.ToFloats(c.Vector) }).ToList();
    }

    /// <inheritdoc />
    public async Task ReplaceFileChunksAsync(long fileId, string modelId, IReadOnlyList<ChunkVectorPair> chunks, string? workspaceId = null)
    {
        await _writeGate.WaitAsync();
        try
        {
            var wsId = ResolveWorkspaceId(workspaceId);
            // 使用事务保证删除旧切块和插入新切块的原子性
            using var scope = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
            await _chunks.DeleteAsync(c => c.FileId == fileId);
            foreach (var p in chunks)
            {
                var entity = new DbLlmWikiChunk
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
    public async Task<IReadOnlyList<VectorEntry>> LoadVectorsAsync(string? pathPrefix = null, string? language = null, int maxResults = int.MaxValue, string? workspaceId = null)
    {
        var q = BuildChunkQuery(pathPrefix, language, workspaceId);

        // 调用方可能要求全量候选（int.MaxValue）：超过上限时抽样并告警，避免大库内存/CPU 失控
        if (maxResults > MaxCandidateChunks)
        {
            var candidates = await q.CountAsync();
            if (candidates > MaxCandidateChunks)
            {
                Logger.Warn($"检索候选切块过多（{candidates}），本次仅抽样 {MaxCandidateChunks} 个参与相似度计算，结果可能不完整");
                maxResults = MaxCandidateChunks;
            }
        }

        // 固定按 Id 升序，保证同一查询的候选集和并列分数顺序可复现
        q = q.OrderBy((c, f) => c.Id);
        if (maxResults < int.MaxValue) q = q.Take(maxResults);
        var list = await q.Select((c, f) => new { c.Id, c.Vector }).ToListAsync();
        return list.Select(x => new VectorEntry { ChunkId = x.Id, Vector = VectorMath.ToFloats(x.Vector) }).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 只做一次「有没有候选」的存在性判断：库为空时调用方可据此短路，
    /// 不必先加载嵌入模型（数十 MB 原生内存）再发现无内容可查。
    /// </remarks>
    public async Task<bool> HasVectorsAsync(string? pathPrefix = null, string? language = null, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await BuildChunkQuery(pathPrefix, language, workspaceId)
            .OrderBy((c, f) => c.Id)
            .Select((c, f) => new { c.Id })
            .Take(1)
            .ToListAsync();
        return rows.Count > 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 分页扫描 + 有界 top-K：内存占用固定在「一页候选 + K 条结果」，
    /// 不随索引规模增长；相似度直接在 SQLite 返回的 byte[] 上按 float 读取，逐行不再 new float[]。
    /// </remarks>
    public async Task<IReadOnlyList<VectorHit>> SearchTopKAsync(float[] queryVector, int topK, string? pathPrefix = null, string? language = null, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        topK = Math.Clamp(topK, 1, 20);
        var collector = new VectorTopK(topK);

        // 按 Id 游标翻页（而不是 OFFSET）：每页重新构造查询，既避开 SqlSugar 复用同一
        // queryable 实例时的语句缓存风险，也避免深分页时 OFFSET 的线性扫描开销。
        var lastId = 0L;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cursor = lastId;
            var rows = await BuildChunkQuery(pathPrefix, language, workspaceId)
                .Where((c, f) => c.Id > cursor)
                .OrderBy((c, f) => c.Id)
                .Select((c, f) => new { c.Id, c.Vector })
                .Take(ScanPageSize)
                .ToListAsync();

            foreach (var row in rows)
            {
                collector.Add(row.Id, VectorMath.Cosine(queryVector, row.Vector));
                lastId = row.Id;
            }

            if (rows.Count < ScanPageSize) break;
        }

        return collector.Results;
    }

    /// <summary>检索候选的唯一口径：未软删切块 × 未软删文件 × 目标工作区。</summary>
    private ISugarQueryable<DbLlmWikiChunk, DbLlmWikiFile> BuildChunkQuery(string? pathPrefix, string? language, string? workspaceId)
    {
        var wsId = ResolveWorkspaceId(workspaceId);
        var q = _chunks.Context.Queryable<DbLlmWikiChunk>()
            .InnerJoin<DbLlmWikiFile>((c, f) => c.FileId == f.Id)
            .Where((c, f) => !c.IsDelete && !f.IsDelete && f.WorkspaceId == wsId);
        if (!string.IsNullOrEmpty(pathPrefix)) q = q.Where((c, f) => f.FilePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(language)) q = q.Where((c, f) => f.Language == language);
        return q;
    }

    /// <inheritdoc />
    public async Task<Dictionary<long, CodeChunk>> GetChunksAsync(IReadOnlyList<long> chunkIds, string? workspaceId = null)
    {
        var wsId = ResolveWorkspaceId(workspaceId);
        var q = _chunks.Context.Queryable<DbLlmWikiChunk>()
            .InnerJoin<DbLlmWikiFile>((c, f) => c.FileId == f.Id)
            .Where((c, f) => chunkIds.Contains(c.Id) && !c.IsDelete && !f.IsDelete && f.WorkspaceId == wsId)
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
    public async Task<StoreStats> GetStatsAsync(string? workspaceId = null)
    {
        var wsId = ResolveWorkspaceId(workspaceId);
        var fileCount = await _files.AsQueryable().Where(f => !f.IsDelete && f.WorkspaceId == wsId).CountAsync();
        // 切块统计需联表排除已软删的文件，避免残留孤儿切块被计入
        var liveChunks = _chunks.Context.Queryable<DbLlmWikiChunk>()
            .InnerJoin<DbLlmWikiFile>((c, f) => c.FileId == f.Id)
            .Where((c, f) => !c.IsDelete && !f.IsDelete && f.WorkspaceId == wsId);
        var chunkCount = await liveChunks.CountAsync();
        var first = await liveChunks.OrderBy((c, f) => c.Id).Select((c, f) => new { c.ModelId, c.Vector }).FirstAsync();
        return new StoreStats
        {
            FileCount = fileCount,
            ChunkCount = chunkCount,
            ModelId = first?.ModelId,
            Dimension = first != null ? first.Vector.Length / sizeof(float) : 0
        };
    }

    /// <summary>单次语义检索允许加载的最大候选切块数（超过则抽样并告警）</summary>
    private const int MaxCandidateChunks = 200_000;

    /// <summary>top-K 检索的分页扫描大小：内存只驻留一页候选，不随索引规模增长</summary>
    private const int ScanPageSize = 512;
}
