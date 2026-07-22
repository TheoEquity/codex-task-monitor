import Foundation

public struct MonitorItem: Identifiable, Equatable, Sendable {
    public var id: String { "\(threadID):\(turnID)" }

    public let threadID: String
    public let turnID: String
    public let title: String
    public let projectName: String
    public let eventDate: Date
    public let state: TaskState
}

public final class TaskMonitor {
    public private(set) var unreadableRolloutCount = 0

    private let databaseURL: URL
    private var cache: [URL: CacheEntry] = [:]

    public init(databaseURL: URL) {
        self.databaseURL = databaseURL
    }

    public func currentlyRunningTurnIDs(since date: Date) throws -> Set<String> {
        let events = try latestEvents(updatedAfter: date)
        guard unreadableRolloutCount == 0 else {
            throw CodexDataError.unreadable("rollouts")
        }
        return Set(events.values.filter { $0.kind == .started }.map(\.turnID))
    }

    public func scan(
        baseline: Date,
        adoptedTurnIDs: Set<String> = [],
        dismissedTurnIDs: Set<String> = []
    ) throws -> [MonitorItem] {
        // ponytail: one-hour lookback only recovers first-launch turns; new turns have no TTL.
        let oldestRelevantUpdate = baseline.addingTimeInterval(-3_600)
        return try latestEvents(updatedAfter: oldestRelevantUpdate).compactMap { thread, event in
            guard let state = TaskState.resolve(
                event,
                baseline: baseline,
                adoptedTurnIDs: adoptedTurnIDs,
                dismissedTurnIDs: dismissedTurnIDs
            ) else {
                return nil
            }

            return MonitorItem(
                threadID: thread.id,
                turnID: event.turnID,
                title: thread.title.isEmpty ? "New chat" : thread.title,
                projectName: URL(fileURLWithPath: thread.cwd).lastPathComponent,
                eventDate: event.activityDate,
                state: state
            )
        }
        .sorted { $0.eventDate > $1.eventDate }
    }

    private func latestEvents(updatedAfter date: Date) throws -> [ThreadRecord: LifecycleEvent] {
        unreadableRolloutCount = 0
        var events: [ThreadRecord: LifecycleEvent] = [:]

        for thread in try ThreadStore.readThreads(from: databaseURL, updatedAfter: date) {
            do {
                if let event = try event(for: thread.rolloutURL) {
                    events[thread] = event
                }
            } catch CodexDataError.formatChanged {
                throw CodexDataError.formatChanged
            } catch {
                unreadableRolloutCount += 1
                if let event = cache[thread.rolloutURL]?.event {
                    events[thread] = event
                }
            }
        }

        if events.isEmpty && unreadableRolloutCount > 0 {
            throw CodexDataError.unreadable("rollouts")
        }
        return events
    }

    private func event(for url: URL) throws -> LifecycleEvent? {
        let values = try url.resourceValues(forKeys: [.contentModificationDateKey, .fileSizeKey])
        guard let fileSize = values.fileSize else {
            throw CodexDataError.unreadable("rollout size")
        }
        let signature = FileSignature(
            modificationDate: values.contentModificationDate,
            size: fileSize
        )
        if let cached = cache[url], cached.signature == signature {
            return cached.event
        }

        if let cached = cache[url], fileSize > cached.processedSize {
            let appendedData = try readData(at: url, fromOffset: cached.processedSize)
            var newLines = cached.trailingFragment
            newLines.append(appendedData)
            let event = try RolloutParser.latestLifecycleEvent(
                after: cached.event,
                appending: newLines
            )
            let processedSize = cached.processedSize + appendedData.count
            cache[url] = CacheEntry(
                signature: FileSignature(
                    modificationDate: values.contentModificationDate,
                    size: processedSize
                ),
                event: event,
                processedSize: processedSize,
                trailingFragment: trailingFragment(in: newLines)
            )
            return event
        }

        let event = try RolloutParser.latestLifecycleEvent(at: url)
        cache[url] = CacheEntry(
            signature: signature,
            event: event,
            processedSize: fileSize,
            trailingFragment: try trailingFragment(at: url, endingAt: fileSize)
        )
        return event
    }

    private func readData(at url: URL, fromOffset offset: Int) throws -> Data {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        try handle.seek(toOffset: UInt64(offset))
        return try handle.readToEnd() ?? Data()
    }

    private func trailingFragment(at url: URL, endingAt end: Int) throws -> Data {
        guard end > 0 else { return Data() }

        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var offset = end
        var laterChunks: [Data] = []

        while offset > 0 {
            let count = min(64 * 1_024, offset)
            offset -= count
            try handle.seek(toOffset: UInt64(offset))
            guard let chunk = try handle.read(upToCount: count), chunk.count == count else {
                throw CodexDataError.unreadable("rollout tail")
            }

            if let newline = chunk.lastIndex(of: 0x0A) {
                var fragment = Data(chunk[chunk.index(after: newline)...])
                for laterChunk in laterChunks.reversed() {
                    fragment.append(laterChunk)
                }
                return fragment
            }
            laterChunks.append(chunk)
        }

        var fragment = Data()
        for chunk in laterChunks.reversed() {
            fragment.append(chunk)
        }
        return fragment
    }

    private func trailingFragment(in data: Data) -> Data {
        guard let newline = data.lastIndex(of: 0x0A) else { return data }
        return Data(data[data.index(after: newline)...])
    }
}

private struct CacheEntry {
    let signature: FileSignature
    let event: LifecycleEvent?
    let processedSize: Int
    let trailingFragment: Data
}

private struct FileSignature: Equatable {
    let modificationDate: Date?
    let size: Int
}

extension ThreadRecord: Hashable {
    public func hash(into hasher: inout Hasher) {
        hasher.combine(id)
    }
}
