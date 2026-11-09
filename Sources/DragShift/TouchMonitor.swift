import AppKit
import Foundation

/// Counts fingers on the trackpad (built-in and Magic Trackpad) using macOS's own
/// MultitouchSupport framework, the same source BetterTouchTool and MiddleClick read.
/// It only ever reads finger counts; it never sends input.
final class TouchMonitor {
    static let shared = TouchMonitor()

    typealias ContactCallback = @convention(c) (UnsafeMutableRawPointer?, UnsafeMutableRawPointer?, Int32, Double, Int32) -> Int32
    private typealias CreateList = @convention(c) () -> Unmanaged<CFMutableArray>?
    private typealias Register = @convention(c) (UnsafeRawPointer, ContactCallback) -> Void
    private typealias Unregister = @convention(c) (UnsafeRawPointer, ContactCallback) -> Void
    private typealias Start = @convention(c) (UnsafeRawPointer, Int32) -> Void
    private typealias Stop = @convention(c) (UnsafeRawPointer) -> Void

    private var createList: CreateList?
    private var register: Register?
    private var unregister: Unregister?
    private var startDevice: Start?
    private var stopDevice: Stop?
    private var devices: CFMutableArray?

    private let lock = NSLock()
    private var perDevice: [UnsafeMutableRawPointer: Int] = [:]
    private var total = 0

    /// Ignore big contacts (palms). Set from Settings on the main thread; read on the touch thread.
    var palmFilter = true

    /// Called on the main thread with (old, new) finger counts.
    var onChange: ((Int, Int) -> Void)?
    /// Called once, on the main thread, the first time any frame arrives.
    var onFirstFrame: (() -> Void)?
    private var sawFrame = false

    var count: Int { lock.lock(); defer { lock.unlock() }; return total }

    /// MTTouch is 96 bytes; `state` sits at byte 20 (4 = touching, 3 = touching down).
    private static let stride = 96
    private static let stateOffset = 20
    private static let sizeOffset = 48
    private static let majorAxisOffset = 60

    /// Size and long axis of each finger in the latest frame, for tuning palm rejection.
    private var lastShapes: [(size: Float, major: Float)] = []
    var shapes: String {
        lock.lock(); defer { lock.unlock() }
        return lastShapes.map { String(format: "%.2f/%.1f", $0.size, $0.major) }.joined(separator: " ")
    }

    @discardableResult
    func start() -> Bool {
        if createList == nil {
            guard let h = dlopen("/System/Library/PrivateFrameworks/MultitouchSupport.framework/MultitouchSupport", RTLD_NOW),
                  let c = dlsym(h, "MTDeviceCreateList"),
                  let r = dlsym(h, "MTRegisterContactFrameCallback"),
                  let u = dlsym(h, "MTUnregisterContactFrameCallback"),
                  let s = dlsym(h, "MTDeviceStart"),
                  let st = dlsym(h, "MTDeviceStop")
            else {
                Log.line("touch: MultitouchSupport not available")
                return false
            }
            createList = unsafeBitCast(c, to: CreateList.self)
            register = unsafeBitCast(r, to: Register.self)
            unregister = unsafeBitCast(u, to: Unregister.self)
            startDevice = unsafeBitCast(s, to: Start.self)
            stopDevice = unsafeBitCast(st, to: Stop.self)
        }
        stop()
        guard let list = createList?()?.takeRetainedValue() else { return false }
        devices = list
        let n = CFArrayGetCount(list)
        for i in 0..<n {
            guard let dev = CFArrayGetValueAtIndex(list, i) else { continue }
            register?(dev, touchCallback)
            startDevice?(dev, 0)
        }
        Log.line("touch: watching \(n) trackpad(s)")
        return n > 0
    }

    func stop() {
        guard let list = devices else { return }
        for i in 0..<CFArrayGetCount(list) {
            guard let dev = CFArrayGetValueAtIndex(list, i) else { continue }
            unregister?(dev, touchCallback)
            stopDevice?(dev)
        }
        devices = nil
        lock.lock(); perDevice.removeAll(); total = 0; lock.unlock()
    }

    fileprivate func handle(device: UnsafeMutableRawPointer?, touches: UnsafeMutableRawPointer?, n: Int) {
        var fingers = 0
        var shapes: [(size: Float, major: Float)] = []
        if let touches {
            for i in 0..<n {
                let base = i * Self.stride
                let state = touches.load(fromByteOffset: base + Self.stateOffset, as: Int32.self)
                if state == 3 || state == 4 {
                    let size = touches.load(fromByteOffset: base + Self.sizeOffset, as: Float.self)
                    let major = touches.load(fromByteOffset: base + Self.majorAxisOffset, as: Float.self)
                    // Palm rejection: your fingertips measured 0.7–1.3 size / 8–11 long axis;
                    // a resting palm or flat thumb is far bigger.
                    if palmFilter && (major > 17 || size > 2.4) { continue }
                    fingers += 1
                    shapes.append((size, major))
                }
            }
        }
        lock.lock()
        if fingers > 0 { lastShapes = shapes }
        if let device { perDevice[device] = fingers }
        let old = total
        total = perDevice.values.reduce(0, +)
        let new = total
        let first = !sawFrame
        sawFrame = true
        lock.unlock()
        if first { DispatchQueue.main.async { self.onFirstFrame?() } }
        if old != new { DispatchQueue.main.async { self.onChange?(old, new) } }
    }
}

/// C callback: no captured context allowed, so it goes through the shared instance.
private let touchCallback: TouchMonitor.ContactCallback = { device, touches, count, _, _ in
    TouchMonitor.shared.handle(device: device, touches: touches, n: Int(count))
    return 0
}
