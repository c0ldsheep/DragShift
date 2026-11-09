import AppKit
import DragShiftCore

/// Menu bar icon and its menu.
@MainActor
final class StatusBar: NSObject, NSMenuDelegate {
    private let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let menu = NSMenu()

    var onConvertFiles: (() -> Void)?
    var onSettings: (() -> Void)?
    var onWelcome: (() -> Void)?
    var onClipboard: (() -> Void)?
    var onClipboardQR: (() -> Void)?

    func setup() {
        item.button?.image = Icon.menuBar(paused: Prefs.paused)
        item.button?.toolTip = "DragShift — drag a file, press Shift"
        menu.delegate = self
        item.menu = menu
    }

    func refreshIcon() { item.button?.image = Icon.menuBar(paused: Prefs.paused) }

    /// Point just under the icon, for the ring and the bubble.
    var anchor: NSPoint {
        guard let b = item.button, let w = b.window else { return NSEvent.mouseLocation }
        let r = w.convertToScreen(b.convert(b.bounds, to: nil))
        return NSPoint(x: r.midX, y: r.minY - G.disc - 24)
    }

    // MARK: Menu

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let paused = Prefs.paused
        let head = NSMenuItem(title: paused ? "DragShift is paused" : "Drag a file, then press ⇧ or tap a 2nd finger", action: nil, keyEquivalent: "")
        head.isEnabled = false
        menu.addItem(head)
        menu.addItem(.separator())
        add(paused ? "Resume DragShift" : "Pause DragShift", #selector(togglePause), "p")
        add("Convert Files…", #selector(convertFiles), "o")
        let clip = add("Open Clipboard in Ring" + (HotKey.currentTitle.map { "   \($0)" } ?? ""), #selector(clipboard), "")
        clip.toolTip = "Files you copied in Finder, a copied screenshot, or copied text"
        add("QR Code from Clipboard", #selector(clipboardQR), "")
        let watching = FolderWatcher.shared.activeCount
        if watching > 0 {
            let w = NSMenuItem(title: "Watching \(watching) folder\(watching == 1 ? "" : "s")", action: nil, keyEquivalent: "")
            w.isEnabled = false
            menu.addItem(w)
        }

        let recent = Runner.shared.recent.filter { FileManager.default.fileExists(atPath: $0.path) }
        if !recent.isEmpty {
            menu.addItem(.separator())
            let title = NSMenuItem(title: "Recent", action: nil, keyEquivalent: "")
            title.isEnabled = false
            menu.addItem(title)
            for url in recent.prefix(6) {
                let i = NSMenuItem(title: url.lastPathComponent, action: #selector(reveal(_:)), keyEquivalent: "")
                i.target = self
                i.representedObject = url
                let icon = NSWorkspace.shared.icon(forFile: url.path)
                icon.size = NSSize(width: 16, height: 16)
                i.image = icon
                menu.addItem(i)
            }
            let undo = add("Undo Last (move to Bin)", #selector(undoLast), "z")
            undo.isEnabled = Runner.shared.canUndo
        }
        menu.addItem(.separator())
        add("How to Use…", #selector(welcome), "")
        add("Settings…", #selector(settings), ",")
        menu.addItem(.separator())
        let quit = NSMenuItem(title: "Quit DragShift", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        menu.addItem(quit)
    }

    @discardableResult
    private func add(_ title: String, _ sel: Selector, _ key: String) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: sel, keyEquivalent: key)
        i.target = self
        menu.addItem(i)
        return i
    }

    @objc private func togglePause() {
        Prefs.paused.toggle()
        refreshIcon()
        if Prefs.paused { RingController.shared.hide() }
    }
    @objc private func convertFiles() { onConvertFiles?() }
    @objc private func clipboard() { onClipboard?() }
    @objc private func clipboardQR() { onClipboardQR?() }
    @objc private func settings() { onSettings?() }
    @objc private func welcome() { onWelcome?() }
    @objc private func undoLast() { Runner.shared.undo() }
    @objc private func reveal(_ sender: NSMenuItem) {
        if let u = sender.representedObject as? URL { NSWorkspace.shared.activateFileViewerSelecting([u]) }
    }
}

enum Icon {
    /// Template icon: a six-slice ring with one slice lit, like the real menu.
    static func menuBar(paused: Bool) -> NSImage {
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            let c = NSPoint(x: 9, y: 9)
            let ro: CGFloat = 8.2, ri: CGFloat = 4.3
            for i in 0..<6 {
                let a0 = 90 - CGFloat(i) * 60 - 26, a1 = a0 + 52
                let p = NSBezierPath()
                p.appendArc(withCenter: c, radius: ro, startAngle: a0, endAngle: a1)
                p.appendArc(withCenter: c, radius: ri, startAngle: a1, endAngle: a0, clockwise: true)
                p.close()
                NSColor.black.withAlphaComponent(paused ? 0.3 : (i == 0 ? 1 : 0.45)).setFill()
                p.fill()
            }
            NSColor.black.withAlphaComponent(paused ? 0.3 : 0.9).setFill()
            NSBezierPath(ovalIn: NSRect(x: 7, y: 7, width: 4, height: 4)).fill()
            return true
        }
        img.isTemplate = true
        return img
    }
}
