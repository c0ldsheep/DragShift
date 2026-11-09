import Foundation

/// "Buy me a coffee": where a tip goes and how the About tab asks for one. Nothing here talks to the internet; the
/// app only shows a QR code of an address (and copies it) for the person's own wallet app to use. It never checks whether
/// anyone paid, and nothing in the app is locked or unlocked by it.
public enum Tip {
    /// c0ldsheep's tip address. An Ethereum address carries its own spelling check in its capital letters
    /// (EIP-55); TipTests runs that check, so a slipped letter here fails the tests instead of losing someone's money.
    public static let address = "0x81fE1d0b00B3e6100E5e40E13982A3644e46e09A"

    public struct Size: Equatable, Sendable {
        public let name: String
        public let dollars: Int
        /// "Medium coffee · $5"
        public var title: String { "\(name) coffee · $\(dollars)" }
    }

    /// Suggestions only: an address can't ask for an amount, so any amount works.
    public static let sizes = [Size(name: "Small", dollars: 2), Size(name: "Medium", dollars: 5), Size(name: "Large", dollars: 10)]

    /// The line above the cup, in three parts so the number can be shown in bold. The count is always there, even
    /// at 0, so people can see that DragShift is counting; the ask only comes once it has done some work.
    public static func lineParts(fixed: Int) -> (before: String, count: String, after: String) {
        let n = max(0, fixed)
        let number = n.formatted(.number.grouping(.automatic).locale(Locale(identifier: "en_US")))
        let count = "\(number) \(n == 1 ? "file" : "files")"
        return ("DragShift has fixed ", count, n == 0 ? " for you so far. When it earns a coffee…" : " for you. If that was worth a coffee…")
    }

    public static func line(fixed: Int) -> String {
        let p = lineParts(fixed: fixed)
        return p.before + p.count + p.after
    }

    /// The ring under the cup: three slices fanned over the lower half, Small on the left. `dx`, `dy`: how far the
    /// cup has been dragged from its saucer (y grows downward, as on screen). Returns the slice the cup is over, or
    /// nil near the middle or above it.
    public static func slice(dx: Double, dy: Double, inner: Double = 30) -> Int? {
        guard (dx * dx + dy * dy).squareRoot() >= inner, dy > 0 else { return nil }
        let degrees = atan2(dy, dx) * 180 / .pi // 0 = right, 90 = down, 180 = left
        return degrees > 120 ? 0 : degrees >= 60 ? 1 : 2
    }
}

/// How many files DragShift has made for this person, kept only in this Mac's settings.
public enum Tally {
    public static let key = "files.fixed"
    public static func count(in d: UserDefaults = .standard) -> Int { max(0, d.integer(forKey: key)) }
    public static func add(_ n: Int, to d: UserDefaults = .standard) {
        guard n > 0 else { return }
        let (sum, overflow) = count(in: d).addingReportingOverflow(n)
        d.set(overflow ? Int.max : sum, forKey: key)
    }
}
