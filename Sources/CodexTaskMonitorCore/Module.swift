import Foundation

public struct LifecycleEvent: Equatable, Sendable {
    public enum Kind: String, Sendable {
        case started = "task_started"
        case completed = "task_complete"
        case aborted = "turn_aborted"
    }

    public let kind: Kind
    public let turnID: String
    public let startedAt: Date
    public let completedAt: Date?
    public var activityDate: Date { completedAt ?? startedAt }

    public init(kind: Kind, turnID: String, startedAt: Date, completedAt: Date? = nil) {
        self.kind = kind
        self.turnID = turnID
        self.startedAt = startedAt
        self.completedAt = completedAt
    }
}

public enum TaskState: Equatable, Sendable {
    case running
    case waiting

    public static func resolve(
        _ event: LifecycleEvent,
        baseline: Date,
        adoptedTurnIDs: Set<String> = [],
        dismissedTurnIDs: Set<String> = []
    ) -> TaskState? {
        let eventBaseline = Date(
            timeIntervalSince1970: baseline.timeIntervalSince1970.rounded(.down)
        )
        let crossesBaseline = event.completedAt.map { $0 >= eventBaseline } ?? false
        guard event.startedAt >= eventBaseline
                || crossesBaseline
                || adoptedTurnIDs.contains(event.turnID)
        else {
            return nil
        }

        switch event.kind {
        case .started:
            return .running
        case .completed, .aborted:
            return dismissedTurnIDs.contains(event.turnID) ? nil : .waiting
        }
    }
}

public enum RolloutParser {
    public static func latestLifecycleEvent(at url: URL) throws -> LifecycleEvent? {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }

        let end = try handle.seekToEnd()
        var allowsIncompleteNewestLine = false
        if end > 0 {
            try handle.seek(toOffset: end - 1)
            guard let byte = try handle.read(upToCount: 1)?.first else {
                throw CodexDataError.unreadable("rollout tail")
            }
            allowsIncompleteNewestLine = byte != 0x0A
        }
        var offset = end
        var carry = Data()
        var state = ReverseLifecycleState(
            allowsIncompleteNewestLine: allowsIncompleteNewestLine
        )

        while offset > 0 {
            let count = min(UInt64(64 * 1_024), offset)
            offset -= count
            try handle.seek(toOffset: offset)
            guard let chunk = try handle.read(upToCount: Int(count)) else { break }
            var combined = Data(capacity: chunk.count + carry.count)
            combined.append(chunk)
            combined.append(carry)

            let lines = combined.split(separator: 0x0A, omittingEmptySubsequences: false)
            let completeLines: ArraySlice<Data.SubSequence>
            if offset > 0 {
                carry = Data(lines.first ?? Data.SubSequence())
                completeLines = lines.dropFirst()
            } else {
                carry.removeAll(keepingCapacity: false)
                completeLines = lines[...]
            }

            for line in completeLines.reversed() {
                if let event = try state.consume(line) {
                    return event
                }
            }
        }

        return state.newestUnmatchedTerminal.flatMap { lifecycleEvent(from: $0) }
    }

    public static func latestLifecycleEvent(in data: Data) throws -> LifecycleEvent? {
        var state = ReverseLifecycleState(
            allowsIncompleteNewestLine: data.last.map { $0 != 0x0A } ?? false
        )
        let lines = data.split(separator: 0x0A, omittingEmptySubsequences: true)
        for line in lines.reversed() {
            if let event = try state.consume(line) {
                return event
            }
        }
        return state.newestUnmatchedTerminal.flatMap { lifecycleEvent(from: $0) }
    }

    public static func latestLifecycleEvent(
        after current: LifecycleEvent?,
        appending data: Data
    ) throws -> LifecycleEvent? {
        var latest = current
        let lines = data.split(separator: 0x0A, omittingEmptySubsequences: true)
        let allowsIncompleteLastLine = data.last.map { $0 != 0x0A } ?? false

        for (index, line) in lines.enumerated() {
            guard containsLifecycleMarker(line) else { continue }

            let event: RolloutEvent
            do {
                event = try JSONDecoder().decode(RolloutEvent.self, from: Data(line))
            } catch where index == lines.count - 1 && allowsIncompleteLastLine {
                continue
            }

            guard event.type == "event_msg" else { continue }
            switch event.payload.type.flatMap(LifecycleEvent.Kind.init(rawValue:)) {
            case .started?:
                guard let turnID = event.payload.turnID,
                      let startedAt = event.payload.startedAt
                else {
                    throw CodexDataError.formatChanged
                }
                latest = LifecycleEvent(
                    kind: .started,
                    turnID: turnID,
                    startedAt: Date(timeIntervalSince1970: startedAt)
                )
            case .completed?, .aborted?:
                guard let turnID = event.payload.turnID,
                      event.payload.startedAt != nil,
                      event.payload.completedAt != nil
                else {
                    throw CodexDataError.formatChanged
                }
                if latest == nil || latest?.turnID == turnID {
                    latest = lifecycleEvent(from: event.payload)
                }
            case nil:
                continue
            }
        }

        return latest
    }
}

private let lifecycleMarkers = [
    Data("task_started".utf8),
    Data("task_complete".utf8),
    Data("turn_aborted".utf8),
]

private func containsLifecycleMarker(_ line: Data.SubSequence) -> Bool {
    lifecycleMarkers.contains { line.range(of: $0) != nil }
}

private struct ReverseLifecycleState {
    let allowsIncompleteNewestLine: Bool
    private var terminals: [String: RolloutEvent.Payload] = [:]
    private var isNewestLine = true
    private(set) var newestUnmatchedTerminal: RolloutEvent.Payload?

    init(allowsIncompleteNewestLine: Bool) {
        self.allowsIncompleteNewestLine = allowsIncompleteNewestLine
    }

    mutating func consume(_ line: Data.SubSequence) throws -> LifecycleEvent? {
        guard !line.isEmpty else { return nil }
        defer { isNewestLine = false }

        guard containsLifecycleMarker(line) else {
            return nil
        }

        let event: RolloutEvent
        do {
            event = try JSONDecoder().decode(RolloutEvent.self, from: Data(line))
        } catch where isNewestLine && allowsIncompleteNewestLine {
            // ponytail: Codex can be midway through the final JSONL line; retry next poll.
            return nil
        }

        guard event.type == "event_msg" else { return nil }
        switch event.payload.type.flatMap(LifecycleEvent.Kind.init(rawValue:)) {
        case .completed?, .aborted?:
            guard let turnID = event.payload.turnID,
                  event.payload.startedAt != nil,
                  event.payload.completedAt != nil
            else {
                throw CodexDataError.formatChanged
            }
            if terminals[turnID] == nil {
                terminals[turnID] = event.payload
                newestUnmatchedTerminal = newestUnmatchedTerminal ?? event.payload
            }
            return nil
        case .started?:
            guard let turnID = event.payload.turnID,
                  let startedAt = event.payload.startedAt
            else {
                throw CodexDataError.formatChanged
            }
            if let terminal = terminals[turnID] {
                return lifecycleEvent(from: terminal)
            }
            return LifecycleEvent(
                kind: .started,
                turnID: turnID,
                startedAt: Date(timeIntervalSince1970: startedAt)
            )
        case nil:
            return nil
        }
    }
}

private func lifecycleEvent(from payload: RolloutEvent.Payload) -> LifecycleEvent? {
    guard let type = payload.type,
          let kind = LifecycleEvent.Kind(rawValue: type),
          let turnID = payload.turnID,
          let startedAt = payload.startedAt,
          let completedAt = payload.completedAt
    else {
        return nil
    }
    return LifecycleEvent(
        kind: kind,
        turnID: turnID,
        startedAt: Date(timeIntervalSince1970: startedAt),
        completedAt: Date(timeIntervalSince1970: completedAt)
    )
}

private struct RolloutEvent: Decodable {
    let type: String
    let payload: Payload

    struct Payload: Decodable {
        let type: String?
        let turnID: String?
        let startedAt: TimeInterval?
        let completedAt: TimeInterval?

        enum CodingKeys: String, CodingKey {
            case type
            case turnID = "turn_id"
            case startedAt = "started_at"
            case completedAt = "completed_at"
        }
    }
}
