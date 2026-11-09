import AppKit
import DragShiftCore
import SwiftUI

/// Favourites shelf: while you drag a file, a thin glowing line sits on one side of the screen.
/// Push the file to that edge and your favourite actions slide out as big drop targets.
/// (The side edge, not the top: dragging to the top edge opens Mission Control.)
@MainActor
final class ShelfModel: ObservableObject {
    @Published var actions: [Action] = []
    @Published var hovered: Int?
    @Published var expanded = false
    @Published var visible = false
    @Published var left = false
}

enum ShelfG {
    static let width: CGFloat = 112
    static let tileH: CGFloat = 64
    static let gap: CGFloat = 8
    static let header: CGFloat = 34
    static let pad: CGFloat = 10
    static func height(_ n: Int) -> CGFloat { header + CGFloat(n) * tileH + CGFloat(max(0, n - 1)) * gap + pad }
    /// Tile under a point (panel coordinates, top-left origin).
    static func tile(at p: CGPoint, count: Int) -> Int? {
        let y = p.y - header
        guard y >= 0 else { return nil }
        let i = Int(y / (tileH + gap))
        return i < count ? i : nil
    }
}

final class ShelfDropView: NSView {
    weak var shelf: FavoritesShelf?
    override var isFlipped: Bool { true }
    override init(frame: NSRect) {
        super.init(frame: frame)
        registerForDraggedTypes(Incoming.dragTypes)
    }
    required init?(coder: NSCoder) { fatalError() }
    override func hitTest(_ point: NSPoint) -> NSView? { frame.contains(point) ? self : nil }
    private func local(_ info: NSDraggingInfo) -> CGPoint { convert(info.draggingLocation, from: nil) }
    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        MainActor.assumeIsolated { shelf?.track(local(sender)) ?? [] }
    }
    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        MainActor.assumeIsolated { shelf?.track(local(sender)) ?? [] }
    }
    override func draggingExited(_ sender: NSDraggingInfo?) {
        MainActor.assumeIsolated { shelf?.exited() }
    }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        MainActor.assumeIsolated { shelf?.drop(sender, at: local(sender)) ?? false }
    }
}

@MainActor
final class FavoritesShelf {
    static let shared = FavoritesShelf()

    let model = ShelfModel()
    private var strip: NSPanel
    private var panel: NSPanel
    private let dropView: ShelfDropView
    private var area = NSRect.zero
    private var collapseWork: DispatchWorkItem?

    var onPerform: ((Action, [URL], NSPoint, Bool) -> Void)?

    private static func makePanel() -> NSPanel {
        let p = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false
        p.level = .popUpMenu
        p.hidesOnDeactivate = false
        p.collectionBehavior = Floating.behavior
        return p
    }

    private static func makeStrip() -> NSPanel {
        let p = makePanel()
        p.ignoresMouseEvents = true // only a hint; it never gets in the way of a drop
        return p
    }

    private init() {
        strip = Self.makeStrip()
        strip.contentView = NSHostingView(rootView: ShelfStrip(model: model))
        panel = Self.makePanel()
        dropView = ShelfDropView(frame: .zero)
        let host = NSHostingView(rootView: ShelfView(model: model))
        let container = NSView()
        container.addSubview(host)
        container.addSubview(dropView)
        host.autoresizingMask = [.width, .height]
        dropView.autoresizingMask = [.width, .height]
        panel.contentView = container
        dropView.shelf = self
    }

    private var side: String { Prefs.d.string(forKey: Key.shelf) ?? "right" }

    func dragBegan(_ peek: Incoming.Peek) {
        guard side != "off", !Prefs.paused else { return }
        let actions = Catalog.favoriteActions(Prefs.favorites, files: peek.catalogURLs, recipes: Prefs.recipes,
                                              printPresets: Prefs.allPrintPresets)
        guard !actions.isEmpty else { return }
        let mouse = NSEvent.mouseLocation
        guard let screen = NSScreen.screens.first(where: { NSMouseInRect(mouse, $0.frame, false) }) ?? NSScreen.main else { return }
        area = screen.visibleFrame
        model.actions = actions
        model.left = side == "left"
        model.hovered = nil
        model.expanded = false
        let h: CGFloat = 120
        let x = model.left ? area.minX : area.maxX - 6
        strip.setFrame(NSRect(x: x, y: area.midY - h / 2, width: 6, height: h), display: true)
        Floating.orderFront(strip, rebuild: Self.makeStrip) { [unowned self] in strip = $0 }
        model.visible = true
    }

    /// Pointer moved during the drag: open when it reaches the edge, close when it wanders off.
    func moved(_ loc: NSPoint) {
        guard model.visible else { return }
        let fromEdge = model.left ? loc.x - area.minX : area.maxX - loc.x
        if !model.expanded && fromEdge <= 4 && loc.y > area.minY && loc.y < area.maxY {
            expand(around: loc.y)
        } else if model.expanded && fromEdge > ShelfG.width + 90 {
            collapse()
        }
    }

    private func expand(around y: CGFloat) {
        collapseWork?.cancel()
        let h = min(ShelfG.height(model.actions.count), area.height - 20)
        let x = model.left ? area.minX + 6 : area.maxX - ShelfG.width - 6
        let top = min(max(y - h / 2, area.minY + 10), area.maxY - h - 10)
        panel.setFrame(NSRect(x: x, y: top, width: ShelfG.width, height: h), display: true)
        dropView.frame = NSRect(x: 0, y: 0, width: ShelfG.width, height: h)
        Floating.orderFront(panel, rebuild: Self.makePanel) { [unowned self] in panel = $0 }
        model.expanded = true
        Feedback.tick(strong: true)
    }

    private func collapse() {
        model.expanded = false
        model.hovered = nil
        let work = DispatchWorkItem { [weak self] in
            guard let self, !self.model.expanded else { return }
            self.panel.orderOut(nil)
        }
        collapseWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2, execute: work)
    }

    func dragEnded() {
        guard model.visible else { return }
        // A moment's grace so a drop on the shelf finishes first.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in
            guard let self else { return }
            self.model.visible = false
            self.model.expanded = false
            self.strip.orderOut(nil)
            self.panel.orderOut(nil)
        }
    }

    func track(_ p: CGPoint) -> NSDragOperation {
        collapseWork?.cancel()
        let i = ShelfG.tile(at: p, count: model.actions.count)
        if i != model.hovered {
            model.hovered = i
            if i != nil { Feedback.tick() }
        }
        return i == nil ? [] : .copy
    }

    func exited() {
        model.hovered = nil
    }

    func drop(_ info: NSDraggingInfo, at p: CGPoint) -> Bool {
        guard let i = ShelfG.tile(at: p, count: model.actions.count) else { return false }
        let action = model.actions[i]
        let at = NSPoint(x: model.left ? panel.frame.maxX + 190 : panel.frame.minX - 190, y: panel.frame.midY)
        Incoming.receive(info.draggingPasteboard) { [weak self] files, temp in
            guard !files.isEmpty else { return }
            self?.onPerform?(action, files, at, temp)
        }
        Log.line("shelf drop: \(action.id)")
        model.visible = false
        strip.orderOut(nil)
        panel.orderOut(nil)
        return true
    }
}

/// The thin glowing line at the edge.
struct ShelfStrip: View {
    @ObservedObject var model: ShelfModel
    var body: some View {
        Capsule()
            .fill(Palette.hot)
            .frame(width: 5, height: 110)
            .shadow(color: Palette.hotB.opacity(0.7), radius: 6)
            .opacity(model.visible && !model.expanded ? 0.95 : 0)
            .animation(.easeOut(duration: 0.2), value: model.visible)
            .animation(.easeOut(duration: 0.15), value: model.expanded)
    }
}

struct ShelfView: View {
    @ObservedObject var model: ShelfModel
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        VStack(spacing: ShelfG.gap) {
            HStack(spacing: 4) {
                Image(systemName: "star.fill").font(.system(size: 10, weight: .bold)).foregroundStyle(Palette.hotB)
                Text("Favorites").font(.system(size: 11, weight: .bold))
            }
            .frame(height: ShelfG.header - ShelfG.gap)
            ForEach(Array(model.actions.enumerated()), id: \.element.id) { i, a in
                let hot = model.hovered == i
                VStack(spacing: 4) {
                    Image(systemName: a.symbol).font(.system(size: 18, weight: .semibold))
                    Text(a.title.uppercased())
                        .font(.system(size: 9.5, weight: .bold))
                        .lineLimit(1).minimumScaleFactor(0.7)
                }
                .foregroundStyle(hot ? Palette.onHot : Color.primary)
                .frame(width: ShelfG.width - 2 * ShelfG.pad, height: ShelfG.tileH)
                .background {
                    if hot { RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Palette.hot) }
                    else {
                        RoundedRectangle(cornerRadius: 14, style: .continuous)
                            .fill(Color.white.opacity(scheme == .dark ? 0.1 : 0.6))
                    }
                }
                .scaleEffect(hot ? 1.05 : 1)
            }
            Spacer(minLength: 0)
        }
        .padding(.bottom, ShelfG.pad)
        .frame(width: ShelfG.width)
        .frame(maxHeight: .infinity, alignment: .top)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).strokeBorder(Color.white.opacity(scheme == .dark ? 0.15 : 0.5)))
        .opacity(model.expanded ? 1 : 0)
        .offset(x: model.expanded ? 0 : (model.left ? -30 : 30))
        .animation(.spring(response: 0.25, dampingFraction: 0.85), value: model.expanded)
        .animation(.easeOut(duration: 0.12), value: model.hovered)
    }
}
