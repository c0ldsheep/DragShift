import AppKit
import AVFoundation
import Foundation
import ImageIO
import PDFKit
import Testing
@testable import DragShiftCore

private func act(_ id: String, for urls: [URL]) -> Action? {
    Catalog.pages(for: urls).flatMap(\.actions).first { $0.id == id }
}

private func run(_ id: String, _ urls: [URL], _ o: Options = Options()) async throws -> RunReport {
    let a = try #require(act(id, for: urls), "no slice \(id) for \(urls.map(\.lastPathComponent))")
    return await Engine.run(a, inputs: urls, options: o)
}

private func pixel(_ img: CGImage, _ x: Int, _ y: Int) -> (Int, Int, Int, Int) {
    let ctx = CGContext(data: nil, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    // CG origin is bottom-left; callers pass top-left coordinates.
    ctx.draw(img, in: CGRect(x: -x, y: -(img.height - 1 - y), width: img.width, height: img.height))
    let p = ctx.data!.bindMemory(to: UInt8.self, capacity: 4)
    return (Int(p[0]), Int(p[1]), Int(p[2]), Int(p[3]))
}

// MARK: - Naming

@Suite struct NamingTests {
    @Test func neverOverwrites() throws {
        let s = try Sandbox()
        let a = s.url("Page 001.pdf")
        try Data().write(to: s.url("Page 001.jpg"))
        let out = Naming.output(for: a, ext: "jpg", in: s.dir)
        #expect(out.lastPathComponent == "Page 001 2.jpg")
        let cropped = Naming.output(for: a, ext: "jpg", suffix: "Cropped", in: s.dir)
        #expect(cropped.lastPathComponent == "Page 001 Cropped.jpg")
    }

    @Test func readOnlyFolderFallsBackToDownloads() {
        let out = Naming.folder(for: URL(fileURLWithPath: "/System/Library/foo.png"), override: nil)
        #expect(out == Naming.downloads)
    }
}

// MARK: - Catalog

@Suite struct CatalogTests {
    @Test func imageRingHidesItsOwnFormat() {
        let pages = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/x.jpg")])
        #expect(pages.map(\.id) == ["convert", "edit", "more", "share"])
        let convert = pages[0].actions.map(\.title)
        #expect(!convert.contains("JPG"))
        #expect(convert.contains("PNG") && convert.contains("PDF") && convert.contains("TEXT"))
        #expect(pages.allSatisfy { $0.actions.count <= Catalog.maxSlices })
    }

    @Test func pdfAndVideoRings() {
        let pdf = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/x.pdf")])
        #expect(pdf[0].actions.map(\.title).starts(with: ["JPG", "PNG", "TEXT", "WORD"]))
        #expect(!pdf[1].actions.contains { $0.id == "pdf.merge" })
        let two = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/x.pdf"), URL(fileURLWithPath: "/tmp/y.pdf")])
        #expect(two[1].actions.contains { $0.id == "pdf.merge" })
        let mov = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/x.mov")])
        #expect(!mov[0].actions.contains { $0.title == "MOV" })
        #expect(mov[1].actions.contains { $0.id == "media.trim" })
    }

    @Test func hiddenSlicesStayHidden() {
        let pages = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/x.png")], hidden: ["img.to.avif", "share.mail"])
        #expect(!pages.flatMap(\.actions).contains { $0.id == "img.to.avif" || $0.id == "share.mail" })
    }

    @Test func everyActionIsUnique() {
        let ids = Catalog.everyAction.map(\.id)
        #expect(Set(ids).count == ids.count)
        #expect(ids.count > 40)
    }
}

// MARK: - Images

@Suite struct ImageTests {
    @Test func convertKeepsPhotoUprightAndCameraInfo() async throws {
        let s = try Sandbox()
        let photo = try s.phonePhoto()
        let r = try await run("img.to.png", [photo])
        #expect(r.failures.isEmpty)
        let out = try #require(r.outputs.first)
        #expect(out.lastPathComponent == "Photo.png")
        let img = try ImageOps.load(out)
        #expect(img.width == 200 && img.height == 400) // rotated by EXIF 6
        let props = ImageOps.properties(out)
        #expect((props[kCGImagePropertyOrientation] as? Int ?? 1) == 1)
    }

    @Test func everyFormatOnTheRingWorks() async throws {
        let s = try Sandbox()
        let photo = try s.phonePhoto()
        for f in ImageFormat.allCases where f.available {
            let out = try ImageOps.convert(photo, to: f, quality: 0.8, dir: s.dir)
            let back = try ImageOps.load(out)
            #expect(back.width == 200 && back.height == 400, "\(f)")
        }
    }

    @Test func transparentPNGToJPGGoesOnWhite() async throws {
        let s = try Sandbox()
        let png = try s.writeImage(Sandbox.image(100, 100, transparentBorder: 20), "Logo.png", type: .png)
        let r = try await run("img.to.jpg", [png])
        let img = try ImageOps.load(try #require(r.outputs.first))
        let corner = pixel(img, 2, 2)
        #expect(corner.0 > 240 && corner.1 > 240 && corner.2 > 240)
    }

    @Test func compressShrinksAndReportsSavings() async throws {
        let s = try Sandbox()
        let big = try s.writeImage(Sandbox.image(2400, 1600, noise: true), "Big.png", type: .png)
        var o = Options()
        o.compressMaxEdge = 1600
        let r = try await run("img.compress", [big], o)
        let out = try #require(r.outputs.first)
        #expect(out.pathExtension == "jpg")
        #expect(r.bytesAfter < r.bytesBefore / 3)
        #expect(r.summary.contains("−"))
        #expect(max(try ImageOps.load(out).width, try ImageOps.load(out).height) == 1600)
    }

    @Test func compressNeverMakesBiggerFiles() async throws {
        let s = try Sandbox()
        let tiny = try s.writeImage(Sandbox.image(40, 40), "Tiny.jpg", type: .jpeg,
                                    props: [kCGImageDestinationLossyCompressionQuality: 0.3])
        let r = try await run("img.compress", [tiny])
        #expect(r.outputs.isEmpty)
        #expect(r.summary.contains("already"))
    }

    @Test func cleanRemovesLocationAndCameraButKeepsOrientation() async throws {
        let s = try Sandbox()
        let photo = try s.phonePhoto()
        #expect(ImageOps.hasPrivateMetadata(photo))
        let r = try await run("img.clean", [photo])
        let out = try #require(r.outputs.first)
        #expect(out.lastPathComponent == "Photo Clean.jpg")
        #expect(!ImageOps.hasPrivateMetadata(out))
        let img = try ImageOps.load(out)
        #expect(img.width == 200 && img.height == 400)
    }

    @Test func cropRotateFlipResize() throws {
        let s = try Sandbox()
        let png = try s.writeImage(Sandbox.image(300, 100), "Wide.png", type: .png)
        let c = try ImageOps.crop(png, rect: CGRect(x: 0, y: 0, width: 150, height: 100), quality: 0.9, dir: s.dir)
        let ci = try ImageOps.load(c)
        #expect(ci.width == 150 && ci.height == 100)
        #expect(pixel(ci, 140, 50).0 > 200) // left half is red
        let r = try ImageOps.load(try ImageOps.rotate(png, quarterTurns: 1, quality: 0.9, dir: s.dir))
        #expect(r.width == 100 && r.height == 300)
        #expect(pixel(r, 50, 10).0 > 200) // red was on the left, now on top
        let f = try ImageOps.load(try ImageOps.flip(png, quality: 0.9, dir: s.dir))
        #expect(pixel(f, 10, 50).2 > 200) // blue now on the left
        let small = try ImageOps.load(try ImageOps.resize(png, longEdge: 150, scale: nil, quality: 0.9, dir: s.dir))
        #expect(small.width == 150 && small.height == 50)
        #expect(s.files().contains("Wide 150x50.png"))
    }

    @Test func enhanceAndBackdrop() throws {
        let s = try Sandbox()
        let png = try s.writeImage(Sandbox.image(400, 300), "Shot.png", type: .png)
        _ = try ImageOps.enhance(png, quality: 0.9, dir: s.dir)
        #expect(s.files().contains("Shot Enhanced.png"))
        var style = BackdropStyle()
        style.ratio = .square
        let out = try #require(Backdrop.render(try ImageOps.load(png), style: style))
        #expect(out.width == out.height)
        #expect(out.width >= 400 + 2 * 32)
        style.fill = .blurredSelf
        style.ratio = .wide
        let wide = try #require(Backdrop.render(try ImageOps.load(png), style: style))
        #expect(abs(Double(wide.width) / Double(wide.height) - 16.0 / 9) < 0.01)
    }

    @Test func imagesToOnePDFAndGIF() async throws {
        let s = try Sandbox()
        let a = try s.writeImage(Sandbox.image(200, 100), "Scan 1.png", type: .png)
        let b = try s.writeImage(Sandbox.image(200, 100), "Scan 2.png", type: .png)
        let c = try s.writeImage(Sandbox.image(200, 100), "Scan 10.png", type: .png)
        let r = try await run("img.to.pdf", [c, b, a])
        let merged = try #require(r.outputs.first)
        let pdf = try #require(PDFDocument(url: merged))
        #expect(pdf.pageCount == 3)
        #expect(r.outputs[0].lastPathComponent == "Scan 1 (3 pages).pdf")
        let g = try await run("img.gif", [a, b])
        let gif = try #require(g.outputs.first)
        #expect(CGImageSourceGetCount(CGImageSourceCreateWithURL(gif as CFURL, nil)!) == 2)
    }

    @Test func webpUsesCwebpWhenInstalled() throws {
        guard Tools.cwebp != nil else { return }
        let s = try Sandbox()
        let png = try s.writeImage(Sandbox.image(120, 80, transparentBorder: 10), "Icon.png", type: .png)
        let out = try ImageOps.convert(png, to: .webp, quality: 0.8, dir: s.dir)
        let back = try ImageOps.load(out)
        #expect(back.width == 120 && back.height == 80)
        #expect(ImageOps.hasTransparency(back))
    }
}

// MARK: - Vision

@Suite struct VisionTests {
    @Test func readsTextFromAnImage() async throws {
        let s = try Sandbox()
        let img = try s.writeImage(Sandbox.textImage(["Hello from DragShift", "Order number 4821"]), "Note.png", type: .png)
        let r = try await run("img.to.text", [img])
        let text = try #require(r.text)
        #expect(text.contains("DragShift"))
        #expect(text.contains("4821"))
        #expect(s.files().contains("Note.txt"))
    }

    @Test func findsPrivateDetails() throws {
        let img = Sandbox.textImage(["Mail me at test.user@example.com", "Call 98765 43210 anytime", "PAN ABCDE1234F"])
        let found = try VisionOps.findPrivate(img, faces: false)
        let labels = Set(found.map(\.label))
        #expect(labels.contains("Email"))
        #expect(labels.contains("Phone"))
        #expect(labels.contains("PAN"))
        let red = try #require(VisionOps.redact(img, rects: found.map(\.rect), style: .box))
        #expect(try VisionOps.findPrivate(red, faces: false).isEmpty)
    }

    @Test func pixelateAndBlurKeepSize() throws {
        let img = Sandbox.image(300, 200, noise: true)
        for style in VisionOps.RedactStyle.allCases {
            let out = try #require(VisionOps.redact(img, rects: [CGRect(x: 10, y: 10, width: 100, height: 60)], style: style))
            #expect(out.width == 300 && out.height == 200)
        }
    }
}

// MARK: - PDF

@Suite struct PDFTests {
    @Test func pdfToTextWordAndImages() async throws {
        let s = try Sandbox()
        let pdf = try s.pdf("Report.pdf", pages: ["First page says hello", "Second page says goodbye"])
        #expect(PDFDocument(url: pdf)!.pageCount == 2)

        let t = try await run("pdf.to.txt", [pdf])
        #expect(try String(contentsOf: try #require(t.outputs.first), encoding: .utf8).contains("goodbye"))

        let w = try await run("pdf.to.docx", [pdf])
        let docx = try #require(w.outputs.first)
        #expect(docx.lastPathComponent == "Report.docx")
        let back = try NSAttributedString(url: docx, options: [:], documentAttributes: nil)
        #expect(back.string.contains("hello"))

        var o = Options()
        o.pdfDPI = 72
        let j = try await run("pdf.to.jpg", [pdf], o)
        let folder = try #require(j.outputs.first)
        let pages = try FileManager.default.contentsOfDirectory(atPath: folder.path).sorted()
        #expect(pages == ["Report 1.jpg", "Report 2.jpg"])
        let page = try ImageOps.load(folder.appendingPathComponent("Report 1.jpg"))
        #expect(page.width == 595 && page.height == 842)
    }

    @Test func rotatedPagesRenderUpright() throws {
        let s = try Sandbox()
        let pdf = try s.pdf("Turned.pdf", pages: ["Sideways"])
        let doc = PDFDocument(url: pdf)!
        doc.page(at: 0)!.rotation = 90
        doc.write(to: pdf)
        let img = try #require(try PDFOps.render(PDFDocument(url: pdf)!.page(at: 0)!, dpi: 72))
        #expect(img.width == 842 && img.height == 595)
    }

    @Test func splitMergeRotateLockUnlock() async throws {
        let s = try Sandbox()
        let a = try s.pdf("A.pdf", pages: ["one", "two"])
        let b = try s.pdf("B.pdf", pages: ["three"])
        let img = try s.writeImage(Sandbox.image(200, 100), "C.png", type: .png)

        let split = try PDFOps.split(a, dir: s.dir)
        #expect(try FileManager.default.contentsOfDirectory(atPath: split.path).sorted() == ["A 1.pdf", "A 2.pdf"])

        let m = try await run("pdf.merge", [b, a])
        #expect(PDFDocument(url: try #require(m.outputs.first))!.pageCount == 3)
        #expect(m.outputs[0].lastPathComponent == "A Merged.pdf")

        let mixed = try await run("mixed.merge", [a, img])
        #expect(PDFDocument(url: try #require(mixed.outputs.first))!.pageCount == 3)

        let rot = try await run("pdf.rotate", [b])
        #expect(PDFDocument(url: try #require(rot.outputs.first))!.page(at: 0)!.rotation == 90)

        let locked = try PDFOps.lock(a, password: "1234", dir: s.dir)
        #expect(PDFDocument(url: locked)!.isLocked)
        let blocked = try await run("pdf.to.txt", [locked])
        #expect(blocked.outputs.isEmpty && blocked.failures.first?.contains("password") == true)
        #expect(throws: DragShiftError.self) { try PDFOps.unlock(locked, password: "nope", dir: s.dir) }
        let open = try PDFOps.unlock(locked, password: "1234", dir: s.dir)
        let doc = PDFDocument(url: open)!
        #expect(!doc.isLocked && !doc.isEncrypted && doc.pageCount == 2)
    }

    @Test func scannedPDFIsReadWithOCR() async throws {
        let s = try Sandbox()
        let scan = try s.writeImage(Sandbox.textImage(["Invoice total 5600 rupees"]), "Scan.png", type: .png)
        let pdf = s.url("Scan.pdf")
        try ImageOps.makePDF([scan], to: pdf, quality: 0.9)
        let r = try await run("pdf.to.txt", [pdf])
        #expect(r.text?.contains("5600") == true)
    }

    @Test func compressPDF() async throws {
        let s = try Sandbox()
        let big = try s.writeImage(Sandbox.image(2400, 1600, noise: true), "Photo.png", type: .png)
        let pdf = s.url("Photos.pdf")
        try ImageOps.makePDF([big], to: pdf, quality: 1.0)
        let r = try await run("pdf.compress", [pdf])
        #expect(r.failures.isEmpty)
        #expect(r.bytesAfter <= r.bytesBefore)
    }
}

// MARK: - Documents

@Suite struct DocTests {
    @Test @MainActor func markdownToWordPdfHtml() async throws {
        let s = try Sandbox()
        let md = s.url("Notes.md")
        try "# Title\n\nSome **bold** and *italic* text.\n\n- one\n- two\n".write(to: md, atomically: true, encoding: .utf8)
        for f in [DocFormat.docx, .pdf, .html, .rtf, .odt, .txt] {
            let out = try DocOps.convert(md, to: f, dir: s.dir)
            #expect(FileInspector.size(of: out) > 0, "\(f)")
        }
        let back = try NSAttributedString(url: s.url("Notes.docx"), options: [:], documentAttributes: nil)
        #expect(back.string.contains("Title") && back.string.contains("bold") && !back.string.contains("**"))
        #expect(PDFDocument(url: s.url("Notes.pdf"))!.string!.contains("italic"))
        let roundTrip = try DocOps.convert(s.url("Notes.docx"), to: .md, dir: s.dir)
        let text = try String(contentsOf: roundTrip, encoding: .utf8)
        #expect(text.contains("**bold**"))
    }

    @Test func markdownHTML() {
        let html = Markdown.html("## Hi\n\n1. a\n2. b\n\n`x < y` [link](https://a.b)")
        #expect(html.contains("<h2>Hi</h2>"))
        #expect(html.contains("<ol>") && html.contains("<li>b</li>"))
        #expect(html.contains("<code>x &lt; y</code>"))
        #expect(html.contains("<a href=\"https://a.b\">link</a>"))
    }

    @Test func longTextMakesManyPages() throws {
        let s = try Sandbox()
        let long = String(repeating: "Line of text that goes on. ", count: 3000)
        try DocOps.write(DocOps.plain(long), as: .pdf, to: s.url("Long.pdf"))
        #expect(PDFDocument(url: s.url("Long.pdf"))!.pageCount > 5)
    }
}

// MARK: - Media

@Suite(.serialized) struct MediaTests {
    @Test func videoConversions() async throws {
        let s = try Sandbox()
        let mov = try await s.video()
        #expect(await MediaOps.hasVideo(mov))

        let mp4 = try await run("media.to.mp4", [mov])
        #expect(await MediaOps.duration(try #require(mp4.outputs.first)) > 1.5)

        let gif = try await run("media.to.gif", [mov])
        let frames = CGImageSourceGetCount(CGImageSourceCreateWithURL(try #require(gif.outputs.first) as CFURL, nil)!)
        #expect(frames >= 20)

        let m4a = try await run("media.to.m4a", [mov])
        #expect(!(await MediaOps.hasVideo(try #require(m4a.outputs.first))))

        let wav = try await run("media.to.wav", [mov])
        #expect(await MediaOps.duration(try #require(wav.outputs.first)) > 1.5)

        let still = try await run("media.still", [mov])
        #expect(try ImageOps.load(try #require(still.outputs.first)).width == 320)

        let mute = try await run("media.mute", [mov])
        let muted = AVURLAsset(url: try #require(mute.outputs.first))
        #expect(try await muted.loadTracks(withMediaType: .audio).isEmpty)
        #expect(try await !muted.loadTracks(withMediaType: .video).isEmpty)

        let trimmed = try await MediaOps.trim(mov, range: CMTimeRange(start: .zero, duration: CMTime(seconds: 1, preferredTimescale: 600)), dir: s.dir)
        #expect(await MediaOps.duration(trimmed) < 1.3)
    }

    @Test func audioConversions() async throws {
        let s = try Sandbox()
        let wav = try s.wav()
        for id in ["media.to.m4a", "media.to.aiff", "media.to.caf", "media.to.mp3", "media.to.flac"] {
            guard let a = act(id, for: [wav]) else { continue } // mp3/flac only with ffmpeg
            let r = await Engine.run(a, inputs: [wav], options: Options())
            #expect(r.failures.isEmpty, "\(id): \(r.failures)")
            #expect(await MediaOps.duration(try #require(r.outputs.first)) > 0.8, "\(id)")
        }
    }
}

// MARK: - Files

@Suite struct FileTests {
    @Test func zipAndUnzip() async throws {
        let s = try Sandbox()
        let a = s.url("a.txt"), b = s.url("b.txt")
        try "A".write(to: a, atomically: true, encoding: .utf8)
        try "B".write(to: b, atomically: true, encoding: .utf8)
        let one = try await run("file.zip", [a])
        #expect(one.outputs.first?.lastPathComponent == "a.txt.zip")
        let many = try await run("file.zip", [a, b])
        let zip = try #require(many.outputs.first)
        #expect(zip.lastPathComponent == "Archive.zip")
        let un = try await run("file.unzip", [zip])
        let folder = try #require(un.outputs.first)
        // Same layout as Finder's Compress: files at the root, extracted into "Archive".
        let inner = try FileManager.default.contentsOfDirectory(atPath: folder.path).sorted()
        #expect(folder.lastPathComponent == "Archive")
        #expect(inner == ["a.txt", "b.txt"])
        #expect(try String(contentsOf: folder.appendingPathComponent("b.txt"), encoding: .utf8) == "B")
    }

    @Test func folderActions() async throws {
        let s = try Sandbox()
        let folder = s.url("Shoot")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try s.writeImage(Sandbox.image(300, 200, noise: true), "Shoot/1.png", type: .png)
        try s.writeImage(Sandbox.image(300, 200, noise: true), "Shoot/2.png", type: .png)
        try "x".write(to: folder.appendingPathComponent("notes.txt"), atomically: true, encoding: .utf8)
        #expect(FileInspector.kind(of: folder) == .folder)

        let jpg = try await run("folder.jpg", [folder])
        let out = try #require(jpg.outputs.first)
        #expect(out.lastPathComponent == "Shoot (JPG)")
        #expect(try FileManager.default.contentsOfDirectory(atPath: out.path).sorted() == ["1.jpg", "2.jpg"])

        let pdf = try await run("folder.pdf", [folder])
        #expect(pdf.outputs.first?.lastPathComponent == "Shoot (2 pages).pdf")
        #expect(PDFDocument(url: pdf.outputs[0])!.pageCount == 2)

        let gif = try await run("folder.gif", [folder])
        #expect(gif.outputs.first?.lastPathComponent == "Shoot.gif")
    }

    @Test func failuresDontStopTheBatch() async throws {
        let s = try Sandbox()
        let good = try s.writeImage(Sandbox.image(50, 50), "good.png", type: .png)
        let bad = s.url("bad.png")
        try Data("not an image".utf8).write(to: bad)
        let r = try await run("img.to.jpg", [bad, good])
        #expect(r.outputs.count == 1)
        #expect(r.failures.count == 1 && r.failures[0].hasPrefix("bad.png"))
    }
}

// MARK: - Real photo (opt-in: DRAGSHIFT_PHOTO=/path/to/photo.jpg DRAGSHIFT_OUT=/some/folder)

@Suite struct RealPhotoTests {
    @Test func cutOutAndShopPhoto() throws {
        guard let path = ProcessInfo.processInfo.environment["DRAGSHIFT_PHOTO"],
              let outDir = ProcessInfo.processInfo.environment["DRAGSHIFT_OUT"] else { return }
        guard #available(macOS 14, *) else { return }
        let url = URL(fileURLWithPath: path), dir = URL(fileURLWithPath: outDir)
        let img = try ImageOps.load(url)
        let cut = try VisionOps.removeBackground(img)
        #expect(cut.width == img.width && cut.height == img.height)
        #expect(ImageOps.hasTransparency(cut))
        _ = try ImageOps.saveEdited(cut, like: url, suffix: "Cut Out", dir: dir, quality: 0.9, forceFormat: .png)
        let shop = try VisionOps.shopPhoto(img)
        #expect(shop.width == 2000 && shop.height == 2000)
        _ = try ImageOps.saveEdited(shop, like: url, suffix: "Shop", dir: dir, quality: 0.92, keepMetadata: false, forceFormat: .jpg)
    }
}
