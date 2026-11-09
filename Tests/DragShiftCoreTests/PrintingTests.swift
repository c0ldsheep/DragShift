import AppKit
import Foundation
import ImageIO
import PDFKit
import Testing
import UniformTypeIdentifiers
@testable import DragShiftCore

private let mm: CGFloat = 72 / 25.4
private let a4 = PrintPreset.Paper.a4.size!

@Suite struct PrintLayoutTests {
    @Test func fitTurnsALandscapePhotoOnPortraitPaper() {
        let p = PrintLayout.place(CGSize(width: 4000, height: 3000), on: a4, fit: .fit)
        #expect(p.rotate)
        let area = CGRect(origin: .zero, size: a4).insetBy(dx: PrintLayout.margin, dy: PrintLayout.margin)
        #expect(area.insetBy(dx: -0.01, dy: -0.01).contains(p.rect))
        #expect(abs(p.rect.height / p.rect.width - 4.0 / 3.0) < 0.001, "keeps the shape, turned")
        #expect(abs(p.rect.width - area.width) < 0.01 || abs(p.rect.height - area.height) < 0.01, "as big as the page allows")
    }

    @Test func fillCoversThePage() {
        let p = PrintLayout.place(CGSize(width: 3000, height: 2000), on: PrintPreset.Paper.photo4x6.size!, fit: .fill)
        #expect(p.rect.width >= 288 - 0.01 && p.rect.height >= 432 - 0.01)
    }

    @Test func actualSizeStaysRealAndCentred() {
        let photo = CGSize(width: 35 * mm, height: 45 * mm)
        let p = PrintLayout.place(photo, on: a4, fit: .actual)
        #expect(!p.rotate)
        #expect(abs(p.rect.width - photo.width) < 0.01 && abs(p.rect.height - photo.height) < 0.01)
        #expect(abs(p.rect.midX - a4.width / 2) < 0.01)
        let huge = PrintLayout.place(CGSize(width: 5000, height: 5000), on: a4, fit: .actual)
        #expect(huge.rect.width <= a4.width, "too big for the paper: shrunk to fit")
    }

    @Test func passportPhotosPerSheet() {
        let photo = PrintLayout.realSize(pixels: CGSize(width: 413, height: 531), dpi: CGSize(width: 300, height: 300))
        #expect(abs(photo.width / mm - 35) < 0.2 && abs(photo.height / mm - 45) < 0.2, "413 × 531 px at 300 dpi is 3.5 × 4.5 cm")
        let six = PrintLayout.grid(photo, on: PrintPreset.Paper.photo4x6.size!)
        #expect(six.rects.count == 8, "eight on a 4×6, got \(six.rects.count)")
        #expect(six.sheet.width > six.sheet.height, "the 4×6 turns sideways for eight")
        let sheet = CGRect(origin: .zero, size: six.sheet)
        for r in six.rects { #expect(sheet.contains(r)) }
        for (i, a) in six.rects.enumerated() { for b in six.rects[(i + 1)...] { #expect(!a.insetBy(dx: 0.5, dy: 0.5).intersects(b)) } }
        #expect(PrintLayout.grid(photo, on: a4).rects.count == 30, "thirty on A4")
        #expect(PrintLayout.grid(CGSize(width: 500, height: 700), on: a4).rects.isEmpty, "a big picture doesn't repeat")
    }

    @Test func presetSteps() throws {
        let p = PrintPreset.builtIns[2]
        #expect(PrintPreset.id(fromStep: p.stepID) == p.id)
        #expect(PrintPreset.id(fromStep: "fit.kb.50") == nil)
        #expect(p.summary(defaultPrinter: "EPSON") == "EPSON (default) · 4 × 6 in photo · Colour · Fill the sheet")
        let json = try? JSONEncoder().encode(PrintPreset.builtIns)
        #expect(try JSONDecoder().decode([PrintPreset].self, from: #require(json)) == PrintPreset.builtIns)
    }
}

@Suite struct PrintDocumentTests {
    private func pages(_ url: URL) -> [CGRect] {
        guard let d = PDFDocument(url: url) else { return [] }
        return (0..<d.pageCount).compactMap { d.page(at: $0)?.bounds(for: .mediaBox) }
    }

    @Test func passportSheetOn4x6() async throws {
        let s = try Sandbox()
        let photo = try s.writeImage(Sandbox.image(413, 531, noise: true), "Me Passport.jpg", type: .jpeg,
                                     props: [kCGImagePropertyDPIWidth: 300, kCGImagePropertyDPIHeight: 300])
        let job = try await PrintDocument.make([photo], preset: PrintPreset.builtIns[2], paper: PrintPreset.Paper.photo4x6.size!, dir: s.dir)
        #expect(job.pages == 1)
        let p = pages(job.pdf)
        #expect(p.count == 1)
        #expect(abs(p[0].width - 432) < 0.5 && abs(p[0].height - 288) < 0.5, "a sideways 4×6 sheet")
    }

    @Test func picturesOnePerPage() async throws {
        let s = try Sandbox()
        let a = try s.writeImage(Sandbox.image(1200, 800, noise: true), "Wide.png", type: .png)
        let b = try s.phonePhoto()
        let job = try await PrintDocument.make([a, b], preset: PrintPreset.builtIns[0], paper: a4, dir: s.dir)
        #expect(job.pages == 2)
        for r in pages(job.pdf) { #expect(abs(r.width - a4.width) < 0.5 && abs(r.height - a4.height) < 0.5) }
    }

    @Test func pdfsAndDocumentsPageByPage() async throws {
        let s = try Sandbox()
        let pdf = try s.pdf("Marks.pdf", pages: ["One", "Two", "Three"])
        let txt = s.url("Notes.txt")
        try "A short note to print.".write(to: txt, atomically: true, encoding: .utf8)
        let n = PDFDocument(url: pdf)?.pageCount ?? 0
        #expect(n >= 3)
        let job = try await PrintDocument.make([pdf, txt], preset: PrintPreset.builtIns[0], paper: a4, dir: s.dir)
        #expect(job.pages == n + 1)
        #expect(job.notes.isEmpty)
    }

    @Test func blackAndWhiteComesOutGrey() async throws {
        let s = try Sandbox()
        let pdf = try s.pdf("Colour.pdf", pages: ["Hello"])
        let png = try s.writeImage(Sandbox.image(600, 400, noise: true), "Colour.png", type: .png)
        let job = try await PrintDocument.make([png, pdf], preset: PrintPreset.builtIns[1], paper: a4, dir: s.dir)
        let doc = try #require(PDFDocument(url: job.pdf))
        for i in 0..<doc.pageCount {
            let thumb = try #require(doc.page(at: i)?.thumbnail(of: CGSize(width: 300, height: 420), for: .mediaBox))
            let rep = try #require(thumb.cgImage(forProposedRect: nil, context: nil, hints: nil).map(NSBitmapImageRep.init(cgImage:)))
            var worst: CGFloat = 0
            for y in stride(from: 0, to: rep.pixelsHigh, by: 7) {
                for x in stride(from: 0, to: rep.pixelsWide, by: 7) {
                    guard let c = rep.colorAt(x: x, y: y)?.usingColorSpace(.sRGB) else { continue }
                    worst = max(worst, abs(c.redComponent - c.greenComponent), abs(c.greenComponent - c.blueComponent))
                }
            }
            #expect(worst < 0.03, "page \(i + 1) has colour: \(worst)")
        }
    }

    @Test func refusesWhatCantPrint() async throws {
        let s = try Sandbox()
        let zip = s.url("Box.zip")
        try Data("PK".utf8).write(to: zip)
        await #expect(throws: PrintDocument.Failure.self) {
            try await PrintDocument.make([zip], preset: PrintPreset.builtIns[0], paper: a4, dir: s.dir)
        }
        let png = try s.writeImage(Sandbox.image(300, 300), "Ok.png", type: .png)
        let job = try await PrintDocument.make([png, zip], preset: PrintPreset.builtIns[0], paper: a4, dir: s.dir)
        #expect(job.pages == 1 && job.notes.count == 1, "prints what it can and says what it skipped")
    }

    @Test func printSitsOnTheFilesPage() {
        let ids = { (name: String) in Catalog.pages(for: [URL(fileURLWithPath: "/tmp/\(name)")]).flatMap(\.actions).map(\.id) }
        for f in ["a.jpg", "a.pdf", "a.docx"] {
            #expect(ids(f).contains("file.print"), "\(f) can be printed")
            #expect(!ids(f).contains("share.messages"))
        }
        #expect(!ids("a.mov").contains("file.print"))
        #expect(ids("a.mov").contains("share.messages"))
        let preset = PrintPreset.builtIns[2]
        let fav = Catalog.favoriteActions([preset.stepID], files: [URL(fileURLWithPath: "/tmp/a.jpg")], recipes: [], printPresets: PrintPreset.builtIns)
        #expect(fav.map(\.title) == ["Passport 4×6"])
        #expect(Catalog.favoriteActions([preset.stepID], files: [URL(fileURLWithPath: "/tmp/a.mov")], recipes: [],
                                        printPresets: PrintPreset.builtIns).isEmpty)
        for f in ["a.jpg", "a.pdf", "a.docx", "a.mov"] {
            for p in Catalog.pages(for: [URL(fileURLWithPath: "/tmp/\(f)")]) { #expect(p.actions.count <= Catalog.maxSlices) }
        }
    }

    @Test func printPageHoldsThePresetsToDropOn() {
        let mine = (1...9).map { PrintPreset(id: "p\($0)", name: "Preset \($0)") }
        let pages = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.pdf")], hidden: ["print.preset.p1"], printPresets: mine)
        let ids = pages.map(\.id)
        let print = try! #require(pages.first { $0.id == "print" })
        #expect(ids.firstIndex(of: "print")! + 1 == ids.firstIndex(of: "share"), "just before Files")
        #expect(print.actions.count == Catalog.maxSlices)
        #expect(print.actions.first?.title == "Preset 2", "hidden presets stay off it")
        #expect(print.actions.last?.id == "file.print", "the last slice opens the print window")
        #expect(!Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.mov")], printPresets: mine).contains { $0.id == "print" })
        #expect(!Catalog.pages(for: [URL(fileURLWithPath: "/tmp/a.jpg")]).contains { $0.id == "print" }, "no presets, no page")
    }

    @Test func macPresetsAreTheirOwnThing() throws {
        let amazon = PrintPreset(id: "mac.EPSON.Amazon", name: "Amazon Packing", printer: "EPSON L8050 Series", system: "Amazon Packing ")
        #expect(!amazon.sameSettings(as: PrintPreset(name: "Plain", printer: "EPSON L8050 Series")))
        #expect(amazon.summary() == "EPSON L8050 Series · Saved in the macOS print window")
        // Presets saved before macOS presets existed still load.
        let old = try JSONEncoder().encode(PrintPreset.builtIns[0])
        var json = try #require(JSONSerialization.jsonObject(with: old) as? [String: Any])
        json["system"] = nil
        let back = try JSONDecoder().decode(PrintPreset.self, from: JSONSerialization.data(withJSONObject: json))
        #expect(back == PrintPreset.builtIns[0])
    }

    @Test func macPresetsPrintPagesAsTheyAre() async throws {
        let s = try Sandbox()
        let label = s.url("Label.pdf")
        var box = CGRect(x: 0, y: 0, width: 432, height: 288)
        let ctx = try #require(CGContext(label as CFURL, mediaBox: &box, nil))
        ctx.beginPDFPage(nil); ctx.fill(CGRect(x: 20, y: 20, width: 100, height: 50)); ctx.endPDFPage(); ctx.closePDF()
        let photo = try s.writeImage(Sandbox.image(600, 300), "Photo.png", type: .png,
                                     props: [kCGImagePropertyDPIWidth: 150, kCGImagePropertyDPIHeight: 150])
        let preset = PrintPreset(name: "Labels", system: "Labels")
        let job = try await PrintDocument.make([label, photo], preset: preset, paper: a4, dir: s.dir)
        let doc = try #require(PDFDocument(url: job.pdf))
        #expect(doc.pageCount == 2)
        #expect(doc.page(at: 0)?.bounds(for: .mediaBox).size == CGSize(width: 432, height: 288), "the label keeps its size")
        #expect(doc.page(at: 1)?.bounds(for: .mediaBox).size == CGSize(width: 288, height: 144), "600 × 300 px at 150 dpi = 4 × 2 in")
    }
}
