import AppKit
import CoreImage
import Foundation
import ImageIO
import PDFKit
import UniformTypeIdentifiers

/// Image loading, saving and the one-drop image tools.
/// Every edit works on the "oriented" pixels, so phone photos never come out sideways.
public enum ImageOps {

    // MARK: Loading

    public static func source(_ url: URL) throws -> CGImageSource {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary),
              CGImageSourceGetCount(src) > 0
        else { throw DragShiftError("\(url.lastPathComponent) isn't an image this Mac can read.") }
        return src
    }

    public static func properties(_ url: URL) -> [CFString: Any] {
        guard let src = try? source(url) else { return [:] }
        return (CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any]) ?? [:]
    }

    public static func pixelSize(_ url: URL) -> CGSize? {
        let p = properties(url)
        guard let w = p[kCGImagePropertyPixelWidth] as? Int, let h = p[kCGImagePropertyPixelHeight] as? Int else {
            return nil
        }
        let o = (p[kCGImagePropertyOrientation] as? Int) ?? 1
        return o >= 5 ? CGSize(width: h, height: w) : CGSize(width: w, height: h)
    }

    /// The most pixels a picture may have to be opened (250 megapixels: a 12 MP phone photo or a 100 MP camera is
    /// fine). A file can claim, say, 60,000 × 60,000 in a few bytes; decoding it would need about 14 GB and crash
    /// the app, and a watch folder would hit it again with every copy.
    public static let maxPixels = 250_000_000

    /// Full-size pixels with the EXIF rotation baked in. `maxPixel` downsamples on decode (fast).
    public static func load(_ url: URL, maxPixel: Int? = nil) throws -> CGImage {
        if let src = try? source(url) {
            let p = (CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any]) ?? [:]
            let w = (p[kCGImagePropertyPixelWidth] as? Int) ?? 0
            let h = (p[kCGImagePropertyPixelHeight] as? Int) ?? 0
            if w * h > maxPixels {
                throw DragShiftError("\(url.lastPathComponent) is \(w * h / 1_000_000) megapixels, too big to open safely.")
            }
            let longest = max(w, h)
            let limit = min(maxPixel ?? longest, longest > 0 ? longest : (maxPixel ?? 4096))
            let opts: [CFString: Any] = [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: max(1, limit),
                kCGImageSourceShouldCacheImmediately: true,
            ]
            if let img = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) { return img }
        }
        // Formats ImageIO can't decode (SVG and friends): let AppKit try.
        if let ns = NSImage(contentsOf: url) {
            var rect = CGRect(origin: .zero, size: ns.size)
            if let cg = ns.cgImage(forProposedRect: &rect, context: nil, hints: nil) { return cg }
        }
        throw DragShiftError("Couldn't open \(url.lastPathComponent) as an image.")
    }

    // MARK: Pixels

    static let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!

    static func context(_ w: Int, _ h: Int, space: CGColorSpace? = nil, opaque: Bool = false) -> CGContext? {
        let info = opaque ? CGImageAlphaInfo.noneSkipLast.rawValue : CGImageAlphaInfo.premultipliedLast.rawValue
        if let space, space.model == .rgb,
           let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: space, bitmapInfo: info) {
            return ctx
        }
        return CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: sRGB, bitmapInfo: info)
    }

    public static func hasAlphaChannel(_ img: CGImage) -> Bool {
        switch img.alphaInfo {
        case .none, .noneSkipFirst, .noneSkipLast: return false
        default: return true
        }
    }

    /// True only if some pixel is actually see-through (many PNGs carry an unused alpha channel).
    public static func hasTransparency(_ img: CGImage) -> Bool {
        guard hasAlphaChannel(img) else { return false }
        let scale = min(1, 512 / Double(max(img.width, img.height)))
        let w = max(1, Int(Double(img.width) * scale)), h = max(1, Int(Double(img.height) * scale))
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                  space: sRGB, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let data = ctx.data
        else { return true }
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        let px = data.bindMemory(to: UInt8.self, capacity: w * h * 4)
        for i in stride(from: 3, to: w * h * 4, by: 4) where px[i] < 250 { return true }
        return false
    }

    /// Puts the image on a solid colour (needed before saving as JPG, which has no transparency).
    public static func flatten(_ img: CGImage, on color: CGColor = CGColor(red: 1, green: 1, blue: 1, alpha: 1)) -> CGImage {
        guard let ctx = context(img.width, img.height, space: img.colorSpace, opaque: true) else { return img }
        let r = CGRect(x: 0, y: 0, width: img.width, height: img.height)
        ctx.setFillColor(color)
        ctx.fill(r)
        ctx.draw(img, in: r)
        return ctx.makeImage() ?? img
    }

    public static func scaled(_ img: CGImage, to size: CGSize) -> CGImage {
        let w = max(1, Int(size.width.rounded())), h = max(1, Int(size.height.rounded()))
        guard let ctx = context(w, h, space: img.colorSpace, opaque: !hasAlphaChannel(img)) else { return img }
        ctx.interpolationQuality = .high
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage() ?? img
    }

    public static func fit(_ img: CGImage, longEdge: Int) -> CGImage {
        let longest = max(img.width, img.height)
        guard longest > longEdge else { return img }
        let s = Double(longEdge) / Double(longest)
        return scaled(img, to: CGSize(width: Double(img.width) * s, height: Double(img.height) * s))
    }

    /// Quarter turns clockwise.
    public static func rotated(_ img: CGImage, quarterTurns: Int) -> CGImage {
        let q = ((quarterTurns % 4) + 4) % 4
        guard q != 0 else { return img }
        let w = q % 2 == 0 ? img.width : img.height
        let h = q % 2 == 0 ? img.height : img.width
        guard let ctx = context(w, h, space: img.colorSpace, opaque: !hasAlphaChannel(img)) else { return img }
        ctx.translateBy(x: CGFloat(w) / 2, y: CGFloat(h) / 2)
        ctx.rotate(by: -CGFloat(q) * .pi / 2)
        ctx.draw(img, in: CGRect(x: -CGFloat(img.width) / 2, y: -CGFloat(img.height) / 2,
                                 width: CGFloat(img.width), height: CGFloat(img.height)))
        return ctx.makeImage() ?? img
    }

    public static func flipped(_ img: CGImage, horizontal: Bool = true) -> CGImage {
        guard let ctx = context(img.width, img.height, space: img.colorSpace, opaque: !hasAlphaChannel(img)) else { return img }
        if horizontal {
            ctx.translateBy(x: CGFloat(img.width), y: 0)
            ctx.scaleBy(x: -1, y: 1)
        } else {
            ctx.translateBy(x: 0, y: CGFloat(img.height))
            ctx.scaleBy(x: 1, y: -1)
        }
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: img.width, height: img.height))
        return ctx.makeImage() ?? img
    }

    // MARK: Saving

    /// Source metadata made safe to re-attach to new pixels: rotation reset (pixels are
    /// already upright) and stale pixel dimensions dropped. Camera, date and GPS are kept.
    static func carriedMetadata(_ props: [CFString: Any]) -> [CFString: Any] {
        var out: [CFString: Any] = [:]
        for key in [kCGImagePropertyExifDictionary, kCGImagePropertyGPSDictionary,
                    kCGImagePropertyIPTCDictionary, kCGImagePropertyTIFFDictionary] {
            guard var dict = props[key] as? [CFString: Any] else { continue }
            dict.removeValue(forKey: kCGImagePropertyExifPixelXDimension)
            dict.removeValue(forKey: kCGImagePropertyExifPixelYDimension)
            if key == kCGImagePropertyTIFFDictionary { dict[kCGImagePropertyTIFFOrientation] = 1 }
            out[key] = dict
        }
        for key in [kCGImagePropertyDPIWidth, kCGImagePropertyDPIHeight] {
            if let v = props[key] { out[key] = v }
        }
        out[kCGImagePropertyOrientation] = 1
        return out
    }

    /// Saves pixels in a format. WebP goes through cwebp; everything else is ImageIO.
    public static func write(_ img: CGImage, to url: URL, format: ImageFormat, quality: Double,
                             metadata: [CFString: Any]? = nil) throws {
        var pixels = img
        if !format.supportsAlpha && hasAlphaChannel(img) { pixels = flatten(img) }
        if format == .webp {
            try writeWebP(pixels, to: url, quality: quality)
            return
        }
        guard format.available else { throw DragShiftError("This Mac can't write \(format.title) files.") }
        try Naming.atomically(url) { tmp in
            guard let dest = CGImageDestinationCreateWithURL(tmp as CFURL, format.uti as CFString, 1, nil) else {
                throw DragShiftError("Couldn't create a \(format.title) file.")
            }
            var props = metadata ?? [:]
            if format.isLossy { props[kCGImageDestinationLossyCompressionQuality] = quality }
            CGImageDestinationAddImage(dest, pixels, props as CFDictionary)
            guard CGImageDestinationFinalize(dest) else { throw DragShiftError("Couldn't save the \(format.title) file.") }
        }
    }

    static func writeWebP(_ img: CGImage, to url: URL, quality: Double) throws {
        guard let cwebp = Tools.cwebp else {
            let data = try WebPLossless.encode(img)
            try Naming.atomically(url) { tmp in try data.write(to: tmp) }
            return
        }
        let tmpPNG = FileManager.default.temporaryDirectory.appendingPathComponent("dragshift-\(UUID().uuidString).png")
        defer { try? FileManager.default.removeItem(at: tmpPNG) }
        try write(img, to: tmpPNG, format: .png, quality: 1)
        try Naming.atomically(url) { tmp in
            try Shell.run(cwebp, ["-quiet", "-mt", "-q", String(Int((quality * 100).rounded())),
                                  "-metadata", "icc", tmpPNG.path, "-o", tmp.path])
        }
    }

    // MARK: One-drop tools

    /// Format conversion, keeping camera data and colour profile.
    public static func convert(_ url: URL, to format: ImageFormat, quality: Double, dir: URL) throws -> URL {
        let img = try load(url)
        let out = Naming.output(for: url, ext: format.ext, in: dir)
        try write(img, to: out, format: format, quality: quality, metadata: carriedMetadata(properties(url)))
        return out
    }

    /// Writes `img` next to `url` in the original's format (JPG if the original format can't be written).
    public static func saveEdited(_ img: CGImage, like url: URL, suffix: String, dir: URL,
                                  quality: Double, keepMetadata: Bool = true, forceFormat: ImageFormat? = nil) throws -> URL {
        var format = forceFormat ?? ImageFormat.from(ext: url.pathExtension) ?? .png
        if !format.available { format = hasTransparency(img) ? .png : .jpg }
        let out = Naming.output(for: url, ext: format.ext, suffix: suffix, in: dir)
        try write(img, to: out, format: format, quality: quality,
                  metadata: keepMetadata ? carriedMetadata(properties(url)) : nil)
        return out
    }

    public struct CompressResult: Sendable {
        public let url: URL?
        public let before: Int64
        public let after: Int64
    }

    /// Smaller copy. Lossy formats stay in their format; PNG/TIFF/BMP become JPG unless
    /// they are see-through (then HEIC, which keeps transparency). Never returns a bigger file.
    public static func compress(_ url: URL, quality: Double, maxEdge: Int, dir: URL) throws -> CompressResult {
        let before = FileInspector.size(of: url)
        let img = fit(try load(url), longEdge: maxEdge)
        var format = ImageFormat.from(ext: url.pathExtension) ?? .jpg
        if !format.isLossy || !format.available {
            format = hasTransparency(img) ? (ImageFormat.heic.available ? .heic : .png) : .jpg
        }
        let out = Naming.output(for: url, ext: format.ext, suffix: "Compressed", in: dir)
        try write(img, to: out, format: format, quality: quality, metadata: carriedMetadata(properties(url)))
        let after = FileInspector.size(of: out)
        if after >= before {
            try? FileManager.default.removeItem(at: out)
            return CompressResult(url: nil, before: before, after: before)
        }
        return CompressResult(url: out, before: before, after: after)
    }

    public static func resize(_ url: URL, longEdge: Int?, scale: Double?, quality: Double, dir: URL) throws -> URL {
        let img = try load(url)
        var size = CGSize(width: img.width, height: img.height)
        if let scale { size = CGSize(width: size.width * scale, height: size.height * scale) }
        if let longEdge {
            let s = Double(longEdge) / Double(max(img.width, img.height))
            size = CGSize(width: Double(img.width) * s, height: Double(img.height) * s)
        }
        let out = scaled(img, to: size)
        return try saveEdited(out, like: url, suffix: "\(out.width)x\(out.height)", dir: dir, quality: quality)
    }

    /// Crop rect is in pixels of the upright image, top-left origin.
    public static func crop(_ url: URL, rect: CGRect, quality: Double, dir: URL) throws -> URL {
        let img = try load(url)
        let bounds = CGRect(x: 0, y: 0, width: img.width, height: img.height)
        let r = rect.integral.intersection(bounds)
        guard r.width >= 1, r.height >= 1, let cut = img.cropping(to: r) else {
            throw DragShiftError("That crop is empty.")
        }
        return try saveEdited(cut, like: url, suffix: "Cropped", dir: dir, quality: quality)
    }

    public static func rotate(_ url: URL, quarterTurns: Int, quality: Double, dir: URL) throws -> URL {
        try saveEdited(rotated(try load(url), quarterTurns: quarterTurns), like: url, suffix: "Rotated", dir: dir, quality: quality)
    }

    public static func flip(_ url: URL, quality: Double, dir: URL) throws -> URL {
        try saveEdited(flipped(try load(url)), like: url, suffix: "Flipped", dir: dir, quality: quality)
    }

    /// Apple's own auto-enhance (the magic wand in Photos): exposure, contrast, colour, shadows.
    public static func enhance(_ url: URL, quality: Double, dir: URL) throws -> URL {
        let img = try load(url)
        var ci = CIImage(cgImage: img)
        for filter in ci.autoAdjustmentFilters(options: [.redEye: false]) {
            filter.setValue(ci, forKey: kCIInputImageKey)
            if let o = filter.outputImage { ci = o }
        }
        let ctx = CIContext()
        guard let out = ctx.createCGImage(ci, from: CGRect(x: 0, y: 0, width: img.width, height: img.height),
                                          format: .RGBA8, colorSpace: img.colorSpace ?? sRGB)
        else { throw DragShiftError("Enhance failed.") }
        return try saveEdited(out, like: url, suffix: "Enhanced", dir: dir, quality: quality)
    }

    /// Removes camera, date, location and every other tag. JPG/PNG/HEIC/TIFF are copied
    /// without re-compressing (no quality loss); other formats are re-saved at high quality.
    public static func stripMetadata(_ url: URL, dir: URL) throws -> URL {
        let src = try source(url)
        let props = (CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any]) ?? [:]
        let orientation = (props[kCGImagePropertyOrientation] as? Int) ?? 1
        let type = (CGImageSourceGetType(src) as String?) ?? UTType.jpeg.identifier
        let ext = url.pathExtension.isEmpty ? "jpg" : url.pathExtension
        let out = Naming.output(for: url, ext: ext, suffix: "Clean", in: dir)
        let lossless: Set<String> = [UTType.jpeg.identifier, UTType.png.identifier, UTType.heic.identifier, UTType.tiff.identifier]
        if lossless.contains(type) {
            do {
                try Naming.atomically(out) { tmp in
                    guard let dest = CGImageDestinationCreateWithURL(tmp as CFURL, type as CFString, 1, nil) else {
                        throw DragShiftError("copy failed")
                    }
                    let opts: [CFString: Any] = [
                        kCGImageDestinationMetadata: CGImageMetadataCreateMutable(),
                        kCGImageDestinationMergeMetadata: false,
                        kCGImageMetadataShouldExcludeGPS: true,
                        kCGImageMetadataShouldExcludeXMP: true,
                        kCGImageDestinationOrientation: orientation,
                    ]
                    var err: Unmanaged<CFError>?
                    guard CGImageDestinationCopyImageSource(dest, src, opts as CFDictionary, &err) else {
                        throw DragShiftError("copy failed")
                    }
                }
                if !hasPrivateMetadata(out) { return out }
                try? FileManager.default.removeItem(at: out)
            } catch {
                try? FileManager.default.removeItem(at: out)
            }
        }
        // Fallback: decode and re-encode with no metadata at all.
        let img = try load(url)
        let format = ImageFormat.from(ext: ext).flatMap { $0.available ? $0 : nil } ?? .jpg
        let out2 = Naming.output(for: url, ext: format.ext, suffix: "Clean", in: dir)
        try write(img, to: out2, format: format, quality: 0.95, metadata: [kCGImagePropertyOrientation: 1])
        return out2
    }

    /// Location, camera serials, dates, editing history: the stuff people don't mean to share.
    public static func hasPrivateMetadata(_ url: URL) -> Bool {
        let p = properties(url)
        if p[kCGImagePropertyGPSDictionary] != nil { return true }
        if let exif = p[kCGImagePropertyExifDictionary] as? [CFString: Any],
           exif[kCGImagePropertyExifDateTimeOriginal] != nil || exif[kCGImagePropertyExifBodySerialNumber] != nil
            || exif[kCGImagePropertyExifLensModel] != nil { return true }
        if let tiff = p[kCGImagePropertyTIFFDictionary] as? [CFString: Any],
           tiff[kCGImagePropertyTIFFModel] != nil || tiff[kCGImagePropertyTIFFMake] != nil
            || tiff[kCGImagePropertyTIFFDateTime] != nil { return true }
        return false
    }

    // MARK: Multi-image outputs

    /// One PDF page per image, in the order given. Pages are sized from the image's DPI
    /// (scans come out A4-ish); photos with no DPI are fitted to an A4-sized page.
    public static func makePDF(_ urls: [URL], to out: URL, quality: Double) throws {
        let doc = PDFDocument()
        for url in urls {
            let img = try load(url)
            let p = properties(url)
            let dpi = (p[kCGImagePropertyDPIWidth] as? Double) ?? 72
            var size: CGSize
            if dpi > 72.5 {
                size = CGSize(width: Double(img.width) * 72 / dpi, height: Double(img.height) * 72 / dpi)
            } else {
                let s = min(1, 842 / Double(max(img.width, img.height)))
                size = CGSize(width: Double(img.width) * s, height: Double(img.height) * s)
            }
            let ns = NSImage(cgImage: img, size: size)
            guard let page = PDFPage(image: ns, options: [
                .mediaBox: CGRect(origin: .zero, size: size),
                .compressionQuality: quality,
                .upscaleIfSmaller: true,
            ]) else { throw DragShiftError("Couldn't add \(url.lastPathComponent) to the PDF.") }
            doc.insert(page, at: doc.pageCount)
        }
        try Naming.atomically(out) { tmp in
            guard doc.write(to: tmp) else { throw DragShiftError("Couldn't save the PDF.") }
        }
    }

    /// The most pixels a stitched picture may have (about 400 MB while it's being drawn), and JPEG's tallest side.
    static let stitchMaxPixels = 100_000_000
    static let jpegMaxSide = 65_000

    /// Sizes for stacking pictures top to bottom at one shared width (the widest one's), shrunk together if the
    /// stack would be too big for a JPG or for memory. Pure numbers, so it's tested without files.
    public static func stitchPlan(_ sizes: [CGSize]) -> (width: Int, heights: [Int]) {
        let widest = sizes.map(\.width).max() ?? 0
        guard widest > 0 else { return (0, []) }
        let tall = sizes.map { $0.width > 0 ? $0.height * widest / $0.width : 0 }
        let total = tall.reduce(0, +)
        let shrink = min(1, Double(jpegMaxSide) / Double(max(total, 1)), Double(jpegMaxSide) / Double(widest),
                         (Double(stitchMaxPixels) / Double(max(widest * total, 1))).squareRoot())
        return (max(1, Int((widest * shrink).rounded(.down))), tall.map { max(1, Int(shrink < 1 ? ($0 * shrink).rounded(.down) : $0.rounded())) })
    }

    /// Several pictures become one tall JPG, top to bottom in the order given: the long "scrollable" picture that
    /// upload forms and chats want instead of three separate pages.
    public static func stitch(_ urls: [URL], quality: Double, dir: URL) throws -> URL {
        guard urls.count > 1 else { throw DragShiftError("Pick two or more pictures to stitch.") }
        let plan = stitchPlan(try urls.map { url in
            guard let size = pixelSize(url) else { throw DragShiftError("Couldn't open \(url.lastPathComponent) as an image.") }
            return size
        })
        let height = plan.heights.reduce(0, +)
        guard let ctx = context(plan.width, height, opaque: true) else { throw DragShiftError("Not enough memory to stitch these pictures.") }
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: plan.width, height: height))
        ctx.interpolationQuality = .high
        var top = 0
        for (url, h) in zip(urls, plan.heights) {
            // Decoded no bigger than it will be drawn, one at a time, so a stack of phone photos stays light.
            let img = try load(url, maxPixel: max(plan.width, h))
            ctx.draw(img, in: CGRect(x: 0, y: height - top - h, width: plan.width, height: h))
            top += h
        }
        guard let out = ctx.makeImage() else { throw DragShiftError("Couldn't stitch these pictures.") }
        let dest = Naming.output(for: urls[0], ext: "jpg", suffix: "(\(urls.count) stitched)", in: dir)
        try write(out, to: dest, format: .jpg, quality: quality)
        return dest
    }

    /// Animated GIF from still images (one every `delay` seconds, looping forever).
    public static func makeGIF(_ images: [CGImage], to out: URL, delay: Double) throws {
        guard !images.isEmpty else { throw DragShiftError("No frames to put in the GIF.") }
        try Naming.atomically(out) { tmp in
            guard let dest = CGImageDestinationCreateWithURL(tmp as CFURL, UTType.gif.identifier as CFString, images.count, nil) else {
                throw DragShiftError("Couldn't create the GIF.")
            }
            CGImageDestinationSetProperties(dest, [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]] as CFDictionary)
            let frame = [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: delay,
                                                         kCGImagePropertyGIFUnclampedDelayTime: delay]] as CFDictionary
            for img in images { CGImageDestinationAddImage(dest, img, frame) }
            guard CGImageDestinationFinalize(dest) else { throw DragShiftError("Couldn't save the GIF.") }
        }
    }
}
