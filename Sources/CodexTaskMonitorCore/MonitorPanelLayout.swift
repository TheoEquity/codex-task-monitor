import Foundation

public enum MonitorPanelLayout {
    public static func height(itemCount: Int, hasError: Bool) -> CGFloat {
        let visibleRows = min(max(itemCount, 0), 6)
        let rowsAndDividers = CGFloat(visibleRows * 63)
        return 48 + rowsAndDividers + (hasError ? 32 : 0)
    }
}
