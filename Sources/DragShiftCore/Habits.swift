import Foundation

/// "Your usual": the slice you pick most for each kind of file, so the ring can make it glow.
/// Every pick adds 1 to that slice and fades the other slices of the same kind by 10%, so a new
/// habit takes over after a few picks instead of fighting years of old counts. A slice becomes
/// your usual once its score reaches 2.5, which is three picks in a row.
public struct Habits: Codable, Equatable, Sendable {
    public static let fade = 0.9
    public static let threshold = 2.5

    /// kind → slice id → score
    public var scores: [String: [String: Double]] = [:]

    public init() {}

    /// What was picked, as a ring slice: "Under 100 KB" counts for the Under KB slice,
    /// "1600 px" for Resize.
    public static func sliceID(for id: String) -> String {
        if SizeLimit.kb(fromStep: id) != nil { return Catalog.fitKB.id }
        if id.hasPrefix("img.size.") || id.hasPrefix("img.pct.") { return "img.resize" }
        return id
    }

    public mutating func record(_ id: String, kind: FileKind) {
        let slice = Self.sliceID(for: id)
        var s = (scores[kind.rawValue] ?? [:]).mapValues { $0 * Self.fade }.filter { $0.value >= 0.05 }
        s[slice, default: 0] += 1
        scores[kind.rawValue] = s
    }

    /// The habit for this kind, if there is one. Ties go to the lower id, so the answer
    /// never flickers between two slices.
    public func usual(for kind: FileKind) -> String? {
        guard let s = scores[kind.rawValue],
              let best = s.max(by: { $0.value != $1.value ? $0.value < $1.value : $0.key > $1.key }),
              best.value >= Self.threshold else { return nil }
        return best.key
    }
}
