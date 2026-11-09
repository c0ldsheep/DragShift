import Foundation

/// A quick, honest strength hint for the password you put on a PDF (Lock). It never blocks you and never claims
/// how long cracking would take: it only points at the usual weak spots, like short passwords, dates, common
/// words and sequences.
public enum PasswordStrength: Int, Comparable, Sendable {
    case weak, fair, strong

    public static func < (a: Self, b: Self) -> Bool { a.rawValue < b.rawValue }

    /// The rating and one short line to show under the field (nil for an empty password).
    public static func rate(_ password: String) -> (strength: PasswordStrength, hint: String)? {
        let p = password
        guard !p.isEmpty else { return nil }
        let lower = p.lowercased()
        let chars = Array(p)
        if common.contains(lower) || Set(chars).count == 1 || isSequence(lower) {
            return (.weak, "Too easy to guess")
        }
        if looksLikeDate(p) { return (.weak, "Dates are easy to guess, even with a name") }
        if p.allSatisfy(\.isNumber) && p.count < 12 { return (.weak, "Numbers only: add letters, or make it longer") }
        if p.count < 8 { return (.weak, "Use at least 8 characters") }
        let kinds = [p.contains { $0.isLowercase }, p.contains { $0.isUppercase }, p.contains { $0.isNumber },
                     p.contains { !$0.isLetter && !$0.isNumber }].filter { $0 }.count
        if p.count >= 16 || (p.count >= 12 && kinds >= 2) { return (.strong, "Strong") }
        if kinds >= 2 { return (.fair, "OK. A few more characters makes it strong") }
        return (.fair, "OK. Mix letters, numbers and symbols")
    }

    /// The most used passwords (they're the first ones anyone tries).
    static let common: Set<String> = [
        "password", "password1", "password123", "passw0rd", "123456", "1234567", "12345678", "123456789", "1234567890",
        "qwerty", "qwerty123", "abc123", "111111", "000000", "iloveyou", "admin", "welcome", "letmein", "monkey",
        "dragon", "sunshine", "princess", "football", "baseball", "india123", "india@123", "pass@123", "admin@123",
    ]

    /// 1234…, 9876…, abcd…, qwerty…: each next character one step along.
    static func isSequence(_ s: String) -> Bool {
        let rows = ["0123456789", "abcdefghijklmnopqrstuvwxyz", "qwertyuiop", "asdfghjkl", "zxcvbnm"]
        guard s.count >= 4 else { return false }
        for row in rows where row.contains(s) || String(row.reversed()).contains(s) { return true }
        return false
    }

    /// Bank and form passwords are often a birth date (with or without part of a name): ddmmyyyy, yyyymmdd,
    /// dd-mm-yyyy, ddmm…
    static func looksLikeDate(_ s: String) -> Bool {
        let digits = s.filter(\.isNumber)
        guard digits.count == 8 || digits.count == 6 || digits.count == 4 else { return false }
        let letters = s.filter(\.isLetter).count
        let others = s.count - digits.count - letters
        guard letters <= 5, others <= 2 else { return false }
        let d = Array(digits).map { Int(String($0))! }
        func day(_ a: Int, _ b: Int) -> Bool { (1...31).contains(a * 10 + b) }
        func month(_ a: Int, _ b: Int) -> Bool { (1...12).contains(a * 10 + b) }
        switch d.count {
        case 8: return (day(d[0], d[1]) && month(d[2], d[3]) && (d[4] == 1 || d[4] == 2))   // ddmmyyyy
            || ((d[0] == 1 || d[0] == 2) && month(d[4], d[5]) && day(d[6], d[7]))             // yyyymmdd
        case 6: return day(d[0], d[1]) && month(d[2], d[3])                                    // ddmmyy
        default: return letters > 0 && day(d[0], d[1]) && month(d[2], d[3])                   // name + ddmm
        }
    }
}
