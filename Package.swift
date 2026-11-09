// swift-tools-version:6.0
import PackageDescription

let mode: [SwiftSetting] = [.swiftLanguageMode(.v5)]

let package = Package(
    name: "DragShift",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "DragShift", targets: ["DragShift"]),
    ],
    targets: [
        // Everything that touches files: formats, conversions, tools. No UI.
        .target(name: "DragShiftCore", swiftSettings: mode),
        // The menu bar app: drag watcher, finger trigger, ring menu, editors.
        .executableTarget(name: "DragShift", dependencies: ["DragShiftCore"], swiftSettings: mode),
        .testTarget(name: "DragShiftCoreTests", dependencies: ["DragShiftCore"], swiftSettings: mode),
    ]
)
