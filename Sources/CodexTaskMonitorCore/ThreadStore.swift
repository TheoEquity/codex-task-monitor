import Foundation
import SQLite3

public struct ThreadRecord: Equatable, Sendable {
    public let id: String
    public let title: String
    public let cwd: String
    public let updatedAt: Date
    public let rolloutURL: URL

    public init(id: String, title: String, cwd: String, updatedAt: Date, rolloutURL: URL) {
        self.id = id
        self.title = title
        self.cwd = cwd
        self.updatedAt = updatedAt
        self.rolloutURL = rolloutURL
    }
}

public enum CodexDataError: LocalizedError, Equatable {
    case databaseMissing
    case formatChanged
    case unreadable(String)

    public var errorDescription: String? {
        switch self {
        case .databaseMissing:
            return "未找到本机 Codex 数据"
        case .formatChanged:
            return "Codex 数据格式已变化"
        case .unreadable:
            return "暂时无法读取 Codex 数据"
        }
    }
}

public enum ThreadStore {
    public static func readThreads(
        from databaseURL: URL,
        updatedAfter date: Date = .distantPast
    ) throws -> [ThreadRecord] {
        guard FileManager.default.fileExists(atPath: databaseURL.path) else {
            throw CodexDataError.databaseMissing
        }

        var database: OpaquePointer?
        let flags = SQLITE_OPEN_READONLY | SQLITE_OPEN_FULLMUTEX
        guard sqlite3_open_v2(databaseURL.path, &database, flags, nil) == SQLITE_OK,
              let database
        else {
            if let database { sqlite3_close(database) }
            throw CodexDataError.unreadable("open")
        }
        defer { sqlite3_close(database) }
        sqlite3_busy_timeout(database, 500)

        let sql = """
            SELECT id, title, cwd, updated_at_ms, rollout_path
            FROM threads
            WHERE archived = 0
              AND preview <> ''
              AND COALESCE(thread_source, 'user') <> 'subagent'
              AND updated_at_ms >= ?
            ORDER BY updated_at_ms DESC
            """
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(database, sql, -1, &statement, nil) == SQLITE_OK,
              let statement
        else {
            throw CodexDataError.formatChanged
        }
        defer { sqlite3_finalize(statement) }

        let updatedAtMilliseconds = Int64(
            (date.timeIntervalSince1970 * 1_000).rounded(.down)
        )
        guard sqlite3_bind_int64(statement, 1, updatedAtMilliseconds) == SQLITE_OK else {
            throw CodexDataError.unreadable("bind")
        }

        var records: [ThreadRecord] = []
        while true {
            switch sqlite3_step(statement) {
            case SQLITE_ROW:
                guard let id = string(statement, 0),
                      let title = string(statement, 1),
                      let cwd = string(statement, 2),
                      let rolloutPath = string(statement, 4)
                else {
                    throw CodexDataError.formatChanged
                }
                let updatedAt = Date(
                    timeIntervalSince1970: Double(sqlite3_column_int64(statement, 3)) / 1_000
                )
                records.append(
                    ThreadRecord(
                        id: id,
                        title: title,
                        cwd: cwd,
                        updatedAt: updatedAt,
                        rolloutURL: URL(fileURLWithPath: rolloutPath)
                    )
                )
            case SQLITE_DONE:
                return records
            default:
                throw CodexDataError.unreadable("read")
            }
        }
    }

    private static func string(_ statement: OpaquePointer, _ column: Int32) -> String? {
        sqlite3_column_text(statement, column).map { String(cString: $0) }
    }
}
