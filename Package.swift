// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "CodexTaskMonitor",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "CodexTaskMonitorCore", targets: ["CodexTaskMonitorCore"]),
        .executable(name: "CodexTaskMonitor", targets: ["CodexTaskMonitor"]),
    ],
    targets: [
        .target(
            name: "CodexTaskMonitorCore",
            linkerSettings: [.linkedLibrary("sqlite3")]
        ),
        .executableTarget(
            name: "CoreChecks",
            dependencies: ["CodexTaskMonitorCore"]
        ),
        .executableTarget(
            name: "CodexTaskMonitor",
            dependencies: ["CodexTaskMonitorCore"]
        ),
    ]
)
