using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragShift.Core
{
    /// <summary>
    /// Built-in lossless WebP writer (VP8L): Windows can show WebP but has no WebP saver.
    /// Same encoder as the Mac version: subtract-green transform, then each pixel is a literal or a
    /// copy of the run to its left or the row above, with five canonical Huffman codes (Google's VP8L spec).
    /// </summary>
    public static class WebPLossless
    {
        /// <summary><paramref name="argb"/> is straight (not premultiplied) 0xAARRGGBB, row by row.</summary>
        public static byte[] Encode(uint[] argb, int w, int h)
        {
            if (w <= 0 || h <= 0 || w > 16384 || h > 16384)
                throw new UserError("WebP pictures can be at most 16384 px wide and tall.");
            int n = w * h;
            var px = new uint[n];
            bool hasAlpha = false;
            // Subtract green: red and blue are stored as their difference from green.
            for (int i = 0; i < n; i++)
            {
                uint p = argb[i];
                if (p >> 24 != 0xff) hasAlpha = true;
                uint g = (p >> 8) & 0xff;
                uint r = (((p >> 16) & 0xff) - g) & 0xff;
                uint b = ((p & 0xff) - g) & 0xff;
                px[i] = (p & 0xff00ff00) | (r << 16) | b;
            }

            // LZ77 with two distances: previous pixel (plane code 2) and pixel above (plane code 1).
            var tokens = new List<long>(n / 2); // literal: pixel; copy: -(len << 8 | code)
            int k = 0;
            while (k < n)
            {
                int best = 0, bestCode = 0;
                if (k >= 1)
                {
                    int l = 0;
                    while (k + l < n && l < 4096 && px[k + l] == px[k + l - 1]) l++;
                    if (l > best) { best = l; bestCode = 2; }
                }
                if (k >= w)
                {
                    int l = 0;
                    while (k + l < n && l < 4096 && px[k + l] == px[k + l - w]) l++;
                    if (l > best) { best = l; bestCode = 1; }
                }
                if (best >= 3) { tokens.Add(-(((long)best << 8) | (long)bestCode)); k += best; }
                else { tokens.Add(px[k]); k++; }
            }

            var green = new int[256 + 24];
            var red = new int[256]; var blue = new int[256]; var alpha = new int[256];
            var dist = new int[40];
            foreach (var t in tokens)
            {
                if (t >= 0)
                {
                    uint p = (uint)t;
                    green[(p >> 8) & 0xff]++; red[(p >> 16) & 0xff]++; blue[p & 0xff]++; alpha[p >> 24]++;
                }
                else
                {
                    long c = -t;
                    green[256 + Prefix((int)(c >> 8)).Code]++;
                    dist[Prefix((int)(c & 0xff)).Code]++;
                }
            }

            var bw = new BitWriter();
            bw.Put(0x2f, 8);
            bw.Put(w - 1, 14);
            bw.Put(h - 1, 14);
            bw.Put(hasAlpha ? 1 : 0, 1);
            bw.Put(0, 3);
            bw.Put(1, 1); bw.Put(2, 2); // one transform: SUBTRACT_GREEN
            bw.Put(0, 1);               // no more transforms
            bw.Put(0, 1);               // no colour cache
            bw.Put(0, 1);               // no meta prefix codes
            var gc = WriteCode(green, bw); var rc = WriteCode(red, bw); var bc = WriteCode(blue, bw);
            var ac = WriteCode(alpha, bw); var dc = WriteCode(dist, bw);

            foreach (var t in tokens)
            {
                if (t >= 0)
                {
                    uint p = (uint)t;
                    gc.Put((int)((p >> 8) & 0xff), bw); rc.Put((int)((p >> 16) & 0xff), bw);
                    bc.Put((int)(p & 0xff), bw); ac.Put((int)(p >> 24), bw);
                }
                else
                {
                    long c = -t;
                    var l = Prefix((int)(c >> 8));
                    gc.Put(256 + l.Code, bw);
                    if (l.Bits > 0) bw.Put(l.Extra, l.Bits);
                    var d = Prefix((int)(c & 0xff));
                    dc.Put(d.Code, bw);
                    if (d.Bits > 0) bw.Put(d.Extra, d.Bits);
                }
            }
            bw.Finish();

            var payload = bw.Bytes;
            int size = payload.Count;
            if (size % 2 == 1) payload.Add(0);
            var outStream = new MemoryStream();
            void Ascii(string s) { foreach (var ch in s) outStream.WriteByte((byte)ch); }
            void Le32(int v) { outStream.WriteByte((byte)v); outStream.WriteByte((byte)(v >> 8)); outStream.WriteByte((byte)(v >> 16)); outStream.WriteByte((byte)(v >> 24)); }
            Ascii("RIFF");
            Le32(4 + 8 + payload.Count);
            Ascii("WEBPVP8L");
            Le32(size);
            outStream.Write(payload.ToArray(), 0, payload.Count);
            return outStream.ToArray();
        }

        public struct PrefixCode { public int Code, Bits, Extra; }

        /// <summary>VP8L prefix coding for lengths and distance codes (value >= 1).</summary>
        public static PrefixCode Prefix(int v)
        {
            if (v <= 4) return new PrefixCode { Code = v - 1 };
            int d = v - 1;
            int hb = 31 - LeadingZeros((uint)d);
            int second = (d >> (hb - 1)) & 1;
            int bits = hb - 1;
            return new PrefixCode { Code = 2 * hb + second, Bits = bits, Extra = d & ((1 << bits) - 1) };
        }

        static int LeadingZeros(uint x)
        {
            if (x == 0) return 32;
            int n = 0;
            while ((x & 0x80000000u) == 0) { n++; x <<= 1; }
            return n;
        }

        sealed class BitWriter
        {
            public readonly List<byte> Bytes = new List<byte>();
            ulong acc;
            int n;
            public void Put(int v, int bits)
            {
                if (bits <= 0) return;
                acc |= (ulong)(v & ((1 << bits) - 1)) << n;
                n += bits;
                while (n >= 8) { Bytes.Add((byte)(acc & 0xff)); acc >>= 8; n -= 8; }
            }
            public void Finish()
            {
                if (n > 0) Bytes.Add((byte)(acc & 0xff));
                acc = 0; n = 0;
            }
        }

        sealed class Code
        {
            public int[] Lengths; // bits written per symbol (0 for a lone symbol: it costs nothing)
            public int[] Codes;   // bit-reversed canonical codes
            public void Put(int s, BitWriter bw) => bw.Put(Codes[s], Lengths[s]);
        }

        static readonly int[] CodeLengthOrder = { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        /// <summary>Writes the code for an alphabet and returns how to emit its symbols.</summary>
        static Code WriteCode(int[] freq, BitWriter bw)
        {
            var used = Enumerable.Range(0, freq.Length).Where(i => freq[i] > 0).ToList();
            if (used.Count <= 2 && used.All(s => s < 256))
            {
                // "Simple" code: one or two 8-bit symbols listed directly.
                var syms = used.Count == 0 ? new List<int> { 0 } : used;
                bw.Put(1, 1);
                bw.Put(syms.Count - 1, 1);
                if (syms[0] <= 1) { bw.Put(0, 1); bw.Put(syms[0], 1); } else { bw.Put(1, 1); bw.Put(syms[0], 8); }
                if (syms.Count == 2) bw.Put(syms[1], 8);
                var lens = new int[freq.Length];
                if (syms.Count == 2) { lens[syms[0]] = 1; lens[syms[1]] = 1; }
                return new Code { Lengths = lens, Codes = Canonical(lens) };
            }

            var f = (int[])freq.Clone();
            if (used.Count == 1) f[used[0] == 0 ? 1 : 0] = 1; // a complete code needs two symbols
            var lengths = HuffmanLengths(f, 15);

            // Code lengths, with runs of zeros squeezed into codes 17 and 18.
            var tokens = new List<int[]>(); // {sym, extra, bits}
            int i0 = 0;
            while (i0 < lengths.Length)
            {
                if (lengths[i0] == 0)
                {
                    int run = 0;
                    while (i0 + run < lengths.Length && lengths[i0 + run] == 0) run++;
                    int left = run;
                    while (left >= 11) { int take = Math.Min(left, 138); tokens.Add(new[] { 18, take - 11, 7 }); left -= take; }
                    while (left >= 3) { int take = Math.Min(left, 10); tokens.Add(new[] { 17, take - 3, 3 }); left -= take; }
                    for (int z = 0; z < left; z++) tokens.Add(new[] { 0, 0, 0 });
                    i0 += run;
                }
                else
                {
                    tokens.Add(new[] { lengths[i0], 0, 0 });
                    i0++;
                }
            }
            var clFreq = new int[19];
            foreach (var t in tokens) clFreq[t[0]]++;
            var clUsed = Enumerable.Range(0, 19).Where(i => clFreq[i] > 0).ToList();
            if (clUsed.Count == 1) clFreq[clUsed[0] == 0 ? 1 : 0] = 1;
            var clLengths = HuffmanLengths(clFreq, 7);
            var clCodes = Canonical(clLengths);

            bw.Put(0, 1);
            int count = 19;
            while (count > 4 && clLengths[CodeLengthOrder[count - 1]] == 0) count--;
            bw.Put(count - 4, 4);
            for (int j = 0; j < count; j++) bw.Put(clLengths[CodeLengthOrder[j]], 3);
            bw.Put(0, 1); // every symbol's length follows
            foreach (var t in tokens)
            {
                bw.Put(clCodes[t[0]], clLengths[t[0]]);
                if (t[2] > 0) bw.Put(t[1], t[2]);
            }
            return new Code { Lengths = lengths, Codes = Canonical(lengths) };
        }

        /// <summary>Huffman code lengths no longer than <paramref name="limit"/> (rare symbols get nudged up until they fit).</summary>
        static int[] HuffmanLengths(int[] freq, int limit)
        {
            var used = Enumerable.Range(0, freq.Length).Where(i => freq[i] > 0).ToList();
            var outLens = new int[freq.Length];
            if (used.Count == 0) return outLens;
            if (used.Count == 1) { outLens[used[0]] = 1; return outLens; }
            long minWeight = 1;
            while (true)
            {
                // Plain Huffman by repeatedly merging the two lightest nodes.
                var weight = used.Select(s => Math.Max((long)freq[s], minWeight)).ToList();
                var parent = Enumerable.Repeat(-1, used.Count * 2).ToArray();
                var alive = Enumerable.Range(0, used.Count).ToList();
                while (alive.Count > 1)
                {
                    alive = alive.OrderBy(x => weight[x]).ThenBy(x => x).ToList();
                    int a = alive[0], b = alive[1];
                    alive.RemoveRange(0, 2);
                    int node = weight.Count;
                    weight.Add(weight[a] + weight[b]);
                    parent[a] = node; parent[b] = node;
                    alive.Add(node);
                }
                int maxLen = 0;
                for (int s = 0; s < used.Count; s++)
                {
                    int d = 0, x = s;
                    while (parent[x] != -1) { x = parent[x]; d++; }
                    outLens[used[s]] = d;
                    maxLen = Math.Max(maxLen, d);
                }
                if (maxLen <= limit) return outLens;
                minWeight *= 2;
            }
        }

        /// <summary>Canonical codes (shorter first, then by symbol), bit-reversed for the LSB-first stream.</summary>
        static int[] Canonical(int[] lengths)
        {
            var count = new int[16];
            foreach (var l in lengths) if (l > 0) count[l]++;
            var next = new int[16];
            int code = 0;
            for (int bits = 1; bits <= 15; bits++)
            {
                code = (code + count[bits - 1]) << 1;
                next[bits] = code;
            }
            var outCodes = new int[lengths.Length];
            for (int s = 0; s < lengths.Length; s++)
            {
                int l = lengths[s];
                if (l == 0) continue;
                int c = next[l]++;
                int r = 0;
                for (int b = 0; b < l; b++) { r = (r << 1) | (c & 1); c >>= 1; }
                outCodes[s] = r;
            }
            return outCodes;
        }
    }
}
