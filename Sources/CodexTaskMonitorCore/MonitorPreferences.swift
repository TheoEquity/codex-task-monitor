import Foundation

public struct MonitorPreferences {
    public private(set) var baseline: Date?
    public private(set) var adoptedTurnIDs: Set<String>
    public private(set) var dismissedTurnIDs: Set<String>

    private let defaults: UserDefaults

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        baseline = defaults.object(forKey: Key.baseline) as? Date
        adoptedTurnIDs = Set(defaults.stringArray(forKey: Key.adoptedTurnIDs) ?? [])
        dismissedTurnIDs = Set(defaults.stringArray(forKey: Key.dismissedTurnIDs) ?? [])
    }

    public mutating func initialize(baseline: Date, adoptedTurnIDs: Set<String>) {
        guard self.baseline == nil else { return }
        self.baseline = baseline
        self.adoptedTurnIDs = adoptedTurnIDs
        defaults.set(baseline, forKey: Key.baseline)
        defaults.set(adoptedTurnIDs.sorted(), forKey: Key.adoptedTurnIDs)
    }

    public mutating func dismiss(turnID: String) {
        dismissedTurnIDs.insert(turnID)
        defaults.set(dismissedTurnIDs.sorted(), forKey: Key.dismissedTurnIDs)
    }

    private enum Key {
        static let baseline = "monitorStartedAt"
        static let adoptedTurnIDs = "adoptedTurnIDs"
        static let dismissedTurnIDs = "dismissedTurnIDs"
    }
}
