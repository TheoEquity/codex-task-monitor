# Windows 11 Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Windows 11 x64 Codex task monitor that preserves the existing macOS app, reproduces task monitoring and exact sidebar reveal on the validated machine, and ships as a per-user installer.

**Architecture:** Keep the Swift/macOS targets unchanged and add a separate .NET 8 solution under `windows/`. A platform-neutral C# core reads Codex SQLite/JSONL state and exposes immutable monitor items; a WPF shell owns display, startup, deep links, and a Windows UI Automation adapter with deterministic scroll fallback.

**Tech Stack:** .NET SDK 8.0.424, C# 12, WPF, Microsoft.Data.Sqlite 8.0.30, xUnit 2.9.3, Windows UI Automation, Win32 P/Invoke, Inno Setup 7.1.0, GitHub Actions.

## Global Constraints

- Target only the validated Windows 11 x64 machine and the currently installed Codex/ChatGPT desktop app behavior.
- Preserve all existing Swift/macOS source and build behavior.
- Produce a self-contained `win-x64` build; users must not need a separately installed .NET runtime.
- Open Codex data read-only and never write into `%USERPROFILE%\.codex`.
- Poll every 2 seconds, show at most 6 task rows before scrolling, use a 330 DIP window, a 48 DIP header, and 62 DIP rows.
- Use `threadID:turnID` as the exact handled-item key.
- Reject missing, duplicate, or structurally ambiguous sidebar matches; never guess or send a click.
- Bound sidebar reveal to 5 seconds of window readiness plus 8 seconds/80 scroll steps.
- Keep logs local and exclude task titles, prompts, rollout bodies, credentials, and authentication data.
- Install per user without elevation; commercial code signing is outside the first release.
- Apply TDD for every behavior change and commit after each task passes its focused and regression tests.

---

## Planned File Structure

### Repository and build files

- `global.json` — pins .NET SDK 8.0.424.
- `windows/CodexTaskMonitor.sln` — Windows solution.
- `windows/Directory.Build.props` — shared compiler and analyzer settings.
- `windows/Directory.Packages.props` — centrally pinned NuGet versions.
- `.github/workflows/windows.yml` — Windows build, test, publish, installer, and release artifacts.

### `windows/CodexTaskMonitor.Core`

- `CodexTaskMonitor.Core.csproj` — .NET 8 core library.
- `Data/CodexDataPaths.cs` — resolves Codex and app-local paths.
- `Data/ThreadRecord.cs` — immutable SQLite thread record.
- `Data/IThreadStore.cs` — thread-store contract.
- `Data/SqliteThreadStore.cs` — schema-checked read-only SQLite query.
- `Monitoring/LifecycleEvent.cs` — lifecycle event model.
- `Monitoring/TaskStateResolver.cs` — running/waiting/hidden rules.
- `Monitoring/MonitorItem.cs` — UI-facing immutable task record.
- `Monitoring/RolloutParser.cs` — complete-line lifecycle parser.
- `Monitoring/TaskMonitor.cs` — per-file cache, incremental reads, scan ordering.
- `Preferences/MonitorPreferences.cs` — persisted baseline and handled IDs.
- `Preferences/MonitorPreferencesStore.cs` — atomic JSON persistence.
- `Sidebar/SessionIndex.cs` — latest exact UUID-to-title mapping.
- `Sidebar/SidebarThreadGroup.cs` — pinned/section/project/projectless group model.
- `Sidebar/SidebarTargetResolver.cs` — exact sidebar target resolution.
- `CodexThreadLink.cs` — validates and builds `codex://threads/<UUID>`.
- `MonitorPanelLayout.cs` — deterministic panel height.
- `MonitorListUpdate.cs` — deterministic inserted-row detection.

### `windows/CodexTaskMonitor.Windows`

- `CodexTaskMonitor.Windows.csproj` — WPF executable.
- `App.xaml`, `App.xaml.cs` — composition root and application lifetime.
- `MainWindow.xaml`, `MainWindow.xaml.cs` — borderless floating panel.
- `ViewModels/MonitorViewModel.cs` — polling, commands, error priority, cancellation generations.
- `ViewModels/MonitorItemViewModel.cs` — row projection.
- `Services/SingleInstanceService.cs` — one process per Windows user.
- `Services/StartupRegistration.cs` — shared HKCU Run entry.
- `Services/CodexDeepLinkLauncher.cs` — shell deep-link launch.
- `Services/ThreadActivationService.cs` — deep link followed by sidebar reveal.
- `Services/LocalDiagnostics.cs` — privacy-safe rotating local logs.
- `Automation/AutomationNode.cs` — immutable UIA snapshot node.
- `Automation/ChatGptWindowLocator.cs` — current ChatGPT main-window handle.
- `Automation/UiAutomationSnapshotProvider.cs` — bounded raw UIA traversal.
- `Automation/SidebarMatcher.cs` — exact title and group disambiguation.
- `Automation/UiAutomationSidebarScrollInput.cs` — preferred UIA `ScrollPattern` input.
- `Automation/SidebarScrollInput.cs` — ordered UIA/native input dispatcher.
- `Automation/SidebarScrollController.cs` — upward reset and downward bounded scan.
- `Automation/WindowsSidebarRevealer.cs` — reveal orchestration and safe degradation.
- `Interop/NativeSidebarWheelInput.cs` — posted-message and foreground-only physical wheel fallback.
- `Interop/NativeMethods.cs` — Win32 wheel-message and cursor interop.

### `windows/CodexTaskMonitor.Tests`

- Mirrors each production component with focused xUnit tests.
- `Fixtures/CodexFixture.cs` — creates temporary SQLite, JSONL, and state fixtures.
- `Fakes/FakeAutomationEnvironment.cs` — synthetic UIA snapshots and scroll behavior.

### Packaging and acceptance

- `windows/Installer/CodexTaskMonitor.iss` — per-user Inno Setup installer.
- `windows/Scripts/verify_windows_environment.ps1` — read-only target-machine preflight.
- `docs/windows-manual-test.md` — exact current-machine acceptance checklist.
- `README.md` — Windows build, install, privacy, and known-boundary documentation.

---

### Task 1: Bootstrap the Windows toolchain, solution, and path contract

**Files:**
- Create: `global.json`
- Create: `windows/CodexTaskMonitor.sln`
- Create: `windows/Directory.Build.props`
- Create: `windows/Directory.Packages.props`
- Create: `windows/CodexTaskMonitor.Core/CodexTaskMonitor.Core.csproj`
- Create: `windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj`
- Create: `windows/CodexTaskMonitor.Tests/CodexTaskMonitor.Tests.csproj`
- Create: `windows/CodexTaskMonitor.Core/Data/CodexDataPaths.cs`
- Test: `windows/CodexTaskMonitor.Tests/Data/CodexDataPathsTests.cs`

**Interfaces:**
- Produces: `CodexDataPaths.ForHome(string homeDirectory, string localAppDataDirectory)` returning database, session-index, global-state, preferences, and log paths.
- Consumes: No earlier task.

- [ ] **Step 1: Install and verify the pinned developer tools**

Run in an ordinary PowerShell window:

```powershell
winget install --exact --id Microsoft.DotNet.SDK.8 --version 8.0.424 --source winget --accept-source-agreements --accept-package-agreements
winget install --exact --id JRSoftware.InnoSetup.7 --version 7.1.0 --source winget --accept-source-agreements --accept-package-agreements
dotnet --version
& 'C:\Program Files\Inno Setup 7\ISCC.exe' /?
```

Expected: `dotnet --version` prints `8.0.424`; ISCC prints its command-line help and exits successfully.

- [ ] **Step 2: Create the solution and three projects**

Run:

```powershell
dotnet new sln -n CodexTaskMonitor -o windows
dotnet new classlib -n CodexTaskMonitor.Core -o windows/CodexTaskMonitor.Core -f net8.0
dotnet new wpf -n CodexTaskMonitor.Windows -o windows/CodexTaskMonitor.Windows -f net8.0
dotnet new xunit -n CodexTaskMonitor.Tests -o windows/CodexTaskMonitor.Tests -f net8.0
dotnet sln windows/CodexTaskMonitor.sln add windows/CodexTaskMonitor.Core/CodexTaskMonitor.Core.csproj
dotnet sln windows/CodexTaskMonitor.sln add windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj
dotnet sln windows/CodexTaskMonitor.sln add windows/CodexTaskMonitor.Tests/CodexTaskMonitor.Tests.csproj
dotnet add windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj reference windows/CodexTaskMonitor.Core/CodexTaskMonitor.Core.csproj
dotnet add windows/CodexTaskMonitor.Tests/CodexTaskMonitor.Tests.csproj reference windows/CodexTaskMonitor.Core/CodexTaskMonitor.Core.csproj windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj
Remove-Item -LiteralPath windows/CodexTaskMonitor.Core/Class1.cs
Remove-Item -LiteralPath windows/CodexTaskMonitor.Tests/UnitTest1.cs
```

Expected: the solution contains exactly the core, WPF, and test projects.

- [ ] **Step 3: Pin the SDK, compiler settings, and packages**

Write `global.json`:

```json
{
  "sdk": {
    "version": "8.0.424",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Write `windows/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'CodexTaskMonitor.Windows'">
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>CodexTaskMonitor.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
</Project>
```

Write `windows/Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="8.0.30" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
```

Replace the three project files with:

```xml
<!-- windows/CodexTaskMonitor.Core/CodexTaskMonitor.Core.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Microsoft.Data.Sqlite" /></ItemGroup>
</Project>
```

```xml
<!-- windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>CodexTaskMonitor</AssemblyName>
    <RootNamespace>CodexTaskMonitor.Windows</RootNamespace>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="..\CodexTaskMonitor.Core\CodexTaskMonitor.Core.csproj" /></ItemGroup>
</Project>
```

```xml
<!-- windows/CodexTaskMonitor.Tests/CodexTaskMonitor.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodexTaskMonitor.Core\CodexTaskMonitor.Core.csproj" />
    <ProjectReference Include="..\CodexTaskMonitor.Windows\CodexTaskMonitor.Windows.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Write the failing path-resolution test**

Create `windows/CodexTaskMonitor.Tests/Data/CodexDataPathsTests.cs`:

```csharp
using CodexTaskMonitor.Core.Data;

namespace CodexTaskMonitor.Tests.Data;

public sealed class CodexDataPathsTests
{
    [Fact]
    public void ForHome_UsesCodexAndLocalAppDataRoots()
    {
        var paths = CodexDataPaths.ForHome(@"C:\Users\Tester", @"D:\LocalAppData");

        Assert.Equal(@"C:\Users\Tester\.codex\state_5.sqlite", paths.DatabasePath);
        Assert.Equal(@"C:\Users\Tester\.codex\session_index.jsonl", paths.SessionIndexPath);
        Assert.Equal(@"C:\Users\Tester\.codex\.codex-global-state.json", paths.GlobalStatePath);
        Assert.Equal(@"D:\LocalAppData\CodexTaskMonitor\settings.json", paths.PreferencesPath);
        Assert.Equal(@"D:\LocalAppData\CodexTaskMonitor\logs", paths.LogDirectory);
    }
}
```

- [ ] **Step 5: Run the test and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~CodexDataPathsTests
```

Expected: FAIL to compile with `CS0246` because `CodexDataPaths` does not exist.

- [ ] **Step 6: Implement the minimal path contract**

Create `windows/CodexTaskMonitor.Core/Data/CodexDataPaths.cs`:

```csharp
namespace CodexTaskMonitor.Core.Data;

public sealed record CodexDataPaths(
    string DatabasePath,
    string SessionIndexPath,
    string GlobalStatePath,
    string PreferencesPath,
    string LogDirectory)
{
    public static CodexDataPaths ForHome(string homeDirectory, string localAppDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataDirectory);

        var codex = Path.Combine(homeDirectory, ".codex");
        var app = Path.Combine(localAppDataDirectory, "CodexTaskMonitor");
        return new CodexDataPaths(
            Path.Combine(codex, "state_5.sqlite"),
            Path.Combine(codex, "session_index.jsonl"),
            Path.Combine(codex, ".codex-global-state.json"),
            Path.Combine(app, "settings.json"),
            Path.Combine(app, "logs"));
    }
}
```

- [ ] **Step 7: Run the focused test and solution build**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~CodexDataPathsTests
dotnet build windows/CodexTaskMonitor.sln -c Release
```

Expected: one focused test passes; Release build exits 0 with zero warnings.

- [ ] **Step 8: Commit**

```powershell
git add global.json windows
git commit -m "build: bootstrap Windows monitor solution"
```

### Task 2: Port lifecycle state, deep links, and deterministic layout helpers

**Files:**
- Create: `windows/CodexTaskMonitor.Core/Monitoring/LifecycleEvent.cs`
- Create: `windows/CodexTaskMonitor.Core/Monitoring/TaskStateResolver.cs`
- Create: `windows/CodexTaskMonitor.Core/Monitoring/MonitorItem.cs`
- Create: `windows/CodexTaskMonitor.Core/CodexThreadLink.cs`
- Create: `windows/CodexTaskMonitor.Core/MonitorPanelLayout.cs`
- Create: `windows/CodexTaskMonitor.Core/MonitorListUpdate.cs`
- Test: `windows/CodexTaskMonitor.Tests/Monitoring/TaskStateResolverTests.cs`
- Test: `windows/CodexTaskMonitor.Tests/CodexThreadLinkTests.cs`
- Test: `windows/CodexTaskMonitor.Tests/MonitorUiRulesTests.cs`

**Interfaces:**
- Produces: `LifecycleEvent`, `TaskState`, `TaskStateResolver.Resolve`, `MonitorItem`, `CodexThreadLink.TryCreate`, `MonitorPanelLayout.Height`, and `MonitorListUpdate.InsertedId`.
- Consumes: No earlier production interface.

- [ ] **Step 1: Write failing domain-rule tests**

Create the three test files with these cases:

```csharp
// Monitoring/TaskStateResolverTests.cs
using CodexTaskMonitor.Core.Monitoring;

namespace CodexTaskMonitor.Tests.Monitoring;

public sealed class TaskStateResolverTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.FromUnixTimeSeconds(100);

    [Fact]
    public void StartedAfterBaseline_IsRunning() =>
        Assert.Equal(TaskState.Running, TaskStateResolver.Resolve(
            new LifecycleEvent(LifecycleKind.Started, "turn-1", DateTimeOffset.FromUnixTimeSeconds(101), null),
            Baseline, [], []));

    [Fact]
    public void CompletedAfterBaseline_IsWaiting() =>
        Assert.Equal(TaskState.Waiting, TaskStateResolver.Resolve(
            new LifecycleEvent(LifecycleKind.Completed, "turn-1", DateTimeOffset.FromUnixTimeSeconds(99), DateTimeOffset.FromUnixTimeSeconds(101)),
            Baseline, [], []));

    [Fact]
    public void HandledExactTurn_IsHidden() =>
        Assert.Null(TaskStateResolver.Resolve(
            new LifecycleEvent(LifecycleKind.Aborted, "turn-1", DateTimeOffset.FromUnixTimeSeconds(101), DateTimeOffset.FromUnixTimeSeconds(102)),
            Baseline, [], ["turn-1"]));

    [Fact]
    public void AdoptedOldRunningTurn_IsRunning() =>
        Assert.Equal(TaskState.Running, TaskStateResolver.Resolve(
            new LifecycleEvent(LifecycleKind.Started, "turn-old", DateTimeOffset.FromUnixTimeSeconds(90), null),
            Baseline, ["turn-old"], []));
}
```

```csharp
// CodexThreadLinkTests.cs
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class CodexThreadLinkTests
{
    [Fact]
    public void ValidUuid_BuildsCodexThreadUri()
    {
        Assert.True(CodexThreadLink.TryCreate("11111111-1111-4111-8111-111111111111", out var uri));
        Assert.Equal("codex://threads/11111111-1111-4111-8111-111111111111", uri!.AbsoluteUri);
    }

    [Fact]
    public void InvalidUuid_IsRejected() => Assert.False(CodexThreadLink.TryCreate("not-a-uuid", out _));
}
```

```csharp
// MonitorUiRulesTests.cs
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class MonitorUiRulesTests
{
    [Theory]
    [InlineData(3, false, 237)]
    [InlineData(8, true, 458)]
    public void Height_MatchesApprovedLayout(int count, bool error, double expected) =>
        Assert.Equal(expected, MonitorPanelLayout.Height(count, error));

    [Fact]
    public void InsertedId_ReturnsOnlyAnUnambiguousAddition()
    {
        Assert.Equal("new", MonitorListUpdate.InsertedId(["first", "last"], ["first", "new", "last"]));
        Assert.Null(MonitorListUpdate.InsertedId(["first", "handled", "last"], ["new", "first", "last"]));
    }
}
```

- [ ] **Step 2: Run the focused tests and verify they fail to compile**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter "FullyQualifiedName~TaskStateResolverTests|FullyQualifiedName~CodexThreadLinkTests|FullyQualifiedName~MonitorUiRulesTests"
```

Expected: FAIL with missing lifecycle, link, and layout types.

- [ ] **Step 3: Implement the domain types and helpers**

Create the production files:

```csharp
// Monitoring/LifecycleEvent.cs
namespace CodexTaskMonitor.Core.Monitoring;

public enum LifecycleKind { Started, Completed, Aborted }
public enum TaskState { Running, Waiting }

public sealed record LifecycleEvent(
    LifecycleKind Kind,
    string TurnId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt)
{
    public DateTimeOffset ActivityDate => CompletedAt ?? StartedAt;
}
```

```csharp
// Monitoring/TaskStateResolver.cs
namespace CodexTaskMonitor.Core.Monitoring;

public static class TaskStateResolver
{
    public static TaskState? Resolve(
        LifecycleEvent item,
        DateTimeOffset baseline,
        IReadOnlySet<string> adoptedTurnIds,
        IReadOnlySet<string> dismissedTurnIds)
    {
        var roundedBaseline = DateTimeOffset.FromUnixTimeSeconds(baseline.ToUnixTimeSeconds());
        var crossesBaseline = item.CompletedAt is { } completed && completed >= roundedBaseline;
        if (item.StartedAt < roundedBaseline && !crossesBaseline && !adoptedTurnIds.Contains(item.TurnId))
            return null;

        return item.Kind switch
        {
            LifecycleKind.Started => TaskState.Running,
            LifecycleKind.Completed or LifecycleKind.Aborted when dismissedTurnIds.Contains(item.TurnId) => null,
            LifecycleKind.Completed or LifecycleKind.Aborted => TaskState.Waiting,
            _ => null
        };
    }
}
```

```csharp
// Monitoring/MonitorItem.cs
namespace CodexTaskMonitor.Core.Monitoring;

public sealed record MonitorItem(
    string ThreadId,
    string TurnId,
    string Title,
    string Cwd,
    string ProjectName,
    DateTimeOffset EventDate,
    TaskState State)
{
    public string Id => $"{ThreadId}:{TurnId}";
}
```

```csharp
// CodexThreadLink.cs
namespace CodexTaskMonitor.Core;

public static class CodexThreadLink
{
    public static bool TryCreate(string threadId, out Uri? uri)
    {
        uri = null;
        if (!Guid.TryParseExact(threadId, "D", out var parsed)) return false;
        uri = new Uri($"codex://threads/{parsed:D}");
        return true;
    }
}
```

```csharp
// MonitorPanelLayout.cs
namespace CodexTaskMonitor.Core;

public static class MonitorPanelLayout
{
    public static double Height(int itemCount, bool hasError)
    {
        var rows = Math.Min(Math.Max(itemCount, 0), 6);
        var dividers = rows > 0 ? rows : 0;
        return 48 + rows * 62 + dividers + (hasError ? 32 : 0);
    }
}
```

```csharp
// MonitorListUpdate.cs
namespace CodexTaskMonitor.Core;

public static class MonitorListUpdate
{
    public static string? InsertedId(IReadOnlyList<string> oldIds, IReadOnlyList<string> newIds)
    {
        var removed = oldIds.Except(newIds, StringComparer.Ordinal).Any();
        var inserted = newIds.Except(oldIds, StringComparer.Ordinal).ToArray();
        return !removed && inserted.Length == 1 ? inserted[0] : null;
    }
}
```

- [ ] **Step 4: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter "FullyQualifiedName~TaskStateResolverTests|FullyQualifiedName~CodexThreadLinkTests|FullyQualifiedName~MonitorUiRulesTests"
dotnet test windows/CodexTaskMonitor.sln
```

Expected: all focused tests and the full suite pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/CodexTaskMonitor.Core windows/CodexTaskMonitor.Tests
git commit -m "feat: port monitor domain rules to Windows"
```

### Task 3: Resolve exact sidebar titles and groups

**Files:**
- Create: `windows/CodexTaskMonitor.Core/Sidebar/SessionIndex.cs`
- Create: `windows/CodexTaskMonitor.Core/Sidebar/SidebarThreadGroup.cs`
- Create: `windows/CodexTaskMonitor.Core/Sidebar/SidebarTargetResolver.cs`
- Test: `windows/CodexTaskMonitor.Tests/Sidebar/SidebarTargetResolverTests.cs`

**Interfaces:**
- Consumes: no earlier production interface.
- Produces: `SessionIndex.LatestTitle`, `ThreadGroupingInfo`, `SidebarThreadGroup`, `SidebarTarget`, and `SidebarTargetResolver.Resolve`.

- [ ] **Step 1: Write failing target-resolution tests**

Create `windows/CodexTaskMonitor.Tests/Sidebar/SidebarTargetResolverTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Tests.Sidebar;

public sealed class SidebarTargetResolverTests
{
    private const string ThreadId = "11111111-1111-4111-8111-111111111111";

    [Fact]
    public void LatestCompleteSessionRow_WinsOverOlderTitleAndPartialTail()
    {
        var data = Encoding.UTF8.GetBytes(
            $$"""
            {"id":"{{ThreadId}}","thread_name":"Old"}
            {"id":"{{ThreadId}}","thread_name":"Sidebar title"}
            {"id":"other","thread_name":
            """);

        Assert.Equal("Sidebar title", SessionIndex.LatestTitle(ThreadId, data));
    }

    [Fact]
    public void PinnedDatabaseFlag_HasHighestPriority()
    {
        var title = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { id = ThreadId, thread_name = "Pinned task" }) + "\n");
        var grouping = new ThreadGroupingInfo(true, null, @"C:\work\demo");
        var target = SidebarTargetResolver.Resolve(ThreadId, grouping, title, "{}"u8.ToArray());

        Assert.Equal(new SidebarTarget("Pinned task", SidebarThreadGroup.Pinned()), target);
    }

    [Fact]
    public void SectionName_WinsBeforeProjectFallback()
    {
        var title = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { id = ThreadId, thread_name = "Section task" }) + "\n");
        var grouping = new ThreadGroupingInfo(false, "Research", @"C:\work\demo");
        var target = SidebarTargetResolver.Resolve(ThreadId, grouping, title, "{}"u8.ToArray());

        Assert.Equal(new SidebarTarget("Section task", SidebarThreadGroup.Section("Research")), target);
    }

    [Fact]
    public void MissingExactTitle_ReturnsNull()
    {
        var grouping = new ThreadGroupingInfo(false, null, @"C:\work\demo");
        Assert.Null(SidebarTargetResolver.Resolve(ThreadId, grouping, ""u8.ToArray(), "{}"u8.ToArray()));
    }
}
```

- [ ] **Step 2: Run the tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~SidebarTargetResolverTests
```

Expected: FAIL to compile because sidebar resolution types do not exist.

- [ ] **Step 3: Implement session-index parsing and target priority**

Create the three production files:

```csharp
// Sidebar/SessionIndex.cs
using System.Text.Json;

namespace CodexTaskMonitor.Core.Sidebar;

public static class SessionIndex
{
    public static string? LatestTitle(string threadId, ReadOnlySpan<byte> data)
    {
        string? title = null;
        var endsWithNewline = data.Length == 0 || data[^1] == (byte)'\n';
        var lines = data.ToArray().AsSpan();
        var start = 0;
        for (var index = 0; index <= lines.Length; index++)
        {
            if (index < lines.Length && lines[index] != (byte)'\n') continue;
            var line = lines[start..index];
            start = index + 1;
            if (index == lines.Length && !endsWithNewline) break;
            if (line.IsEmpty) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.GetProperty("id").GetString() == threadId)
                {
                    var candidate = root.GetProperty("thread_name").GetString();
                    title = string.IsNullOrEmpty(candidate) ? null : candidate;
                }
            }
            catch (JsonException) { throw; }
        }
        return title;
    }
}
```

```csharp
// Sidebar/SidebarThreadGroup.cs
namespace CodexTaskMonitor.Core.Sidebar;

public enum SidebarThreadGroupKind { Pinned, Section, Project, Projectless }

public sealed record SidebarThreadGroup(SidebarThreadGroupKind Kind, string? Name)
{
    public static SidebarThreadGroup Pinned() => new(SidebarThreadGroupKind.Pinned, null);
    public static SidebarThreadGroup Section(string name) => new(SidebarThreadGroupKind.Section, name);
    public static SidebarThreadGroup Project(string name) => new(SidebarThreadGroupKind.Project, name);
    public static SidebarThreadGroup Projectless() => new(SidebarThreadGroupKind.Projectless, null);
}

public sealed record ThreadGroupingInfo(bool IsPinned, string? SectionName, string Cwd);
public sealed record SidebarTarget(string Title, SidebarThreadGroup Group);
```

```csharp
// Sidebar/SidebarTargetResolver.cs
using System.Text.Json;

namespace CodexTaskMonitor.Core.Sidebar;

public static class SidebarTargetResolver
{
    public static SidebarTarget? Resolve(
        string threadId,
        ThreadGroupingInfo grouping,
        ReadOnlySpan<byte> sessionIndex,
        ReadOnlySpan<byte> globalState)
    {
        var title = SessionIndex.LatestTitle(threadId, sessionIndex);
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (grouping.IsPinned) return new SidebarTarget(title, SidebarThreadGroup.Pinned());
        if (!string.IsNullOrWhiteSpace(grouping.SectionName))
            return new SidebarTarget(title, SidebarThreadGroup.Section(grouping.SectionName));

        using var document = JsonDocument.Parse(globalState.IsEmpty ? "{}"u8 : globalState);
        var root = document.RootElement;
        if (TryProjectName(root, threadId, out var projectName))
            return new SidebarTarget(title, SidebarThreadGroup.Project(projectName!));
        if (Contains(root, "projectless-thread-ids", threadId))
            return new SidebarTarget(title, SidebarThreadGroup.Projectless());

        var cwdProject = UniqueProjectForCwd(root, grouping.Cwd);
        return cwdProject is null ? null : new SidebarTarget(title, SidebarThreadGroup.Project(cwdProject));
    }

    private static bool TryProjectName(JsonElement root, string threadId, out string? name)
    {
        name = null;
        if (!root.TryGetProperty("thread-project-assignments", out var assignments) ||
            !assignments.TryGetProperty(threadId, out var assignment) ||
            !assignment.TryGetProperty("projectId", out var projectIdElement) ||
            !root.TryGetProperty("local-projects", out var projects) ||
            !projects.TryGetProperty(projectIdElement.GetString()!, out var project) ||
            !project.TryGetProperty("name", out var nameElement)) return false;
        name = nameElement.GetString();
        return !string.IsNullOrWhiteSpace(name);
    }

    private static bool Contains(JsonElement root, string property, string value) =>
        root.TryGetProperty(property, out var array) &&
        array.ValueKind == JsonValueKind.Array &&
        array.EnumerateArray().Any(item => item.GetString() == value);

    private static string? UniqueProjectForCwd(JsonElement root, string cwd)
    {
        if (!root.TryGetProperty("local-projects", out var projects)) return null;
        var normalized = Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar);
        var matches = new List<string>();
        foreach (var property in projects.EnumerateObject())
        {
            var project = property.Value;
            if (!project.TryGetProperty("name", out var name) || !project.TryGetProperty("rootPaths", out var roots)) continue;
            if (roots.EnumerateArray().Any(root =>
                string.Equals(Path.GetFullPath(root.GetString()!).TrimEnd(Path.DirectorySeparatorChar), normalized, StringComparison.OrdinalIgnoreCase)))
                matches.Add(name.GetString()!);
        }
        return matches.Distinct(StringComparer.Ordinal).Take(2).Count() == 1 ? matches[0] : null;
    }
}
```

- [ ] **Step 4: Add project, projectless, malformed-complete-line, and ambiguous-cwd cases**

Extend `SidebarTargetResolverTests.cs` with explicit JSON fixtures for:

```csharp
[Theory]
[InlineData(true, "DemoProject")]
[InlineData(false, null)]
public void ProjectFallback_IsAcceptedOnlyWhenUnique(bool unique, string? expected)
{
    var duplicate = unique ? "" : ",\"p2\":{\"name\":\"Other\",\"rootPaths\":[\"C:\\\\work\\\\demo\"]}";
    var state = Encoding.UTF8.GetBytes(
        "{\"local-projects\":{\"p1\":{\"name\":\"DemoProject\",\"rootPaths\":[\"C:\\\\work\\\\demo\"]}" + duplicate + "}}");
    var title = Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { id = ThreadId, thread_name = "Task" }) + "\n");
    var target = SidebarTargetResolver.Resolve(ThreadId, new(false, null, @"C:\work\demo"), title, state);
    Assert.Equal(expected, target?.Group.Name);
}
```

Also add these exact assertions so both malformed and syntactically valid non-newline tails are ignored, while a newline-terminated malformed record fails loudly:

```csharp
[Fact]
public void SessionIndex_UsesOnlyNewlineTerminatedRecords()
{
    var complete = JsonSerializer.Serialize(new { id = ThreadId, thread_name = "Complete" }) + "\n";
    var validButUnterminated = JsonSerializer.Serialize(new { id = ThreadId, thread_name = "Do not use yet" });
    Assert.Equal("Complete", SessionIndex.LatestTitle(ThreadId, Encoding.UTF8.GetBytes(complete + validButUnterminated)));
    Assert.Equal("Complete", SessionIndex.LatestTitle(ThreadId, Encoding.UTF8.GetBytes(complete + "{\"id\":")));
    Assert.Throws<JsonException>(() => SessionIndex.LatestTitle(
        ThreadId, Encoding.UTF8.GetBytes(complete + "{\"id\":}\n")));
}
```

- [ ] **Step 5: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~SidebarTargetResolverTests
dotnet test windows/CodexTaskMonitor.sln
```

Expected: all sidebar tests and the full suite pass.

- [ ] **Step 6: Commit**

```powershell
git add windows/CodexTaskMonitor.Core/Sidebar windows/CodexTaskMonitor.Tests/Sidebar
git commit -m "feat: resolve exact Windows sidebar targets"
```

### Task 4: Read visible Codex threads from SQLite in read-only mode

**Files:**
- Create: `windows/CodexTaskMonitor.Core/Data/ThreadRecord.cs`
- Create: `windows/CodexTaskMonitor.Core/Data/IThreadStore.cs`
- Create: `windows/CodexTaskMonitor.Core/Data/SqliteThreadStore.cs`
- Create: `windows/CodexTaskMonitor.Tests/Fixtures/CodexFixture.cs`
- Test: `windows/CodexTaskMonitor.Tests/Data/SqliteThreadStoreTests.cs`

**Interfaces:**
- Consumes: `ThreadGroupingInfo` from Task 3.
- Produces: `IThreadStore.ReadThreadsAsync(DateTimeOffset, CancellationToken)`, `IThreadGroupingLookup.FindGroupingAsync`, and immutable `ThreadRecord` values for Tasks 6 and 12.

- [ ] **Step 1: Write failing SQLite visibility tests**

Create `windows/CodexTaskMonitor.Tests/Data/SqliteThreadStoreTests.cs`:

```csharp
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Tests.Fixtures;

namespace CodexTaskMonitor.Tests.Data;

public sealed class SqliteThreadStoreTests
{
    [Fact]
    public async Task ReadThreads_ReturnsOnlyVisibleUserThreadsAndCodexCreatedVisibleThreads()
    {
        await using var fixture = await CodexFixture.CreateAsync();
        await fixture.InsertThreadAsync("visible", "Visible", "user", "vscode", archived: false, preview: "hello");
        await fixture.InsertThreadAsync("codex-created", "Codex", "subagent", "vscode", archived: false, preview: "hello");
        await fixture.InsertThreadAsync("internal", "Internal", "subagent", "{\"subagent\":{}}", archived: false, preview: "hello");
        await fixture.InsertThreadAsync("archived", "Archived", "user", "vscode", archived: true, preview: "hello");
        await fixture.InsertThreadAsync("empty", "Empty", "user", "vscode", archived: false, preview: "");

        var records = await new SqliteThreadStore(fixture.DatabasePath).ReadThreadsAsync(DateTimeOffset.UnixEpoch, default);

        Assert.Equal(["codex-created", "visible"], records.Select(record => record.Id).Order().ToArray());
    }

    [Fact]
    public async Task MissingRequiredColumn_ThrowsFormatChanged()
    {
        await using var fixture = await CodexFixture.CreateAsync(includePreviewColumn: false);
        var error = await Assert.ThrowsAsync<CodexDataException>(() =>
            new SqliteThreadStore(fixture.DatabasePath).ReadThreadsAsync(DateTimeOffset.UnixEpoch, default));
        Assert.Equal(CodexDataError.FormatChanged, error.Error);
    }
}
```

- [ ] **Step 2: Create the complete test fixture**

Create `windows/CodexTaskMonitor.Tests/Fixtures/CodexFixture.cs` with a temporary directory, an SQLite connection, and these exact helpers:

```csharp
using Microsoft.Data.Sqlite;

namespace CodexTaskMonitor.Tests.Fixtures;

public sealed class CodexFixture : IAsyncDisposable
{
    private readonly string root;
    public string DatabasePath { get; }
    public string RolloutPath { get; }

    private CodexFixture(string root)
    {
        this.root = root;
        DatabasePath = Path.Combine(root, "state_5.sqlite");
        RolloutPath = Path.Combine(root, "rollout.jsonl");
    }

    public static async Task<CodexFixture> CreateAsync(bool includePreviewColumn = true)
    {
        var fixture = new CodexFixture(Path.Combine(Path.GetTempPath(), $"CodexTaskMonitor-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(fixture.root);
        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        var preview = includePreviewColumn ? ", preview TEXT NOT NULL DEFAULT ''" : "";
        var command = connection.CreateCommand();
        command.CommandText = $$"""
            CREATE TABLE thread_sections (id TEXT PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE threads (
              id TEXT PRIMARY KEY, rollout_path TEXT NOT NULL, cwd TEXT NOT NULL,
              title TEXT NOT NULL, archived INTEGER NOT NULL, updated_at_ms INTEGER,
              thread_source TEXT, source TEXT NOT NULL, is_pinned INTEGER NOT NULL DEFAULT 0,
              thread_section_id TEXT {{preview}}
            );
            """;
        await command.ExecuteNonQueryAsync();
        return fixture;
    }

    public async Task InsertThreadAsync(
        string id,
        string title,
        string threadSource,
        string source,
        bool archived,
        string preview,
        long updatedAtMs = 123_000,
        bool isPinned = false,
        string? sectionId = null)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO threads
              (id, rollout_path, cwd, title, archived, updated_at_ms, thread_source, source, is_pinned, thread_section_id, preview)
            VALUES
              ($id, $rollout, $cwd, $title, $archived, $updatedAt, $threadSource, $source, $isPinned, $sectionId, $preview);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$rollout", RolloutPath);
        command.Parameters.AddWithValue("$cwd", root);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$archived", archived ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", updatedAtMs);
        command.Parameters.AddWithValue("$threadSource", threadSource);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$isPinned", isPinned ? 1 : 0);
        command.Parameters.AddWithValue("$sectionId", (object?)sectionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$preview", preview);
        await command.ExecuteNonQueryAsync();
    }

    public async Task InsertSectionAsync(string id, string name)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO thread_sections (id, name) VALUES ($id, $name);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        await command.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(root, recursive: true);
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 3: Run the tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~SqliteThreadStoreTests
```

Expected: FAIL to compile because thread-store types do not exist.

- [ ] **Step 4: Implement the read-only store and schema check**

Create the model and interface:

```csharp
// Data/ThreadRecord.cs
using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Core.Data;

public sealed record ThreadRecord(
    string Id,
    string Title,
    string Cwd,
    DateTimeOffset UpdatedAt,
    string RolloutPath,
    ThreadGroupingInfo Grouping);

public enum CodexDataError { DatabaseMissing, FormatChanged, Unreadable }

public sealed class CodexDataException(CodexDataError error, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public CodexDataError Error { get; } = error;
}
```

```csharp
// Data/IThreadStore.cs
namespace CodexTaskMonitor.Core.Data;

public interface IThreadStore
{
    Task<IReadOnlyList<ThreadRecord>> ReadThreadsAsync(DateTimeOffset updatedAfter, CancellationToken cancellationToken);
}

public interface IThreadGroupingLookup
{
    Task<CodexTaskMonitor.Core.Sidebar.ThreadGroupingInfo?> FindGroupingAsync(string threadId, CancellationToken cancellationToken);
}
```

Create `Data/SqliteThreadStore.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace CodexTaskMonitor.Core.Data;

public sealed class SqliteThreadStore(string databasePath) : IThreadStore, IThreadGroupingLookup
{
    private static readonly string[] RequiredThreadColumns =
    [
        "id", "rollout_path", "cwd", "title", "archived", "updated_at_ms",
        "thread_source", "source", "preview", "is_pinned", "thread_section_id"
    ];
    private static readonly string[] RequiredSectionColumns = ["id", "name"];

    public async Task<IReadOnlyList<ThreadRecord>> ReadThreadsAsync(
        DateTimeOffset updatedAfter,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
            throw new CodexDataException(CodexDataError.DatabaseMissing, "Codex database is missing");

        try
        {
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await connection.OpenAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT t.id, t.title, t.cwd, t.updated_at_ms, t.rollout_path,
                       t.is_pinned, s.name
                FROM threads t
                LEFT JOIN thread_sections s ON s.id = t.thread_section_id
                WHERE t.archived = 0
                  AND t.preview <> ''
                  AND (COALESCE(t.thread_source, 'user') <> 'subagent' OR t.source = 'vscode')
                  AND t.updated_at_ms >= $updatedAfter
                ORDER BY t.updated_at_ms DESC;
                """;
            command.Parameters.AddWithValue("$updatedAfter", updatedAfter.ToUnixTimeMilliseconds());
            var records = new List<ThreadRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var cwd = reader.GetString(2);
                records.Add(new ThreadRecord(
                    reader.GetString(0), reader.GetString(1), cwd,
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)), reader.GetString(4),
                    new(reader.GetInt64(5) != 0, reader.IsDBNull(6) ? null : reader.GetString(6), cwd)));
            }
            return records;
        }
        catch (CodexDataException) { throw; }
        catch (SqliteException error)
        {
            throw new CodexDataException(CodexDataError.Unreadable, "Unable to read Codex database", error);
        }
    }

    public async Task<CodexTaskMonitor.Core.Sidebar.ThreadGroupingInfo?> FindGroupingAsync(string threadId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.is_pinned, s.name, t.cwd
            FROM threads t
            LEFT JOIN thread_sections s ON s.id = t.thread_section_id
            WHERE t.id = $threadId;
            """;
        command.Parameters.AddWithValue("$threadId", threadId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CodexTaskMonitor.Core.Sidebar.ThreadGroupingInfo(reader.GetInt64(0) != 0, reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureColumnsAsync(connection, "threads", RequiredThreadColumns, cancellationToken);
        await EnsureColumnsAsync(connection, "thread_sections", RequiredSectionColumns, cancellationToken);
    }

    private static async Task EnsureColumnsAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> required,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        var actual = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) actual.Add(reader.GetString(1));
        if (required.Any(column => !actual.Contains(column)))
            throw new CodexDataException(CodexDataError.FormatChanged, $"Codex {table} schema changed");
    }
}
```

- [ ] **Step 5: Add time filtering, pinned, and section assertions**

Append these complete tests; the Step 2 fixture already exposes the required optional arguments and section helper:

```csharp
[Fact]
public async Task UpdatedAfter_ExcludesOlderRows()
{
    await using var fixture = await CodexFixture.CreateAsync();
    await fixture.InsertThreadAsync("old", "Old", "user", "vscode", false, "visible", updatedAtMs: 123_000);
    var store = new SqliteThreadStore(fixture.DatabasePath);
    Assert.Empty(await store.ReadThreadsAsync(DateTimeOffset.FromUnixTimeMilliseconds(124_000), default));
}

[Fact]
public async Task PinnedAndSectionGrouping_AreReturnedAndCanBeLookedUp()
{
    await using var fixture = await CodexFixture.CreateAsync();
    await fixture.InsertSectionAsync("research", "Research");
    await fixture.InsertThreadAsync("pinned", "Pinned", "user", "vscode", false, "visible", isPinned: true);
    await fixture.InsertThreadAsync("sectioned", "Sectioned", "user", "vscode", false, "visible", sectionId: "research");
    var store = new SqliteThreadStore(fixture.DatabasePath);
    var records = await store.ReadThreadsAsync(DateTimeOffset.UnixEpoch, default);
    var pinned = records.Single(record => record.Id == "pinned");
    var sectioned = records.Single(record => record.Id == "sectioned");
    Assert.True(pinned.Grouping.IsPinned);
    Assert.Equal("Research", sectioned.Grouping.SectionName);
    Assert.Equal(sectioned.Grouping, await store.FindGroupingAsync(sectioned.Id, default));
}
```

The SQL parameters must remain bound values; do not interpolate fixture values into SQL text.

- [ ] **Step 6: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~SqliteThreadStoreTests
dotnet test windows/CodexTaskMonitor.sln
```

Expected: all thread-store tests and the full suite pass.

- [ ] **Step 7: Commit**

```powershell
git add windows/CodexTaskMonitor.Core/Data windows/CodexTaskMonitor.Tests/Data windows/CodexTaskMonitor.Tests/Fixtures
git commit -m "feat: read visible Codex threads on Windows"
```

### Task 5: Parse complete and appended rollout lifecycle events

**Files:**
- Create: `windows/CodexTaskMonitor.Core/Monitoring/RolloutParser.cs`
- Test: `windows/CodexTaskMonitor.Tests/Monitoring/RolloutParserTests.cs`

**Interfaces:**
- Consumes: `LifecycleEvent` and `LifecycleKind` from Task 2.
- Produces: `RolloutParser.LatestAsync(string, CancellationToken)` and `RolloutParser.LatestAfter(LifecycleEvent?, ReadOnlySpan<byte>)` for Task 6.

- [ ] **Step 1: Write failing parser tests**

Create `windows/CodexTaskMonitor.Tests/Monitoring/RolloutParserTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;

namespace CodexTaskMonitor.Tests.Monitoring;

public sealed class RolloutParserTests
{
    [Fact]
    public void CompleteTurn_ReturnsWaitingTerminalEvent()
    {
        var data = Encoding.UTF8.GetBytes("""
            {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-1","started_at":101}}
            {"type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1","started_at":101,"completed_at":102}}

            """);
        var item = RolloutParser.LatestAfter(null, data);
        Assert.Equal(LifecycleKind.Completed, item!.Kind);
        Assert.Equal("turn-1", item.TurnId);
    }

    [Fact]
    public void IncompleteTrailingJson_IsIgnored()
    {
        var data = Encoding.UTF8.GetBytes(
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\",\"started_at\":101}}\n{\"type\":");
        Assert.Equal(LifecycleKind.Started, RolloutParser.LatestAfter(null, data)!.Kind);
    }

    [Fact]
    public void NewlineTerminatedMalformedLifecycleLine_ReportsFormatChange()
    {
        var data = Encoding.UTF8.GetBytes("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"\n");
        var error = Assert.Throws<CodexDataException>(() => RolloutParser.LatestAfter(null, data));
        Assert.Equal(CodexDataError.FormatChanged, error.Error);
    }

    [Fact]
    public void LateTerminalForOldTurn_DoesNotReplaceNewerRunningTurn()
    {
        var current = new LifecycleEvent(LifecycleKind.Started, "turn-b", DateTimeOffset.FromUnixTimeSeconds(102), null);
        var appended = Encoding.UTF8.GetBytes(
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-a\",\"started_at\":101,\"completed_at\":103}}\n");
        Assert.Equal(current, RolloutParser.LatestAfter(current, appended));
    }
}
```

- [ ] **Step 2: Run the parser tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~RolloutParserTests
```

Expected: FAIL to compile because `RolloutParser` does not exist.

- [ ] **Step 3: Implement the complete-line parser**

Create `windows/CodexTaskMonitor.Core/Monitoring/RolloutParser.cs`:

```csharp
using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Core.Data;

namespace CodexTaskMonitor.Core.Monitoring;

public static class RolloutParser
{
    private static readonly string[] Markers = ["task_started", "task_complete", "turn_aborted"];

    public static async Task<LifecycleEvent?> LatestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var endsWithNewline = stream.Length == 0;
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            endsWithNewline = stream.ReadByte() == (byte)'\n';
            stream.Seek(0, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        LifecycleEvent? current = null;
        var pending = await reader.ReadLineAsync(cancellationToken);
        while (pending is not null)
        {
            var next = await reader.ReadLineAsync(cancellationToken);
            if (next is not null || endsWithNewline) current = ApplyLine(current, pending);
            pending = next;
        }
        return current;
    }

    public static LifecycleEvent? LatestAfter(LifecycleEvent? current, ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        var endsWithNewline = text.Length == 0 || text[^1] == '\n';
        var lines = text.Split('\n');
        var limit = endsWithNewline ? lines.Length : lines.Length - 1;
        for (var index = 0; index < limit; index++) current = ApplyLine(current, lines[index].TrimEnd('\r'));
        return current;
    }

    private static LifecycleEvent? ApplyLine(LifecycleEvent? current, string line)
    {
        if (string.IsNullOrEmpty(line) || !Markers.Any(line.Contains)) return current;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var eventType) || eventType.GetString() != "event_msg") return current;
            var payload = root.GetProperty("payload");
            var kindText = payload.GetProperty("type").GetString();
            var kind = kindText switch
            {
                "task_started" => LifecycleKind.Started,
                "task_complete" => LifecycleKind.Completed,
                "turn_aborted" => LifecycleKind.Aborted,
                _ => (LifecycleKind?)null
            };
            if (kind is null) return current;
            var turnId = payload.GetProperty("turn_id").GetString();
            var started = payload.GetProperty("started_at").GetDouble();
            if (string.IsNullOrWhiteSpace(turnId)) throw new JsonException("turn_id is missing");
            var startedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(started * 1000));
            if (kind == LifecycleKind.Started)
                return new LifecycleEvent(kind.Value, turnId, startedAt, null);

            var completed = payload.GetProperty("completed_at").GetDouble();
            var terminal = new LifecycleEvent(
                kind.Value, turnId, startedAt,
                DateTimeOffset.FromUnixTimeMilliseconds((long)(completed * 1000)));
            return current is null || current.TurnId == turnId ? terminal : current;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new CodexDataException(CodexDataError.FormatChanged, "Codex rollout format changed", error);
        }
    }
}
```

- [ ] **Step 4: Add file-stream, aborted, unrelated-event, and large-line tests**

Append these concrete fixtures and tests:

```csharp
[Fact]
public async Task FileStream_ReturnsLatestAbortedTurnAndIgnoresUnrelatedRows()
{
    var path = Path.GetTempFileName();
    await File.WriteAllTextAsync(path, """
        {"type":"turn_context","payload":{"type":"task_started is text only"}}
        {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-a","started_at":101}}
        {"type":"response_item","payload":{"type":"message"}}
        {"type":"event_msg","payload":{"type":"turn_aborted","turn_id":"turn-a","started_at":101,"completed_at":102}}

        """);
    Assert.Equal(LifecycleKind.Aborted, (await RolloutParser.LatestAsync(path, default))!.Kind);
}

[Fact]
public void NewerStartedTurn_WinsOverLateOldTerminal()
{
    var data = Encoding.UTF8.GetBytes("""
        {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-a","started_at":101}}
        {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-b","started_at":102}}
        {"type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-a","started_at":101,"completed_at":103}}

        """);
    Assert.Equal("turn-b", RolloutParser.LatestAfter(null, data)!.TurnId);
}

[Fact]
public void UnrelatedLineLargerThanTwoChunks_DoesNotTruncateLifecycleSearch()
{
    var started = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\",\"started_at\":101}}\n";
    var unrelated = JsonSerializer.Serialize(new { type = "agent_reasoning", payload = new string('x', 130 * 1024) }) + "\n";
    var data = Encoding.UTF8.GetBytes(started + unrelated);
    Assert.Equal("turn-1", RolloutParser.LatestAfter(null, data)!.TurnId);
}
```

- [ ] **Step 5: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~RolloutParserTests
dotnet test windows/CodexTaskMonitor.sln
```

Expected: parser tests and full suite pass.

- [ ] **Step 6: Commit**

```powershell
git add windows/CodexTaskMonitor.Core/Monitoring/RolloutParser.cs windows/CodexTaskMonitor.Tests/Monitoring/RolloutParserTests.cs
git commit -m "feat: parse Codex rollout lifecycle events"
```

### Task 6: Add incremental rollout caching and task scans

**Files:**
- Create: `windows/CodexTaskMonitor.Core/Monitoring/ITaskMonitor.cs`
- Create: `windows/CodexTaskMonitor.Core/Monitoring/TaskMonitor.cs`
- Test: `windows/CodexTaskMonitor.Tests/Monitoring/TaskMonitorTests.cs`

**Interfaces:**
- Consumes: `IThreadStore`, `ThreadRecord`, `RolloutParser`, `TaskStateResolver`, and `MonitorItem`.
- Produces: `ITaskMonitor.CurrentlyRunningTurnIdsAsync` and `ITaskMonitor.ScanAsync` for Task 8.

- [ ] **Step 1: Write failing scan and cache tests**

Create `windows/CodexTaskMonitor.Tests/Monitoring/TaskMonitorTests.cs` with an in-memory fake `IThreadStore` and temporary rollout file:

```csharp
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Tests.Monitoring;

public sealed class TaskMonitorTests
{
    [Fact]
    public async Task AppendedCompletion_ChangesRunningItemToWaiting()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path,
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\",\"started_at\":101}}\n");
        var store = new FakeThreadStore(new ThreadRecord(
            "thread-1", "Visible", @"C:\work\demo", DateTimeOffset.FromUnixTimeSeconds(123), path,
            new ThreadGroupingInfo(false, null, @"C:\work\demo")));
        var monitor = new TaskMonitor(store);
        var options = new MonitorScanOptions(DateTimeOffset.FromUnixTimeSeconds(100), [], [], []);

        Assert.Equal(TaskState.Running, (await monitor.ScanAsync(options, default)).Items.Single().State);
        await File.AppendAllTextAsync(path,
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-1\",\"started_at\":101,\"completed_at\":102}}\n");
        Assert.Equal(TaskState.Waiting, (await monitor.ScanAsync(options, default)).Items.Single().State);
    }

    [Fact]
    public async Task ExactDismissedItem_DoesNotHideSameTurnInAnotherThread()
    {
        var fixture = await MonitorFixture.CreateTwoThreadsWithSameTurnAsync();
        var result = await fixture.Monitor.ScanAsync(
            new(DateTimeOffset.FromUnixTimeSeconds(100), [], [], ["thread-a:turn-1"]), default);
        Assert.Equal(["thread-b"], result.Items.Select(item => item.ThreadId));
    }
}
```

Add these helpers inside `TaskMonitorTests`; the two threads deliberately use separate rollout files with the same turn ID:

```csharp
private sealed class FakeThreadStore(params ThreadRecord[] records) : IThreadStore
{
    public Task<IReadOnlyList<ThreadRecord>> ReadThreadsAsync(DateTimeOffset updatedAfter, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<ThreadRecord>>(
            records.Where(record => record.UpdatedAt >= updatedAfter).ToArray());
}

private sealed record MonitorFixture(TaskMonitor Monitor)
{
    public static async Task<MonitorFixture> CreateTwoThreadsWithSameTurnAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"monitor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var records = new List<ThreadRecord>();
        foreach (var id in new[] { "thread-a", "thread-b" })
        {
            var path = Path.Combine(root, id + ".jsonl");
            await File.WriteAllTextAsync(path,
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-1\",\"started_at\":101,\"completed_at\":102}}\n");
            records.Add(new ThreadRecord(id, id, root, DateTimeOffset.FromUnixTimeSeconds(123), path,
                new ThreadGroupingInfo(false, null, root)));
        }
        return new MonitorFixture(new TaskMonitor(new FakeThreadStore(records.ToArray())));
    }
}
```

- [ ] **Step 2: Run focused tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~TaskMonitorTests
```

Expected: FAIL to compile because `TaskMonitor`, options, and scan result types do not exist.

- [ ] **Step 3: Define the monitor contract**

Create `windows/CodexTaskMonitor.Core/Monitoring/ITaskMonitor.cs`:

```csharp
namespace CodexTaskMonitor.Core.Monitoring;

public sealed record MonitorScanOptions(
    DateTimeOffset Baseline,
    IReadOnlySet<string> AdoptedTurnIds,
    IReadOnlySet<string> DismissedTurnIds,
    IReadOnlySet<string> DismissedItemIds);

public sealed record MonitorScanResult(IReadOnlyList<MonitorItem> Items, int UnreadableRolloutCount);

public interface ITaskMonitor
{
    Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken cancellationToken);
    Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement cache-aware task monitoring**

Create `windows/CodexTaskMonitor.Core/Monitoring/TaskMonitor.cs`:

```csharp
using CodexTaskMonitor.Core.Data;

namespace CodexTaskMonitor.Core.Monitoring;

public sealed class TaskMonitor(IThreadStore threadStore) : ITaskMonitor
{
    private readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken token)
    {
        var (events, unreadable) = await LatestEventsAsync(since, token);
        if (unreadable != 0) throw new CodexDataException(CodexDataError.Unreadable, "Some rollouts are unreadable");
        return events.Values.Where(item => item.Event.Kind == LifecycleKind.Started)
            .Select(item => item.Event.TurnId).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken token)
    {
        var oldest = options.Baseline.AddHours(-1);
        var (events, unreadable) = await LatestEventsAsync(oldest, token);
        var items = events.Values.Select(pair =>
        {
            var state = TaskStateResolver.Resolve(pair.Event, options.Baseline, options.AdoptedTurnIds, options.DismissedTurnIds);
            if (state is null) return null;
            var projectName = Path.GetFileName(pair.Thread.Cwd.TrimEnd(Path.DirectorySeparatorChar));
            var item = new MonitorItem(pair.Thread.Id, pair.Event.TurnId,
                string.IsNullOrEmpty(pair.Thread.Title) ? "New chat" : pair.Thread.Title,
                pair.Thread.Cwd, projectName, pair.Event.ActivityDate, state.Value);
            return options.DismissedItemIds.Contains(item.Id) ? null : item;
        }).OfType<MonitorItem>().OrderByDescending(item => item.EventDate).ToArray();
        return new MonitorScanResult(items, unreadable);
    }

    private async Task<(Dictionary<string, ThreadEvent> Events, int Unreadable)> LatestEventsAsync(
        DateTimeOffset updatedAfter,
        CancellationToken token)
    {
        var events = new Dictionary<string, ThreadEvent>(StringComparer.Ordinal);
        var unreadable = 0;
        foreach (var thread in await threadStore.ReadThreadsAsync(updatedAfter, token))
        {
            try
            {
                var item = await EventForAsync(thread.RolloutPath, token);
                if (item is not null) events[thread.Id] = new ThreadEvent(thread, item);
            }
            catch (CodexDataException error) when (error.Error != CodexDataError.FormatChanged)
            {
                unreadable++;
                if (cache.TryGetValue(thread.RolloutPath, out var cached) && cached.Event is not null)
                    events[thread.Id] = new ThreadEvent(thread, cached.Event);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                unreadable++;
                if (cache.TryGetValue(thread.RolloutPath, out var cached) && cached.Event is not null)
                    events[thread.Id] = new ThreadEvent(thread, cached.Event);
            }
        }
        if (events.Count == 0 && unreadable > 0)
            throw new CodexDataException(CodexDataError.Unreadable, "All relevant rollouts are unreadable");
        return (events, unreadable);
    }

    private async Task<LifecycleEvent?> EventForAsync(string path, CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new CodexDataException(CodexDataError.Unreadable, "Rollout is missing");
        var signature = new FileSignature(info.LastWriteTimeUtc, info.Length);
        if (cache.TryGetValue(path, out var cached) && cached.Signature == signature) return cached.Event;

        LifecycleEvent? item;
        byte[] trailing;
        if (cached is not null && info.Length > cached.ProcessedSize)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(cached.ProcessedSize, SeekOrigin.Begin);
            var appended = new byte[checked((int)(info.Length - cached.ProcessedSize))];
            await stream.ReadExactlyAsync(appended, token);
            var combined = new byte[cached.TrailingFragment.Length + appended.Length];
            cached.TrailingFragment.CopyTo(combined, 0);
            appended.CopyTo(combined, cached.TrailingFragment.Length);
            item = RolloutParser.LatestAfter(cached.Event, combined);
            trailing = TrailingFragment(combined);
        }
        else
        {
            item = await RolloutParser.LatestAsync(path, token);
            trailing = await ReadTrailingFragmentAsync(path, token);
        }
        cache[path] = new CacheEntry(signature, item, info.Length, trailing);
        return item;
    }

    private static byte[] TrailingFragment(byte[] data)
    {
        var newline = Array.LastIndexOf(data, (byte)'\n');
        return newline < 0 ? data : data[(newline + 1)..];
    }

    private static async Task<byte[]> ReadTrailingFragmentAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var offset = stream.Length;
        var laterChunks = new List<byte[]>();
        while (offset > 0)
        {
            var count = (int)Math.Min(64 * 1024, offset);
            offset -= count;
            stream.Seek(offset, SeekOrigin.Begin);
            var chunk = new byte[count];
            await stream.ReadExactlyAsync(chunk, token);
            var newline = Array.LastIndexOf(chunk, (byte)'\n');
            if (newline >= 0)
            {
                using var result = new MemoryStream();
                result.Write(chunk, newline + 1, chunk.Length - newline - 1);
                foreach (var later in laterChunks.AsEnumerable().Reverse()) result.Write(later);
                return result.ToArray();
            }
            laterChunks.Add(chunk);
        }
        using var complete = new MemoryStream();
        foreach (var chunk in laterChunks.AsEnumerable().Reverse()) complete.Write(chunk);
        return complete.ToArray();
    }

    private sealed record CacheEntry(FileSignature Signature, LifecycleEvent? Event, long ProcessedSize, byte[] TrailingFragment);
    private readonly record struct FileSignature(DateTime LastWriteTimeUtc, long Size);
    private sealed record ThreadEvent(ThreadRecord Thread, LifecycleEvent Event);
}
```

- [ ] **Step 5: Add first-scan, partial-append, truncation, unreadable-cache, ordering, and format-change tests**

Append these exact tests to `TaskMonitorTests.cs`:

```csharp
[Fact]
public async Task FirstScan_ReturnsCurrentlyRunningTurnIds()
{
    var path = Path.GetTempFileName();
    await File.WriteAllTextAsync(path,
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-running\",\"started_at\":101}}\n");
    var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));
    Assert.Equal(["turn-running"], await monitor.CurrentlyRunningTurnIdsAsync(DateTimeOffset.FromUnixTimeSeconds(100), default));
}

[Fact]
public async Task PartialAppendLargerThanSixtyFourKiB_IsRetriedFromItsTrueStart()
{
    var path = Path.GetTempFileName();
    var started = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\",\"started_at\":101}}\n";
    var partialTerminal = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-1\",\"started_at\":101,\"completed_at\":102,\"padding\":\"" + new string('x', 70 * 1024);
    await File.WriteAllTextAsync(path, started + partialTerminal);
    var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));
    var options = Options();
    Assert.Equal(TaskState.Running, (await monitor.ScanAsync(options, default)).Items.Single().State);

    await File.AppendAllTextAsync(path, "\"}}\n");
    Assert.Equal(TaskState.Waiting, (await monitor.ScanAsync(options, default)).Items.Single().State);
}

[Fact]
public async Task TruncatedFile_IsFullyReparsed()
{
    var path = Path.GetTempFileName();
    await File.WriteAllTextAsync(path,
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-before\",\"started_at\":101,\"padding\":\"" + new string('x', 1000) + "\"}}\n");
    var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));
    await monitor.ScanAsync(Options(), default);

    await File.WriteAllTextAsync(path,
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-after-truncate\",\"started_at\":103}}\n");
    Assert.Equal("turn-after-truncate", (await monitor.ScanAsync(Options(), default)).Items.Single().TurnId);
}

[Fact]
public async Task TemporarilyMissingRollout_UsesCacheAndReportsCount()
{
    var path = Path.GetTempFileName();
    await File.WriteAllTextAsync(path,
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\",\"started_at\":101}}\n");
    var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));
    await monitor.ScanAsync(Options(), default);
    File.Delete(path);

    var result = await monitor.ScanAsync(Options(), default);
    Assert.Single(result.Items);
    Assert.Equal(1, result.UnreadableRolloutCount);
}

[Fact]
public async Task Items_AreSortedByLifecycleActivityDescending()
{
    var olderPath = Path.GetTempFileName();
    var newerPath = Path.GetTempFileName();
    await File.WriteAllTextAsync(olderPath,
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"old\",\"started_at\":101}}\n");
    await File.WriteAllTextAsync(newerPath,
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"new\",\"started_at\":102}}\n");
    var monitor = new TaskMonitor(new FakeThreadStore(Record("older", olderPath), Record("newer", newerPath)));
    Assert.Equal(["newer", "older"], (await monitor.ScanAsync(Options(), default)).Items.Select(item => item.ThreadId));
}

[Fact]
public async Task CompleteLifecycleLineWithMissingFields_ReportsFormatChange()
{
    var path = Path.GetTempFileName();
    await File.WriteAllTextAsync(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n");
    var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));
    var error = await Assert.ThrowsAsync<CodexDataException>(() => monitor.ScanAsync(Options(), default));
    Assert.Equal(CodexDataError.FormatChanged, error.Error);
}

private static ThreadRecord Record(string id, string path) =>
    new(id, id, @"C:\work", DateTimeOffset.FromUnixTimeSeconds(123), path,
        new ThreadGroupingInfo(false, null, @"C:\work"));

private static MonitorScanOptions Options() =>
    new(DateTimeOffset.FromUnixTimeSeconds(100), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
```

All paths in these tests are temporary and the monitor receives an injected `IThreadStore`; automated tests never open the user's real `%USERPROFILE%\.codex` data.

- [ ] **Step 6: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~TaskMonitorTests
dotnet test windows/CodexTaskMonitor.sln
```

Expected: focused tests and full suite pass.

- [ ] **Step 7: Commit**

```powershell
git add windows/CodexTaskMonitor.Core/Monitoring windows/CodexTaskMonitor.Tests/Monitoring
git commit -m "feat: monitor Codex rollouts incrementally"
```

### Task 7: Persist baseline and handled items atomically

**Files:**
- Create: `windows/CodexTaskMonitor.Core/Preferences/MonitorPreferences.cs`
- Create: `windows/CodexTaskMonitor.Core/Preferences/IMonitorPreferencesStore.cs`
- Create: `windows/CodexTaskMonitor.Core/Preferences/MonitorPreferencesStore.cs`
- Test: `windows/CodexTaskMonitor.Tests/Preferences/MonitorPreferencesStoreTests.cs`

**Interfaces:**
- Produces: immutable `MonitorPreferences`, `IMonitorPreferencesStore.LoadAsync`, and `SaveAsync` for Task 8.
- Consumes: path supplied by `CodexDataPaths`.

- [ ] **Step 1: Write failing persistence tests**

Create `windows/CodexTaskMonitor.Tests/Preferences/MonitorPreferencesStoreTests.cs`:

```csharp
using CodexTaskMonitor.Core.Preferences;

namespace CodexTaskMonitor.Tests.Preferences;

public sealed class MonitorPreferencesStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsExactSets()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var store = new MonitorPreferencesStore(path);
        var expected = new MonitorPreferences(
            DateTimeOffset.FromUnixTimeSeconds(100), ["adopted"], ["legacy"], ["thread:turn"], 120, 240, false);

        await store.SaveAsync(expected, default);
        var actual = await store.LoadAsync(default);

        Assert.Equal(expected.Baseline, actual.Baseline);
        Assert.True(expected.AdoptedTurnIds.SetEquals(actual.AdoptedTurnIds));
        Assert.True(expected.DismissedItemIds.SetEquals(actual.DismissedItemIds));
        Assert.Equal((120d, 240d), (actual.WindowLeft, actual.WindowTop));
        Assert.Equal(false, actual.LaunchAtLoginEnabled);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task MissingFile_ReturnsEmptyPreferences()
    {
        var store = new MonitorPreferencesStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));
        Assert.Equal(MonitorPreferences.Empty, await store.LoadAsync(default));
    }
}
```

- [ ] **Step 2: Run focused tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~MonitorPreferencesStoreTests
```

Expected: FAIL to compile because preference types do not exist.

- [ ] **Step 3: Implement immutable preferences and atomic storage**

Create the three production files:

```csharp
// Preferences/MonitorPreferences.cs
namespace CodexTaskMonitor.Core.Preferences;

public sealed record MonitorPreferences(
    DateTimeOffset? Baseline,
    HashSet<string> AdoptedTurnIds,
    HashSet<string> DismissedTurnIds,
    HashSet<string> DismissedItemIds,
    double? WindowLeft,
    double? WindowTop,
    bool? LaunchAtLoginEnabled)
{
    public static MonitorPreferences Empty { get; } = new(null, [], [], [], null, null, null);

    public MonitorPreferences Initialize(DateTimeOffset baseline, IEnumerable<string> adopted) =>
        Baseline is null ? this with { Baseline = baseline, AdoptedTurnIds = adopted.ToHashSet(StringComparer.Ordinal) } : this;

    public MonitorPreferences Dismiss(string itemId)
    {
        var updated = DismissedItemIds.ToHashSet(StringComparer.Ordinal);
        updated.Add(itemId);
        return this with { DismissedItemIds = updated };
    }

    public MonitorPreferences WithWindowPosition(double left, double top) =>
        this with { WindowLeft = left, WindowTop = top };

    public MonitorPreferences WithLaunchAtLogin(bool enabled) =>
        this with { LaunchAtLoginEnabled = enabled };
}
```

```csharp
// Preferences/IMonitorPreferencesStore.cs
namespace CodexTaskMonitor.Core.Preferences;

public interface IMonitorPreferencesStore
{
    Task<MonitorPreferences> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(MonitorPreferences preferences, CancellationToken cancellationToken);
}
```

```csharp
// Preferences/MonitorPreferencesStore.cs
using System.Text.Json;

namespace CodexTaskMonitor.Core.Preferences;

public sealed class MonitorPreferencesStore(string path) : IMonitorPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<MonitorPreferences> LoadAsync(CancellationToken token)
    {
        if (!File.Exists(path)) return MonitorPreferences.Empty;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var value = await JsonSerializer.DeserializeAsync<MonitorPreferences>(stream, Options, token);
        return value ?? MonitorPreferences.Empty;
    }

    public async Task SaveAsync(MonitorPreferences preferences, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, preferences, Options, token);
            await stream.FlushAsync(token);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
```

- [ ] **Step 4: Add malformed-file and exact-dismissal tests**

Append these tests to `MonitorPreferencesStoreTests.cs`:

```csharp
[Fact]
public async Task MalformedJson_IsReportedInsteadOfDiscarded()
{
    var path = Path.GetTempFileName();
    await File.WriteAllTextAsync(path, "{not-json");
    var store = new MonitorPreferencesStore(path);
    await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync(default));
}

[Fact]
public void Dismiss_UsesTheExactThreadAndTurnKey()
{
    var updated = MonitorPreferences.Empty.Dismiss("thread-a:turn");
    Assert.Contains("thread-a:turn", updated.DismissedItemIds);
    Assert.DoesNotContain("thread-b:turn", updated.DismissedItemIds);
}
```

- [ ] **Step 5: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~MonitorPreferencesStoreTests
dotnet test windows/CodexTaskMonitor.sln
```

Expected: focused tests and full suite pass.

- [ ] **Step 6: Commit**

```powershell
git add windows/CodexTaskMonitor.Core/Preferences windows/CodexTaskMonitor.Tests/Preferences
git commit -m "feat: persist Windows monitor preferences"
```

### Task 8: Build the WPF floating panel and polling view model

**Files:**
- Modify: `windows/CodexTaskMonitor.Windows/App.xaml`
- Modify: `windows/CodexTaskMonitor.Windows/App.xaml.cs`
- Modify: `windows/CodexTaskMonitor.Windows/MainWindow.xaml`
- Modify: `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs`
- Create: `windows/CodexTaskMonitor.Windows/ViewModels/AsyncCommand.cs`
- Create: `windows/CodexTaskMonitor.Windows/ViewModels/MonitorItemViewModel.cs`
- Create: `windows/CodexTaskMonitor.Windows/ViewModels/MonitorViewModel.cs`
- Test: `windows/CodexTaskMonitor.Tests/ViewModels/MonitorViewModelTests.cs`

**Interfaces:**
- Consumes: `ITaskMonitor`, `IMonitorPreferencesStore`, `MonitorPanelLayout`, and `MonitorListUpdate`.
- Produces: `MonitorViewModel`, `IThreadActivationService`, `IStartupRegistration`, and `ICodexLaunchTimeProvider` contracts for Tasks 9 and 12.

- [ ] **Step 1: Write failing view-model tests with fakes**

Create `windows/CodexTaskMonitor.Tests/ViewModels/MonitorViewModelTests.cs`:

```csharp
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Tests.ViewModels;

public sealed class MonitorViewModelTests
{
    [Fact]
    public async Task Start_AdoptsRecentRunningTurnsAndPublishesItems()
    {
        var monitor = new FakeTaskMonitor(["turn-adopted"],
            [new MonitorItem("thread", "turn-adopted", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Running)]);
        var preferences = new FakePreferencesStore(MonitorPreferences.Empty);
        var viewModel = new MonitorViewModel(
            monitor, preferences, new FakeActivation(), new FakeStartup(),
            new FakeLaunchTime(DateTimeOffset.UtcNow.AddMinutes(-10)), TimeProvider.System);

        await viewModel.StartAsync(startPollingLoop: false, default);

        Assert.Single(viewModel.Items);
        Assert.Contains("turn-adopted", preferences.Value.AdoptedTurnIds);
        Assert.True(viewModel.PanelHeight >= 48);
    }

    [Fact]
    public async Task Dismiss_PersistsExactWaitingItemAndRefreshes()
    {
        var item = new MonitorItem("thread", "turn", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting);
        var monitor = new FakeTaskMonitor([], [item]);
        var preferences = new FakePreferencesStore(new(DateTimeOffset.UtcNow.AddHours(-1), [], [], [], null, null, true));
        var viewModel = new MonitorViewModel(monitor, preferences, new FakeActivation(), new FakeStartup(), new FakeLaunchTime(null), TimeProvider.System);
        await viewModel.StartAsync(false, default);

        await viewModel.DismissAsync(viewModel.Items.Single(), default);

        Assert.Contains("thread:turn", preferences.Value.DismissedItemIds);
    }

    [Fact]
    public async Task ToggleStartup_PersistsDisabledChoiceAcrossRestarts()
    {
        var preferences = new FakePreferencesStore(MonitorPreferences.Empty);
        var startup = new FakeStartup(enabled: true);
        var viewModel = new MonitorViewModel(
            new FakeTaskMonitor([], []), preferences, new FakeActivation(), startup,
            new FakeLaunchTime(null), TimeProvider.System);
        await viewModel.StartAsync(false, default);

        await viewModel.ToggleStartupAsync(default);

        Assert.False(viewModel.IsStartupEnabled);
        Assert.Equal(false, preferences.Value.LaunchAtLoginEnabled);
        Assert.False(startup.IsEnabled);
    }
}
```

Add these focused fakes inside `MonitorViewModelTests`, immediately before its final `}`; they never touch the shell or registry:

```csharp
private sealed class FakeTaskMonitor(
    IReadOnlySet<string> adopted,
    IReadOnlyList<MonitorItem> items) : ITaskMonitor
{
    public Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken token) =>
        Task.FromResult(adopted);
    public Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken token) =>
        Task.FromResult(new MonitorScanResult(items, 0));
}

private sealed class FakePreferencesStore(MonitorPreferences initial) : IMonitorPreferencesStore
{
    public MonitorPreferences Value { get; private set; } = initial;
    public Task<MonitorPreferences> LoadAsync(CancellationToken token) => Task.FromResult(Value);
    public Task SaveAsync(MonitorPreferences value, CancellationToken token) { Value = value; return Task.CompletedTask; }
}

private sealed class FakeActivation : IThreadActivationService
{
    public List<string> ActivatedIds { get; } = [];
    public string? Result { get; set; }
    public Task<string?> ActivateAsync(MonitorItem item, CancellationToken token)
    {
        ActivatedIds.Add(item.Id);
        return Task.FromResult(Result);
    }
}

private sealed class FakeStartup(bool enabled = false) : IStartupRegistration
{
    public bool IsEnabled { get; private set; } = enabled;
    public void SetEnabled(bool value) => IsEnabled = value;
}

private sealed class FakeLaunchTime(DateTimeOffset? value) : ICodexLaunchTimeProvider
{
    public DateTimeOffset? GetLaunchTime() => value;
}
```

- [ ] **Step 2: Run focused tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~MonitorViewModelTests
```

Expected: FAIL to compile because view-model and service contracts do not exist.

- [ ] **Step 3: Implement the command and row projection**

Create `ViewModels/AsyncCommand.cs` and `MonitorItemViewModel.cs`:

```csharp
using System.Windows.Input;

namespace CodexTaskMonitor.Windows.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
```

```csharp
using CodexTaskMonitor.Core.Monitoring;

namespace CodexTaskMonitor.Windows.ViewModels;

public sealed record MonitorItemViewModel(MonitorItem Item)
{
    public string Id => Item.Id;
    public string Title => Item.Title;
    public string ProjectName => Item.ProjectName;
    public string StateText => Item.State == TaskState.Running ? "运行中" : "等待处理";
    public string DotColor => Item.State == TaskState.Running ? "#3B82F6" : "#22C55E";
    public bool CanDismiss => Item.State == TaskState.Waiting;
}
```

- [ ] **Step 4: Implement the view-model contracts and polling loop**

Create `ViewModels/MonitorViewModel.cs` with these public contracts and behavior:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexTaskMonitor.Core;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;

namespace CodexTaskMonitor.Windows.ViewModels;

public interface IThreadActivationService { Task<string?> ActivateAsync(MonitorItem item, CancellationToken token); }
public interface IStartupRegistration { bool IsEnabled { get; } void SetEnabled(bool enabled); }
public interface ICodexLaunchTimeProvider { DateTimeOffset? GetLaunchTime(); }

public sealed class MonitorViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ITaskMonitor monitor;
    private readonly IMonitorPreferencesStore preferenceStore;
    private readonly IThreadActivationService activation;
    private readonly IStartupRegistration startup;
    private readonly ICodexLaunchTimeProvider launchTime;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly DateTimeOffset firstAttemptBaseline;
    private MonitorPreferences preferences = MonitorPreferences.Empty;
    private string? errorMessage;
    private Task? polling;
    private TimeSpan nextPollDelay = TimeSpan.FromSeconds(2);

    public ObservableCollection<MonitorItemViewModel> Items { get; } = [];
    public string? ErrorMessage { get => errorMessage; private set { errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(PanelHeight)); } }
    public bool HasError => ErrorMessage is not null;
    public double PanelHeight => MonitorPanelLayout.Height(Items.Count, HasError);
    public double? SavedWindowLeft => preferences.WindowLeft;
    public double? SavedWindowTop => preferences.WindowTop;
    public bool IsStartupEnabled => preferences.LaunchAtLoginEnabled ?? startup.IsEnabled;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ToggleStartupCommand { get; }
    public AsyncCommand QuitCommand { get; }
    public event EventHandler? QuitRequested;
    public event EventHandler<string>? ItemInserted;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MonitorViewModel(ITaskMonitor monitor, IMonitorPreferencesStore preferenceStore,
        IThreadActivationService activation, IStartupRegistration startup,
        ICodexLaunchTimeProvider launchTime, TimeProvider time)
    {
        this.monitor = monitor; this.preferenceStore = preferenceStore; this.activation = activation;
        this.startup = startup; this.launchTime = launchTime; this.time = time;
        firstAttemptBaseline = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        RefreshCommand = new AsyncCommand(() => RefreshAsync(lifetime.Token));
        ToggleStartupCommand = new AsyncCommand(() => ToggleStartupAsync(lifetime.Token));
        QuitCommand = new AsyncCommand(() => { QuitRequested?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; });
    }

    public async Task StartAsync(bool startPollingLoop, CancellationToken token)
    {
        preferences = await preferenceStore.LoadAsync(token);
        // The installer expresses the first-run choice through the existing HKCU value.
        var launchAtLogin = preferences.LaunchAtLoginEnabled ?? startup.IsEnabled;
        startup.SetEnabled(launchAtLogin);
        if (preferences.LaunchAtLoginEnabled is null)
        {
            preferences = preferences.WithLaunchAtLogin(launchAtLogin);
            await preferenceStore.SaveAsync(preferences, token);
        }
        OnPropertyChanged(nameof(IsStartupEnabled));
        await RefreshAsync(token);
        if (startPollingLoop) polling = PollAsync(lifetime.Token);
    }

    public async Task ToggleStartupAsync(CancellationToken token)
    {
        var enabled = !IsStartupEnabled;
        startup.SetEnabled(enabled);
        preferences = preferences.WithLaunchAtLogin(enabled);
        await preferenceStore.SaveAsync(preferences, token);
        OnPropertyChanged(nameof(IsStartupEnabled));
    }

    public async Task OpenAsync(MonitorItemViewModel item, CancellationToken token) =>
        ErrorMessage = await activation.ActivateAsync(item.Item, token);

    public async Task DismissAsync(MonitorItemViewModel item, CancellationToken token)
    {
        if (!item.CanDismiss) return;
        preferences = preferences.Dismiss(item.Id);
        await preferenceStore.SaveAsync(preferences, token);
        await RefreshAsync(token);
    }

    public async Task SaveWindowPositionAsync(double left, double top, CancellationToken token)
    {
        preferences = preferences.WithWindowPosition(left, top);
        await preferenceStore.SaveAsync(preferences, token);
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        if (!await refreshGate.WaitAsync(0, token)) return;
        try
        {
            if (preferences.Baseline is null)
            {
                var hourAgo = firstAttemptBaseline.AddHours(-1);
                var activeSince = launchTime.GetLaunchTime() is { } launched && launched > hourAgo ? launched : hourAgo;
                var adopted = await Task.Run(
                    () => monitor.CurrentlyRunningTurnIdsAsync(activeSince, token), token);
                preferences = preferences.Initialize(firstAttemptBaseline, adopted);
                await preferenceStore.SaveAsync(preferences, token);
            }
            var scanOptions = new MonitorScanOptions(
                preferences.Baseline!.Value, preferences.AdoptedTurnIds,
                preferences.DismissedTurnIds, preferences.DismissedItemIds);
            var result = await Task.Run(() => monitor.ScanAsync(scanOptions, token), token);
            var oldIds = Items.Select(item => item.Id).ToArray();
            var nextItems = result.Items.Select(item => new MonitorItemViewModel(item)).ToArray();
            var insertedId = MonitorListUpdate.InsertedId(oldIds, nextItems.Select(item => item.Id).ToArray());
            if (!Items.SequenceEqual(nextItems))
            {
                Items.Clear();
                foreach (var item in nextItems) Items.Add(item);
                if (insertedId is not null) ItemInserted?.Invoke(this, insertedId);
            }
            nextPollDelay = TimeSpan.FromSeconds(2);
            ErrorMessage = result.UnreadableRolloutCount == 0 ? null : $"{result.UnreadableRolloutCount} 个任务暂时无法读取";
            OnPropertyChanged(nameof(PanelHeight));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            nextPollDelay = error is CodexTaskMonitor.Core.Data.CodexDataException
                { Error: CodexTaskMonitor.Core.Data.CodexDataError.DatabaseMissing }
                ? TimeSpan.FromSeconds(10)
                : TimeSpan.FromSeconds(2);
            ErrorMessage = UserMessage(error);
        }
        finally { refreshGate.Release(); }
    }

    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(nextPollDelay, time, token);
                await RefreshAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static string UserMessage(Exception error) => error switch
    {
        CodexTaskMonitor.Core.Data.CodexDataException { Error: CodexTaskMonitor.Core.Data.CodexDataError.DatabaseMissing } => "未找到本机 Codex 数据",
        CodexTaskMonitor.Core.Data.CodexDataException { Error: CodexTaskMonitor.Core.Data.CodexDataError.FormatChanged } => "Codex 数据格式已变化",
        _ => "暂时无法读取 Codex 数据"
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (polling is not null) await polling;
        lifetime.Dispose();
        refreshGate.Dispose();
    }
}
```

- [ ] **Step 5: Implement the approved floating-window XAML**

Replace `MainWindow.xaml` with a 330 DIP, topmost, borderless panel. Use a `ListBox` capped to 378 DIP (six 62-DIP rows plus six 1-DIP dividers), bind row colors/text, expose a row-open button and a separate handled button, and show the orange error strip with a data trigger:

```xml
<Window x:Class="CodexTaskMonitor.Windows.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="330" Height="{Binding PanelHeight}" MaxHeight="458"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True" ResizeMode="NoResize">
  <Border CornerRadius="14" Background="#EE1E242F">
    <Grid>
      <Grid.RowDefinitions><RowDefinition Height="48"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
      <Grid Grid.Row="0" MouseLeftButtonDown="Header_MouseLeftButtonDown">
        <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
        <StackPanel Orientation="Horizontal" Margin="14,0" VerticalAlignment="Center">
          <TextBlock Text="Codex 任务" FontSize="13" FontWeight="SemiBold" Foreground="White"/>
          <TextBlock Text="{Binding Items.Count}" Margin="8,0,0,0" FontSize="11" Foreground="#94A3B8"/>
        </StackPanel>
        <Button Grid.Column="1" Content="•••" Width="32" Height="28" Margin="0,0,10,0" Click="More_Click">
          <Button.ContextMenu>
            <ContextMenu DataContext="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}">
              <MenuItem Header="重新读取" Command="{Binding RefreshCommand}"/>
              <MenuItem Header="登录时启动" IsCheckable="True" IsChecked="{Binding IsStartupEnabled, Mode=OneWay}" Command="{Binding ToggleStartupCommand}"/>
              <Separator/>
              <MenuItem Header="退出" Command="{Binding QuitCommand}"/>
            </ContextMenu>
          </Button.ContextMenu>
        </Button>
      </Grid>
      <ListBox x:Name="TaskList" Grid.Row="1" ItemsSource="{Binding Items}" MaxHeight="378" Background="Transparent" BorderThickness="0" ScrollViewer.VerticalScrollBarVisibility="Auto">
        <ListBox.ItemContainerStyle>
          <Style TargetType="ListBoxItem">
            <Setter Property="Padding" Value="0"/><Setter Property="Margin" Value="0"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Focusable" Value="False"/>
          </Style>
        </ListBox.ItemContainerStyle>
        <ListBox.ItemTemplate>
          <DataTemplate>
            <Border Height="63" BorderBrush="#14FFFFFF" BorderThickness="0,1,0,0">
            <Grid Height="62" Margin="14,0">
              <Grid.ColumnDefinitions><ColumnDefinition Width="18"/><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <Ellipse Width="8" Height="8" Fill="{Binding DotColor}"/>
              <Button Grid.Column="1" Tag="{Binding}" Click="Open_Click" Background="Transparent" BorderThickness="0" HorizontalContentAlignment="Stretch">
                <StackPanel>
                  <TextBlock Text="{Binding Title}" Foreground="White" FontSize="13" TextTrimming="CharacterEllipsis"/>
                  <TextBlock Foreground="#94A3B8" FontSize="10.5"><Run Text="{Binding StateText}"/><Run Text=" · "/><Run Text="{Binding ProjectName}"/></TextBlock>
                </StackPanel>
              </Button>
              <Button Grid.Column="2" Tag="{Binding}" Click="Dismiss_Click" Content="已处理" FontSize="10.5" Padding="7,4">
                <Button.Style><Style TargetType="Button"><Setter Property="Visibility" Value="Visible"/><Style.Triggers><DataTrigger Binding="{Binding CanDismiss}" Value="False"><Setter Property="Visibility" Value="Collapsed"/></DataTrigger></Style.Triggers></Style></Button.Style>
              </Button>
            </Grid>
            </Border>
          </DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>
      <Border Grid.Row="2" Height="32" Background="#0EF59E0B">
        <Border.Style><Style TargetType="Border"><Setter Property="Visibility" Value="Visible"/><Style.Triggers><DataTrigger Binding="{Binding HasError}" Value="False"><Setter Property="Visibility" Value="Collapsed"/></DataTrigger></Style.Triggers></Style></Border.Style>
        <TextBlock Text="{Binding ErrorMessage}" Margin="14,0" VerticalAlignment="Center" Foreground="#FBBF24" FontSize="10.5" TextTrimming="CharacterEllipsis"/>
      </Border>
      <Border Grid.RowSpan="3" CornerRadius="14" BorderBrush="#26FFFFFF" BorderThickness="1" IsHitTestVisible="False"/>
    </Grid>
  </Border>
</Window>
```

- [ ] **Step 6: Wire window events without business logic**

Replace `MainWindow.xaml.cs` with:

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows;

public partial class MainWindow : Window
{
    private CancellationTokenSource? positionSave;
    private bool exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MonitorViewModel model) return;
        model.ItemInserted += OnItemInserted;
        if (model.SavedWindowLeft is { } left && model.SavedWindowTop is { } top &&
            left >= SystemParameters.VirtualScreenLeft && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
            top >= SystemParameters.VirtualScreenTop && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
        {
            Left = left; Top = top;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - ActualHeight) / 2;
        }
    }

    private void OnItemInserted(object? sender, string itemId)
    {
        if (DataContext is not MonitorViewModel model) return;
        var item = model.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is not null) TaskList.ScrollIntoView(item);
    }

    private async void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || DataContext is not MonitorViewModel model) return;
        positionSave?.Cancel();
        positionSave?.Dispose();
        positionSave = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, positionSave.Token);
            await model.SaveWindowPositionAsync(Left, Top, positionSave.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MonitorItemViewModel item } && DataContext is MonitorViewModel model)
            await model.OpenAsync(item, CancellationToken.None);
    }

    private async void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MonitorItemViewModel item } && DataContext is MonitorViewModel model)
            await model.DismissAsync(item, CancellationToken.None);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exitRequested) return;
        e.Cancel = true;
        Hide();
    }

    public void RequestExit()
    {
        positionSave?.Cancel();
        positionSave?.Dispose();
        if (DataContext is MonitorViewModel model) model.ItemInserted -= OnItemInserted;
        exitRequested = true;
        Close();
    }
}
```

This file only translates WPF events into view-model calls; it contains no SQLite, rollout, or UIA logic.

- [ ] **Step 7: Run view-model tests and compile the WPF app**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~MonitorViewModelTests
dotnet build windows/CodexTaskMonitor.sln -c Release
```

Expected: view-model tests pass and the WPF project compiles with zero warnings.

- [ ] **Step 8: Commit**

```powershell
git add windows/CodexTaskMonitor.Windows windows/CodexTaskMonitor.Tests/ViewModels
git commit -m "feat: add Windows task monitor panel"
```

### Task 9: Add single-instance, startup, launch-time, and privacy-safe diagnostics services

**Files:**
- Create: `windows/CodexTaskMonitor.Windows/Services/SingleInstanceService.cs`
- Create: `windows/CodexTaskMonitor.Windows/Services/StartupRegistration.cs`
- Create: `windows/CodexTaskMonitor.Windows/Services/CodexLaunchTimeProvider.cs`
- Create: `windows/CodexTaskMonitor.Windows/Services/LocalDiagnostics.cs`
- Test: `windows/CodexTaskMonitor.Tests/Services/WindowsServicesTests.cs`

**Interfaces:**
- Consumes: `IStartupRegistration` and `ICodexLaunchTimeProvider` from Task 8.
- Produces: concrete services used by the composition root in Task 12.

- [ ] **Step 1: Write failing service tests**

Create `windows/CodexTaskMonitor.Tests/Services/WindowsServicesTests.cs`:

```csharp
using CodexTaskMonitor.Windows.Services;

namespace CodexTaskMonitor.Tests.Services;

public sealed class WindowsServicesTests
{
    [Fact]
    public void StartupRegistration_QuotesExecutableAndDeletesSameValue()
    {
        var values = new FakeRunValueStore();
        var registration = new StartupRegistration(values, @"C:\Apps\Codex Task Monitor\CodexTaskMonitor.exe");
        registration.SetEnabled(true);
        Assert.Equal("\"C:\\Apps\\Codex Task Monitor\\CodexTaskMonitor.exe\"", values.Value);
        Assert.True(registration.IsEnabled);
        registration.SetEnabled(false);
        Assert.Null(values.Value);
    }

    [Fact]
    public void SingleInstance_SecondOwnerWithSameNameIsRejected()
    {
        var name = $"CodexTaskMonitor.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceService.TryAcquire(name);
        using var second = SingleInstanceService.TryAcquire(name);
        Assert.True(first.IsOwner);
        Assert.False(second.IsOwner);
    }

    [Fact]
    public async Task SingleInstance_SecondLaunchSignalsExistingOwner()
    {
        var name = $"CodexTaskMonitor.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceService.TryAcquire(name);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += (_, _) => activated.TrySetResult();

        using var second = SingleInstanceService.TryAcquire(name);

        Assert.False(second.IsOwner);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Diagnostics_DoesNotAcceptTaskContentAndRotatesBySize()
    {
        var root = Path.Combine(Path.GetTempPath(), $"logs-{Guid.NewGuid():N}");
        var log = new LocalDiagnostics(root, maxBytes: 120, retainedFiles: 2);
        for (var index = 0; index < 20; index++) await log.WriteAsync("uia-timeout", TimeSpan.FromMilliseconds(20), 1, default);
        Assert.InRange(Directory.GetFiles(root).Length, 1, 2);
        Assert.DoesNotContain("title", await File.ReadAllTextAsync(Directory.GetFiles(root)[0]), StringComparison.OrdinalIgnoreCase);
    }
}
```

Add this fake inside `WindowsServicesTests`, immediately before its final `}`:

```csharp
private sealed class FakeRunValueStore : IRunValueStore
{
    public string? Value { get; private set; }
    public string? Read() => Value;
    public void Write(string value) => Value = value;
    public void Delete() => Value = null;
}
```

- [ ] **Step 2: Run focused tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~WindowsServicesTests
```

Expected: FAIL to compile because the concrete services do not exist.

- [ ] **Step 3: Implement the single-instance and startup services**

Create `Services/SingleInstanceService.cs`:

```csharp
namespace CodexTaskMonitor.Windows.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activationSignal;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task? listener;
    public bool IsOwner { get; }
    public event EventHandler? ActivationRequested;

    private SingleInstanceService(Mutex mutex, EventWaitHandle activationSignal, bool owner)
    {
        this.mutex = mutex;
        this.activationSignal = activationSignal;
        IsOwner = owner;
        if (owner) listener = Task.Run(Listen);
        else activationSignal.Set();
    }

    public static SingleInstanceService TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, $"Local\\{name}", out var createdNew);
        var activation = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{name}.Activate");
        return new SingleInstanceService(mutex, activation, createdNew);
    }

    private void Listen()
    {
        var handles = new WaitHandle[] { activationSignal, lifetime.Token.WaitHandle };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            if (lifetime.IsCancellationRequested) return;
            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        activationSignal.Set();
        listener?.GetAwaiter().GetResult();
        if (IsOwner) mutex.ReleaseMutex();
        activationSignal.Dispose();
        mutex.Dispose();
        lifetime.Dispose();
    }
}
```

Create `Services/StartupRegistration.cs`:

```csharp
using Microsoft.Win32;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows.Services;

public interface IRunValueStore
{
    string? Read();
    void Write(string value);
    void Delete();
}

public sealed class RegistryRunValueStore : IRunValueStore
{
    private const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "CodexTaskMonitor";
    public string? Read() => Registry.CurrentUser.OpenSubKey(Path)?.GetValue(Name) as string;
    public void Write(string value) { using var key = Registry.CurrentUser.CreateSubKey(Path); key.SetValue(Name, value); }
    public void Delete() { using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true); key?.DeleteValue(Name, throwOnMissingValue: false); }
}

public sealed class StartupRegistration(IRunValueStore values, string executablePath) : IStartupRegistration
{
    private string Command => $"\"{executablePath}\"";
    public bool IsEnabled => string.Equals(values.Read(), Command, StringComparison.OrdinalIgnoreCase);
    public void SetEnabled(bool enabled) { if (enabled) values.Write(Command); else values.Delete(); }
}
```

- [ ] **Step 4: Implement launch-time lookup and diagnostics**

Create `Services/CodexLaunchTimeProvider.cs`:

```csharp
using System.Diagnostics;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows.Services;

public sealed class CodexLaunchTimeProvider : ICodexLaunchTimeProvider
{
    public DateTimeOffset? GetLaunchTime()
    {
        var times = Process.GetProcessesByName("ChatGPT")
            .Where(process => !process.HasExited && process.MainWindowHandle != IntPtr.Zero)
            .Select(process => { try { return new DateTimeOffset(process.StartTime.ToUniversalTime()); } catch { return (DateTimeOffset?)null; } })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return times.Length == 0 ? null : times.Max();
    }
}
```

Create `Services/LocalDiagnostics.cs`:

```csharp
using System.Text.Json;

namespace CodexTaskMonitor.Windows.Services;

public interface ILocalDiagnostics
{
    Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token);
}

public sealed class LocalDiagnostics(string directory, long maxBytes = 1_048_576, int retainedFiles = 3) : ILocalDiagnostics
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "monitor.log");
            if (File.Exists(path) && new FileInfo(path).Length >= maxBytes) Rotate();
            var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, category, duration_ms = (long)duration.TotalMilliseconds, count });
            await File.AppendAllTextAsync(path, line + Environment.NewLine, token);
        }
        finally { gate.Release(); }
    }

    private void Rotate()
    {
        for (var index = retainedFiles - 1; index >= 1; index--)
        {
            var source = Path.Combine(directory, index == 1 ? "monitor.log" : $"monitor.{index - 1}.log");
            var target = Path.Combine(directory, $"monitor.{index}.log");
            if (File.Exists(source)) File.Move(source, target, overwrite: true);
        }
    }
}
```

- [ ] **Step 5: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~WindowsServicesTests
dotnet test windows/CodexTaskMonitor.sln
```

Expected: service tests and full suite pass without writing the real HKCU Run key.

- [ ] **Step 6: Commit**

```powershell
git add windows/CodexTaskMonitor.Windows/Services windows/CodexTaskMonitor.Tests/Services
git commit -m "feat: add Windows monitor system services"
```

### Task 10: Capture UI Automation snapshots and match exact sidebar items

**Files:**
- Modify: `windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj`
- Create: `windows/CodexTaskMonitor.Windows/Automation/AutomationNode.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/ChatGptWindowLocator.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/IUiAutomationSnapshotProvider.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/UiAutomationSnapshotProvider.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/SidebarMatcher.cs`
- Test: `windows/CodexTaskMonitor.Tests/Automation/SidebarMatcherTests.cs`

**Interfaces:**
- Consumes: `SidebarTarget` and group types from Task 3.
- Produces: `AutomationSnapshot`, `SidebarMatchResult`, `IUiAutomationSnapshotProvider`, and `IChatGptWindowLocator` for Tasks 11–12.

- [ ] **Step 1: Add the Windows UI Automation assembly references**

Add to `CodexTaskMonitor.Windows.csproj`:

```xml
<ItemGroup>
  <Reference Include="UIAutomationClient" />
  <Reference Include="UIAutomationTypes" />
</ItemGroup>
```

- [ ] **Step 2: Write failing matcher tests with synthetic snapshots**

Create `windows/CodexTaskMonitor.Tests/Automation/SidebarMatcherTests.cs`:

```csharp
using System.Windows;
using CodexTaskMonitor.Core.Sidebar;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Tests.Automation;

public sealed class SidebarMatcherTests
{
    [Fact]
    public void UniqueExactListItem_IsAccepted()
    {
        var snapshot = Snapshot(
            Node("heading", "ControlType.Text", "DemoProject", ["root", "sidebar"]),
            Node("task", "ControlType.ListItem", "Exact title", ["root", "sidebar", "project"]));
        var result = SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Project("DemoProject")));
        Assert.Equal(SidebarMatchStatus.Found, result.Status);
        Assert.Equal("task", result.Node!.RuntimeId);
    }

    [Fact]
    public void DuplicateWithoutUniqueGroupEvidence_IsAmbiguous()
    {
        var snapshot = Snapshot(
            Node("a", "ControlType.ListItem", "Same", ["root", "left"]),
            Node("b", "ControlType.ListItem", "Same", ["root", "right"]));
        var result = SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Projectless()));
        Assert.Equal(SidebarMatchStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void SubstringTitle_IsNeverAccepted()
    {
        var snapshot = Snapshot(Node("task", "ControlType.ListItem", "Exact title continued", ["root", "sidebar"]));
        Assert.Equal(SidebarMatchStatus.NotFound,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Projectless())).Status);
    }
}
```

Add these helpers to the test file:

```csharp
private static AutomationNode Node(
    string id,
    string controlType,
    string name,
    string[] ancestors,
    bool offscreen = false,
    Rect? bounds = null) =>
    new(id, controlType, name, "", bounds ?? new Rect(20, 20, 220, 40), offscreen, ancestors, 0);

private static AutomationSnapshot Snapshot(params AutomationNode[] nodes) =>
    new(new Rect(0, 0, 1000, 800), nodes);
```

- [ ] **Step 3: Run matcher tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~SidebarMatcherTests
```

Expected: FAIL to compile because automation snapshot and matcher types do not exist.

- [ ] **Step 4: Implement immutable automation snapshot models and exact matching**

Create `Automation/AutomationNode.cs`:

```csharp
using System.Windows;

namespace CodexTaskMonitor.Windows.Automation;

public sealed record AutomationNode(
    string RuntimeId,
    string ControlType,
    string Name,
    string ClassName,
    Rect Bounds,
    bool IsOffscreen,
    IReadOnlyList<string> AncestorRuntimeIds,
    int TraversalIndex);

public sealed record AutomationSnapshot(Rect WindowBounds, IReadOnlyList<AutomationNode> Nodes);
public enum SidebarMatchStatus { Found, NotFound, Ambiguous }
public sealed record SidebarMatchResult(SidebarMatchStatus Status, AutomationNode? Node);
```

Create `Automation/SidebarMatcher.cs`:

```csharp
using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Windows.Automation;

public static class SidebarMatcher
{
    public static SidebarMatchResult Match(AutomationSnapshot snapshot, SidebarTarget target)
    {
        var leftLimit = snapshot.WindowBounds.Left + snapshot.WindowBounds.Width * 0.45;
        var candidates = snapshot.Nodes.Where(node =>
            node.ControlType == "ControlType.ListItem" &&
            string.Equals(node.Name, target.Title, StringComparison.Ordinal) &&
            (node.IsOffscreen || (!node.Bounds.IsEmpty && node.Bounds.Left < leftLimit))).ToArray();
        if (candidates.Length == 0) return new(SidebarMatchStatus.NotFound, null);
        if (candidates.Length == 1)
            return candidates[0] is { IsOffscreen: false } visible
                ? new(SidebarMatchStatus.Found, visible)
                : new(SidebarMatchStatus.NotFound, null);

        var label = target.Group.Kind switch
        {
            SidebarThreadGroupKind.Pinned => "置顶",
            SidebarThreadGroupKind.Section or SidebarThreadGroupKind.Project => target.Group.Name,
            _ => null
        };
        if (string.IsNullOrEmpty(label)) return new(SidebarMatchStatus.Ambiguous, null);
        var headings = snapshot.Nodes.Where(node =>
            (node.ControlType is "ControlType.Text" or "ControlType.Button") &&
            string.Equals(node.Name, label, StringComparison.Ordinal)).ToArray();
        if (headings.Length == 0) return new(SidebarMatchStatus.Ambiguous, null);

        var scored = candidates.Select(candidate => new
        {
            Candidate = candidate,
            Score = headings.Max(heading => CommonPrefix(candidate.AncestorRuntimeIds, heading.AncestorRuntimeIds))
        }).OrderByDescending(item => item.Score).ToArray();
        if (scored.Length < 2 || scored[0].Score < 2 || scored[0].Score == scored[1].Score)
            return new(SidebarMatchStatus.Ambiguous, null);
        return scored[0].Candidate is { IsOffscreen: false } groupedVisible
            ? new(SidebarMatchStatus.Found, groupedVisible)
            : new(SidebarMatchStatus.NotFound, null);
    }

    private static int CommonPrefix(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var count = 0;
        while (count < left.Count && count < right.Count && left[count] == right[count]) count++;
        return count;
    }
}
```

- [ ] **Step 5: Implement bounded raw-tree capture and ChatGPT window lookup**

Create the contracts:

```csharp
namespace CodexTaskMonitor.Windows.Automation;

public interface IUiAutomationSnapshotProvider
{
    Task<AutomationSnapshot> CaptureAsync(nint windowHandle, CancellationToken token);
}

public interface IChatGptWindowLocator
{
    nint FindMainWindow();
}
```

Create `Automation/ChatGptWindowLocator.cs`:

```csharp
using System.Diagnostics;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class ChatGptWindowLocator : IChatGptWindowLocator
{
    public nint FindMainWindow()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT").OrderByDescending(SafeStartTime))
        {
            try { if (!process.HasExited && process.MainWindowHandle != 0) return process.MainWindowHandle; }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        return 0;
    }

    private static DateTime SafeStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return DateTime.MinValue; }
    }
}
```

Create `Automation/UiAutomationSnapshotProvider.cs`:

```csharp
using System.Windows.Automation;

namespace CodexTaskMonitor.Windows.Automation;

internal interface IAutomationTreeNode
{
    string RuntimeId { get; }
    string ControlType { get; }
    string Name { get; }
    string ClassName { get; }
    System.Windows.Rect Bounds { get; }
    bool IsOffscreen { get; }
    IReadOnlyList<IAutomationTreeNode> Children { get; }
}

public sealed class UiAutomationSnapshotProvider : IUiAutomationSnapshotProvider
{
    private const int MaximumElementCount = 5_000;

    public Task<AutomationSnapshot> CaptureAsync(nint windowHandle, CancellationToken token)
    {
        var completion = new TaskCompletionSource<AutomationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(Capture(windowHandle, token)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "CodexTaskMonitor.UIA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static AutomationSnapshot Capture(nint windowHandle, CancellationToken token)
    {
        var root = AutomationElement.FromHandle(windowHandle)
            ?? throw new InvalidOperationException("ChatGPT UIA root is unavailable");
        return ProjectTree(new AutomationElementTreeNode(root), root.Current.BoundingRectangle, token);
    }

    internal static AutomationSnapshot ProjectTree(
        IAutomationTreeNode root,
        System.Windows.Rect windowBounds,
        CancellationToken token)
    {
        var queue = new Queue<(IAutomationTreeNode Element, string[] Ancestors)>();
        queue.Enqueue((root, []));
        var nodes = new List<AutomationNode>();
        while (queue.Count > 0 && nodes.Count < MaximumElementCount)
        {
            token.ThrowIfCancellationRequested();
            var (element, ancestors) = queue.Dequeue();
            try
            {
                var runtimeId = string.IsNullOrEmpty(element.RuntimeId) ? $"fallback-{nodes.Count}" : element.RuntimeId;
                nodes.Add(new AutomationNode(
                    runtimeId,
                    element.ControlType,
                    element.Name,
                    element.ClassName,
                    element.Bounds,
                    element.IsOffscreen,
                    ancestors,
                    nodes.Count));
                var childAncestors = ancestors.Append(runtimeId).ToArray();
                foreach (var child in element.Children)
                    queue.Enqueue((child, childAncestors));
            }
            catch (ElementNotAvailableException) { }
        }
        return new AutomationSnapshot(windowBounds, nodes);
    }

    private sealed class AutomationElementTreeNode(AutomationElement element) : IAutomationTreeNode
    {
        public string RuntimeId { get { try { return string.Join('.', element.GetRuntimeId()); } catch (InvalidOperationException) { return string.Empty; } } }
        public string ControlType => element.Current.ControlType.ProgrammaticName;
        public string Name => element.Current.Name ?? string.Empty;
        public string ClassName => element.Current.ClassName ?? string.Empty;
        public System.Windows.Rect Bounds => element.Current.BoundingRectangle;
        public bool IsOffscreen => element.Current.IsOffscreen;
        public IReadOnlyList<IAutomationTreeNode> Children
        {
            get
            {
                var children = new List<IAutomationTreeNode>();
                var walker = TreeWalker.RawViewWalker;
                for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                    children.Add(new AutomationElementTreeNode(child));
                return children;
            }
        }
    }
}
```

The dedicated STA thread, 5,000-node cap, carried ancestor IDs, and cancellation checks are mandatory. The adapter catches `ElementNotAvailableException` only for individual stale elements; failure to obtain the root remains a reveal error.

- [ ] **Step 6: Add duplicate-group, offscreen, and 5,000-node cap tests**

Append these matcher tests; the second test proves a visible duplicate in the wrong group is never accepted while the correct row is offscreen:

```csharp
[Fact]
public void DuplicateTitle_UsesUniqueGroupEvidence()
{
    var snapshot = Snapshot(
        Node("heading-a", "ControlType.Text", "Project A", ["root", "sidebar", "a"]),
        Node("task-a", "ControlType.ListItem", "Same", ["root", "sidebar", "a"]),
        Node("task-b", "ControlType.ListItem", "Same", ["root", "sidebar", "b"]));
    var result = SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Project("Project A")));
    Assert.Equal("task-a", result.Node!.RuntimeId);
}

[Fact]
public void CorrectGroupedDuplicateOffscreen_DoesNotAcceptVisibleWrongGroup()
{
    var snapshot = Snapshot(
        Node("heading-a", "ControlType.Text", "Project A", ["root", "sidebar", "a"]),
        Node("task-a", "ControlType.ListItem", "Same", ["root", "sidebar", "a"], offscreen: true, bounds: Rect.Empty),
        Node("task-b", "ControlType.ListItem", "Same", ["root", "sidebar", "b"]));
    Assert.Equal(SidebarMatchStatus.NotFound,
        SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Project("Project A"))).Status);
}

[Fact]
public void EqualGroupScores_AreAmbiguous()
{
    var snapshot = Snapshot(
        Node("heading", "ControlType.Text", "Project A", ["root", "sidebar"]),
        Node("task-a", "ControlType.ListItem", "Same", ["root", "sidebar", "a"]),
        Node("task-b", "ControlType.ListItem", "Same", ["root", "sidebar", "b"]));
    Assert.Equal(SidebarMatchStatus.Ambiguous,
        SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Project("Project A"))).Status);
}
```

The Step 5 provider already exposes the bounded loop as internal `ProjectTree(IAutomationTreeNode, Rect, CancellationToken)`, and Task 1 grants the test assembly internal access. Create `UiAutomationSnapshotProviderTests.cs` with this exact synthetic cap test:

```csharp
[Fact]
public void ProjectTree_StopsAtFiveThousandAndPreservesFlagsAndBounds()
{
    var root = FakeAutomationTreeNode.Chain(length: 5_100, offscreenIndex: 4_999, new Rect(7, 8, 9, 10));
    var snapshot = UiAutomationSnapshotProvider.ProjectTree(root, new Rect(0, 0, 100, 100), default);
    Assert.Equal(5_000, snapshot.Nodes.Count);
    Assert.True(snapshot.Nodes[^1].IsOffscreen);
    Assert.Equal(new Rect(7, 8, 9, 10), snapshot.Nodes[^1].Bounds);
}
```

Add this fake inside `UiAutomationSnapshotProviderTests`, immediately before its final `}`; it returns a linked synthetic tree without accessing the desktop:

```csharp
private sealed class FakeAutomationTreeNode(
    string id,
    bool offscreen,
    Rect bounds,
    IReadOnlyList<IAutomationTreeNode> children) : IAutomationTreeNode
{
    public string RuntimeId => id;
    public string ControlType => "ControlType.ListItem";
    public string Name => id;
    public string ClassName => "fake";
    public Rect Bounds => bounds;
    public bool IsOffscreen => offscreen;
    public IReadOnlyList<IAutomationTreeNode> Children => children;

    public static FakeAutomationTreeNode Chain(int length, int offscreenIndex, Rect offscreenBounds)
    {
        IAutomationTreeNode? next = null;
        for (var index = length - 1; index >= 0; index--)
        {
            var marked = index == offscreenIndex;
            next = new FakeAutomationTreeNode(
                $"node-{index}", marked, marked ? offscreenBounds : new Rect(1, 1, 2, 2),
                next is null ? [] : [next]);
        }
        return (FakeAutomationTreeNode)next!;
    }
}
```

- [ ] **Step 7: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter "FullyQualifiedName~SidebarMatcherTests|FullyQualifiedName~UiAutomationSnapshotProviderTests"
dotnet test windows/CodexTaskMonitor.sln
```

Expected: automation unit tests and full suite pass.

- [ ] **Step 8: Commit**

```powershell
git add windows/CodexTaskMonitor.Windows/Automation windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj windows/CodexTaskMonitor.Tests/Automation
git commit -m "feat: match exact Codex sidebar items with UIA"
```

### Task 11: Implement bounded sidebar scrolling without clicks

**Files:**
- Create: `windows/CodexTaskMonitor.Windows/Automation/SidebarRegionDetector.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/ISidebarScrollInput.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/UiAutomationSidebarScrollInput.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/SidebarScrollInput.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/SidebarScrollController.cs`
- Create: `windows/CodexTaskMonitor.Windows/Interop/NativeSidebarWheelInput.cs`
- Create: `windows/CodexTaskMonitor.Windows/Interop/NativeMethods.cs`
- Test: `windows/CodexTaskMonitor.Tests/Fakes/FakeAutomationEnvironment.cs`
- Test: `windows/CodexTaskMonitor.Tests/Automation/SidebarScrollControllerTests.cs`

**Interfaces:**
- Consumes: `AutomationSnapshot`, `SidebarMatcher`, `SidebarTarget`, and snapshot provider from Task 10.
- Produces: `SidebarScrollController.RevealAsync(nint, SidebarTarget, CancellationToken)` and `SidebarScrollResult` for Task 12.

- [ ] **Step 1: Write failing deterministic-scroll tests**

Create `windows/CodexTaskMonitor.Tests/Automation/SidebarScrollControllerTests.cs`:

```csharp
using CodexTaskMonitor.Core.Sidebar;
using CodexTaskMonitor.Tests.Fakes;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Tests.Automation;

public sealed class SidebarScrollControllerTests
{
    [Fact]
    public async Task Reveal_ResetsUpThenScansDownUntilUniqueTargetIsVisible()
    {
        var environment = FakeAutomationEnvironment.WithPages(1,
            Page("top"), Page("middle"), Page("target", targetTitle: "Wanted"));
        var controller = new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, maxSteps: 80, timeout: TimeSpan.FromSeconds(8));

        var result = await controller.RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.Found, result.Status);
        Assert.Contains(ScrollDirection.Up, environment.Directions);
        Assert.Equal(ScrollDirection.Down, environment.Directions[^1]);
        Assert.Equal(SidebarInputMode.AutomationPattern, environment.Modes[0]);
        Assert.DoesNotContain(environment.Actions, action => action == "click");
    }

    [Fact]
    public async Task Reveal_StopsOnAmbiguousMatchWithoutScrolling()
    {
        var environment = FakeAutomationEnvironment.Ambiguous("Same");
        var controller = new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8));
        var result = await controller.RevealAsync(123, new SidebarTarget("Same", SidebarThreadGroup.Projectless()), default);
        Assert.Equal(SidebarScrollStatus.Ambiguous, result.Status);
        Assert.Empty(environment.Directions);
    }
}
```

- [ ] **Step 2: Run focused tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~SidebarScrollControllerTests
```

Expected: FAIL to compile because scroll contracts and controller do not exist.

- [ ] **Step 3: Implement safe sidebar-region detection**

Create `Automation/SidebarRegionDetector.cs`:

```csharp
using System.Windows;

namespace CodexTaskMonitor.Windows.Automation;

public static class SidebarRegionDetector
{
    public static Rect? Detect(AutomationSnapshot snapshot)
    {
        var leftLimit = snapshot.WindowBounds.Left + snapshot.WindowBounds.Width * 0.45;
        var items = snapshot.Nodes.Where(node =>
            node.ControlType == "ControlType.ListItem" && !node.IsOffscreen &&
            !node.Bounds.IsEmpty && node.Bounds.Left < leftLimit).ToArray();
        if (items.Length < 3) return null;
        var left = items.Min(node => node.Bounds.Left);
        var top = items.Min(node => node.Bounds.Top);
        var right = items.Max(node => node.Bounds.Right);
        var bottom = items.Max(node => node.Bounds.Bottom);
        var region = new Rect(left, top, right - left, bottom - top);
        return region.Height >= 120 && region.Width >= 120 ? region : null;
    }

    public static string Signature(AutomationSnapshot snapshot, Rect region) => string.Join('|',
        snapshot.Nodes.Where(node => node.ControlType == "ControlType.ListItem" && !node.IsOffscreen && region.IntersectsWith(node.Bounds))
            .OrderBy(node => node.Bounds.Top).Select(node => $"{node.RuntimeId}:{node.Bounds.Top:0}"));
}
```

- [ ] **Step 4: Implement the bounded upward-reset/downward-scan state machine**

Create `Automation/ISidebarScrollInput.cs` and `SidebarScrollController.cs`:

```csharp
using System.Windows;

namespace CodexTaskMonitor.Windows.Automation;

public enum ScrollDirection { Up, Down }
public enum SidebarInputMode { AutomationPattern, PostedMessage, PhysicalFallback }
public enum SidebarScrollStatus { Found, NotFound, Ambiguous, RegionUnavailable, TimedOut }
public sealed record SidebarScrollResult(SidebarScrollStatus Status, AutomationNode? Node);

public interface ISidebarScrollInput
{
    Task<bool> ScrollAsync(nint windowHandle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token);
}
```

```csharp
using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class SidebarScrollController(
    IUiAutomationSnapshotProvider snapshots,
    ISidebarScrollInput input,
    TimeProvider time,
    TimeSpan settleDelay,
    int maxSteps,
    TimeSpan timeout)
{
    public async Task<SidebarScrollResult> RevealAsync(nint handle, SidebarTarget target, CancellationToken token)
    {
        var started = time.GetTimestamp();
        var steps = 0;
        var snapshot = await snapshots.CaptureAsync(handle, token);
        var immediate = SidebarMatcher.Match(snapshot, target);
        if (immediate.Status == SidebarMatchStatus.Ambiguous) return new(SidebarScrollStatus.Ambiguous, null);
        if (immediate.Status == SidebarMatchStatus.Found && immediate.Node is { IsOffscreen: false } visible)
            return new(SidebarScrollStatus.Found, visible);
        var region = SidebarRegionDetector.Detect(snapshot);
        if (region is null) return new(SidebarScrollStatus.RegionUnavailable, null);

        var modes = new[]
        {
            SidebarInputMode.AutomationPattern,
            SidebarInputMode.PostedMessage,
            SidebarInputMode.PhysicalFallback
        };
        SidebarInputMode? selectedMode = null;
        var anyInputAccepted = false;
        foreach (var mode in modes)
        {
            var stableAtTop = 0;
            var previous = SidebarRegionDetector.Signature(snapshot, region.Value);
            while (stableAtTop < 2)
            {
                if (time.GetElapsedTime(started) >= timeout) return new(SidebarScrollStatus.TimedOut, null);
                if (steps >= maxSteps) return new(SidebarScrollStatus.NotFound, null);
                steps++;
                if (!await input.ScrollAsync(handle, region.Value, ScrollDirection.Up, mode, token)) break;
                anyInputAccepted = true;
                await Task.Delay(settleDelay, time, token);
                snapshot = await snapshots.CaptureAsync(handle, token);
                var signature = SidebarRegionDetector.Signature(snapshot, region.Value);
                stableAtTop = signature == previous ? stableAtTop + 1 : 0;
                previous = signature;
            }
            if (stableAtTop < 2) continue;

            var atTop = SidebarMatcher.Match(snapshot, target);
            if (atTop.Status == SidebarMatchStatus.Ambiguous) return new(SidebarScrollStatus.Ambiguous, null);
            if (atTop.Status == SidebarMatchStatus.Found && atTop.Node is { } topNode)
                return new(SidebarScrollStatus.Found, topNode);

            if (time.GetElapsedTime(started) >= timeout) return new(SidebarScrollStatus.TimedOut, null);
            if (steps >= maxSteps) return new(SidebarScrollStatus.NotFound, null);
            steps++;
            if (!await input.ScrollAsync(handle, region.Value, ScrollDirection.Down, mode, token)) continue;
            anyInputAccepted = true;
            await Task.Delay(settleDelay, time, token);
            var probe = await snapshots.CaptureAsync(handle, token);
            if (SidebarRegionDetector.Signature(probe, region.Value) == previous) continue;

            // The down probe proves this mode moves the sidebar. Reset once more before the full scan.
            snapshot = probe;
            var resetStable = 0;
            previous = SidebarRegionDetector.Signature(snapshot, region.Value);
            while (resetStable < 2)
            {
                if (time.GetElapsedTime(started) >= timeout) return new(SidebarScrollStatus.TimedOut, null);
                if (steps >= maxSteps) return new(SidebarScrollStatus.NotFound, null);
                steps++;
                if (!await input.ScrollAsync(handle, region.Value, ScrollDirection.Up, mode, token))
                    return new(SidebarScrollStatus.RegionUnavailable, null);
                await Task.Delay(settleDelay, time, token);
                snapshot = await snapshots.CaptureAsync(handle, token);
                var signature = SidebarRegionDetector.Signature(snapshot, region.Value);
                resetStable = signature == previous ? resetStable + 1 : 0;
                previous = signature;
            }
            selectedMode = mode;
            break;
        }

        if (selectedMode is null)
            return new(anyInputAccepted ? SidebarScrollStatus.NotFound : SidebarScrollStatus.RegionUnavailable, null);

        var stableAtBottom = 0;
        var lastSignature = SidebarRegionDetector.Signature(snapshot, region.Value);
        while (steps < maxSteps)
        {
            var match = SidebarMatcher.Match(snapshot, target);
            if (match.Status == SidebarMatchStatus.Ambiguous) return new(SidebarScrollStatus.Ambiguous, null);
            if (match.Status == SidebarMatchStatus.Found && match.Node is { } found)
                return new(SidebarScrollStatus.Found, found);
            if (time.GetElapsedTime(started) >= timeout) return new(SidebarScrollStatus.TimedOut, null);
            steps++;
            if (!await input.ScrollAsync(handle, region.Value, ScrollDirection.Down, selectedMode.Value, token))
                return new(SidebarScrollStatus.RegionUnavailable, null);
            await Task.Delay(settleDelay, time, token);
            snapshot = await snapshots.CaptureAsync(handle, token);
            var signature = SidebarRegionDetector.Signature(snapshot, region.Value);
            stableAtBottom = signature == lastSignature ? stableAtBottom + 1 : 0;
            if (stableAtBottom >= 2) return new(SidebarScrollStatus.NotFound, null);
            lastSignature = signature;
        }
        return new(SidebarScrollStatus.NotFound, null);
    }
}
```

- [ ] **Step 5: Implement UIA scrolling and native fallbacks with no click path**

Create `Automation/UiAutomationSidebarScrollInput.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class UiAutomationSidebarScrollInput : ISidebarScrollInput
{
    public Task<bool> ScrollAsync(nint handle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token)
    {
        if (mode != SidebarInputMode.AutomationPattern) return Task.FromResult(false);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(Scroll(handle, region, direction, token)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "CodexTaskMonitor.UIAScroll" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static bool Scroll(nint handle, Rect region, ScrollDirection direction, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var condition = new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true);
            var center = new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
            var candidates = root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>()
                .Select(element => (Element: element, Bounds: element.Current.BoundingRectangle))
                .Where(item => !item.Bounds.IsEmpty && item.Bounds.Contains(center))
                .OrderBy(item => item.Bounds.Width * item.Bounds.Height);
            foreach (var candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                if (!candidate.Element.TryGetCurrentPattern(ScrollPattern.Pattern, out var value) ||
                    value is not ScrollPattern pattern || !pattern.Current.VerticallyScrollable) continue;
                pattern.Scroll(ScrollAmount.NoAmount,
                    direction == ScrollDirection.Up ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement);
                return true;
            }
            return false;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
```

Create `Interop/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;

namespace CodexTaskMonitor.Windows.Interop;

internal static class NativeMethods
{
    private const uint WmMouseWheel = 0x020A;
    private const uint InputMouse = 0;
    private const uint MouseeventfWheel = 0x0800;

    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static bool PostWheel(nint handle, Point point, int delta)
    {
        var wParam = (nint)(delta << 16);
        var lParam = (nint)((point.Y & 0xFFFF) << 16 | (point.X & 0xFFFF));
        return PostMessage(handle, WmMouseWheel, wParam, lParam);
    }

    public static bool SendWheel(int delta)
    {
        var inputs = new[] { new Input { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { MouseData = unchecked((uint)delta), Flags = MouseeventfWheel } } } };
        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }
}
```

Create `Interop/NativeSidebarWheelInput.cs` and the composite `Automation/SidebarScrollInput.cs`:

```csharp
// Interop/NativeSidebarWheelInput.cs
using System.Windows;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Windows.Interop;

public sealed class NativeSidebarWheelInput
{
    public Task<bool> ScrollAsync(nint handle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var point = new NativeMethods.Point((int)region.Left + 12, (int)region.Top + (int)region.Height / 2);
        var delta = direction == ScrollDirection.Up ? 240 : -240;
        if (mode == SidebarInputMode.PostedMessage)
        {
            var renderHandle = NativeMethods.WindowFromPoint(point);
            return Task.FromResult(NativeMethods.PostWheel(renderHandle == 0 ? handle : renderHandle, point, delta));
        }
        if (mode != SidebarInputMode.PhysicalFallback || NativeMethods.GetForegroundWindow() != handle)
            return Task.FromResult(false);
        if (!NativeMethods.GetCursorPos(out var original)) return Task.FromResult(false);
        try
        {
            if (!NativeMethods.SetCursorPos(point.X, point.Y)) return Task.FromResult(false);
            return Task.FromResult(NativeMethods.SendWheel(delta));
        }
        finally { NativeMethods.SetCursorPos(original.X, original.Y); }
    }
}
```

```csharp
// Automation/SidebarScrollInput.cs
using System.Windows;
using CodexTaskMonitor.Windows.Interop;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class SidebarScrollInput(
    UiAutomationSidebarScrollInput automation,
    NativeSidebarWheelInput native) : ISidebarScrollInput
{
    public Task<bool> ScrollAsync(nint handle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token) =>
        mode == SidebarInputMode.AutomationPattern
            ? automation.ScrollAsync(handle, region, direction, mode, token)
            : native.ScrollAsync(handle, region, direction, mode, token);
}
```

The input union contains only `MouseInput`; `SendWheel` sends exactly one `MOUSEEVENTF_WHEEL` record and never emits button flags.

- [ ] **Step 6: Implement the deterministic fake and complete edge-case tests**

Create `Fakes/FakeAutomationEnvironment.cs`:

```csharp
using CodexTaskMonitor.Windows.Automation;
using System.Windows;

namespace CodexTaskMonitor.Tests.Fakes;

public sealed class FakeAutomationEnvironment : IUiAutomationSnapshotProvider, ISidebarScrollInput
{
    private readonly IReadOnlyList<AutomationSnapshot> pages;
    private readonly HashSet<SidebarInputMode> acceptedModes;
    private int index;

    private FakeAutomationEnvironment(
        IReadOnlyList<AutomationSnapshot> pages,
        int startIndex,
        IEnumerable<SidebarInputMode> acceptedModes)
    {
        this.pages = pages;
        index = startIndex;
        this.acceptedModes = acceptedModes.ToHashSet();
    }

    public List<ScrollDirection> Directions { get; } = [];
    public List<SidebarInputMode> Modes { get; } = [];
    public List<string> Actions { get; } = [];

    public static FakeAutomationEnvironment WithPages(int startIndex, params AutomationSnapshot[] pages) =>
        new(pages, startIndex, [SidebarInputMode.AutomationPattern]);

    public static FakeAutomationEnvironment WithModes(
        int startIndex,
        SidebarInputMode[] modes,
        params AutomationSnapshot[] pages) => new(pages, startIndex, modes);

    public static FakeAutomationEnvironment Ambiguous(string title)
    {
        var nodes = new List<AutomationNode>();
        for (var item = 0; item < 3; item++)
            nodes.Add(new($"filler-{item}", "ControlType.ListItem", $"filler-{item}", "", new Rect(10, 20 + item * 40, 200, 30), false, ["root", "sidebar"], item));
        nodes.Add(new("a", "ControlType.ListItem", title, "", new Rect(10, 160, 200, 30), false, ["root", "sidebar"], 4));
        nodes.Add(new("b", "ControlType.ListItem", title, "", new Rect(10, 200, 200, 30), false, ["root", "sidebar"], 5));
        return WithPages(0, new AutomationSnapshot(new Rect(0, 0, 1000, 800), nodes));
    }

    public Task<AutomationSnapshot> CaptureAsync(nint handle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(pages[index]);
    }

    public Task<bool> ScrollAsync(nint handle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directions.Add(direction);
        Modes.Add(mode);
        if (!acceptedModes.Contains(mode)) return Task.FromResult(false);
        Actions.Add("scroll");
        index = direction == ScrollDirection.Up ? Math.Max(0, index - 1) : Math.Min(pages.Count - 1, index + 1);
        return Task.FromResult(true);
    }
}
```

Add this page helper inside `SidebarScrollControllerTests`:

```csharp
private static AutomationSnapshot Page(string key, string? targetTitle = null, bool targetOffscreen = false)
{
    var nodes = new List<AutomationNode>();
    for (var item = 0; item < 3; item++)
        nodes.Add(new($"{key}-{item}", "ControlType.ListItem", $"{key}-filler-{item}", "",
            new Rect(10, 20 + item * 40, 200, 30), false, ["root", "sidebar"], item));
    if (targetTitle is not null)
        nodes.Add(new($"{key}-target", "ControlType.ListItem", targetTitle, "",
            targetOffscreen ? Rect.Empty : new Rect(10, 160, 200, 30), targetOffscreen,
            ["root", "sidebar"], 4));
    return new AutomationSnapshot(new Rect(0, 0, 1000, 800), nodes);
}
```

Append these deterministic tests:

```csharp
[Fact]
public async Task Reveal_MissingSidebarRegionFailsWithoutInput()
{
    var environment = FakeAutomationEnvironment.WithPages(0,
        new AutomationSnapshot(new Rect(0, 0, 1000, 800), []));
    var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8))
        .RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);
    Assert.Equal(SidebarScrollStatus.RegionUnavailable, result.Status);
    Assert.Empty(environment.Actions);
}

[Fact]
public async Task Reveal_UsesPhysicalFallbackOnlyAfterEarlierModesRejectInput()
{
    var environment = FakeAutomationEnvironment.WithModes(1, [SidebarInputMode.PhysicalFallback],
        Page("top"), Page("middle"), Page("target", targetTitle: "Wanted"));
    var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8))
        .RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);
    Assert.Equal(SidebarScrollStatus.Found, result.Status);
    Assert.Equal(
        [SidebarInputMode.AutomationPattern, SidebarInputMode.PostedMessage, SidebarInputMode.PhysicalFallback],
        environment.Modes.Distinct());
}

[Fact]
public async Task Reveal_StopsAtEightyScrollAttempts()
{
    var pages = Enumerable.Range(0, 100).Select(index => Page($"page-{index}")).ToArray();
    var environment = FakeAutomationEnvironment.WithPages(99, pages);
    var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromMinutes(1))
        .RevealAsync(123, new SidebarTarget("missing", SidebarThreadGroup.Projectless()), default);
    Assert.Equal(SidebarScrollStatus.NotFound, result.Status);
    Assert.Equal(80, environment.Directions.Count);
}

[Fact]
public async Task Reveal_ZeroTimeoutIsDeterministicallyTimedOut()
{
    var environment = FakeAutomationEnvironment.WithPages(0, Page("top"));
    var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.Zero)
        .RevealAsync(123, new SidebarTarget("missing", SidebarThreadGroup.Projectless()), default);
    Assert.Equal(SidebarScrollStatus.TimedOut, result.Status);
}

[Fact]
public async Task Reveal_HonorsCancellationBeforeInput()
{
    var environment = FakeAutomationEnvironment.WithPages(0, Page("top"));
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8))
            .RevealAsync(123, new SidebarTarget("missing", SidebarThreadGroup.Projectless()), cancellation.Token));
    Assert.Empty(environment.Actions);
}

[Fact]
public async Task Reveal_OffscreenTargetBecomesVisibleBeforeSuccess()
{
    var environment = FakeAutomationEnvironment.WithPages(0,
        Page("top", "Wanted", targetOffscreen: true), Page("visible", "Wanted"));
    var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8))
        .RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);
    Assert.Equal(SidebarScrollStatus.Found, result.Status);
    Assert.False(result.Node!.IsOffscreen);
}
```

- [ ] **Step 7: Run focused and full tests**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter FullyQualifiedName~SidebarScrollControllerTests
dotnet test windows/CodexTaskMonitor.sln
```

Expected: scroll tests and full suite pass; fake action history contains no click action.

- [ ] **Step 8: Commit**

```powershell
git add windows/CodexTaskMonitor.Windows/Automation windows/CodexTaskMonitor.Windows/Interop windows/CodexTaskMonitor.Tests/Automation windows/CodexTaskMonitor.Tests/Fakes
git commit -m "feat: reveal offscreen Codex tasks safely"
```

### Task 12: Integrate deep links, sidebar reveal, error priority, and application composition

**Files:**
- Create: `windows/CodexTaskMonitor.Windows/Services/CodexDeepLinkLauncher.cs`
- Create: `windows/CodexTaskMonitor.Windows/Automation/WindowsSidebarRevealer.cs`
- Create: `windows/CodexTaskMonitor.Windows/Services/ThreadActivationService.cs`
- Modify: `windows/CodexTaskMonitor.Windows/ViewModels/MonitorViewModel.cs`
- Modify: `windows/CodexTaskMonitor.Windows/App.xaml.cs`
- Modify: `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs`
- Test: `windows/CodexTaskMonitor.Tests/Services/ThreadActivationServiceTests.cs`
- Test: `windows/CodexTaskMonitor.Tests/ViewModels/MonitorErrorPriorityTests.cs`

**Interfaces:**
- Consumes: all core monitor, preference, startup, UIA, matcher, and scroll interfaces from Tasks 1–11.
- Produces: a runnable end-to-end Windows monitor with safe action-error priority.

- [ ] **Step 1: Write failing activation-order and safe-degradation tests**

Create `windows/CodexTaskMonitor.Tests/Services/ThreadActivationServiceTests.cs`:

```csharp
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Windows.Automation;
using CodexTaskMonitor.Windows.Services;

namespace CodexTaskMonitor.Tests.Services;

public sealed class ThreadActivationServiceTests
{
    [Fact]
    public async Task Activate_OpensDeepLinkBeforeSidebarReveal()
    {
        var calls = new List<string>();
        var service = new ThreadActivationService(
            new FakeDeepLink(true, calls), new FakeRevealer(null, calls), new FakeDiagnostics(), TimeProvider.System);
        var error = await service.ActivateAsync(Item(), default);
        Assert.Null(error);
        Assert.Equal(["open", "reveal"], calls);
    }

    [Fact]
    public async Task DeepLinkFailure_SkipsSidebarReveal()
    {
        var calls = new List<string>();
        var service = new ThreadActivationService(
            new FakeDeepLink(false, calls), new FakeRevealer(null, calls), new FakeDiagnostics(), TimeProvider.System);
        Assert.Equal("无法打开对应的 Codex 对话", await service.ActivateAsync(Item(), default));
        Assert.Equal(["open"], calls);
    }

    private static MonitorItem Item() => new(
        "11111111-1111-4111-8111-111111111111", "turn", "Task", @"C:\work", "work",
        DateTimeOffset.UtcNow, TaskState.Waiting);
}
```

Add the activation fakes inside `ThreadActivationServiceTests`, immediately before its final `}`:

```csharp
private sealed class FakeDeepLink(bool succeeds, List<string> calls) : ICodexDeepLinkLauncher
{
    public bool Open(string threadId) { calls.Add("open"); return succeeds; }
}

private sealed class FakeRevealer(string? result, List<string> calls) : IWindowsSidebarRevealer
{
    public Task<string?> RevealAsync(MonitorItem item, CancellationToken token)
    {
        calls.Add("reveal");
        return Task.FromResult(result);
    }
}

private sealed class FakeDiagnostics : ILocalDiagnostics
{
    public List<string> Categories { get; } = [];
    public Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token)
    {
        Categories.Add(category);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run focused tests and verify the red state**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter "FullyQualifiedName~ThreadActivationServiceTests|FullyQualifiedName~MonitorErrorPriorityTests"
```

Expected: FAIL to compile because deep-link, revealer, and activation services do not exist.

- [ ] **Step 3: Implement deep-link and reveal orchestration**

Create `Services/CodexDeepLinkLauncher.cs`:

```csharp
using System.Diagnostics;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Windows.Services;

public interface ICodexDeepLinkLauncher { bool Open(string threadId); }

public sealed class CodexDeepLinkLauncher : ICodexDeepLinkLauncher
{
    public bool Open(string threadId)
    {
        if (!CodexThreadLink.TryCreate(threadId, out var uri)) return false;
        try { Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true }); return true; }
        catch { return false; }
    }
}
```

Create `Automation/WindowsSidebarRevealer.cs`:

```csharp
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Windows.Automation;

public interface IWindowsSidebarRevealer { Task<string?> RevealAsync(MonitorItem item, CancellationToken token); }

public sealed class WindowsSidebarRevealer(
    IChatGptWindowLocator windows,
    SidebarScrollController scroller,
    string sessionIndexPath,
    string globalStatePath,
    CodexTaskMonitor.Core.Data.IThreadGroupingLookup groupingLookup,
    TimeProvider time) : IWindowsSidebarRevealer
{
    public async Task<string?> RevealAsync(MonitorItem item, CancellationToken token)
    {
        var deadline = time.GetTimestamp() + (long)(TimeSpan.FromSeconds(5).TotalSeconds * time.TimestampFrequency);
        nint handle;
        while ((handle = windows.FindMainWindow()) == 0)
        {
            if (time.GetTimestamp() >= deadline) return "已打开对话；暂时无法在侧栏定位";
            await Task.Delay(250, time, token);
        }
        var resolution = await Task.Run(async () =>
        {
            var grouping = await groupingLookup.FindGroupingAsync(item.ThreadId, token);
            if (grouping is null) return (GroupingFound: false, Target: (SidebarTarget?)null);
            var target = SidebarTargetResolver.Resolve(
                item.ThreadId, grouping,
                await File.ReadAllBytesAsync(sessionIndexPath, token),
                await File.ReadAllBytesAsync(globalStatePath, token));
            return (GroupingFound: true, Target: target);
        }, token);
        if (!resolution.GroupingFound) return "已打开对话；无法确定侧栏分组";
        if (resolution.Target is null) return "已打开对话；无法读取 Codex 会话索引";
        SidebarScrollResult result;
        while (true)
        {
            try { result = await scroller.RevealAsync(handle, resolution.Target, token); break; }
            catch (InvalidOperationException) when (time.GetTimestamp() < deadline)
            {
                await Task.Delay(250, time, token);
            }
        }
        return result.Status switch
        {
            SidebarScrollStatus.Found => null,
            SidebarScrollStatus.Ambiguous => "已打开对话；侧栏有同名任务，已停止定位",
            SidebarScrollStatus.RegionUnavailable => "已打开对话；Codex 侧栏结构已变化",
            _ => "已打开对话；暂时无法在侧栏定位"
        };
    }
}
```

Create `Services/ThreadActivationService.cs`:

```csharp
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Windows.Automation;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows.Services;

public sealed class ThreadActivationService(
    ICodexDeepLinkLauncher links,
    IWindowsSidebarRevealer revealer,
    ILocalDiagnostics diagnostics,
    TimeProvider time)
    : IThreadActivationService
{
    private CancellationTokenSource? activeReveal;

    public async Task<string?> ActivateAsync(MonitorItem item, CancellationToken token)
    {
        var started = time.GetTimestamp();
        activeReveal?.Cancel();
        activeReveal?.Dispose();
        activeReveal = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (!links.Open(item.ThreadId))
        {
            await diagnostics.WriteAsync("deep-link-failed", time.GetElapsedTime(started), 1, CancellationToken.None);
            return "无法打开对应的 Codex 对话";
        }
        try
        {
            var message = await revealer.RevealAsync(item, activeReveal.Token);
            await diagnostics.WriteAsync(message is null ? "reveal-ok" : "reveal-warning", time.GetElapsedTime(started), 1, CancellationToken.None);
            return message;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (OperationCanceledException) { throw; }
        catch
        {
            await diagnostics.WriteAsync("reveal-error", time.GetElapsedTime(started), 1, CancellationToken.None);
            return "已打开对话；暂时无法在侧栏定位";
        }
    }
}
```

- [ ] **Step 4: Separate scan errors from action errors in the view model**

Replace the single mutable error source with:

```csharp
private string? scanErrorMessage;
private string? actionErrorMessage;
public string? ErrorMessage => actionErrorMessage ?? scanErrorMessage;
public bool HasError => ErrorMessage is not null;

private void SetScanError(string? value)
{
    if (scanErrorMessage == value) return;
    scanErrorMessage = value;
    RaiseErrorProperties();
}

private void SetActionError(string? value)
{
    if (actionErrorMessage == value) return;
    actionErrorMessage = value;
    RaiseErrorProperties();
}

private void RaiseErrorProperties()
{
    OnPropertyChanged(nameof(ErrorMessage));
    OnPropertyChanged(nameof(HasError));
    OnPropertyChanged(nameof(PanelHeight));
}

public async Task OpenAsync(MonitorItemViewModel item, CancellationToken token)
{
    SetActionError(null);
    SetActionError(await activation.ActivateAsync(item.Item, token));
}
```

Change the success and catch paths in `RefreshAsync` to:

```csharp
nextPollDelay = TimeSpan.FromSeconds(2);
SetScanError(result.UnreadableRolloutCount == 0
    ? null
    : $"{result.UnreadableRolloutCount} 个任务暂时无法读取");
// ...
catch (OperationCanceledException) { throw; }
catch (Exception error)
{
    nextPollDelay = error is CodexTaskMonitor.Core.Data.CodexDataException
        { Error: CodexTaskMonitor.Core.Data.CodexDataError.DatabaseMissing }
        ? TimeSpan.FromSeconds(10)
        : TimeSpan.FromSeconds(2);
    SetScanError(UserMessage(error));
}
```

Create `windows/CodexTaskMonitor.Tests/ViewModels/MonitorErrorPriorityTests.cs`:

```csharp
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Tests.ViewModels;

public sealed class MonitorErrorPriorityTests
{
    [Fact]
    public async Task RefreshCannotEraseActionWarning_AndLaterSuccessClearsIt()
    {
        var item = new MonitorItem("thread", "turn", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting);
        var activation = new SequencedActivation("已打开对话；暂时无法在侧栏定位", null);
        var model = new MonitorViewModel(
            new StaticMonitor(item),
            new MemoryPreferences(new(DateTimeOffset.UtcNow.AddHours(-1), [], [], [], null, null, false)),
            activation, new DisabledStartup(), new NoLaunchTime(), TimeProvider.System);
        await model.StartAsync(false, default);

        await model.OpenAsync(model.Items.Single(), default);
        await model.RefreshAsync(default);
        Assert.Equal("已打开对话；暂时无法在侧栏定位", model.ErrorMessage);

        await model.OpenAsync(model.Items.Single(), default);
        Assert.Null(model.ErrorMessage);
    }

    private sealed class StaticMonitor(params MonitorItem[] items) : ITaskMonitor
    {
        public Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken token) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken token) =>
            Task.FromResult(new MonitorScanResult(items, 0));
    }

    private sealed class MemoryPreferences(MonitorPreferences value) : IMonitorPreferencesStore
    {
        private MonitorPreferences current = value;
        public Task<MonitorPreferences> LoadAsync(CancellationToken token) => Task.FromResult(current);
        public Task SaveAsync(MonitorPreferences preferences, CancellationToken token) { current = preferences; return Task.CompletedTask; }
    }

    private sealed class SequencedActivation(params string?[] messages) : IThreadActivationService
    {
        private readonly Queue<string?> queue = new(messages);
        public Task<string?> ActivateAsync(MonitorItem item, CancellationToken token) => Task.FromResult(queue.Dequeue());
    }

    private sealed class DisabledStartup : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class NoLaunchTime : ICodexLaunchTimeProvider
    {
        public DateTimeOffset? GetLaunchTime() => null;
    }
}
```

- [ ] **Step 5: Complete the composition root and window handlers**

In `App.xaml`, remove `StartupUri`. In `App.xaml.cs`, acquire `SingleInstanceService`, construct paths from `Environment.SpecialFolder.UserProfile` and `LocalApplicationData`, instantiate one `SqliteThreadStore` and pass it as both `IThreadStore` and `IThreadGroupingLookup`, then instantiate `TaskMonitor`, preferences, system services, UIA provider, native wheel input, scroll controller, revealer, activation service, and `MonitorViewModel`. Set `MainWindow.DataContext`, connect `QuitRequested` to `Shutdown`, start the model after the window is shown, and dispose the model and mutex on exit. All database calls remain asynchronous and off the WPF UI thread.

Use this composition root:

```xml
<!-- App.xaml -->
<Application x:Class="CodexTaskMonitor.Windows.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
  <Application.Resources />
</Application>
```

```csharp
// App.xaml.cs
using System.Security.Principal;
using System.Windows;
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;
using CodexTaskMonitor.Windows.Automation;
using CodexTaskMonitor.Windows.Interop;
using CodexTaskMonitor.Windows.Services;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows;

public partial class App : Application
{
    private SingleInstanceService? singleInstance;
    private MonitorViewModel? model;
    private bool activationPending;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        singleInstance = SingleInstanceService.TryAcquire($"CodexTaskMonitor.{sid}");
        if (!singleInstance.IsOwner) { Shutdown(); return; }
        singleInstance.ActivationRequested += OnActivationRequested;

        var paths = CodexDataPaths.ForHome(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var threads = new SqliteThreadStore(paths.DatabasePath);
        var monitor = new CodexTaskMonitor.Core.Monitoring.TaskMonitor(threads);
        var preferences = new MonitorPreferencesStore(paths.PreferencesPath);
        var diagnostics = new LocalDiagnostics(paths.LogDirectory);
        var snapshots = new UiAutomationSnapshotProvider();
        var scroll = new SidebarScrollController(
            snapshots,
            new SidebarScrollInput(new UiAutomationSidebarScrollInput(), new NativeSidebarWheelInput()),
            TimeProvider.System, TimeSpan.FromMilliseconds(100), 80, TimeSpan.FromSeconds(8));
        var revealer = new WindowsSidebarRevealer(
            new ChatGptWindowLocator(), scroll, paths.SessionIndexPath, paths.GlobalStatePath,
            threads, TimeProvider.System);
        var activation = new ThreadActivationService(
            new CodexDeepLinkLauncher(), revealer, diagnostics, TimeProvider.System);
        var startup = new StartupRegistration(new RegistryRunValueStore(), Environment.ProcessPath!);
        model = new MonitorViewModel(
            monitor, preferences, activation, startup, new CodexLaunchTimeProvider(), TimeProvider.System);

        var window = new MainWindow { DataContext = model };
        MainWindow = window;
        model.QuitRequested += (_, _) => { window.RequestExit(); Shutdown(); };
        await model.StartAsync(startPollingLoop: true, CancellationToken.None);
        window.Show();
        if (activationPending) ActivateMainWindow();
    }

    private void OnActivationRequested(object? sender, EventArgs e) =>
        _ = Dispatcher.InvokeAsync(ActivateMainWindow);

    private void ActivateMainWindow()
    {
        if (MainWindow is not Window window) { activationPending = true; return; }
        activationPending = false;
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (model is not null) await model.DisposeAsync();
        if (singleInstance is not null) singleInstance.ActivationRequested -= OnActivationRequested;
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
```

Keep the Task 8 menu/open/dismiss/header handlers unchanged. The final event-handler set is:

```csharp
private async void Open_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button { Tag: MonitorItemViewModel item } && DataContext is MonitorViewModel model)
        await model.OpenAsync(item, CancellationToken.None);
}

private async void Dismiss_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button { Tag: MonitorItemViewModel item } && DataContext is MonitorViewModel model)
        await model.DismissAsync(item, CancellationToken.None);
}

private void More_Click(object sender, RoutedEventArgs e)
{
    if (sender is not Button { ContextMenu: { } menu } button) return;
    menu.PlacementTarget = button;
    menu.IsOpen = true;
}

private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    if (e.ButtonState == MouseButtonState.Pressed) DragMove();
}
```

The Task 8 XAML already binds “登录时启动” to `ToggleStartupCommand` and `IsStartupEnabled`. Preserve those bindings so the nullable first-run preference defaults to enabled once, while an explicit disabled choice survives later launches.

- [ ] **Step 6: Run focused, full, and Release verification**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln --filter "FullyQualifiedName~ThreadActivationServiceTests|FullyQualifiedName~MonitorErrorPriorityTests"
dotnet test windows/CodexTaskMonitor.sln
dotnet build windows/CodexTaskMonitor.sln -c Release
```

Expected: activation/error tests and full suite pass; Release build has zero warnings.

- [ ] **Step 7: Commit**

```powershell
git add windows/CodexTaskMonitor.Windows windows/CodexTaskMonitor.Tests
git commit -m "feat: integrate Windows Codex task activation"
```

### Task 13: Package the self-contained app and automate Windows CI releases

**Files:**
- Modify: `windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj`
- Create: `windows/Installer/CodexTaskMonitor.iss`
- Create: `.github/workflows/windows.yml`
- Test: generated `windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe`

**Interfaces:**
- Consumes: the runnable WPF app from Task 12.
- Produces: a per-user x64 installer, portable publish directory, CI artifact, and tag-release upload.

- [ ] **Step 1: Add deterministic publish metadata**

Add to `CodexTaskMonitor.Windows.csproj`:

```xml
<PropertyGroup>
  <Version>1.0.0</Version>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <DebugType>embedded</DebugType>
</PropertyGroup>
```

Run:

```powershell
dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows/publish/win-x64
& windows/publish/win-x64/CodexTaskMonitor.exe
```

Expected: the publish directory contains `CodexTaskMonitor.exe`; it launches without asking for a .NET runtime.

- [ ] **Step 2: Write the per-user Inno Setup installer**

Create `windows/Installer/CodexTaskMonitor.iss`:

```ini
#define MyAppName "Codex Task Monitor"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "TheoEquity"
#define MyAppExeName "CodexTaskMonitor.exe"

[Setup]
AppId={{8A3D60C5-243A-4C7C-9618-C965F9C05BF3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Codex Task Monitor
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=Codex-Task-Monitor-Windows-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "startup"; Description: "登录 Windows 时启动"; GroupDescription: "其他选项："; Flags: checkedonce

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CodexTaskMonitor"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CodexTaskMonitor');
end;
```

- [ ] **Step 3: Compile and inspect the installer**

Run:

```powershell
New-Item -ItemType Directory -Force -Path windows/artifacts | Out-Null
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows/Installer/CodexTaskMonitor.iss
Get-FileHash windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe -Algorithm SHA256
```

Expected: ISCC exits 0 and prints an installer path; SHA-256 is emitted for the generated `.exe`.

- [ ] **Step 4: Write the Windows GitHub Actions workflow**

Create `.github/workflows/windows.yml`:

```yaml
name: Windows

on:
  push:
    branches: [main]
    tags: ['v*']
    paths:
      - 'windows/**'
      - '.github/workflows/windows.yml'
      - 'global.json'
  pull_request:
    paths:
      - 'windows/**'
      - '.github/workflows/windows.yml'
      - 'global.json'
  workflow_dispatch:

permissions:
  contents: write

jobs:
  build-test-package:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.424'
          cache: true
          cache-dependency-path: |
            windows/**/*.csproj
            windows/Directory.Packages.props
            global.json
      - name: Restore
        run: dotnet restore windows/CodexTaskMonitor.sln
      - name: Test
        run: dotnet test windows/CodexTaskMonitor.sln -c Release --no-restore --logger "trx;LogFileName=windows-tests.trx"
      - name: Publish self-contained app
        run: dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true --no-restore -o windows/publish/win-x64
      - name: Install Inno Setup 7
        shell: pwsh
        run: winget install --exact --id JRSoftware.InnoSetup.7 --version 7.1.0 --source winget --silent --accept-source-agreements --accept-package-agreements
      - name: Build installer
        shell: pwsh
        run: "& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows/Installer/CodexTaskMonitor.iss"
      - name: Hash artifacts
        shell: pwsh
        run: Get-FileHash windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe -Algorithm SHA256 | Format-List
      - uses: actions/upload-artifact@v4
        with:
          name: Codex-Task-Monitor-Windows-x64
          path: |
            windows/publish/win-x64/**
            windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe
            windows/CodexTaskMonitor.Tests/TestResults/**
      - name: Create or update tag release
        if: startsWith(github.ref, 'refs/tags/v')
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          $tag = '${{ github.ref_name }}'
          gh release view $tag *> $null
          if ($LASTEXITCODE -ne 0) { gh release create $tag --title "Codex Task Monitor $tag" --generate-notes }
          gh release upload $tag windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe --clobber
```

- [ ] **Step 5: Validate workflow syntax and local packaging**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln -c Release
dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows/publish/win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows/Installer/CodexTaskMonitor.iss
Test-Path -LiteralPath windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe
```

Expected: tests and publish pass, ISCC exits 0, and `Test-Path` returns `True`.

- [ ] **Step 6: Commit**

```powershell
git add global.json windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj windows/Installer .github/workflows/windows.yml
git commit -m "build: package Windows task monitor"
```

### Task 14: Add target-machine preflight, manual acceptance, and Windows documentation

**Files:**
- Create: `windows/Scripts/verify_windows_environment.ps1`
- Create: `docs/windows-manual-test.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: packaged app and installer from Task 13.
- Produces: repeatable read-only environment evidence and the release acceptance record.

- [ ] **Step 1: Write the read-only Windows environment verifier**

Create `windows/Scripts/verify_windows_environment.ps1`:

```powershell
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$codexRoot = Join-Path $env:USERPROFILE '.codex'
$database = Join-Path $codexRoot 'state_5.sqlite'
$sessionIndex = Join-Path $codexRoot 'session_index.jsonl'
$globalState = Join-Path $codexRoot '.codex-global-state.json'
$requiredThreadColumns = @('id','rollout_path','cwd','title','archived','updated_at_ms','thread_source','source','preview','is_pinned','thread_section_id')
$requiredSectionColumns = @('id','name')
$actualThreadColumns = @()
$actualSectionColumns = @()
$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue

if ((Test-Path -LiteralPath $database) -and $null -ne $sqlite) {
    $actualThreadColumns = @(& $sqlite.Source -readonly $database 'PRAGMA table_info(threads);' |
        ForEach-Object { ($_ -split '\|')[1] })
    $actualSectionColumns = @(& $sqlite.Source -readonly $database 'PRAGMA table_info(thread_sections);' |
        ForEach-Object { ($_ -split '\|')[1] })
}

$protocol = Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\codex' -ErrorAction SilentlyContinue
$chatGpt = @(Get-Process ChatGPT -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0)
$result = [ordered]@{
    windows_11 = [Environment]::OSVersion.Version.Build -ge 22000
    x64_process = [Environment]::Is64BitProcess
    database_exists = Test-Path -LiteralPath $database
    sqlite_cli_available = $null -ne $sqlite
    session_index_exists = Test-Path -LiteralPath $sessionIndex
    global_state_exists = Test-Path -LiteralPath $globalState
    required_schema_present =
        @($requiredThreadColumns | Where-Object { $_ -notin $actualThreadColumns }).Count -eq 0 -and
        @($requiredSectionColumns | Where-Object { $_ -notin $actualSectionColumns }).Count -eq 0
    codex_protocol_registered = $null -ne $protocol
    chatgpt_main_window_found = $chatGpt.Count -ge 1
}

$result | ConvertTo-Json
if ($result.Values -contains $false) { exit 1 }
```

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/Scripts/verify_windows_environment.ps1
```

Expected on the validated machine: every JSON property is `true` and the script exits 0. The script prints no thread IDs, titles, paths outside fixed Codex locations, or user content.

- [ ] **Step 2: Write the exact manual acceptance checklist**

Create `docs/windows-manual-test.md` with checkboxes for these cases and an evidence field under each:

```markdown
# Windows 11 Manual Acceptance

- [ ] Per-user installer completes without elevation and app starts without a separate .NET runtime.
- [ ] A second launch does not create a second floating panel and re-shows/activates the existing panel if it was hidden.
- [ ] Starting the monitor while Codex is already running establishes the baseline without losing an active turn.
- [ ] Starting the monitor before Codex, then opening a task, launches Codex and waits up to 5 seconds for its UIA root.
- [ ] A new running Codex task appears within 4 seconds with a blue dot.
- [ ] Completion and abort both change the row to green “等待处理”.
- [ ] “已处理” hides only the selected `threadID:turnID`; two consecutive later turns each reappear.
- [ ] 7 or more rows cap the panel at 6 visible rows and show a vertical scrollbar.
- [ ] Clicking a visible unique task opens its exact thread and leaves the sidebar row visible.
- [ ] Clicking unique tasks above and below the current sidebar viewport opens each exact thread and reveals its row within 8 seconds.
- [ ] A pinned task, section task, project task, and projectless task each resolve in their correct group.
- [ ] Duplicate titles produce the ambiguity warning and no sidebar click.
- [ ] A missing session-index mapping opens the body and reports a sidebar warning.
- [ ] Closing/restarting Codex during reveal ends with a bounded warning, not continued scrolling.
- [ ] Login startup survives reboot; disabling it removes the HKCU Run value.
- [ ] Upgrade preserves handled-item settings.
- [ ] Uninstall removes program files and the HKCU Run value while leaving local settings/logs.
- [ ] Logs contain categories/counts/timings only and no task title or prompt text.

## Evidence

Record Windows build, Codex/ChatGPT package version, installer SHA-256, test commit SHA, and pass date.
```

- [ ] **Step 3: Document Windows build, install, privacy, and known boundaries in README**

Add a Windows section containing:

````markdown
## Windows 11

The Windows build is a separate .NET 8/WPF implementation under `windows/`. It reads the same local Codex state in `%USERPROFILE%\.codex` and uses the registered `codex://` protocol plus Windows UI Automation.

### Build

```powershell
dotnet test windows/CodexTaskMonitor.sln -c Release
dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows/publish/win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows/Installer/CodexTaskMonitor.iss
```

### Privacy and compatibility

The monitor reads Codex state locally and does not upload task data. Sidebar reveal depends on the current Windows Codex/ChatGPT UI Automation structure. Missing or ambiguous matches safely degrade to opening the thread body without clicking a sidebar item. The first installer is unsigned and may trigger Windows SmartScreen.

The panel follows the active Windows virtual desktop's native behavior; the first release does not use undocumented APIs to pin itself across every virtual desktop.
````

Keep the existing macOS requirements/build section intact; label platform-specific commands explicitly.

- [ ] **Step 4: Run automated release verification**

Run:

```powershell
dotnet test windows/CodexTaskMonitor.sln -c Release --logger "console;verbosity=normal"
dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows/publish/win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows/Installer/CodexTaskMonitor.iss
powershell -NoProfile -ExecutionPolicy Bypass -File windows/Scripts/verify_windows_environment.ps1
git diff --check
```

Expected: all tests pass with zero failures, publish and ISCC exit 0, environment verification emits only `true`, and `git diff --check` is empty.

- [ ] **Step 5: Perform and record current-machine acceptance**

Install `windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe`, execute every checkbox in `docs/windows-manual-test.md`, fill the evidence section, and capture failures as focused regression tests before changing production code. Re-run Step 4 after every correction.

- [ ] **Step 6: Commit the verified documentation and acceptance evidence**

```powershell
git add windows/Scripts/verify_windows_environment.ps1 docs/windows-manual-test.md README.md
git commit -m "docs: add Windows installation and acceptance guide"
```

---

## Final Verification Gate

Before declaring the Windows port complete, run from `D:\codex-task-monitor`:

```powershell
dotnet test windows/CodexTaskMonitor.sln -c Release --logger "console;verbosity=normal"
dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows/publish/win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows/Installer/CodexTaskMonitor.iss
powershell -NoProfile -ExecutionPolicy Bypass -File windows/Scripts/verify_windows_environment.ps1
git status --short
```

Completion requires: zero failed tests, successful self-contained publish, successful installer compilation, all preflight properties `true`, every manual-acceptance checkbox recorded as passed, and only intentionally generated/ignored build outputs in `git status`.

## Execution Handoff

Implement one task at a time in order. Each task begins with its stated failing test, ends with the full regression suite, and receives a review before the next task starts. Use an isolated worktree at execution time; do not implement directly in the clean `main` checkout.
