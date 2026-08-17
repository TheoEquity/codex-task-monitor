# `2026-08-17-windows-bark-notification` Windows Bark 自动通知——方法级实现稿

## 范围与约束

- 必须实现：Windows 11 版检测到任务完成或中止后，自动发送 Bark 通知。
- 正常完成：标题为任务标题，正文为项目名。
- 中止：标题为任务标题，正文为 `项目名 · 已中止`。
- 完成边界：当前电脑完成一次性 Bark 接入后，程序可自动通知、去重、退避重试并跨重启保留状态。
- 后续确认覆盖原设计中的设置窗口：不新增 Bark 菜单、设置窗口、启用开关或测试按钮。
- Bark 地址通过一次性本机脚本从 Windows 剪贴板读取、测试并使用 DPAPI CurrentUser 加密。
- 不在代码、Git、文档、日志或命令输出中保存、显示真实 Bark 地址。
- 不修改 macOS 版本。
- 不增加铃声、图标、通知级别、自托管管理或其他通知渠道。
- 投递语义为至少一次；HTTP 成功但去重状态持久化失败时，重启后可能极少数重复一次。

## 方法

| 方法 | 位置 | 状态 | 当前职责 | 计划职责或变化 | 约定或调用方影响 |
|---|---|---|---|---|---|
| `CodexDataPaths.ForHome` | `windows/CodexTaskMonitor.Core/Data/CodexDataPaths.cs` | 现有，需要修改 | 生成数据库、偏好和日志路径 | 增加 `BarkStatePath`、`BarkSecretPath` | 文件仍位于 `%LOCALAPPDATA%\CodexTaskMonitor` |
| `TaskMonitor.ToMonitorItem` | `windows/CodexTaskMonitor.Core/Monitoring/TaskMonitor.cs` | 现有，需要修改 | 将最新生命周期事件转换为监视项 | 将 `Completed`、`Aborted` 映射为独立终态原因 | 不改变现有状态过滤与排序 |
| `MonitorViewModel` 构造方法 | `windows/CodexTaskMonitor.Windows/ViewModels/MonitorViewModel.cs` | 现有，需要修改 | 接收扫描、偏好和激活依赖 | 接收 `ITaskCompletionNotifier`，订阅脱敏警告 | 现有调用方 `App.OnStartup` 同步更新 |
| `MonitorViewModel.RefreshCoreAsync` | 同上 | 现有，需要修改 | 扫描并提交最新任务列表 | 仅在当前 generation 提交成功后调用 `Observe` | 过期或取消刷新不产生通知 |
| `MonitorViewModel.DismissAsync` | 同上 | 现有，需要修改 | 保存“已处理”状态并刷新 | 保存成功后立即调用 `Remove(item.Id)` | 防止扫描失败时仍继续重试 |
| `MonitorViewModel.SetBarkError` | 同上 | 计划新增 | 当前不存在独立 Bark 错误入口 | 保存低优先级 Bark 警告并刷新错误属性 | 优先级低于操作错误和扫描错误 |
| `MonitorViewModel.DisposeCoreAsync` | 同上 | 现有，需要修改 | 等待扫描并释放资源 | 额外等待通知器安全退出 | 不遗留后台 HTTP 请求 |
| `App.OnStartup` | `windows/CodexTaskMonitor.Windows/App.xaml.cs` | 现有，需要修改 | 组装 Windows 服务和主窗口 | 创建 Bark store、客户端和通知器并注入 ViewModel | 不创建 Bark 设置窗口 |
| `ILocalDiagnostics.WriteAsync` | `windows/CodexTaskMonitor.Windows/Services/LocalDiagnostics.cs` | 现有，原样复用 | 写入固定字段的本地安全诊断 | 记录 Bark 成功、失败和秘密读取失败类别 | 不传入密钥、URL、任务标题或项目名 |
| `Invoke-BarkProvisioning` | `windows/Scripts/provision_bark.ps1` | 计划新增 | 当前无一次性 Bark 接入能力 | 从剪贴板读取 URL，完成校验、测试、加密和状态初始化 | 程序运行时拒绝改配；不回显 URL |
| `ConvertTo-BarkEndpoint` | 同上 | 计划新增 | 当前无 Bark 示例 URL 解析 | 识别并去掉固定示例路径 | 仅接受绝对 HTTPS URL |
| `Send-BarkTest` | 同上 | 计划新增 | 当前无 Bark 测试动作 | POST 固定测试通知并校验响应 | 失败时不修改本机配置 |
| `Protect-BarkSecret` | 同上 | 计划新增 | 当前无 Bark 密钥保护 | 用 DPAPI CurrentUser 加密配置 ID 和端点 | 输出为二进制密文文件 |
| `Write-BarkState` | 同上 | 计划新增 | 当前无 Bark 状态初始化 | 原子写入启用边界、配置 ID 和空去重集合 | 不包含 Bark URL |
| `BarkSecretStore.LoadAsync` | `windows/CodexTaskMonitor.Windows/Notifications/BarkSecretStore.cs` | 计划新增 | 当前无秘密读取能力 | 读取并解密 `bark-secret.dat` | 返回 `BarkSecret?`；失败不返回明文 |
| `BarkStateStore.LoadAsync` | `windows/CodexTaskMonitor.Windows/Notifications/BarkStateStore.cs` | 计划新增 | 当前无通知状态读取能力 | 原子读取启用边界、配置 ID 和已通知集合 | 缺失时返回禁用状态 |
| `BarkStateStore.MarkNotifiedAsync` | 同上 | 计划新增 | 当前无通知去重持久化 | 在配置 ID 仍匹配时加入精确任务 ID | 返回是否成功提交当前 generation |
| `BarkNotificationClient.SendAsync` | `windows/CodexTaskMonitor.Windows/Notifications/BarkNotificationClient.cs` | 计划新增 | 当前无 Bark HTTP 客户端 | 发送 JSON POST 并分类处理失败 | 不记录请求或响应正文 |
| `TaskCompletionNotifier.Observe` | `windows/CodexTaskMonitor.Windows/Notifications/TaskCompletionNotifier.cs` | 计划新增 | 当前无任务通知协调 | 接收最新已提交任务快照并唤醒后台循环 | 同步返回，不阻塞 UI |
| `TaskCompletionNotifier.Remove` | 同上 | 计划新增 | 当前无待发送任务取消 | 移除指定任务的发送与重试状态 | 由“已处理”动作调用 |
| `TaskCompletionNotifier.RunAsync` | 同上 | 计划新增 | 当前无后台通知循环 | 串行筛选、发送、去重并控制退避 | 最大退避间隔 15 分钟 |
| `TaskCompletionNotifier.DisposeAsync` | 同上 | 计划新增 | 当前无通知资源释放 | 取消并等待后台循环与 HTTP 请求 | 多次调用保持幂等 |

新增能力不能复用现有 `MonitorPreferencesStore`：该 store 由 `MonitorViewModel` 持有缓存并负责现有面板偏好，通知后台并发写入会产生陈旧覆盖风险。因此 Bark 使用独立状态文件和独立原子 store。

## 技术与共享资源

| 技术或资源 | 位置 | 状态 | 当前用途 | 计划用途或变化 |
|---|---|---|---|---|
| `TaskTerminalKind` | `windows/CodexTaskMonitor.Core/Monitoring/TaskTerminalKind.cs` | 计划新增 | 当前终态被合并为 Waiting | 定义 `Completed`、`Aborted` |
| `MonitorItem` | `windows/CodexTaskMonitor.Core/Monitoring/MonitorItem.cs` | 现有，需要修改 | 表示面板任务 | 增加可空 `TerminalKind` |
| `BarkState` | `windows/CodexTaskMonitor.Windows/Notifications/BarkState.cs` | 计划新增 | 当前不存在 | 保存 `Enabled`、`ConfigurationId`、`EnabledAt`、`NotifiedItemIds` |
| `BarkSecret` | 同上 | 计划新增 | 当前不存在 | 保存解密后的配置 ID 和端点，仅在内存短暂存在 |
| `BarkNotification` | 同上 | 计划新增 | 当前不存在 | 保存标题、正文和固定分组 |
| `bark-state.json` | `%LOCALAPPDATA%\CodexTaskMonitor\bark-state.json` | 计划新增 | 当前不存在 | 保存非敏感通知状态 |
| `bark-secret.dat` | `%LOCALAPPDATA%\CodexTaskMonitor\bark-secret.dat` | 计划新增 | 当前不存在 | 保存 DPAPI 二进制密文 |
| Windows DPAPI CurrentUser | Windows 系统能力 | 计划新增 | 项目当前未使用 | 保护 Bark 端点，只允许当前用户解密 |
| `HttpClient` | `BarkNotificationClient` 内部 | 计划新增 | 项目当前无 HTTP 客户端 | 复用连接发送 Bark POST |
| `TimeProvider` | 现有依赖模式 | 现有，原样复用 | 控制轮询和时间 | 提供启用时间与退避时钟 |
| `LocalDiagnostics.SafeCategories` | `LocalDiagnostics.cs` | 现有，需要修改 | 限制允许写入的诊断类别 | 增加 Bark 成功、发送失败、状态失败和秘密失败类别 |
| `README.md` 隐私说明 | `README.md` | 现有，需要修改 | 声明应用完全本地 | 改为“未接入 Bark 时完全本地；接入后发送标题、项目名和终态” |
| `MainWindow.xaml` | Windows 主窗口 | 现有，原样复用 | 菜单和任务列表 | 不增加 Bark 菜单或设置入口 |

## 实现流程

### 步骤一：一次性测试并保护 Bark 地址

```text
Invoke-BarkProvisioning():

if CodexTaskMonitor 进程正在运行:
    返回通用错误“请先退出 Codex Task Monitor”

clipboardText = Windows 剪贴板文本

if clipboardText 为空:
    返回通用错误“剪贴板中没有 Bark 地址”

endpoint = ConvertTo-BarkEndpoint(clipboardText)

if endpoint 不是绝对 HTTPS URL:
    返回通用错误“Bark 地址格式无效”

if endpoint 包含用户名、密码、fragment 或缺少设备密钥路径:
    返回通用错误“Bark 地址格式无效”

if 路径尾部严格匹配固定 Bark 示例路径:
    去掉固定示例路径
else if 密钥之后仍存在未知路径:
    返回通用错误“无法识别 Bark 示例地址”

testResult = Send-BarkTest(
    endpoint,
    title = "Codex Task Monitor",
    body = "Bark 连接成功",
    group = "Codex Task Monitor"
)

if testResult 失败:
    输出脱敏错误类别
    return failure

configurationId = 新 GUID
enabledAt = 当前 UTC 时间
protectedSecret = Protect-BarkSecret(
    configurationId,
    endpoint,
    DataProtectionScope.CurrentUser
)

Write-BarkState(
    Enabled = true,
    ConfigurationId = configurationId,
    EnabledAt = enabledAt,
    NotifiedItemIds = []
)

原子替换 bark-secret.dat
原子替换 bark-state.json
清空持有明文 URL 的临时变量
输出“Bark 测试成功并已安全保存”
return success
```

脚本文件只包含通用逻辑。真实地址只从剪贴板进入内存，不作为参数、命令行文本、日志字段或仓库内容出现。

### 步骤二：启动通知能力并保留任务终态

```text
App.OnStartup():
    paths = CodexDataPaths.ForHome(...)
    barkStateStore = new BarkStateStore(paths.BarkStatePath)
    barkSecretStore = new BarkSecretStore(paths.BarkSecretPath)
    barkClient = new BarkNotificationClient(HttpClient, timeout)
    notifier = new TaskCompletionNotifier(
        barkStateStore,
        barkSecretStore,
        barkClient,
        diagnostics,
        TimeProvider.System
    )

    model = new MonitorViewModel(
        原有依赖,
        notifier
    )

MonitorViewModel 构造:
    notifier.WarningChanged += SetBarkError

TaskMonitor.ToMonitorItem(pair, options):
    state = TaskStateResolver.Resolve(...)

    if state is null:
        return null

    terminalKind =
        if pair.Event.Kind == LifecycleKind.Completed:
            TaskTerminalKind.Completed
        else if pair.Event.Kind == LifecycleKind.Aborted:
            TaskTerminalKind.Aborted
        else:
            null

    return MonitorItem(原有字段, State = state, TerminalKind = terminalKind)
```

`TaskStateResolver`、rollout 解析和面板颜色保持不变。终态只供通知层区分内容。

### 步骤三：只让当前扫描结果触发通知

```text
MonitorViewModel.RefreshCoreAsync(refresh, token):
    result = await monitor.ScanAsync(scanOptions, token)
    insertedId = null

    applied = TryCommit(refresh, Items, () =>:
        insertedId = UpdateItems(result.Items)
        更新轮询延迟
        更新扫描错误
    )

    if applied == false:
        return

    if insertedId 不为空:
        ItemInserted 事件通知主窗口滚动

    notifier.Observe(result.Items)

TaskCompletionNotifier.Observe(items):
    snapshot = items 的独立只读副本

    lock state:
        if disposing:
            return

        latestSnapshot = snapshot
        snapshotVersion += 1

    signal.Release()
    return

MonitorViewModel.DismissAsync(item, token):
    if item 不能已处理:
        return

    await MutatePreferencesAsync(current => current.Dismiss(item.Id), token)
    notifier.Remove(item.Id)
    await RefreshAsync(token)
```

### 步骤四：筛选、发送并持久化去重

```text
TaskCompletionNotifier.RunAsync(lifetimeToken):
    while lifetimeToken 未取消:
        等待新快照信号或最近一次退避到期
        state = await barkStateStore.LoadAsync(lifetimeToken)
        secret = await barkSecretStore.LoadAsync(lifetimeToken)

        if state.Enabled == false:
            清空重试状态
            WarningChanged(null)
            continue

        if secret 为空或 secret.ConfigurationId != state.ConfigurationId:
            清空重试状态
            WarningChanged("Bark 配置不可用")
            diagnostics.WriteAsync("bark-secret-failure", duration, 1)
            continue

        visibleIds = latestSnapshot 中所有 item.Id
        删除所有不在 visibleIds 中的重试记录

        for item in latestSnapshot:
            if item.TerminalKind 为空:
                continue

            if item.EventDate < state.EnabledAt:
                continue

            if state.NotifiedItemIds 包含 item.Id:
                continue

            if item.Id 正在发送或尚未到重试时间:
                continue

            notification =
                if item.TerminalKind == Completed:
                    title = item.Title
                    body = item.ProjectName
                else:
                    title = item.Title
                    body = item.ProjectName + " · 已中止"

            sendResult = await barkClient.SendAsync(
                secret.Endpoint,
                notification,
                lifetimeToken
            )

            if sendResult 成功:
                在当前 configurationId 的进程内集合中加入 item.Id
                删除重试记录

                try:
                    committed = await barkStateStore.MarkNotifiedAsync(
                        expectedConfigurationId = state.ConfigurationId,
                        itemId = item.Id,
                        lifetimeToken
                    )

                    if committed:
                        state = state 加入 item.Id
                        WarningChanged(null)
                        diagnostics.WriteAsync("bark-send-ok", duration, 1)
                catch persistenceError:
                    WarningChanged("Bark 通知状态暂时无法保存")
                    diagnostics.WriteAsync("bark-state-failure", duration, 1)
                    // 当前进程内集合继续阻止重复；重启后允许极少数重复一次

                continue
```

Bark 请求负载只能包含标题、项目名、中止标记和固定分组。不得包含线程 ID、turn ID、完整工作目录或 Codex 数据内容。

### 步骤五：失败退避、状态取消与安全退出

```text
if barkClient.SendAsync 失败:
    attempt = retry.Attempt + 1
    delay = 按 attempt 递增
    delay = min(delay, 15 分钟)

    retries[item.Id] = {
        Attempt = attempt,
        NextAttemptAt = 当前 UTC 时间 + delay
    }

    WarningChanged("Bark 通知发送失败，将自动重试")
    diagnostics.WriteAsync("bark-send-failure", duration, 1)
    continue

BarkNotificationClient.SendAsync(endpoint, notification, token):
    使用共享 HttpClient POST JSON
    校验 HTTP 成功状态
    校验 Bark 响应表示成功

    if token 被取消:
        向上抛出取消，不登记普通发送失败

    if DNS、连接、超时、HTTP 或 Bark 响应失败:
        返回仅包含错误类别的失败结果
        不返回 endpoint、请求体或响应正文

MonitorViewModel.ErrorMessage:
    return actionErrorMessage
        ?? scanErrorMessage
        ?? barkErrorMessage

TaskCompletionNotifier.Remove(itemId):
    lock state:
        从重试集合和最新快照中排除 itemId
    signal.Release()

MonitorViewModel.DisposeCoreAsync(...):
    notifier.WarningChanged -= SetBarkError
    await notifier.DisposeAsync()
    等待现有刷新和偏好写入
    释放原有资源

TaskCompletionNotifier.DisposeAsync():
    标记 disposing
    lifetime.Cancel()
    signal.Release()
    await 后台循环退出
    await BarkNotificationClient.DisposeAsync()
    释放同步与取消资源
```
