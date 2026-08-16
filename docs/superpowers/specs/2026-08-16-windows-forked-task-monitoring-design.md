# Windows Fork 任务监控设计

## 目标

让用户在 Codex 界面中主动 Fork、且作为独立任务出现在左侧列表中的任务，与手动新建任务完全一致地进入 Windows 监视面板。Fork 任务使用相同的运行中、等待处理、打开和“已处理”流程；内部自动运行的子代理任务继续排除。

## 已确认的数据事实

当前机器上的只读数据核查表明：

- 普通用户任务通常是 `thread_source='user'`、`source='vscode'`。
- 用户可见 Fork 任务是 `thread_source='subagent'`、`source='vscode'`。
- 内部子代理使用 JSON 来源，其中包含 `subagent.thread_spawn` 等字段；它们不应进入面板。
- 最近的用户可见 Fork 记录未归档、预览非空、更新时间满足一小时扫描窗口，并且 rollout 文件中存在受支持的 `task_started`、`task_complete` 或 `turn_aborted` 生命周期标记。
- 现有 SQLite 条件从语义上已经允许 `subagent/vscode`，因此不能通过包含全部 `subagent` 来修复；必须用端到端回归找到实际丢失边界。

核查过程只比较来源类别、计数和生命周期类型，不把任务标题、提示词、线程 ID 或 rollout 内容写入日志或文档。

## 可管理任务规则

一条任务记录只有同时满足以下条件，才允许进入监控流水线：

1. `archived = 0`；
2. `preview` 非空；
3. `updated_at_ms` 不早于调用方给出的扫描下限；
4. 来源属于下列任一种：
   - 普通用户任务；
   - `thread_source='subagent' AND source='vscode'` 的用户可见 Fork；
5. JSON 形式的内部子代理来源不满足第 4 条，必须排除。

通过规则后，不在领域模型中保留“Fork 特殊类型”。普通任务与 Fork 都转换为相同的 `ThreadRecord`，避免后续状态、排序和交互出现两套逻辑。

## 数据流

```text
只读 state_5.sqlite
    -> SqliteThreadStore 应用可管理任务规则
    -> ThreadRecord（普通任务与 Fork 同构）
    -> TaskMonitor 读取 rollout 生命周期
    -> TaskStateResolver 计算运行中/等待处理
    -> MonitorItem（身份始终为 threadID:turnID）
    -> MonitorViewModel.Items
    -> Windows 监视面板
```

每个边界的要求如下：

- `SqliteThreadStore`：只读查询；返回用户可见 Fork，拒绝内部子代理。
- `RolloutParser`：继续只识别已批准的三种生命周期事件，不因 Fork 扩大事件映射。
- `TaskMonitor`：Fork 与普通任务使用同一基线、缓存、排序和不可读降级机制。
- `TaskStateResolver`：不根据任务来源改变状态规则。
- `MonitorViewModel`：不增加 Fork 标签、分组或专用按钮；直接显示相同的 `MonitorItemViewModel`。
- 打开与“已处理”：继续使用精确 `threadID:turnID`，不能因 Fork 继承父任务历史或复用 turn ID 而跨线程隐藏。

## 同名与侧栏定位

Fork 可能保留父任务标题。面板允许同时显示同名的普通任务和 Fork，因为它们的内部身份不同。打开正文继续使用精确线程 ID；侧栏自动定位若无法从标题和分组结构中唯一识别，必须保持现有安全行为：报告歧义且不猜测、不点击错误会话。

## 错误处理与隐私

- SQLite 仍以只读模式打开，不能创建或修改 Codex 数据库。
- 数据库缺失、格式变化和 rollout 不可读继续使用现有固定错误类别。
- 不记录任务标题、线程 ID、父线程 ID、提示词、工作目录或 rollout 内容。
- 可以记录固定分类和计数，例如“不可读 rollout 数量”；不新增 Fork 关系日志。
- 如果实机数据与已确认的来源规则发生变化，应以格式变化或安全排除处理，不包含全部未知 subagent。

## 测试设计

实施必须按 TDD 找到最早失败边界：

1. SQLite 测试同时插入普通任务、`subagent/vscode` 用户可见 Fork、JSON 内部子代理、归档任务和空预览任务；断言只返回普通任务与 Fork。
2. 监控集成测试使用真实临时 SQLite 和真实 rollout 文件，让 Fork 分别产生运行中、完成和中止事件；断言它与普通任务采用相同状态和排序规则。
3. 身份隔离测试让父任务与 Fork 使用相同 turn ID；对父任务设置精确 `threadID:turnID` 已处理键后，Fork 仍必须显示。
4. ViewModel 测试断言 Fork 生成普通 `MonitorItemViewModel`，可调用相同的打开和“已处理”命令。
5. 如果某层测试在当前实现上已经通过，不为制造代码变化而重写该层；继续向后测试，直到复现实机缺失。生产修复只落在第一个真实失败边界。

完成修复后运行聚焦测试、完整 Release 测试、警告视为错误的构建、只读预检和安装包验证。重新发布后仍需由用户在真实 Codex UI 中确认一个新 Fork 任务能像普通任务一样进入面板。

## 不在范围内

- 不显示内部子代理任务。
- 不展示父子关系、Fork 徽标或任务树。
- 不改变最多 6 行、2 秒轮询、窗口尺寸和排序规则。
- 不放宽同名侧栏匹配或执行猜测点击。
- 不修改 Swift/macOS 实现。
