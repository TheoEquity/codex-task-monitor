# Windows 中止事件时间戳兼容设计

## 目标

让 Windows 监视器正确读取 Codex 当前实际产生的 `turn_aborted` 事件：当事件没有 `payload.completed_at`、但外层包含有效 ISO-8601 `timestamp` 时，仍将该轮识别为已中止。普通任务与用户可见 Fork 使用相同规则，内部自动子代理仍不进入监视面板。

## 根因

实机只读诊断确认，任务查询本身仍能返回普通任务和用户可见 Fork；面板空白不是任务来源筛选造成的。

实际 rollout 中存在如下合法中止事件结构：

```json
{
  "type": "event_msg",
  "timestamp": "<ISO-8601 时间>",
  "payload": {
    "type": "turn_aborted",
    "turn_id": "<轮次标识>",
    "started_at": 0,
    "reason": "<中止原因>"
  }
}
```

当前 `RolloutParser` 对 `task_complete` 和 `turn_aborted` 一律读取 `payload.completed_at`。上述事件缺少该字段时会抛出 `KeyNotFoundException`，随后被转换成 `CodexDataException(FormatChanged)`。`TaskMonitor` 按既定安全策略传播格式变化，因此单个中止的 Fork 会使整次扫描失败，面板无法发布其他正常任务。

诊断和验证只使用固定字段名称、事件类别、错误类别和计数，不输出任务标题、任务 ID、提示词、工作目录、rollout 路径或内容。

## 解析规则

三类已批准生命周期事件仍采用严格、相互独立的规则：

1. `task_started`
   - 保持现有行为；
   - 必须提供有效的 `payload.turn_id` 和数字型 `payload.started_at`。
2. `task_complete`
   - 保持现有行为；
   - 必须提供有效的 `payload.turn_id`、`payload.started_at` 和数字型 `payload.completed_at`；
   - 不允许退回使用外层 `timestamp`。
3. `turn_aborted`
   - 必须提供有效的 `payload.turn_id` 和数字型 `payload.started_at`；
   - 如果存在数字型 `payload.completed_at`，继续优先使用它；
   - 如果 `payload.completed_at` 不存在，则要求外层 `timestamp` 是可解析的 ISO-8601 字符串，并将其作为终止时间；
   - 如果两种终止时间都不可用或无效，仍报告 `FormatChanged`。

外层时间戳只为 `turn_aborted` 的已确认协议形态提供兼容，不成为全部事件的通用宽松回退。解析结果仍是现有 `LifecycleEvent`，不新增 Fork 专用类型或状态。

## 数据流与错误边界

```text
只读 SQLite 任务记录
    -> 普通任务或用户可见 Fork
    -> RolloutParser 严格识别生命周期
       -> turn_aborted.completed_at 存在：使用 payload 时间
       -> turn_aborted.completed_at 缺失：验证并使用外层 timestamp
       -> 两者无效：FormatChanged
    -> TaskMonitor 保持原有错误传播与缓存行为
    -> TaskStateResolver 生成 Aborted 状态
    -> 监视面板与其他任务一起发布
```

`TaskMonitor` 不吞掉 `FormatChanged`，也不按文件跳过格式错误。本次修复属于已确认协议结构的解析兼容，不是忽略损坏数据；因此未知或畸形生命周期记录继续触发现有安全失败路径。

## 测试设计

实施按 TDD 进行：

1. 解析器 RED：构造没有 `payload.completed_at`、但带有效外层 ISO-8601 `timestamp` 的完整 `turn_aborted` 行；确认当前实现抛出格式变化错误。
2. 解析器 GREEN：断言结果类型为 `Aborted`，终止时间精确等于外层 `timestamp`。
3. 优先级保护：同时提供 `payload.completed_at` 和外层 `timestamp` 时，断言继续使用 `payload.completed_at`。
4. 严格性保护：`turn_aborted` 同时缺少有效 `payload.completed_at` 和有效外层 `timestamp` 时，断言仍为 `FormatChanged`。
5. 非目标保护：`task_complete` 缺少 `payload.completed_at` 时，即使外层 `timestamp` 有效也仍为 `FormatChanged`。
6. 端到端回归：使用临时真实 SQLite 与 rollout 文件，让一个用户可见 Fork 产生上述真实中止结构，并与普通任务共存；断言扫描成功、Fork 按普通已中止任务处理，其他任务不会因它消失。
7. 完成聚焦测试后运行全部 Release 测试和警告视为错误的 Release 构建。
8. 重建并升级本机安装后，直接调用实际生产扫描链验证真实本地数据；输出仅包含成功状态、类别和数量，不包含任何任务内容或身份信息。

## 实施范围

- 只修改 rollout 生命周期解析及其测试。
- 不修改 SQLite 查询或普通任务/Fork 来源白名单。
- 不放宽 `TaskMonitor` 的 `FormatChanged` 传播策略。
- 不改变任务身份、已处理键、排序、缓存、轮询或打开行为。
- 不改变窗口布局、拖动、按钮、侧栏自动化或日志格式。
- 不让内部自动子代理进入面板。
- 不修改 Swift/macOS 实现。

## 验收标准

- 实机已确认形态的 `turn_aborted` 不再使整次扫描失败。
- 用户可见 Fork 与普通任务一样出现在监视面板并使用相同状态逻辑。
- 其他正常任务不会因一个合法的 Fork 中止事件而全部消失。
- 未确认或畸形的生命周期格式继续触发 `FormatChanged`，不会被静默忽略。
- 自动化测试、Release 构建和隐私安全的实机扫描验证全部通过。
