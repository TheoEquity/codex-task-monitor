import Foundation
import CodexTaskMonitorCore
import SQLite3

let baseline = Date(timeIntervalSince1970: 100)
let fractionalBaseline = Date(timeIntervalSince1970: 100.9)
let threadID = "11111111-1111-4111-8111-111111111111"

expect(
    CodexThreadLink.openURL(threadID: threadID)?.absoluteString
        == "codex://threads/11111111-1111-4111-8111-111111111111",
    "Codex thread links use the supported deep-link route"
)
expect(
    CodexThreadLink.openURL(threadID: "not-a-uuid") == nil,
    "Codex thread links reject invalid thread IDs"
)

let sessionIndexData = Data(
    """
    {"id":"\(threadID)","thread_name":"Old title","updated_at":"2026-07-22T00:00:00Z"}
    {"id":"22222222-2222-4222-8222-222222222222","thread_name":"Other task","updated_at":"2026-07-22T00:01:00Z"}
    {"id":"\(threadID)","thread_name":"Sidebar title","updated_at":"2026-07-22T00:02:00Z"}
    """.utf8
)
try expect(
    try SessionIndex.threadName(for: threadID, in: sessionIndexData) == "Sidebar title",
    "the session index uses the newest name for the exact thread UUID"
)
let partialSessionIndexData = sessionIndexData + Data(
    (
        "\n" + #"{"id":"22222222-2222-4222-8222-222222222222","thread_name":"#
    ).utf8
)
try expect(
    try SessionIndex.threadName(for: threadID, in: partialSessionIndexData) == "Sidebar title",
    "an incomplete trailing session-index row keeps the last complete name"
)
expect(
    SidebarTaskMatch.uniqueIndex(
        for: "Sidebar title",
        among: ["Other task", "Sidebar title"]
    ) == 1,
    "a unique sidebar title identifies its exact candidate"
)
expect(
    SidebarTaskMatch.uniqueIndex(
        for: "Sidebar title",
        among: ["Sidebar title", "Other task", "Sidebar title"]
    ) == nil,
    "duplicate sidebar titles are rejected instead of guessing"
)

let globalStateData = Data(
    """
    {
      "pinned-thread-ids": ["pinned-thread"],
      "projectless-thread-ids": ["projectless-thread"],
      "thread-project-assignments": {
        "pinned-thread": {"projectId": "project-1"},
        "project-thread": {"projectId": "project-1"}
      },
      "local-projects": {
        "project-1": {"name": "DemoProject", "rootPaths": ["/tmp/DemoProject"]}
      }
    }
    """.utf8
)
try expect(
    try SidebarThreadGroup.resolve(threadID: "pinned-thread", in: globalStateData)
        == .pinned,
    "pinned threads resolve to the pinned sidebar group"
)
try expect(
    try SidebarThreadGroup.resolve(threadID: "project-thread", in: globalStateData)
        == .project(name: "DemoProject"),
    "project threads resolve through their exact project assignment"
)
try expect(
    try SidebarThreadGroup.resolve(threadID: "projectless-thread", in: globalStateData)
        == .projectless,
    "projectless threads resolve to the ungrouped task list"
)
expect(
    SidebarThreadGroup.project(name: "DemoProject").listDescription
        == "DemoProject中的已安排任务",
    "project groups use the AX list description exposed by Codex"
)

let sidebarTargetIndexData = Data(
    """
    {"id":"project-thread","thread_name":"Assigned task"}
    {"id":"implicit-project-thread","thread_name":"Implicit project task"}
    """.utf8
)
try expect(
    try SidebarTargetResolver.resolve(
        threadID: "implicit-project-thread",
        cwd: "/tmp/DemoProject",
        sessionIndexData: sidebarTargetIndexData,
        globalStateData: globalStateData
    ) == SidebarTarget(
        title: "Implicit project task",
        group: .project(name: "DemoProject")
    ),
    "an exact unique project root recovers a missing thread assignment"
)
try expect(
    try SidebarTargetResolver.resolve(
        threadID: "missing-title-thread",
        cwd: "/tmp/DemoProject",
        sessionIndexData: sidebarTargetIndexData,
        globalStateData: globalStateData
    ) == nil,
    "a missing UUID-to-title mapping is rejected instead of using a database title"
)

let ambiguousProjectStateData = Data(
    """
    {
      "local-projects": {
        "project-1": {"name": "First", "rootPaths": ["/tmp/shared"]},
        "project-2": {"name": "Second", "rootPaths": ["/tmp/shared"]}
      }
    }
    """.utf8
)
let ambiguousProjectIndexData = Data(
    #"{"id":"ambiguous-thread","thread_name":"Ambiguous task"}"#.utf8
)
try expect(
    try SidebarTargetResolver.resolve(
        threadID: "ambiguous-thread",
        cwd: "/tmp/shared",
        sessionIndexData: ambiguousProjectIndexData,
        globalStateData: ambiguousProjectStateData
    ) == nil,
    "multiple projects with the same root are rejected instead of guessed"
)

expect(
    MonitorPanelLayout.height(itemCount: 3, hasError: false) == 237,
    "panel height includes every visible row divider"
)
expect(
    MonitorPanelLayout.height(itemCount: 8, hasError: true) == 458,
    "panel height caps the list at six rows and includes the error strip"
)
expect(
    MonitorListUpdate.insertedID(
        from: ["first", "handled", "last"],
        to: ["first", "last"]
    ) == nil,
    "removing a task does not request list scrolling"
)
expect(
    MonitorListUpdate.insertedID(
        from: ["first", "last"],
        to: ["first", "new", "last"]
    ) == "new",
    "adding a task targets the inserted row"
)
expect(
    MonitorListUpdate.insertedID(
        from: ["first", "handled", "last"],
        to: ["new", "first", "last"]
    ) == nil,
    "handling a task keeps the viewport stable when another task arrives"
)

expect(
    TaskState.resolve(
        LifecycleEvent(kind: .started, turnID: "turn-1", startedAt: date(101)),
        baseline: baseline
    ) == .running,
    "started turn is running"
)
expect(
    TaskState.resolve(
        LifecycleEvent(kind: .started, turnID: "same-second", startedAt: date(100)),
        baseline: fractionalBaseline
    ) == .running,
    "event timestamps in the baseline second are not lost to subsecond precision"
)

expect(
    TaskState.resolve(
        LifecycleEvent(
            kind: .completed,
            turnID: "turn-1",
            startedAt: date(101),
            completedAt: date(102)
        ),
        baseline: baseline
    ) == .waiting,
    "completed turn waits for handling"
)

expect(
    TaskState.resolve(
        LifecycleEvent(
            kind: .completed,
            turnID: "turn-1",
            startedAt: date(101),
            completedAt: date(102)
        ),
        baseline: baseline,
        dismissedTurnIDs: ["turn-1"]
    ) == nil,
    "dismissed completed turn is hidden"
)

expect(
    TaskState.resolve(
        LifecycleEvent(kind: .started, turnID: "turn-2", startedAt: date(103)),
        baseline: baseline,
        dismissedTurnIDs: ["turn-1"]
    ) == .running,
    "new turn reappears after previous turn was dismissed"
)

let oldCompletion = LifecycleEvent(
    kind: .completed,
    turnID: "turn-1",
    startedAt: date(98),
    completedAt: date(99)
)
expect(TaskState.resolve(oldCompletion, baseline: baseline) == nil, "old completed turn is ignored")
expect(
    TaskState.resolve(oldCompletion, baseline: baseline, adoptedTurnIDs: ["turn-1"]) == .waiting,
    "adopted old turn waits for handling"
)

let partialJSON = Data(
    """
    {"timestamp":"2099-01-01T00:01:41Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-1","started_at":101}}
    {"timestamp":"2099-01-01T00:01:42Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1","started_at":101,"completed_at":102}}
    {"timestamp":
    """.utf8
)
try expect(
    try RolloutParser.latestLifecycleEvent(in: partialJSON)
        == LifecycleEvent(
            kind: .completed,
            turnID: "turn-1",
            startedAt: date(101),
            completedAt: date(102)
        ),
    "incomplete trailing JSON is ignored"
)

let malformedCompleteLine = Data(
    ("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"" + "\n").utf8
)
var rejectedMalformedCompleteLine = false
do {
    _ = try RolloutParser.latestLifecycleEvent(in: malformedCompleteLine)
} catch {
    rejectedMalformedCompleteLine = true
}
expect(rejectedMalformedCompleteLine, "a newline-terminated malformed line is not ignored")

let abortedJSON = Data(
    """
    {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-a","started_at":101}}
    {"type":"event_msg","payload":{"type":"turn_aborted","turn_id":"turn-a","started_at":101,"completed_at":102}}
    """.utf8
)
let abortedEvent = try RolloutParser.latestLifecycleEvent(in: abortedJSON)
expect(abortedEvent?.kind == .aborted, "aborted turn is terminal")
expect(TaskState.resolve(abortedEvent!, baseline: baseline) == .waiting, "aborted turn waits for handling")

let supersededJSON = Data(
    """
    {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-a","started_at":101}}
    {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-b","started_at":102}}
    {"type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-a","started_at":101,"completed_at":103}}
    """.utf8
)
try expect(
    try RolloutParser.latestLifecycleEvent(in: supersededJSON)?.turnID == "turn-b",
    "newer started turn wins over a late terminal event for an older turn"
)

let currentTurn = LifecycleEvent(kind: .started, turnID: "turn-b", startedAt: date(102))
try expect(
    try RolloutParser.latestLifecycleEvent(
        after: currentTurn,
        appending: Data(
            """
            {"type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-a","started_at":101,"completed_at":103}}
            """.utf8
        )
    ) == currentTurn,
    "an appended late terminal event does not replace the current turn"
)

let appendedCompletion = Data(
    """
    {"type":"event_msg","payload":{"type":"agent_reasoning"}}
    {"type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-b","started_at":102,"completed_at":104}}
    {"type":
    """.utf8
)
try expect(
    try RolloutParser.latestLifecycleEvent(after: currentTurn, appending: appendedCompletion)
        == LifecycleEvent(
            kind: .completed,
            turnID: "turn-b",
            startedAt: date(102),
            completedAt: date(104)
        ),
    "only newly appended lifecycle lines update the cached turn"
)
var rejectedMalformedAppend = false
do {
    _ = try RolloutParser.latestLifecycleEvent(
        after: currentTurn,
        appending: malformedCompleteLine
    )
} catch {
    rejectedMalformedAppend = true
}
expect(rejectedMalformedAppend, "a malformed appended complete line is retried as an error")

let crossingBaseline = LifecycleEvent(
    kind: .completed,
    turnID: "turn-crossing",
    startedAt: date(99),
    completedAt: date(101)
)
expect(
    TaskState.resolve(crossingBaseline, baseline: baseline) == .waiting,
    "turn completing across first-scan baseline is tracked"
)

let missingLifecycleField = Data(
    """
    {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-missing"}}
    """.utf8
)
var detectedFormatChange = false
do {
    _ = try RolloutParser.latestLifecycleEvent(in: missingLifecycleField)
} catch let error as CodexDataError {
    detectedFormatChange = error == .formatChanged
}
expect(detectedFormatChange, "missing lifecycle fields report a format change")

let unrelatedPayloadShape = Data(
    """
    {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-shape","started_at":101}}
    {"type":"turn_context","payload":{"cwd":"/tmp/project"}}
    {"type":"event_msg","payload":{"type":"agent_reasoning"}}
    """.utf8
)
try expect(
    try RolloutParser.latestLifecycleEvent(in: unrelatedPayloadShape)?.turnID == "turn-shape",
    "unrelated payload shapes do not invalidate lifecycle parsing"
)

let temporaryDirectory = FileManager.default.temporaryDirectory
    .appendingPathComponent(UUID().uuidString, isDirectory: true)
try FileManager.default.createDirectory(at: temporaryDirectory, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: temporaryDirectory) }

let databaseURL = temporaryDirectory.appendingPathComponent("state.sqlite")
let rolloutURL = temporaryDirectory.appendingPathComponent("visible.jsonl")
let changedFormatDatabaseURL = temporaryDirectory.appendingPathComponent("changed-format.sqlite")
let changedFormatRolloutURL = temporaryDirectory.appendingPathComponent("changed-format.jsonl")
try missingLifecycleField.write(to: changedFormatRolloutURL)
try createFixtureDatabase(
    at: changedFormatDatabaseURL,
    rolloutPath: changedFormatRolloutURL.path
)
var monitorReportedFormatChange = false
do {
    _ = try TaskMonitor(databaseURL: changedFormatDatabaseURL).scan(baseline: baseline)
} catch let error as CodexDataError {
    monitorReportedFormatChange = error == .formatChanged
}
expect(monitorReportedFormatChange, "monitor surfaces a changed lifecycle format")

let startedLine = """
    {"timestamp":"2099-01-01T00:01:41Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-1","started_at":101}}

    """
let largeUnrelatedLine = """
    {"type":"response_item","payload":{"text":"\(String(repeating: "x", count: 130_000))"}}

    """
let initialRollout = startedLine + largeUnrelatedLine
try Data(initialRollout.utf8).write(to: rolloutURL)
try createFixtureDatabase(at: databaseURL, rolloutPath: rolloutURL.path)
let threads = try ThreadStore.readThreads(from: databaseURL)
expect(
    threads == [
        ThreadRecord(
            id: "visible",
            title: "Visible task",
            cwd: "/tmp/project",
            updatedAt: Date(timeIntervalSince1970: 123),
            rolloutURL: rolloutURL
        ),
    ],
    "thread store returns only visible, unarchived threads"
)
try expect(
    try ThreadStore.readThreads(from: databaseURL, updatedAfter: date(124)).isEmpty,
    "thread store applies the update-time filter in SQLite"
)

let monitor = TaskMonitor(databaseURL: databaseURL)
try expect(
    try monitor.currentlyRunningTurnIDs(since: baseline) == ["turn-1"],
    "first scan adopts recently updated running turns"
)
try expect(
    try monitor.currentlyRunningTurnIDs(since: date(124)).isEmpty,
    "first scan ignores stale unclosed turns"
)

let missingRolloutURL = temporaryDirectory.appendingPathComponent("missing.jsonl")
try insertVisibleThread(
    into: databaseURL,
    id: "temporarily-unreadable",
    rolloutPath: missingRolloutURL.path
)
let partiallyUnreadableMonitor = TaskMonitor(databaseURL: databaseURL)
var rejectedPartiallyUnreadableFirstScan = false
do {
    _ = try partiallyUnreadableMonitor.currentlyRunningTurnIDs(since: baseline)
} catch {
    rejectedPartiallyUnreadableFirstScan = true
}
expect(
    rejectedPartiallyUnreadableFirstScan,
    "a partially unreadable first scan does not establish the baseline"
)

var items = try monitor.scan(baseline: baseline)
expect(items.count == 1 && items[0].state == .running, "monitor finds running task")

let completedLine = """
    {"timestamp":"2099-01-01T00:01:42Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1","started_at":101,"completed_at":102}}

    """
try Data((initialRollout + completedLine).utf8).write(to: rolloutURL)
items = try monitor.scan(baseline: baseline)
expect(items.count == 1 && items[0].state == .waiting, "monitor observes completed task")

items = try monitor.scan(baseline: baseline, dismissedTurnIDs: ["turn-1"])
expect(items.isEmpty, "monitor hides handled task")

let nextStartedLine = """
    {"timestamp":"2099-01-01T00:01:43Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-2","started_at":103}}

    """
let secondTurnRollout = initialRollout + completedLine + nextStartedLine
try Data(secondTurnRollout.utf8).write(to: rolloutURL)
items = try monitor.scan(baseline: baseline, dismissedTurnIDs: ["turn-1"])
expect(
    items.count == 1 && items[0].turnID == "turn-2" && items[0].state == .running,
    "monitor shows a new turn after the previous turn was handled"
)

let partialSecondCompletion =
    #"{"type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-2","started_at":103,"completed_at":"#
try Data((secondTurnRollout + partialSecondCompletion).utf8).write(to: rolloutURL)
items = try monitor.scan(baseline: baseline, dismissedTurnIDs: ["turn-1"])
expect(items.first?.state == .running, "an appended partial line keeps the cached state")

let completedSecondTurnRollout = secondTurnRollout + partialSecondCompletion + "104}}\n"
try Data(completedSecondTurnRollout.utf8).write(to: rolloutURL)
items = try monitor.scan(baseline: baseline, dismissedTurnIDs: ["turn-1"])
expect(items.first?.state == .waiting, "a completed appended line updates the cached state")

let sameTurnRolloutURL = temporaryDirectory.appendingPathComponent("same-turn.jsonl")
try Data(completedSecondTurnRollout.utf8).write(to: sameTurnRolloutURL)
try insertVisibleThread(
    into: databaseURL,
    id: "same-turn",
    rolloutPath: sameTurnRolloutURL.path
)
items = try monitor.scan(
    baseline: baseline,
    dismissedItemIDs: ["visible:turn-2"]
)
expect(
    items.count == 1 && items[0].threadID == "same-turn",
    "handling one task does not hide another thread with the same turn ID"
)

try FileManager.default.removeItem(at: rolloutURL)
let unreadableMonitor = TaskMonitor(databaseURL: databaseURL)
var rejectedUnreadableFirstScan = false
do {
    _ = try unreadableMonitor.currentlyRunningTurnIDs(since: baseline)
} catch {
    rejectedUnreadableFirstScan = true
}
expect(rejectedUnreadableFirstScan, "an unreadable first scan does not establish the baseline")

let defaultsSuite = "CodexTaskMonitorChecks.\(UUID().uuidString)"
let defaults = UserDefaults(suiteName: defaultsSuite)!
defer { defaults.removePersistentDomain(forName: defaultsSuite) }
defaults.set(["turn-legacy"], forKey: "dismissedTurnIDs")
var preferences = MonitorPreferences(defaults: defaults)
expect(preferences.baseline == nil, "preferences start without a baseline")
preferences.initialize(baseline: baseline, adoptedTurnIDs: ["turn-adopted"])
preferences.dismiss(itemID: "thread-finished:turn-finished")
preferences = MonitorPreferences(defaults: defaults)
expect(preferences.baseline == baseline, "preferences restore the baseline")
expect(preferences.adoptedTurnIDs == ["turn-adopted"], "preferences restore adopted turns")
expect(preferences.dismissedTurnIDs == ["turn-legacy"], "preferences retain legacy handled turns")
expect(
    preferences.dismissedItemIDs == ["thread-finished:turn-finished"],
    "preferences restore the exact handled task"
)

print("Core checks passed")

private func date(_ seconds: TimeInterval) -> Date {
    Date(timeIntervalSince1970: seconds)
}

private func expect(_ condition: @autoclosure () throws -> Bool, _ message: String) rethrows {
    guard try condition() else {
        fatalError("Check failed: \(message)")
    }
}

private func createFixtureDatabase(at url: URL, rolloutPath: String) throws {
    var database: OpaquePointer?
    guard sqlite3_open(url.path, &database) == SQLITE_OK, let database else {
        throw FixtureError.sqlite
    }
    defer { sqlite3_close(database) }

    let escapedRolloutPath = rolloutPath.replacingOccurrences(of: "'", with: "''")
    let sql = """
        CREATE TABLE threads (
            id TEXT, title TEXT, cwd TEXT, updated_at_ms INTEGER,
            rollout_path TEXT, archived INTEGER, preview TEXT, thread_source TEXT
        );
        INSERT INTO threads VALUES
            ('visible', 'Visible task', '/tmp/project', 123000, '\(escapedRolloutPath)', 0, 'hello', 'user'),
            ('archived', 'Archived task', '/tmp/project', 124000, '/tmp/archived.jsonl', 1, 'hello', 'user'),
            ('hidden', 'Hidden task', '/tmp/project', 125000, '/tmp/hidden.jsonl', 0, '', 'user'),
            ('subagent', 'Subagent task', '/tmp/project', 126000, '/tmp/subagent.jsonl', 0, 'hello', 'subagent');
        """
    guard sqlite3_exec(database, sql, nil, nil, nil) == SQLITE_OK else {
        throw FixtureError.sqlite
    }
}

private func insertVisibleThread(into url: URL, id: String, rolloutPath: String) throws {
    var database: OpaquePointer?
    guard sqlite3_open(url.path, &database) == SQLITE_OK, let database else {
        throw FixtureError.sqlite
    }
    defer { sqlite3_close(database) }

    let escapedID = id.replacingOccurrences(of: "'", with: "''")
    let escapedRolloutPath = rolloutPath.replacingOccurrences(of: "'", with: "''")
    let sql = """
        INSERT INTO threads VALUES
            ('\(escapedID)', 'Unreadable task', '/tmp/project', 123000,
             '\(escapedRolloutPath)', 0, 'hello', 'user');
        """
    guard sqlite3_exec(database, sql, nil, nil, nil) == SQLITE_OK else {
        throw FixtureError.sqlite
    }
}

private enum FixtureError: Error {
    case sqlite
}
