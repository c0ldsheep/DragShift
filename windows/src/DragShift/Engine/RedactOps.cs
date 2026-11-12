using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DragShift.Core;

namespace DragShift.Engine
{
    /// <summary>
    /// Redact: what the window shows (the private things found in a picture), and the pixel work when you save.
    /// Everything is done on this PC. The finder's rules (patterns, Luhn, padding, overlap) live in Core/Redact.cs.
    /// </summary>
    static class RedactOps
    {
        /// <summary>The longest side of the picture shown in the window. Boxes are kept in the picture's own pixels.</summary>
        public const int PreviewEdge = 1800;

        /// <summary>What the window needs to open: a preview, and everything found.</summary>
        public sealed class Scan : IDisposable
        {
            /// <summary>The upright picture's own size, in pixels.</summary>
            public int Width, Height;

            /// <summary>A copy of the picture, at most <see cref="PreviewEdge"/> on its long side.</summary>
            public Bitmap Preview;

            /// <summary>False when this PC has no text reader (or it failed): the window then asks you to draw the boxes.</summary>
            public bool CanRead;

            /// <summary>False when this PC can't look for faces.</summary>
            public bool CanFindFaces;

            /// <summary>The emails, numbers and so on the text reader found, as boxes (the picture's pixels).</summary>
            public List<Finding> Text = new List<Finding>();

            /// <summary>The faces as the finder drew them; <see cref="Redaction.AddFaces"/> grows them.</summary>
            public List<Box> Faces = new List<Box>();

            public void Dispose() { Preview?.Dispose(); Preview = null; }
        }

        /// <summary>Opens the picture and looks for private details in it (text and faces). Nothing is changed or saved.
        /// <paramref name="opened"/> is called as soon as the preview is ready (the findings are filled in afterwards), so a
        /// window can show the picture while the reading goes on.</summary>
        public static async Task<Scan> Look(string path, Action<Scan> opened = null)
        {
            var scan = new Scan();
            try
            {
                using (var full = await Images.Load(path))
                {
                    scan.Width = full.Width;
                    scan.Height = full.Height;
                    scan.Preview = Math.Max(full.Width, full.Height) > PreviewEdge ? Images.Fit(full, PreviewEdge) : new Bitmap(full);
                    opened?.Invoke(scan);
                    try
                    {
                        var lines = await Vision.ReadLines(full);
                        scan.CanRead = lines != null;
                        if (lines != null) scan.Text = Redaction.FindText(lines);
                    }
                    catch (Exception) { scan.CanRead = false; scan.Text = new List<Finding>(); } // the reader failed: same as having none
                    try
                    {
                        scan.CanFindFaces = Vision.FacesAvailable;
                        if (scan.CanFindFaces) scan.Faces = await Vision.Faces(full);
                    }
                    catch (Exception) { scan.Faces = new List<Box>(); }
                }
                return scan;
            }
            catch (Exception)
            {
                scan.Dispose();
                throw;
            }
        }

        // MARK: Pixels

        sealed class Patch
        {
            public int X, Y, W, H;
            public byte[] Pixels; // BGRA, premultiplied, W×H
        }

        /// <summary>
        /// Replaces the pixels inside each box, in place. Every box is worked out from the untouched picture first and
        /// painted afterwards, so boxes that touch or overlap never feed each other. The picture ends up with the covered
        /// pixels really gone (not drawn over in a layer).
        /// </summary>
        public static void Apply(Bitmap img, IList<Box> boxes, RedactStyle style)
        {
            var patches = new List<Patch>();
            foreach (var box in boxes)
            {
                if (!Redaction.Clip(box, img.Width, img.Height, out int x, out int y, out int w, out int h)) continue;
                var patch = new Patch { X = x, Y = y, W = w, H = h };
                patch.Pixels = style == RedactStyle.Bar ? Bar(w, h)
                             : style == RedactStyle.Pixelate ? Pixelated(Read(img, x, y, w, h), w, h, (int)Math.Round(Redaction.Amount(box.Width, box.Height)))
                             : Blurred(Read(img, x, y, w, h), w, h, Redaction.Amount(box.Width, box.Height));
                patches.Add(patch);
            }
            foreach (var p in patches) Write(img, p);
        }

        static byte[] Bar(int w, int h)
        {
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = Redaction.BarBlue; px[i + 1] = Redaction.BarGreen; px[i + 2] = Redaction.BarRed; px[i + 3] = 255;
            }
            return px;
        }

        /// <summary>Square blocks of one average colour. The blocks start at the box's top-left corner.</summary>
        static byte[] Pixelated(byte[] src, int w, int h, int block)
        {
            block = Math.Max(1, block);
            var px = new byte[w * h * 4];
            for (int by = 0; by < h; by += block)
            {
                for (int bx = 0; bx < w; bx += block)
                {
                    int x1 = Math.Min(w, bx + block), y1 = Math.Min(h, by + block);
                    long b = 0, g = 0, r = 0, a = 0;
                    for (int y = by; y < y1; y++)
                        for (int x = bx; x < x1; x++)
                        {
                            int i = (y * w + x) * 4;
                            b += src[i]; g += src[i + 1]; r += src[i + 2]; a += src[i + 3];
                        }
                    int n = (x1 - bx) * (y1 - by);
                    byte mb = (byte)((b + n / 2) / n), mg = (byte)((g + n / 2) / n), mr = (byte)((r + n / 2) / n), ma = (byte)((a + n / 2) / n);
                    for (int y = by; y < y1; y++)
                        for (int x = bx; x < x1; x++)
                        {
                            int i = (y * w + x) * 4;
                            px[i] = mb; px[i + 1] = mg; px[i + 2] = mr; px[i + 3] = ma;
                        }
                }
            }
            return px;
        }

        /// <summary>A Gaussian-style blur (three box blurs, the usual way to get one) of the box's own pixels. The picture
        /// outside the box is never mixed in, and the edge pixels are repeated outward.</summary>
        static byte[] Blurred(byte[] src, int w, int h, double sigma)
        {
            // Three passes of a box of width d approximate a Gaussian with this standard deviation.
            int d = Math.Max(1, (int)Math.Floor(sigma * 3 * Math.Sqrt(2 * Math.PI) / 4 + 0.5));
            var a = (byte[])src.Clone();
            var b = new byte[a.Length];
            int[] radii = d % 2 == 1 ? new[] { d / 2, d / 2, d / 2 } : new[] { d / 2, d / 2 - 1, d / 2 };
            foreach (int r in radii)
            {
                BoxBlur(a, b, w, h, Math.Max(0, r), horizontal: true);
                BoxBlur(b, a, w, h, Math.Max(0, r), horizontal: false);
            }
            return a;
        }

        /// <summary>One pass of a moving average along rows or columns, with the edge pixel repeated beyond the box.</summary>
        static void BoxBlur(byte[] from, byte[] to, int w, int h, int radius, bool horizontal)
        {
            int length = horizontal ? w : h, lines = horizontal ? h : w;
            int step = horizontal ? 4 : w * 4, lineStep = horizontal ? w * 4 : 4;
            int width = radius * 2 + 1;
            for (int line = 0; line < lines; line++)
            {
                int start = line * lineStep;
                for (int ch = 0; ch < 4; ch++)
                {
                    long sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += from[start + Math.Min(length - 1, Math.Max(0, k)) * step + ch];
                    for (int i = 0; i < length; i++)
                    {
                        to[start + i * step + ch] = (byte)((sum + width / 2) / width);
                        int leaving = Math.Min(length - 1, Math.Max(0, i - radius)), entering = Math.Min(length - 1, Math.Max(0, i + radius + 1));
                        sum += from[start + entering * step + ch] - from[start + leaving * step + ch];
                    }
                }
            }
        }

        static byte[] Read(Bitmap img, int x, int y, int w, int h)
        {
            var bytes = new byte[w * h * 4];
            var d = img.LockBits(new Rectangle(x, y, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int row = 0; row < h; row++) Marshal.Copy(d.Scan0 + row * d.Stride, bytes, row * w * 4, w * 4);
            }
            finally { img.UnlockBits(d); }
            return bytes;
        }

        static void Write(Bitmap img, Patch p)
        {
            var d = img.LockBits(new Rectangle(p.X, p.Y, p.W, p.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int row = 0; row < p.H; row++) Marshal.Copy(p.Pixels, row * p.W * 4, d.Scan0 + row * d.Stride, p.W * 4);
            }
            finally { img.UnlockBits(d); }
        }
    }
}
