# 创建流水线（newproject-create-pipeline）

> 「新建项目」插件原子技能 · 对应 **Step 7** · **M4-P1 骨架版**（执行脚本在 M4-P6 补齐）。

## 何时使用

- 主 skill `newproject-create` 的第 7 步；
- 或**仅补建某端流水线**（例如 Mobile 后补）。

## 前置条件

- Step 6 已完成（各端仓库有可拉取的代码）。
- 插件配置里已登记该端的**参考流水线标识**（`newproject.settings.json` 的 `pipelineReferences`，M6 真机联调时填入真实值）。

## 执行（云效相关步骤：委托 MCP 工具，PRD §4.10.7 / §4.11.3 注⑤）

对所选**每个端**（最多 3 条）：

1. `mcp_yunxiao_flow_clone_pipeline`：以该端 `TJCES` 参考流水线为源
   （内部：`GetPipeline` 拉取源 YAML → 按 `substitutions` 替换**名称 / 端口 / Codeup 仓库地址 / 流水线参数** → `CreatePipeline` 创建）。
2. 若需确认创建结果，可 `mcp_yunxiao_flow_list_pipelines` 复核；列表返回体不含创建人与状态，需要时再逐条 `GetPipeline` 补充（注⑦，注意 N+1 调用开销）。
3. 回写档案：流水线 ID 列表与远端产物清单（`RemoteArtifactKind.Pipeline`）。

## 结果

- 所选端**各 1 条、最多 3 条**流水线；名称含项目缩写，便于识别。

## 重要约束

- 新建流水线**默认禁用部署阶段**，并产出「**待人工绑定清单**」（PRD §6 #14）：部署相关的变量 / 主机 / 环境需人工确认后再启用。
- 建完流水线**不自动运行**（PRD §4.11.3 注⑥）；如需排障，Agent 可即席调用运行 / 日志相关工具。
- 中止 / 清理时**远端流水线不自动删除**，只列孤儿清单。

## 失败处理

- 参考流水线 ID 为空或不存在：阻断并提示「该端参考流水线未配置 / 失效」，给出配置位置。
- 替换后 YAML 非法：停在创建前，附上失败原因，不创建半成品流水线。