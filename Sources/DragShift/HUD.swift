import AppKit
import DragShiftCore
import SwiftUI

/// The little glass bubble: "Converting to JPG · 1 of 3" with a progress bar, then
/// "Saved Page 001.jpg" with Show and Undo.
@MainActor
final class HUDModel: ObservableObject {
    enum State { case working, done, failed }
    @Published var state: State = .working
    @Published var title = ""
    @Published var detail = ""
    @Published var progress: Double = 0
    @Published var outputs: [URL] = []
    @Published var visible = false
    @Published var link: URL?
}

@MainActor
final class HUD {
    static let shared = HUD()
    let model = HUDModel()
    private var panel: NSPanel
    private var hideWork: DispatchWorkItem?
    var onUndo: (() -> Void)?
    var onCancel: (() -> Void)?

    private init() {
        panel = Self.makePanel()
        panel.contentView = FirstClickHostingView(rootView: HUDView(model: model, hud: self))
    }

    private static func makePanel() -> NSPanel {
        let p = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 360, height: 96),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false
        p.level = .statusBar
        p.hidesOnDeactivate = false
        p.becomesKeyOnlyIfNeeded = true
        p.collectionBehavior = Floating.behavior
        return p
    }

    func begin(_ title: String, count: Int, near point: NSPoint) {
        hideWork?.cancel()
        model.state = .working
        model.title = title
        model.detail = count > 1 ? "0 of \(count) done" : "Working…"
        model.progress = 0.02
        model.outputs = []
        place(near: point)
    }

    func update(done: Int, total: Int, fraction: Double) {
        model.progress = max(model.progress, min(1, fraction))
        if total > 1 { model.detail = "\(min(done, total)) of \(total) done" }
    }

    func finish(_ report: RunReport, title: String) {
        if report.outputs.isEmpty, report.failures.isEmpty, let text = report.text {
            // Read-only results (QR codes): show what was found; it's already on the clipboard.
            model.state = .done
            model.title = "Found: \(text.split(separator: "\n").count == 1 ? "1 code" : "\(text.split(separator: "\n").count) codes")"
            model.detail = text + (Prefs.d.bool(forKey: Key.copyText) ? "  · copied" : "")
            model.progress = 1
            model.outputs = []
            model.link = text.split(separator: "\n").first.flatMap { URL(string: String($0)) }.flatMap { $0.scheme?.hasPrefix("http") == true ? $0 : nil }
            Feedback.done(ok: true)
            scheduleHide(after: 8)
            return
        }
        model.link = nil
        let ok = !report.outputs.isEmpty
        model.state = ok ? .done : (report.failures.isEmpty ? .done : .failed)
        model.title = ok ? title : (report.failures.isEmpty ? "Nothing to do" : "Couldn't finish")
        var detail = report.summary
        if ok && !report.failures.isEmpty { detail += " · \(report.failures.count) failed" }
        if report.text != nil && Prefs.d.bool(forKey: Key.copyText) { detail += " · text copied" }
        model.detail = detail
        model.progress = 1
        model.outputs = report.outputs
        Feedback.done(ok: ok || report.failures.isEmpty)
        scheduleHide(after: model.state == .failed ? 8 : 5)
    }

    /// One-line message (Copy, Copy Path, AirDrop...).
    func flash(_ title: String, _ detail: String, near point: NSPoint, outputs: [URL] = []) {
        hideWork?.cancel()
        model.link = nil
        model.state = .done
        model.title = title
        model.detail = detail
        model.progress = 1
        model.outputs = outputs
        place(near: point)
        scheduleHide(after: 2.5)
    }

    func fail(_ title: String, _ detail: String, near point: NSPoint) {
        hideWork?.cancel()
        model.state = .failed
        model.title = title
        model.detail = detail
        model.outputs = []
        place(near: point)
        Feedback.done(ok: false)
        scheduleHide(after: 6)
    }

    func keepOpen() { hideWork?.cancel() }
    func releaseOpen() { scheduleHide(after: 2) }

    private func place(near point: NSPoint) {
        let size = NSSize(width: 360, height: 96)
        let screen = NSScreen.screens.first { NSMouseInRect(point, $0.frame, false) } ?? NSScreen.main
        // Just below the pointer, never under it (hovering keeps the bubble open).
        var origin = NSPoint(x: point.x - size.width / 2, y: point.y - size.height - 40)
        if let vf = screen?.visibleFrame {
            if origin.y < vf.minY + 8 { origin.y = point.y + 40 }
            origin.x = min(max(origin.x, vf.minX + 8), vf.maxX - size.width - 8)
            origin.y = min(max(origin.y, vf.minY + 8), vf.maxY - size.height - 8)
        }
        panel.setFrame(NSRect(origin: origin, size: size), display: true)
        Floating.orderFront(panel, rebuild: Self.makePanel) { [unowned self] in panel = $0 }
        model.visible = true
    }

    private func scheduleHide(after seconds: Double) {
        hideWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            self.model.visible = false
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                if !self.model.visible { self.panel.orderOut(nil) }
            }
        }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
    }

    func hideNow() {
        hideWork?.cancel()
        model.visible = false
        panel.orderOut(nil)
    }
}

struct HUDView: View {
    @ObservedObject var model: HUDModel
    let hud: HUD
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                icon
                VStack(alignment: .leading, spacing: 2) {
                    Text(model.title)
                        .font(.system(size: 13, weight: .semibold))
                        .lineLimit(1)
                    Text(model.detail)
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                        .truncationMode(.middle)
                }
                Spacer(minLength: 4)
                buttons
            }
            if model.state == .working {
                ProgressView(value: model.progress)
                    .progressViewStyle(.linear)
                    .tint(Palette.hotB)
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 12)
        .frame(width: 344)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous)
            .strokeBorder(Color.white.opacity(scheme == .dark ? 0.12 : 0.5), lineWidth: 1))
        .shadow(color: .black.opacity(0.22), radius: 16, y: 6)
        .frame(width: 360, height: 96)
        .opacity(model.visible ? 1 : 0)
        .offset(y: model.visible ? 0 : 8)
        .animation(.spring(response: 0.3, dampingFraction: 0.85), value: model.visible)
        .onHover { inside in inside ? hud.keepOpen() : hud.releaseOpen() }
    }

    @ViewBuilder private var icon: some View {
        switch model.state {
        case .working:
            ProgressView().controlSize(.small).frame(width: 22)
        case .done:
            Image(systemName: "checkmark.circle.fill").font(.system(size: 20)).foregroundStyle(.green).frame(width: 22)
        case .failed:
            Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 18)).foregroundStyle(.orange).frame(width: 22)
        }
    }

    @ViewBuilder private var buttons: some View {
        switch model.state {
        case .working:
            Button { hud.onCancel?() } label: { Image(systemName: "xmark") }
                .buttonStyle(.borderless)
                .help("Cancel")
        case .done where model.link != nil:
            Button("Open") { if let l = model.link { NSWorkspace.shared.open(l) } }
                .controlSize(.small)
        case .done where !model.outputs.isEmpty:
            HStack(spacing: 6) {
                Button("Show") { NSWorkspace.shared.activateFileViewerSelecting(model.outputs) }
                Button("Undo") { hud.onUndo?() }
            }
            .controlSize(.small)
        default:
            EmptyView()
        }
    }
}

/// Buttons in the bubble work on the first click, even though the bubble never takes focus.
final class FirstClickHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}
