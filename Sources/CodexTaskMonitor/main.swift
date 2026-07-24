import AppKit
@preconcurrency import ApplicationServices
import Combine
import ServiceManagement
import SwiftUI
import CodexTaskMonitorCore

@main
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var panel: NSPanel?
    private var model: MonitorModel?
    private var sizeObserver: AnyCancellable?

    static func main() {
        let application = NSApplication.shared
        let delegate = AppDelegate()
        application.delegate = delegate
        application.setActivationPolicy(.accessory)
        application.run()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let databaseURL = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".codex/state_5.sqlite")
        let model = MonitorModel(databaseURL: databaseURL)
        self.model = model
        model.start()

        let panel = makePanel(content: MonitorView(model: model))
        self.panel = panel

        restoreOrPlace(panel)
        resize(panel, itemCount: 0, hasError: false)
        sizeObserver = model.$items
            .combineLatest(model.$errorMessage)
            .sink { [weak self, weak panel] items, errorMessage in
                let itemCount = items.count
                let hasError = errorMessage != nil
                DispatchQueue.main.async { [weak self, weak panel] in
                    guard let self, let panel else { return }
                    self.resize(panel, itemCount: itemCount, hasError: hasError)
                }
            }

        panel.orderFrontRegardless()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    private func makePanel(content: some View) -> NSPanel {
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 330, height: 48),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isMovableByWindowBackground = true
        panel.isFloatingPanel = true
        panel.becomesKeyOnlyIfNeeded = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.hasShadow = true
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.contentView = NSHostingView(rootView: content.preferredColorScheme(.dark))
        return panel
    }

    private func restoreOrPlace(_ panel: NSPanel) {
        let frameName = "CodexTaskMonitorPanel"
        if !panel.setFrameUsingName(frameName), let screen = NSScreen.main {
            let frame = screen.visibleFrame
            panel.setFrameOrigin(
                NSPoint(x: frame.maxX - panel.frame.width - 20, y: frame.midY)
            )
        }
        panel.setFrameAutosaveName(frameName)
    }

    private func resize(_ panel: NSPanel, itemCount: Int, hasError: Bool) {
        let height = MonitorPanelLayout.height(itemCount: itemCount, hasError: hasError)
        guard panel.frame.width != 330 || panel.frame.height != height else { return }
        let oldTop = panel.frame.maxY
        panel.setFrame(
            NSRect(x: panel.frame.minX, y: oldTop - height, width: 330, height: height),
            display: true,
            animate: true
        )
    }
}

@MainActor
final class MonitorModel: NSObject, ObservableObject {
    @Published private(set) var items: [MonitorItem] = []
    @Published private(set) var errorMessage: String?

    private let monitor: TaskMonitor
    private let sidebarRevealer: CodexSidebarRevealer
    private let firstAttemptBaseline = Date(
        timeIntervalSince1970: Date().timeIntervalSince1970.rounded(.down)
    )
    private var preferences = MonitorPreferences()
    private var timer: Timer?
    private var scanErrorMessage: String?
    private var actionErrorMessage: String?
    private var sidebarRevealGeneration = 0

    init(databaseURL: URL) {
        monitor = TaskMonitor(databaseURL: databaseURL)
        sidebarRevealer = CodexSidebarRevealer(
            sessionIndexURL: databaseURL
                .deletingLastPathComponent()
                .appendingPathComponent("session_index.jsonl"),
            globalStateURL: databaseURL
                .deletingLastPathComponent()
                .appendingPathComponent(".codex-global-state.json")
        )
    }

    func start() {
        refresh()
        timer = Timer.scheduledTimer(
            timeInterval: 2,
            target: self,
            selector: #selector(refresh),
            userInfo: nil,
            repeats: true
        )
        enableLaunchAtLogin(showErrors: false)
    }

    @objc func refresh() {
        do {
            if preferences.baseline == nil {
                let baseline = firstAttemptBaseline
                let hourAgo = baseline.addingTimeInterval(-3_600)
                let codexLaunchDate = NSRunningApplication
                    .runningApplications(withBundleIdentifier: "com.openai.codex")
                    .compactMap(\.launchDate)
                    .max()
                let activeSince = max(hourAgo, codexLaunchDate ?? hourAgo)
                let adoptedTurnIDs = try monitor.currentlyRunningTurnIDs(since: activeSince)
                preferences.initialize(baseline: baseline, adoptedTurnIDs: adoptedTurnIDs)
            }

            guard let baseline = preferences.baseline else { return }
            let updatedItems = try monitor.scan(
                baseline: baseline,
                adoptedTurnIDs: preferences.adoptedTurnIDs,
                dismissedTurnIDs: preferences.dismissedTurnIDs,
                dismissedItemIDs: preferences.dismissedItemIDs
            )
            if items != updatedItems { items = updatedItems }

            let updatedScanErrorMessage = monitor.unreadableRolloutCount == 0
                ? nil
                : "\(monitor.unreadableRolloutCount) 个任务暂时无法读取"
            if scanErrorMessage != updatedScanErrorMessage {
                scanErrorMessage = updatedScanErrorMessage
                updateErrorMessage()
            }
        } catch {
            let updatedScanErrorMessage = (error as? LocalizedError)?.errorDescription
                ?? "暂时无法读取 Codex 数据"
            if scanErrorMessage != updatedScanErrorMessage {
                scanErrorMessage = updatedScanErrorMessage
                updateErrorMessage()
            }
        }
    }

    func open(_ item: MonitorItem) {
        sidebarRevealGeneration += 1
        let generation = sidebarRevealGeneration
        setActionError(nil)

        guard let url = CodexThreadLink.openURL(threadID: item.threadID),
              NSWorkspace.shared.open(url)
        else {
            setActionError("无法打开对应的 Codex 对话")
            return
        }

        guard CodexSidebarRevealer.requestAccessibilityPermission() else {
            setActionError(CodexSidebarRevealError.accessibilityPermissionRequired.errorDescription)
            return
        }
        revealSidebar(
            threadID: item.threadID,
            cwd: item.cwd,
            generation: generation,
            attempt: 0
        )
    }

    func dismiss(_ item: MonitorItem) {
        guard item.state == .waiting else { return }
        preferences.dismiss(itemID: item.id)
        refresh()
    }

    func enableLaunchAtLogin(showErrors: Bool = true) {
        do {
            switch SMAppService.mainApp.status {
            case .enabled:
                return
            case .requiresApproval:
                if showErrors { SMAppService.openSystemSettingsLoginItems() }
            case .notRegistered, .notFound:
                try SMAppService.mainApp.register()
            @unknown default:
                return
            }
        } catch {
            if showErrors {
                setActionError("无法启用登录时启动：\(error.localizedDescription)")
            }
        }
    }

    func quit() {
        NSApp.terminate(nil)
    }

    private func revealSidebar(
        threadID: String,
        cwd: String,
        generation: Int,
        attempt: Int
    ) {
        guard generation == sidebarRevealGeneration else { return }

        do {
            try sidebarRevealer.reveal(
                threadID: threadID,
                cwd: cwd
            )
            setActionError(nil)
        } catch let error as CodexSidebarRevealError
            where error.isRetryable && attempt < 19
        {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in
                self?.revealSidebar(
                    threadID: threadID,
                    cwd: cwd,
                    generation: generation,
                    attempt: attempt + 1
                )
            }
        } catch {
            let message = (error as? LocalizedError)?.errorDescription
                ?? "已打开对话；暂时无法在侧栏定位"
            setActionError(message)
        }
    }

    private func setActionError(_ message: String?) {
        guard actionErrorMessage != message else { return }
        actionErrorMessage = message
        updateErrorMessage()
    }

    private func updateErrorMessage() {
        let updatedErrorMessage = actionErrorMessage ?? scanErrorMessage
        if errorMessage != updatedErrorMessage {
            errorMessage = updatedErrorMessage
        }
    }
}

private struct MonitorView: View {
    @ObservedObject var model: MonitorModel

    var body: some View {
        VStack(spacing: 0) {
            header
            if !model.items.isEmpty {
                Divider().overlay(.white.opacity(0.12))
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 0) {
                            ForEach(model.items) { item in
                                TaskRow(
                                    item: item,
                                    open: { model.open(item) },
                                    dismiss: { model.dismiss(item) }
                                )
                                .id(item.id)
                                if item.id != model.items.last?.id {
                                    Divider().overlay(.white.opacity(0.08))
                                }
                            }
                        }
                    }
                    .scrollIndicators(.visible)
                    .onChange(of: model.items.map(\.id)) { oldIDs, newIDs in
                        guard let insertedID = MonitorListUpdate.insertedID(
                            from: oldIDs,
                            to: newIDs
                        ) else {
                            return
                        }
                        proxy.scrollTo(insertedID, anchor: .top)
                    }
                }
            }
            if let errorMessage = model.errorMessage {
                Text(errorMessage)
                    .font(.caption2)
                    .foregroundStyle(.orange)
                    .lineLimit(1)
                    .padding(.horizontal, 14)
                    .frame(maxWidth: .infinity, minHeight: 32, alignment: .leading)
            }
        }
        .frame(width: 330)
        .frame(maxHeight: .infinity, alignment: .top)
        .ignoresSafeArea()
        .background(.ultraThinMaterial)
        .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(.white.opacity(0.14), lineWidth: 1)
        }
    }

    private var header: some View {
        HStack(spacing: 8) {
            Text("Codex 任务")
                .font(.system(size: 13, weight: .semibold))
            Text("\(model.items.count)")
                .font(.caption)
                .foregroundStyle(.secondary)
            Spacer()
            Menu {
                Button("重新读取", action: model.refresh)
                Button("登录时启动") { model.enableLaunchAtLogin() }
                Divider()
                Button("退出", action: model.quit)
            } label: {
                Image(systemName: "ellipsis")
                    .frame(width: 22, height: 22)
            }
            .menuStyle(.borderlessButton)
            .accessibilityLabel("更多操作")
        }
        .padding(.horizontal, 14)
        .frame(height: 48)
    }
}

private struct TaskRow: View {
    let item: MonitorItem
    let open: () -> Void
    let dismiss: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Circle()
                .fill(item.state == .running ? Color.blue : Color.green)
                .frame(width: 8, height: 8)
                .accessibilityLabel(item.state == .running ? "运行中" : "等待处理")

            Button(action: open) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(item.title)
                        .font(.system(size: 13))
                        .lineLimit(1)
                    HStack(spacing: 4) {
                        Text(item.state == .running ? "运行中" : "等待处理")
                        Text("·")
                        Text(item.projectName)
                        Text("·")
                        Text(item.eventDate, style: .relative)
                    }
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            if item.state == .waiting {
                Button("已处理", action: dismiss)
                    .font(.caption2)
                    .buttonStyle(.bordered)
                    .controlSize(.small)
            } else {
                Image(systemName: "chevron.right")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
                    .accessibilityHidden(true)
            }
        }
        .padding(.horizontal, 14)
        .frame(height: 62)
    }
}

private enum CodexSidebarRevealError: LocalizedError {
    case accessibilityPermissionRequired
    case ambiguousTask
    case sessionIndexUnavailable
    case sidebarStateUnavailable
    case targetNotReady
    case unsupportedSystem
    case unsupportedCodexVersion

    var isRetryable: Bool {
        switch self {
        case .sessionIndexUnavailable, .sidebarStateUnavailable, .targetNotReady:
            return true
        case .accessibilityPermissionRequired, .ambiguousTask,
             .unsupportedSystem, .unsupportedCodexVersion:
            return false
        }
    }

    var errorDescription: String? {
        switch self {
        case .accessibilityPermissionRequired:
            return "已打开对话；请允许辅助功能权限"
        case .ambiguousTask:
            return "已打开对话；侧栏有同名任务，未滚动"
        case .sessionIndexUnavailable:
            return "已打开对话；无法读取 Codex 会话索引"
        case .sidebarStateUnavailable:
            return "已打开对话；无法读取 Codex 侧栏状态"
        case .targetNotReady:
            return "已打开对话；暂时无法在侧栏定位"
        case .unsupportedSystem:
            return "当前 macOS 不支持侧栏定位"
        case .unsupportedCodexVersion:
            return "当前 Codex 版本不支持侧栏定位"
        }
    }
}

private struct CodexSidebarRevealer {
    private static let codexBundleIdentifier = "com.openai.codex"
    private static let maximumElementCount = 5_000

    let sessionIndexURL: URL
    let globalStateURL: URL

    static func requestAccessibilityPermission() -> Bool {
        let promptKey = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        return AccessibilityPermissionGate.request(
            isTrusted: { AXIsProcessTrusted() },
            prompt: {
                AXIsProcessTrustedWithOptions([promptKey: true] as CFDictionary)
            }
        )
    }

    func reveal(threadID: String, cwd: String) throws {
        guard #available(macOS 26.0, *) else {
            throw CodexSidebarRevealError.unsupportedSystem
        }
        guard AXIsProcessTrusted() else {
            throw CodexSidebarRevealError.accessibilityPermissionRequired
        }

        let data: Data
        do {
            data = try Data(contentsOf: sessionIndexURL, options: .mappedIfSafe)
        } catch {
            throw CodexSidebarRevealError.sessionIndexUnavailable
        }

        let globalStateData: Data
        do {
            globalStateData = try Data(contentsOf: globalStateURL, options: .mappedIfSafe)
        } catch {
            throw CodexSidebarRevealError.sidebarStateUnavailable
        }

        let target: SidebarTarget
        do {
            guard let resolvedTarget = try SidebarTargetResolver.resolve(
                threadID: threadID,
                cwd: cwd,
                sessionIndexData: data,
                globalStateData: globalStateData
            ) else {
                throw CodexSidebarRevealError.targetNotReady
            }
            target = resolvedTarget
        } catch let error as CodexSidebarRevealError {
            throw error
        } catch SidebarTargetResolutionError.sessionIndexChanged {
            throw CodexSidebarRevealError.sessionIndexUnavailable
        } catch SidebarTargetResolutionError.globalStateChanged {
            throw CodexSidebarRevealError.sidebarStateUnavailable
        } catch {
            throw CodexSidebarRevealError.sidebarStateUnavailable
        }

        guard let application = NSRunningApplication
            .runningApplications(withBundleIdentifier: Self.codexBundleIdentifier)
            .first(where: { !$0.isTerminated })
        else {
            throw CodexSidebarRevealError.targetNotReady
        }

        let applicationElement = AXUIElementCreateApplication(application.processIdentifier)
        guard let focusedWindow = attribute(
            applicationElement,
            kAXFocusedWindowAttribute
        ) else {
            throw CodexSidebarRevealError.targetNotReady
        }
        guard let root = axElement(focusedWindow) else {
            throw CodexSidebarRevealError.targetNotReady
        }
        let candidates = sidebarTaskTitles(
            in: root,
            matching: target.title,
            listDescription: target.group.listDescription
        )
        guard let index = SidebarTaskMatch.uniqueIndex(
            for: target.title,
            among: candidates.map(\.title)
        ) else {
            if candidates.isEmpty {
                throw CodexSidebarRevealError.targetNotReady
            }
            throw CodexSidebarRevealError.ambiguousTask
        }

        let action = NSAccessibility.Action.scrollToVisibleAction.rawValue as CFString
        switch AXUIElementPerformAction(candidates[index].element, action) {
        case .success:
            return
        case .cannotComplete, .invalidUIElement:
            throw CodexSidebarRevealError.targetNotReady
        case .actionUnsupported, .notImplemented:
            throw CodexSidebarRevealError.unsupportedCodexVersion
        default:
            throw CodexSidebarRevealError.targetNotReady
        }
    }

    private func sidebarTaskTitles(
        in root: AXUIElement,
        matching threadName: String,
        listDescription: String
    ) -> [(element: AXUIElement, title: String)] {
        var queue = [root]
        var index = 0
        var matches: [(AXUIElement, String)] = []

        while index < queue.count && index < Self.maximumElementCount {
            let element = queue[index]
            index += 1

            if stringAttribute(element, kAXRoleAttribute) == kAXStaticTextRole,
               stringAttribute(element, kAXValueAttribute) == threadName,
               isInsideSidebarTaskList(element, description: listDescription)
            {
                matches.append((element, threadName))
            }

            if let children = attribute(element, kAXChildrenAttribute) as? [AXUIElement] {
                queue.append(contentsOf: children)
            }
        }

        return matches
    }

    private func isInsideSidebarTaskList(
        _ element: AXUIElement,
        description: String
    ) -> Bool {
        var current = element
        for _ in 0..<6 {
            guard let parent = axElement(attribute(current, kAXParentAttribute)) else {
                return false
            }
            current = parent
            if stringAttribute(current, kAXRoleAttribute) == kAXListRole,
               stringAttribute(current, kAXDescriptionAttribute) == description
            {
                return true
            }
        }
        return false
    }

    private func attribute(_ element: AXUIElement, _ name: String) -> AnyObject? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success else {
            return nil
        }
        return value
    }

    private func axElement(_ value: AnyObject?) -> AXUIElement? {
        guard let value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return unsafeDowncast(value, to: AXUIElement.self)
    }

    private func stringAttribute(_ element: AXUIElement, _ name: String) -> String? {
        attribute(element, name) as? String
    }
}
