using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DragShift.Core
{
    /// <summary>How Redact hides a box. The words and the saved names (box, pixelate, blur) are the Mac's.</summary>
    public enum RedactStyle { Bar, Pixelate, Blur }

    public static class RedactStyles
    {
        public static string Title(this RedactStyle s) => s == RedactStyle.Bar ? "Black bar" : s == RedactStyle.Pixelate ? "Pixelate" : "Blur";

        /// <summary>The name kept in the settings file.</summary>
        public static string Key(this RedactStyle s) => s == RedactStyle.Bar ? "box" : s == RedactStyle.Pixelate ? "pixelate" : "blur";

        /// <summary>Anything that isn't a known name gives the black bar, the Mac's first choice too.</summary>
        public static RedactStyle Parse(string key) => key == "pixelate" ? RedactStyle.Pixelate : key == "blur" ? RedactStyle.Blur : RedactStyle.Bar;
    }

    /// <summary>One thing Redact found: what it is, and where (pixels of the upright picture, top-left origin).</summary>
    public sealed class Finding
    {
        public string Label;
        public Box Rect;

        public Finding(string label, Box rect) { Label = label; Rect = rect; }
    }

    /// <summary>A word the text reader found, with its box in the picture's pixels.</summary>
    public struct ReadWord
    {
        public string Text;
        public Box Rect;

        public ReadWord(string text, Box rect) { Text = text; Rect = rect; }
    }

    /// <summary>A line the text reader found: its text, and its words in the same order.</summary>
    public sealed class ReadLine
    {
        public string Text;
        public List<ReadWord> Words = new List<ReadWord>();

        public ReadLine(string text) { Text = text; }
    }

    /// <summary>
    /// What Redact looks for in the text of a picture. The patterns are copied exactly from the Mac app
    /// (<c>VisionOps.patterns</c>). The Mac also asks macOS for street addresses; Windows has no such tool, so this
    /// never looks for addresses.
    /// </summary>
    public static class Redaction
    {
        public sealed class Pattern
        {
            public readonly string Label, Expression;
            public Pattern(string label, string expression) { Label = label; Expression = expression; }
        }

        public static readonly Pattern[] Patterns =
        {
            new Pattern("Email", @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"),
            new Pattern("UPI ID", @"\b[A-Za-z0-9.\-_]{2,}@[A-Za-z]{2,}\b"),
            new Pattern("Aadhaar", @"\b[2-9]\d{3}[ \-]?\d{4}[ \-]?\d{4}\b"),
            new Pattern("PAN", @"\b[A-Z]{5}[0-9]{4}[A-Z]\b"),
            new Pattern("IFSC", @"\b[A-Z]{4}0[A-Z0-9]{6}\b"),
            new Pattern("Card", @"\b(?:\d[ \-]?){13,19}\b"),
            new Pattern("Phone", @"(?:\+?91[ \-]?)?\b[6-9]\d{4}[ \-]?\d{5}\b"),
            new Pattern("Phone", @"\+\d{1,3}[ \-]?\(?\d{2,4}\)?[ \-]?\d{3,4}[ \-]?\d{3,4}"),
            new Pattern("Bank account", @"\b\d{9,18}\b"),
        };

        // Compiled once. A time limit keeps a strange line of text from ever hanging the window.
        static readonly Regex[] Compiled = Array.ConvertAll(Patterns, p => new Regex(p.Expression, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

        /// <summary>Padding around a found box, as a share of the line's height, on every side.</summary>
        public const double PadShare = 0.12;

        /// <summary>A new box that overlaps an earlier one by more than this share of its own area is dropped.</summary>
        public const double OverlapShare = 0.6;

        /// <summary>A face box grows by this much of its width on each side and this much of its height on each side.</summary>
        public const double FaceWider = 0.1, FaceTaller = 0.15;

        /// <summary>The black bar: a solid near-black (the Mac's 0.05, 0.05, 0.06).</summary>
        public const byte BarRed = 13, BarGreen = 13, BarBlue = 15;

        /// <summary>Pixelate's block size and Blur's strength: a fifth of the box's shorter side, at least 8 pixels.</summary>
        public static double Amount(double width, double height) => Math.Max(8, Math.Min(width, height) / 5);

        /// <summary>A card number counts only if its digits pass the Luhn check (13 to 19 digits).</summary>
        public static bool Luhn(string digits)
        {
            var ds = new List<int>();
            foreach (var c in digits ?? "") if (char.IsDigit(c)) ds.Add((int)char.GetNumericValue(c));
            if (ds.Count < 13 || ds.Count > 19) return false;
            int sum = 0;
            for (int i = 0; i < ds.Count; i++)
            {
                int d = ds[ds.Count - 1 - i];
                if (i % 2 == 1) { int x = d * 2; sum += x > 9 ? x - 9 : x; }
                else sum += d;
            }
            return sum % 10 == 0;
        }

        public struct Hit
        {
            public string Label;
            public int Start, Length;
        }

        /// <summary>Every match in a line of text, pattern by pattern in the list's order (a Card only if it passes Luhn).</summary>
        public static List<Hit> Matches(string text)
        {
            var hits = new List<Hit>();
            if (string.IsNullOrEmpty(text)) return hits;
            for (int i = 0; i < Compiled.Length; i++)
            {
                try
                {
                    foreach (Match m in Compiled[i].Matches(text))
                    {
                        if (Patterns[i].Label == "Card" && !Luhn(m.Value)) continue;
                        hits.Add(new Hit { Label = Patterns[i].Label, Start = m.Index, Length = m.Length });
                    }
                }
                catch (RegexMatchTimeoutException) { }
            }
            return hits;
        }

        /// <summary>A box grown by the line's padding (12% of the line's height) on every side.</summary>
        public static Box Pad(Box b, double lineHeight)
        {
            double pad = lineHeight * PadShare;
            return new Box(b.X - pad, b.Y - pad, b.Width + pad * 2, b.Height + pad * 2);
        }

        /// <summary>A face box widened by 10% and made taller by 15% on each side.</summary>
        public static Box FaceBox(Box face) =>
            new Box(face.X - face.Width * FaceWider, face.Y - face.Height * FaceTaller, face.Width * (1 + 2 * FaceWider), face.Height * (1 + 2 * FaceTaller));

        /// <summary>The area two boxes share (0 when they only touch or are apart).</summary>
        public static double Shared(Box a, Box b)
        {
            double w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
            double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            return w > 0 && h > 0 ? w * h : 0;
        }

        /// <summary>Adds a finding unless it mostly lies over an earlier one (more than 60% of its own area).</summary>
        public static bool Add(List<Finding> found, string label, Box rect)
        {
            double area = rect.Width * rect.Height;
            foreach (var f in found) if (Shared(f.Rect, rect) > OverlapShare * area) return false;
            found.Add(new Finding(label, rect));
            return true;
        }

        /// <summary>Where each word starts in the line's text. Words come in the line's order; if one can't be found where
        /// expected (a reader that rewrites its text), the next word is taken to follow it directly.</summary>
        public static int[] WordStarts(ReadLine line)
        {
            var starts = new int[line.Words.Count];
            int at = 0;
            for (int i = 0; i < starts.Length; i++)
            {
                var w = line.Words[i].Text ?? "";
                int found = w.Length == 0 ? -1 : line.Text.IndexOf(w, at, StringComparison.Ordinal);
                starts[i] = found < 0 ? Math.Min(at, line.Text.Length) : found;
                at = starts[i] + w.Length;
            }
            return starts;
        }

        /// <summary>Boxes for everything private in the lines the text reader found: each match covers the words it touches,
        /// grown by the padding.</summary>
        public static List<Finding> FindText(IEnumerable<ReadLine> lines)
        {
            var found = new List<Finding>();
            foreach (var line in lines)
            {
                if (line == null || string.IsNullOrEmpty(line.Text) || line.Words.Count == 0) continue;
                var starts = WordStarts(line);
                double top = double.MaxValue, bottom = double.MinValue;
                foreach (var w in line.Words) { top = Math.Min(top, w.Rect.Y); bottom = Math.Max(bottom, w.Rect.Bottom); }
                double lineHeight = bottom - top;
                foreach (var hit in Matches(line.Text))
                {
                    double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                    bool touched = false;
                    for (int i = 0; i < line.Words.Count; i++)
                    {
                        int s = starts[i], e = s + (line.Words[i].Text ?? "").Length;
                        if (e <= hit.Start || s >= hit.Start + hit.Length) continue; // this word is outside the match
                        var r = line.Words[i].Rect;
                        x0 = Math.Min(x0, r.X); y0 = Math.Min(y0, r.Y); x1 = Math.Max(x1, r.Right); y1 = Math.Max(y1, r.Bottom);
                        touched = true;
                    }
                    if (touched) Add(found, hit.Label, Pad(new Box(x0, y0, x1 - x0, y1 - y0), lineHeight));
                }
            }
            return found;
        }

        /// <summary>The findings plus the faces (each box grown), faces last. A face lying mostly over something found
        /// earlier is left out, the same rule as for text.</summary>
        public static List<Finding> AddFaces(IEnumerable<Finding> found, IEnumerable<Box> faces)
        {
            var all = new List<Finding>(found);
            foreach (var face in faces) Add(all, "Face", FaceBox(face));
            return all;
        }

        /// <summary>The part of a box that lies inside a picture of this size, in whole pixels; null when none does.</summary>
        public static bool Clip(Box b, int width, int height, out int x, out int y, out int w, out int h)
        {
            double x0 = Math.Max(0, Math.Floor(b.X)), y0 = Math.Max(0, Math.Floor(b.Y));
            double x1 = Math.Min(width, Math.Ceiling(b.Right)), y1 = Math.Min(height, Math.Ceiling(b.Bottom));
            x = (int)x0; y = (int)y0; w = (int)Math.Max(0, x1 - x0); h = (int)Math.Max(0, y1 - y0);
            return w > 0 && h > 0;
        }
    }
}
