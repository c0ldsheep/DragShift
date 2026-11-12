import CoreGraphics
import Foundation
import ImageIO
import PDFKit
import UniformTypeIdentifiers

/// "Under 50 KB" for exam, job and government portals: the best-looking file that still fits.
public enum SizeLimit {
    /// Portals disagree on what a KB is (1,000 or 1,024 bytes). Aiming under 1,000 × N passes both.
    public static func bytes(kb: Int) -> Int { kb * 1000 }

    public static let presets = [20, 50, 100, 200, 500, 1000]

    public static func title(kb: Int) -> String { kb >= 1000 && kb % 1000 == 0 ? "\(kb / 1000) MB" : "\(kb) KB" }

    /// Step id for one size, e.g. `fit.kb.50`. Works for images and PDFs.
    public static func stepID(kb: Int) -> String { "fit.kb.\(kb)" }

    public static func kb(fromStep id: String) -> Int? {
        let p = id.split(separator: ".")
        guard p.count == 3, p[0] == "fit", p[1] == "kb", let n = Int(p[2]), n > 0 else { return nil }
        return n
    }

    public static func action(kb: Int) -> Action {
        Action(stepID(kb: kb), "Under \(title(kb: kb))", "scalemass", verb: "Fitting under \(title(kb: kb))")
    }

    // MARK: Images

    /// JPEG at the highest quality that fits. The picture only gets smaller when even a
    /// low quality is too big, so it stays as sharp as the limit allows.
    public static func jpeg(_ img: CGImage, maxBytes: Int, dpi: Double? = nil) throws -> Data {
        var scale = 1.0
        for _ in 0..<14 {
            let w = max(1, Int((Double(img.width) * scale).rounded())), h = max(1, Int((Double(img.height) * scale).rounded()))
            let pixels = try opaqueSRGB(img, width: w, height: h)
            let top = try encodeJPEG(pixels, quality: 0.95, dpi: dpi)
            if top.count <= maxBytes { return top }
            let floorQ = max(w, h) <= 400 ? 0.2 : 0.5
            let low = try encodeJPEG(pixels, quality: floorQ, dpi: dpi)
            if low.count > maxBytes {
                if max(w, h) <= 64 { break }
                // Shrink about as much as the bytes say, never more than half a step at a time.
                scale *= min(0.92, max(0.5, (Double(maxBytes) / Double(low.count)).squareRoot() * 0.97))
                continue
            }
            var lo = floorQ, hi = 0.95, best = low
            for _ in 0..<7 {
                let mid = (lo + hi) / 2
                let d = try encodeJPEG(pixels, quality: mid, dpi: dpi)
                if d.count <= maxBytes { best = d; lo = mid } else { hi = mid }
            }
            return best
        }
        throw DragShiftError("Couldn't get it under \(title(kb: maxBytes / 1000)). Try a bigger limit.")
    }

    /// Image file → "Name Under 50 KB.jpg". JPGs already under the limit are left alone.
    public static func fitImage(_ url: URL, kb: Int, dir: URL) throws -> ImageOps.CompressResult {
        let before = FileInspector.size(of: url)
        let isJPEG = ["jpg", "jpeg"].contains(url.pathExtension.lowercased())
        if isJPEG && before <= Int64(bytes(kb: kb)) { return .init(url: nil, before: before, after: before) }
        let img = try ImageOps.load(url)
        let dpi = ImageOps.properties(url)[kCGImagePropertyDPIWidth] as? Double
        let data = try jpeg(img, maxBytes: bytes(kb: kb), dpi: dpi.flatMap { $0 > 72.5 ? $0 : nil })
        let out = Naming.output(for: url, ext: "jpg", suffix: "Under \(title(kb: kb))", in: dir)
        try Naming.atomically(out) { tmp in try data.write(to: tmp) }
        return .init(url: out, before: before, after: Int64(data.count))
    }

    /// Flattened on white, in sRGB, at the given size: what every portal can show correctly.
    static func opaqueSRGB(_ img: CGImage, width: Int, height: Int) throws -> CGImage {
        guard let ctx = ImageOps.context(width, height, space: ImageOps.sRGB, opaque: true) else {
            throw DragShiftError("Out of memory.")
        }
        let r = CGRect(x: 0, y: 0, width: width, height: height)
        ctx.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(r)
        ctx.interpolationQuality = .high
        ctx.draw(img, in: r)
        guard let out = ctx.makeImage() else { throw DragShiftError("Out of memory.") }
        return out
    }

    static func gray(_ img: CGImage) throws -> CGImage {
        guard let ctx = CGContext(data: nil, width: img.width, height: img.height, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue)
        else { throw DragShiftError("Out of memory.") }
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: img.width, height: img.height))
        guard let out = ctx.makeImage() else { throw DragShiftError("Out of memory.") }
        return out
    }

    /// No camera data or location in the result: fewer bytes, nothing private sent to a portal.
    static func encodeJPEG(_ img: CGImage, quality: Double, dpi: Double?) throws -> Data {
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(data as CFMutableData, UTType.jpeg.identifier as CFString, 1, nil) else {
            throw DragShiftError("Couldn't make a JPG.")
        }
        var props: [CFString: Any] = [kCGImageDestinationLossyCompressionQuality: quality, kCGImagePropertyOrientation: 1]
        if let dpi { props[kCGImagePropertyDPIWidth] = dpi; props[kCGImagePropertyDPIHeight] = dpi }
        CGImageDestinationAddImage(dest, img, props as CFDictionary)
        guard CGImageDestinationFinalize(dest) else { throw DragShiftError("Couldn't make a JPG.") }
        return data as Data
    }

    // MARK: PDFs

    public struct PDFResult: Sendable {
        public let result: ImageOps.CompressResult
        /// True when pages had to be saved as pictures (text can't be selected any more).
        public let flattened: Bool
    }

    /// First tries a normal compress (text stays text). If that's not enough, every page is
    /// redrawn as a picture, at the sharpest resolution and quality that fit.
    public static func fitPDF(_ url: URL, kb: Int, dir: URL, progress: ((Double) -> Void)? = nil) throws -> PDFResult {
        let before = FileInspector.size(of: url)
        let limit = bytes(kb: kb)
        if before <= Int64(limit) { return .init(result: .init(url: nil, before: before, after: before), flattened: false) }
        let doc = try PDFOps.open(url)
        let out = Naming.output(for: url, ext: "pdf", suffix: "Under \(title(kb: kb))", in: dir)

        if #available(macOS 13.4, *) {
            let tmp = FileManager.default.temporaryDirectory.appendingPathComponent("dragshift-fit-\(UUID().uuidString).pdf")
            defer { try? FileManager.default.removeItem(at: tmp) }
            if doc.write(to: tmp, withOptions: [.saveImagesAsJPEGOption: true, .optimizeImagesForScreenOption: true]),
               FileInspector.size(of: tmp) <= Int64(limit) {
                try Naming.atomically(out) { t in try FileManager.default.copyItem(at: tmp, to: t) }
                return .init(result: .init(url: out, before: before, after: FileInspector.size(of: out)), flattened: false)
            }
        }

        let pages = (0..<doc.pageCount).compactMap { doc.page(at: $0) }
        guard !pages.isEmpty else { throw DragShiftError("\(url.lastPathComponent) has no pages.") }
        let ladder = [0.8, 0.7, 0.6, 0.5, 0.4, 0.32]
        let dpis = [150.0, 120, 96, 72, 56]
        // The writer's own bytes: header, page objects, xref.
        let overhead = 400 + 520 * pages.count
        for (di, dpi) in dpis.enumerated() {
            var totals = [Int](repeating: overhead, count: ladder.count)
            var datas = [[PDFWriter.Page]](repeating: [], count: ladder.count)
            for (pi, page) in pages.enumerated() {
                try CancelToken.check()
                guard let img = try PDFOps.render(page, dpi: dpi) else { throw DragShiftError("Couldn't read page \(pi + 1).") }
                let pixels = isGray(img) ? try gray(img) : img
                let size = CGSize(width: Double(img.width) * 72 / dpi, height: Double(img.height) * 72 / dpi)
                for (li, q) in ladder.enumerated() where totals[li] <= limit {
                    let d = try encodeJPEG(pixels, quality: q, dpi: nil)
                    totals[li] += d.count
                    if totals[li] <= limit {
                        datas[li].append(.init(jpeg: d, pixelWidth: pixels.width, pixelHeight: pixels.height,
                                               gray: pixels.colorSpace?.model == .monochrome, size: size))
                    } else {
                        datas[li] = []
                    }
                }
                progress?((Double(di) + Double(pi + 1) / Double(pages.count)) / Double(dpis.count))
                if totals.allSatisfy({ $0 > limit }) { break }
            }
            for li in ladder.indices where totals[li] <= limit {
                let data = PDFWriter.make(datas[li])
                guard data.count <= limit else { continue }
                try Naming.atomically(out) { t in try data.write(to: t) }
                return .init(result: .init(url: out, before: before, after: Int64(data.count)), flattened: true)
            }
        }
        throw DragShiftError("Couldn't get \(url.lastPathComponent) under \(title(kb: kb)). Try a bigger limit, or Split it first.")
    }

    /// Scans and black-and-white documents save a third smaller as grayscale.
    static func isGray(_ img: CGImage) -> Bool {
        let w = min(200, img.width), h = max(1, Int(Double(img.height) * Double(w) / Double(img.width)))
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4, space: ImageOps.sRGB,
                                  bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue), let data = ctx.data else { return false }
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        let px = data.bindMemory(to: UInt8.self, capacity: w * h * 4)
        var colored = 0
        for i in stride(from: 0, to: w * h * 4, by: 4) {
            let r = Int(px[i]), g = Int(px[i + 1]), b = Int(px[i + 2])
            if max(abs(r - g), abs(g - b), abs(r - b)) > 24 { colored += 1 }
        }
        return colored * 400 < w * h // under 0.25% coloured pixels
    }
}

/// The smallest possible PDF for pages that are pictures: one JPEG per page, stored as is.
public enum PDFWriter {
    public struct Page: Sendable {
        public let jpeg: Data
        public let pixelWidth: Int
        public let pixelHeight: Int
        public let gray: Bool
        /// Page size in points (1/72 inch).
        public let size: CGSize
    }

    public static func make(_ pages: [Page]) -> Data {
        var out = Data()
        var offsets: [Int] = []
        func put(_ s: String) { out.append(contentsOf: Array(s.utf8)) }
        func object(_ body: () -> Void) {
            offsets.append(out.count)
            put("\(offsets.count) 0 obj\n")
            body()
            put("\nendobj\n")
        }
        func n(_ v: Double) -> String { String(format: "%.2f", v) }

        put("%PDF-1.4\n%")
        out.append(contentsOf: [0xE2, 0xE3, 0xCF, 0xD3]) // marks the file as binary
        put("\n")
        // 1 catalog, 2 page tree, then per page: page, contents, image.
        let kids = pages.indices.map { "\(3 + $0 * 3) 0 R" }.joined(separator: " ")
        object { put("<< /Type /Catalog /Pages 2 0 R >>") }
        object { put("<< /Type /Pages /Kids [\(kids)] /Count \(pages.count) >>") }
        for (i, p) in pages.enumerated() {
            let base = 3 + i * 3
            let draw = "q \(n(p.size.width)) 0 0 \(n(p.size.height)) 0 0 cm /Im0 Do Q"
            object {
                put("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 \(n(p.size.width)) \(n(p.size.height))] ")
                put("/Resources << /XObject << /Im0 \(base + 2) 0 R >> >> /Contents \(base + 1) 0 R >>")
            }
            object { put("<< /Length \(draw.utf8.count) >>\nstream\n\(draw)\nendstream") }
            object {
                put("<< /Type /XObject /Subtype /Image /Width \(p.pixelWidth) /Height \(p.pixelHeight) ")
                put("/ColorSpace /\(p.gray ? "DeviceGray" : "DeviceRGB") /BitsPerComponent 8 /Filter /DCTDecode ")
                put("/Length \(p.jpeg.count) >>\nstream\n")
                out.append(p.jpeg)
                put("\nendstream")
            }
        }
        let xref = out.count
        put("xref\n0 \(offsets.count + 1)\n0000000000 65535 f \n")
        for o in offsets { put(String(format: "%010d 00000 n \n", o)) }
        put("trailer\n<< /Size \(offsets.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n")
        return out
    }
}
