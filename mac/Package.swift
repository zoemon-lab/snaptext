// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "SnapText",
    platforms: [.macOS(.v15)],
    targets: [
        .executableTarget(
            name: "SnapText",
            path: "Sources/SnapText",
            resources: [.copy("Resources")]
        )
    ],
    swiftLanguageModes: [.v5]
)
