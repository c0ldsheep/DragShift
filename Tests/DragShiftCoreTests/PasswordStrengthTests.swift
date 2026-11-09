import Testing
@testable import DragShiftCore

@Suite struct PasswordStrengthTests {
    @Test func nothingTypedShowsNoHint() {
        #expect(PasswordStrength.rate("") == nil)
    }

    @Test func theUsualWeakSpotsAreWeak() {
        for p in ["password", "123456", "qwerty", "aaaaaaaa", "abcdefgh", "9876543210", "India@123",
                  "15081995", "1995-08-15", "MANGO1508", "150895", "1234", "Ab1!"] {
            #expect(PasswordStrength.rate(p)?.strength == .weak, "\(p)")
        }
    }

    @Test func shortButMixedIsFairAndLongMixedIsStrong() {
        #expect(PasswordStrength.rate("mango4Tree")?.strength == .fair)
        #expect(PasswordStrength.rate("onlylowercase")?.strength == .fair)
        #expect(PasswordStrength.rate("mango4Tree!rain")?.strength == .strong)
        #expect(PasswordStrength.rate("correct horse battery staple")?.strength == .strong)
    }

    @Test func everyHintIsShortAndMakesNoCrackingTimeClaims() {
        for p in ["a", "password", "15081995", "mango4Tree", "mango4Tree!rain", "1234567890123"] {
            let hint = PasswordStrength.rate(p)!.hint
            #expect(hint.count <= 48, "\(hint)")
            #expect(!hint.lowercased().contains("second") && !hint.lowercased().contains("year"), "\(hint)")
        }
    }
}
