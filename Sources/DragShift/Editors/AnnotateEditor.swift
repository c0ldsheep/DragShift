import AppKit
import DragShiftCore
import SwiftUI

/// Arrows, boxes, highlighter, pen, text and numbered steps on a picture.
/// Marks are kept in image pixels, so the saved file is drawn at full resolution.
struct Mark: Identifiable {
    enum Tool: String, CaseIterable {
        case arrow, box, highlight, pen, text, number
        var symbol: String {
            switch self {
            case .arrow: return "arrow.up.right"
            case .box: return "rectangle"
            case .highlight: return "highlighter"
            case .pen: return "scribble"
            case .text: return "textformat"
            case .number: return "1.circle"
            }
        }
        var title: String { rawValue.capitalized }
    }

    let id = UUID()
    var tool: Tool
    var color: NSColor
    var width: CGFloat
    var points: [CGPoint]
    var text = ""

    /// Draws into the current NSGraphicsContext, which must be flipped (y grows downward).
    func draw() {
        guard let first = points.first else { return }
        let last = points.last ?? first
        switch tool {
        case .pen:
            let p = NSBezierPath()
            p.lineWidth = width
            p.lineCapStyle = .round
            p.lineJoinStyle = .round
            p.move(to: first)
            for pt in points.dropFirst() { p.line(to: pt) }
            color.setStroke()
            p.stroke()
        case .arrow:
            let p = NSBezierPath()
            p.lineWidth = width
            p.lineCapStyle = .round
            p.move(to: first)
            p.line(to: last)
            color.setStroke()
            p.stroke()
            let angle = atan2(last.y - first.y, last.x - first.x)
            let head = max(width * 4, 12)
            let head1 = CGPoint(x: last.x - head * cos(angle - .pi / 7), y: last.y - head * sin(angle - .pi / 7))
            let head2 = CGPoint(x: last.x - head * cos(angle + .pi / 7), y: last.y - head * sin(angle + .pi / 7))
            let tip = NSBezierPath()
            tip.move(to: last); tip.line(to: head1); tip.line(to: head2); tip.close()
            color.setFill()
            tip.fill()
        case .box:
            let r = CGRect(x: min(first.x, last.x), y: min(first.y, last.y), width: abs(last.x - first.x), height: abs(last.y - first.y))
            let p = NSBezierPath(roundedRect: r, xRadius: width, yRadius: width)
            p.lineWidth = width
            color.setStroke()
            p.stroke()
        case .highlight:
            let r = CGRect(x: min(first.x, last.x), y: min(first.y, last.y), width: abs(last.x - first.x), height: abs(last.y - first.y))
            color.withAlphaComponent(0.35).setFill()
            NSBezierPath(rect: r).fill()
        case .text:
            let font = NSFont.systemFont(ofSize: width * 6, weight: .bold)
            let shadow = NSShadow()
            shadow.shadowBlurRadius = width
            shadow.shadowColor = NSColor.black.withAlphaComponent(0.45)
            NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color, .shadow: shadow]).draw(at: first)
        case .number:
            let r = width * 5
            color.setFill()
            NSBezierPath(ovalIn: CGRect(x: first.x - r, y: first.y - r, width: 2 * r, height: 2 * r)).fill()
            let font = NSFont.systemFont(ofSize: r * 1.15, weight: .heavy)
            let s = NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: NSColor.white])
            let size = s.size()
            s.draw(at: CGPoint(x: first.x - size.width / 2, y: first.y - size.height / 2))
        }
    }
}

struct AnnotateEditor: View {
    let file: URL
    let options: Options

    @ViewState private var image: NSImage?
    @ViewState private var pixel = CGSize(width: 1, height: 1)
    @ViewState private var marks: [Mark] = []
    @ViewState private var current: Mark?
    @ViewState private var tool: Mark.Tool = .arrow
    @ViewState private var color: NSColor = .systemRed
    @ViewState private var size = 1
    @ViewState private var typing: CGPoint?
    @ViewState private var typed = ""

    private let colors: [NSColor] = [.systemRed, .systemOrange, .systemYellow, .systemGreen, .systemBlue, .white, .black]

    private var width: CGFloat {
        let base = max(pixel.width, pixel.height)
        return base * [0.004, 0.007, 0.012][size]
    }

    var body: some View {
        VStack(spacing: 12) {
            toolbar
            GeometryReader { geo in
                let fit = CropEditor.fit(pixel, in: geo.size)
                let scale = fit.width / max(1, pixel.width)
                ZStack(alignment: .topLeading) {
                    if let image {
                        Image(nsImage: image).resizable().frame(width: fit.width, height: fit.height).offset(x: fit.minX, y: fit.minY)
                    }
                    Canvas { ctx, _ in
                        ctx.withCGContext { cg in
                            NSGraphicsContext.saveGraphicsState()
                            NSGraphicsContext.current = NSGraphicsContext(cgContext: cg, flipped: true)
                            cg.translateBy(x: fit.minX, y: fit.minY)
                            cg.scaleBy(x: scale, y: scale)
                            for m in marks { m.draw() }
                            current?.draw()
                            NSGraphicsContext.restoreGraphicsState()
                        }
                    }
                    .allowsHitTesting(false)
                    if let t = typing {
                        TextField("Type, then press Return", text: $typed)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 220)
                            .position(x: fit.minX + t.x * scale + 110, y: fit.minY + t.y * scale)
                            .onSubmit { commitText() }
                    }
                }
                .frame(width: geo.size.width, height: geo.size.height)
                .contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance: 0)
                    .onChanged { v in changed(v, fit: fit, scale: scale) }
                    .onEnded { v in ended(v, fit: fit, scale: scale) })
            }
            .background(Color.black.opacity(0.06), in: RoundedRectangle(cornerRadius: 10))
            HStack {
                Text(hint).font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("Undo") { if !marks.isEmpty { marks.removeLast() } }.keyboardShortcut("z", modifiers: .command)
                    .disabled(marks.isEmpty)
                Button("Clear") { marks.removeAll() }.disabled(marks.isEmpty)
                Button("Cancel") { Windows.close("annotate") }.keyboardShortcut(.cancelAction)
                Button("Save") { save() }.buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction).disabled(marks.isEmpty)
            }
        }
        .padding(16)
        .padding(.top, 14)
        .task { load() }
    }

    private var toolbar: some View {
        HStack(spacing: 6) {
            ForEach(Mark.Tool.allCases, id: \.self) { t in
                Button { tool = t; commitText() } label: {
                    Image(systemName: t.symbol).frame(width: 30, height: 26)
                }
                .buttonStyle(.plain)
                .foregroundStyle(tool == t ? Palette.onHot : Color.primary)
                .background { if tool == t { RoundedRectangle(cornerRadius: 7).fill(Palette.hot) } }
                .help(t.title)
            }
            Divider().frame(height: 22).padding(.horizontal, 6)
            ForEach(Array(colors.enumerated()), id: \.offset) { _, c in
                Circle().fill(Color(nsColor: c))
                    .frame(width: 20, height: 20)
                    .overlay(Circle().strokeBorder(color == c ? Palette.hotB : Color.black.opacity(0.2), lineWidth: color == c ? 3 : 1))
                    .onTapGesture { color = c }
            }
            Divider().frame(height: 22).padding(.horizontal, 6)
            Picker("", selection: $size) { Text("S").tag(0); Text("M").tag(1); Text("L").tag(2) }
                .pickerStyle(.segmented).labelsHidden().frame(width: 100)
            Spacer()
        }
    }

    private var hint: String {
        switch tool {
        case .text: return "Click where the text goes, type, press Return."
        case .number: return "Click to drop numbered steps: 1, 2, 3…"
        case .pen: return "Draw freely."
        default: return "Drag on the picture."
        }
    }

    private func toImage(_ p: CGPoint, _ fit: CGRect, _ scale: CGFloat) -> CGPoint {
        CGPoint(x: (p.x - fit.minX) / scale, y: (p.y - fit.minY) / scale)
    }

    private func changed(_ v: DragGesture.Value, fit: CGRect, scale: CGFloat) {
        guard tool != .text && tool != .number else { return }
        let p = toImage(v.location, fit, scale)
        if current == nil {
            current = Mark(tool: tool, color: color, width: width, points: [toImage(v.startLocation, fit, scale)])
        }
        if tool == .pen { current?.points.append(p) } else { current?.points = [current?.points.first ?? p, p] }
    }

    private func ended(_ v: DragGesture.Value, fit: CGRect, scale: CGFloat) {
        let p = toImage(v.location, fit, scale)
        switch tool {
        case .text:
            commitText()
            typing = p
            typed = ""
        case .number:
            let n = marks.filter { $0.tool == .number }.count + 1
            marks.append(Mark(tool: .number, color: color, width: width, points: [p], text: "\(n)"))
        default:
            if var m = current, let a = m.points.first, hypot(p.x - a.x, p.y - a.y) > 3 || tool == .pen {
                if tool != .pen { m.points = [a, p] }
                marks.append(m)
            }
        }
        current = nil
    }

    private func commitText() {
        if let t = typing, !typed.trimmingCharacters(in: .whitespaces).isEmpty {
            marks.append(Mark(tool: .text, color: color, width: width, points: [t], text: typed))
        }
        typing = nil
        typed = ""
    }

    private func load() {
        pixel = ImageOps.pixelSize(file) ?? pixel
        if let cg = try? ImageOps.load(file, maxPixel: 2000) {
            image = NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
        }
    }

    private func save() {
        commitText()
        let marks = marks, f = file, o = options
        Editors.work("Annotating", count: 1, close: "annotate") {
            let out = try Self.render(marks, on: try ImageOps.load(f))
            return [try ImageOps.saveEdited(out, like: f, suffix: "Annotated", dir: Naming.folder(for: f, override: o.outputFolder),
                                            quality: o.quality)]
        }
    }

    /// Draws the marks onto the full-size picture (marks are in its pixels, top-left origin).
    static func render(_ marks: [Mark], on img: CGImage) throws -> CGImage {
        let w = img.width, h = img.height
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                                         hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
              let ctx = NSGraphicsContext(bitmapImageRep: rep) else { throw DragShiftError("Out of memory.") }
        NSGraphicsContext.saveGraphicsState()
        defer { NSGraphicsContext.restoreGraphicsState() }
        ctx.cgContext.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        // Flip so y grows downward, like on screen.
        ctx.cgContext.translateBy(x: 0, y: CGFloat(h))
        ctx.cgContext.scaleBy(x: 1, y: -1)
        NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx.cgContext, flipped: true)
        for m in marks { m.draw() }
        guard let out = rep.cgImage else { throw DragShiftError("Couldn't draw the marks.") }
        return out
    }
}
