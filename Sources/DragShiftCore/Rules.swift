import Foundation

// Small rules the app follows, kept in the engine so the tests can check them.

/// Which mouse buttons open the ring while the left button holds a file. The numbers are the bits of
/// `NSEvent.pressedMouseButtons`: bit 2 is the wheel, bits 3 and 4 the two side buttons.
///
/// The right button isn't offered on the Mac: the app only watches which buttons are down and never takes a click
/// away from the app under the pointer, so a right-click while dragging would also open that app's menu.
public struct MouseButtons: OptionSet, Sendable, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let middle = MouseButtons(rawValue: 1 << 2)
    public static let back = MouseButtons(rawValue: 1 << 3)
    public static let forward = MouseButtons(rawValue: 1 << 4)
    public static let all: MouseButtons = [.middle, .back, .forward]

    static let words: [(String, MouseButtons)] = [("middle", .middle), ("back", .back), ("forward", .forward)]

    /// From the saved setting, a list of plain words ("middle,back"). Nothing saved = every button. A saved empty
    /// list = none. Words it doesn't know are skipped, and a list with no known word in it (a typo, a newer
    /// version's setting) counts as nothing saved, so a mistake can never switch the mouse off without a trace.
    public init(saved: String?) {
        guard let saved else { self = .all; return }
        let list = saved.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces).lowercased() }.filter { !$0.isEmpty }
        if list.isEmpty { self = []; return }
        var found: MouseButtons = []
        for (word, button) in Self.words where list.contains(word) { found.insert(button) }
        self = found.isEmpty ? .all : found
    }

    public var saved: String {
        Self.words.filter { contains($0.1) }.map(\.0).joined(separator: ",")
    }

    /// Whether one of the chosen buttons is down. `pressed` is `NSEvent.pressedMouseButtons`. Mice with more than
    /// five buttons: the extra ones count only while every listed button is chosen, as they did before the choice existed.
    public func matches(pressed: Int) -> Bool {
        if pressed & rawValue != 0 { return true }
        return self == .all && pressed & ~0b11111 != 0
    }
}

/// How wide the progress bubble is. It keeps its usual width and grows only when the title and the buttons beside
/// it don't fit ("Stitched into one long picture" next to Show and Undo), never past `widest`.
public enum BubbleLayout {
    public static let normal: Double = 360
    public static let widest: Double = 560
    /// Everything in the bubble's top row that isn't the title or the buttons: the shadow margin, the padding,
    /// the icon and the gaps.
    static let chrome: Double = 16 + 28 + 22 + 30 + 4

    public static func width(title: Double, buttons: Double) -> Double {
        guard title.isFinite, buttons.isFinite else { return widest }
        let need = chrome + max(0, title) + max(0, buttons)
        return min(widest, max(normal, need.rounded(.up)))
    }
}

/// The temporary copies the app makes of things that aren't files yet (a copied screenshot, a picture dragged out of
/// a web page, a photo dragged from Photos). They are removed when the action ends; this is the safety net for the
/// ones that outlive that (a crash, a force quit).
public enum InboxSweep {
    public static let maxAge: TimeInterval = 24 * 60 * 60

    /// The folders old enough to remove. `folders` is each one with the time it was made.
    public static func expired(_ folders: [(url: URL, made: Date)], now: Date, maxAge: TimeInterval = maxAge) -> [URL] {
        folders.filter { now.timeIntervalSince($0.made) > maxAge }.map(\.url)
    }
}
