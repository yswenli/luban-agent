/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Entities
*文件名： DbRagFile
*版本号： V1.0.0.0
*唯一标识：RAG 索引文件
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：RAG 索引文件
*
*****************************************************************************/
namespace LubanAgentCli.Entities;

/// <summary>
/// RAG 索引文件实体
/// </summary>
[SugarTable("rag_file", "RAG 索引文件")]
public class DbRagFile : EntityBase
{
    /// <summary>
    /// 文件路径
    /// </summary>
    [SugarColumn(ColumnDescription = "文件路径", Length = 1024, IsNullable = false)]
    public string FilePath { get; set; } = "";

    /// <summary>
    /// 文件 Hash
    /// </summary>
    [SugarColumn(ColumnDescription = "文件Hash", Length = 64, IsNullable = false)]
    public string FileHash { get; set; } = "";

    /// <summary>
    /// 语言
    /// </summary>
    [SugarColumn(ColumnDescription = "语言", Length = 32, IsNullable = false)]
    public string Language { get; set; } = "";

    /// <summary>
    /// 切块数
    /// </summary>
    [SugarColumn(ColumnDescription = "切块数", IsNullable = false)]
    public int ChunkCount { get; set; }

    /// <summary>
    /// 索引时间
    /// </summary>
    [SugarColumn(ColumnDescription = "索引时间", IsNullable = false)]
    public DateTime IndexedTime { get; set; }

    /// <summary>
    /// 工作区ID
    /// </summary>
    [SugarColumn(ColumnDescription = "工作区ID", Length = 64, IsNullable = false)]
    public string WorkspaceId { get; set; } = "";
}

/// <summary>
/// RAG 文本切块实体
/// </summary>
[SugarTable("rag_chunk", "RAG 文本切块")]
public class DbRagChunk : EntityBase
{
    /// <summary>
    /// 文件 Id
    /// </summary>
    [SugarColumn(ColumnDescription = "文件Id", IsNullable = false)]
    public long FileId { get; set; }

    /// <summary>
    /// 切块序号
    /// </summary>
    [SugarColumn(ColumnDescription = "切块序号", IsNullable = false)]
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 起始行
    /// </summary>
    [SugarColumn(ColumnDescription = "起始行", IsNullable = false)]
    public int StartLine { get; set; }

    /// <summary>
    /// 结束行
    /// </summary>
    [SugarColumn(ColumnDescription = "结束行", IsNullable = false)]
    public int EndLine { get; set; }

    /// <summary>
    /// 切块类型
    /// </summary>
    [SugarColumn(ColumnDescription = "切块类型", Length = 32, IsNullable = false)]
    public string ChunkType { get; set; } = "";

    /// <summary>
    /// 符号名
    /// </summary>
    [SugarColumn(ColumnDescription = "符号名", Length = 256, IsNullable = true)]
    public string? SymbolName { get; set; }

    /// <summary>
    /// 内容
    /// </summary>
    [SugarColumn(ColumnDescription = "内容", ColumnDataType = "text", IsNullable = false)]
    public string Content { get; set; } = "";

    /// <summary>
    /// 内容 Hash
    /// </summary>
    [SugarColumn(ColumnDescription = "内容Hash", Length = 64, IsNullable = false)]
    public string ContentHash { get; set; } = "";

    /// <summary>
    /// 向量
    /// </summary>
    [SugarColumn(ColumnDescription = "向量", IsNullable = false)]
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 嵌入模型
    /// </summary>
    [SugarColumn(ColumnDescription = "嵌入模型", Length = 64, IsNullable = false)]
    public string ModelId { get; set; } = "";

    /// <summary>
    /// 工作区ID
    /// </summary>
    [SugarColumn(ColumnDescription = "工作区ID", Length = 64, IsNullable = false)]
    public string WorkspaceId { get; set; } = "";
}
