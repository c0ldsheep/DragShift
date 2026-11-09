import AppKit
import DragShiftCore
import ServiceManagement
import SwiftUI

/// First-run tour with a practice file you can drag right inside the window.
struct WelcomeView: View {
    @ObservedObject var live = Live.shared
    @ObservedObject var demo: RingModel
    @ViewState private var login = SMAppService.mainApp.status == .enabled
    let practice: URL

    var body: some View {
        HStack(spacing: 0) {
            ZStack {
                LinearGradient(colors: [Color(red: 1, green: 0.62, blue: 0.3), Palette.hotB, Color(red: 0.6, green: 0.42, blue: 1)],
                               startPoint: .topLeading, endPoint: .bottomTrailing)
                RingView(model: demo)
                    .scaleEffect(0.62)
                    .frame(width: 280, height: 300)
                    .allowsHitTesting(false)
            }
            .frame(width: 300)

            VStack(alignment: .leading, spacing: 16) {
                VStack(alignment: .leading, spacing: 4) {
                    Text("DragShift").font(.system(size: 28, weight: .heavy))
                    Text("Change any file while you're dragging it.").font(.title3).foregroundStyle(.secondary)
                }
                step(1, "Start dragging a file", "From Finder, the Desktop, Photos, Mail, or a web page.")
                step(2, "Press ⇧ Shift — or touch a 2nd finger down", "The ring opens right where the file is.")
                step(3, "Push it toward a slice and let go", "Or let go in the middle: the ring stays open, so lift your fingers, glide to a slice and tap. ⇧ shows more; double-tap repeats your last one.")

                practiceBox

                HStack(spacing: 6) {
                    Image(systemName: "star.fill").foregroundStyle(Palette.hotB)
                    Text("Push a dragged file against the right edge of the screen for your Favorites.")
                }
                .font(.callout)

                TrackpadStatus(live: live)

                HStack {
                    Toggle("Open at login", isOn: $login)
                        .onChange(of: login) { on in
                            if on { try? SMAppService.mainApp.register() } else { try? SMAppService.mainApp.unregister() }
                        }
                    Spacer()
                    Button("Start using DragShift") {
                        Prefs.d.set(true, forKey: Key.welcomeDone)
                        Windows.close("welcome")
                    }
                    .buttonStyle(HotButtonStyle())
                    .keyboardShortcut(.defaultAction)
                }
            }
            .padding(26)
            .padding(.top, 8)
        }
        .frame(width: 820, height: 600)
    }

    private func step(_ n: Int, _ title: String, _ detail: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(n)").font(.system(size: 14, weight: .bold)).foregroundStyle(Palette.onHot)
                .frame(width: 26, height: 26).background(Palette.hot, in: Circle())
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 14, weight: .semibold))
                Text(detail).font(.callout).foregroundStyle(.secondary)
            }
        }
    }

    private var practiceBox: some View {
        HStack(spacing: 14) {
            DraggableFile(url: practice)
                .frame(width: 84, height: 84)
            VStack(alignment: .leading, spacing: 4) {
                Text("Try it here").font(.system(size: 13, weight: .semibold))
                Text(practiceHint).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                if let r = live.lastResult, !r.outputs.isEmpty, r.outputs[0].path.hasPrefix(practice.deletingLastPathComponent().path) {
                    Button("Show what you made") { NSWorkspace.shared.activateFileViewerSelecting(r.outputs) }
                        .controlSize(.small)
                }
            }
            Spacer()
        }
        .padding(12)
        .background(Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(Color.secondary.opacity(0.2), style: StrokeStyle(lineWidth: 1, dash: [5, 4])))
    }

    private var practiceHint: String {
        if let r = live.lastResult, !r.outputs.isEmpty, r.outputs[0].path.hasPrefix(practice.deletingLastPathComponent().path) {
            return "You did it: \(r.outputs[0].lastPathComponent). That's all there is to it."
        }
        if live.ringOpen { return "Now push it toward a slice and let go, or let go in the middle and tap one." }
        if live.dragging { return "Keep holding. Press Shift, or tap a second finger on the trackpad." }
        return "Drag this picture a little way out of its box."
    }
}

/// A real file you can drag (writes a proper file URL to the drag, like Finder does).
struct DraggableFile: NSViewRepresentable {
    let url: URL
    func makeNSView(context: Context) -> DragSourceView { DragSourceView(url: url) }
    func updateNSView(_ nsView: DragSourceView, context: Context) {}
}

final class DragSourceView: NSView, NSDraggingSource {
    let url: URL
    private let icon: NSImage

    init(url: URL) {
        self.url = url
        icon = NSImage(contentsOf: url) ?? NSWorkspace.shared.icon(forFile: url.path)
        super.init(frame: .zero)
    }
    required init?(coder: NSCoder) { fatalError() }

    override func draw(_ dirtyRect: NSRect) {
        let r = bounds.insetBy(dx: 6, dy: 6)
        let shadow = NSShadow()
        shadow.shadowBlurRadius = 6
        shadow.shadowOffset = NSSize(width: 0, height: -2)
        shadow.shadowColor = NSColor.black.withAlphaComponent(0.25)
        shadow.set()
        icon.draw(in: r, from: .zero, operation: .sourceOver, fraction: 1)
    }

    override func mouseDragged(with event: NSEvent) {
        let item = NSDraggingItem(pasteboardWriter: url as NSURL)
        item.setDraggingFrame(bounds, contents: icon)
        beginDraggingSession(with: [item], event: event, source: self)
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        // Logged so the automated drag test knows where to grab.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { [weak self] in
            guard let self, let w = self.window else { return }
            let r = w.convertToScreen(self.convert(self.bounds, to: nil))
            Log.line("practice tile at \(Int(r.midX)),\(Int(r.midY))")
        }
    }

    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        .copy
    }
}

enum Practice {
    /// A friendly sample picture in Application Support, recreated if it's gone.
    static var file: URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("DragShift/Practice")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let url = dir.appendingPathComponent("Try Me.png")
        if !FileManager.default.fileExists(atPath: url.path) { make(url) }
        return url
    }

    private static func make(_ url: URL) {
        let w = 640, h = 480
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return }
        let colors = [CGColor(srgbRed: 1, green: 0.62, blue: 0.3, alpha: 1), CGColor(srgbRed: 1, green: 0.24, blue: 0.43, alpha: 1),
                      CGColor(srgbRed: 0.6, green: 0.42, blue: 1, alpha: 1)]
        let g = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: colors as CFArray, locations: [0, 0.55, 1])!
        ctx.drawLinearGradient(g, start: .zero, end: CGPoint(x: w, y: h), options: [])
        ctx.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 0.9))
        ctx.fillEllipse(in: CGRect(x: 220, y: 140, width: 200, height: 200))
        ctx.setFillColor(CGColor(srgbRed: 1, green: 0.4, blue: 0.4, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: 280, y: 200, width: 80, height: 80))
        guard let img = ctx.makeImage() else { return }
        try? ImageOps.write(img, to: url, format: .png, quality: 1)
    }
}
