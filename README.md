# Codex Task Monitor

一个只在本机运行的 macOS 常驻小窗，用来查看当前 Codex 中所有非空闲任务。

## 功能

- 显示运行中和等待处理的 Codex 任务。
- 运行中任务使用蓝点，已完成但未处理的任务使用绿点。
- 超过 6 个任务时固定窗口高度并显示滚动条。
- 点击任务会打开对应 Codex 正文，并把 Codex 侧栏滚动到对应任务。
- 点击“已处理”只移除对应任务。
- 自动注册为登录时启动。

## 要求（macOS）

- macOS 26 或更高版本。
- 已安装并运行 Codex 桌面应用。
- 首次使用侧栏定位时，在系统设置中授予辅助功能权限。

默认情况下，所有任务数据均从本机 `~/.codex` 只读获取，不上传到外部服务。Windows 用户主动完成下述 Bark 一次性接入后，应用只会把任务标题、项目名和完成/中止状态发送到用户配置的 Bark 服务，并由其转交 Apple 推送服务。

## 构建（macOS）

```bash
swift run -Xswiftc -warnings-as-errors CoreChecks
Scripts/package_app.sh
```

打包结果位于 `outputs/Codex Task Monitor.app`。将应用放到固定路径后启动一次，应用会尝试注册为登录项。

## 已知边界

Codex 的 macOS 辅助功能树不提供任务 UUID。监控器会先通过官方 `codex://threads/<UUID>` 深链精确打开正文，再按当前项目和完整标题定位侧栏；同一分组存在重名任务或旧任务缺少会话索引时，只打开正文，不猜测侧栏位置。

## Windows 11

The Windows build is a separate .NET 8/WPF implementation under `windows/`. It reads the same
local Codex state in `%USERPROFILE%\.codex` and uses the registered `codex://` protocol plus
Windows UI Automation. The macOS requirements and build commands above remain unchanged.

### Windows build and install

Run these commands in PowerShell on Windows 11 x64:

```powershell
dotnet test windows/CodexTaskMonitor.sln -c Release
dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows/publish/win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows/Installer/CodexTaskMonitor.iss
```

The resulting per-user installer is
`windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe`. It installs without elevation to
the current user's local application directory and includes the .NET runtime required by the
application.

### Optional Bark notifications

The Windows build can send an iPhone notification when a Codex task completes or is aborted.
There is no in-app Bark settings screen. Provision the current Windows account once:

1. Exit Codex Task Monitor.
2. Copy a complete sample URL from Bark on the iPhone, such as its `Body Text` sample, to the
   Windows clipboard.
3. Run the script installed with the app using Windows PowerShell 5.1, which is included with
   Windows 11:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\Codex Task Monitor\Scripts\provision_bark.ps1"
   ```

The script sends a fixed test notification before saving anything. On success it stores only an
encrypted endpoint in `%LOCALAPPDATA%\CodexTaskMonitor\bark-secret.dat` using Windows DPAPI
CurrentUser scope, plus non-secret delivery state in `bark-state.json`. It never prints the Bark
URL or accepts it as a command-line argument.

Normal completion notifications use the task title as the notification title and the project name
as the body. Aborted tasks append `· 已中止` to the body. Successfully delivered task identities
are retained locally to prevent duplicate pushes across polling and restarts.
Delivery is at-least-once: if Bark accepts a notification but Windows cannot persist its identity,
the current process suppresses another send, but a later app restart may send it again.

### Privacy and compatibility

The monitor reads Codex state locally. Unless the user has provisioned Bark, it does not upload task
data. When Bark is provisioned, only the task title, project name, terminal status, and fixed
notification group leave the machine; thread IDs, turn IDs, prompts, rollout contents, full working
directory paths, and Codex credentials are never included. The target-machine verifier uses
`sqlite3` only in read-only mode and emits only fixed boolean fields. Sidebar reveal depends on the
current Windows Codex/ChatGPT UI Automation structure. Missing or ambiguous matches safely degrade
to opening the thread body without clicking a sidebar item. The first installer is unsigned and may
trigger Windows SmartScreen.

The panel follows the active Windows virtual desktop's native behavior; the first release does
not use undocumented APIs to pin itself across every virtual desktop. See
`docs/windows-manual-test.md` for the acceptance checklist and the checks that still require a
real Codex interaction, visual confirmation, or reboot.
