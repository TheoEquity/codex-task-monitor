public enum AccessibilityPermissionGate {
    public static func request(
        isTrusted: () -> Bool,
        prompt: () -> Bool
    ) -> Bool {
        guard !isTrusted() else { return true }
        return prompt()
    }
}
