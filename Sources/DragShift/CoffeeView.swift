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

    private var cup: some View {
        ZStack {
            Circle().fill(Palette.gold)
            Circle().strokeBorder(Palette.goldRim, lineWidth: 1)
            Image(systemName: "cup.and.saucer.fill").font(.system(size: 20, weight: .semibold)).foregroundStyle(Palette.onHot)
        }
        .frame(width: Self.cup, height: Self.cup)
        .shadow(color: .black.opacity(0.16), radius: 3, y: 2)
        .contentShape(Circle())
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
