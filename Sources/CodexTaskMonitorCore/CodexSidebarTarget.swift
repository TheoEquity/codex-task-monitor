import Foundation

public enum SessionIndex {
    public static func threadName(for threadID: String, in data: Data) throws -> String? {
        let lines = data.split(separator: 0x0A, omittingEmptySubsequences: false)
        let allowsIncompleteLastLine = data.last.map { $0 != 0x0A } ?? false
        var threadName: String?

        for (index, line) in lines.enumerated() where !line.isEmpty {
            let entry: Entry
            do {
                entry = try JSONDecoder().decode(Entry.self, from: Data(line))
            } catch where index == lines.count - 1 && allowsIncompleteLastLine {
                continue
            } catch {
                throw CodexDataError.formatChanged
            }

            if entry.id == threadID {
                threadName = entry.threadName.isEmpty ? nil : entry.threadName
            }
        }

        return threadName
    }

    private struct Entry: Decodable {
        let id: String
        let threadName: String

        private enum CodingKeys: String, CodingKey {
            case id
            case threadName = "thread_name"
        }
    }
}

public enum SidebarTaskMatch {
    public static func uniqueIndex(for title: String, among candidates: [String]) -> Int? {
        var matchedIndex: Int?
        for (index, candidate) in candidates.enumerated() where candidate == title {
            guard matchedIndex == nil else { return nil }
            matchedIndex = index
        }
        return matchedIndex
    }
}

public enum SidebarThreadGroup: Equatable {
    case pinned
    case project(name: String)
    case projectless

    public var listDescription: String {
        switch self {
        case .pinned:
            return "置顶"
        case let .project(name):
            return "\(name)中的已安排任务"
        case .projectless:
            return ""
        }
    }

    public static func resolve(
        threadID: String,
        cwd: String? = nil,
        in data: Data
    ) throws -> SidebarThreadGroup? {
        let state = try JSONDecoder().decode(GlobalState.self, from: data)
        if state.pinnedThreadIDs?.contains(threadID) == true {
            return .pinned
        }
        if let projectID = state.threadProjectAssignments?[threadID]?.projectID,
           let projectName = state.localProjects?[projectID]?.name
        {
            return .project(name: projectName)
        }
        if state.projectlessThreadIDs?.contains(threadID) == true {
            return .projectless
        }
        guard let cwd, !cwd.isEmpty else { return nil }

        let normalizedCWD = URL(fileURLWithPath: cwd).standardizedFileURL.path
        let matchingProjects: [String] = state.localProjects?.values.compactMap {
            project -> String? in
            let containsRoot = project.rootPaths?.contains { rootPath in
                URL(fileURLWithPath: rootPath).standardizedFileURL.path == normalizedCWD
            } == true
            return containsRoot ? project.name : nil
        } ?? []
        guard matchingProjects.count == 1 else { return nil }
        return .project(name: matchingProjects[0])
    }

    private struct GlobalState: Decodable {
        let pinnedThreadIDs: [String]?
        let projectlessThreadIDs: [String]?
        let threadProjectAssignments: [String: ProjectAssignment]?
        let localProjects: [String: LocalProject]?

        private enum CodingKeys: String, CodingKey {
            case pinnedThreadIDs = "pinned-thread-ids"
            case projectlessThreadIDs = "projectless-thread-ids"
            case threadProjectAssignments = "thread-project-assignments"
            case localProjects = "local-projects"
        }
    }

    private struct ProjectAssignment: Decodable {
        let projectID: String

        private enum CodingKeys: String, CodingKey {
            case projectID = "projectId"
        }
    }

    private struct LocalProject: Decodable {
        let name: String
        let rootPaths: [String]?
    }
}

public struct SidebarTarget: Equatable {
    public let title: String
    public let group: SidebarThreadGroup

    public init(title: String, group: SidebarThreadGroup) {
        self.title = title
        self.group = group
    }
}

public enum SidebarTargetResolutionError: Error {
    case sessionIndexChanged
    case globalStateChanged
}

public enum SidebarTargetResolver {
    public static func resolve(
        threadID: String,
        cwd: String,
        sessionIndexData: Data,
        globalStateData: Data
    ) throws -> SidebarTarget? {
        let title: String?
        do {
            title = try SessionIndex.threadName(for: threadID, in: sessionIndexData)
        } catch {
            throw SidebarTargetResolutionError.sessionIndexChanged
        }
        guard let title, !title.isEmpty else { return nil }

        let group: SidebarThreadGroup?
        do {
            group = try SidebarThreadGroup.resolve(
                threadID: threadID,
                cwd: cwd,
                in: globalStateData
            )
        } catch {
            throw SidebarTargetResolutionError.globalStateChanged
        }
        guard let group else { return nil }
        return SidebarTarget(title: title, group: group)
    }
}
