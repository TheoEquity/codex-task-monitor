import AppKit
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
                guard let self, let panel else { return }
                self.resize(panel, itemCount: items.count, hasError: errorMessage != nil)
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
    private let firstAttemptBaseline = Date(
        timeIntervalSince1970: Date().timeIntervalSince1970.rounded(.down)
    )
    private var preferences = MonitorPreferences()
    private var timer: Timer?

    init(databaseURL: URL) {
        monitor = TaskMonitor(databaseURL: databaseURL)
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
                dismissedTurnIDs: preferences.dismissedTurnIDs
            )
            if items != updatedItems { items = updatedItems }

            let updatedErrorMessage = monitor.unreadableRolloutCount == 0
                ? nil
                : "\(monitor.unreadableRolloutCount) 个任务暂时无法读取"
            if errorMessage != updatedErrorMessage { errorMessage = updatedErrorMessage }
        } catch {
            let updatedErrorMessage = (error as? LocalizedError)?.errorDescription
                ?? "暂时无法读取 Codex 数据"
            if errorMessage != updatedErrorMessage { errorMessage = updatedErrorMessage }
        }
    }

    func open(_ item: MonitorItem) {
        guard let url = URL(string: "codex://threads/\(item.threadID)"),
              NSWorkspace.shared.open(url)
        else {
            errorMessage = "无法打开对应的 Codex 对话"
            return
        }
    }

    func dismiss(_ item: MonitorItem) {
        guard item.state == .waiting else { return }
        preferences.dismiss(turnID: item.turnID)
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
            if showErrors { errorMessage = "无法启用登录时启动：\(error.localizedDescription)" }
        }
    }

    func quit() {
        NSApp.terminate(nil)
    }
}

private struct MonitorView: View {
    @ObservedObject var model: MonitorModel

    var body: some View {
        VStack(spacing: 0) {
            header
            if !model.items.isEmpty {
                Divider().overlay(.white.opacity(0.12))
                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(model.items) { item in
                            TaskRow(
                                item: item,
                                open: { model.open(item) },
                                dismiss: { model.dismiss(item) }
                            )
                            if item.id != model.items.last?.id {
                                Divider().overlay(.white.opacity(0.08))
                            }
                        }
                    }
                }
                .id(model.items.map(\.id))
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
                .fill(item.state == .running ? Color.blue : Color.orange)
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
