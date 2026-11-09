import AppKit
import DragShiftCore
import SwiftUI

/// Crop box with corner/edge handles and ratio presets. With several images, the same
/// relative crop is applied to each (handy for a batch of screenshots).
struct CropEditor: View {
    let files: [URL]
    let options: Options

    @ViewState private var image: NSImage?
    @ViewState private var pixel = CGSize(width: 1, height: 1)
    @ViewState private var rect = CGRect(x: 0, y: 0, width: 1, height: 1) // 0...1 of the image
    @ViewState private var ratio: Double? = nil                            // width / height in pixels
    @ViewState private var start: CGRect?
    @ViewState private var grab: Grab = .none
    @ViewState private var wText = ""
    @ViewState private var hText = ""

    enum Grab { case none, move, tl, tr, bl, br, l, r, t, b }

    static let ratios: [(String, Double?)] = [("Free", nil), ("1:1", 1), ("4:5", 0.8), ("16:9", 16.0 / 9), ("9:16", 9.0 / 16),
                                              ("4:3", 4.0 / 3), ("3:4", 0.75)]

    var body: some View {
        VStack(spacing: 14) {
            EditorHeader(files: files, note: files.count > 1 ? "The same crop is applied to all \(files.count) images"
                                                            : "\(Int(pixel.width)) × \(Int(pixel.height)) px")
            GeometryReader { geo in
                let fit = Self.fit(pixel, in: geo.size)
                ZStack(alignment: .topLeading) {
                    if let image {
                        Image(nsImage: image).resizable().frame(width: fit.width, height: fit.height).offset(x: fit.minX, y: fit.minY)
                    }
                    overlay(fit)
                }
                .frame(width: geo.size.width, height: geo.size.height)
                .contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance: 0)
                    .onChanged { v in drag(v, fit: fit) }
                    .onEnded { _ in start = nil; grab = .none; syncFields() })
            }
            .background(Color.black.opacity(0.06), in: RoundedRectangle(cornerRadius: 10))

            HStack(spacing: 4) {
                Text("Aspect ratio").font(.caption).foregroundStyle(.secondary).padding(.trailing, 4)
                ForEach(Self.ratios, id: \.0) { name, value in
                    Button(name) { setRatio(value) }
                        .buttonStyle(.plain)
                        .font(.system(size: 11, weight: .semibold))
                        .padding(.horizontal, 9).padding(.vertical, 4)
                        .foregroundStyle(ratio == value ? Palette.onHot : Color.primary)
                        .background {
                            if ratio == value { Capsule().fill(Palette.hot) } else { Capsule().fill(Color.secondary.opacity(0.12)) }
                        }
                }
                Spacer()
            }

            HStack(spacing: 8) {
                Text("W").font(.caption).foregroundStyle(.secondary)
                TextField("", text: $wText).frame(width: 64).onSubmit { applyFields(width: true) }
                Text("H").font(.caption).foregroundStyle(.secondary)
                TextField("", text: $hText).frame(width: 64).onSubmit { applyFields(width: false) }
                Text("px").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("Reset") { rect = full(for: ratio); syncFields() }
                Button("Cancel") { Windows.close("crop") }.keyboardShortcut(.cancelAction)
                Button("Apply") { apply() }.buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
            }
            .textFieldStyle(.roundedBorder)
        }
        .padding(18)
        .padding(.top, 10)
        .task { load() }
    }

    // MARK: Drawing

    private func overlay(_ fit: CGRect) -> some View {
        let r = CGRect(x: fit.minX + rect.minX * fit.width, y: fit.minY + rect.minY * fit.height,
                       width: rect.width * fit.width, height: rect.height * fit.height)
        return ZStack(alignment: .topLeading) {
            Path { p in
                p.addRect(fit)
                p.addRect(r)
            }
            .fill(Color.black.opacity(0.45), style: FillStyle(eoFill: true))
            Path { p in
                for i in 1...2 {
                    let x = r.minX + r.width * CGFloat(i) / 3, y = r.minY + r.height * CGFloat(i) / 3
                    p.move(to: CGPoint(x: x, y: r.minY)); p.addLine(to: CGPoint(x: x, y: r.maxY))
                    p.move(to: CGPoint(x: r.minX, y: y)); p.addLine(to: CGPoint(x: r.maxX, y: y))
                }
            }
            .stroke(Color.white.opacity(0.5), lineWidth: 0.5)
            Rectangle().path(in: r).stroke(Color.white, lineWidth: 1.5)
            ForEach(Array(handlePoints(r).enumerated()), id: \.offset) { _, pt in
                Circle().fill(Palette.hotB).frame(width: 12, height: 12)
                    .overlay(Circle().stroke(Color.white, lineWidth: 2))
                    .position(pt)
            }
        }
        .allowsHitTesting(false)
    }

    private func handlePoints(_ r: CGRect) -> [CGPoint] {
        [CGPoint(x: r.minX, y: r.minY), CGPoint(x: r.maxX, y: r.minY), CGPoint(x: r.minX, y: r.maxY), CGPoint(x: r.maxX, y: r.maxY),
         CGPoint(x: r.midX, y: r.minY), CGPoint(x: r.midX, y: r.maxY), CGPoint(x: r.minX, y: r.midY), CGPoint(x: r.maxX, y: r.midY)]
    }

    static func fit(_ size: CGSize, in box: CGSize) -> CGRect {
        let s = min(box.width / max(1, size.width), box.height / max(1, size.height))
        let w = size.width * s, h = size.height * s
        return CGRect(x: (box.width - w) / 2, y: (box.height - h) / 2, width: w, height: h)
    }

    // MARK: Interaction

    private func drag(_ v: DragGesture.Value, fit: CGRect) {
        if start == nil {
            start = rect
            grab = which(v.startLocation, fit: fit)
        }
        guard let s = start, grab != .none else { return }
        let dx = v.translation.width / fit.width, dy = v.translation.height / fit.height
        let minW = 16 / fit.width, minH = 16 / fit.height
        var r = s
        switch grab {
        case .move:
            r.origin.x = min(max(0, s.minX + dx), 1 - s.width)
            r.origin.y = min(max(0, s.minY + dy), 1 - s.height)
        default:
            var x0 = s.minX, y0 = s.minY, x1 = s.maxX, y1 = s.maxY
            if [.l, .tl, .bl].contains(grab) { x0 = min(max(0, s.minX + dx), x1 - minW) }
            if [.r, .tr, .br].contains(grab) { x1 = max(min(1, s.maxX + dx), x0 + minW) }
            if [.t, .tl, .tr].contains(grab) { y0 = min(max(0, s.minY + dy), y1 - minH) }
            if [.b, .bl, .br].contains(grab) { y1 = max(min(1, s.maxY + dy), y0 + minH) }
            r = CGRect(x: x0, y: y0, width: x1 - x0, height: y1 - y0)
            if let ratio { r = lockRatio(r, ratio: ratio, from: s) }
        }
        rect = r
        syncFields()
    }

    /// Keeps the ratio, anchored at the corner/edge opposite the one being dragged.
    private func lockRatio(_ r: CGRect, ratio: Double, from s: CGRect) -> CGRect {
        let rn = ratio * pixel.height / pixel.width // ratio in 0...1 space
        var w = r.width, h = r.height
        switch grab {
        case .t, .b: w = h * rn
        default: h = w / rn
        }
        // Shrink to fit if it would leave the image.
        let anchorX: CGFloat = [.l, .tl, .bl].contains(grab) ? s.maxX : ([.t, .b].contains(grab) ? s.midX : s.minX)
        let anchorY: CGFloat = [.t, .tl, .tr].contains(grab) ? s.maxY : ([.l, .r].contains(grab) ? s.midY : s.minY)
        let maxW = [.l, .tl, .bl].contains(grab) ? anchorX : ([.t, .b].contains(grab) ? 2 * min(anchorX, 1 - anchorX) : 1 - anchorX)
        let maxH = [.t, .tl, .tr].contains(grab) ? anchorY : ([.l, .r].contains(grab) ? 2 * min(anchorY, 1 - anchorY) : 1 - anchorY)
        let k = min(1, maxW / max(w, 0.0001), maxH / max(h, 0.0001))
        w *= k; h *= k
        let x = [.l, .tl, .bl].contains(grab) ? anchorX - w : ([.t, .b].contains(grab) ? anchorX - w / 2 : anchorX)
        let y = [.t, .tl, .tr].contains(grab) ? anchorY - h : ([.l, .r].contains(grab) ? anchorY - h / 2 : anchorY)
        return CGRect(x: x, y: y, width: w, height: h)
    }

    private func which(_ p: CGPoint, fit: CGRect) -> Grab {
        let r = CGRect(x: fit.minX + rect.minX * fit.width, y: fit.minY + rect.minY * fit.height,
                       width: rect.width * fit.width, height: rect.height * fit.height)
        let near: CGFloat = 14
        let l = abs(p.x - r.minX) < near, rr = abs(p.x - r.maxX) < near
        let t = abs(p.y - r.minY) < near, b = abs(p.y - r.maxY) < near
        let inX = p.x > r.minX - near && p.x < r.maxX + near, inY = p.y > r.minY - near && p.y < r.maxY + near
        if l && t { return .tl }
        if rr && t { return .tr }
        if l && b { return .bl }
        if rr && b { return .br }
        if l && inY { return .l }
        if rr && inY { return .r }
        if t && inX { return .t }
        if b && inX { return .b }
        if r.contains(p) { return .move }
        return .none
    }

    private func setRatio(_ value: Double?) {
        ratio = value
        rect = full(for: value)
        syncFields()
    }

    /// Biggest centred rect with this ratio.
    private func full(for ratio: Double?) -> CGRect {
        guard let ratio else { return CGRect(x: 0, y: 0, width: 1, height: 1) }
        let rn = ratio * pixel.height / pixel.width
        var w: CGFloat = 1, h: CGFloat = 1
        if rn > 1 { h = 1 / rn } else { w = rn }
        return CGRect(x: (1 - w) / 2, y: (1 - h) / 2, width: w, height: h)
    }

    private func syncFields() {
        wText = "\(Int((rect.width * pixel.width).rounded()))"
        hText = "\(Int((rect.height * pixel.height).rounded()))"
    }

    private func applyFields(width: Bool) {
        guard let w = Double(wText), let h = Double(hText), w > 0, h > 0 else { syncFields(); return }
        var nw = min(1, w / pixel.width), nh = min(1, h / pixel.height)
        if let ratio {
            let rn = ratio * pixel.height / pixel.width
            if width { nh = nw / rn } else { nw = nh * rn }
            let k = min(1, 1 / nw, 1 / nh)
            nw *= k; nh *= k
        }
        let c = CGPoint(x: rect.midX, y: rect.midY)
        rect = CGRect(x: min(max(0, c.x - nw / 2), 1 - nw), y: min(max(0, c.y - nh / 2), 1 - nh), width: nw, height: nh)
        syncFields()
    }

    private func load() {
        guard let first = files.first else { return }
        pixel = ImageOps.pixelSize(first) ?? CGSize(width: 1000, height: 1000)
        if let cg = try? ImageOps.load(first, maxPixel: 1600) {
            image = NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
        }
        syncFields()
    }

    private func apply() {
        let r = rect, files = files, o = options
        Editors.work("Cropping", count: files.count, close: "crop") {
            try files.map { f in
                let px = ImageOps.pixelSize(f) ?? .zero
                let pr = CGRect(x: r.minX * px.width, y: r.minY * px.height, width: r.width * px.width, height: r.height * px.height)
                return try ImageOps.crop(f, rect: pr, quality: o.quality, dir: Naming.folder(for: f, override: o.outputFolder))
            }
        }
    }
}
