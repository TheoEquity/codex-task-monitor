# Windows Fork 任务监控实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目标：** 让用户在 Codex 左侧栏可见的 Fork 任务像普通任务一样进入 Windows 监视面板，同时继续排除内部自动子代理任务。

**架构：** 在只读 SQLite 边界使用显式来源白名单：普通用户任务与 `subagent/vscode` 用户可见 Fork 可以进入统一的 `ThreadRecord` 流水线，其他来源安全排除。领域模型不增加 Fork 类型；真实 SQLite、真实 JSONL rollout、`TaskMonitor` 和 `MonitorViewModel` 的回归测试证明 Fork 复用现有状态、排序、打开和精确“已处理”行为。

**技术栈：** .NET 8、C# 12、Microsoft.Data.Sqlite、xUnit、WPF、PowerShell、Inno Setup 7.1.0。

## 全局约束

- 只管理用户主动 Fork 且出现在 Codex 左侧栏的任务；内部自动子代理任务不得进入面板。
- SQLite 必须保持只读，不能创建或修改 Codex 数据库。
- Fork 与普通任务都转换为相同的 `ThreadRecord`、`MonitorItem` 和 `MonitorItemViewModel`，不新增 Fork 标签、按钮、分组或父子树。
- 状态事件仍只识别 `task_started`、`task_complete` 和 `turn_aborted`。
- 面板身份和“已处理”键始终使用区分大小写的精确 `threadID:turnID`。
- 同名任务允许同时显示；侧栏定位歧义时继续拒绝猜测和点击。
- 不记录任务标题、线程 ID、父线程 ID、提示词、工作目录或 rollout 内容。
- 不改变最多 6 行、2 秒轮询、窗口尺寸、排序规则和 Swift/macOS 实现。
- 所有生产变更必须由先失败的回归测试驱动；若下游回归在现有实现上已通过，只提交测试，不制造额外生产改动。

---

## 文件结构

- 修改 `windows/CodexTaskMonitor.Core/Data/SqliteThreadStore.cs`：把可管理任务来源写成显式白名单。
- 修改 `windows/CodexTaskMonitor.Tests/Data/SqliteThreadStoreTests.cs`：覆盖普通任务、可见 Fork、内部子代理和未知来源。
- 修改 `windows/CodexTaskMonitor.Tests/Fixtures/CodexFixture.cs`：允许每条测试线程使用独立 rollout 路径。
- 新建 `windows/CodexTaskMonitor.Tests/Monitoring/ForkedTaskMonitoringTests.cs`：以真实 SQLite 和真实 JSONL 验证 Fork 的端到端状态与身份隔离。
- 修改 `windows/CodexTaskMonitor.Tests/ViewModels/MonitorViewModelTests.cs`：验证同名父任务与 Fork 被投影为两个普通面板项，并使用各自的打开与“已处理”身份。
- 修改 `docs/windows-manual-test.md`：仅在重新生成安装包后记录新的提交、安装包哈希、大小和自动验收事实；18 项真实 UI/重启人工检查保持未勾选。

### 任务 1：收紧 SQLite 可见任务来源规则

**文件：**
- 修改：`windows/CodexTaskMonitor.Tests/Data/SqliteThreadStoreTests.cs:9-20`
- 修改：`windows/CodexTaskMonitor.Core/Data/SqliteThreadStore.cs:25-37`

**接口：**
- 使用：`SqliteThreadStore.ReadThreadsAsync(DateTimeOffset, CancellationToken)`。
- 保持输出：`Task<IReadOnlyList<ThreadRecord>>`，不更改公共类型。
- 产生规则：`COALESCE(thread_source, 'user') = 'user'` 或 `thread_source = 'subagent' AND source = 'vscode'`。

- [ ] **步骤 1：编写失败的来源白名单测试**

将首个测试改名为 `ReadThreads_ReturnsUserAndVisibleForkThreadsOnly`，并使用以下完整测试体：

```csharp
[Fact]
public async Task ReadThreads_ReturnsUserAndVisibleForkThreadsOnly()
{
    await using var fixture = await CodexFixture.CreateAsync();
    await fixture.InsertThreadAsync("user", "User", "user", "vscode", archived: false, preview: "hello");
    await fixture.InsertThreadAsync("visible-fork", "Fork", "subagent", "vscode", archived: false, preview: "hello");
    await fixture.InsertThreadAsync("internal", "Internal", "subagent", "{\"subagent\":{}}", archived: false, preview: "hello");
    await fixture.InsertThreadAsync("unknown", "Unknown", "automation", "vscode", archived: false, preview: "hello");
    await fixture.InsertThreadAsync("archived", "Archived", "user", "vscode", archived: true, preview: "hello");
    await fixture.InsertThreadAsync("empty", "Empty", "user", "vscode", archived: false, preview: "");

    var records = await new SqliteThreadStore(fixture.DatabasePath)
        .ReadThreadsAsync(DateTimeOffset.UnixEpoch, default);

    Assert.Equal(["user", "visible-fork"], records.Select(record => record.Id).Order().ToArray());
}
```

- [ ] **步骤 2：运行测试并确认红灯**

运行：

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~SqliteThreadStoreTests.ReadThreads_ReturnsUserAndVisibleForkThreadsOnly"
```

预期：FAIL；实际结果额外包含 `unknown`，证明旧条件会放行未知的非 subagent 来源。

- [ ] **步骤 3：实现最小来源白名单**

把 `SqliteThreadStore.ReadThreadsAsync` 的来源条件替换为：

```sql
AND (
      COALESCE(t.thread_source, 'user') = 'user'
      OR (t.thread_source = 'subagent' AND t.source = 'vscode')
    )
```

保留归档、空预览、更新时间、排序、只读连接和 schema 校验的其余代码不变。

- [ ] **步骤 4：运行数据层聚焦测试并确认绿灯**

运行：

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~SqliteThreadStoreTests"
```

预期：所有 `SqliteThreadStoreTests` PASS；缺列、时间下限、分组和缺失数据库回归不变。

- [ ] **步骤 5：提交来源规则**

```powershell
git add windows/CodexTaskMonitor.Core/Data/SqliteThreadStore.cs windows/CodexTaskMonitor.Tests/Data/SqliteThreadStoreTests.cs
git diff --cached --check
git -c user.name='Codex Agent' -c user.email='codex-agent@localhost' commit -m "fix: include only visible Codex fork tasks"
```

### 任务 2：增加真实 SQLite 到监控项的 Fork 回归

**文件：**
- 修改：`windows/CodexTaskMonitor.Tests/Fixtures/CodexFixture.cs:31-64`
- 新建：`windows/CodexTaskMonitor.Tests/Monitoring/ForkedTaskMonitoringTests.cs`

**接口：**
- 修改测试夹具：`InsertThreadAsync(..., string? rolloutPath = null)`；未传参数时继续使用 `CodexFixture.RolloutPath`。
- 使用生产接口：`new TaskMonitor(new SqliteThreadStore(databasePath)).ScanAsync(MonitorScanOptions, CancellationToken)`。
- 产生验证：Fork 对三种已批准事件分别生成 `Running`、`Waiting`、`Waiting`，并通过 `threadID:turnID` 与父任务隔离。

- [ ] **步骤 1：让测试夹具支持独立 rollout 文件**

在 `CodexFixture.InsertThreadAsync` 的最后增加可选参数：

```csharp
string? sectionId = null,
string? rolloutPath = null)
```

并把 rollout 参数绑定改为：

```csharp
command.Parameters.AddWithValue("$rollout", rolloutPath ?? RolloutPath);
```

- [ ] **步骤 2：编写 Fork 生命周期端到端回归**

新建 `ForkedTaskMonitoringTests.cs`，内容为：

```csharp
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Tests.Fixtures;

namespace CodexTaskMonitor.Tests.Monitoring;

public sealed class ForkedTaskMonitoringTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.FromUnixTimeSeconds(100);

    [Theory]
    [InlineData("task_started", TaskState.Running)]
    [InlineData("task_complete", TaskState.Waiting)]
    [InlineData("turn_aborted", TaskState.Waiting)]
    public async Task VisibleFork_UsesNormalLifecycleState(string eventType, TaskState expectedState)
    {
        await using var fixture = await CodexFixture.CreateAsync();
        var rollout = await WriteRolloutAsync(fixture, "fork", LifecycleLine(eventType, "fork-turn"));
        await fixture.InsertThreadAsync(
            "fork-thread", "Shared title", "subagent", "vscode",
            archived: false, preview: "visible", rolloutPath: rollout);
        var monitor = new TaskMonitor(new SqliteThreadStore(fixture.DatabasePath));

        var result = await monitor.ScanAsync(Options(), default);

        var item = Assert.Single(result.Items);
        Assert.Equal("fork-thread", item.ThreadId);
        Assert.Equal("fork-turn", item.TurnId);
        Assert.Equal(expectedState, item.State);
    }

    [Fact]
    public async Task DismissingParent_DoesNotHideForkWithSameTitleAndTurnId()
    {
        await using var fixture = await CodexFixture.CreateAsync();
        var parentRollout = await WriteRolloutAsync(fixture, "parent", LifecycleLine("task_complete", "shared-turn"));
        var forkRollout = await WriteRolloutAsync(fixture, "fork", LifecycleLine("task_complete", "shared-turn"));
        await fixture.InsertThreadAsync(
            "parent-thread", "Shared title", "user", "vscode",
            archived: false, preview: "visible", rolloutPath: parentRollout);
        await fixture.InsertThreadAsync(
            "fork-thread", "Shared title", "subagent", "vscode",
            archived: false, preview: "visible", rolloutPath: forkRollout);
        var monitor = new TaskMonitor(new SqliteThreadStore(fixture.DatabasePath));
        var options = Options(new HashSet<string>(StringComparer.Ordinal) { "parent-thread:shared-turn" });

        var result = await monitor.ScanAsync(options, default);

        var item = Assert.Single(result.Items);
        Assert.Equal("fork-thread:shared-turn", item.Id);
        Assert.Equal("Shared title", item.Title);
    }

    private static MonitorScanOptions Options(IReadOnlySet<string>? dismissedItems = null) =>
        new(
            Baseline,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            dismissedItems ?? new HashSet<string>(StringComparer.Ordinal));

    private static string LifecycleLine(string eventType, string turnId) =>
        eventType == "task_started"
            ? "{\"type\":\"event_msg\",\"payload\":{\"type\":\"" + eventType +
              "\",\"turn_id\":\"" + turnId + "\",\"started_at\":101}}\n"
            : "{\"type\":\"event_msg\",\"payload\":{\"type\":\"" + eventType +
              "\",\"turn_id\":\"" + turnId + "\",\"started_at\":101,\"completed_at\":102}}\n";

    private static async Task<string> WriteRolloutAsync(CodexFixture fixture, string name, string contents)
    {
        var root = Path.GetDirectoryName(fixture.DatabasePath)!;
        var path = Path.Combine(root, $"{name}.jsonl");
        await File.WriteAllTextAsync(path, contents);
        return path;
    }
}
```

- [ ] **步骤 3：运行端到端测试并定位真实边界**

运行：

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~ForkedTaskMonitoringTests"
```

预期：4/4 PASS，证明显式白名单后的 Fork 已穿过真实 SQLite、rollout 解析、状态解析和精确已处理过滤。如果出现失败，停止扩大来源规则，使用 `systematic-debugging` 在失败调用栈所指的第一个生产边界修复，并保留本步骤测试作为红灯证据。

- [ ] **步骤 4：运行数据层和监控层组合回归**

运行：

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~SqliteThreadStoreTests|FullyQualifiedName~ForkedTaskMonitoringTests|FullyQualifiedName~TaskMonitorTests|FullyQualifiedName~TaskStateResolverTests|FullyQualifiedName~RolloutParserTests"
```

预期：全部 PASS；内部子代理仍被排除，三种事件映射、缓存、排序和精确身份均不回归。

- [ ] **步骤 5：提交端到端回归**

```powershell
git add windows/CodexTaskMonitor.Tests/Fixtures/CodexFixture.cs windows/CodexTaskMonitor.Tests/Monitoring/ForkedTaskMonitoringTests.cs
git diff --cached --check
git -c user.name='Codex Agent' -c user.email='codex-agent@localhost' commit -m "test: cover visible fork task monitoring"
```

### 任务 3：验证面板把父任务与 Fork 当作普通独立项

**文件：**
- 修改：`windows/CodexTaskMonitor.Tests/ViewModels/MonitorViewModelTests.cs:8-52`
- 修改测试替身：`windows/CodexTaskMonitor.Tests/ViewModels/MonitorViewModelTests.cs` 中的 `FakeActivation`

**接口：**
- 使用：`MonitorViewModel.StartAsync(bool, CancellationToken)`、`OpenAsync(MonitorItemViewModel, CancellationToken)` 和 `DismissAsync(MonitorItemViewModel, CancellationToken)`。
- 测试替身产生：`FakeActivation.LastItem` 记录最近一次打开的 `MonitorItem`。
- 不修改生产 ViewModel 接口。

- [ ] **步骤 1：让打开服务测试替身记录精确项目**

把 `FakeActivation` 改为：

```csharp
private sealed class FakeActivation : IThreadActivationService
{
    public Exception? Exception { get; init; }

    public MonitorItem? LastItem { get; private set; }

    public Task<string?> ActivateAsync(MonitorItem item, CancellationToken token)
    {
        LastItem = item;
        return Exception is null
            ? Task.FromResult<string?>(null)
            : Task.FromException<string?>(Exception);
    }
}
```

- [ ] **步骤 2：编写同名父任务和 Fork 的面板测试**

在 `MonitorViewModelTests` 增加：

```csharp
[Fact]
public async Task ParentAndForkWithSameTitle_AreIndependentNormalPanelItems()
{
    var parent = new MonitorItem(
        "parent-thread", "shared-turn", "Shared title", @"C:\work", "work",
        DateTimeOffset.UtcNow, TaskState.Waiting);
    var fork = new MonitorItem(
        "fork-thread", "shared-turn", "Shared title", @"C:\work", "work",
        DateTimeOffset.UtcNow.AddSeconds(1), TaskState.Waiting);
    var monitor = new FakeTaskMonitor(Set(), [fork, parent]);
    var preferences = new FakePreferencesStore(
        new MonitorPreferences(DateTimeOffset.UtcNow.AddHours(-1), [], [], [], null, null, true));
    var activation = new FakeActivation();
    var viewModel = new MonitorViewModel(
        monitor, preferences, activation, new FakeStartup(), new FakeLaunchTime(null), TimeProvider.System);
    await viewModel.StartAsync(startPollingLoop: false, CancellationToken.None);

    Assert.Equal(
        ["fork-thread:shared-turn", "parent-thread:shared-turn"],
        viewModel.Items.Select(item => item.Id).ToArray());
    Assert.All(viewModel.Items, item =>
    {
        Assert.Equal("Shared title", item.Title);
        Assert.True(item.CanDismiss);
    });

    var forkItem = viewModel.Items.Single(item => item.ThreadId == "fork-thread");
    await viewModel.OpenAsync(forkItem, CancellationToken.None);
    await viewModel.DismissAsync(forkItem, CancellationToken.None);

    Assert.Equal("fork-thread:shared-turn", activation.LastItem!.Id);
    Assert.Contains("fork-thread:shared-turn", preferences.Value.DismissedItemIds);
    Assert.DoesNotContain("parent-thread:shared-turn", preferences.Value.DismissedItemIds);
}
```

- [ ] **步骤 3：运行 ViewModel 聚焦测试**

运行：

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~MonitorViewModelTests"
```

预期：全部 PASS；新测试证明相同标题和 turn ID 不会合并两个线程，打开和“已处理”都命中 Fork 的精确身份。

- [ ] **步骤 4：提交面板回归**

```powershell
git add windows/CodexTaskMonitor.Tests/ViewModels/MonitorViewModelTests.cs
git diff --cached --check
git -c user.name='Codex Agent' -c user.email='codex-agent@localhost' commit -m "test: treat visible forks as normal panel items"
```

### 任务 4：完整验证、重新打包并升级本机安装

**文件：**
- 修改：`docs/windows-manual-test.md:45-70`
- 生成但保持忽略：`windows/publish/win-x64/CodexTaskMonitor.exe`
- 生成但保持忽略：`windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe`

**接口：**
- 使用：`windows/Scripts/verify_windows_environment.ps1` 和 `windows/Scripts/verify_windows_packaging.ps1`。
- 产生：一个 self-contained 单文件应用、一个未签名 per-user 安装包和与该字节文件绑定的 SHA-256。

- [ ] **步骤 1：运行完整 Release 验证**

```powershell
dotnet test windows\CodexTaskMonitor.sln -c Release
dotnet build windows\CodexTaskMonitor.sln -c Release -warnaserror
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_environment.ps1
git diff --check
```

预期：完整测试全部 PASS；构建 0 warnings/0 errors；环境预检固定 9 个布尔值全部为 `true`；diff check 无输出。

- [ ] **步骤 2：生成一次应用和一次安装包**

```powershell
dotnet publish windows\CodexTaskMonitor.Windows\CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows\publish\win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows\Installer\CodexTaskMonitor.iss
$installer = 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $installer).Length
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_packaging.ps1 -RequireOutputs -ExpectedInstallerSha256 $hash
"SHA256=$hash SIZE=$size"
```

预期：publish 目录只有 `CodexTaskMonitor.exe`；ISCC 7.1.0 成功；应用和安装包均为 PE/MZ 且 `NotSigned`；包装验证通过并输出本次安装包的精确哈希与大小。

- [ ] **步骤 3：静默升级并做非交互存活检查**

```powershell
$installer = (Resolve-Path 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe').Path
$process = Start-Process -FilePath $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installer exit code: $($process.ExitCode)" }
$app = Join-Path $env:LOCALAPPDATA 'Programs\Codex Task Monitor\CodexTaskMonitor.exe'
Start-Process -FilePath $app
Start-Sleep -Seconds 5
if (@(Get-Process -Name CodexTaskMonitor -ErrorAction SilentlyContinue).Count -ne 1) { throw 'Expected one running monitor process.' }
Start-Sleep -Seconds 15
if (@(Get-Process -Name CodexTaskMonitor -ErrorAction SilentlyContinue).Count -ne 1) { throw 'Monitor did not remain alive for 20 seconds.' }
```

预期：安装器退出码 0；安装目录和 HKCU 启动项由现有安装器契约保持；监视器在 5 秒和 20 秒时均只有一个运行进程。此步骤不点击、不滚动、不向 Codex UI 输入。

- [ ] **步骤 4：更新自动证据但不虚假完成人工项**

用 `apply_patch` 将 `docs/windows-manual-test.md` 中旧的测试提交、安装包 SHA-256、字节大小和自动安装存活证据替换为步骤 1–3 的精确结果。现有 18 项真实 UI/点击/重启/login 检查继续保持 `[ ]`，并新增第 19 条未勾选的 Fork 验收项：

```markdown
- [ ] 在 Codex 左侧栏主动 Fork 一个用户任务，确认 Fork 与普通新建任务一样出现在监视面板；内部子代理任务不出现。
```

- [ ] **步骤 5：提交证据并做最终一致性验证**

```powershell
git add docs/windows-manual-test.md
git diff --cached --check
git -c user.name='Codex Agent' -c user.email='codex-agent@localhost' commit -m "docs: record visible fork monitoring build"
$expected = (Select-String -LiteralPath docs\windows-manual-test.md -Pattern '[A-F0-9]{64}').Matches.Value | Select-Object -First 1
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_packaging.ps1 -RequireOutputs -ExpectedInstallerSha256 $expected
dotnet test windows\CodexTaskMonitor.sln -c Release --no-restore
git show --check --oneline HEAD
git status --short
```

预期：文档哈希与当前未改动安装包一致；完整测试再次 PASS；`git show --check` 无错误；tracked 工作树干净。最终报告明确区分自动验证与仍需用户完成的真实 Fork UI 验收。
