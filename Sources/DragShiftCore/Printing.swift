import CoreGraphics
import Foundation
import ImageIO
import PDFKit

/// Saved print preferences: one tap prints with the printer, paper and options you chose before.
public struct PrintPreset: Codable, Hashable, Identifiable, Sendable {
    public enum Paper: String, Codable, CaseIterable, Sendable {
        case printerDefault, a4, letter, legal, a5, a3, photo4x6, photo5x7

        public var title: String {
            switch self {
            case .printerDefault: return "Printer's paper"
            case .a4: return "A4"
            case .letter: return "Letter"
            case .legal: return "Legal"
            case .a5: return "A5"
            case .a3: return "A3"
            case .photo4x6: return "4 × 6 in photo"
            case .photo5x7: return "5 × 7 in photo"
            }
        }

        /// Portrait size in points (1/72 inch); nil means "whatever the printer is set to".
        public var size: CGSize? {
            let mm = 72 / 25.4
            switch self {
            case .printerDefault: return nil
            case .a4: return CGSize(width: 210 * mm, height: 297 * mm)
            case .letter: return CGSize(width: 612, height: 792)
            case .legal: return CGSize(width: 612, height: 1008)
            case .a5: return CGSize(width: 148 * mm, height: 210 * mm)
            case .a3: return CGSize(width: 297 * mm, height: 420 * mm)
            case .photo4x6: return CGSize(width: 288, height: 432)
            case .photo5x7: return CGSize(width: 360, height: 504)
            }
        }
    }

    /// How a picture sits on the page: whole picture, page filled (edges cropped), or its real printed size.
    public enum Fit: String, Codable, CaseIterable, Sendable {
        case fit, fill, actual
        public var title: String {
            switch self {
            case .fit: return "Fit"
            case .fill: return "Fill"
            case .actual: return "Actual size"
            }
        }
    }

    public var id: String
    public var name: String
    /// nil: the Mac's default printer.
    public var printer: String?
    public var paper: Paper
    public var copies: Int
    public var color: Bool
    public var twoSided: Bool
    public var fit: Fit
    /// Repeat each picture at its real size across the sheet, ready to cut (passport photos).
    public var fillSheet: Bool
    /// A preset saved in the macOS print window (Presets → Save Current Settings as Preset…), by its name there. It
    /// prints with exactly what macOS saved (pages per sheet, quality, paper type…); the options above don't apply.
    public var system: String?

    public init(id: String = UUID().uuidString, name: String, printer: String? = nil, paper: Paper = .printerDefault,
                copies: Int = 1, color: Bool = true, twoSided: Bool = false, fit: Fit = .fit, fillSheet: Bool = false,
                system: String? = nil) {
        self.id = id; self.name = name; self.printer = printer; self.paper = paper; self.copies = copies
        self.color = color; self.twoSided = twoSided; self.fit = fit; self.fillSheet = fillSheet; self.system = system
    }

    public static let builtIns: [PrintPreset] = [
        PrintPreset(id: "everyday", name: "Everyday"),
        PrintPreset(id: "bw", name: "Black & white", color: false, twoSided: true),
        PrintPreset(id: "passport-4x6", name: "Passport 4×6", paper: .photo4x6, fit: .actual, fillSheet: true),
        PrintPreset(id: "passport-a4", name: "Passport A4", paper: .a4, fit: .actual, fillSheet: true),
    ]

    /// One line for lists: "EPSON L8050 · A4 · 2 copies · Black & white · Two-sided".
    public func summary(defaultPrinter: String? = nil) -> String {
        let printer = printer ?? defaultPrinter.map { "\($0) (default)" } ?? "Default printer"
        if system != nil { return "\(printer) · Saved in the macOS print window" }
        var parts = [printer, paper.title]
        if copies > 1 { parts.append("\(copies) copies") }
        parts.append(color ? "Colour" : "Black & white")
        if twoSided { parts.append("Two-sided") }
        parts.append(fillSheet ? "Fill the sheet" : fit.title)
        return parts.joined(separator: " · ")
    }

    /// Same printer, paper and options (the name and id don't count).
    public func sameSettings(as o: PrintPreset) -> Bool {
        printer == o.printer && paper == o.paper && copies == o.copies && color == o.color && twoSided == o.twoSided
            && fit == o.fit && fillSheet == o.fillSheet && system == o.system
    }

    /// The id of "whatever the print window sent last time", for double-tap repeat.
    public static let lastID = "last"

    // Ring steps: "print.preset.<id>" prints straight away with that preset.
    public static let stepPrefix = "print.preset."
    public var stepID: String { Self.stepPrefix + id }
    public static func id(fromStep step: String) -> String? {
        step.hasPrefix(stepPrefix) ? String(step.dropFirst(stepPrefix.count)) : nil
    }
    public var action: Action { Action(stepID, name, "printer.fill", verb: "Printing") }
}

/// Where things go on the paper. Pure geometry, so it's tested on its own.
public enum PrintLayout {
    /// 6 mm: inside what home printers can reach.
    public static let margin: CGFloat = 6 * 72 / 25.4
    /// 2 mm between repeated photos, room for scissors.
    public static let gap: CGFloat = 2 * 72 / 25.4
    /// 3 mm round a sheet of repeated photos: what photo printers reach, and what eight 3.5 × 4.5 cm photos on a
    /// 4 × 6 need (4 × 35 mm + 3 gaps = 146 of 152.4 mm).
    public static let sheetMargin: CGFloat = 3 * 72 / 25.4

    /// The rectangle a picture of `item` size (points) gets on `page`, and whether it's turned 90° to use the page
    /// better. Fill covers the whole page (the edges are cropped); Actual keeps the real size but shrinks what
    /// doesn't fit.
    public static func place(_ item: CGSize, on page: CGSize, fit: PrintPreset.Fit, margin: CGFloat = margin) -> (rect: CGRect, rotate: Bool) {
        guard item.width > 0, item.height > 0 else { return (.zero, false) }
        let area = fit == .fill ? CGRect(origin: .zero, size: page) : CGRect(origin: .zero, size: page).insetBy(dx: margin, dy: margin)
        let turn = fit != .actual && item.width != item.height && (item.width > item.height) != (area.width > area.height)
        let s = turn ? CGSize(width: item.height, height: item.width) : item
        let scale: CGFloat
        switch fit {
        case .fit: scale = min(area.width / s.width, area.height / s.height)
        case .fill: scale = max(area.width / s.width, area.height / s.height)
        case .actual: scale = min(1, area.width / s.width, area.height / s.height)
        }
        let w = s.width * scale, h = s.height * scale
        return (CGRect(x: area.midX - w / 2, y: area.midY - h / 2, width: w, height: h), turn)
    }

    /// Real-size copies of `item` across a sheet, in whichever orientation fits more. Empty when not even two fit.
    public static func grid(_ item: CGSize, on page: CGSize, margin: CGFloat = sheetMargin, gap: CGFloat = gap) -> (rects: [CGRect], sheet: CGSize) {
        func layout(_ sheet: CGSize) -> [CGRect] {
            let cols = Int((sheet.width - 2 * margin + gap) / (item.width + gap))
            let rows = Int((sheet.height - 2 * margin + gap) / (item.height + gap))
            guard cols > 0, rows > 0 else { return [] }
            let bw = CGFloat(cols) * item.width + CGFloat(cols - 1) * gap
            let bh = CGFloat(rows) * item.height + CGFloat(rows - 1) * gap
            let x0 = (sheet.width - bw) / 2, y0 = (sheet.height - bh) / 2
            var out: [CGRect] = []
            for r in 0..<rows {
                for c in 0..<cols {
                    // PDF pages count y from the bottom: fill from the top row down.
                    out.append(CGRect(x: x0 + CGFloat(c) * (item.width + gap), y: y0 + bh - item.height - CGFloat(r) * (item.height + gap),
                                      width: item.width, height: item.height))
                }
            }
            return out
        }
        let upright = layout(page)
        let turned = layout(CGSize(width: page.height, height: page.width))
        let best = turned.count > upright.count ? (turned, CGSize(width: page.height, height: page.width)) : (upright, page)
        return best.0.count >= 2 ? (best.0, best.1) : ([], page)
    }

    /// A picture's real printed size in points, from its pixels and DPI (72 when the file doesn't say).
    public static func realSize(pixels: CGSize, dpi: CGSize?) -> CGSize {
        let dx = (dpi?.width ?? 0) > 1 ? dpi!.width : 72
        let dy = (dpi?.height ?? 0) > 1 ? dpi!.height : 72
        return CGSize(width: pixels.width * 72 / dx, height: pixels.height * 72 / dy)
    }
}

/// The print-ready PDF: every file laid out on pages of the chosen paper. The printer then prints it 1:1. For a macOS
/// preset every page keeps its own size instead, and macOS fits it to the paper with the preset's settings.
public struct PrintJob: Sendable {
    public let pdf: URL
    public let pages: Int
    /// What the person should know ("Skipped Clip.mov: it can't be printed").
    public let notes: [String]
}

public enum PrintDocument {
    public enum Failure: LocalizedError {
        case nothing
        public var errorDescription: String? { "Nothing here can be printed. Pictures, PDFs and documents can." }
    }

    /// Files DragShift can print.
    public static func canPrint(_ url: URL) -> Bool {
        [.image, .pdf, .document].contains(FileInspector.kind(of: url))
    }

    /// Lays out `urls` for `preset` on paper of `paper` size (portrait, points) and writes the PDF into `dir`.
    /// Documents become PDFs first (AppKit's text system wants the main thread); the rest runs on the work queue.
    public static func make(_ urls: [URL], preset: PrintPreset, paper: CGSize, dir: URL) async throws -> PrintJob {
        var items: [(url: URL, name: String)] = []
        var notes: [String] = []
        var temps: [URL] = []
        defer { temps.forEach { try? FileManager.default.removeItem(at: $0) } }
        for url in urls {
            guard FileInspector.kind(of: url) == .document else { items.append((url, url.lastPathComponent)); continue }
            do {
                let tmp = FileManager.default.temporaryDirectory.appendingPathComponent("dragshift-print-\(UUID().uuidString)")
                try FileManager.default.createDirectory(at: tmp, withIntermediateDirectories: true)
                temps.append(tmp)
                let pdf = try await MainActor.run { try DocOps.convert(url, to: .pdf, dir: tmp) }
                items.append((pdf, url.lastPathComponent))
            } catch {
                notes.append("Skipped \(url.lastPathComponent): \(error.localizedDescription)")
            }
        }
        let ready = items
        let job = try await Engine.offload { try layout(ready, preset: preset, paper: paper, dir: dir) }
        return PrintJob(pdf: job.pdf, pages: job.pages, notes: notes + job.notes)
    }

    private static func layout(_ items: [(url: URL, name: String)], preset: PrintPreset, paper: CGSize, dir: URL) throws -> PrintJob {
        let out = dir.appendingPathComponent("DragShift Print \(UUID().uuidString.prefix(8)).pdf")
        var box = CGRect(origin: .zero, size: paper)
        guard let ctx = CGContext(out as CFURL, mediaBox: &box, nil) else { throw CocoaError(.fileWriteUnknown) }
        var pages = 0
        var notes: [String] = []

        func page(_ size: CGSize, _ draw: (CGContext) -> Void) {
            var mb = CGRect(origin: .zero, size: size)
            let info = [kCGPDFContextMediaBox as String: Data(bytes: &mb, count: MemoryLayout<CGRect>.size)] as CFDictionary
            ctx.beginPDFPage(info)
            ctx.setFillColor(CGColor(gray: 1, alpha: 1))
            ctx.fill(mb)
            draw(ctx)
            ctx.endPDFPage()
            pages += 1
        }

        for (url, name) in items {
            do {
                switch FileInspector.kind(of: url) {
                case .image where preset.system != nil: try asIs(image: url, page: page)
                case .pdf where preset.system != nil: try asIs(pdf: url, page: page)
                case .image: try image(url, preset: preset, paper: paper, page: page)
                case .pdf: try pdf(url, preset: preset, paper: paper, page: page)
                default: notes.append("Skipped \(name): it can't be printed")
                }
            } catch {
                notes.append("Skipped \(name): \(error.localizedDescription)")
            }
        }
        ctx.closePDF()
        guard pages > 0 else {
            try? FileManager.default.removeItem(at: out)
            throw Failure.nothing
        }
        return PrintJob(pdf: out, pages: pages, notes: notes)
    }

    private static func image(_ url: URL, preset: PrintPreset, paper: CGSize,
                              page: (CGSize, (CGContext) -> Void) -> Void) throws {
        var img = try ImageOps.load(url, maxPixel: 4200)
        if ImageOps.hasTransparency(img) { img = ImageOps.flatten(img) }
        if !preset.color { img = try SizeLimit.gray(img) }
        // Real size from the original pixels and DPI; the file may be stored turned (EXIF), so match the shape.
        let props = ImageOps.properties(url)
        let px = ImageOps.pixelSize(url) ?? CGSize(width: img.width, height: img.height)
        let dpi = CGSize(width: (props[kCGImagePropertyDPIWidth] as? Double) ?? 0, height: (props[kCGImagePropertyDPIHeight] as? Double) ?? 0)
        var real = PrintLayout.realSize(pixels: px, dpi: dpi)
        if (img.width > img.height) != (real.width > real.height) { real = CGSize(width: real.height, height: real.width) }

        if preset.fillSheet {
            let g = PrintLayout.grid(real, on: paper)
            if !g.rects.isEmpty {
                page(g.sheet) { ctx in
                    for r in g.rects {
                        ctx.interpolationQuality = .high
                        ctx.draw(img, in: r)
                        // A hairline to cut along
                        ctx.setStrokeColor(CGColor(gray: 0.78, alpha: 1))
                        ctx.setLineWidth(0.3)
                        ctx.stroke(r)
                    }
                }
                return
            }
        }
        let size = preset.fit == .actual ? real : CGSize(width: img.width, height: img.height)
        let p = PrintLayout.place(size, on: paper, fit: preset.fit)
        page(paper) { ctx in
            ctx.saveGState()
            ctx.clip(to: CGRect(origin: .zero, size: paper))
            ctx.interpolationQuality = .high
            draw(img, in: p.rect, turned: p.rotate, ctx)
            ctx.restoreGState()
        }
    }

    private static func pdf(_ url: URL, preset: PrintPreset, paper: CGSize,
                            page: (CGSize, (CGContext) -> Void) -> Void) throws {
        guard let doc = CGPDFDocument(url as CFURL) else { throw CocoaError(.fileReadCorruptFile) }
        if doc.isEncrypted && !doc.isUnlocked { throw NSError(domain: "DragShift", code: 1, userInfo: [NSLocalizedDescriptionKey: "it has a password"]) }
        guard doc.numberOfPages > 0 else { throw CocoaError(.fileReadCorruptFile) }
        let fit: PrintPreset.Fit = preset.fit == .fill ? .fit : preset.fit
        for i in 1...doc.numberOfPages {
            guard let pg = doc.page(at: i) else { continue }
            let box = pg.getBoxRect(.cropBox)
            let quarter = (pg.rotationAngle / 90) % 2 != 0
            let shown = quarter ? CGSize(width: box.height, height: box.width) : box.size
            let p = PrintLayout.place(shown, on: paper, fit: fit)
            page(paper) { ctx in
                if preset.color {
                    ctx.saveGState()
                    place(pg, shown: shown, in: p.rect, turned: p.rotate, ctx)
                    ctx.restoreGState()
                } else if let gray = render(pg, shown: shown, dpi: 300) {
                    // Black & white: the page goes to the printer already grey.
                    ctx.interpolationQuality = .high
                    draw(gray, in: p.rect, turned: p.rotate, ctx)
                }
            }
        }
    }

    /// macOS presets: a picture on a page of its real printed size, which macOS then scales to the paper.
    private static func asIs(image url: URL, page: (CGSize, (CGContext) -> Void) -> Void) throws {
        var img = try ImageOps.load(url, maxPixel: 4200)
        if ImageOps.hasTransparency(img) { img = ImageOps.flatten(img) }
        let props = ImageOps.properties(url)
        let px = ImageOps.pixelSize(url) ?? CGSize(width: img.width, height: img.height)
        let dpi = CGSize(width: (props[kCGImagePropertyDPIWidth] as? Double) ?? 0, height: (props[kCGImagePropertyDPIHeight] as? Double) ?? 0)
        var real = PrintLayout.realSize(pixels: px, dpi: dpi)
        if (img.width > img.height) != (real.width > real.height) { real = CGSize(width: real.height, height: real.width) }
        page(real) { ctx in
            ctx.interpolationQuality = .high
            ctx.draw(img, in: CGRect(origin: .zero, size: real))
        }
    }

    /// macOS presets: every PDF page as it is, the way Preview hands it to the printer.
    private static func asIs(pdf url: URL, page: (CGSize, (CGContext) -> Void) -> Void) throws {
        guard let doc = CGPDFDocument(url as CFURL) else { throw CocoaError(.fileReadCorruptFile) }
        if doc.isEncrypted && !doc.isUnlocked { throw NSError(domain: "DragShift", code: 1, userInfo: [NSLocalizedDescriptionKey: "it has a password"]) }
        guard doc.numberOfPages > 0 else { throw CocoaError(.fileReadCorruptFile) }
        for i in 1...doc.numberOfPages {
            guard let pg = doc.page(at: i) else { continue }
            let box = pg.getBoxRect(.cropBox)
            let shown = (pg.rotationAngle / 90) % 2 != 0 ? CGSize(width: box.height, height: box.width) : box.size
            page(shown) { ctx in place(pg, shown: shown, in: CGRect(origin: .zero, size: shown), turned: false, ctx) }
        }
    }

    /// Draws an image into `rect`; when `turned`, the image is rotated a quarter turn to fill it.
    private static func draw(_ img: CGImage, in rect: CGRect, turned: Bool, _ ctx: CGContext) {
        guard turned else { ctx.draw(img, in: rect); return }
        ctx.saveGState()
        ctx.translateBy(x: rect.midX, y: rect.midY)
        ctx.rotate(by: -.pi / 2)
        ctx.draw(img, in: CGRect(x: -rect.height / 2, y: -rect.width / 2, width: rect.height, height: rect.width))
        ctx.restoreGState()
    }

    /// Draws a PDF page (with its own rotation) into `rect`, scaled to fit it exactly.
    private static func place(_ pg: CGPDFPage, shown: CGSize, in rect: CGRect, turned: Bool, _ ctx: CGContext) {
        ctx.translateBy(x: rect.midX, y: rect.midY)
        if turned { ctx.rotate(by: -.pi / 2) }
        let target = turned ? CGSize(width: rect.height, height: rect.width) : rect.size
        ctx.scaleBy(x: target.width / shown.width, y: target.height / shown.height)
        ctx.translateBy(x: -shown.width / 2, y: -shown.height / 2)
        ctx.concatenate(pg.getDrawingTransform(.cropBox, rect: CGRect(origin: .zero, size: shown), rotate: 0, preserveAspectRatio: true))
        ctx.clip(to: pg.getBoxRect(.cropBox))
        ctx.drawPDFPage(pg)
    }

    /// The page as a grey picture at `dpi`.
    private static func render(_ pg: CGPDFPage, shown: CGSize, dpi: CGFloat) -> CGImage? {
        let w = max(1, Int(shown.width * dpi / 72)), h = max(1, Int(shown.height * dpi / 72))
        guard let g = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return nil }
        g.setFillColor(CGColor(gray: 1, alpha: 1))
        g.fill(CGRect(x: 0, y: 0, width: w, height: h))
        g.interpolationQuality = .high
        place(pg, shown: shown, in: CGRect(x: 0, y: 0, width: w, height: h), turned: false, g)
        return g.makeImage()
    }
}
