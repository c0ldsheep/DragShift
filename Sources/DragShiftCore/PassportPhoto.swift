import CoreGraphics
import Foundation
import Vision

/// What a passport-size or form photo must look like. Default: 3.5 × 4.5 cm on white, under 50 KB
/// (Indian passport size, and what SSC, IBPS and most job and exam portals ask for).
public struct PhotoSpec: Codable, Sendable, Equatable {
    public enum Background: String, Codable, Sendable, CaseIterable {
        case white, lightBlue, keep
        public var title: String {
            switch self {
            case .white: return "White"
            case .lightBlue: return "Light blue"
            case .keep: return "Keep the original"
            }
        }
        var color: CGColor? {
            switch self {
            case .white: return CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1)
            case .lightBlue: return CGColor(srgbRed: 0.85, green: 0.92, blue: 1, alpha: 1)
            case .keep: return nil
            }
        }
    }

    public var widthMM: Double = 35
    public var heightMM: Double = 45
    /// 0 = no size limit.
    public var maxKB: Int = 50
    public var background: Background = .white
    public var dpi: Double = 300
    /// Head (crown to chin) as a share of the photo's height: passports ask 70–80%, US visas 50–69%.
    public var head: Double = 0.72

    public init() {}
    public init(widthMM: Double, heightMM: Double, maxKB: Int, head: Double = 0.72, background: Background = .white) {
        self.widthMM = widthMM; self.heightMM = heightMM; self.maxKB = maxKB; self.head = head; self.background = background
    }

    public var pixelWidth: Int { Int((widthMM / 25.4 * dpi).rounded()) }
    public var pixelHeight: Int { Int((heightMM / 25.4 * dpi).rounded()) }

    /// "3.5 × 4.5 cm" or "2 × 2 in".
    public var sizeTitle: String {
        let inches = abs(widthMM - 50.8) < 0.1 && abs(heightMM - 50.8) < 0.1
        if inches { return "2 × 2 in" }
        func cm(_ v: Double) -> String {
            let c = v / 10
            return c == c.rounded() ? String(Int(c)) : String(format: "%.1f", c)
        }
        return "\(cm(widthMM)) × \(cm(heightMM)) cm"
    }

    public static let presets: [(String, PhotoSpec)] = [
        ("Passport size · 3.5 × 4.5 cm", PhotoSpec()),
        ("US visa · 2 × 2 in", PhotoSpec(widthMM: 50.8, heightMM: 50.8, maxKB: 240, head: 0.6)),
        ("Square · 3.5 × 3.5 cm", PhotoSpec(widthMM: 35, heightMM: 35, maxKB: 50, head: 0.62)),
    ]
}

public enum PassportPhoto {
    /// Space above the head, as a share of the photo's height.
    static func topShare(_ spec: PhotoSpec) -> Double { spec.head >= 0.7 ? 0.08 : 0.1 }
    /// Vision's face box runs from the brows to the chin; the top of the hair sits about this far
    /// above it (measured 0.51–0.54 on real portraits; a little extra keeps hair off the edge).
    static let crownAbove = 0.55

    /// Finds the biggest face in the picture (pixels, top-left origin).
    public static func face(in img: CGImage) throws -> CGRect {
        let req = VNDetectFaceRectanglesRequest()
        try VNImageRequestHandler(cgImage: img, options: [:]).perform([req])
        let W = Double(img.width), H = Double(img.height)
        guard let best = (req.results ?? []).max(by: { $0.boundingBox.width * $0.boundingBox.height
                                                         < $1.boundingBox.width * $1.boundingBox.height }) else {
            throw DragShiftError("Couldn't find a face. Use a photo where the face is clear and looking at the camera.")
        }
        let b = best.boundingBox
        return CGRect(x: b.minX * W, y: (1 - b.maxY) * H, width: b.width * W, height: b.height * H)
    }

    /// A line farther above the brows than this isn't hair: it's a shelf, a picture frame or a doorway.
    static let maxCrownAbove = 1.2

    /// The part of the picture that becomes the photo (pixels, top-left origin). It can reach past
    /// the picture's edges; that part is filled with the background colour.
    /// `measuredCrown` is where the hair really ends (see `crown(in:face:)`). Only a higher line than the usual guess
    /// counts (tall hair, a turban, a hat); a lower one never takes away room above the head.
    public static func frame(face f: CGRect, spec: PhotoSpec, measuredCrown: Double? = nil) -> CGRect {
        var crown = f.minY - f.height * crownAbove
        if let m = measuredCrown, !m.isNaN, m < crown { crown = max(m, f.minY - f.height * maxCrownAbove) }
        let head = f.maxY - crown
        let h = head / spec.head
        let w = h * spec.widthMM / spec.heightMM
        return CGRect(x: f.midX - w / 2, y: crown - h * topShare(spec), width: w, height: h)
    }

    /// The top of the hair: the highest row of the person's cut-out that is joined to the face. It walks up from
    /// the brows row by row and stops at the first real gap, so a poster or a shelf above the head doesn't count.
    /// `face` is in the mask's own pixels (top-left origin). nil when the cut-out doesn't cover the face.
    static func crown(in mask: VisionOps.Mask, face f: CGRect) -> Double? {
        guard mask.width > 0, mask.height > 0, f.width >= 4, f.height >= 4 else { return nil }
        let x0 = max(0, Int(f.minX - f.width * 0.6)), x1 = min(mask.width, Int(f.maxX + f.width * 0.6))
        let run = max(2, Int(f.width * 0.08))        // thinner than this is a stray hair, a finger, an edge
        let gap = max(1, Int(f.height * 0.03))       // a shine in the hair may break the cut-out for a row or two
        func solid(_ y: Int) -> Bool {
            var n = 0
            for x in x0..<x1 {
                n = mask.alpha[y * mask.width + x] >= 128 ? n + 1 : 0
                if n >= run { return true }
            }
            return false
        }
        let start = min(mask.height - 1, Int(f.minY + f.height * 0.15))
        guard x1 - x0 >= run, start >= 0, solid(start) else { return nil }
        var top = start, missing = 0, y = start - 1
        while y >= 0 && missing <= gap {
            if solid(y) { top = y; missing = 0 } else { missing += 1 }
            y -= 1
        }
        return Double(top)
    }

    /// Where the hair ends in `img` (pixels, top-left origin), read from the person's cut-out on a small copy.
    @available(macOS 14, *)
    static func measuredCrown(_ img: CGImage, face f: CGRect) -> Double? {
        let k = min(1, 1024 / Double(max(img.width, img.height)))
        let small = k < 1 ? ImageOps.scaled(img, to: CGSize(width: Double(img.width) * k, height: Double(img.height) * k)) : img
        let kx = Double(small.width) / Double(img.width), ky = Double(small.height) / Double(img.height)
        let face = CGRect(x: f.minX * kx, y: f.minY * ky, width: f.width * kx, height: f.height * ky)
        guard let mask = try? VisionOps.subjectMask(small, at: CGPoint(x: face.midX, y: face.midY)),
              let row = crown(in: mask, face: CGRect(x: face.minX * Double(mask.width) / Double(small.width),
                                                     y: face.minY * Double(mask.height) / Double(small.height),
                                                     width: face.width * Double(mask.width) / Double(small.width),
                                                     height: face.height * Double(mask.height) / Double(small.height)))
        else { return nil }
        return row * Double(small.height) / Double(mask.height) / ky
    }

    public struct Result {
        public let image: CGImage
        /// Set when the background couldn't be replaced (older macOS, or no clear person found).
        public let note: String?
    }

    /// Crops around the face to the spec's shape and size, and puts the person on the chosen colour.
    /// `face` and `crown` are for tests; normally the face is found with Vision and the top of the hair is measured.
    public static func make(_ img: CGImage, spec: PhotoSpec, face known: CGRect? = nil, crown knownCrown: Double? = nil) throws -> Result {
        let f = try known ?? face(in: img)
        var crown = knownCrown
        if known == nil, #available(macOS 14, *) { crown = measuredCrown(img, face: f) }
        let frame = frame(face: f, spec: spec, measuredCrown: crown)
        let bounds = CGRect(x: 0, y: 0, width: img.width, height: img.height)
        let visible = frame.intersection(bounds).integral
        guard visible.width >= 8, visible.height >= 8, var part = img.cropping(to: visible) else {
            throw DragShiftError("The face is too close to the edge of the photo.")
        }
        // Only what ends up in the photo, at most twice the final size: much faster to cut out.
        let outW = spec.pixelWidth, outH = spec.pixelHeight
        let scale = Double(outH) / frame.height
        let keep = min(1, 2 * scale)
        if keep < 1 {
            part = ImageOps.scaled(part, to: CGSize(width: Double(part.width) * keep, height: Double(part.height) * keep))
        }

        var note: String?
        let fill = spec.background.color
        if fill != nil {
            if #available(macOS 14, *) {
                do { part = try VisionOps.removeBackground(part) } catch {
                    note = "Background kept: couldn't tell the person apart from it."
                }
            } else {
                note = "Background kept: changing it needs macOS 14 or newer."
            }
        }

        guard let ctx = ImageOps.context(outW, outH, space: ImageOps.sRGB, opaque: true) else { throw DragShiftError("Out of memory.") }
        ctx.setFillColor(fill ?? CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: outW, height: outH))
        ctx.interpolationQuality = .high
        // `visible` in photo pixels; CoreGraphics counts y from the bottom.
        let x = (visible.minX - frame.minX) * scale
        let top = (visible.minY - frame.minY) * scale
        let w = visible.width * scale, h = visible.height * scale
        ctx.draw(part, in: CGRect(x: x, y: Double(outH) - top - h, width: w, height: h))
        guard let out = ctx.makeImage() else { throw DragShiftError("Out of memory.") }
        return Result(image: out, note: note)
    }

    /// "Photo Passport.jpg", printed at the right size (DPI is stored) and under the spec's KB limit.
    public static func save(_ url: URL, spec: PhotoSpec, dir: URL, face: CGRect? = nil) throws -> (url: URL, note: String?) {
        let r = try make(try ImageOps.load(url), spec: spec, face: face)
        let data = spec.maxKB > 0
            ? try SizeLimit.jpeg(r.image, maxBytes: SizeLimit.bytes(kb: spec.maxKB), dpi: spec.dpi)
            : try SizeLimit.encodeJPEG(r.image, quality: 0.92, dpi: spec.dpi)
        let out = Naming.output(for: url, ext: "jpg", suffix: "Passport", in: dir)
        try Naming.atomically(out) { tmp in try data.write(to: tmp) }
        return (out, r.note)
    }
}
