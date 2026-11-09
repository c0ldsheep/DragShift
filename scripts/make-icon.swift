// Draws the DragShift app icon at every size macOS wants and writes an .iconset folder.
// Usage: swift scripts/make-icon.swift <out.iconset>
import AppKit

let out = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "AppIcon.iconset")
try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)

func draw(_ px: Int) -> Data {
    let s = CGFloat(px)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let ctx = NSGraphicsContext.current!.cgContext

    // macOS icon grid: 824/1024 tile with a 185/1024 corner radius.
    let tile = CGRect(x: s * 100 / 1024, y: s * 100 / 1024, width: s * 824 / 1024, height: s * 824 / 1024)
    let radius = s * 185 / 1024
    let tilePath = CGPath(roundedRect: tile, cornerWidth: radius, cornerHeight: radius, transform: nil)

    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -s * 0.012), blur: s * 0.03, color: NSColor.black.withAlphaComponent(0.3).cgColor)
    ctx.addPath(tilePath)
    ctx.setFillColor(NSColor.black.cgColor)
    ctx.fillPath()
    ctx.restoreGState()

    ctx.saveGState()
    ctx.addPath(tilePath)
    ctx.clip()
    let colors = [NSColor(srgbRed: 1, green: 0.66, blue: 0.3, alpha: 1).cgColor,
                  NSColor(srgbRed: 1, green: 0.27, blue: 0.45, alpha: 1).cgColor,
                  NSColor(srgbRed: 0.55, green: 0.38, blue: 1, alpha: 1).cgColor] as CFArray
    let g = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: colors, locations: [0, 0.55, 1])!
    ctx.drawLinearGradient(g, start: CGPoint(x: tile.minX, y: tile.maxY), end: CGPoint(x: tile.maxX, y: tile.minY), options: [])
    // Soft light from the top.
    let glow = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                          colors: [NSColor.white.withAlphaComponent(0.28).cgColor, NSColor.white.withAlphaComponent(0).cgColor] as CFArray,
                          locations: [0, 1])!
    ctx.drawRadialGradient(glow, startCenter: CGPoint(x: tile.midX, y: tile.maxY), startRadius: 0,
                           endCenter: CGPoint(x: tile.midX, y: tile.maxY), endRadius: tile.width * 0.9, options: [])

    // The ring: six petals, one lit, and a hub.
    let c = CGPoint(x: tile.midX, y: tile.midY)
    let ro = tile.width * 0.36, ri = tile.width * 0.17
    let gap = tile.width * 0.022
    for i in 0..<6 {
        let span = CGFloat.pi / 3
        let mid = CGFloat.pi / 2 - CGFloat(i) * span
        let ao = asin(gap / ro), ai = asin(gap / ri)
        let p = CGMutablePath()
        p.addArc(center: c, radius: ro, startAngle: mid + span / 2 - ao, endAngle: mid - span / 2 + ao, clockwise: true)
        p.addArc(center: c, radius: ri, startAngle: mid - span / 2 + ai, endAngle: mid + span / 2 - ai, clockwise: false)
        p.closeSubpath()
        ctx.saveGState()
        if i == 1 {
            ctx.setShadow(offset: CGSize(width: 0, height: -s * 0.006), blur: s * 0.02, color: NSColor.black.withAlphaComponent(0.25).cgColor)
            ctx.addPath(p)
            ctx.setFillColor(NSColor.white.cgColor)
        } else {
            ctx.addPath(p)
            ctx.setFillColor(NSColor.white.withAlphaComponent(0.42).cgColor)
        }
        ctx.fillPath()
        ctx.restoreGState()
    }
    ctx.setShadow(offset: CGSize(width: 0, height: -s * 0.006), blur: s * 0.02, color: NSColor.black.withAlphaComponent(0.2).cgColor)
    ctx.setFillColor(NSColor.white.withAlphaComponent(0.95).cgColor)
    ctx.fillEllipse(in: CGRect(x: c.x - ri * 0.72, y: c.y - ri * 0.72, width: ri * 1.44, height: ri * 1.44))
    // A tiny page in the hub.
    let pw = ri * 0.62, ph = ri * 0.8
    let page = CGRect(x: c.x - pw / 2, y: c.y - ph / 2, width: pw, height: ph)
    ctx.setShadow(offset: .zero, blur: 0, color: nil)
    ctx.setFillColor(NSColor(srgbRed: 1, green: 0.32, blue: 0.45, alpha: 1).cgColor)
    ctx.addPath(CGPath(roundedRect: page, cornerWidth: pw * 0.16, cornerHeight: pw * 0.16, transform: nil))
    ctx.fillPath()
    ctx.restoreGState()

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

for (name, px) in [("16x16", 16), ("16x16@2x", 32), ("32x32", 32), ("32x32@2x", 64), ("128x128", 128),
                   ("128x128@2x", 256), ("256x256", 256), ("256x256@2x", 512), ("512x512", 512), ("512x512@2x", 1024)] {
    try! draw(px).write(to: out.appendingPathComponent("icon_\(name).png"))
}
print("wrote \(out.path)")
