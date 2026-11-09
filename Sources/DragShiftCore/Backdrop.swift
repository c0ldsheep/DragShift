import CoreImage
import Foundation
import ImageIO

public struct RGBA: Codable, Sendable, Equatable, Hashable {
    public var r, g, b, a: Double
    public init(_ r: Double, _ g: Double, _ b: Double, _ a: Double = 1) { self.r = r; self.g = g; self.b = b; self.a = a }
    public init(hex: UInt32) {
        self.init(Double((hex >> 16) & 0xff) / 255, Double((hex >> 8) & 0xff) / 255, Double(hex & 0xff) / 255)
    }
    public var cgColor: CGColor { CGColor(srgbRed: r, green: g, blue: b, alpha: a) }
    public static let white = RGBA(1, 1, 1)
}

/// "Add BG": the screenshot-beautifier look. Padding, corners and shadow are fractions of the
/// image's longest side, so a preset looks the same on a 600px and a 6000px image.
public struct BackdropStyle: Codable, Sendable, Equatable {
    public enum Fill: Codable, Sendable, Equatable, Hashable {
        case gradient([RGBA], angle: Double)
        case solid(RGBA)
        case blurredSelf
        case image(path: String)
        case transparent
    }

    public enum Ratio: String, Codable, Sendable, CaseIterable {
        case auto, square, wide, portrait, story
        public var title: String {
            switch self {
            case .auto: return "Auto"
            case .square: return "1:1"
            case .wide: return "16:9"
            case .portrait: return "4:5"
            case .story: return "9:16"
            }
        }
        var value: Double? {
            switch self {
            case .auto: return nil
            case .square: return 1
            case .wide: return 16.0 / 9
            case .portrait: return 4.0 / 5
            case .story: return 9.0 / 16
            }
        }
    }

    public var fill: Fill
    public var padding: Double
    public var corner: Double
    public var shadow: Double
    public var ratio: Ratio

    public init(fill: Fill = .gradient([RGBA(hex: 0xFF9A3C), RGBA(hex: 0xFF3D6E), RGBA(hex: 0x9B6BFF)], angle: 135),
                padding: Double = 0.08, corner: Double = 0.025, shadow: Double = 0.03, ratio: Ratio = .auto) {
        self.fill = fill; self.padding = padding; self.corner = corner; self.shadow = shadow; self.ratio = ratio
    }
}

public enum Backdrop {
    /// Final canvas size in pixels for an image of `size`.
    public static func canvasSize(for size: CGSize, style: BackdropStyle) -> CGSize {
        let pad = style.padding * Double(max(size.width, size.height))
        var w = size.width + 2 * pad, h = size.height + 2 * pad
        if let r = style.ratio.value {
            if w / h < r { w = h * r } else { h = w / r }
        }
        return CGSize(width: w.rounded(), height: h.rounded())
    }

    public static func render(_ img: CGImage, style: BackdropStyle) -> CGImage? {
        let size = CGSize(width: img.width, height: img.height)
        let canvas = canvasSize(for: size, style: style)
        let W = Int(canvas.width), H = Int(canvas.height)
        guard let ctx = ImageOps.context(W, H, space: ImageOps.sRGB, opaque: false) else { return nil }
        let full = CGRect(x: 0, y: 0, width: W, height: H)
        ctx.interpolationQuality = .high

        switch style.fill {
        case .solid(let c):
            ctx.setFillColor(c.cgColor)
            ctx.fill(full)
        case .gradient(let colors, let angle):
            drawGradient(ctx, colors: colors, angle: angle, rect: full)
        case .blurredSelf:
            if let bg = blurredCover(img, size: canvas) { ctx.draw(bg, in: full) }
        case .image(let path):
            if let bg = try? ImageOps.load(URL(fileURLWithPath: path)) {
                ctx.draw(bg, in: aspectFill(CGSize(width: bg.width, height: bg.height), into: full))
            }
        case .transparent:
            break
        }

        let longest = Double(max(size.width, size.height))
        let imgRect = CGRect(x: (canvas.width - size.width) / 2, y: (canvas.height - size.height) / 2,
                             width: size.width, height: size.height)
        let radius = min(style.corner * longest, Double(min(size.width, size.height)) / 2)
        let blur = style.shadow * longest
        let see = ImageOps.hasTransparency(img)

        ctx.saveGState()
        if blur > 0.5 {
            ctx.setShadow(offset: CGSize(width: 0, height: -blur * 0.35), blur: blur,
                          color: CGColor(srgbRed: 0, green: 0, blue: 0, alpha: 0.38))
        }
        if see {
            // Cut-out subjects cast their own shadow; no card behind them.
            ctx.draw(img, in: imgRect)
            ctx.restoreGState()
        } else {
            let path = CGPath(roundedRect: imgRect, cornerWidth: radius, cornerHeight: radius, transform: nil)
            ctx.addPath(path)
            ctx.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
            ctx.fillPath()
            ctx.restoreGState()
            ctx.saveGState()
            ctx.addPath(path)
            ctx.clip()
            ctx.draw(img, in: imgRect)
            ctx.restoreGState()
        }
        return ctx.makeImage()
    }

    static func drawGradient(_ ctx: CGContext, colors: [RGBA], angle: Double, rect: CGRect) {
        let cs = colors.isEmpty ? [RGBA.white, RGBA.white] : (colors.count == 1 ? [colors[0], colors[0]] : colors)
        let locs = cs.indices.map { CGFloat($0) / CGFloat(cs.count - 1) }
        guard let g = CGGradient(colorsSpace: ImageOps.sRGB, colors: cs.map(\.cgColor) as CFArray, locations: locs) else { return }
        // Angle like CSS: 0 = bottom->top, 90 = left->right, 135 = top-left -> bottom-right.
        let rad = (angle - 90) * .pi / 180
        let c = CGPoint(x: rect.midX, y: rect.midY)
        let half = (abs(cos(rad)) * rect.width + abs(sin(rad)) * rect.height) / 2
        let dx = cos(rad) * half, dy = -sin(rad) * half
        ctx.drawLinearGradient(g, start: CGPoint(x: c.x - dx, y: c.y - dy), end: CGPoint(x: c.x + dx, y: c.y + dy),
                               options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
    }

    static func aspectFill(_ s: CGSize, into r: CGRect) -> CGRect {
        let scale = max(r.width / s.width, r.height / s.height)
        let w = s.width * scale, h = s.height * scale
        return CGRect(x: r.midX - w / 2, y: r.midY - h / 2, width: w, height: h)
    }

    static func blurredCover(_ img: CGImage, size: CGSize) -> CGImage? {
        let small = ImageOps.fit(img, longEdge: 600)
        let scale = max(size.width / Double(small.width), size.height / Double(small.height))
        var ci = CIImage(cgImage: small).clampedToExtent()
        ci = ci.applyingGaussianBlur(sigma: 28)
            .applyingFilter("CIColorControls", parameters: [kCIInputSaturationKey: 1.25, kCIInputBrightnessKey: -0.06])
            .cropped(to: CGRect(x: 0, y: 0, width: small.width, height: small.height))
            .transformed(by: CGAffineTransform(scaleX: scale, y: scale))
        let w = Double(small.width) * scale, h = Double(small.height) * scale
        let crop = CGRect(x: (w - size.width) / 2, y: (h - size.height) / 2, width: size.width, height: size.height)
        return CIContext().createCGImage(ci, from: crop)
    }
}
