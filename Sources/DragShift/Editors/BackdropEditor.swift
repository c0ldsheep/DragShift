import AppKit
import DragShiftCore
import SwiftUI
import UniformTypeIdentifiers

/// "Add BG": puts screenshots and product shots on a gradient, colour, blurred copy of
/// itself, or your own picture, with padding, rounded corners and a shadow.
struct BackdropEditor: View {
    let files: [URL]
    let options: Options

    @ViewState private var style = Prefs.backdrop
    @ViewState private var source: CGImage?
    @ViewState private var preview: NSImage?
    @ViewState private var longest: Double = 1000
    @ViewState private var renderID = 0

    static let gradients: [[RGBA]] = [
        [RGBA(hex: 0xFF9A3C), RGBA(hex: 0xFF3D6E), RGBA(hex: 0x9B6BFF)],
        [RGBA(hex: 0xFFD3A5), RGBA(hex: 0xFD6585)],
        [RGBA(hex: 0x2193B0), RGBA(hex: 0x6DD5ED)],
        [RGBA(hex: 0x43E97B), RGBA(hex: 0x38F9D7)],
        [RGBA(hex: 0xA18CD1), RGBA(hex: 0xFBC2EB)],
        [RGBA(hex: 0x0F2027), RGBA(hex: 0x2C5364)],
        [RGBA(hex: 0xF7971E), RGBA(hex: 0xFFD200)],
        [RGBA(hex: 0xE0E0E0), RGBA(hex: 0xFFFFFF)],
    ]
    static let solids: [RGBA] = [.white, RGBA(hex: 0xF2F2F7), RGBA(hex: 0x1C1C1E), RGBA(hex: 0xFF3D6E),
                                 RGBA(hex: 0xFF9500), RGBA(hex: 0x34C759), RGBA(hex: 0x0A84FF), RGBA(hex: 0x5E5CE6)]

    var body: some View {
        HStack(spacing: 0) {
            ZStack {
                Checkerboard().opacity(0.5)
                if let preview {
                    Image(nsImage: preview).resizable().aspectRatio(contentMode: .fit).padding(20)
                        .shadow(color: .black.opacity(0.12), radius: 8)
                } else {
                    ProgressView()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .clipShape(RoundedRectangle(cornerRadius: 12))
            .padding(16)

            sidebar
                .frame(width: 280)
                .padding(16)
                .background(.thinMaterial)
        }
        .padding(.top, 20)
        .task { load() }
        .onChange(of: style) { _ in render() }
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 14) {
            EditorHeader(files: files, note: files.count > 1 ? "Saved as PNG · all \(files.count) images" : "Saved as a new PNG")
            Text("Background").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 6) {
                    ForEach(Array(Self.gradients.enumerated()), id: \.offset) { _, g in
                        swatch(.gradient(g, angle: 135)) {
                            LinearGradient(colors: g.map { Color(cgColor: $0.cgColor) }, startPoint: .topLeading, endPoint: .bottomTrailing)
                        }
                    }
                }
                HStack(spacing: 6) {
                    ForEach(Array(Self.solids.enumerated()), id: \.offset) { _, c in
                        swatch(.solid(c)) { Color(cgColor: c.cgColor) }
                    }
                }
            }
            HStack(spacing: 6) {
                special("Blur", "drop.fill", .blurredSelf)
                special("None", "square.dashed", .transparent)
                Button {
                    pickImage()
                } label: {
                    Label("Image…", systemImage: "photo")
                        .font(.system(size: 11, weight: .medium))
                }
                .buttonStyle(.bordered).controlSize(.small)
            }

            slider("Padding", value: $style.padding, range: 0...0.3)
            slider("Corners", value: $style.corner, range: 0...0.12)
            slider("Shadow", value: $style.shadow, range: 0...0.08)

            Text("Ratio").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            Picker("", selection: $style.ratio) {
                ForEach(BackdropStyle.Ratio.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            Spacer()
            HStack {
                Button("Cancel") { Windows.close("backdrop") }.keyboardShortcut(.cancelAction)
                Spacer()
                Button("Save with Background") { save() }.buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
            }
        }
    }

    private func swatch<V: View>(_ fill: BackdropStyle.Fill, @ViewBuilder _ look: () -> V) -> some View {
        Button { style.fill = fill } label: {
            look()
                .frame(width: 26, height: 26)
                .clipShape(RoundedRectangle(cornerRadius: 7))
                .overlay(RoundedRectangle(cornerRadius: 7).strokeBorder(style.fill == fill ? Palette.hotB : Color.black.opacity(0.12),
                                                                        lineWidth: style.fill == fill ? 2.5 : 1))
        }
        .buttonStyle(.plain)
    }

    private func special(_ title: String, _ icon: String, _ fill: BackdropStyle.Fill) -> some View {
        Button { style.fill = fill } label: {
            Label(title, systemImage: icon).font(.system(size: 11, weight: .medium))
        }
        .buttonStyle(.bordered).controlSize(.small)
        .tint(style.fill == fill ? Palette.hotB : nil)
    }

    private func slider(_ name: String, value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        HStack {
            Text(name).font(.caption).frame(width: 54, alignment: .leading)
            Slider(value: value, in: range)
            Text("\(Int(value.wrappedValue * longest)) px").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                .frame(width: 52, alignment: .trailing)
        }
    }

    private func pickImage() {
        let p = NSOpenPanel()
        p.allowedContentTypes = [.image]
        p.level = .modalPanel
        if p.runModal() == .OK, let u = p.url { style.fill = .image(path: u.path) }
    }

    private func load() {
        guard let first = files.first else { return }
        if let size = ImageOps.pixelSize(first) { longest = Double(max(size.width, size.height)) }
        source = try? ImageOps.load(first, maxPixel: 1000)
        render()
    }

    private func render() {
        guard let source else { return }
        renderID += 1
        let id = renderID, s = style
        Task.detached(priority: .userInitiated) {
            guard let out = Backdrop.render(source, style: s) else { return }
            let img = NSImage(cgImage: out, size: NSSize(width: out.width, height: out.height))
            await MainActor.run { if id == renderID { preview = img } }
        }
    }

    private func save() {
        Prefs.backdrop = style
        let s = style, files = files, o = options
        Editors.work("Adding background", count: files.count, close: "backdrop") {
            try files.map { f in
                guard let out = Backdrop.render(try ImageOps.load(f), style: s) else { throw DragShiftError("Couldn't render \(f.lastPathComponent).") }
                return try ImageOps.saveEdited(out, like: f, suffix: "With Background", dir: Naming.folder(for: f, override: o.outputFolder),
                                               quality: o.quality, forceFormat: .png)
            }
        }
    }
}

struct Checkerboard: View {
    var body: some View {
        Canvas { ctx, size in
            let s: CGFloat = 10
            for y in stride(from: 0, to: size.height, by: s) {
                for x in stride(from: 0, to: size.width, by: s) where (Int(x / s) + Int(y / s)) % 2 == 0 {
                    ctx.fill(Path(CGRect(x: x, y: y, width: s, height: s)), with: .color(.gray.opacity(0.25)))
                }
            }
        }
    }
}
