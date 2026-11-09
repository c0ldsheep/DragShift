import Foundation
import Testing
@testable import DragShiftCore

/// Every slice the ring offers, on realistic files of every kind (SelfCheck). The same check runs in the app on any
/// Mac, Intel included: `DragShift --selftest <folder> [photo]`.
@Suite(.serialized) struct EveryActionTests {
    static var cat: URL {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("site/assets/img/cat.jpg")
    }

    @Test func everySliceForEveryKindOfFile() async throws {
        let s = try Sandbox()
        let r = try await SelfCheck.run(in: s.dir, photo: Self.cat)
        print("\n" + r.lines.joined(separator: "\n") + "\n" + r.summary + "\n")
        #expect(r.failed.isEmpty, "\(r.failed.joined(separator: "\n"))")
        #expect(r.worked > 140, "\(r.summary)")
    }
}
