import Foundation
import Testing
@testable import DragShiftCore

/// Files built to crash the app or fill the disk: refused with a plain message, nothing left behind.
@Suite struct BombTests {
    /// A real, valid PNG of 20,000 × 20,000 one-bit pixels: 48 KB on disk, 400 megapixels (1.6 GB) once decoded.
    static var bomb: URL {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Fixtures/bomb-400mp.png")
    }

    @Test func aSmallPictureThatDecodesToGigabytesIsRefusedNotOpened() throws {
        #expect(ImageOps.pixelSize(Self.bomb) == CGSize(width: 20_000, height: 20_000))
        do {
            _ = try ImageOps.load(Self.bomb)
            Issue.record("it was opened")
        } catch {
            #expect(error.localizedDescription.contains("too big to open safely"), "\(error.localizedDescription)")
        }
        // a big but real photo still opens
        let s = try Sandbox()
        let ok = try s.writeImage(Sandbox.image(8000, 6000), "Big.jpg", type: .jpeg)
        #expect(try ImageOps.load(ok).width == 8000)
    }

    @Test func aZipThatUnpacksTooBigIsRefusedAndLeavesNothing() throws {
        let s = try Sandbox()
        let zeros = s.url("zeros.bin")
        try Data(count: 3 * 1024 * 1024).write(to: zeros)            // 3 MB of zeros squeezes to a few KB
        let zip = try Archive.zip([zeros], dir: s.dir)
        #expect((Archive.listedSize(zip)?.bytes ?? 0) >= 3 * 1024 * 1024)
        #expect(throws: DragShiftError.self) { try Archive.unzip(zip, dir: s.dir, limit: 1024 * 1024) }
        #expect(!s.files().contains("zeros.bin 2") && s.files().filter { $0.hasPrefix("zeros") }.count == 2, "nothing unpacked: \(s.files())")
        let folder = try Archive.unzip(zip, dir: s.dir)              // the normal limit: fine
        // one zipped file unpacks as just that file, like Finder's (not inside its old parent folder)
        #expect(FileManager.default.fileExists(atPath: folder.appendingPathComponent("zeros.bin").path), "\((try? FileManager.default.subpathsOfDirectory(atPath: folder.path)) ?? [])")
    }
}
