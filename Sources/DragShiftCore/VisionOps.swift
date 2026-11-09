import CoreImage
import CoreVideo
import Foundation
import ImageIO
import Vision

/// On-device AI from Apple's Vision framework. Nothing leaves the Mac.
public enum VisionOps {

    // MARK: Text (OCR)

    public struct TextLine: Sendable {
        public let text: String
        /// Normalised Vision box (0...1, bottom-left origin).
        public let box: CGRect
    }

    /// The accurate reader first; if this Mac can't run it (seen running as Intel under Rosetta: "CRImageReaderError
    /// error 9"), the fast one, which reads clear text just as well. Only if both fail does the person see an error,
    /// and then a plain one.
    public static func recognize(_ img: CGImage) throws -> [VNRecognizedTextObservation] {
        func run(_ level: VNRequestTextRecognitionLevel) throws -> [VNRecognizedTextObservation] {
            let req = VNRecognizeTextRequest()
            req.recognitionLevel = level
            req.usesLanguageCorrection = level == .accurate
            req.automaticallyDetectsLanguage = true
            try VNImageRequestHandler(cgImage: img, options: [:]).perform([req])
            return req.results ?? []
        }
        do { return try run(.accurate) } catch {
            do { return try run(.fast) } catch {
                throw DragShiftError("This Mac couldn't read the text (macOS text recognition isn't working here).")
            }
        }
    }

    /// Lines in reading order (top to bottom, then left to right).
    public static func lines(_ img: CGImage) throws -> [TextLine] {
        let obs = try recognize(img)
        let lines = obs.compactMap { o -> TextLine? in
            guard let s = o.topCandidates(1).first?.string else { return nil }
            return TextLine(text: s, box: o.boundingBox)
        }
        return lines.sorted { a, b in
            let rowTolerance = min(a.box.height, b.box.height) * 0.5
            if abs(a.box.midY - b.box.midY) > rowTolerance { return a.box.midY > b.box.midY }
            return a.box.minX < b.box.minX
        }
    }

    public static func text(_ img: CGImage) throws -> String {
        try lines(img).map(\.text).joined(separator: "\n")
    }

    // MARK: Subject lift

    /// Subject lifting arrived in macOS 14.
    public static var canCutOut: Bool {
        if #available(macOS 14, *) { return true }
        return false
    }

    /// Cuts the main subject out of the photo (same as "Copy Subject" in Photos).
    @available(macOS 14, *)
    public static func removeBackground(_ img: CGImage, cropToSubject: Bool = false) throws -> CGImage {
        let req = VNGenerateForegroundInstanceMaskRequest()
        let handler = VNImageRequestHandler(cgImage: img, options: [:])
        try handler.perform([req])
        guard let result = req.results?.first, !result.allInstances.isEmpty else {
            throw DragShiftError("Couldn't find a clear subject in this photo.")
        }
        let buffer = try result.generateMaskedImage(ofInstances: result.allInstances, from: handler,
                                                    croppedToInstancesExtent: cropToSubject)
        let ci = CIImage(cvPixelBuffer: buffer)
        guard let out = CIContext().createCGImage(ci, from: ci.extent, format: .RGBA8, colorSpace: ImageOps.sRGB) else {
            throw DragShiftError("Couldn't build the cut-out image.")
        }
        return out
    }

    /// Marketplace-ready product photo: background removed, subject centred on pure white,
    /// filling about 85% of a square (what Amazon and Flipkart ask for).
    @available(macOS 14, *)
    public static func shopPhoto(_ img: CGImage, side: Int = 2000, fill: Double = 0.85) throws -> CGImage {
        let subject = try removeBackground(img, cropToSubject: true)
        guard let ctx = ImageOps.context(side, side, space: ImageOps.sRGB, opaque: true) else {
            throw DragShiftError("Out of memory.")
        }
        ctx.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: side, height: side))
        let box = Double(side) * fill
        let s = box / Double(max(subject.width, subject.height))
        let w = Double(subject.width) * s, h = Double(subject.height) * s
        ctx.interpolationQuality = .high
        ctx.draw(subject, in: CGRect(x: (Double(side) - w) / 2, y: (Double(side) - h) / 2, width: w, height: h))
        guard let out = ctx.makeImage() else { throw DragShiftError("Couldn't build the photo.") }
        return out
    }

    // MARK: Redaction finder

    public struct Finding: Sendable, Identifiable {
        public let id = UUID()
        public let label: String
        /// Pixel rect in the upright image, top-left origin.
        public let rect: CGRect
    }

    static let patterns: [(String, String)] = [
        ("Email", #"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"#),
        ("UPI ID", #"\b[A-Za-z0-9.\-_]{2,}@[A-Za-z]{2,}\b"#),
        ("Aadhaar", #"\b[2-9]\d{3}[ \-]?\d{4}[ \-]?\d{4}\b"#),
        ("PAN", #"\b[A-Z]{5}[0-9]{4}[A-Z]\b"#),
        ("IFSC", #"\b[A-Z]{4}0[A-Z0-9]{6}\b"#),
        ("Card", #"\b(?:\d[ \-]?){13,19}\b"#),
        ("Phone", #"(?:\+?91[ \-]?)?\b[6-9]\d{4}[ \-]?\d{5}\b"#),
        ("Phone", #"\+\d{1,3}[ \-]?\(?\d{2,4}\)?[ \-]?\d{3,4}[ \-]?\d{3,4}"#),
        ("Bank account", #"\b\d{9,18}\b"#),
    ]

    /// Finds things people usually want hidden: emails, phone numbers, UPI IDs, Aadhaar, PAN,
    /// card and bank numbers, street addresses, and (optionally) faces.
    public static func findPrivate(_ img: CGImage, faces: Bool = true) throws -> [Finding] {
        let W = Double(img.width), H = Double(img.height)
        func pixelRect(_ n: CGRect) -> CGRect {
            let pad = n.height * H * 0.12
            return CGRect(x: n.minX * W - pad, y: (1 - n.maxY) * H - pad,
                          width: n.width * W + pad * 2, height: n.height * H + pad * 2)
        }
        var found: [Finding] = []
        var taken: [CGRect] = []
        func add(_ label: String, _ r: CGRect) {
            if taken.contains(where: { $0.intersection(r).width * $0.intersection(r).height > 0.6 * r.width * r.height }) { return }
            taken.append(r)
            found.append(Finding(label: label, rect: r))
        }

        let detector = try? NSDataDetector(types: NSTextCheckingResult.CheckingType.address.rawValue
            | NSTextCheckingResult.CheckingType.phoneNumber.rawValue)
        for obs in try recognize(img) {
            guard let cand = obs.topCandidates(1).first else { continue }
            let s = cand.string
            let ns = s as NSString
            var hits: [(String, NSRange)] = []
            for (label, pattern) in patterns {
                guard let re = try? NSRegularExpression(pattern: pattern) else { continue }
                for m in re.matches(in: s, range: NSRange(location: 0, length: ns.length)) {
                    let digits = ns.substring(with: m.range).filter(\.isNumber)
                    if label == "Card" && !luhn(digits) { continue }
                    hits.append((label, m.range))
                }
            }
            for m in detector?.matches(in: s, range: NSRange(location: 0, length: ns.length)) ?? [] {
                hits.append((m.resultType == .address ? "Address" : "Phone", m.range))
            }
            for (label, range) in hits {
                guard let r = Range(range, in: s), let box = try? cand.boundingBox(for: r) else { continue }
                add(label, pixelRect(box.boundingBox))
            }
        }

        if faces {
            let req = VNDetectFaceRectanglesRequest()
            try? VNImageRequestHandler(cgImage: img, options: [:]).perform([req])
            for f in req.results ?? [] {
                let b = f.boundingBox
                add("Face", CGRect(x: b.minX * W, y: (1 - b.maxY) * H, width: b.width * W, height: b.height * H)
                    .insetBy(dx: -b.width * W * 0.1, dy: -b.height * H * 0.15))
            }
        }
        return found
    }

    static func luhn(_ digits: String) -> Bool {
        let ds = digits.compactMap(\.wholeNumberValue)
        guard (13...19).contains(ds.count) else { return false }
        var sum = 0
        for (i, d) in ds.reversed().enumerated() {
            if i % 2 == 1 { let x = d * 2; sum += x > 9 ? x - 9 : x } else { sum += d }
        }
        return sum % 10 == 0
    }

    public enum RedactStyle: String, Codable, Sendable, CaseIterable {
        case box, pixelate, blur
        public var title: String { self == .box ? "Black bar" : (self == .pixelate ? "Pixelate" : "Blur") }
    }

    /// Covers each rect (pixels, top-left origin). The result never carries the original's metadata.
    public static func redact(_ img: CGImage, rects: [CGRect], style: RedactStyle) -> CGImage? {
        let W = img.width, H = img.height
        guard let ctx = ImageOps.context(W, H, space: img.colorSpace, opaque: !ImageOps.hasAlphaChannel(img)) else { return nil }
        let full = CGRect(x: 0, y: 0, width: W, height: H)
        ctx.draw(img, in: full)
        // CoreGraphics is bottom-left origin.
        let flipped = rects.map { CGRect(x: $0.minX, y: Double(H) - $0.maxY, width: $0.width, height: $0.height).intersection(full) }
            .filter { !$0.isEmpty }
        switch style {
        case .box:
            ctx.setFillColor(CGColor(srgbRed: 0.05, green: 0.05, blue: 0.06, alpha: 1))
            for r in flipped { ctx.fill(r) }
        case .pixelate, .blur:
            let ci = CIImage(cgImage: img).clampedToExtent()
            let cictx = CIContext()
            for r in flipped {
                let amount = max(8, min(r.width, r.height) / 5)
                let filtered = style == .pixelate
                    ? ci.applyingFilter("CIPixellate", parameters: [kCIInputScaleKey: amount,
                                                                     kCIInputCenterKey: CIVector(x: r.minX, y: r.minY)])
                    : ci.applyingGaussianBlur(sigma: amount)
                if let patch = cictx.createCGImage(filtered, from: r) { ctx.draw(patch, in: r) }
            }
        }
        return ctx.makeImage()
    }
}
