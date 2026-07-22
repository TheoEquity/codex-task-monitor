import Foundation

public struct MonitorPreferences {
    public private(set) var baseline: Date?
    public private(set) var adoptedTurnIDs: Set<String>
    public private(set) var dismissedTurnIDs: Set<String>
    public private(set) var dismissedItemIDs: Set<String>

    private let defaults: UserDefaults

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        baseline = defaults.object(forKey: Key.baseline) as? Date
        adoptedTurnIDs = Set(defaults.stringArray(forKey: Key.adoptedTurnIDs) ?? [])
        dismissedTurnIDs = Set(defaults.stringArray(forKey: Key.dismissedTurnIDs) ?? [])
        dismissedItemIDs = Set(defaults.stringArray(forKey: Key.dismissedItemIDs) ?? [])
    }

    public mutating func initialize(baseline: Date, adoptedTurnIDs: Set<String>) {
        guard self.baseline == nil else { return }
        self.baseline = baseline
        self.adoptedTurnIDs = adoptedTurnIDs
        defaults.set(baseline, forKey: Key.baseline)
        defaults.set(adoptedTurnIDs.sorted(), forKey: Key.adoptedTurnIDs)
    }

    public mutating func dismiss(itemID: String) {
        dismissedItemIDs.insert(itemID)
        defaults.set(dismissedItemIDs.sorted(), forKey: Key.dismissedItemIDs)
    }

    private enum Key {
        static let baseline = "monitorStartedAt"
        static let adoptedTurnIDs = "adoptedTurnIDs"
        static let dismissedTurnIDs = "dismissedTurnIDs"
        static let dismissedItemIDs = "dismissedItemIDs"
    }
}
