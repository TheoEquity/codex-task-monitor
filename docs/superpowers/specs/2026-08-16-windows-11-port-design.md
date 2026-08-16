# Codex Task Monitor Windows 11 适配设计

## 1. 状态与目标

本设计已由用户确认，目标是在保留现有 Swift/macOS 实现的同时，在同一仓库中新增面向当前 Windows 11 x64 机器的 C#/.NET 8 实现。

Windows 版本必须复刻现有核心体验：

- 显示所有非空闲 Codex 任务。
- 使用蓝点表示运行中，绿点表示已结束但尚未处理。
- 点击任务通过 `codex://threads/<UUID>` 打开对应正文。
- 打开正文后，通过 Windows UI Automation 将 Codex 侧栏滚动到唯一匹配的任务。
- “已处理”只隐藏准确的 `threadID:turnID` 任务。
- 超过 6 个任务时固定列表高度并显示滚动条。
- 自动注册为当前用户登录时启动。
- 提供按当前用户安装的正式 Windows 安装包。

首版只保证当前机器、当前 Windows 11 x64 环境和当前 Codex/ChatGPT 桌面应用版本。跨机器 UIA 兼容、通用中英文界面适配、ARM64、商业代码签名和统一跨平台 UI 不在首版范围内。

## 2. 已验证的环境约束

设计阶段已在目标机器上完成只读验证：

- Codex Windows 桌面应用已安装并以 `ChatGPT.exe` 运行。
- `%USERPROFILE%\.codex\state_5.sqlite`、`session_index.jsonl`、`.codex-global-state.json` 和 rollout JSONL 均存在。
- `threads` 表继续提供 `id`、`rollout_path`、`cwd`、`title`、`archived`、`updated_at_ms`、`thread_source`、`preview` 等监控所需字段。
- 当前数据库还提供 `is_pinned`、`thread_section_id`，并有 `thread_sections(id, name)` 表。
- rollout 路径是有效的 Windows 本地路径。
- `codex://` URL 协议已注册。
- UI Automation 树可读取 Codex 侧栏任务标题。一次抽样中，49 个近期标题有 25 个出现在当前 UIA 树，24 个唯一匹配，1 个存在重名。
- 匹配到的任务主要暴露为 `ControlType.ListItem`，但不提供标准 `ScrollItemPattern`。因此必须提供受控滚动回退。

这些本地文件和 UIA 结构都是实现依赖，不视为稳定的公共 API。格式或界面变化必须安全失败，不得猜测目标。

## 3. 仓库与项目结构

现有 macOS 代码保持原位，Windows 代码放在独立目录：

```text
codex-task-monitor/
├─ Sources/                              现有 Swift/macOS 实现
├─ windows/
│  ├─ CodexTaskMonitor.sln
│  ├─ CodexTaskMonitor.Core/             平台无关的 Windows 业务核心
│  ├─ CodexTaskMonitor.Windows/          WPF 与 Win32/UIA 集成
│  ├─ CodexTaskMonitor.Tests/            xUnit 测试
│  └─ Installer/                         Inno Setup 配置
└─ .github/workflows/windows.yml         Windows 构建、测试与打包
```

Swift 和 C# 不共享二进制代码。两者共享行为规范、测试场景和夹具数据。这样可以保留现有 macOS 实现，同时避免 Swift/Windows UI 和跨语言调用造成的额外复杂度。

## 4. 技术选型

- 运行时：.NET 8。
- UI：WPF，使用轻量 MVVM，不引入额外 MVVM 框架。
- 数据库：`Microsoft.Data.Sqlite`，以只读模式打开 Codex SQLite 数据库。
- Windows 集成：`System.Windows.Automation`、必要的 Win32 P/Invoke、`ProcessStartInfo.UseShellExecute`。
- 测试：xUnit。
- 安装器：Inno Setup，按用户安装。
- 发布：Windows 11 x64 自包含部署，目标机器无需预装 .NET。

## 5. 组件边界

### 5.1 CodexTaskMonitor.Core

`CodexDataPaths`

- 从用户配置目录解析数据库、session index、全局状态和设置文件位置。
- 不硬编码用户名或盘符。

`SqliteThreadStore`

- 以只读模式打开 `state_5.sqlite`。
- 验证必需字段是否存在。
- 只返回未归档、具有预览、并且不是内部 subagent 的用户可见线程。
- 同时读取 `is_pinned`、`thread_section_id` 和对应 section 名称，供侧栏定位使用。

`RolloutParser` 与 `TaskMonitor`

- 移植现有 Swift 生命周期解析语义。
- 识别 `task_started`、`task_complete`、`turn_aborted`。
- 使用文件大小和修改时间缓存解析结果。
- 文件仅追加时只解析新增字节；文件缩短或被替换时完整重读。
- 忽略尚未写完的最后一行，但对已经换行的损坏生命周期事件报告格式变化。

`TaskStateResolver`

- 保留首次启动基线、最近一小时运行任务收养、跨基线完成和精确任务隐藏语义。
- 任务唯一键为 `threadID:turnID`。

`MonitorPreferences`

- 将基线、收养任务和已处理任务保存到 `%LOCALAPPDATA%\CodexTaskMonitor\settings.json`。
- 使用临时文件加原子替换写入，避免异常退出造成半写文件。

### 5.2 CodexTaskMonitor.Windows

`MonitorWindow` 与 `MonitorViewModel`

- 呈现浮窗并每 2 秒刷新任务。
- UI 线程只接收不可变快照；SQLite 和文件解析在后台执行。
- 防止重叠扫描：上一次扫描未结束时不启动下一次扫描。

`SingleInstanceService`

- 使用按用户命名互斥量保证只运行一个实例。
- 再次启动时激活已有浮窗后退出。

`CodexDeepLinkLauncher`

- 验证 UUID 后，通过 shell 打开 `codex://threads/<UUID>`。
- 深链失败时不启动 UIA 定位。

`SidebarTargetResolver`

- 从 session index 的最新完整记录解析完整任务标题。
- 优先使用 SQLite 的置顶标记和 section 关系确定分组。
- 其次使用全局状态中的项目分配、项目外任务列表和唯一 cwd 项目根目录匹配。
- 缺少标题或无法唯一确定分组时返回不可定位，不使用数据库短标题猜测。

`ChatGptWindowLocator`

- 查找具有主窗口句柄的 `ChatGPT.exe`。
- 深链打开后等待主窗口和 UIA 根节点稳定，最长 5 秒。

`WindowsSidebarRevealer`

- 读取当前 ChatGPT 主窗口的 UIA 树。
- 只接受完整标题匹配的 `ListItem`。
- 标题全局唯一时可直接使用；存在重名时必须通过分组标题与 UIA 遍历顺序消歧。
- 无法证明唯一时返回 `AmbiguousTask`，不得点击或滚动到任意候选项。

`SidebarScrollController`

- 从侧栏可访问元素的矩形聚类确定侧栏滚动区域。
- 优先尝试祖先容器提供的 `ScrollPattern`。
- 标准模式不可用时，向已验证的 Chromium 渲染窗口和侧栏坐标发送受控滚轮消息。
- 若窗口对消息无响应，只有在 ChatGPT 位于前台时才允许使用 `SendInput`；保存并恢复鼠标位置，全程不发送点击。
- 找不到目标时先向上滚动，直到连续两次侧栏签名不变；随后向下扫描，直到找到目标、连续两次签名不变、达到 80 步或超过 8 秒。
- 每次滚动后重新获取 UIA 快照。找到唯一目标且其矩形进入侧栏可见区域后停止。

`StartupRegistration`

- 使用 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 管理当前用户登录启动。
- 安装器与应用使用同一个注册值名。

`LocalDiagnostics`

- 写入 `%LOCALAPPDATA%\CodexTaskMonitor\logs`，按大小轮转。
- 只记录错误类别、耗时、数量和版本，不记录任务标题、提示内容、rollout 正文或认证信息。

## 6. 数据与事件流

1. 应用启动，加载偏好设置并建立首次运行基线。
2. `SqliteThreadStore` 查询最近相关的用户可见线程。
3. `TaskMonitor` 读取每个线程对应的 rollout，解析最新生命周期事件。
4. `TaskStateResolver` 过滤旧任务、已处理任务和不应展示的内部线程。
5. `MonitorViewModel` 按活动时间倒序发布任务列表。
6. 用户点击任务时，应用先打开系统已注册的 `codex://` 深链。
7. `SidebarTargetResolver` 解析完整标题和分组。
8. `WindowsSidebarRevealer` 查找唯一 UIA 目标；目标不可见时调用受控滚动状态机。
9. 用户点击“已处理”时，只持久化当前 `threadID:turnID` 并立即刷新列表。

每次用户打开任务都会增加定位 generation。新的定位请求会取消旧 generation，防止较慢的旧请求继续滚动侧栏。

## 7. 窗口与交互设计

- 固定宽度 330 DIP。
- 标题栏高度 48 DIP，任务行高度 62 DIP。
- 最多直接显示 6 行；更多任务在固定高度列表中滚动。
- 窗口无系统边框、置顶、不显示任务栏按钮、支持拖动并保存位置。
- 使用 Windows 11 圆角、深色半透明背景和 Segoe UI。
- 运行中任务为蓝点，等待处理任务为绿点。
- 点击任务正文区域打开任务；等待处理项显示独立“已处理”按钮。
- 菜单包含“重新读取”“登录时启动”和“退出”。
- 错误条使用橙色，仅显示当前最优先的用户可操作错误。
- 首版遵循当前 Windows 虚拟桌面的原生行为，不依赖未公开 API 强制固定到所有虚拟桌面。

## 8. 错误处理与安全降级

- 数据库缺失：显示“未找到本机 Codex 数据”，低频重试。
- SQLite 暂时忙或 rollout 暂时不可读：保留缓存状态，显示可恢复错误并继续轮询。
- 必需字段或生命周期格式变化：显示“Codex 数据格式已变化”，停止本轮解析。
- 不完整 JSONL 尾行：保留上次结果，下次轮询重试。
- 深链失败：显示“无法打开对应的 Codex 对话”，不执行 UIA 操作。
- ChatGPT 未就绪：在规定时间内重试，之后显示“已打开对话；暂时无法在侧栏定位”。
- 任务重名或分组不明：不滚动、不点击，显示明确警告。
- UIA 结构变化或无法确定侧栏区域：降级为只打开正文。
- 滚动超时：立即停止输入模拟并报告定位失败。

应用只读访问 Codex 数据。除 Windows shell 深链、UI Automation、开机启动注册和自身本地设置外，不修改 Codex 文件或界面状态。应用不包含网络请求。

## 9. 测试设计

### 9.1 单元测试

将现有 `CoreChecks` 场景移植为 xUnit，包括：

- UUID 深链校验。
- session index 最新标题与残缺尾行。
- 置顶、section、项目和项目外分组解析。
- 重名任务拒绝。
- 面板高度与列表插入行为。
- started、completed、aborted、跨基线完成和已处理状态。
- 大 rollout、增量追加、残缺 JSON、错误完整行和生命周期格式变化。
- SQLite 可见线程过滤与 Codex 创建的用户可见线程。
- 缓存、文件截断、暂时不可读和偏好设置恢复。

### 9.2 集成测试

- 使用临时 SQLite、session index、全局状态和 rollout 文件验证完整扫描流程。
- 使用 UIA 抽象和合成元素树测试唯一匹配、分组消歧、侧栏边界识别、顶部/底部检测、滚动超时和 generation 取消。
- 使用假的深链启动器验证深链失败时不会执行 UIA。
- 使用临时注册表位置测试开机启动逻辑，不修改真实登录项。

### 9.3 当前机器人工验收

- Codex 已运行与未运行两种启动方式。
- 运行中、已完成、中止和连续新 turn。
- 当前可见任务与需要向上/向下滚动才能看到的任务。
- 置顶、section、项目和项目外任务。
- 同名任务安全失败。
- 7 个以上任务的浮窗滚动。
- 开机启动、升级安装、卸载与再次安装。

真实 Codex UIA 测试不在 CI 中自动操作用户桌面。

## 10. 构建、安装与发布

GitHub Actions 在 `windows-latest` 上执行：

1. 恢复 NuGet 依赖。
2. 以 warnings-as-errors 构建 Release。
3. 执行全部 xUnit 测试。
4. `dotnet publish` 生成 win-x64、自包含、单文件优先的发布目录。
5. 调用 Inno Setup 生成按用户安装的 `.exe`。
6. 上传便携构建目录和安装包作为工作流产物；版本标签发布时附加到 GitHub Release。

安装器不要求管理员权限，负责创建开始菜单快捷方式和卸载入口。升级保持 `%LOCALAPPDATA%\CodexTaskMonitor\settings.json`。卸载清除开机启动注册和程序文件，但默认保留设置与无敏感内容的诊断日志。

首版不配置商业代码签名，因此 SmartScreen 可能要求用户手动确认。签名证书和信誉积累属于后续发布增强，不阻塞当前机器验收。

## 11. 验收标准

Windows 首版达到以下条件即视为完成：

1. 安装包能在当前 Windows 11 x64 机器上按用户安装、升级和卸载。
2. 无需预装 .NET 即可启动，且同一用户只能运行一个实例。
3. 新运行任务在正常轮询条件下 4 秒内出现在浮窗。
4. 完成或中止任务变为等待处理；“已处理”只隐藏准确任务，新 turn 会重新出现。
5. 点击任务能打开正确的 `codex://threads/<UUID>` 正文。
6. 唯一标题任务无论初始是否可见，均能在 8 秒内滚动到侧栏可见区域。
7. 重名、缺失映射、UIA 结构变化和滚动超时时不误点，正文仍保持打开并显示警告。
8. 超过 6 个任务时浮窗高度固定且列表可滚动。
9. 登录启动可启用，卸载后不留下登录启动项。
10. 自动测试通过，当前机器人工验收清单全部通过。
11. 应用不上传任务数据，不写入 Codex 数据文件，日志不包含任务内容。

## 12. 实施顺序

1. 建立 Windows 解决方案和 CI 骨架。
2. 移植核心数据模型、SQLite 查询、rollout 解析和偏好设置。
3. 移植现有 CoreChecks 为 xUnit，并补齐 Windows 路径与新 schema 测试。
4. 实现 WPF 浮窗、单实例和开机启动。
5. 实现深链、UIA 快照、唯一匹配和分组消歧。
6. 实现受控滚动状态机并在当前机器调校。
7. 完成人工验收、Inno Setup 打包和 GitHub Release 流程。
