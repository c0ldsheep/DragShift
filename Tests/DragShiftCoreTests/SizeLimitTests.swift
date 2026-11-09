import AppKit
import Foundation
import ImageIO
import PDFKit
import Testing
@testable import DragShiftCore

private func pixel(_ img: CGImage, _ x: Int, _ y: Int) -> (Int, Int, Int) {
    var px = [UInt8](repeating: 0, count: 4)
    px.withUnsafeMutableBytes { buf in
        let ctx = CGContext(data: buf.baseAddress, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
        // Top-left origin: move the wanted pixel onto the 1×1 canvas.
        ctx.draw(img, in: CGRect(x: -x, y: -(img.height - 1 - y), width: img.width, height: img.height))
    }
    return (Int(px[0]), Int(px[1]), Int(px[2]))
}

@Suite struct SizeLimitTests {
    @Test func photoFitsJustUnderTheLimit() throws {
        let s = try Sandbox()
        let png = try s.writeImage(Sandbox.image(3000, 2000, noise: true), "Photo.png", type: .png)
        let r = try SizeLimit.fitImage(png, kb: 100, dir: s.dir)
        let out = try #require(r.url)
        #expect(out.lastPathComponent == "Photo Under 100 KB.jpg")
        let size = FileInspector.size(of: out)
        #expect(size <= 100_000)
        #expect(size > 60_000, "should use most of the allowance (sharpest result), got \(size)")
        #expect(ImageOps.pixelSize(out) != nil)
    }

    @Test func tinyLimitsShrinkThePicture() throws {
        let s = try Sandbox()
        let png = try s.writeImage(Sandbox.image(3000, 2000, noise: true), "Big.png", type: .png)
        let out = try #require(try SizeLimit.fitImage(png, kb: 20, dir: s.dir).url)
        #expect(FileInspector.size(of: out) <= 20_000)
        let px = try #require(ImageOps.pixelSize(out))
        #expect(px.width < 3000)
        #expect(abs(px.width / px.height - 1.5) < 0.02, "keeps the shape")
    }

    @Test func smallJPGIsLeftAloneButPNGBecomesJPG() throws {
        let s = try Sandbox()
        let jpg = try s.writeImage(Sandbox.image(200, 150), "Small.jpg", type: .jpeg)
        #expect(try SizeLimit.fitImage(jpg, kb: 100, dir: s.dir).url == nil)
        let png = try s.writeImage(Sandbox.image(200, 150), "Small.png", type: .png)
        let out = try #require(try SizeLimit.fitImage(png, kb: 100, dir: s.dir).url)
        #expect(out.pathExtension == "jpg")
    }

    @Test func dropsLocationAndCameraInfo() throws {
        let s = try Sandbox()
        let photo = try s.phonePhoto()
        let out = try #require(try SizeLimit.fitImage(photo, kb: 5, dir: s.dir).url)
        #expect(!ImageOps.hasPrivateMetadata(out))
        // Stored sideways with an EXIF turn; the result is upright pixels.
        let px = try #require(ImageOps.pixelSize(out))
        #expect(px.height > px.width)
    }

    @Test func pdfFitsAndStillOpens() throws {
        let s = try Sandbox()
        let pages = try (1...3).map { i in
            try s.writeImage(Sandbox.image(1800, 2400, noise: true), "Scan \(i).png", type: .png)
        }
        let pdf = s.url("Marksheet.pdf")
        try ImageOps.makePDF(pages, to: pdf, quality: 0.95)
        #expect(FileInspector.size(of: pdf) > 300_000)

        let r = try SizeLimit.fitPDF(pdf, kb: 200, dir: s.dir)
        let out = try #require(r.result.url)
        #expect(out.lastPathComponent == "Marksheet Under 200 KB.pdf")
        #expect(FileInspector.size(of: out) <= 200_000)
        let doc = try #require(PDFDocument(url: out))
        #expect(doc.pageCount == 3)
        let before = try #require(PDFDocument(url: pdf)?.page(at: 0)?.bounds(for: .mediaBox))
        let after = try #require(doc.page(at: 0)?.bounds(for: .mediaBox))
        #expect(abs(before.width / before.height - after.width / after.height) < 0.01)
    }

    @Test func pdfAlreadyUnderIsLeftAlone() throws {
        let s = try Sandbox()
        let pdf = try s.pdf("Letter.pdf", pages: ["Hello"])
        #expect(try SizeLimit.fitPDF(pdf, kb: 500, dir: s.dir).result.url == nil)
    }

    @Test func pdfWriterMakesValidPages() throws {
        let rgb = try SizeLimit.encodeJPEG(Sandbox.image(300, 400), quality: 0.8, dpi: nil)
        let gray = try SizeLimit.encodeJPEG(try SizeLimit.gray(Sandbox.textImage(["Gray page"], width: 400)), quality: 0.8, dpi: nil)
        let data = PDFWriter.make([
            .init(jpeg: rgb, pixelWidth: 300, pixelHeight: 400, gray: false, size: CGSize(width: 300, height: 400)),
            .init(jpeg: gray, pixelWidth: 400, pixelHeight: 168, gray: true, size: CGSize(width: 200, height: 84)),
        ])
        let doc = try #require(PDFDocument(data: data))
        #expect(doc.pageCount == 2)
        #expect(doc.page(at: 1)?.bounds(for: .mediaBox).size == CGSize(width: 200, height: 84))
        let page = try #require(doc.page(at: 0))
        let img = try #require(PDFOps.render(page, dpi: 72))
        let (r, _, b) = pixel(img, 50, 200)
        #expect(r > 180 && b < 90, "left half is red")
    }

    @Test func scansAreDetectedAsGray() {
        #expect(SizeLimit.isGray(Sandbox.textImage(["Roll number 1234"], width: 600)))
        #expect(!SizeLimit.isGray(Sandbox.image(300, 200)))
    }

    @Test func recipeCanEndWithASizeLimit() async throws {
        let s = try Sandbox()
        let png = try s.writeImage(Sandbox.image(2400, 1600, noise: true), "Form.png", type: .png)
        let recipe = Recipe(name: "Portal", steps: ["img.size.1600", SizeLimit.stepID(kb: 50)])
        #expect(recipe.kinds == [.image])
        #expect(Recipe(name: "Any", steps: [SizeLimit.stepID(kb: 200)]).kinds == [.image, .pdf])
        let r = await Engine.runRecipe(recipe, inputs: [png], options: Options())
        #expect(r.failures.isEmpty, "\(r.failures)")
        let out = try #require(r.outputs.first)
        #expect(out.lastPathComponent == "Form Portal.jpg")
        #expect(FileInspector.size(of: out) <= 50_000)
        #expect(RecipeSteps.title("fit.kb.75") == "Under 75 KB")
        #expect(RecipeSteps.title("fit.kb.1000") == "Under 1 MB")
    }

    @Test func ringShowsTheNewSlices() {
        let one = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.jpg")]).flatMap(\.actions).map(\.id)
        #expect(one.contains("img.passport") && one.contains("fit.kb") && one.contains("img.annotate"))
        let many = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.jpg"), URL(fileURLWithPath: "/tmp/b.jpg")])
            .flatMap(\.actions).map(\.id)
        #expect(many.contains("fit.kb") && many.contains("img.gif"))
        #expect(!many.contains("img.annotate") && !many.contains("img.redact"), "one-picture editors only show for one")
        let pdf = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.pdf")]).flatMap(\.actions).map(\.id)
        #expect(pdf.contains("fit.kb"))
        for p in Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.jpg")]) { #expect(p.actions.count <= Catalog.maxSlices) }

        let faves = Catalog.favoriteActions(["fit.kb.50"], files: [URL(fileURLWithPath: "/tmp/a.pdf")], recipes: [])
        #expect(faves.map(\.title) == ["Under 50 KB"])
        #expect(Catalog.favoriteActions(["fit.kb.50"], files: [URL(fileURLWithPath: "/tmp/a.mov")], recipes: []).isEmpty)
    }

    @Test func engineRunsSizeStepsFromTheRing() async throws {
        let s = try Sandbox()
        let png = try s.writeImage(Sandbox.image(1200, 900, noise: true), "Shot.png", type: .png)
        let r = await Engine.run(SizeLimit.action(kb: 50), inputs: [png], options: Options())
        #expect(r.failures.isEmpty)
        #expect(r.outputs.count == 1)
        #expect(r.summary.contains("KB"))
        let again = await Engine.run(SizeLimit.action(kb: 50), inputs: r.outputs, options: Options())
        #expect(again.outputs.isEmpty)
        #expect(again.notes.first?.contains("already under 50 KB") == true)
    }
}

@Suite struct PassportTests {
    /// Grey picture with a red "face" box where Vision would put it.
    static func portrait() -> (CGImage, CGRect) {
        let w = 1200, h = 1600
        let face = CGRect(x: 450, y: 420, width: 300, height: 360) // top-left origin
        let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
        ctx.setFillColor(CGColor(srgbRed: 0.5, green: 0.5, blue: 0.5, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        ctx.setFillColor(CGColor(srgbRed: 0.9, green: 0.1, blue: 0.1, alpha: 1))
        ctx.fill(CGRect(x: face.minX, y: Double(h) - face.maxY, width: face.width, height: face.height))
        return (ctx.makeImage()!, face)
    }

    @Test func framesTheFaceThePassportWay() throws {
        let (img, face) = Self.portrait()
        var spec = PhotoSpec()
        spec.background = .keep
        let out = try PassportPhoto.make(img, spec: spec, face: face).image
        #expect(out.width == 413 && out.height == 531, "3.5 × 4.5 cm at 300 dpi")

        // Head = face box + crown above it = 72% of the height, with 8% above the head.
        let frame = PassportPhoto.frame(face: face, spec: spec)
        let scale = 531 / frame.height
        let faceTop = (face.minY - frame.minY) * scale
        let k = PassportPhoto.crownAbove
        #expect(abs(faceTop - (0.08 + 0.72 * k / (1 + k)) * 531) < 2)
        let (r, g, _) = pixel(out, 206, Int(faceTop + face.height * scale / 2))
        #expect(r > 180 && g < 80, "the face is in the middle")
        let (r2, _, _) = pixel(out, 5, 5)
        #expect(r2 > 100 && r2 < 160, "corner is the grey background")
    }

    @Test func faceNearTheEdgeIsPaddedWithTheBackground() throws {
        let (img, _) = Self.portrait()
        let face = CGRect(x: 0, y: 40, width: 300, height: 360) // at the left edge, close to the top
        var spec = PhotoSpec()
        spec.background = .keep
        let out = try PassportPhoto.make(img, spec: spec, face: face).image
        let (r, g, b) = pixel(out, 3, 3)
        #expect(r > 250 && g > 250 && b > 250, "outside the original = white")
    }

    @Test func savesUnder50KBAt300DPI() throws {
        let s = try Sandbox()
        let (img, face) = Self.portrait()
        let url = try s.writeImage(img, "Me.jpg", type: .jpeg)
        var spec = PhotoSpec()
        spec.background = .keep
        let r = try PassportPhoto.save(url, spec: spec, dir: s.dir, face: face)
        #expect(r.url.lastPathComponent == "Me Passport.jpg")
        #expect(FileInspector.size(of: r.url) <= 50_000)
        let p = ImageOps.properties(r.url)
        #expect((p[kCGImagePropertyDPIWidth] as? Double) == 300)
        #expect(ImageOps.pixelSize(r.url) == CGSize(width: 413, height: 531))
    }

    @Test func usVisaIsSquareWithSmallerHead() throws {
        let (img, face) = Self.portrait()
        var spec = PhotoSpec.presets[1].1
        spec.background = .keep
        let out = try PassportPhoto.make(img, spec: spec, face: face).image
        #expect(out.width == 600 && out.height == 600, "2 × 2 in at 300 dpi")
        #expect(spec.sizeTitle == "2 × 2 in")
        #expect(PhotoSpec().sizeTitle == "3.5 × 4.5 cm")
    }

    /// Opt-in check on a real portrait: DRAGSHIFT_FACE=photo.jpg DRAGSHIFT_OUT=folder
    @Test func realPortrait() throws {
        guard let path = ProcessInfo.processInfo.environment["DRAGSHIFT_FACE"],
              let outDir = ProcessInfo.processInfo.environment["DRAGSHIFT_OUT"] else { return }
        let r = try PassportPhoto.save(URL(fileURLWithPath: path), spec: PhotoSpec(), dir: URL(fileURLWithPath: outDir))
        #expect(FileInspector.size(of: r.url) <= 50_000)
        if let n = r.note { print("note: \(n)") }
    }
}

@Suite struct HabitTests {
    @Test func threePicksInARowMakeAHabit() {
        var h = Habits()
        h.record("img.to.jpg", kind: .image)
        h.record("img.to.jpg", kind: .image)
        #expect(h.usual(for: .image) == nil, "two picks are not a habit yet")
        h.record("img.to.jpg", kind: .image)
        #expect(h.usual(for: .image) == "img.to.jpg")
        #expect(h.usual(for: .pdf) == nil, "each kind of file has its own habit")
    }

    @Test func aNewHabitTakesOver() {
        var h = Habits()
        for _ in 0..<30 { h.record("pdf.to.jpg", kind: .pdf) }
        var picks = 0
        while h.usual(for: .pdf) != "fit.kb" && picks < 20 {
            h.record("fit.kb.100", kind: .pdf)
            picks += 1
        }
        #expect(h.usual(for: .pdf) == "fit.kb", "sizes count for the Under KB slice")
        #expect(picks <= 8, "an old habit gives way within 8 new picks, took \(picks)")
    }

    @Test func survivesSaving() throws {
        var h = Habits()
        for _ in 0..<3 { h.record("img.size.1600", kind: .image) }
        let back = try JSONDecoder().decode(Habits.self, from: JSONEncoder().encode(h))
        #expect(back.usual(for: .image) == "img.resize")
    }
}
