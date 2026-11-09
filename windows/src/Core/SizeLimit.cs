using System;

namespace DragShift.Core
{
    /// <summary>"Under 50 KB" for exam, job and government portals: the best-looking file that still fits.</summary>
    public static class SizeLimit
    {
        /// <summary>Portals disagree on what a KB is (1,000 or 1,024 bytes). Aiming under 1,000 × N passes both.</summary>
        public static int Bytes(int kb) => kb * 1000;

        public static readonly int[] Presets = { 20, 50, 100, 200, 500, 1000 };

        public static string Title(int kb) => kb >= 1000 && kb % 1000 == 0 ? (kb / 1000) + " MB" : kb + " KB";

        /// <summary>Step id for one size, e.g. fit.kb.50. Works for pictures and PDFs.</summary>
        public static string StepId(int kb) => "fit.kb." + kb;

        public static int? KbFromStep(string id)
        {
            var p = (id ?? "").Split('.');
            if (p.Length == 3 && p[0] == "fit" && p[1] == "kb" && int.TryParse(p[2], out var n) && n > 0) return n;
            return null;
        }

        public static Slice Slice(int kb) =>
            new Slice(StepId(kb), "Under " + Title(kb), Catalog.Glyph.Size, "Fitting under " + Title(kb));

        /// <summary>
        /// Finds the JPEG that fits: the highest quality at full size if that's enough, otherwise the picture
        /// is made smaller step by step. <paramref name="encode"/>(scale, quality) returns JPEG bytes.
        /// </summary>
        public static byte[] Fit(Func<double, double, byte[]> encode, int width, int height, int maxBytes)
        {
            double scale = 1;
            for (int attempt = 0; attempt < 14; attempt++)
            {
                int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
                var top = encode(scale, 0.95);
                if (top.Length <= maxBytes) return top;
                double floorQ = Math.Max(w, h) <= 400 ? 0.2 : 0.5;
                var low = encode(scale, floorQ);
                if (low.Length > maxBytes)
                {
                    if (Math.Max(w, h) <= 64) break;
                    // Shrink about as much as the bytes say, never more than half at a time.
                    scale *= Num.Clamp(Math.Sqrt((double)maxBytes / low.Length) * 0.97, 0.5, 0.92);
                    continue;
                }
                double lo = floorQ, hi = 0.95;
                var best = low;
                for (int i = 0; i < 7; i++)
                {
                    double mid = (lo + hi) / 2;
                    var d = encode(scale, mid);
                    if (d.Length <= maxBytes) { best = d; lo = mid; } else hi = mid;
                }
                return best;
            }
            throw new UserError("Couldn't get it under " + Title(maxBytes / 1000) + ". Try a bigger limit.");
        }

        /// <summary>PDF pages are redrawn as pictures: sharpest resolution first, then the best quality that fits.</summary>
        public static readonly double[] PdfQualities = { 0.8, 0.7, 0.6, 0.5, 0.4, 0.32 };
        public static readonly double[] PdfDpis = { 150, 120, 96, 72, 56 };

        /// <summary>Bytes PdfWriter adds around the page pictures.</summary>
        public static int PdfOverhead(int pages) => 400 + 520 * pages;
    }
}
