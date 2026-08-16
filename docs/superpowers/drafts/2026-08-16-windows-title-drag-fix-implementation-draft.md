# `WIN-DRAG-001` Windows Title Drag Fix — Method-Level Implementation Draft

## Revision

- Draft revision: `1`
- Requirement source: `docs/superpowers/specs/2026-08-16-windows-title-drag-fix-design.md`
- Supersedes: none
- Change summary: converts the approved transparent-title-bar design into verified WPF method and resource changes.

## Requirement Definition

Pressing and dragging a blank point in the existing 48-DIP title bar must move `MainWindow`. The More button must retain its click behavior, and the existing delayed window-position persistence must continue unchanged. Task rows, the error strip, and the More button are excluded as drag surfaces.

## Constraints and Invariants

- Preserve the 330-DIP window width, 48-DIP title row, borderless/topmost styling, and current visual appearance.
- Reuse `Header_MouseLeftButtonDown`, `Window.DragMove()`, `OnLocationChanged`, and `MonitorViewModel.SaveWindowPositionAsync()` without behavior changes.
- Do not add mouse-coordinate logging, new persistence, dependencies, retries, or input hooks.
- Keep the 18 manual UI, reboot, and login acceptance items unchecked until performed by a person.

## Context Scan

| Evidence | Location | Verified finding |
|---|---|---|
| Direct entry | `windows/CodexTaskMonitor.Windows/MainWindow.xaml:15` | The row-zero title-bar `Grid` routes `MouseLeftButtonDown` but has no background, so blank pixels are not a WPF hit surface. |
| Caller | `windows/CodexTaskMonitor.Windows/App.xaml.cs:68-71` | Application startup creates `MainWindow`, assigns the monitor view model, and shows the window. |
| Callee | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:117-120` | `Header_MouseLeftButtonDown` gates on a pressed left button and calls the framework `DragMove()` method. |
| Shared resource | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:52-72` | `OnLocationChanged` already debounces movement and delegates persistence to `MonitorViewModel.SaveWindowPositionAsync()`. |
| Test | `windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs` | The test project already has an STA-thread pattern that loads `MainWindow` and exercises WPF layout safely. |
| Acceptance | `docs/windows-manual-test.md` | Real visual and interaction checks are recorded separately from automated evidence. |

## Technologies and Shared Resources

| Technology or resource | Location | Status | Current purpose | Planned use or change |
|---|---|---|---|---|
| Title-bar `Grid` | `windows/CodexTaskMonitor.Windows/MainWindow.xaml:15` | Existing, modify | Hosts title text and the More button; routes the drag event only from hit-testable descendants | Set `Background="Transparent"` so its blank area participates in hit testing without changing appearance. |
| `MainWindow` event pipeline | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs` | Existing, reuse unchanged | Starts native window drag and persists later position changes | No behavior change. |
| WPF layout and hit testing | .NET 8 WPF | Existing, reuse unchanged | Measures visuals and resolves the element under a point | Exercise the real title-bar visual on an STA test thread. |
| `MainWindowXamlTests` | `windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs` | Existing, modify | Covers runtime XAML binding and layout behavior | Add one regression test; no new test assembly or framework is needed. |

## Methods

| Method | Location | Status | Current responsibility | Planned responsibility or change |
|---|---|---|---|---|
| `MainWindow()` | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:14-20` | Existing, reuse unchanged | Loads XAML and subscribes window lifecycle events | Supply the real compiled title-bar visual to the regression test. |
| `Header_MouseLeftButtonDown(object, MouseButtonEventArgs)` | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:117-121` | Existing, reuse unchanged | Start `DragMove()` only while the left button is pressed | Receive blank-title-bar presses once the XAML surface becomes hit-testable. |
| `Window.DragMove()` | .NET 8 WPF framework | Existing, reuse unchanged | Perform the native interactive move loop | No behavior change. |
| `OnLocationChanged(object?, EventArgs)` | `windows/CodexTaskMonitor.Windows/MainWindow.xaml.cs:52-72` | Existing, reuse unchanged | Debounce and save the final window coordinates | No behavior change. |
| `HeaderBlankArea_IsHitTestableForDragging()` | `windows/CodexTaskMonitor.Tests/MainWindowXamlTests.cs` | Planned new | Not present | Prove the approved blank title-bar point resolves to the title-bar `Grid`; existing tests cover row bindings but not pointer hit testing. |

## Implementation Flow

### Reproduce the missing blank-area hit target

- Goal: establish a failing regression that exercises the compiled WPF visual rather than searching XAML text.
- Methods:
  - `MainWindow()` — Existing, reuse unchanged
  - `HeaderBlankArea_IsHitTestableForDragging()` — Planned new
- Shared resources:
  - `MainWindowXamlTests` STA-thread pattern — Existing, modify
  - WPF layout and hit testing — Existing, reuse unchanged
- Method cooperation:

  ```text
  on an STA thread:
      window = MainWindow()
      titleBar = row-zero Grid from the window content
      measure and arrange titleBar at 330 x 48 DIP
      hit = titleBar.InputHitTest(blank point between title text and More button)
      assert hit is titleBar
  ```

- Key inputs: a stable blank point inside the title bar and outside both child controls.
- Key outputs or state: one deterministic failing assertion on the current background-less `Grid`; no persistent state.
- Success condition: the test fails because the blank point has no title-bar hit target.
- Failure owner: xUnit assertion propagation from the joined STA test thread.
- Log point: no runtime log is introduced; the focused test result is the checkpoint and contains no user content or coordinates.

### Expose the existing drag handler across the title surface

- Goal: make blank title-bar pixels route the existing mouse event without changing the drag method.
- Methods:
  - `Header_MouseLeftButtonDown(object, MouseButtonEventArgs)` — Existing, reuse unchanged
  - `Window.DragMove()` — Existing, reuse unchanged
- Shared resources:
  - Title-bar `Grid` — Existing, modify
- Method cooperation:

  ```text
  titleBar.Background = Transparent

  when blank titleBar surface receives a pressed left-button event:
      routed event reaches Header_MouseLeftButtonDown
      Header_MouseLeftButtonDown calls DragMove()

  when More button receives input:
      Button keeps ownership of its handled press and click path
  ```

- Key inputs: WPF left-button routed input on either blank title-bar space or the existing button.
- Key outputs or state: native window movement for blank-area drags; unchanged button click behavior.
- Success condition: the regression test passes with only the XAML background change.
- Failure owner: the existing WPF routed-input boundary; do not add recovery or retries.
- Log point: keep diagnostics unchanged. Mouse movement is high-frequency input, and recording it would add noise and unnecessary interaction data.

### Preserve the existing position pipeline

- Goal: ensure the new hit surface feeds the already approved movement and persistence flow without adding state.
- Methods:
  - `OnLocationChanged(object?, EventArgs)` — Existing, reuse unchanged
  - `MonitorViewModel.SaveWindowPositionAsync(double, double, CancellationToken)` — Existing, reuse unchanged
- Shared resources:
  - Existing 300-millisecond position-save cancellation source — Existing, reuse unchanged
  - Existing monitor preference store — Existing, reuse unchanged
- Method cooperation:

  ```text
  DragMove changes Window.Left or Window.Top
  OnLocationChanged cancels the prior delayed save
  after 300 milliseconds:
      SaveWindowPositionAsync(final Left, final Top, token)
  existing error boundary reports a fixed recoverable message
  ```

- Key inputs: final `Left` and `Top` values produced by the framework move loop.
- Key outputs or state: the same persisted window-position fields already used at startup.
- Success condition: existing preference tests remain green and no production method changes are required.
- Failure owner: the existing `OnLocationChanged` catch boundary and `MonitorViewModel.ReportActionFailure()` behavior.
- Log point: keep the existing privacy-safe error behavior; do not add position or mouse-coordinate logs.

## Failure and Recovery

- Owning boundary: WPF routed input for drag initiation; existing `OnLocationChanged` handling for persistence failure.
- Naturally propagated failures: STA regression setup and WPF layout failures fail the automated test.
- Business-handled failures: existing position-save cancellation and fixed action error remain unchanged.
- Retry identity and stop condition: no new retries; the existing 300-millisecond debounce keeps only the latest position save.
- Data that must survive failure: the previously committed window position remains valid if a later save fails.
- Error log location: no new logger; the requirement explicitly preserves current diagnostics and excludes coordinate logging.

## Logging Plan

| Major boundary | Actual method | Log moment | Required context | Error required? |
|---|---|---|---|---|
| Blank-area hit regression | `HeaderBlankArea_IsHitTestableForDragging()` | Focused test completion | Pass/fail only | Test failure is sufficient |
| Drag initiation | `Header_MouseLeftButtonDown(...)` | None added | Mouse coordinates and UI content are intentionally not collected | No |
| Position persistence | `OnLocationChanged(...)` | Existing recoverable error path only | Fixed error category through the view model | Existing behavior only |

## Current Implementation Gap

- Current behavior: the drag handler exists, but blank title-bar pixels do not route input because the title-bar `Grid` has no background.
- Target behavior: the full blank portion of the 48-DIP title bar starts the existing native drag operation.
- Required method/resource changes: add one real WPF hit-test regression and set one existing XAML resource property; production C# methods remain unchanged.

## Unconfirmed Items

- None. The direct entry, routed handler, persistence flow, test harness, and acceptance boundary are all present in the inspected code.

## Acceptance Links

- `docs/windows-manual-test.md`
- `docs/superpowers/specs/2026-08-16-windows-title-drag-fix-design.md`
