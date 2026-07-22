# Codex 本机任务悬浮监控设计

- 日期：2026-07-22
- 状态：已实现
- 平台：当前 Mac，macOS 14+

## 目标

做一个独立、常驻桌面的原生 macOS 小应用，展示当前 Mac 上的 Codex 任务：

- 正在运行的任务；
- 运行结束、仍等用户确认处理的任务。

点击任务主体只打开对应 Codex 对话；完成后的任务必须另点“已处理”才消失。已处理对话出现新一轮活动时自动重新出现。

## 明确不做

- 不做 Codex 插件；插件没有桌面常驻窗口能力，也不能向外部应用提供桌面端内部任务状态。
- 不修改或重签 `/Applications/ChatGPT.app`，不延续桌宠补丁和重补 Launcher。
- 不读取远程主机或云端任务，不联网。
- 不做桌宠、动画、主题系统、标签页、搜索或任务管理后台。

## 已验证的数据契约

本机 Codex 的 `~/.codex/state_5.sqlite` 中，`threads` 表提供任务 ID、标题、更新时间、工作目录和 `rollout_path`。可见本机任务使用：

```sql
SELECT id, title, cwd, updated_at_ms, rollout_path
FROM threads
WHERE archived = 0
  AND preview <> ''
  AND COALESCE(thread_source, 'user') <> 'subagent'
  AND updated_at_ms >= ?;
```

排除 `thread_source = 'subagent'`，避免把 Codex 为并行工作创建的内部子任务重复显示为用户任务。

每个 `rollout_path` 指向 JSONL 日志。任务轮次包含：

- `type = "event_msg"` 且 `payload.type = "task_started"`；
- `type = "event_msg"` 且 `payload.type = "task_complete"`；
- `type = "event_msg"` 且 `payload.type = "turn_aborted"`。

生命周期事件使用稳定的 `payload.turn_id` 作为一轮活动的唯一标识，并从数字字段 `payload.started_at`、`payload.completed_at` 读取时间。中止与正常完成一样进入“等待处理”。

已验证 Codex 对话深链为 `codex://threads/<thread-id>`。

独立启动的 `codex app-server` 无法看到 Codex Desktop 所拥有的运行态，返回 `notLoaded`，因此不作为数据源。

## 状态规则

对每个任务只看最新一轮：

| 条件 | 显示状态 |
|---|---|
| 最新 `task_started.turn_id` 尚无同 ID 的 `task_complete` 或 `turn_aborted` | 运行中 |
| 该 turn 已完成或中止、属于本应用追踪范围、且未标记处理 | 等待处理 |
| 该 turn 已完成且已标记处理 | 隐藏 |
| 后续出现新的 `task_started.turn_id` | 作为新一轮重新出现 |

运行中的任务不能标记“已处理”。

### 首次启动

首次启动保存 `monitorStartedAt`，并收养当时仍在运行的 turn ID：

- 当时正在运行的任务立即显示，完成后进入“等待处理”；
- 首次启动前已经完成的旧任务不导入；
- 首次启动后产生的 turn 即使在应用暂时关闭期间完成，下次启动仍会进入“等待处理”。

首次尝试的时间在应用模型创建时固定，并向下对齐到 rollout 的整数秒精度；若部分 rollout 暂时不可读，应用不保存不完整的首次边界，后续重试仍沿用同一时间，避免同秒事件或重试期间完成的任务漏显。

rollout 可能残留很久以前没有闭合的 `task_started`，而独立应用无法读取 Codex Desktop 的内存运行态。首版因此只在首次启动时收养同时满足以下条件的未闭合 turn：对应任务更新时间不早于当前 Codex Desktop 进程的启动时间，且不早于一小时前。首次边界建立后，新 turn 不设一小时 TTL。

本应用在 `UserDefaults` 中只保存：

- `monitorStartedAt`；
- 首次启动时收养的活动 turn ID；
- 已处理 turn ID；
- 悬浮窗位置。

不修改 Codex 数据库或 rollout 文件。

## 读取流程

应用每 2 秒执行一次：

1. 以只读方式查询 `state_5.sqlite` 中未归档的可见任务，并用已有更新时间索引在 SQL 内过滤监控边界之前的历史任务。
2. 首次读取 rollout 时从文件尾分块向前查找最近一个 `task_started`、`task_complete` 或 `turn_aborted`，每行最多解析一次且不设固定字节截断。之后文件增长时只解析新增字节；未变化文件复用内存结果。
3. 用最近生命周期事件的类型和 `turn_id` 计算任务状态。
4. 应用首次启动边界和已处理集合。
5. 仅当结果改变时更新界面。

首版使用定时轮询，不实现文件系统监听、守护进程或第二份任务数据库。若实测空闲资源占用明显，再考虑事件监听。

## 界面与交互

应用使用 SwiftUI 内容加无标题栏 `NSPanel`，避免透明系统标题栏压缩列表内容：

- 无 Dock 图标；
- 悬浮在普通窗口之上，贴在屏幕边缘；
- 标题栏显示“Codex 任务”和当前数量；
- 每行显示标题、状态、项目目录短名称和相对时间；
- 蓝点表示“运行中”，黄点表示“等待处理”；
- 点击任务主体打开 `codex://threads/<thread-id>`；
- 只有已完成行显示“已处理”按钮；
- 无任务时缩成一条窄状态条 `Codex 任务 · 0`；
- 拖动标题栏可移动，位置自动保存。

应用首次运行时注册为登录启动项；如果系统拒绝，继续运行并在菜单中提供“登录时启动”重试入口。除该入口、重新读取和退出外，不增加设置页。

## 异常处理

- SQLite 短暂忙碌：保留当前列表，下轮重试。
- rollout 最后一行正在写入或 JSON 不完整：忽略该行，下轮重试。
- `state_5.sqlite` 不存在：显示“未找到本机 Codex 数据”。
- 必需表列或事件字段缺失：显示“Codex 数据格式已变化”，停止猜测状态并继续定时重试。
- 深链打开失败：任务保留，显示一次错误提示。
- 单个 rollout 不可读：保留其他任务，并在标题区显示非阻塞错误标记。

任何读取错误都不得清空已显示的任务或写入 Codex 文件。

## 最小实现边界

使用系统能力：SwiftUI、AppKit、SQLite3、`UserDefaults`、`SMAppService` 和 `NSWorkspace`。不引入第三方依赖。

实现只需要：

1. 只读任务查询和 rollout 状态解析；
2. 一个保存首次边界与已处理 turn ID 的状态模型；
3. 一个悬浮列表窗口；
4. 一个原生登录启动注册入口。

不创建插件 manifest、MCP server、后台 daemon、网络服务或更新器。

## 验收标准

1. 应用首次启动时只显示当时仍在运行的任务，不导入更早已完成任务。
2. 新任务出现 `task_started` 后，最迟 2 秒内显示为“运行中”。
3. 同一 turn 出现 `task_complete` 后，最迟 2 秒内显示为“等待处理”。
4. 点击任务主体打开正确的 Codex 对话，但任务不消失。
5. 点击“已处理”后该 turn 消失。
6. 同一对话出现新的 turn ID 后重新出现。
7. 应用退出期间产生、且晚于首次监控时间的完成 turn，在下次启动时显示。
8. 运行中的任务没有“已处理”按钮。
9. 半截 JSON、SQLite 短暂锁定或单文件读取失败不会让当前列表闪空。
10. Codex 更新后，只要已验证字段仍存在，本应用无需重补或重签 Codex。
11. 重启 Mac 后应用自动启动，并恢复窗口位置和已处理记录。

## 验证

`swift run CoreChecks` 使用临时 SQLite/JSONL 样本覆盖：

- started → 运行中；
- complete → 等待处理；
- 已处理 → 隐藏；
- 新 turn → 重新出现；
- 首次启动边界；
- 首次边界与生命周期时间的秒级精度对齐；
- 完成跨越首次边界；
- 中止与旧 turn 的迟到完成事件；
- 子任务过滤；
- 半截 JSON 行忽略并重试；
- 首次读取失败不保存边界；
- 部分 rollout 不可读时也不保存首次边界；
- 必需字段变化经监控层明确上报；
- 大行跨 64 KB 分块与新增字节增量解析；
- 面板行数与高度计算；
- `UserDefaults` 持久化。

真实环境已核对悬浮窗显示、`codex://` 的系统处理程序和登录启动注册（本机 ad-hoc 签名状态为 enabled）；完成/已处理转移使用临时 JSONL/SQLite 验证，未对用户真实任务执行“已处理”，也未以重启 Mac 作为本次验收步骤。
