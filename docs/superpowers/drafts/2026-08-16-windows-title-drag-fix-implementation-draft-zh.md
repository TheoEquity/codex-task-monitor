# `WIN-DRAG-001` Windows 标题栏拖动修复——方法级实施草案

## 修订信息

- 草案版本：`2`
- 需求来源：`docs/superpowers/specs/2026-08-16-windows-title-drag-fix-design.md`
- 取代版本：`docs/superpowers/drafts/2026-08-16-windows-title-drag-fix-implementation-draft.md`（英文第 1 版）
- 修订说明：在不改变技术范围的前提下，将实施草案完整改写为中文；方法、资源、步骤、日志及验收边界保持一致。

## 需求定义

用户在现有 48 DIP 高的标题栏空白区域按住鼠标左键并拖动时，`MainWindow` 必须随之移动。“更多”按钮仍需保持原有点击行为，现有窗口位置延迟保存逻辑也必须保持不变。任务行、错误提示条和“更多”按钮本身不属于可拖动区域。

## 约束与不变量

- 保持窗口宽度 330 DIP、标题栏高度 48 DIP、无边框、置顶及现有视觉样式不变。
- 原样复用 `Header_MouseLeftButtonDown`、`Window.DragMove()`、`OnLocationChanged` 和 `MonitorViewModel.SaveWindowPositionAsync()`，不修改其行为。
- 不新增鼠标坐标日志、持久化字段、依赖项、重试机制或全局输入钩子。
- 18 项真实 UI、重启和登录验收必须由人工执行；执行前继续保持未勾选状态。

状态说明：表格中的 `Existing, reuse unchanged` 表示“现有，原样复用”，`Existing, modify` 表示“现有，需要修改”，`Planned new` 表示“计划新增”，`Unconfirmed` 表示“尚未确认”。

## 代码上下文核查

| 证据 | 位置 | 已确认事实 |
|---|---|---|
| 直接入口 | `windows/CodexTaskMonitor.Windows/MainWindow.xaml:15` | 第 0 行的标题栏 `Grid` 已绑定 `MouseLeftButtonDown`，但没有背景，因此空白像素不属于 WPF 鼠标命中区域。 |
| 调用方 | `windows/CodexTaskMonitor.Windows/App.xaml.cs:68-71` | 应用启动时创建 `MainWindow`、设置监视器视图模型并显示窗口。 |
| 被调用方 | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:117-120` | `Header_MouseLeftButtonDown` 先确认鼠标左键处于按下状态，再调用框架方法 `DragMove()`。 |
| 共享资源 | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:52-72` | `OnLocationChanged` 已对窗口移动事件进行防抖，并把最终坐标交给 `MonitorViewModel.SaveWindowPositionAsync()` 保存。 |
| 测试 | `windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs` | 测试项目已有 STA 线程模式，可安全加载 `MainWindow` 并执行真实 WPF 布局。 |
| 验收文档 | `docs/windows-manual-test.md` | 真实视觉和交互验收与自动化证据分开记录。 |

## 技术与共享资源

| 技术或资源 | 位置 | 状态 | 当前职责 | 计划用途或修改 |
|---|---|---|---|---|
| 标题栏 `Grid` | `windows/CodexTaskMonitor.Windows/MainWindow.xaml:15` | `Existing, modify`（现有，需要修改） | 承载标题文字和“更多”按钮；目前只能从可命中的子元素路由拖动事件 | 设置 `Background="Transparent"`，使空白区域参与鼠标命中，同时保持视觉不变。 |
| `MainWindow` 事件链 | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs` | `Existing, reuse unchanged`（现有，原样复用） | 启动系统窗口拖动，并在移动后保存位置 | 不改变行为。 |
| WPF 布局与命中测试 | .NET 8 WPF | `Existing, reuse unchanged`（现有，原样复用） | 测量视觉元素并解析指定坐标命中的元素 | 在 STA 测试线程中验证真实标题栏视觉树。 |
| `MainWindowXamlTests` | `windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs` | `Existing, modify`（现有，需要修改） | 覆盖运行时 XAML 绑定与布局行为 | 新增一条回归测试，无需新增测试程序集或测试框架。 |

## 方法

| 方法 | 位置 | 状态 | 当前职责 | 计划职责或修改 |
|---|---|---|---|---|
| `MainWindow()` | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:14-20` | `Existing, reuse unchanged`（现有，原样复用） | 加载 XAML 并订阅窗口生命周期事件 | 为回归测试提供真实编译后的标题栏视觉树。 |
| `Header_MouseLeftButtonDown(object, MouseButtonEventArgs)` | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:117-121` | `Existing, reuse unchanged`（现有，原样复用） | 仅在鼠标左键按下时启动 `DragMove()` | 标题栏空白区域变为可命中后，接收其路由事件。 |
| `Window.DragMove()` | .NET 8 WPF 框架 | `Existing, reuse unchanged`（现有，原样复用） | 执行系统原生的窗口交互式移动循环 | 不改变行为。 |
| `OnLocationChanged(object?, EventArgs)` | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:52-72` | `Existing, reuse unchanged`（现有，原样复用） | 对位置变化进行防抖并保存最终窗口坐标 | 不改变行为。 |
| `HeaderBlankArea_IsHitTestableForDragging()` | `windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs` | `Planned new`（计划新增） | 当前不存在 | 证明已批准的标题栏空白坐标能够命中标题栏 `Grid`；现有测试只覆盖任务行绑定，无法承担鼠标命中验证职责。 |

## 实施流程

### 复现标题栏空白区域无法命中的问题

- 目标：建立一条针对编译后 WPF 视觉树的失败回归测试，而不是仅搜索 XAML 文本。
- 方法：
  - `MainWindow()`——`Existing, reuse unchanged`（现有，原样复用）
  - `HeaderBlankArea_IsHitTestableForDragging()`——`Planned new`（计划新增）
- 共享资源：
  - `MainWindowXamlTests` 的 STA 线程模式——`Existing, modify`（现有，需要修改）
  - WPF 布局与命中测试——`Existing, reuse unchanged`（现有，原样复用）
- 方法协作：

  ```text
  在 STA 线程中：
      window = MainWindow()
      titleBar = 从窗口内容中取得第 0 行 Grid
      按 330 × 48 DIP 测量并排列 titleBar
      hit = titleBar.InputHitTest(标题文字与“更多”按钮之间的空白坐标)
      断言 hit 就是 titleBar
  ```

- 关键输入：标题栏内部且不落在标题文字或“更多”按钮上的稳定空白坐标。
- 关键输出或状态：当前无背景 `Grid` 上产生一条确定性的失败断言；不修改持久化状态。
- 成功条件：测试因空白坐标无法命中标题栏而失败，证明测试覆盖了用户报告的问题。
- 失败归属：STA 测试线程中的异常由 xUnit 断言自然传播。
- 日志点：不新增运行时日志；聚焦测试的通过或失败就是检查点，且不包含用户内容或鼠标坐标。

### 让整个标题栏空白区域进入现有拖动事件链

- 目标：让标题栏空白像素能够路由现有鼠标事件，同时不修改拖动方法。
- 方法：
  - `Header_MouseLeftButtonDown(object, MouseButtonEventArgs)`——`Existing, reuse unchanged`（现有，原样复用）
  - `Window.DragMove()`——`Existing, reuse unchanged`（现有，原样复用）
- 共享资源：
  - 标题栏 `Grid`——`Existing, modify`（现有，需要修改）
- 方法协作：

  ```text
  titleBar.Background = Transparent

  当标题栏空白区域收到鼠标左键按下事件时：
      路由事件到达 Header_MouseLeftButtonDown
      Header_MouseLeftButtonDown 调用 DragMove()

  当“更多”按钮收到输入时：
      Button 继续拥有其已处理的按下与点击事件链
  ```

- 关键输入：发生在标题栏空白区域或现有按钮上的 WPF 鼠标左键路由输入。
- 关键输出或状态：空白区域拖动时移动窗口；按钮点击行为保持不变。
- 成功条件：仅修改 XAML 背景属性后，回归测试由红转绿。
- 失败归属：沿用现有 WPF 路由输入边界，不新增恢复逻辑或重试。
- 日志点：保持诊断逻辑不变。鼠标移动属于高频输入，记录它会产生噪声和不必要的交互数据。

### 保持现有窗口位置保存链路

- 目标：确保新命中区域接入已经批准的窗口移动与位置保存流程，不新增状态。
- 方法：
  - `OnLocationChanged(object?, EventArgs)`——`Existing, reuse unchanged`（现有，原样复用）
  - `MonitorViewModel.SaveWindowPositionAsync(double, double, CancellationToken)`——`Existing, reuse unchanged`（现有，原样复用）
- 共享资源：
  - 现有 300 毫秒位置保存取消源——`Existing, reuse unchanged`（现有，原样复用）
  - 现有监视器偏好设置存储——`Existing, reuse unchanged`（现有，原样复用）
- 方法协作：

  ```text
  DragMove 改变 Window.Left 或 Window.Top
  OnLocationChanged 取消上一次延迟保存
  经过 300 毫秒后：
      SaveWindowPositionAsync(最终 Left, 最终 Top, token)
  现有错误边界显示固定、可恢复的错误消息
  ```

- 关键输入：WPF 移动循环产生的最终 `Left` 与 `Top` 值。
- 关键输出或状态：继续写入启动恢复位置时使用的原有偏好字段。
- 成功条件：现有偏好设置测试继续通过，并且无需修改任何生产 C# 方法。
- 失败归属：沿用 `OnLocationChanged` 的现有异常处理边界和 `MonitorViewModel.ReportActionFailure()` 行为。
- 日志点：保持现有隐私安全错误处理；不新增窗口位置或鼠标坐标日志。

## 失败与恢复

- 责任边界：拖动启动由 WPF 路由输入负责；位置保存失败由现有 `OnLocationChanged` 处理。
- 自然传播的失败：STA 回归测试初始化或 WPF 布局失败时，自动化测试直接失败。
- 业务层已处理的失败：沿用现有位置保存取消机制与固定操作错误提示。
- 重试标识与停止条件：不新增重试；现有 300 毫秒防抖只保留最后一次位置保存。
- 失败时必须保留的数据：若新的位置保存失败，先前已提交的窗口位置仍保持有效。
- 错误日志位置：不新增日志器；需求明确要求保持当前诊断逻辑，并排除坐标记录。

## 日志方案

| 主要边界 | 实际方法 | 记录时机 | 所需上下文 | 是否需要错误日志 |
|---|---|---|---|---|
| 空白区域命中回归 | `HeaderBlankArea_IsHitTestableForDragging()` | 聚焦测试结束 | 仅通过/失败结果 | 测试失败信息已足够 |
| 启动拖动 | `Header_MouseLeftButtonDown(...)` | 不新增记录 | 明确不收集鼠标坐标与 UI 内容 | 否 |
| 保存窗口位置 | `OnLocationChanged(...)` | 仅沿用现有可恢复错误路径 | 通过视图模型显示固定错误类别 | 仅沿用现有行为 |

## 当前实现缺口

- 当前行为：拖动处理器已经存在，但标题栏 `Grid` 没有背景，空白像素无法路由鼠标输入。
- 目标行为：48 DIP 标题栏的整个空白部分都能启动现有系统原生拖动操作。
- 所需方法或资源变更：新增一条真实 WPF 命中回归测试，并设置一个现有 XAML 资源属性；生产 C# 方法全部保持不变。

## 尚未确认事项

- 无。直接入口、路由处理器、位置保存流程、测试设施与验收边界均已在当前代码中核实。

## 验收链接

- `docs/windows-manual-test.md`
- `docs/superpowers/specs/2026-08-16-windows-title-drag-fix-design.md`
