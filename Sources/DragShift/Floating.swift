import AppKit

/// DragShift's floating windows (the ring, the progress bubble, the Favorites shelf) live on every Space. macOS can
/// lose such a window from its Spaces (seen on 29 Oct 2026 after full-screen apps came and went): AppKit still thinks
/// it's showing, the window server shows nothing, and Shift seems to "do nothing". `orderFront` checks with the window
/// server and repairs a window that didn't reach the screen.
@MainActor
enum Floating {
    static let behavior: NSWindow.CollectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
    /// The window server takes a few tens of milliseconds to report a window it has just put up.
    static let settle = 0.15
    static var log: (String) -> Void = { Log.line($0) }

    /// What the window server says, not what AppKit believes.
    static func onScreen(_ w: NSWindow) -> Bool {
        guard w.windowNumber > 0,
              let info = CGWindowListCopyWindowInfo(.optionIncludingWindow, CGWindowID(w.windowNumber)) as? [[String: Any]]
        else { return false }
        return info.first?[kCGWindowIsOnscreen as String] as? Bool ?? false
    }

    /// `orderFrontRegardless`, made sure. If the window still isn't up a moment later it re-joins all Spaces; if that
    /// doesn't bring it back, its contents move into a fresh window from `rebuild`, handed to `replace` to keep.
    static func orderFront<P: NSPanel>(_ p: P, rebuild: @escaping () -> P, replace: @escaping (P) -> Void) {
        p.orderFrontRegardless()
        DispatchQueue.main.asyncAfter(deadline: .now() + settle) {
            guard lost(p) else { return }
            p.collectionBehavior = []
            p.collectionBehavior = behavior
            p.orderFrontRegardless()
            DispatchQueue.main.asyncAfter(deadline: .now() + settle) {
                guard lost(p) else {
                    log("window \(p.windowNumber) didn't show; re-joined all Spaces")
                    return
                }
                let fresh = rebuild()
                move(p, into: fresh)
                replace(fresh)
                log("window \(p.windowNumber) didn't show; moved into new window \(fresh.windowNumber)")
            }
        }
    }

    /// Ordered in as far as AppKit knows, yet not on screen, while there is a screen awake to show it on.
    private static func lost(_ p: NSWindow) -> Bool {
        p.isVisible && !onScreen(p) && !NSScreen.screens.isEmpty && CGDisplayIsAsleep(CGMainDisplayID()) == 0
    }

    static func move(_ old: NSWindow, into fresh: NSWindow) {
        let content = old.contentView
        let frame = old.frame
        old.contentView = NSView()
        old.orderOut(nil)
        fresh.contentView = content
        fresh.setFrame(frame, display: false)
        fresh.orderFrontRegardless()
    }

    /// Developer check: `DragShift --space-test` runs the last-resort repair on a test window and confirms the new
    /// window is on screen with the old one's contents. Exit code 0 when it worked.
    static func selfTest() -> Bool {
        log = { print($0) }
        func spin() { RunLoop.current.run(until: Date().addingTimeInterval(settle * 2)) }
        func make() -> NSPanel {
            let p = NSPanel(contentRect: NSRect(x: 200, y: 200, width: 120, height: 80),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
            p.level = .popUpMenu
            p.alphaValue = 0.02
            p.collectionBehavior = behavior
            return p
        }
        let old = make()
        let content = NSView()
        old.contentView = content
        var shown: NSPanel = old
        orderFront(old, rebuild: make) { shown = $0 }
        spin()
        print("healthy window shows and is kept:", onScreen(old), shown === old)
        let fresh = make()
        move(old, into: fresh)
        spin()
        let ok = onScreen(fresh) && !onScreen(old) && fresh.contentView === content
        print("rebuilt window shows with the same contents, old one gone:", ok)
        return ok && shown === old
    }
}
