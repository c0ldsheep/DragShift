import CoreGraphics
import Foundation
import Testing
@testable import DragShiftCore

/// Stitch: several pictures become one long JPG, top to bottom.
struct StitchTests {
    @Test func threeScansBecomeOneLongPicture() throws {
        let s = try Sandbox()
        // Three "pages" in different solid colours, so the order can be read back from the result.
        let colours: [(CGFloat, CGFloat, CGFloat)] = [(1, 0, 0), (0, 1, 0), (0, 0, 1)]
        let pages = try colours.enumerated().map { i, c -> URL in
            let ctx = ImageOps.context(1653, 2339, opaque: true)!
            ctx.setFillColor(CGColor(red: c.0, green: c.1, blue: c.2, alpha: 1))
            ctx.fill(CGRect(x: 0, y: 0, width: 1653, height: 2339))
            return try s.writeImage(ctx.makeImage()!, "GST \(i + 1).jpg", type: .jpeg)
        }
        // Given out of order, as a drag from Finder can be.
        let out = try ImageOps.stitch([pages[2], pages[0], pages[1]].sorted(by: Naming.finderOrder), quality: 0.85, dir: s.dir)
        #expect(out.lastPathComponent == "GST 1 (3 stitched).jpg")
        #expect(ImageOps.pixelSize(out) == CGSize(width: 1653, height: 7017))
        let img = try ImageOps.load(out)
        let ctx = ImageOps.context(img.width, img.height, opaque: true)!
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: img.width, height: img.height))
        let px = ctx.data!.assumingMemoryBound(to: UInt8.self)
        func strongest(row: Int) -> Int { // 0 red, 1 green, 2 blue; row counted from the top
            let p = px + row * ctx.bytesPerRow + 800 * 4
            return [Int(p[0]), Int(p[1]), Int(p[2])].enumerated().max { $0.element < $1.element }!.offset
        }
        #expect(strongest(row: 1000) == 0 && strongest(row: 3500) == 1 && strongest(row: 6000) == 2, "page 1 on top, then 2, then 3")
        for p in pages { #expect(FileManager.default.fileExists(atPath: p.path), "originals stay") }
    }

    @Test func mixedWidthsShareTheWidestAndHugeStacksShrink() {
        let mixed = ImageOps.stitchPlan([CGSize(width: 1000, height: 500), CGSize(width: 2000, height: 500)])
        #expect(mixed.width == 2000 && mixed.heights == [1000, 500])
        let huge = ImageOps.stitchPlan(Array(repeating: CGSize(width: 4000, height: 6000), count: 30))
        #expect(huge.heights.reduce(0, +) <= ImageOps.jpegMaxSide)
        #expect(huge.width * huge.heights.reduce(0, +) <= ImageOps.stitchMaxPixels + huge.width * 30)
    }

    @Test func stitchIsOfferedOnlyForSeveralPictures() {
        let one = Catalog.pages(for: [URL(fileURLWithPath: "/a/GST 1.jpg")]).flatMap(\.actions).map(\.id)
        let three = Catalog.pages(for: (1...3).map { URL(fileURLWithPath: "/a/GST \($0).jpg") })
        #expect(!one.contains("img.stitch"))
        #expect(three.flatMap(\.actions).map(\.id).contains("img.stitch"))
        for p in three { #expect(p.actions.count <= Catalog.maxSlices) }
    }
}
