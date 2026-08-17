# Windows 中止事件时间戳兼容实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 兼容 Codex 实际产生的、没有 `payload.completed_at` 但带有效外层 ISO-8601 `timestamp` 的 `turn_aborted` 事件，使普通任务和用户可见 Fork 不再令整个 Windows 监视面板空白。

**Architecture:** 在 `RolloutParser.ApplyLine` 的生命周期边界增加私有终止时间解析方法。`task_complete` 继续严格要求数字型 `payload.completed_at`；`turn_aborted` 仅在该字段缺失时读取外层 ISO-8601 `timestamp`。`TaskMonitor`、SQLite 来源白名单、领域模型和界面均保持不变。

**Tech Stack:** .NET 8、C# 12、System.Text.Json、xUnit、Microsoft.Data.Sqlite、PowerShell、Inno Setup 7.1.0。

## Global Constraints

- 只兼容实机确认的 `turn_aborted` 外层时间戳结构，不把它扩展成所有事件的通用回退。
- `task_started` 与 `task_complete` 的字段要求保持不变。
- `turn_aborted` 已包含 `payload.completed_at` 时必须优先使用它；字段存在但类型错误时不得回退。
- 缺少有效终止时间时继续报告 `CodexDataError.FormatChanged`。
- 不修改 TaskMonitor 错误传播、SQLite 来源白名单、任务身份、缓存、排序、轮询、WPF、侧栏自动化或 macOS。
- 内部自动子代理仍不进入面板。
- 测试和实机验证不得输出任务标题、ID、提示词、工作目录、rollout 路径或内容，只输出固定状态和计数。
- 所有生产改动必须先由失败测试证明。

---

## 文件结构

- 修改 `windows/CodexTaskMonitor.Tests/Monitoring/RolloutParserTests.cs`：协议兼容与严格边界测试。
- 修改 `windows/CodexTaskMonitor.Tests/Monitoring/ForkedTaskMonitoringTests.cs`：真实 SQLite + JSONL 端到端回归。
- 修改 `windows/CodexTaskMonitor.Core/Monitoring/RolloutParser.cs`：最小终止时间兼容逻辑。
- 修改 `docs/windows-manual-test.md`：重建安装包后的精确自动证据，人工项保持未勾选。

### Task 1: 严格兼容 `turn_aborted` 外层时间戳

**Files:**
- Modify: `windows/CodexTaskMonitor.Tests/Monitoring/RolloutParserTests.cs`
- Modify: `windows/CodexTaskMonitor.Tests/Monitoring/ForkedTaskMonitoringTests.cs`
- Modify: `windows/CodexTaskMonitor.Core/Monitoring/RolloutParser.cs:121-137`

**Interfaces:**
- Consumes: `RolloutParser.LatestAfter(LifecycleEvent?, ReadOnlySpan<byte>)`、`TaskMonitor.ScanAsync(MonitorScanOptions, CancellationToken)`。
- Produces: 现有 `LifecycleEvent`；新增私有 `TerminalAt(JsonElement, JsonElement, LifecycleKind) -> DateTimeOffset`，公共接口不变。

- [ ] **Step 1: 编写解析器 RED 与保护测试**

在 `RolloutParserTests` 增加：

```csharp
[Fact]
public void AbortedTurnWithoutCompletedAt_UsesRootTimestamp()
{
    var data = Encoding.UTF8.GetBytes(
        "{\"type\":\"event_msg\",\"timestamp\":\"2026-08-16T12:34:56.789Z\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"turn-a\",\"started_at\":101}}\n");

    var item = RolloutParser.LatestAfter(null, data);

    Assert.Equal(LifecycleKind.Aborted, item!.Kind);
    Assert.Equal(DateTimeOffset.Parse("2026-08-16T12:34:56.789Z"), item.CompletedAt);
}

[Fact]
public void AbortedTurnWithCompletedAt_PrefersPayloadTimestamp()
{
    var data = Encoding.UTF8.GetBytes(
        "{\"type\":\"event_msg\",\"timestamp\":\"2026-08-16T12:34:56.789Z\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"turn-a\",\"started_at\":101,\"completed_at\":102}}\n");

    var item = RolloutParser.LatestAfter(null, data);

    Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(102), item!.CompletedAt);
}

[Theory]
[InlineData("{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"turn-a\",\"started_at\":101}}\n")]
[InlineData("{\"type\":\"event_msg\",\"timestamp\":\"not-a-timestamp\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"turn-a\",\"started_at\":101}}\n")]
[InlineData("{\"type\":\"event_msg\",\"timestamp\":\"2026-08-16T12:34:56.789Z\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"turn-a\",\"started_at\":101,\"completed_at\":\"invalid\"}}\n")]
public void AbortedTurnWithoutValidTerminalTimestamp_ReportsFormatChange(string json)
{
    var error = Assert.Throws<CodexDataException>(
        () => RolloutParser.LatestAfter(null, Encoding.UTF8.GetBytes(json)));

    Assert.Equal(CodexDataError.FormatChanged, error.Error);
}

[Fact]
public void CompletedTurnWithoutCompletedAt_DoesNotUseRootTimestamp()
{
    var data = Encoding.UTF8.GetBytes(
        "{\"type\":\"event_msg\",\"timestamp\":\"2026-08-16T12:34:56.789Z\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-a\",\"started_at\":101}}\n");

    var error = Assert.Throws<CodexDataException>(() => RolloutParser.LatestAfter(null, data));

    Assert.Equal(CodexDataError.FormatChanged, error.Error);
}
```

- [ ] **Step 2: 编写真实 SQLite + rollout 端到端 RED**

在 `ForkedTaskMonitoringTests` 增加：

```csharp
[Fact]
public async Task AbortedVisibleForkWithRootTimestamp_DoesNotBlockNormalTasks()
{
    await using var fixture = await CodexFixture.CreateAsync();
    var userRollout = await WriteRolloutAsync(
        fixture, "user", LifecycleLine("task_started", "user-turn"));
    var forkRollout = await WriteRolloutAsync(
        fixture, "fork-aborted",
        "{\"type\":\"event_msg\",\"timestamp\":\"1970-01-01T00:01:42Z\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"fork-turn\",\"started_at\":101,\"reason\":\"interrupted\"}}\n");
    await fixture.InsertThreadAsync(
        "user-thread", "Normal", "user", "vscode",
        archived: false, preview: "visible", rolloutPath: userRollout);
    await fixture.InsertThreadAsync(
        "fork-thread", "Fork", "subagent", "vscode",
        archived: false, preview: "visible", rolloutPath: forkRollout);
    var monitor = new TaskMonitor(new SqliteThreadStore(fixture.DatabasePath));

    var result = await monitor.ScanAsync(Options(), default);

    Assert.Equal(2, result.Items.Count);
    Assert.Contains(result.Items, item => item.Id == "user-thread:user-turn" && item.State == TaskState.Running);
    Assert.Contains(result.Items, item => item.Id == "fork-thread:fork-turn" && item.State == TaskState.Waiting);
    Assert.Equal(0, result.UnreadableRolloutCount);
}
```

- [ ] **Step 3: 运行测试并确认 RED**

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~RolloutParserTests|FullyQualifiedName~ForkedTaskMonitoringTests"
```

预期：两个新兼容测试因现有代码读取缺失 `payload.completed_at` 而以 `CodexDataException(FormatChanged)` 失败；失败不能来自测试拼写或夹具错误。

- [ ] **Step 4: 写入最小生产实现**

把 `ApplyLine` 中的终止事件构造替换为：

```csharp
var terminal = new LifecycleEvent(
    kind.Value,
    turnId,
    startedAt,
    TerminalAt(root, payload, kind.Value));
return current is null || current.TurnId == turnId ? terminal : current;
```

并在 `FormatChanged` 之前增加：

```csharp
private static DateTimeOffset TerminalAt(
    JsonElement root,
    JsonElement payload,
    LifecycleKind kind)
{
    if (payload.TryGetProperty("completed_at", out var completedElement))
    {
        var completed = completedElement.GetDouble();
        return DateTimeOffset.FromUnixTimeMilliseconds((long)(completed * 1000));
    }

    if (kind == LifecycleKind.Aborted &&
        root.TryGetProperty("timestamp", out var timestampElement) &&
        timestampElement.ValueKind == JsonValueKind.String &&
        timestampElement.TryGetDateTimeOffset(out var timestamp))
        return timestamp;

    throw new JsonException("terminal timestamp is missing");
}
```

不增加 catch-and-ignore 分支，所有无效生命周期仍走固定 `FormatChanged`。

- [ ] **Step 5: 运行聚焦 GREEN**

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~RolloutParserTests|FullyQualifiedName~ForkedTaskMonitoringTests|FullyQualifiedName~TaskMonitorTests"
```

预期：全部 PASS；合法中止 Fork 与普通任务共存，严格格式检测、增量读取和截断尾行行为不回归。

- [ ] **Step 6: 提交协议兼容修复**

```powershell
git add windows/CodexTaskMonitor.Core/Monitoring/RolloutParser.cs windows/CodexTaskMonitor.Tests/Monitoring/RolloutParserTests.cs windows/CodexTaskMonitor.Tests/Monitoring/ForkedTaskMonitoringTests.cs
git diff --cached --check
git -c user.name='Codex Agent' -c user.email='codex-agent@localhost' commit -m "fix: parse aborted turns with root timestamps"
```

### Task 2: 完整验证、实机扫描、打包并升级安装

**Files:**
- Modify: `docs/windows-manual-test.md:45-72`
- Generate but keep ignored: `windows/publish/win-x64/CodexTaskMonitor.exe`
- Generate but keep ignored: `windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe`

**Interfaces:**
- Consumes: Release 生产程序集、`verify_windows_environment.ps1`、`verify_windows_packaging.ps1` 和本机只读 Codex 数据。
- Produces: 已验证提交、绑定当前字节文件的安装包 SHA-256、升级后的 per-user 安装。

- [ ] **Step 1: 运行完整 Release 验证**

```powershell
dotnet test windows\CodexTaskMonitor.sln -c Release
dotnet build windows\CodexTaskMonitor.sln -c Release -warnaserror
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_environment.ps1
git diff --check
```

预期：测试 0 failed；构建 0 warnings/0 errors；环境预检 9 个布尔值全部为 `true`；diff check 无输出。

- [ ] **Step 2: 用生产程序集扫描本机真实数据**

运行以下 PowerShell 7 脚本；它只打印固定状态和计数：

```powershell
$release = (Resolve-Path 'windows\CodexTaskMonitor.Tests\bin\Release\net8.0-windows').Path
[System.Runtime.InteropServices.NativeLibrary]::Load(
    (Join-Path $release 'runtimes\win-x64\native\e_sqlite3.dll')) | Out-Null
@(
    'SQLitePCLRaw.core.dll',
    'SQLitePCLRaw.provider.e_sqlite3.dll',
    'Microsoft.Data.Sqlite.dll',
    'CodexTaskMonitor.Core.dll'
) | ForEach-Object { Add-Type -Path (Join-Path $release $_) }

$paths = [CodexTaskMonitor.Core.Data.CodexDataPaths]::ForHome(
    [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile),
    [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData))
$preferences = [CodexTaskMonitor.Core.Preferences.MonitorPreferencesStore]::new($paths.PreferencesPath)
$settings = $preferences.LoadAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult()
$baseline = if ($null -ne $settings.Baseline) {
    $settings.Baseline
} else {
    [DateTimeOffset]::UtcNow.AddHours(-1)
}
$options = [CodexTaskMonitor.Core.Monitoring.MonitorScanOptions]::new(
    $baseline,
    $settings.AdoptedTurnIds,
    $settings.DismissedTurnIds,
    $settings.DismissedItemIds)
$monitor = [CodexTaskMonitor.Core.Monitoring.TaskMonitor]::new(
    [CodexTaskMonitor.Core.Data.SqliteThreadStore]::new($paths.DatabasePath))
$result = $monitor.ScanAsync($options, [Threading.CancellationToken]::None).GetAwaiter().GetResult()

Write-Output 'SCAN_SUCCESS=true'
Write-Output ("ITEM_COUNT={0}" -f $result.Items.Count)
Write-Output ("RUNNING_COUNT={0}" -f @($result.Items | Where-Object State -eq Running).Count)
Write-Output ("WAITING_COUNT={0}" -f @($result.Items | Where-Object State -eq Waiting).Count)
Write-Output ("UNREADABLE_COUNT={0}" -f $result.UnreadableRolloutCount)
```

预期：脚本完成且打印 `SCAN_SUCCESS=true`，不出现 `FormatChanged`；禁止加入任何任务内容、ID 和路径输出。

- [ ] **Step 3: 只生成一次应用和一次安装包**

```powershell
dotnet publish windows\CodexTaskMonitor.Windows\CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows\publish\win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows\Installer\CodexTaskMonitor.iss
$installer = 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $installer).Length
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_packaging.ps1 -RequireOutputs -ExpectedInstallerSha256 $hash
"SHA256=$hash SIZE=$size"
```

预期：publish 目录只含 `CodexTaskMonitor.exe`；ISCC 7.1.0 成功；应用和安装包均为 PE/MZ、`NotSigned`；包装验证通过。

- [ ] **Step 4: 静默升级并做非交互存活验收**

```powershell
$installer = (Resolve-Path 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe').Path
$install = Start-Process -FilePath $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Installer exit code: $($install.ExitCode)" }
$app = Join-Path $env:LOCALAPPDATA 'Programs\Codex Task Monitor\CodexTaskMonitor.exe'
Start-Process -FilePath $app
Start-Sleep -Seconds 5
if (@(Get-Process -Name CodexTaskMonitor -ErrorAction SilentlyContinue).Count -ne 1) { throw 'Expected one running monitor process.' }
```

预期：安装器退出码 0，只有一个监视器进程运行；不点击、不滚动、不控制 Codex/ChatGPT UI。

- [ ] **Step 5: 更新自动证据并提交**

使用 `apply_patch` 将 `docs/windows-manual-test.md` 的提交、测试总数、安装包 SHA-256、字节大小和自动扫描结果替换为步骤 1–4 的精确输出。所有真实 UI、点击、重启和登录项继续保持 `[ ]`。

```powershell
git add docs/windows-manual-test.md
git diff --cached --check
git -c user.name='Codex Agent' -c user.email='codex-agent@localhost' commit -m "docs: record aborted event compatibility build"
```

- [ ] **Step 6: 最终一致性验证**

```powershell
$expected = (Select-String -LiteralPath docs\windows-manual-test.md -Pattern '[A-F0-9]{64}').Matches.Value | Select-Object -First 1
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_packaging.ps1 -RequireOutputs -ExpectedInstallerSha256 $expected
dotnet test windows\CodexTaskMonitor.sln -c Release --no-restore
dotnet build windows\CodexTaskMonitor.sln -c Release -warnaserror --no-restore
git diff --check
git show --check --oneline HEAD
git status --short
```

预期：包装哈希与当前安装包一致；全部测试与 warnings-as-errors 构建通过；tracked 工作树干净。最终报告明确区分自动验证与仍需用户完成的视觉确认。
