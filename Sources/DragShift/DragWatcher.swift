import AppKit
import DragShiftCore

/// Notices when a file drag is in progress anywhere on the Mac, and turns Shift,
/// an extra finger on the trackpad, a mouse wheel click, a shake, or a pause into "open the ring".
///
/// How it sees drags without special permissions: every drag writes to the system
/// drag pasteboard, and its change count ticks. While the mouse button is down we
/// poll that count, the Shift key and the pointer at 60 Hz; when idle, 4 Hz.
@MainActor
final class DragWatcher {
    enum Trigger: String { case shift, finger, mouse, shake, hover }

    var onBegin: ((Incoming.Peek) -> Void)?
    var onTrigger: ((Trigger) -> Void)?
    var onEnd: (() -> Void)?
    /// Pointer position while a file is being dragged (for the Favorites shelf).
    var onMove: ((NSPoint) -> Void)?

    private(set) var dragging = false
    private(set) var peek = Incoming.Peek()

    private let pb = NSPasteboard(name: .drag)
    private var idleCount = 0
    private var pressed = false
    private var ignoreThisPress = false
    private var fast: Timer?
    private var idle: Timer?
    private var monitors: [Any] = []

    private var ticks = 0
    private var shiftWasDown = false
    private var otherButtonWasDown = false
    private var fingerBase = 0
    private var settleUntil = Date.distantPast
    private var lastFingers = 0

    private var trail: [(t: TimeInterval, x: CGFloat)] = []
    private var stillSince: (t: TimeInterval, p: NSPoint)?
    private var hoverFired = false
    private var lastShake: TimeInterval = 0

    func start() {
        idleCount = pb.changeCount
        let down: NSEvent.EventTypeMask = [.leftMouseDown]
        if let m = NSEvent.addGlobalMonitorForEvents(matching: down, handler: { [weak self] _ in
            Task { @MainActor in self?.mouseDown() }
        }) { monitors.append(m) }
        if let m = NSEvent.addLocalMonitorForEvents(matching: down, handler: { [weak self] e in
            self?.mouseDown()
            return e
        }) { monitors.append(m) }

        // Backup in case a mouse-down slipped past the monitors (three-finger drag, etc.).
        let t = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.idleTick() }
        }
        t.tolerance = 0.1 // lets macOS batch this wake-up with others (saves battery)
        RunLoop.main.add(t, forMode: .common)
        idle = t

        TouchMonitor.shared.onChange = { [weak self] old, new in
            Task { @MainActor in self?.fingers(old: old, new: new) }
        }
    }

    private func idleTick() {
        let down = NSEvent.pressedMouseButtons & 1 != 0
        if !down && !pressed { idleCount = pb.changeCount }
        if down && !pressed { mouseDown() }
    }

    private func mouseDown() {
        guard !pressed else { return }
        pressed = true
        ignoreThisPress = false
        let t = Timer(timeInterval: 1.0 / 60, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        RunLoop.main.add(t, forMode: .common)
        fast = t
    }

    private func tick() {
        let down = NSEvent.pressedMouseButtons & 1 != 0
        if !down {
            end()
            return
        }
        ticks += 1
        if !dragging && !ignoreThisPress && ticks % 3 == 0 && pb.changeCount != idleCount {
            let p = Incoming.peek(pb)
            if p.isEmpty {
                ignoreThisPress = true // dragging text, a window, a slider...
            } else {
                begin(p)
            }
        }
        guard dragging, !Prefs.paused else { return }

        let shift = NSEvent.modifierFlags.contains(.shift)
        if shift && !shiftWasDown && Prefs.d.bool(forKey: Key.triggerShift) { fire(.shift) }
        shiftWasDown = shift

        // Mice: the wheel (middle button) or a side button, while the left button holds the file.
        let other = NSEvent.pressedMouseButtons & ~0b11 != 0
        if other && !otherButtonWasDown && Prefs.d.bool(forKey: Key.triggerMouse) { fire(.mouse) }
        otherButtonWasDown = other

        let now = ProcessInfo.processInfo.systemUptime
        let loc = NSEvent.mouseLocation
        onMove?(loc)
        if Prefs.d.bool(forKey: Key.triggerShake) { shake(now, loc) }
        if Prefs.d.bool(forKey: Key.triggerHover) { hover(now, loc) }
    }

    private func begin(_ p: Incoming.Peek) {
        dragging = true
        peek = p
        shiftWasDown = false
        otherButtonWasDown = NSEvent.pressedMouseButtons & ~0b11 != 0
        trail.removeAll()
        stillSince = nil
        hoverFired = false
        fingerBase = TouchMonitor.shared.count
        lastFingers = fingerBase
        settleUntil = Date().addingTimeInterval(0.3)
        Log.line("drag: \(p.urls.count) file(s), \(p.promised.count) promised, image data \(p.imageData), fingers \(fingerBase)")
        Live.shared.dragging = true
        onBegin?(p)
        // "Hold Shift, then grab the file" works too.
        if !Prefs.paused && Prefs.d.bool(forKey: Key.triggerShift) && NSEvent.modifierFlags.contains(.shift) {
            shiftWasDown = true
            fire(.shift)
        }
    }

    private func end() {
        fast?.invalidate()
        fast = nil
        pressed = false
        idleCount = pb.changeCount
        if dragging {
            dragging = false
            Live.shared.dragging = false
            onEnd?()
        }
    }

    private func fire(_ t: Trigger) {
        Log.line("trigger: \(t.rawValue)")
        onTrigger?(t)
    }

    /// One more finger than the drag started with = trigger. Lifting and re-tapping fires again
    /// (next page). Finger shuffles that never go above the starting count are ignored, so
    /// people who hold the click with a thumb and move with another finger are safe.
    private func fingers(old: Int, new: Int) {
        Live.shared.fingers = new
        defer { lastFingers = new }
        guard dragging, !Prefs.paused, Prefs.d.bool(forKey: Key.triggerFinger) else { return }
        if Date() < settleUntil {
            fingerBase = max(fingerBase, new)
            return
        }
        if lastFingers <= fingerBase && new > fingerBase {
            Log.line("fingers \(lastFingers)→\(new), base \(fingerBase), shapes \(TouchMonitor.shared.shapes)")
            fire(.finger)
        }
    }

    /// Three quick left-right swings while dragging.
    private func shake(_ now: TimeInterval, _ loc: NSPoint) {
        trail.append((now, loc.x))
        trail.removeAll { now - $0.t > 0.6 }
        guard trail.count > 4, now - lastShake > 1 else { return }
        var turns = 0
        var dir: CGFloat = 0
        var anchor = trail[0].x
        for s in trail.dropFirst() {
            let d = s.x - anchor
            if abs(d) < 18 { continue }
            let sign: CGFloat = d > 0 ? 1 : -1
            if dir != 0 && sign != dir { turns += 1 }
            dir = sign
            anchor = s.x
        }
        if turns >= 3 {
            lastShake = now
            trail.removeAll()
            fire(.shake)
        }
    }

    /// Holding the file still for 0.7 s.
    private func hover(_ now: TimeInterval, _ loc: NSPoint) {
        if let s = stillSince, hypot(loc.x - s.p.x, loc.y - s.p.y) < 4 {
            if !hoverFired && now - s.t > 0.7 {
                hoverFired = true
                fire(.hover)
            }
        } else {
            if let s = stillSince, hypot(loc.x - s.p.x, loc.y - s.p.y) > 40 { hoverFired = false }
            stillSince = (now, loc)
        }
    }
}
