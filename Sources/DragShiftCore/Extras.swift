import AppKit
import CoreImage
import CoreText
import Foundation
import ImageIO
import Vision

/// Watermark settings (Settings → Recipes). Size is a fraction of the image's short side.
public struct WatermarkStyle: Codable, Sendable, Equatable {
    public enum Position: String, Codable, Sendable, CaseIterable {
        case bottomRight, bottomLeft, topRight, topLeft, center, tiled
        public var title: String {
            switch self {
            case .bottomRight: return "Bottom right"
            case .bottomLeft: return "Bottom left"
            case .topRight: return "Top right"
            case .topLeft: return "Top left"
            case .center: return "Centre"
            case .tiled: return "All over"
            }
        }
    }

    public var text: String
    public var logoPath: String?
    public var position: Position
    public var size: Double
    public var opacity: Double

    public init(text: String = "© \(NSFullUserName())", logoPath: String? = nil, position: Position = .bottomRight,
                size: Double = 0.045, opacity: Double = 0.8) {
        self.text = text; self.logoPath = logoPath; self.position = position; self.size = size; self.opacity = opacity
    }
}

extension ImageOps {
    /// Crops the middle to the target shape, then scales to exactly `size` (Instagram 4:5, square...).
    public static func fill(_ img: CGImage, to size: CGSize) -> CGImage {
        let W = Double(img.width), H = Double(img.height)
        let target = size.width / size.height
        var crop = CGRect(x: 0, y: 0, width: W, height: H)
        if W / H > target { crop.size.width = H * target; crop.origin.x = (W - crop.width) / 2 }
        else { crop.size.height = W / target; crop.origin.y = (H - crop.height) / 2 }
        guard let cut = img.cropping(to: crop.integral) else { return img }
        return scaled(cut, to: size)
    }

    /// Whole picture inside `size`, with a blurred copy of itself filling the edges (Stories, Reels covers).
    public static func fitStory(_ img: CGImage, to size: CGSize) -> CGImage {
        let style = BackdropStyle(fill: .blurredSelf, padding: 0, corner: 0.02, shadow: 0.025, ratio: .auto)
        let W = Int(size.width), H = Int(size.height)
        guard let ctx = context(W, H, space: sRGB, opaque: true),
              let bg = Backdrop.blurredCover(img, size: size) else { return img }
        ctx.draw(bg, in: CGRect(x: 0, y: 0, width: W, height: H))
        let margin = 0.06 * Double(min(W, H))
        let s = min((Double(W) - 2 * margin) / Double(img.width), (Double(H) - 2 * margin) / Double(img.height))
        let w = Double(img.width) * s, h = Double(img.height) * s
        let r = CGRect(x: (Double(W) - w) / 2, y: (Double(H) - h) / 2, width: w, height: h)
        let radius = style.corner * max(w, h)
        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: -h * 0.01), blur: style.shadow * max(w, h),
                      color: CGColor(srgbRed: 0, green: 0, blue: 0, alpha: 0.35))
        ctx.addPath(CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil))
        ctx.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
        ctx.fillPath()
        ctx.restoreGState()
        ctx.saveGState()
        ctx.addPath(CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil))
        ctx.clip()
        ctx.interpolationQuality = .high
        ctx.draw(img, in: r)
        ctx.restoreGState()
        return ctx.makeImage() ?? img
    }

    /// Text and/or logo stamped on the picture, with a soft shadow so it reads on any background.
    public static func watermarked(_ img: CGImage, style: WatermarkStyle) -> CGImage {
        let W = Double(img.width), H = Double(img.height)
        guard let ctx = context(img.width, img.height, space: img.colorSpace, opaque: !hasAlphaChannel(img)) else { return img }
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: W, height: H))
        let short = min(W, H)
        let margin = short * 0.035
        let logo = style.logoPath.flatMap { try? load(URL(fileURLWithPath: $0), maxPixel: 1600) }

        // Build the mark: logo (if any) then text, side by side.
        let fontSize = max(10, short * style.size)
        let font = CTFontCreateWithName("Helvetica-Bold" as CFString, fontSize, nil)
        let text = style.text.trimmingCharacters(in: .whitespaces)
        let line = text.isEmpty ? nil : CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [
            .font: font, .foregroundColor: NSColor.white,
        ]))
        var textW = 0.0, ascent: CGFloat = 0, descent: CGFloat = 0
        if let line { textW = CTLineGetTypographicBounds(line, &ascent, &descent, nil) }
        let logoH = logo == nil ? 0 : fontSize * 1.6
        let logoW = logo.map { logoH * Double($0.width) / Double($0.height) } ?? 0
        let gap = logo != nil && line != nil ? fontSize * 0.4 : 0
        let markW = logoW + gap + textW
        let markH = max(logoH, Double(ascent + descent))
        guard markW > 0 else { return img }

        func draw(at origin: CGPoint) {
            if let logo {
                ctx.draw(logo, in: CGRect(x: origin.x, y: origin.y + (markH - logoH) / 2, width: logoW, height: logoH))
            }
            if let line {
                ctx.textPosition = CGPoint(x: origin.x + logoW + gap, y: origin.y + (markH - Double(ascent + descent)) / 2 + Double(descent))
                CTLineDraw(line, ctx)
            }
        }

        ctx.saveGState()
        ctx.setAlpha(style.opacity)
        ctx.setShadow(offset: CGSize(width: 0, height: -fontSize * 0.06), blur: fontSize * 0.25,
                      color: CGColor(srgbRed: 0, green: 0, blue: 0, alpha: 0.55))
        switch style.position {
        case .tiled:
            ctx.setAlpha(style.opacity * 0.35)
            ctx.translateBy(x: W / 2, y: H / 2)
            ctx.rotate(by: .pi / 6)
            let stepX = markW + fontSize * 4, stepY = markH + fontSize * 3.5
            let reach = hypot(W, H) / 2
            var y = -reach
            var row = 0
            while y < reach {
                var x = -reach + (row % 2 == 0 ? 0 : stepX / 2)
                while x < reach { draw(at: CGPoint(x: x, y: y)); x += stepX }
                y += stepY; row += 1
            }
        default:
            let x: Double, y: Double
            switch style.position {
            case .bottomLeft: x = margin; y = margin
            case .topRight: x = W - margin - markW; y = H - margin - markH
            case .topLeft: x = margin; y = H - margin - markH
            case .center: x = (W - markW) / 2; y = (H - markH) / 2
            default: x = W - margin - markW; y = margin
            }
            draw(at: CGPoint(x: x, y: y))
        }
        ctx.restoreGState()
        return ctx.makeImage() ?? img
    }
}

/// QR codes: read them out of pictures, or make one from a link or short text.
public enum QR {
    public static func read(_ img: CGImage) throws -> [String] {
        let req = VNDetectBarcodesRequest()
        try VNImageRequestHandler(cgImage: img, options: [:]).perform([req])
        var seen = Set<String>()
        return (req.results ?? []).compactMap(\.payloadStringValue).filter { seen.insert($0).inserted }
    }

    public static func make(_ text: String, side: Int = 1024) throws -> CGImage {
        let data = Data(text.utf8)
        guard data.count <= 2_900 else { throw DragShiftError("That's too much text for a QR code (keep it under ~2,900 letters).") }
        guard let f = CIFilter(name: "CIQRCodeGenerator") else { throw DragShiftError("QR codes aren't available.") }
        f.setValue(data, forKey: "inputMessage")
        f.setValue("M", forKey: "inputCorrectionLevel")
        guard let code = f.outputImage else { throw DragShiftError("Couldn't make the QR code.") }
        // Quiet zone of 4 modules, then scale up with hard edges.
        let modules = code.extent.width
        let total = modules + 8
        let scale = (Double(side) / total).rounded(.down)
        let framed = code.transformed(by: CGAffineTransform(translationX: 4, y: 4))
            .composited(over: CIImage(color: .white).cropped(to: CGRect(x: 0, y: 0, width: total, height: total)))
            .samplingNearest()
            .transformed(by: CGAffineTransform(scaleX: scale, y: scale))
        guard let out = CIContext().createCGImage(framed, from: framed.extent, format: .RGBA8, colorSpace: ImageOps.sRGB) else {
            throw DragShiftError("Couldn't make the QR code.")
        }
        return out
    }

    /// File name for a QR made from `text`: the web site name, or the first few words.
    public static func name(for text: String) -> String {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if let u = URL(string: t), let host = u.host { return "QR \(host.replacingOccurrences(of: "www.", with: ""))" }
        let words = t.split(whereSeparator: \.isWhitespace).prefix(4).joined(separator: " ")
        let safe = words.replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: "-")
        return "QR \(safe.prefix(40))"
    }
}

/// On-device translation needs macOS 14.4+ (the app shows Apple's translation panel).
public enum Translator {
    public static var available: Bool {
        if #available(macOS 14.4, *) { return true }
        return false
    }
}
