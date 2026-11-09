import AppKit
import DragShiftCore
import QuickLookThumbnailing
import SwiftUI

/// Borderless floating panel that never steals focus from the app you're dragging out of.
final class RingPanel: NSPanel {
    var allowKey = false
    override var canBecomeKey: Bool { allowKey }
    override var canBecomeMain: Bool { false }

    var onEscape: (() -> Void)?
    override func cancelOperation(_ sender: Any?) { onEscape?() }
}

/// Transparent layer over the ring that receives the dropped file, and clicks once it's pinned.
final class RingDropView: NSView {
    weak var controller: RingController?
    override var isFlipped: Bool { true }

    override init(frame: NSRect) {
        super.init(frame: frame)
        registerForDraggedTypes(Incoming.dragTypes)
    }
    required init?(coder: NSCoder) { fatalError() }

    override func hitTest(_ point: NSPoint) -> NSView? { frame.contains(point) ? self : nil }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { MainActor.assumeIsolated { controller?.hide(restoreFocus: true) } } else { super.keyDown(with: event) }
    }
    /// Shift flips pages while the ring is pinned, same as during a drag.
    override func flagsChanged(with event: NSEvent) {
        if event.modifierFlags.contains(.shift) { MainActor.assumeIsolated { controller?.nextPage() } }
    }

    private func local(_ info: NSDraggingInfo) -> CGPoint { convert(info.draggingLocation, from: nil) }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        MainActor.assumeIsolated { controller?.track(local(sender), dragging: true) ?? [] }
    }
    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        MainActor.assumeIsolated { controller?.track(local(sender), dragging: true) ?? [] }
    }
    override func draggingExited(_ sender: NSDraggingInfo?) {
        MainActor.assumeIsolated { controller?.exited() }
    }
    override func prepareForDragOperation(_ sender: NSDraggingInfo) -> Bool { true }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        MainActor.assumeIsolated { controller?.drop(sender, at: local(sender)) ?? false }
    }

    private var tracking: NSTrackingArea?
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let t = NSTrackingArea(rect: bounds, options: [.mouseMoved, .activeAlways, .inVisibleRect, .mouseEnteredAndExited],
                               owner: self, userInfo: nil)
        addTrackingArea(t)
        tracking = t
    }
    override func mouseMoved(with event: NSEvent) {
        MainActor.assumeIsolated { _ = controller?.track(convert(event.locationInWindow, from: nil), dragging: false) }
    }
    override func mouseDown(with event: NSEvent) {
        MainActor.assumeIsolated { controller?.click(convert(event.locationInWindow, from: nil)) }
    }
    override func rightMouseDown(with event: NSEvent) {
        MainActor.assumeIsolated { controller?.toggleFavorite(at: convert(event.locationInWindow, from: nil)) }
    }
}

@MainActor
final class RingController {
    static let shared = RingController()

    let model = RingModel()
    private var panel: RingPanel
    private let dropView: RingDropView
    private var urls: [URL] = []
    private var catalogURLs: [URL] = []
    private var temporary = false
    private var anchor = NSPoint.zero
    private var springTimer: Timer?
    private var hideWork: DispatchWorkItem?
    private var clickMonitor: Any?
    private var dropHandled = false
    private var previousApp: NSRunningApplication?
    private(set) var openedAt = Date.distantPast

    /// (action, files, where the ring was, files are temporary copies)
    var onPerform: ((Action, [URL], NSPoint, Bool) -> Void)?

    var isShown: Bool { panel.isVisible && model.visible }

    private init() {
        panel = RingPanel(contentRect: NSRect(origin: .zero, size: G.size),
                          styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        let host = NSHostingView(rootView: RingView(model: model))
        host.frame = NSRect(origin: .zero, size: G.size)
        dropView = RingDropView(frame: NSRect(origin: .zero, size: G.size))
        let container = NSView(frame: NSRect(origin: .zero, size: G.size))
        container.addSubview(host)
        container.addSubview(dropView)
        panel.contentView = container
        dropView.controller = self
        setUp(panel)
    }

    private func setUp(_ p: RingPanel) {
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false
        p.level = .popUpMenu
        p.hidesOnDeactivate = false
        p.isMovable = false
        p.becomesKeyOnlyIfNeeded = true
        p.collectionBehavior = Floating.behavior
        p.onEscape = { [weak self] in self?.hide(restoreFocus: true) }
    }

    /// Front, and really on screen (see `Floating`).
    private func orderFront() {
        Floating.orderFront(panel, rebuild: { [unowned self] in
            let p = RingPanel(contentRect: panel.frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
            setUp(p)
            p.allowKey = panel.allowKey
            return p
        }, replace: { [unowned self] fresh in
            panel = fresh
            if fresh.allowKey {
                fresh.makeKeyAndOrderFront(nil)
                fresh.makeFirstResponder(dropView)
            }
        })
    }

    // MARK: Showing

    /// Drag mode: the file is still in your hand; letting go on a slice runs it.
    func showForDrag(_ peek: Incoming.Peek, at point: NSPoint) {
        urls = peek.urls
        temporary = peek.urls.isEmpty
        dropHandled = false
        configure(peek.catalogURLs, mode: .drag)
        place(at: point)
    }

    /// Click mode: files already chosen (menu, clipboard shortcut, Finder service).
    func showForClick(_ files: [URL], temporary: Bool = false, at point: NSPoint) {
        urls = files
        self.temporary = temporary
        configure(files, mode: .click)
        place(at: point)
        takeFocus()
    }

    /// The drag ended over the ring (let go in the middle, or the second finger lifted):
    /// keep it open with the files so you can glide to a slice and tap it.
    private func pin(_ files: [URL], temporary: Bool) {
        guard !files.isEmpty else { hide(); return }
        urls = files
        self.temporary = temporary
        model.mode = .pinned
        model.hovered = nil
        if !model.visible { model.visible = true }
        takeFocus()
        Log.line("ring pinned with \(files.count) file(s)")
    }

    private func takeFocus() {
        if let front = NSWorkspace.shared.frontmostApplication, front != NSRunningApplication.current { previousApp = front }
        panel.allowKey = true
        NSApp.activate(ignoringOtherApps: true)
        orderFront()
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(dropView)
        if clickMonitor == nil {
            // Clicking anywhere else closes it (that click goes to whatever you clicked).
            clickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
                MainActor.assumeIsolated { self?.hide() }
            }
        }
    }

    private func configure(_ files: [URL], mode: RingModel.Mode) {
        hideWork?.cancel()
        catalogURLs = files
        model.mode = mode
        model.armed = nil
        model.pages = Catalog.pages(for: files, hidden: Prefs.hidden, favorites: Prefs.favorites, recipes: Prefs.recipes,
                                    printPresets: Prefs.allPrintPresets)
        model.favorites = Set(Prefs.favorites)
        model.page = 0
        model.hovered = nil
        model.hoveredTab = nil
        let names = files.map(\.lastPathComponent)
        let real = files.filter { FileManager.default.fileExists(atPath: $0.path) }
        let kind = FileInspector.kind(of: files)
        model.usual = Prefs.usual(for: kind)
        if files.count == 1 {
            model.title = real.isEmpty ? "From \(kind.label.lowercased())" : names[0]
        } else {
            model.title = "\(files.count) \(kind == .other ? "files" : kind.label.lowercased() + "s")"
        }
        if real.isEmpty {
            model.subtitle = files.count == 1 ? kind.label : "\(files.count) items"
        } else {
            let bytes = real.reduce(Int64(0)) { $0 + FileInspector.size(of: $1) }
            model.subtitle = ByteFormat.string(bytes)
        }
        model.thumbnail = real.first.map { NSWorkspace.shared.icon(forFile: $0.path) }
        if let first = real.first { loadThumbnail(first) }
    }

    private func loadThumbnail(_ url: URL) {
        let req = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: 68, height: 60),
                                               scale: NSScreen.main?.backingScaleFactor ?? 2, representationTypes: .thumbnail)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: req) { [weak self] rep, _ in
            guard let img = rep?.nsImage else { return }
            Task { @MainActor in
                guard let self, self.catalogURLs.first == url else { return }
                self.model.thumbnail = img
            }
        }
    }

    private func place(at point: NSPoint) {
        anchor = point
        openedAt = Date()
        let screen = NSScreen.screens.first { NSMouseInRect(point, $0.frame, false) } ?? NSScreen.main
        var origin = NSPoint(x: point.x - G.center.x, y: point.y - (G.size.height - G.center.y))
        if let vf = screen?.visibleFrame {
            // Keep the disc and tabs on screen (the clear margin may hang off the edge).
            let left = origin.x + G.center.x - G.disc, right = origin.x + G.center.x + G.disc
            let top = origin.y + G.size.height - (G.center.y - G.disc)
            let bottom = origin.y + G.size.height - G.hintY - 14
            if left < vf.minX { origin.x += vf.minX - left }
            if right > vf.maxX { origin.x -= right - vf.maxX }
            if top > vf.maxY { origin.y -= top - vf.maxY }
            if bottom < vf.minY { origin.y += vf.minY - bottom }
        }
        panel.setFrameOrigin(origin)
        orderFront()
        model.visible = true
        Live.shared.ringOpen = true
        Feedback.tick(strong: true)
    }

    func nextPage() {
        guard model.pages.count > 1, model.armed == nil else { return }
        model.page = (model.page + 1) % model.pages.count
        model.hovered = nil
        Feedback.tick(strong: true)
    }

    /// Double-tap Shift (or the extra finger): get the last thing you did to this kind of file
    /// ready; letting go anywhere on the ring does it again.
    func repeatLast() {
        let kind = FileInspector.kind(of: catalogURLs)
        let all = model.pages.flatMap(\.actions)
        guard let id = Prefs.lastAction(for: kind) else {
            HUD.shared.flash("Nothing to repeat yet", "Pick something once, then double-tap to repeat it", near: anchor)
            return
        }
        let action = all.first { $0.id == id } ?? Prefs.recipes.first { $0.id == id }?.action
            ?? SizeLimit.kb(fromStep: id).map(SizeLimit.action(kb:))
            ?? Prefs.printPreset(step: id)?.action
        guard let action else { return }
        model.armed = action
        model.hovered = nil
        Feedback.tick(strong: true)
        Log.line("repeat armed: \(action.id)")
    }

    func hide(restoreFocus: Bool = false) {
        springTimer?.invalidate()
        if let m = clickMonitor { NSEvent.removeMonitor(m); clickMonitor = nil }
        if restoreFocus, let app = previousApp { app.activate() }
        previousApp = nil
        guard panel.isVisible else { return }
        model.visible = false
        Live.shared.ringOpen = false
        panel.allowKey = false
        let work = DispatchWorkItem { [weak self] in
            guard let self, !self.model.visible else { return }
            self.panel.orderOut(nil)
            self.model.hovered = nil
            self.model.armed = nil
        }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.18, execute: work)
    }

    /// The drag ended. If it ended over the ring but no drop arrived (it can happen when the
    /// ring appears under a still pointer), pin it; anywhere else, get out of the way.
    func dragEnded(peek: Incoming.Peek) {
        guard model.mode == .drag, panel.isVisible else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            guard let self, self.model.mode == .drag, self.panel.isVisible, !self.dropHandled else { return }
            let over = self.panel.frame.insetBy(dx: 30, dy: 30).contains(NSEvent.mouseLocation)
            guard over, !peek.urls.isEmpty else { self.hide(); return }
            if let armed = self.model.armed {
                self.hide()
                self.onPerform?(armed, peek.urls, self.anchor, false)
            } else {
                self.pin(peek.urls, temporary: false)
            }
        }
    }

    // MARK: Pointer

    @discardableResult
    func track(_ p: CGPoint, dragging: Bool) -> NSDragOperation {
        hideWork?.cancel()
        if dragging && !model.visible { model.visible = true }
        if model.armed != nil { return .copy }
        let hit = G.hit(p, slices: model.actions.count, tabs: model.pages.count, byDirection: true)
        var newSlice: Int? = nil
        var newTab: Int? = nil
        switch hit {
        case .slice(let i): newSlice = i
        case .tab(let i): newTab = i
        default: break
        }
        if newSlice != model.hovered {
            model.hovered = newSlice
            if newSlice != nil { Feedback.tick() }
        }
        if newTab != model.hoveredTab {
            model.hoveredTab = newTab
            springTimer?.invalidate()
            // Spring-loaded tabs: hover a page name for a moment to flip to it, no clicking.
            if let t = newTab, t != model.page {
                springTimer = Timer.scheduledTimer(withTimeInterval: 0.3, repeats: false) { [weak self] _ in
                    MainActor.assumeIsolated {
                        guard let self, self.model.hoveredTab == t else { return }
                        self.model.page = t
                        self.model.hovered = nil
                        Feedback.tick(strong: true)
                    }
                }
            }
        }
        // Accept everywhere: a slice runs it, the middle pins the ring open.
        return .copy
    }

    func exited() {
        model.hovered = nil
        model.hoveredTab = nil
        guard model.mode == .drag else { return }
        // Left the ring: get out of the way so the file can be dropped somewhere else.
        let work = DispatchWorkItem { [weak self] in self?.hide() }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.45, execute: work)
    }

    func drop(_ info: NSDraggingInfo, at p: CGPoint) -> Bool {
        dropHandled = true
        let where_ = anchor
        if let armed = model.armed {
            Incoming.receive(info.draggingPasteboard) { [weak self] files, temp in
                guard !files.isEmpty else { return }
                self?.onPerform?(armed, files, where_, temp)
            }
            Log.line("drop: repeat \(armed.id)")
            hide()
            return true
        }
        if case .slice(let i) = G.hit(p, slices: model.actions.count, tabs: model.pages.count, byDirection: true),
           model.actions.indices.contains(i) {
            let action = model.actions[i]
            Incoming.receive(info.draggingPasteboard) { [weak self] files, temp in
                guard !files.isEmpty else { return }
                self?.onPerform?(action, files, where_, temp)
            }
            Log.line("drop: \(action.id)")
            hide()
            return true
        }
        // Middle or a page name: keep the ring open with the file in it.
        Incoming.receive(info.draggingPasteboard) { [weak self] files, temp in
            self?.pin(files, temporary: temp)
        }
        Log.line("drop: middle, pinning")
        return true
    }

    func click(_ p: CGPoint) {
        let hit = G.hit(p, slices: model.actions.count, tabs: model.pages.count, byDirection: true)
        switch hit {
        case .slice(let i) where model.actions.indices.contains(i):
            let action = model.actions[i]
            let files = urls, temp = temporary, where_ = anchor
            // Editors and sharing open their own windows; everything else hands focus back.
            let opensWindow = action.editor != nil || ["share.airdrop", "share.mail", "share.messages", "share.more"].contains(action.id)
            hide(restoreFocus: !opensWindow)
            onPerform?(action, files, where_, temp)
        case .tab(let i):
            model.page = i
            model.hovered = nil
        default:
            hide(restoreFocus: true)
        }
    }

    /// Right-click a slice (when the ring is pinned or opened from the menu) to add it to
    /// Favorites or take it off.
    func toggleFavorite(at p: CGPoint) {
        guard case .slice(let i) = G.hit(p, slices: model.actions.count, tabs: model.pages.count, byDirection: true),
              model.actions.indices.contains(i) else { return }
        let action = model.actions[i]
        let added = Prefs.toggleFavorite(action.id)
        let page = model.pages[model.page].id
        model.pages = Catalog.pages(for: catalogURLs, hidden: Prefs.hidden, favorites: Prefs.favorites, recipes: Prefs.recipes,
                                    printPresets: Prefs.allPrintPresets)
        model.favorites = Set(Prefs.favorites)
        model.page = model.pages.firstIndex { $0.id == page } ?? 0
        model.hovered = nil
        Feedback.tick(strong: true)
        HUD.shared.flash(added ? "Added to Favorites" : "Removed from Favorites", action.title, near: anchor)
    }
}

/// Trackpad tick when the pointer crosses into a slice, plus a soft sound on finish.
enum Feedback {
    static func tick(strong: Bool = false) {
        guard Prefs.d.bool(forKey: Key.haptics) else { return }
        NSHapticFeedbackManager.defaultPerformer.perform(strong ? .levelChange : .alignment, performanceTime: .now)
    }

    static func done(ok: Bool) {
        guard Prefs.d.bool(forKey: Key.sound) else { return }
        NSSound(named: ok ? "Pop" : "Basso")?.play()
    }
}
