# Windows 固定宽度任务行实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 330 DIP 固定宽度面板中的超长任务标题在“已处理”按钮之前单行省略，按钮始终完整保留在任务行右侧。

**Architecture:** 通过禁用 `ListBox` 横向滚动，使任务行始终按可见视口宽度测量；继续使用状态点固定列、可收缩星号标题列和 Auto 按钮列。用真实 STA WPF 布局测试测量超长标题行，并验证标题与按钮的边界关系。

**Tech Stack:** .NET 8、C# 12、WPF/XAML、xUnit、PowerShell、Inno Setup 7.1.0。

## Global Constraints

- 面板宽度保持 330 DIP，任务内容行保持 62 DIP，左右边距保持 14 DIP。
- `ListBox` 垂直滚动继续为 `Auto`，横向滚动必须为 `Disabled`。
- 标题保持单行，使用 `CharacterEllipsis`；不改变标题、状态和项目名字号。
- “已处理”按钮使用 Auto 宽度，等待处理时完整可见，运行中仍折叠。
- 打开任务与“已处理”的点击区域不得重叠，现有命令和身份键不变。
- 不修改任务筛选、Fork、rollout、偏好、日志、侧栏自动化或 Swift/macOS。
- 生产 XAML 修改必须由当前版本上已失败的真实布局测试驱动。
- 人工视觉项保持未勾选；自动证据不能冒充真人视觉验收。

---

## 文件结构

- 修改 `windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs`：增加超长标题固定宽度布局回归和视觉树查询帮助方法。
- 修改 `windows/CodexTaskMonitor.Windows/MainWindow.xaml`：约束任务列表与标题列的水平测量。
- 修改 `docs/windows-manual-test.md`：重新生成安装包后记录测试数、哈希、大小和自动布局证据。

### Task 1: 约束超长标题并固定“已处理”按钮

**Files:**
- Modify: `windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs`
- Modify: `windows/CodexTaskMonitor.Windows/MainWindow.xaml:35-62`

**Interfaces:**
- Consumes: `MainWindow.TaskList`、`TaskList.ItemTemplate`、`MonitorItemViewModel`。
- Produces: 不新增生产接口；任务行保持现有绑定和点击事件。

- [ ] **Step 1: 编写真实 WPF 布局失败测试**

在 `MainWindowXamlTests` 增加以下测试：

```csharp
[Fact]
public void LongTaskTitle_StaysBeforeVisibleDismissButtonAtFixedWidth()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        MainWindow? window = null;
        try
        {
            window = new MainWindow();
            var taskList = Assert.IsType<ListBox>(window.FindName("TaskList"));
            var longTitle = new string('W', 200);
            taskList.ItemsSource =
            [
                new MonitorItemViewModel(new MonitorItem(
                    "thread",
                    "turn",
                    longTitle,
                    @"C:\work",
                    "work",
                    DateTimeOffset.UtcNow,
                    TaskState.Waiting))
            ];

            taskList.Measure(new Size(330, 63));
            taskList.Arrange(new Rect(0, 0, 330, 63));
            taskList.ApplyTemplate();
            taskList.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            taskList.UpdateLayout();

            var item = Assert.IsType<ListBoxItem>(taskList.ItemContainerGenerator.ContainerFromIndex(0));
            var title = Assert.Single(
                VisualDescendants<TextBlock>(item),
                element => element.Text == longTitle);
            var dismiss = Assert.Single(
                VisualDescendants<Button>(item),
                element => Equals(element.Content, "已处理"));
            var titleBounds = title.TransformToAncestor(taskList).TransformBounds(
                new Rect(new Point(), title.RenderSize));
            var dismissBounds = dismiss.TransformToAncestor(taskList).TransformBounds(
                new Rect(new Point(), dismiss.RenderSize));

            Assert.Equal(Visibility.Visible, dismiss.Visibility);
            Assert.True(dismiss.ActualWidth > 0);
            Assert.True(
                dismissBounds.Right <= taskList.ActualWidth,
                $"Dismiss button right edge {dismissBounds.Right} exceeded list width {taskList.ActualWidth}.");
            Assert.True(
                titleBounds.Right <= dismissBounds.Left,
                $"Title right edge {titleBounds.Right} overlapped dismiss button at {dismissBounds.Left}.");
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);
            Assert.Equal(
                ScrollBarVisibility.Disabled,
                ScrollViewer.GetHorizontalScrollBarVisibility(taskList));
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            window?.RequestExit();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);

    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA layout thread did not finish.");
    Assert.Null(failure);
}
```

并在测试类末尾增加：

```csharp
private static IEnumerable<T> VisualDescendants<T>(DependencyObject root)
    where T : DependencyObject
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        var child = VisualTreeHelper.GetChild(root, index);
        if (child is T match)
            yield return match;

        foreach (var descendant in VisualDescendants<T>(child))
            yield return descendant;
    }
}
```

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~MainWindowXamlTests.LongTaskTitle_StaysBeforeVisibleDismissButtonAtFixedWidth"
```

预期：FAIL。当前 `ListBox` 横向滚动不是 `Disabled`，且超长标题的自然宽度允许任务行水平扩张；失败必须来自按钮边界、标题重叠或横向滚动断言，不能来自未生成容器、数据绑定或线程超时。

- [ ] **Step 3: 写入最小 XAML 布局修改**

把任务列表声明改为：

```xml
<ListBox x:Name="TaskList" Grid.Row="1" ItemsSource="{Binding Items}" MaxHeight="378"
         Background="Transparent" BorderThickness="0"
         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
         ScrollViewer.VerticalScrollBarVisibility="Auto"
         automation:AutomationProperties.Name="Codex 任务列表">
```

把行 Grid 的列定义展开为：

```xml
<Grid.ColumnDefinitions>
  <ColumnDefinition Width="18" />
  <ColumnDefinition Width="*" MinWidth="0" />
  <ColumnDefinition Width="Auto" />
</Grid.ColumnDefinitions>
```

把打开任务按钮增加水平收缩和裁剪属性：

```xml
<Button Grid.Column="1" Tag="{Binding}" Click="Open_Click" Background="Transparent" BorderThickness="0"
        MinWidth="0" ClipToBounds="True" HorizontalContentAlignment="Stretch"
        ToolTip="打开此 Codex 任务" automation:AutomationProperties.Name="打开任务">
```

把标题 TextBlock 明确保持单行省略：

```xml
<TextBlock Text="{Binding Title}" Foreground="White" FontSize="13"
           TextWrapping="NoWrap" TextTrimming="CharacterEllipsis" />
```

- [ ] **Step 4: 运行聚焦 GREEN**

```powershell
dotnet test windows\CodexTaskMonitor.Tests\CodexTaskMonitor.Tests.csproj -c Release --filter "FullyQualifiedName~MainWindowXamlTests"
```

预期：全部 `MainWindowXamlTests` PASS；超长标题不会越过按钮，按钮在 330 DIP 任务列表内保持完整可见，现有标题栏拖动和只读绑定继续通过。

- [ ] **Step 5: 提交布局修复**

```powershell
git add windows/CodexTaskMonitor.Windows/MainWindow.xaml windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs
git diff --cached --check
git -c user.name='Codex Agent' -c user.email='codex-agent@localhost' commit -m "fix: constrain task rows to panel width"
```

### Task 2: 完整验证、重新打包并升级安装

**Files:**
- Modify: `docs/windows-manual-test.md:45-75`
- Generate but keep ignored: `windows/publish/win-x64/CodexTaskMonitor.exe`
- Generate but keep ignored: `windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe`

**Interfaces:**
- Consumes: `verify_windows_environment.ps1`、`verify_windows_packaging.ps1`、Inno Setup 7.1.0。
- Produces: 新的单文件应用、安装包 SHA-256、per-user 升级和自动布局证据。

- [ ] **Step 1: 运行完整 Release 验证**

```powershell
dotnet test windows\CodexTaskMonitor.sln -c Release
dotnet build windows\CodexTaskMonitor.sln -c Release -warnaserror
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_environment.ps1
git diff --check
```

预期：完整测试 0 failed；构建 0 warnings/0 errors；环境预检 9 项为 `true`；diff check 无输出。

- [ ] **Step 2: 生成一次应用和安装包并验证**

先确认现有 ignored 输出只包含预期文件，然后覆盖生成一次：

```powershell
dotnet publish windows\CodexTaskMonitor.Windows\CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true -o windows\publish\win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows\Installer\CodexTaskMonitor.iss
$installer = 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $installer).Length
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_packaging.ps1 -RequireOutputs -ExpectedInstallerSha256 $hash
"SHA256=$hash SIZE=$size"
```

预期：publish 目录只含 `CodexTaskMonitor.exe`；ISCC 成功；应用和安装包为 PE/MZ、`NotSigned`；哈希验证通过。

- [ ] **Step 3: 静默升级并验证单实例存活**

```powershell
Get-Process -Name CodexTaskMonitor -ErrorAction SilentlyContinue | Stop-Process -Force
$installer = (Resolve-Path 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe').Path
$install = Start-Process -FilePath $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Installer exit code: $($install.ExitCode)" }
$app = Join-Path $env:LOCALAPPDATA 'Programs\Codex Task Monitor\CodexTaskMonitor.exe'
Start-Process -FilePath $app
Start-Sleep -Seconds 5
if (@(Get-Process -Name CodexTaskMonitor -ErrorAction SilentlyContinue).Count -ne 1) { throw 'Expected one running monitor process.' }
```

预期：安装器退出码 0，安装文件与本次 publish 一致，HKCU Run 值存在，一个监视器进程运行。此步骤不操作 Codex/ChatGPT UI。

- [ ] **Step 4: 更新证据并提交**

使用 `apply_patch` 更新 `docs/windows-manual-test.md` 中测试总数、测试提交、安装包 SHA-256、字节大小和固定宽度布局自动证据。保留所有人工 UI 项为 `[ ]`。

```powershell
git add docs/windows-manual-test.md
git diff --cached --check
git -c user.name='Codex Agent' -c user.email='codex-agent@localhost' commit -m "docs: record fixed-width task row build"
```

- [ ] **Step 5: 最终一致性验证**

```powershell
$hash = (Get-FileHash -LiteralPath 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe' -Algorithm SHA256).Hash
powershell -NoProfile -ExecutionPolicy Bypass -File windows\Scripts\verify_windows_packaging.ps1 -RequireOutputs -ExpectedInstallerSha256 $hash
dotnet test windows\CodexTaskMonitor.sln -c Release --no-restore
dotnet build windows\CodexTaskMonitor.sln -c Release -warnaserror --no-restore
git diff --check
git show --check --oneline HEAD
git status --short
```

预期：包装哈希一致；完整测试和构建通过；tracked 工作树干净。最终说明自动布局测试已通过，但真实视觉确认仍由用户查看面板。
