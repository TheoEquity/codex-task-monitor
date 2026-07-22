public enum MonitorListUpdate {
    public static func insertedID(from oldIDs: [String], to newIDs: [String]) -> String? {
        let oldIDs = Set(oldIDs)
        guard oldIDs.isSubset(of: Set(newIDs)) else { return nil }
        return newIDs.first { !oldIDs.contains($0) }
    }
}
