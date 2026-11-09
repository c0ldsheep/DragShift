import AppKit
import DragShiftCore
import SwiftUI

/// Finds emails, phone numbers, UPI IDs, Aadhaar/PAN, card and bank numbers, addresses and
/// faces on its own; you tick what to hide and drag to add more boxes.
struct RedactEditor: View {
    let file: URL
    let options: Options

    struct Box: Identifiable {
        let id = UUID()
        var rect: CGRect      // pixels of the upright image, top-left origin
        var label: String
        var on = true
    }

    @ViewState private var image: NSImage?
    @ViewState private var pixel = CGSize(width: 1, height: 1)
    @ViewState private var boxes: [Box] = []
    @ViewState private var scanning = true
    @ViewState private var faces = true
    @ViewState private var style = VisionOps.RedactStyle(rawValue: Prefs.d.string(forKey: Key.redactStyle) ?? "") ?? .box
    @ViewState private var drawing: CGRect?

    var body: some View {
        HStack(spacing: 0) {
            GeometryReader { geo in
                let fit = CropEditor.fit(pixel, in: geo.size)
                ZStack(alignment: .topLeading) {
                    if let image {
                        Image(nsImage: image).resizable().frame(width: fit.width, height: fit.height).offset(x: fit.minX, y: fit.minY)
                    }
                    ForEach(boxes) { b in
                        let r = toView(b.rect, fit)
                        Rectangle()
                            .fill(b.on ? Color.black.opacity(style == .box ? 0.85 : 0.35) : Color.clear)
                            .overlay(Rectangle().strokeBorder(b.on ? Palette.hotB : Color.white.opacity(0.9),
                                                              style: StrokeStyle(lineWidth: 2, dash: b.on ? [] : [5, 3])))
                            .frame(width: r.width, height: r.height)
                            .offset(x: r.minX, y: r.minY)
                            .onTapGesture { toggle(b.id) }
                    }
                    if let d = drawing {
                        Rectangle().strokeBorder(Palette.hotA, lineWidth: 2)
                            .frame(width: d.width, height: d.height).offset(x: d.minX, y: d.minY)
                    }
                    if scanning {
                        Label("Looking for private details…", systemImage: "eye.trianglebadge.exclamationmark")
                            .font(.callout.weight(.medium))
                            .padding(10).background(.regularMaterial, in: Capsule())
                            .frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }
                .frame(width: geo.size.width, height: geo.size.height)
                .contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance: 4)
                    .onChanged { v in drawing = CGRect(origin: v.startLocation, size: .zero).union(CGRect(origin: v.location, size: .zero)) }
                    .onEnded { v in
                        if let d = drawing, d.width > 6, d.height > 6 {
                            boxes.append(Box(rect: toPixels(d, fit), label: "Your box"))
                        }
                        drawing = nil
                    })
            }
            .padding(16)

            VStack(alignment: .leading, spacing: 12) {
                EditorHeader(files: [file], note: "Saved as a new copy with no hidden info")
                Text("Hide").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                ScrollView {
                    VStack(alignment: .leading, spacing: 6) {
                        if boxes.isEmpty && !scanning {
                            Text("Nothing found. Drag on the picture to cover anything.").font(.caption).foregroundStyle(.secondary)
                        }
                        ForEach($boxes) { $b in
                            Toggle(isOn: $b.on) { Text(b.label).font(.callout) }
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                Toggle("Find faces too", isOn: $faces).onChange(of: faces) { _ in scan() }
                Text("Style").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                Picker("", selection: $style) {
                    ForEach(VisionOps.RedactStyle.allCases, id: \.self) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented).labelsHidden()
                Text("Drag on the picture to add a box. Click a box to turn it off.")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                HStack {
                    Button("Cancel") { Windows.close("redact") }.keyboardShortcut(.cancelAction)
                    Spacer()
                    Button("Save Redacted") { save() }.buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
                        .disabled(!boxes.contains { $0.on })
                }
            }
            .frame(width: 260)
            .padding(16)
            .background(.thinMaterial)
        }
        .padding(.top, 20)
        .task { load() }
    }

    private func toView(_ r: CGRect, _ fit: CGRect) -> CGRect {
        let s = fit.width / pixel.width
        return CGRect(x: fit.minX + r.minX * s, y: fit.minY + r.minY * s, width: r.width * s, height: r.height * s)
    }

    private func toPixels(_ r: CGRect, _ fit: CGRect) -> CGRect {
        let s = pixel.width / fit.width
        return CGRect(x: (r.minX - fit.minX) * s, y: (r.minY - fit.minY) * s, width: r.width * s, height: r.height * s)
    }

    private func toggle(_ id: UUID) {
        if let i = boxes.firstIndex(where: { $0.id == id }) { boxes[i].on.toggle() }
    }

    private func load() {
        pixel = ImageOps.pixelSize(file) ?? pixel
        if let cg = try? ImageOps.load(file, maxPixel: 1800) {
            image = NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
        }
        scan()
    }

    private func scan() {
        scanning = true
        let f = file, withFaces = faces
        Task {
            let found = (try? await Engine.offload { try VisionOps.findPrivate(try ImageOps.load(f), faces: withFaces) }) ?? []
            await MainActor.run {
                let mine = boxes.filter { $0.label == "Your box" }
                boxes = found.map { Box(rect: $0.rect, label: $0.label) } + mine
                scanning = false
            }
        }
    }

    private func save() {
        Prefs.d.set(style.rawValue, forKey: Key.redactStyle)
        let rects = boxes.filter(\.on).map(\.rect), s = style, f = file, o = options
        Editors.work("Redacting", count: 1, close: "redact") {
            guard let out = VisionOps.redact(try ImageOps.load(f), rects: rects, style: s) else {
                throw DragShiftError("Couldn't redact \(f.lastPathComponent).")
            }
            return [try ImageOps.saveEdited(out, like: f, suffix: "Redacted", dir: Naming.folder(for: f, override: o.outputFolder),
                                            quality: o.quality, keepMetadata: false)]
        }
    }
}
