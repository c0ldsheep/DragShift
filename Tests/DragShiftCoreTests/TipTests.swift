import Foundation
import Testing
@testable import DragShiftCore

struct TipTests {
    /// Keccak-256 (Ethereum's hash; not the same as SHA3-256), only to run the address's own spelling check.
    static func keccak256(_ data: [UInt8]) -> [UInt8] {
        let rc: [UInt64] = [0x1, 0x8082, 0x800000000000808A, 0x8000000080008000, 0x808B, 0x80000001, 0x8000000080008081,
                            0x8000000000008009, 0x8A, 0x88, 0x80008009, 0x8000000A, 0x8000808B, 0x800000000000008B,
                            0x8000000000008089, 0x8000000000008003, 0x8000000000008002, 0x8000000000000080, 0x800A,
                            0x800000008000000A, 0x8000000080008081, 0x8000000000008080, 0x80000001, 0x8000000080008008]
        let rot = [[0, 36, 3, 41, 18], [1, 44, 10, 45, 2], [62, 6, 43, 15, 61], [28, 55, 25, 21, 56], [27, 20, 39, 8, 14]]
        func rol(_ x: UInt64, _ n: Int) -> UInt64 { n == 0 ? x : (x << UInt64(n)) | (x >> UInt64(64 - n)) }
        var a = [[UInt64]](repeating: [UInt64](repeating: 0, count: 5), count: 5)
        var p = data + [1]
        while p.count % 136 != 0 { p.append(0) }
        p[p.count - 1] |= 0x80
        for block in stride(from: 0, to: p.count, by: 136) {
            for i in 0..<17 {
                var word: UInt64 = 0
                for b in 0..<8 { word |= UInt64(p[block + 8 * i + b]) << UInt64(8 * b) }
                a[i % 5][i / 5] ^= word
            }
            for round in rc {
                let c = (0..<5).map { a[$0][0] ^ a[$0][1] ^ a[$0][2] ^ a[$0][3] ^ a[$0][4] }
                let d = (0..<5).map { c[($0 + 4) % 5] ^ rol(c[($0 + 1) % 5], 1) }
                for x in 0..<5 { for y in 0..<5 { a[x][y] ^= d[x] } }
                var b = a
                for x in 0..<5 { for y in 0..<5 { b[y][(2 * x + 3 * y) % 5] = rol(a[x][y], rot[x][y]) } }
                for x in 0..<5 { for y in 0..<5 { a[x][y] = b[x][y] ^ (~b[(x + 1) % 5][y] & b[(x + 2) % 5][y]) } }
                a[0][0] ^= round
            }
        }
        return (0..<4).flatMap { i in (0..<8).map { UInt8((a[i % 5][i / 5] >> UInt64(8 * $0)) & 0xFF) } }
    }

    /// The address written with the capitals its own hash asks for (EIP-55).
    static func checksummed(_ address: String) -> String {
        let hex = Array(address.dropFirst(2).lowercased())
        let hash = keccak256(Array(String(hex).utf8)).flatMap { [$0 >> 4, $0 & 15] }
        return "0x" + String(hex.enumerated().map { hash[$0.offset] >= 8 ? Character($0.element.uppercased()) : $0.element })
    }

    @Test func theCheckItselfIsRight() {
        #expect(Self.keccak256([]).map { String(format: "%02x", $0) }.joined() == "c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470")
        #expect(Self.checksummed("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed") == "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")
    }

    @Test func theTipAddressPassesItsOwnSpellingCheck() {
        #expect(Tip.address.count == 42 && Tip.address.hasPrefix("0x"))
        #expect(Self.checksummed(Tip.address) == Tip.address, "a letter in the tip address has changed")
    }

    @Test func theQRCodeSaysExactlyTheAddress() throws {
        #expect(try QR.read(try QR.make(Tip.address)) == [Tip.address])
    }

    @Test func theLineAlwaysShowsTheCountAndAsksOnlyAfterTheAppHasWorked() {
        #expect(Tip.line(fixed: 0) == "DragShift has fixed 0 files for you so far. When it earns a coffee…")
        #expect(Tip.line(fixed: 1) == "DragShift has fixed 1 file for you. If that was worth a coffee…")
        #expect(Tip.line(fixed: 214) == "DragShift has fixed 214 files for you. If that was worth a coffee…")
        #expect(Tip.lineParts(fixed: 12_345).count == "12,345 files")
        #expect(Tip.line(fixed: -3).contains("0 files"))
        #expect(Tip.sizes[1].title == "Medium coffee · $5")
    }

    @Test func theCupPicksTheSliceItIsOver() {
        #expect(Tip.slice(dx: 0, dy: 10) == nil, "still on the saucer")
        #expect(Tip.slice(dx: -55, dy: 25) == 0 && Tip.slice(dx: 0, dy: 60) == 1 && Tip.slice(dx: 55, dy: 25) == 2)
        #expect(Tip.slice(dx: 0, dy: -60) == nil, "above the ring")
        #expect(Tip.sizes.map(\.name) == ["Small", "Medium", "Large"])
    }

    @Test func theTallyAddsUpAndNeverGoesBackwards() {
        let d = UserDefaults(suiteName: "dragshift.tally.test.\(UUID().uuidString)")!
        #expect(Tally.count(in: d) == 0)
        Tally.add(3, to: d); Tally.add(0, to: d); Tally.add(-5, to: d); Tally.add(1, to: d)
        #expect(Tally.count(in: d) == 4)
        d.set(Int.max - 1, forKey: Tally.key); Tally.add(10, to: d)
        #expect(Tally.count(in: d) == Int.max)
    }
}
