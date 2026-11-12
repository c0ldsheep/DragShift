import AppKit
import DragShiftCore
import SwiftUI

/// The About tab's tip corner: a line that asks only after DragShift has done some work, and a coffee cup that is
/// tipped with the app's own move. Drag the cup, press Shift, and a small ring of sizes opens under it; let go over
/// one. A plain click works too. It lives only here: no popups, no reminders, and nothing that goes online.
struct CoffeeCorner: View {
    /// The snapshot pictures pass their own number; the app reads the live count, so it changes while this is open.
    var fixedForSnapshot: Int? = nil
    @AppStorage(Tally.key) private var counted = 0
    private var fixed: Int { fixedForSnapshot ?? counted }
    /// A frozen moment for the snapshot pictures: where the cup is and whether the ring is open.
    var frozen: (offset: CGSize, open: Bool)? = nil

    @ViewState private var offset = CGSize.zero
    @ViewState private var open = false
    @ViewState private var dragging = false
    @ViewState private var chosen: Tip.Size?
    @ViewState private var showCard = false
    @ViewState private var shiftWatch: Any?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    static let outer: CGFloat = 96, inner: CGFloat = 32, cup: CGFloat = 46
    static let space = "coffee"
    static let size = CGSize(width: outer * 2 + 8, height: outer + cup / 2 + 8)
    private var rest: CGPoint { CGPoint(x: Self.size.width / 2, y: Self.cup / 2 + 6) }
    private var at: CGSize { frozen?.offset ?? offset }
    private var isOpen: Bool { frozen?.open ?? open }
    private var over: Int? { isOpen ? Tip.slice(dx: at.width, dy: at.height, inner: Self.inner) : nil }

    var body: some View {
        VStack(spacing: 4) {
            countLine.font(.callout).multilineTextAlignment(.center)
            Text(hint).font(.caption).foregroundStyle(.secondary)
            ZStack {
                ForEach(0..<3, id: \.self) { i in slice(i) }
                    .opacity(isOpen ? 1 : 0)
                    .scaleEffect(isOpen ? 1 : 0.86, anchor: UnitPoint(x: 0.5, y: rest.y / Self.size.height))
                Ellipse().fill(Color.primary.opacity(0.10)).frame(width: 46, height: 10)
                    .position(x: rest.x, y: rest.y + Self.cup / 2 - 2).opacity(isOpen ? 0 : 1)
                cup.position(x: rest.x + at.width, y: rest.y + at.height)
            }
            .frame(width: Self.size.width, height: Self.size.height)
            .coordinateSpace(name: Self.space)
            .popover(isPresented: $showCard, arrowEdge: .trailing) { TipCard(size: chosen) }
        }
        .onAppear {
            // Shift pressed while the cup is held still doesn't move the mouse, so listen for the key itself.
            shiftWatch = NSEvent.addLocalMonitorForEvents(matching: .flagsChanged) { e in
                if dragging, e.modifierFlags.contains(.shift) { openRing() }
                return e
            }
        }
        .onDisappear { if let w = shiftWatch { NSEvent.removeMonitor(w) }; shiftWatch = nil }
    }

    /// "DragShift has fixed 214 files for you…", with the count in bold so it's easy to find.
    private var countLine: Text {
        let p = Tip.lineParts(fixed: fixed)
        return Text(p.before) + Text(p.count).bold() + Text(p.after)
    }

    /// The cup covers the slice it is over, so this line names it.
    private var hint: String {
        guard isOpen else { return "Drag the cup and press Shift, or just click it" }
        guard let i = over else { return "Let go over a size" }
        return "Let go: \(Tip.sizes[i].title)"
    }

    /// How far the cup has been pulled from its saucer: 0 at rest, 1 at the edge of the ring.
    private var pulled: CGFloat {
        min(1, (at.width * at.width + at.height * at.height).squareRoot() / (Self.outer - Self.cup / 2))
    }

    private var cup: some View {
        // What you grab is a plain circle that never changes; the drawing sits on top and may reach past it
        // (the handle, the wave, the drops).
        Color.clear
            .frame(width: Self.cup, height: Self.cup)
            .contentShape(Circle())
            .overlay(SpillingCup(spill: pulled).shadow(color: .black.opacity(0.16), radius: 3, y: 2).allowsHitTesting(false))
        .onTapGesture { chosen = nil; showCard = true }
        // Measured in the ring's space, which stays still. Measured on the cup itself the drag shook: the cup moves
        // under the pointer, so each move changed the next measurement.
        .gesture(DragGesture(minimumDistance: 2, coordinateSpace: .named(Self.space))
            .onChanged { g in
                dragging = true
                // No animation while the hand is on it: the cup sits exactly under the pointer.
                var still = Transaction(); still.disablesAnimations = true
                withTransaction(still) { offset = Self.held(g.translation) }
                if NSEvent.modifierFlags.contains(.shift) { openRing() }
            }
            .onEnded { _ in
                if let i = over { chosen = Tip.sizes[i]; showCard = true }
                dragging = false
                withAnimation(reduceMotion ? nil : .spring(response: 0.3, dampingFraction: 0.75)) { offset = .zero; open = false }
            })
        .accessibilityLabel("Buy me a coffee")
        .accessibilityAddTraits(.isButton)
        .help("Buy me a coffee")
    }

    private func openRing() {
        guard !open else { return }
        withAnimation(reduceMotion ? nil : .easeOut(duration: 0.16)) { open = true }
    }

    /// The cup stays inside the ring's half-circle (and barely above its saucer), so it can't be dragged over text.
    static func held(_ t: CGSize) -> CGSize {
        var x = t.width, y = max(t.height, -4)
        let far = (x * x + y * y).squareRoot(), most = outer - cup / 2
        if far > most { x *= most / far; y *= most / far }
        return CGSize(width: x, height: y)
    }

    private func slice(_ i: Int) -> some View {
        let from = Double(180 - 60 * (i + 1)) + 1.5, to = Double(180 - 60 * i) - 1.5, hot = over == i
        let mid = (from + to) / 2 * .pi / 180, r = Double(Self.inner + Self.outer) / 2
        let wedge = Wedge(from: from, to: to, inner: Self.inner + 4, outer: Self.outer, center: rest)
        return ZStack {
            if hot { wedge.fill(Palette.gold.opacity(0.55)) } else { wedge.fill(Color.primary.opacity(0.07)) }
            wedge.stroke(hot ? Palette.goldRim : Color.primary.opacity(0.14), lineWidth: 1)
            VStack(spacing: 0) {
                Text(Tip.sizes[i].name).font(.system(size: 11, weight: .semibold))
                Text("$\(Tip.sizes[i].dollars)").font(.system(size: 10)).opacity(0.8)
            }
            .foregroundStyle(hot ? Palette.onHot : Color.primary)
            .opacity(hot ? 0 : 1)
            .position(x: rest.x + cos(mid) * r, y: rest.y + sin(mid) * r)
        }
    }
}

/// A slice of a ring. Angles are in degrees, 0 = right and 90 = down, as on screen.
private struct Wedge: Shape {
    let from: Double, to: Double
    let inner: CGFloat, outer: CGFloat
    let center: CGPoint

    func path(in rect: CGRect) -> Path {
        func point(_ degrees: Double, _ r: CGFloat) -> CGPoint {
            let a = degrees * .pi / 180
            return CGPoint(x: center.x + cos(a) * r, y: center.y + sin(a) * r)
        }
        let steps = stride(from: from, through: to, by: 2).map { $0 } + [to]
        var p = Path()
        p.move(to: point(from, inner))
        for a in steps { p.addLine(to: point(a, outer)) }
        for a in steps.reversed() { p.addLine(to: point(a, inner)) }
        p.closeSubpath()
        return p
    }
}

/// What opens when a size is picked (or the cup is clicked): a QR code to scan and one button to copy the address.
/// Few words on purpose. The QR code is drawn here by the app's own QR maker, so this needs no internet either.
struct TipCard: View {
    let size: Tip.Size?
    @ViewState private var copied = false
    private static let code: CGImage? = try? QR.make(Tip.address, side: 600)
    /// "0x81fE…e09A": enough to check the pasted address against.
    static let short = Tip.address.prefix(6) + "…" + Tip.address.suffix(4)

    var body: some View {
        VStack(spacing: 14) {
            Text(size?.title ?? "Buy me a coffee").font(.title3.weight(.semibold))
            if let code = Self.code {
                Image(decorative: code, scale: 1).interpolation(.none).resizable().frame(width: 160, height: 160)
                    .padding(10).background(Color.white, in: RoundedRectangle(cornerRadius: 12))
                    .accessibilityLabel("QR code of the tip address")
            }
            Text("Scan with your crypto wallet.\nAny amount works.")
                .font(.callout).multilineTextAlignment(.center).fixedSize(horizontal: false, vertical: true)
            Button(copied ? "Copied \(Self.short)" : "Copy address") {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(Tip.address, forType: .string)
                copied = true
            }
            .buttonStyle(GoldButtonStyle())
            Text("ETH or USDC on Ethereum").font(.caption).foregroundStyle(.secondary)
        }
        .padding(22)
        .frame(width: 260)
    }
}

/// The card's one button, in the cup's flat gold.
struct GoldButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(Palette.onHot)
            .padding(.horizontal, 18).padding(.vertical, 8)
            .background(Palette.gold, in: Capsule())
            .overlay(Capsule().strokeBorder(Palette.goldRim, lineWidth: 1))
            .opacity(configuration.isPressed ? 0.8 : 1)
    }
}

/// The cup: full and level on its saucer, tipping as it is pulled away, the coffee going over its lip by the time
/// it reaches the edge of the ring. `spill` runs from 0 to 1 and can be animated (the cup springs back upright).
struct SpillingCup: View, Animatable {
    var spill: CGFloat
    /// From the cup's left side to the outside of its handle, in points.
    var width: CGFloat = 54

    var animatableData: CGFloat {
        get { spill }
        set { spill = newValue }
    }

    var body: some View {
        // Room all round for the wave and the drops, which fly past the cup's own outline.
        Canvas { ctx, size in
            CupArt.draw(&ctx, at: CGPoint(x: size.width / 2, y: size.height / 2), width: width, spill: spill)
        }
        .frame(width: width * 1.42, height: width * 1.24)
        .accessibilityHidden(true)
    }
}

extension CupArt {
    static let yellow = Color(red: 254 / 255, green: 212 / 255, blue: 32 / 255)
    static let shadeYellow = Color(red: 242 / 255, green: 188 / 255, blue: 20 / 255)
    static let rimYellow = Color(red: 254 / 255, green: 221 / 255, blue: 82 / 255)
    static let coffee = Color(red: 89 / 255, green: 45 / 255, blue: 20 / 255)
    static let coffeeShine = Color(red: 132 / 255, green: 73 / 255, blue: 42 / 255)

    /// Slow at both ends, quick in the middle; clamped to 0...1.
    static func ease(_ x: CGFloat) -> CGFloat {
        let c = min(1, max(0, x))
        return c * c * (3 - 2 * c)
    }

    static func point(_ v: [CGFloat], _ i: Int) -> CGPoint { CGPoint(x: v[2 * i], y: v[2 * i + 1]) }

    static func path(_ v: [CGFloat], around c: CGPoint = .zero, scale: CGFloat = 1) -> Path {
        var p = Path()
        for i in 0..<v.count / 2 {
            let q = CGPoint(x: c.x + v[2 * i] * scale, y: c.y + v[2 * i + 1] * scale)
            if i == 0 { p.move(to: q) } else { p.addLine(to: q) }
        }
        p.closeSubpath()
        return p
    }

    /// For each drop, the point of the coffee's outline it leaves from (the one nearest its start in the full spill).
    static let dropRoots: [Int] = drops.map { d in
        (0..<spill.count / 2).min { a, b in
            let p = point(spill, a), q = point(spill, b)
            return hypot(p.x - d.from.x, p.y - d.from.y) < hypot(q.x - d.from.x, q.y - d.from.y)
        } ?? 0
    }

    /// Draws the cup with the point it turns around at `at`. `spill`: 0 = at rest, 1 = the full spill.
    static func draw(_ ctx: inout GraphicsContext, at: CGPoint, width: CGFloat, spill t: CGFloat) {
        let c = min(1, max(0, t))
        let tip = 1 - (1 - c) * (1 - c)         // the cup answers the hand at once, then settles into its full tilt...
        let pour = ease((c - 0.1) / 0.9)        // ...and the coffee follows a moment behind it
        ctx.translateBy(x: at.x, y: at.y)
        ctx.scaleBy(x: width / 1000, y: width / 1000)
        ctx.rotate(by: .degrees(tilt * Double(tip)))

        var cup = path(outline)
        cup.addPath(path(hole))
        ctx.fill(cup, with: .color(yellow), style: FillStyle(eoFill: true))
        ctx.fill(path(shade), with: .color(shadeYellow))
        ctx.fill(path(rim), with: .color(rimYellow))
        ctx.fill(path(opening), with: .color(shadeYellow))

        // The coffee: every point of its outline is part-way between "at rest" and "full spill".
        var now = [CGFloat](repeating: 0, count: calm.count)
        for i in 0..<calm.count { now[i] = calm[i] + (spill[i] - calm[i]) * pour }
        let liquid = path(now)
        ctx.fill(liquid, with: .color(coffee))

        // Drops leave the wave one after another, the farthest-flying first, and grow as they go.
        for (i, d) in drops.enumerated() {
            let wait = 0.25 + 0.5 * CGFloat(i) / CGFloat(max(1, drops.count - 1))
            let k = ease((pour - wait) / (1 - wait))
            guard k > 0.02 else { continue }
            let root = point(now, dropRoots[i])
            let c = CGPoint(x: root.x + (d.to.x - root.x) * k, y: root.y + (d.to.y - root.y) * k)
            ctx.fill(path(d.shape, around: c, scale: k), with: .color(coffee))
        }
        // The lighter streaks come in at the end, and only where there is coffee to shine.
        let glint = ease((pour - 0.7) / 0.3)
        if glint > 0.02 {
            var onCoffee = ctx
            onCoffee.clip(to: liquid)
            for s in shine { onCoffee.fill(path(s), with: .color(coffeeShine.opacity(Double(glint)))) }
        }
    }
}
