import AppKit
import AVFoundation
import Foundation
import ImageIO
import PDFKit
import Testing
@testable import DragShiftCore

private func rgba(_ img: CGImage) -> [UInt8] {
    var out = [UInt8](repeating: 0, count: img.width * img.height * 4)
    out.withUnsafeMutableBytes { buf in
        let ctx = CGContext(data: buf.baseAddress, width: img.width, height: img.height, bitsPerComponent: 8,
                            bytesPerRow: img.width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: img.width, height: img.height))
    }
    return out
}

private func decodeWebP(_ data: Data) throws -> CGImage {
    let src = try #require(CGImageSourceCreateWithData(data as CFData, nil))
    return try #require(CGImageSourceCreateImageAtIndex(src, 0, nil))
}

/// Flat colours and repeated rows, like a screenshot.
private func screenshotLike(_ w: Int, _ h: Int) -> CGImage {
    let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
    ctx.setFillColor(CGColor(srgbRed: 0.97, green: 0.97, blue: 0.98, alpha: 1)); ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
    ctx.setFillColor(CGColor(srgbRed: 0.2, green: 0.4, blue: 0.9, alpha: 1)); ctx.fill(CGRect(x: 10, y: 10, width: w / 2, height: h / 3))
    ctx.setFillColor(CGColor(srgbRed: 1, green: 0.3, blue: 0.4, alpha: 1)); ctx.fillEllipse(in: CGRect(x: w / 2, y: h / 2, width: w / 3, height: h / 3))
    return ctx.makeImage()!
}

@Suite struct WebPTests {
    @Test func losslessRoundTripIsExact() throws {
        let cases: [(String, CGImage)] = [
            ("noise", Sandbox.image(97, 61, noise: true)),
            ("flat", screenshotLike(640, 400)),
            ("tiny", Sandbox.image(1, 1)),
            ("odd", Sandbox.image(37, 23)),
            ("text", Sandbox.textImage(["WebP without Homebrew", "1234567890"], width: 500, fontSize: 30)),
        ]
        for (name, img) in cases {
            let data = try WebPLossless.encode(img)
            let back = try decodeWebP(data)
            #expect(back.width == img.width && back.height == img.height, "\(name) size")
            #expect(rgba(back) == rgba(img), "\(name) pixels")
        }
    }

    @Test func keepsTransparency() throws {
        let img = Sandbox.image(120, 80, transparentBorder: 15)
        let back = try decodeWebP(try WebPLossless.encode(img))
        #expect(ImageOps.hasTransparency(back))
        let a = rgba(img), b = rgba(back)
        #expect(zip(a, b).allSatisfy { abs(Int($0) - Int($1)) <= 1 })
    }

    @Test func screenshotsShrink() throws {
        let img = screenshotLike(1200, 800)
        let data = try WebPLossless.encode(img)
        #expect(data.count < 1200 * 800 / 20) // far below 4 bytes per pixel
    }
}

@Suite struct RecipeTests {
    @Test func instaPostAndWhatsApp() async throws {
        let s = try Sandbox()
        let photo = try s.writeImage(Sandbox.image(2000, 1000, noise: true), "Holiday.png", type: .png)
        let post = try #require(Recipe.builtIns.first { $0.id == "recipe.insta-post" })
        let r = await Engine.runRecipe(post, inputs: [photo], options: Options())
        #expect(r.failures.isEmpty, "\(r.failures)")
        let out = try #require(r.outputs.first)
        #expect(out.lastPathComponent == "Holiday Insta Post.jpg")
        let img = try ImageOps.load(out)
        #expect(img.width == 1080 && img.height == 1350)
        #expect(s.files() == ["Holiday Insta Post.jpg", "Holiday.png"]) // no leftovers

        let small = try #require(Recipe.builtIns.first { $0.id == "recipe.whatsapp" })
        let w = await Engine.runRecipe(small, inputs: [photo], options: Options())
        let wout = try #require(w.outputs.first)
        #expect(wout.pathExtension == "jpg")
        #expect(max(try ImageOps.load(wout).width, try ImageOps.load(wout).height) == 1600)
    }

    @Test func groupRecipeMakesOnePDF() async throws {
        let s = try Sandbox()
        let files = try (1...3).map { try s.writeImage(Sandbox.image(400, 300, noise: true), "Scan \($0).png", type: .png) }
        let email = try #require(Recipe.builtIns.first { $0.id == "recipe.email-pdf" })
        let r = await Engine.runRecipe(email, inputs: files.reversed(), options: Options())
        #expect(r.failures.isEmpty, "\(r.failures)")
        let out = try #require(r.outputs.first)
        #expect(r.outputs.count == 1)
        #expect(out.lastPathComponent == "Scan 1 Email PDF.pdf")
        #expect(PDFDocument(url: out)?.pageCount == 3)
    }

    @Test func storyAndWatermark() async throws {
        let s = try Sandbox()
        let photo = try s.writeImage(Sandbox.image(800, 600), "Pic.png", type: .png)
        let story = try #require(Recipe.builtIns.first { $0.id == "recipe.insta-story" })
        let r = await Engine.runRecipe(story, inputs: [photo], options: Options())
        let img = try ImageOps.load(try #require(r.outputs.first))
        #expect(img.width == 1080 && img.height == 1920)

        var o = Options()
        o.watermark = WatermarkStyle(text: "SAMPLE", position: .bottomRight, size: 0.08, opacity: 1)
        let mark = try #require(Recipe.builtIns.first { $0.id == "recipe.watermark" })
        let w = await Engine.runRecipe(mark, inputs: [photo], options: o)
        let marked = try ImageOps.load(try #require(w.outputs.first))
        let before = try ImageOps.load(photo)
        // The bottom-right corner changed (text drawn), the top-left didn't.
        let a = rgba(before), b = rgba(marked)
        func px(_ buf: [UInt8], _ x: Int, _ y: Int) -> Int { Int(buf[(y * 800 + x) * 4]) }
        var changed = 0
        for y in 520..<590 { for x in 560..<790 where abs(px(a, x, y) - px(b, x, y)) > 40 { changed += 1 } }
        #expect(changed > 200)
        #expect(abs(px(a, 20, 20) - px(b, 20, 20)) < 12)
    }

    @Test func failingStepStopsCleanly() async throws {
        let s = try Sandbox()
        let bad = s.url("bad.png")
        try Data("nope".utf8).write(to: bad)
        let r = await Engine.runRecipe(Recipe(name: "Test", steps: ["img.size.100", "img.to.jpg"]), inputs: [bad], options: Options())
        #expect(r.outputs.isEmpty && !r.failures.isEmpty)
        #expect(s.files() == ["bad.png"])
    }
}

@Suite struct QRTests {
    @Test func makeAndRead() throws {
        let img = try QR.make("https://www.example.com/shop?item=blue-mug")
        #expect(try QR.read(img) == ["https://www.example.com/shop?item=blue-mug"])
        #expect(QR.name(for: "https://www.example.com/shop") == "QR example.com")
        #expect(throws: DragShiftError.self) { try QR.make(String(repeating: "x", count: 4000)) }
    }

    @Test func fromTextFileAndFromPicture() async throws {
        let s = try Sandbox()
        let txt = s.url("Link.txt")
        try "https://example.com/hello".write(to: txt, atomically: true, encoding: .utf8)
        let a = try #require(Catalog.pages(for: [txt]).flatMap(\.actions).first { $0.id == "doc.qr" })
        let r = await Engine.run(a, inputs: [txt], options: Options())
        let png = try #require(r.outputs.first)
        #expect(png.lastPathComponent == "QR example.com.png")
        let read = try #require(Catalog.pages(for: [png]).flatMap(\.actions).first { $0.id == "img.qr.read" })
        let back = await Engine.run(read, inputs: [png], options: Options())
        #expect(back.text == "https://example.com/hello")
        #expect(back.outputs.isEmpty)
    }
}

@Suite struct SearchablePDFTests {
    @Test func scannedPagesGetText() async throws {
        let s = try Sandbox()
        let scan = try s.writeImage(Sandbox.textImage(["Invoice number 7781", "Total due 4500"]), "Scan.png", type: .png)
        let pdf = s.url("Scan.pdf")
        try ImageOps.makePDF([scan, scan], to: pdf, quality: 0.9)
        let blank = (PDFDocument(url: pdf)?.string ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        #expect(blank.isEmpty)
        let out = try PDFOps.makeSearchable(pdf, dir: s.dir)
        let doc = try #require(PDFDocument(url: out))
        #expect(doc.pageCount == 2)
        #expect(doc.string?.contains("7781") == true)
        #expect(!(doc.findString("Total", withOptions: .caseInsensitive).isEmpty))
        let text = try s.pdf("Typed.pdf", pages: ["Already has text"])
        #expect(throws: DragShiftError.self) { try PDFOps.makeSearchable(text, dir: s.dir) }
    }
}

@Suite(.serialized) struct ReelTests {
    @Test func verticalSquareAndSpeed() async throws {
        let s = try Sandbox()
        let mov = try await s.video()
        let v = try await MediaOps.reframe(mov, size: CGSize(width: 1080, height: 1920), suffix: "9x16", dir: s.dir)
        let track = try #require(try await AVURLAsset(url: v).loadTracks(withMediaType: .video).first)
        let size = try await track.load(.naturalSize)
        #expect(size.width == 1080 && size.height == 1920)
        #expect(v.lastPathComponent == "Clip 9x16.mp4")

        let fast = try await MediaOps.speed(mov, factor: 2, dir: s.dir)
        let d = await MediaOps.duration(fast)
        #expect(d > 0.8 && d < 1.2)
        #expect(fast.lastPathComponent == "Clip 2x.mp4")
    }
}

@Suite struct RenameTests {
    @Test func numberedPlanApplyAndUndo() throws {
        let s = try Sandbox()
        let files = try (1...12).map { i -> URL in
            let u = s.url("IMG_\(1000 + i).jpg"); try Data([1]).write(to: u); return u
        }
        let plan = Renamer.plan(files, mode: .numbered(base: "Heart Vase", start: 1))
        #expect(plan.first?.to.lastPathComponent == "Heart Vase 01.jpg")
        #expect(plan.last?.to.lastPathComponent == "Heart Vase 12.jpg")
        #expect(plan.allSatisfy { $0.problem == nil })
        let done = try Renamer.apply(plan)
        #expect(s.files().first == "Heart Vase 01.jpg")
        try Renamer.revert(done)
        #expect(s.files().first == "IMG_1001.jpg")
    }

    @Test func swapsAndConflicts() throws {
        let s = try Sandbox()
        let a = s.url("a.txt"), b = s.url("b.txt"), c = s.url("c.txt")
        for (u, t) in [(a, "A"), (b, "B"), (c, "C")] { try t.write(to: u, atomically: true, encoding: .utf8) }
        // Replace that would make two files share a name.
        let clash = Renamer.plan([a, b], mode: .replace(find: "b", with: "a"))
        #expect(clash.contains { $0.problem != nil })
        #expect(throws: DragShiftError.self) { try Renamer.apply(clash) }
        // Upper-case is fine and actually renames.
        try Renamer.apply(Renamer.plan([a, c], mode: .uppercase))
        #expect(s.files() == ["A.txt", "C.txt", "b.txt"])
    }
}

@Suite struct WatchAndFavoriteTests {
    @Test func watchRules() {
        let heic = WatchRule(folder: "/tmp", match: .heic, actionID: "img.to.jpg", actionTitle: "JPG")
        #expect(heic.matches(URL(fileURLWithPath: "/tmp/IMG_1.HEIC")))
        #expect(!heic.matches(URL(fileURLWithPath: "/tmp/IMG_1.jpg")))
        #expect(!heic.matches(URL(fileURLWithPath: "/tmp/.hidden.heic")))
        let shots = WatchRule(folder: "/tmp", match: .screenshots, actionID: "img.to.jpg", actionTitle: "JPG")
        #expect(shots.matches(URL(fileURLWithPath: "/tmp/Screenshot 2026-10-22 at 10.00.00.png")))
        #expect(!shots.matches(URL(fileURLWithPath: "/tmp/photo.png")))
        #expect(shots.wouldLoop(URL(fileURLWithPath: "/tmp/Screenshot.jpg")))
        #expect(!shots.wouldLoop(URL(fileURLWithPath: "/tmp/Screenshot.png")))
    }

    @Test func favoritesFollowTheFile() {
        let favs = ["img.compress", "recipe.shop-ready", "img.to.jpg", "share.airdrop"]
        let img = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.png")], favorites: favs, recipes: Recipe.builtIns)
        #expect(img[0].id == "favorites")
        let imgIDs = img[0].actions.map(\.id)
        #expect(imgIDs.contains("img.compress") && imgIDs.contains("img.to.jpg") && imgIDs.contains("share.airdrop"))
        if VisionOps.canCutOut { #expect(imgIDs.contains("recipe.shop-ready")) }

        let pdf = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.pdf")], favorites: favs, recipes: Recipe.builtIns)
        #expect(pdf[0].actions.map(\.id) == ["pdf.compress", "pdf.to.jpg", "share.airdrop"])

        let none = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.zip")], favorites: ["img.to.jpg"], recipes: [])
        #expect(none[0].id != "favorites")
        #expect(Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.png")]).allSatisfy { $0.actions.count <= 8 })
    }
}
