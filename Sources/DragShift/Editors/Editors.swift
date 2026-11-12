import AppKit
import DragShiftCore
import SwiftUI

/// Opens SwiftUI windows for the slices that need a choice (crop box, background, password...).
@MainActor
enum Windows {
    private static var open: [String: NSWindow] = [:]
    private static var delegates: [String: CloseWatcher] = [:]
    private static var closers: [String: [() -> Void]] = [:]

    /// Runs `body` once, when the window `id` closes (now, if it isn't open).
    static func whenClosed(_ id: String, _ body: @escaping () -> Void) {
        if open[id] == nil { body() } else { closers[id, default: []].append(body) }
    }

    @discardableResult
    static func show<V: View>(_ id: String, title: String, size: CGSize, resizable: Bool = false, _ view: V) -> NSWindow {
        if let w = open[id] { w.close() }
        var style: NSWindow.StyleMask = [.titled, .closable, .fullSizeContentView]
        if resizable { style.insert(.resizable) }
        let w = NSWindow(contentRect: NSRect(origin: .zero, size: size), styleMask: style, backing: .buffered, defer: false)
        w.title = title
        w.titlebarAppearsTransparent = true
        w.isReleasedWhenClosed = false
        w.contentViewController = NSHostingController(rootView: view)
        w.setContentSize(size)
        w.center()
        w.level = .floating
        let watcher = CloseWatcher {
            open[id] = nil
            delegates[id] = nil
            let waiting = closers.removeValue(forKey: id) ?? []
            waiting.forEach { $0() }
        }
        w.delegate = watcher
        delegates[id] = watcher
        open[id] = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
        return w
    }

    static func close(_ id: String) { open[id]?.close() }
    static func window(_ id: String) -> NSWindow? { open[id] }

    /// Grows or shrinks a window's content to `size`, keeping its top edge where it is.
    static func resize(_ id: String, to size: CGSize) {
        guard let w = open[id] else { return }
        let target = w.frameRect(forContentRect: NSRect(origin: .zero, size: size))
        var f = w.frame
        f.origin.y += f.height - target.height
        f.size = target.size
        w.setFrame(f, display: true, animate: true)
    }
    static func isOpen(_ id: String) -> Bool { open[id] != nil }
}

final class CloseWatcher: NSObject, NSWindowDelegate {
    let onClose: () -> Void
    init(_ onClose: @escaping () -> Void) { self.onClose = onClose }
    func windowWillClose(_ notification: Notification) { onClose() }
}

@MainActor
enum Editors {
    /// `closed` runs when the editor's window has closed (used to let go of a temporary copy).
    static func open(_ editor: Action.Editor, action: Action, files: [URL], temporary: Bool, closed: (() -> Void)? = nil) {
        let options = Prefs.options(temporary: temporary)
        let id: String
        switch editor {
        case .crop: id = "crop"
        case .backdrop: id = "backdrop"
        case .redact: id = "redact"
        case .resize: id = "resize"
        case .lockPDF, .unlockPDF: id = "password"
        case .annotate: id = "annotate"
        case .rename: id = "rename"
        case .translate: id = "translate"
        case .fitKB: id = "fit"
        case .print: id = "print"
        case .trim: id = "trim"
        }
        defer { if let closed { Windows.whenClosed(id, closed) } }
        switch editor {
        case .crop:
            Windows.show("crop", title: "Crop", size: CGSize(width: 640, height: 640),
                         CropEditor(files: files, options: options))
        case .backdrop:
            Windows.show("backdrop", title: "Add Background", size: CGSize(width: 820, height: 600),
                         BackdropEditor(files: files, options: options))
        case .redact:
            Windows.show("redact", title: "Redact", size: CGSize(width: 860, height: 640), resizable: true,
                         RedactEditor(file: files[0], options: options))
        case .resize:
            Windows.show("resize", title: "Resize", size: CGSize(width: 380, height: 420),
                         ResizeSheet(files: files, options: options))
        case .lockPDF, .unlockPDF:
            Windows.show("password", title: editor == .lockPDF ? "Lock PDF" : "Unlock PDF", size: CGSize(width: 380, height: editor == .lockPDF ? 270 : 250),
                         PasswordSheet(files: files, lock: editor == .lockPDF, options: options))
        case .annotate:
            Windows.show("annotate", title: "Annotate", size: CGSize(width: 860, height: 680), resizable: true,
                         AnnotateEditor(file: files[0], options: options))
        case .rename:
            Windows.show("rename", title: "Rename", size: CGSize(width: 560, height: 520), resizable: true, RenameSheet(files: files))
        case .translate:
            Windows.show("translate", title: "Translate", size: CGSize(width: 560, height: 460), resizable: true,
                         TranslateView(file: files[0]))
        case .fitKB:
            Windows.show("fit", title: "Make it smaller than…", size: CGSize(width: 420, height: 340),
                         FitSheet(files: files, temporary: temporary))
        case .print:
            // "Print right away" skips the window and uses the top preset.
            if Prefs.d.bool(forKey: Key.printQuick), let first = Prefs.shownPrintPresets.first {
                Windows.close("print") // an older print window would otherwise decide when this file is let go
                Runner.shared.perform(first.action, files, at: NSEvent.mouseLocation, temporary: temporary)
            } else {
                Windows.show("print", title: "Print", size: PrintSheet.quickSize(rows: PrintSheet.quickRows),
                             PrintSheet(files: files, temporary: temporary))
            }
        case .trim:
            Windows.show("trim", title: "Trim — drag the yellow handles, then Trim", size: CGSize(width: 760, height: 500),
                         resizable: true, TrimEditor(file: files[0], options: options))
        }
    }

    /// Runs blocking file work on the engine's work queue, then reports through the normal bubble.
    static func work(_ title: String, count: Int, close id: String, _ body: @escaping () throws -> [URL]) {
        workAsync(title, count: count, close: id) { try await Engine.offload(body) }
    }

    static func workAsync(_ title: String, count: Int, close id: String, _ body: @escaping @Sendable () async throws -> [URL]) {
        // The window closes before its work starts: a temporary copy it was showing stays until the work is done.
        let resume = InboxLeases.shared.pause()
        Windows.close(id)
        let point = NSEvent.mouseLocation
        let (job, token) = Runner.shared.startJob()
        HUD.shared.begin(title, count: count, near: point)
        Task {
            var report = RunReport()
            do {
                report.outputs = try await CancelToken.$current.withValue(token) { try await body() }
            } catch is Cancelled {
            } catch {
                report.failures.append(error.localizedDescription)
            }
            Runner.shared.finish(report, title: title, job: job)
            resume()
        }
    }
}

/// Header used by every editor: file name + how many files the change applies to.
struct EditorHeader: View {
    let files: [URL]
    let note: String
    var body: some View {
        HStack(spacing: 8) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: files.first?.path ?? ""))
                .resizable().frame(width: 22, height: 22)
            VStack(alignment: .leading, spacing: 0) {
                Text(files.count == 1 ? files[0].lastPathComponent : "\(files.count) files")
                    .font(.headline).lineLimit(1).truncationMode(.middle)
                Text(note).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
        }
    }
}

struct HotButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(Palette.onHot)
            .padding(.horizontal, 16).padding(.vertical, 7)
            .background(Palette.hot, in: Capsule())
            .opacity(configuration.isPressed ? 0.8 : 1)
    }
}

/// SwiftUI's `@State` is a compiler macro in the macOS 27 SDK, and its plugin ships only with
/// full Xcode. Command Line Tools can still build the plain property wrapper under another name.
typealias ViewState = SwiftUI.State
