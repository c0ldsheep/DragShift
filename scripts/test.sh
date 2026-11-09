#!/bin/zsh
# Runs the engine tests. Command Line Tools keep the swift-testing macro plugin in a
# sub-folder SwiftPM doesn't search, so point at it explicitly.
cd "$(dirname "$0")/.."
PLUGINS=/Library/Developer/CommandLineTools/usr/lib/swift/host/plugins/testing
# One test at a time, like the app runs jobs (Vision and AVFoundation are heavy).
exec swift test --no-parallel -Xswiftc -plugin-path -Xswiftc "$PLUGINS" "$@"
