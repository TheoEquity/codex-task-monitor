import Foundation

public enum CodexThreadLink {
    public static func openURL(threadID: String) -> URL? {
        guard UUID(uuidString: threadID) != nil else { return nil }
        return URL(string: "codex://threads/\(threadID)")
    }
}
