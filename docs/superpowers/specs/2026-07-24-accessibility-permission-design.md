# 辅助功能权限启动与提示设计

## 目标

当 `Codex Task Monitor` 已获得 macOS 辅助功能权限时，重启或点击任务不再重复触发系统授权提示，也不显示误导性的橙色权限错误。

## 根因

任务点击路径每次都直接调用 `AXIsProcessTrustedWithOptions` 并启用提示。当前运行环境还存在一个手工 launchd 任务，直接执行 `.app/Contents/MacOS/CodexTaskMonitor`，会让 macOS 看到与正式应用不同的辅助功能身份。

## 方案

1. 在请求权限前先静默调用 `AXIsProcessTrusted()`。
2. 已信任时立即返回，不调用带提示的 API。
3. 未信任时才调用 `AXIsProcessTrustedWithOptions(...prompt: true)`；返回仍为未信任时，保留现有橙色错误提示。
4. 常驻启动统一使用正式 `Codex Task Monitor.app` 的 `SMAppService.mainApp` 登录项；停止手工 direct-binary launchd 任务，避免再次生成 `CodexTaskMonitor` 身份。
5. 在 `CoreChecks` 增加“已信任不触发提示、未信任才调用提示回调”的回归检查。

## 错误处理

- 系统权限已授予：不弹窗，继续侧栏定位。
- 系统权限未授予：只在用户点击任务时请求一次系统提示；用户未授权时显示现有错误文案。
- 不改变扫描、任务列表、深链或侧栏定位逻辑。

## 验证

- `swift run -Xswiftc -warnings-as-errors CoreChecks`
- `Scripts/package_app.sh`
- 通过正式 `.app` 启动并确认 `AXIsProcessTrusted()` 为真时无系统弹窗、无橙色权限错误。
