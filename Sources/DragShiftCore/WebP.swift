import CoreGraphics
import Foundation

/// Built-in lossless WebP writer (VP8L), so WebP export works on any Mac without extra tools.
/// cwebp still wins when it's installed (smaller, lossy files); this is the always-there fallback.
///
/// What it does: subtract-green transform, then each pixel is either a literal or a copy of the
/// run to its left or the row above (that's where screenshots and graphics shrink), and five
/// canonical Huffman codes, exactly as in Google's VP8L spec.
public enum WebPLossless {
    public static func encode(_ img: CGImage) throws -> Data {
        let w = img.width, h = img.height
        guard w > 0, h > 0, w <= 16384, h <= 16384 else { throw DragShiftError("WebP images can be at most 16384 px wide and tall.") }
        var argb = pixels(img)
        let hasAlpha = argb.contains { $0 >> 24 != 0xff }

        // Subtract green: red and blue are stored as their difference from green.
        for i in argb.indices {
            let p = argb[i]
            let g = (p >> 8) & 0xff
            let r = ((p >> 16) &- g) & 0xff
            let b = (p &- g) & 0xff
            argb[i] = (p & 0xff00ff00) | (r << 16) | b
        }

        // LZ77 with two distances: previous pixel (plane code 2) and pixel above (plane code 1).
        enum Token { case literal(UInt32), copy(len: Int, code: Int) }
        var tokens: [Token] = []
        tokens.reserveCapacity(w * h / 2)
        let n = w * h
        argb.withUnsafeBufferPointer { px in
            var i = 0
            while i < n {
                var best = 0, bestCode = 0
                if i >= 1 {
                    var l = 0
                    while i + l < n && l < 4096 && px[i + l] == px[i + l - 1] { l += 1 }
                    if l > best { best = l; bestCode = 2 }
                }
                if i >= w {
                    var l = 0
                    while i + l < n && l < 4096 && px[i + l] == px[i + l - w] { l += 1 }
                    if l > best { best = l; bestCode = 1 }
                }
                if best >= 3 {
                    tokens.append(.copy(len: best, code: bestCode))
                    i += best
                } else {
                    tokens.append(.literal(px[i]))
                    i += 1
                }
            }
        }

        var green = [Int](repeating: 0, count: 256 + 24)
        var red = [Int](repeating: 0, count: 256), blue = red, alpha = red
        var dist = [Int](repeating: 0, count: 40)
        for t in tokens {
            switch t {
            case .literal(let p):
                green[Int((p >> 8) & 0xff)] += 1
                red[Int((p >> 16) & 0xff)] += 1
                blue[Int(p & 0xff)] += 1
                alpha[Int(p >> 24)] += 1
            case .copy(let len, let code):
                green[256 + prefix(len).code] += 1
                dist[prefix(code).code] += 1
            }
        }

        var bw = BitWriter()
        bw.put(0x2f, 8)
        bw.put(w - 1, 14)
        bw.put(h - 1, 14)
        bw.put(hasAlpha ? 1 : 0, 1)
        bw.put(0, 3)
        bw.put(1, 1); bw.put(2, 2)  // one transform: SUBTRACT_GREEN
        bw.put(0, 1)                // no more transforms
        bw.put(0, 1)                // no colour cache
        bw.put(0, 1)                // no meta prefix codes
        let gc = writeCode(green, &bw), rc = writeCode(red, &bw), bc = writeCode(blue, &bw)
        let ac = writeCode(alpha, &bw), dc = writeCode(dist, &bw)

        for t in tokens {
            switch t {
            case .literal(let p):
                gc.put(Int((p >> 8) & 0xff), &bw)
                rc.put(Int((p >> 16) & 0xff), &bw)
                bc.put(Int(p & 0xff), &bw)
                ac.put(Int(p >> 24), &bw)
            case .copy(let len, let code):
                let l = prefix(len)
                gc.put(256 + l.code, &bw)
                if l.bits > 0 { bw.put(l.extra, l.bits) }
                let d = prefix(code)
                dc.put(d.code, &bw)
                if d.bits > 0 { bw.put(d.extra, d.bits) }
            }
        }
        bw.finish()

        var payload = bw.bytes
        let size = payload.count
        if size % 2 == 1 { payload.append(0) }
        var out = Data()
        out.append(contentsOf: Array("RIFF".utf8))
        out.append(le32(4 + 8 + payload.count))
        out.append(contentsOf: Array("WEBPVP8L".utf8))
        out.append(le32(size))
        out.append(contentsOf: payload)
        return out
    }

    // MARK: Pieces

    /// Straight (non-premultiplied) ARGB.
    static func pixels(_ img: CGImage) -> [UInt32] {
        let w = img.width, h = img.height
        var raw = [UInt8](repeating: 0, count: w * h * 4)
        raw.withUnsafeMutableBytes { buf in
            let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                space: ImageOps.sRGB, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
            ctx?.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        }
        var out = [UInt32](repeating: 0, count: w * h)
        for i in 0..<(w * h) {
            var r = UInt32(raw[i * 4]), g = UInt32(raw[i * 4 + 1]), b = UInt32(raw[i * 4 + 2])
            let a = UInt32(raw[i * 4 + 3])
            if a == 0 { r = 0; g = 0; b = 0 } else if a < 255 {
                r = min(255, (r * 255 + a / 2) / a); g = min(255, (g * 255 + a / 2) / a); b = min(255, (b * 255 + a / 2) / a)
            }
            out[i] = (a << 24) | (r << 16) | (g << 8) | b
        }
        return out
    }

    /// VP8L prefix coding for lengths and distance codes (value >= 1).
    static func prefix(_ v: Int) -> (code: Int, bits: Int, extra: Int) {
        if v <= 4 { return (v - 1, 0, 0) }
        let d = v - 1
        let hb = Int.bitWidth - 1 - d.leadingZeroBitCount
        let second = (d >> (hb - 1)) & 1
        let bits = hb - 1
        return (2 * hb + second, bits, d & ((1 << bits) - 1))
    }

    struct BitWriter {
        var bytes: [UInt8] = []
        var acc: UInt64 = 0
        var n = 0
        mutating func put(_ v: Int, _ bits: Int) {
            guard bits > 0 else { return }
            acc |= UInt64(v & ((1 << bits) - 1)) << UInt64(n)
            n += bits
            while n >= 8 {
                bytes.append(UInt8(acc & 0xff))
                acc >>= 8
                n -= 8
            }
        }
        mutating func finish() {
            if n > 0 { bytes.append(UInt8(acc & 0xff)) }
            acc = 0; n = 0
        }
    }

    struct Code {
        var lengths: [Int]   // bits written per symbol (0 for a lone symbol: it costs nothing)
        var codes: [Int]     // bit-reversed canonical codes
        func put(_ s: Int, _ bw: inout BitWriter) { bw.put(codes[s], lengths[s]) }
    }

    static let codeLengthOrder = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]

    /// Writes the code for an alphabet and returns how to emit its symbols.
    static func writeCode(_ freq: [Int], _ bw: inout BitWriter) -> Code {
        let used = freq.indices.filter { freq[$0] > 0 }
        if used.count <= 2 && used.allSatisfy({ $0 < 256 }) {
            // "Simple" code: one or two 8-bit symbols listed directly.
            let syms = used.isEmpty ? [0] : used
            bw.put(1, 1)
            bw.put(syms.count - 1, 1)
            if syms[0] <= 1 { bw.put(0, 1); bw.put(syms[0], 1) } else { bw.put(1, 1); bw.put(syms[0], 8) }
            if syms.count == 2 { bw.put(syms[1], 8) }
            var lengths = [Int](repeating: 0, count: freq.count)
            if syms.count == 2 { lengths[syms[0]] = 1; lengths[syms[1]] = 1 }
            return Code(lengths: lengths, codes: canonical(lengths))
        }

        var f = freq
        if used.count == 1 { f[used[0] == 0 ? 1 : 0] = 1 } // a complete code needs two symbols
        let lengths = huffmanLengths(f, limit: 15)

        // Code lengths, with runs of zeros squeezed into codes 17 and 18.
        var tokens: [(sym: Int, extra: Int, bits: Int)] = []
        var i = 0
        while i < lengths.count {
            if lengths[i] == 0 {
                var run = 0
                while i + run < lengths.count && lengths[i + run] == 0 { run += 1 }
                var left = run
                while left >= 11 { let take = min(left, 138); tokens.append((18, take - 11, 7)); left -= take }
                while left >= 3 { let take = min(left, 10); tokens.append((17, take - 3, 3)); left -= take }
                for _ in 0..<left { tokens.append((0, 0, 0)) }
                i += run
            } else {
                tokens.append((lengths[i], 0, 0))
                i += 1
            }
        }
        var clFreq = [Int](repeating: 0, count: 19)
        for t in tokens { clFreq[t.sym] += 1 }
        let clUsed = clFreq.indices.filter { clFreq[$0] > 0 }
        if clUsed.count == 1 { clFreq[clUsed[0] == 0 ? 1 : 0] = 1 }
        let clLengths = huffmanLengths(clFreq, limit: 7)
        let clCodes = canonical(clLengths)

        bw.put(0, 1)
        var count = 19
        while count > 4 && clLengths[codeLengthOrder[count - 1]] == 0 { count -= 1 }
        bw.put(count - 4, 4)
        for k in 0..<count { bw.put(clLengths[codeLengthOrder[k]], 3) }
        bw.put(0, 1) // every symbol's length follows
        for t in tokens {
            bw.put(clCodes[t.sym], clLengths[t.sym])
            if t.bits > 0 { bw.put(t.extra, t.bits) }
        }
        return Code(lengths: lengths, codes: canonical(lengths))
    }

    /// Huffman code lengths no longer than `limit` (rare symbols get nudged up until they fit).
    static func huffmanLengths(_ freq: [Int], limit: Int) -> [Int] {
        let used = freq.indices.filter { freq[$0] > 0 }
        var out = [Int](repeating: 0, count: freq.count)
        if used.isEmpty { return out }
        if used.count == 1 { out[used[0]] = 1; return out }
        var minWeight = 1
        while true {
            // Plain Huffman by repeatedly merging the two lightest nodes.
            var weight = used.map { max(freq[$0], minWeight) }
            var parent = [Int](repeating: -1, count: used.count * 2)
            var alive = Array(0..<used.count)
            while alive.count > 1 {
                alive.sort { weight[$0] < weight[$1] }
                let a = alive.removeFirst(), b = alive.removeFirst()
                let node = weight.count
                weight.append(weight[a] + weight[b])
                parent[a] = node; parent[b] = node
                alive.append(node)
            }
            var maxLen = 0
            for (k, sym) in used.enumerated() {
                var d = 0, x = k
                while parent[x] != -1 { x = parent[x]; d += 1 }
                out[sym] = d
                maxLen = max(maxLen, d)
            }
            if maxLen <= limit { return out }
            minWeight *= 2
        }
    }

    /// Canonical codes (shorter first, then by symbol), bit-reversed for the LSB-first stream.
    static func canonical(_ lengths: [Int]) -> [Int] {
        var count = [Int](repeating: 0, count: 16)
        for l in lengths where l > 0 { count[l] += 1 }
        var next = [Int](repeating: 0, count: 16)
        var code = 0
        for bits in 1...15 {
            code = (code + count[bits - 1]) << 1
            next[bits] = code
        }
        var out = [Int](repeating: 0, count: lengths.count)
        for (s, l) in lengths.enumerated() where l > 0 {
            var c = next[l]
            next[l] += 1
            var r = 0
            for _ in 0..<l { r = (r << 1) | (c & 1); c >>= 1 }
            out[s] = r
        }
        return out
    }

    static func le32(_ v: Int) -> Data {
        Data([UInt8(v & 0xff), UInt8((v >> 8) & 0xff), UInt8((v >> 16) & 0xff), UInt8((v >> 24) & 0xff)])
    }
}
