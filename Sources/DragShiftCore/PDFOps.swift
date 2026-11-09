import AppKit
import CoreText
import Foundation
import PDFKit

public enum PDFOps {

    public static func open(_ url: URL, password: String? = nil) throws -> PDFDocument {
        guard let doc = PDFDocument(url: url) else { throw DragShiftError("\(url.lastPathComponent) isn't a readable PDF.") }
        if doc.isLocked {
            guard let password, doc.unlock(withPassword: password) else {
                throw DragShiftError(password == nil ? "\(url.lastPathComponent) is password-protected. Use Unlock first."
                                                     : "Wrong password for \(url.lastPathComponent).")
            }
        }
        return doc
    }

    /// Renders one page to pixels at `dpi`, page rotation applied, on white.
    public static func render(_ page: PDFPage, dpi: Double) -> CGImage? {
        guard let ref = page.pageRef else { return nil }
        let box = ref.getBoxRect(.cropBox)
        let rotated = ref.rotationAngle % 180 != 0
        let pts = rotated ? CGSize(width: box.height, height: box.width) : box.size
        let scale = dpi / 72
        let w = Int((pts.width * scale).rounded()), h = Int((pts.height * scale).rounded())
        guard w > 0, h > 0, let ctx = ImageOps.context(w, h, space: ImageOps.sRGB, opaque: true) else { return nil }
        ctx.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        ctx.interpolationQuality = .high
        ctx.scaleBy(x: scale, y: scale)
        // Same-size rect, so this transform only rotates and moves (it never scales up).
        ctx.concatenate(ref.getDrawingTransform(.cropBox, rect: CGRect(origin: .zero, size: pts), rotate: 0,
                                                preserveAspectRatio: true))
        ctx.drawPDFPage(ref)
        return ctx.makeImage()
    }

    /// Every page as an image. One page -> "Name.jpg"; more -> a folder "Name (JPG)" with "Name 01.jpg"...
    public static func toImages(_ url: URL, format: ImageFormat, dpi: Double, quality: Double, dir: URL,
                                progress: ((Double) -> Void)? = nil) throws -> [URL] {
        let doc = try open(url)
        let n = doc.pageCount
        guard n > 0 else { throw DragShiftError("\(url.lastPathComponent) has no pages.") }
        if n == 1, let page = doc.page(at: 0), let img = render(page, dpi: dpi) {
            let out = Naming.output(for: url, ext: format.ext, in: dir)
            try ImageOps.write(img, to: out, format: format, quality: quality)
            return [out]
        }
        let folder = try Naming.newFolder(named: "\(Naming.base(url)) (\(format.title))", in: dir)
        let digits = String(n).count
        for i in 0..<n {
            guard let page = doc.page(at: i), let img = render(page, dpi: dpi) else { continue }
            let num = String(format: "%0\(digits)d", i + 1)
            let out = folder.appendingPathComponent("\(Naming.base(url)) \(num)").appendingPathExtension(format.ext)
            try ImageOps.write(img, to: out, format: format, quality: quality)
            progress?(Double(i + 1) / Double(n))
        }
        return [folder]
    }

    /// The text layer. Scanned PDFs (no text layer) are read with on-device OCR instead.
    public static func text(_ doc: PDFDocument) throws -> String {
        let direct = (doc.string ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if direct.count > 20 * max(1, doc.pageCount) / 4 { return doc.string ?? direct }
        var pages: [String] = []
        for i in 0..<doc.pageCount {
            guard let page = doc.page(at: i), let img = render(page, dpi: 200) else { continue }
            pages.append(try VisionOps.text(img))
        }
        let ocr = pages.joined(separator: "\n\n")
        return ocr.count > direct.count ? ocr : direct
    }

    /// Styled text, page by page (fonts, bold, sizes as the PDF's text layer has them).
    public static func attributedText(_ doc: PDFDocument) throws -> NSAttributedString {
        let out = NSMutableAttributedString()
        var hasText = false
        for i in 0..<doc.pageCount {
            guard let page = doc.page(at: i) else { continue }
            if let a = page.attributedString, a.string.trimmingCharacters(in: .whitespacesAndNewlines).count > 5 {
                hasText = true
                out.append(a)
            }
            if i < doc.pageCount - 1 { out.append(NSAttributedString(string: "\n\n")) }
        }
        if hasText { return out }
        return DocOps.plain(try text(doc))
    }

    /// Re-saves images inside as JPEG sized for screens. Returns nil url if it didn't shrink.
    public static func compress(_ url: URL, dir: URL) throws -> ImageOps.CompressResult {
        let doc = try open(url)
        let before = FileInspector.size(of: url)
        let out = Naming.output(for: url, ext: "pdf", suffix: "Compressed", in: dir)
        try Naming.atomically(out) { tmp in
            guard #available(macOS 13.4, *) else { throw DragShiftError("PDF compression needs macOS 13.4 or newer.") }
            guard doc.write(to: tmp, withOptions: [.saveImagesAsJPEGOption: true, .optimizeImagesForScreenOption: true]) else {
                throw DragShiftError("Couldn't save the compressed PDF.")
            }
        }
        let after = FileInspector.size(of: out)
        if after >= before {
            try? FileManager.default.removeItem(at: out)
            return .init(url: nil, before: before, after: before)
        }
        return .init(url: out, before: before, after: after)
    }

    /// One PDF per page, in a folder "Name (pages)".
    public static func split(_ url: URL, dir: URL) throws -> URL {
        let doc = try open(url)
        guard doc.pageCount > 1 else { throw DragShiftError("\(url.lastPathComponent) only has one page.") }
        let folder = try Naming.newFolder(named: "\(Naming.base(url)) (pages)", in: dir)
        let digits = String(doc.pageCount).count
        for i in 0..<doc.pageCount {
            guard let page = doc.page(at: i)?.copy() as? PDFPage else { continue }
            let single = PDFDocument()
            single.insert(page, at: 0)
            let num = String(format: "%0\(digits)d", i + 1)
            let out = folder.appendingPathComponent("\(Naming.base(url)) \(num).pdf")
            guard single.write(to: out) else { throw DragShiftError("Couldn't write page \(i + 1).") }
        }
        return folder
    }

    /// Joins PDFs (and images, as pages) in Finder name order.
    public static func merge(_ urls: [URL], dir: URL, quality: Double) throws -> URL {
        let doc = PDFDocument()
        for url in urls {
            switch FileInspector.kind(of: url) {
            case .pdf:
                let src = try open(url)
                for i in 0..<src.pageCount {
                    if let p = src.page(at: i)?.copy() as? PDFPage { doc.insert(p, at: doc.pageCount) }
                }
            case .image:
                let tmp = FileManager.default.temporaryDirectory.appendingPathComponent("dragshift-\(UUID().uuidString).pdf")
                defer { try? FileManager.default.removeItem(at: tmp) }
                try ImageOps.makePDF([url], to: tmp, quality: quality)
                if let one = PDFDocument(url: tmp), let p = one.page(at: 0)?.copy() as? PDFPage { doc.insert(p, at: doc.pageCount) }
            default:
                continue
            }
        }
        guard doc.pageCount > 0, let first = urls.first else { throw DragShiftError("Nothing to merge.") }
        let out = Naming.output(for: first, ext: "pdf", suffix: "Merged", in: dir)
        try Naming.atomically(out) { tmp in
            guard doc.write(to: tmp) else { throw DragShiftError("Couldn't save the merged PDF.") }
        }
        return out
    }

    public static func rotate(_ url: URL, degrees: Int, dir: URL) throws -> URL {
        let doc = try open(url)
        for i in 0..<doc.pageCount {
            guard let p = doc.page(at: i) else { continue }
            p.rotation = ((p.rotation + degrees) % 360 + 360) % 360
        }
        let out = Naming.output(for: url, ext: "pdf", suffix: "Rotated", in: dir)
        try Naming.atomically(out) { tmp in
            guard doc.write(to: tmp) else { throw DragShiftError("Couldn't save the rotated PDF.") }
        }
        return out
    }

    public static func lock(_ url: URL, password: String, dir: URL) throws -> URL {
        guard !password.isEmpty else { throw DragShiftError("Type a password first.") }
        let doc = try open(url)
        let out = Naming.output(for: url, ext: "pdf", suffix: "Locked", in: dir)
        try Naming.atomically(out) { tmp in
            guard doc.write(to: tmp, withOptions: [.userPasswordOption: password, .ownerPasswordOption: password]) else {
                throw DragShiftError("Couldn't save the locked PDF.")
            }
        }
        return out
    }

    public static func unlock(_ url: URL, password: String, dir: URL) throws -> URL {
        let doc = try open(url, password: password)
        let out = Naming.output(for: url, ext: "pdf", suffix: "Unlocked", in: dir)
        guard let data = doc.dataRepresentation(), let clean = PDFDocument(data: data) else {
            throw DragShiftError("Couldn't unlock \(url.lastPathComponent).")
        }
        // A fresh document built from page copies carries no encryption.
        let plain = PDFDocument()
        if clean.isLocked { _ = clean.unlock(withPassword: password) }
        for i in 0..<clean.pageCount {
            if let p = clean.page(at: i)?.copy() as? PDFPage { plain.insert(p, at: plain.pageCount) }
        }
        try Naming.atomically(out) { tmp in
            guard plain.write(to: tmp) else { throw DragShiftError("Couldn't save the unlocked PDF.") }
        }
        return out
    }

    /// Adds an invisible text layer to scanned pages (on-device OCR), so you can search,
    /// select and copy the words. Pages that already have text are kept as they are.
    public static func makeSearchable(_ url: URL, dir: URL, progress: ((Double) -> Void)? = nil) throws -> URL {
        let doc = try open(url)
        guard doc.pageCount > 0 else { throw DragShiftError("\(url.lastPathComponent) has no pages.") }
        let out = Naming.output(for: url, ext: "pdf", suffix: "Searchable", in: dir)
        var added = 0
        try Naming.atomically(out) { tmp in
            guard let ctx = CGContext(tmp as CFURL, mediaBox: nil, nil) else { throw DragShiftError("Couldn't create the PDF.") }
            for i in 0..<doc.pageCount {
                guard let page = doc.page(at: i), let ref = page.pageRef else { continue }
                let box = ref.getBoxRect(.cropBox)
                let rotated = ref.rotationAngle % 180 != 0
                let size = rotated ? CGSize(width: box.height, height: box.width) : box.size
                var media = CGRect(origin: .zero, size: size)
                ctx.beginPage(mediaBox: &media)
                ctx.saveGState()
                ctx.concatenate(ref.getDrawingTransform(.cropBox, rect: media, rotate: 0, preserveAspectRatio: true))
                ctx.drawPDFPage(ref)
                ctx.restoreGState()
                let existing = (page.string ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                if existing.count < 10, let img = render(page, dpi: 220) {
                    for line in try VisionOps.lines(img) {
                        drawInvisible(line.text, in: CGRect(x: line.box.minX * size.width, y: line.box.minY * size.height,
                                                            width: line.box.width * size.width, height: line.box.height * size.height), ctx)
                        added += 1
                    }
                }
                ctx.endPage()
                progress?(Double(i + 1) / Double(doc.pageCount))
            }
            ctx.closePDF()
        }
        if added == 0 {
            try? FileManager.default.removeItem(at: out)
            throw DragShiftError("\(url.lastPathComponent) is already searchable (or has no readable text).")
        }
        return out
    }

    /// Text that's there for search and copy but never shows, stretched to cover the word box.
    static func drawInvisible(_ text: String, in r: CGRect, _ ctx: CGContext) {
        guard r.width > 1, r.height > 1 else { return }
        let font = CTFontCreateWithName("Helvetica" as CFString, r.height * 0.9, nil)
        let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [.font: font]))
        let width = CTLineGetTypographicBounds(line, nil, nil, nil)
        guard width > 0 else { return }
        ctx.saveGState()
        ctx.textMatrix = .identity
        ctx.setTextDrawingMode(.invisible)
        ctx.translateBy(x: r.minX, y: r.minY + r.height * 0.18)
        ctx.scaleBy(x: r.width / width, y: 1)
        ctx.textPosition = .zero
        CTLineDraw(line, ctx)
        ctx.restoreGState()
    }
}
