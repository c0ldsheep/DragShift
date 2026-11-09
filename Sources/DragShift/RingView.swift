import AppKit
import DragShiftCore
import SwiftUI

/// Sizes shared by the drawing (SwiftUI) and the hit-testing (AppKit), so they always agree.
enum G {
    static let size = CGSize(width: 440, height: 440)
    static let center = CGPoint(x: 220, y: 200)
    static let outer: CGFloat = 148
    static let inner: CGFloat = 64
    static let hub: CGFloat = 56
    static let disc: CGFloat = outer + 10
    static let tabsY: CGFloat = center.y + disc + 26
    static let tabWidth: CGFloat = 70
    static let tabHeight: CGFloat = 26
    static let tabGap: CGFloat = 6
    static let hintY: CGFloat = tabsY + 30

    static func tabRects(_ n: Int) -> [CGRect] {
        let total = CGFloat(n) * tabWidth + CGFloat(max(0, n - 1)) * tabGap
        let x0 = center.x - total / 2
        return (0..<n).map {
            CGRect(x: x0 + CGFloat($0) * (tabWidth + tabGap), y: tabsY - tabHeight / 2, width: tabWidth, height: tabHeight)
        }
    }

    enum Hit: Equatable { case none, hub, slice(Int), tab(Int) }

    /// Point is in panel coordinates, top-left origin. In drag mode every point outside the hub
    /// picks a slice by direction, so you only have to push the file the right way, not land on it.
    static func hit(_ p: CGPoint, slices n: Int, tabs: Int, byDirection: Bool) -> Hit {
        for (i, r) in tabRects(tabs).enumerated() where tabs > 1 && r.insetBy(dx: -3, dy: -6).contains(p) { return .tab(i) }
        let dx = p.x - center.x, dy = p.y - center.y
        let d = hypot(dx, dy)
        if d < hub { return .hub }
        guard n > 0 else { return .none }
        if !byDirection && d > outer + 6 { return .none }
        let span = 360 / Double(n)
        var a = atan2(Double(dy), Double(dx)) * 180 / .pi + 90 // 0 = straight up, clockwise
        a = (a + 360 + span / 2).truncatingRemainder(dividingBy: 360)
        return .slice(min(n - 1, Int(a / span)))
    }
}

/// An annular slice with rounded corners: the shape is drawn inset by `corner` and stroked
/// with a round join of the same width, which rounds every corner by exactly `corner`.
struct Petal: Shape {
    var start: Double   // degrees, 0 = up, clockwise
    var end: Double
    var inner: CGFloat
    var outer: CGFloat
    var gap: CGFloat = 3
    var corner: CGFloat = 9

    func path(in rect: CGRect) -> Path {
        let c = CGPoint(x: rect.midX, y: rect.midY)
        let ro = outer - corner, ri = inner + corner
        let off = gap / 2 + corner
        func inset(_ r: CGFloat) -> Double { Double(asin(min(1, off / r))) * 180 / .pi }
        let a1o = start + inset(ro), a2o = end - inset(ro)
        let a1i = start + inset(ri), a2i = end - inset(ri)
        var p = Path()
        guard a2o > a1o, a2i > a1i else { return p }
        // SwiftUI angles: 0 = +x. Subtract 90 so 0 means "up".
        p.addArc(center: c, radius: ro, startAngle: .degrees(a1o - 90), endAngle: .degrees(a2o - 90), clockwise: false)
        p.addArc(center: c, radius: ri, startAngle: .degrees(a2i - 90), endAngle: .degrees(a1i - 90), clockwise: true)
        p.closeSubpath()
        return p
    }
}

@MainActor
final class RingModel: ObservableObject {
    enum Mode { case drag, pinned, click }

    @Published var pages: [RingPage] = []
    @Published var page = 0
    @Published var hovered: Int?
    @Published var hoveredTab: Int?
    @Published var title = ""
    @Published var subtitle = ""
    @Published var thumbnail: NSImage?
    @Published var mode: Mode = .drag
    @Published var visible = false
    @Published var busy = false
    /// Set by double-tap: letting go anywhere repeats this.
    @Published var armed: Action?
    @Published var favorites: Set<String> = []
    /// "Your usual" for these files: that slice has a soft pulsing edge.
    @Published var usual: String?

    var actions: [Action] { pages.indices.contains(page) ? pages[page].actions : [] }
    var hoveredAction: Action? { hovered.flatMap { actions.indices.contains($0) ? actions[$0] : nil } }

    /// "Under 50 KB" on Favorites counts as the Under KB slice.
    func isUsual(_ a: Action) -> Bool { usual != nil && Habits.sliceID(for: a.id) == usual }

    /// When the usual isn't on this page, the first page that has it glows instead.
    var usualTab: Int? {
        guard usual != nil, !actions.contains(where: isUsual) else { return nil }
        return pages.firstIndex { $0.actions.contains(where: isUsual) }
    }
}

/// A slow breath, 0...1, for things that glow. Holds still when Reduce Motion is on.
struct Pulse<Content: View>: View {
    /// Snapshots draw one chosen moment of the breath.
    @MainActor static var frozen: Double? { PulseClock.frozen }
    var period = 1.6
    @ViewBuilder var content: (Double) -> Content
    private let still = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion

    var body: some View {
        TimelineView(.animation(minimumInterval: 1 / 30, paused: still || Self.frozen != nil)) { t in
            content(Self.frozen ?? (still ? 1 : (1 - cos(t.date.timeIntervalSinceReferenceDate * 2 * .pi / period)) / 2))
        }
    }
}

@MainActor enum PulseClock { static var frozen: Double? }

enum Palette {
    static let hotA = Color(red: 1.0, green: 0.55, blue: 0.22)
    static let hotB = Color(red: 1.0, green: 0.24, blue: 0.43)
    static var hot: LinearGradient { LinearGradient(colors: [hotA, hotB], startPoint: .topLeading, endPoint: .bottomTrailing) }
    /// Text on the hot gradient: dark ink, not white. White on orange measured 2.3:1 (unreadable for many people);
    /// this ink is 5.2:1 at the pink end and 7.7:1 at the orange end (the site's buttons use the same, #24131c).
    static let onHot = Color(red: 0.141, green: 0.075, blue: 0.11)
    /// The coffee cup: one flat golden yellow (#FFDD00), no gradient and no shine. Dark ink on it is 13:1.
    static let gold = Color(red: 1.0, green: 0.867, blue: 0.0)
    static let goldRim = Color(red: 0.80, green: 0.64, blue: 0.0)
}

struct RingView: View {
    @ObservedObject var model: RingModel
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        ZStack {
            ring
                .frame(width: G.disc * 2, height: G.disc * 2)
                .position(G.center)
            if model.pages.count > 1 {
                tabs.position(x: G.center.x, y: G.tabsY)
            }
            Text(hint)
                .font(.system(size: 10.5, weight: .medium))
                .foregroundStyle(.secondary)
                .padding(.horizontal, 10).padding(.vertical, 4)
                .background(.thinMaterial, in: Capsule())
                .position(x: G.center.x, y: G.hintY)
        }
        .frame(width: G.size.width, height: G.size.height)
        .scaleEffect(model.visible ? 1 : 0.8, anchor: UnitPoint(x: G.center.x / G.size.width, y: G.center.y / G.size.height))
        .opacity(model.visible ? 1 : 0)
        .animation(.spring(response: 0.26, dampingFraction: 0.8), value: model.visible)
    }

    private var hint: String {
        if model.armed != nil { return "Let go anywhere to repeat" }
        switch model.mode {
        case .drag: return "Let go on a slice, or in the middle to keep it open"
        case .pinned: return "Tap a slice  ·  ⇧ next page  ·  right-click ★  ·  Esc"
        case .click: return "Click a slice  ·  right-click ★  ·  Esc to close"
        }
    }

    private var sliceFill: Color { scheme == .dark ? Color.white.opacity(0.13) : Color.white.opacity(0.62) }
    private var sliceInk: Color { scheme == .dark ? Color.white.opacity(0.92) : Color.black.opacity(0.78) }

    private var ring: some View {
        ZStack {
            Circle()
                .fill(.ultraThinMaterial)
                .overlay(Circle().fill(scheme == .dark ? Color.black.opacity(0.18) : Color.white.opacity(0.18)))
                .overlay(Circle().strokeBorder(Color.white.opacity(scheme == .dark ? 0.16 : 0.55), lineWidth: 1))
                .shadow(color: .black.opacity(0.28), radius: 26, y: 12)

            ForEach(Array(model.actions.enumerated()), id: \.element.id) { i, action in
                slice(i, action)
            }
            .id(model.page)
            .transition(.asymmetric(insertion: .scale(scale: 0.85).combined(with: .opacity), removal: .opacity))

            hub
        }
        .animation(.spring(response: 0.22, dampingFraction: 0.75), value: model.hovered)
        .animation(.easeOut(duration: 0.16), value: model.page)
    }

    private func slice(_ i: Int, _ action: Action) -> some View {
        let n = model.actions.count
        let span = 360 / Double(n)
        let mid = Double(i) * span
        let hot = model.hovered == i
        let petal = Petal(start: mid - span / 2, end: mid + span / 2, inner: G.inner, outer: G.outer)
        let rad = (mid - 90) * .pi / 180
        let r = (G.inner + G.outer) / 2 + 2
        let labelPos = CGPoint(x: G.disc + cos(rad) * r, y: G.disc + sin(rad) * r)
        let width = min(78, CGFloat(2 * .pi * Double(r) / Double(n)) - 10)

        return ZStack {
            Group {
                if hot && model.armed == nil {
                    ZStack {
                        petal.fill(Palette.hot)
                        petal.stroke(Palette.hot, style: StrokeStyle(lineWidth: 18, lineJoin: .round))
                    }
                    .compositingGroup()
                    .shadow(color: Palette.hotB.opacity(0.45), radius: 10, y: 3)
                } else {
                    ZStack {
                        petal.fill(Color.white)
                        petal.stroke(Color.white, style: StrokeStyle(lineWidth: 18, lineJoin: .round))
                    }
                    .compositingGroup()
                    .opacity(scheme == .dark ? 0.13 : 0.62)
                }
            }
            .frame(width: G.disc * 2, height: G.disc * 2)

            if model.isUsual(action) && !hot && model.armed == nil {
                usualEdge(petal).frame(width: G.disc * 2, height: G.disc * 2)
            }

            VStack(spacing: 4) {
                Image(systemName: action.symbol)
                    .font(.system(size: 15, weight: .semibold))
                    .frame(height: 17)
                Text(action.title.uppercased())
                    .font(.system(size: 9.5, weight: .bold))
                    .tracking(0.2)
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
            }
            .foregroundStyle(hot ? Color.white : sliceInk)
            .frame(width: width)
            .position(labelPos)
        }
        .frame(width: G.disc * 2, height: G.disc * 2)
        .scaleEffect(hot ? 1.045 : 1)
        .opacity(model.armed == nil ? 1 : 0.35)
    }

    /// A 2.5-point band just inside the slice's edge: the whole slice shape minus a slightly
    /// smaller one (the slice is drawn as its path widened by a round 18-point stroke).
    private func usualEdge(_ petal: Petal) -> some View {
        Pulse { k in
            ZStack {
                ZStack {
                    petal.fill(Palette.hot)
                    petal.stroke(Palette.hot, style: StrokeStyle(lineWidth: 18, lineJoin: .round))
                }
                ZStack {
                    petal.fill(Color.black)
                    petal.stroke(Color.black, style: StrokeStyle(lineWidth: 13, lineJoin: .round))
                }
                .blendMode(.destinationOut)
            }
            .compositingGroup()
            .shadow(color: Palette.hotB.opacity(0.2 + 0.35 * k), radius: 3 + 5 * k)
            .opacity(0.4 + 0.6 * k)
        }
        .allowsHitTesting(false)
        .transition(.opacity)
    }

    private var hub: some View {
        ZStack {
            Circle()
                .fill(.regularMaterial)
                .overlay(Circle().strokeBorder(Color.white.opacity(scheme == .dark ? 0.12 : 0.6), lineWidth: 1))
                .shadow(color: .black.opacity(0.15), radius: 6, y: 2)
            if let a = model.armed {
                VStack(spacing: 2) {
                    Image(systemName: "arrow.clockwise").font(.system(size: 13, weight: .bold)).foregroundStyle(Palette.hotB)
                    Text(a.title)
                        .font(.system(size: 15, weight: .heavy))
                        .lineLimit(1).minimumScaleFactor(0.6)
                    Text("again").font(.system(size: 9.5, weight: .medium)).foregroundStyle(.secondary)
                }
                .padding(.horizontal, 10)
                .transition(.opacity)
            } else if let a = model.hoveredAction {
                VStack(spacing: 2) {
                    Text(a.title)
                        .font(.system(size: 16, weight: .heavy))
                        .lineLimit(1).minimumScaleFactor(0.6)
                    Text(model.subtitle)
                        .font(.system(size: 9.5, weight: .medium))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                .padding(.horizontal, 10)
                .transition(.opacity)
            } else {
                VStack(spacing: 3) {
                    if let t = model.thumbnail {
                        Image(nsImage: t)
                            .resizable().aspectRatio(contentMode: .fit)
                            .frame(width: 34, height: 30)
                            .shadow(color: .black.opacity(0.2), radius: 2, y: 1)
                    }
                    Text(model.title)
                        .font(.system(size: 10, weight: .semibold))
                        .lineLimit(1).truncationMode(.middle)
                    Text(model.subtitle)
                        .font(.system(size: 9, weight: .medium))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                .padding(.horizontal, 12)
                .transition(.opacity)
            }
        }
        .frame(width: G.hub * 2 - 4, height: G.hub * 2 - 4)
        .animation(.easeOut(duration: 0.12), value: model.hovered)
    }

    private var tabs: some View {
        HStack(spacing: G.tabGap) {
            ForEach(Array(model.pages.enumerated()), id: \.element.id) { i, p in
                let on = i == model.page
                Text(p.id == "favorites" ? "★ Faves" : p.title)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(on ? Palette.onHot : (model.hoveredTab == i ? Color.primary : Color.secondary))
                    .frame(width: G.tabWidth, height: G.tabHeight)
                    .background {
                        if on { Capsule().fill(Palette.hot) } else { Capsule().fill(.regularMaterial) }
                    }
                    .overlay(Capsule().strokeBorder(Color.white.opacity(on ? 0 : 0.35), lineWidth: 1))
                    .overlay {
                        if model.usualTab == i && model.armed == nil {
                            Pulse { k in
                                Capsule().strokeBorder(Palette.hot, lineWidth: 2)
                                    .shadow(color: Palette.hotB.opacity(0.2 + 0.35 * k), radius: 2 + 4 * k)
                                    .opacity(0.4 + 0.6 * k)
                            }
                        }
                    }
                    .scaleEffect(model.hoveredTab == i && !on ? 1.06 : 1)
            }
        }
        .animation(.easeOut(duration: 0.15), value: model.page)
        .animation(.easeOut(duration: 0.15), value: model.hoveredTab)
    }
}
