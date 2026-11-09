using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DragShift.Core
{
    /// <summary>
    /// "Your usual": the slice you pick most for each kind of file, so the ring can make it glow. Every pick adds 1
    /// to that slice and fades the other slices of the same kind by 10%, so a new habit takes over after a few picks
    /// instead of fighting years of old counts. A slice becomes your usual once its score reaches 2.5, which is three
    /// picks in a row. Same rule as the Mac.
    /// </summary>
    public sealed class Habits
    {
        public const double Fade = 0.9, Threshold = 2.5;

        readonly Dictionary<FileKind, Dictionary<string, double>> scores = new Dictionary<FileKind, Dictionary<string, double>>();

        /// <summary>What was picked, as a ring slice: "Under 100 KB" counts for the Under KB slice, "1600 px" for Resize.</summary>
        public static string SliceId(string id)
        {
            if (SizeLimit.KbFromStep(id) != null) return Catalog.FitKB.Id;
            if (id.StartsWith("img.size.", StringComparison.Ordinal) || id.StartsWith("img.pct.", StringComparison.Ordinal)) return Catalog.Resize.Id;
            return id;
        }

        public void Record(FileKind kind, string id)
        {
            var slice = SliceId(id);
            var s = scores.TryGetValue(kind, out var old)
                ? old.Where(kv => kv.Value * Fade >= 0.05).ToDictionary(kv => kv.Key, kv => kv.Value * Fade, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);
            s[slice] = (s.TryGetValue(slice, out var v) ? v : 0) + 1;
            scores[kind] = s;
        }

        /// <summary>The habit for this kind, if there is one. Ties go to the lower id, so the answer never flickers.</summary>
        public string Usual(FileKind kind)
        {
            if (!scores.TryGetValue(kind, out var s) || s.Count == 0) return null;
            var best = s.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
            return best.Value >= Threshold ? best.Key : null;
        }

        /// <summary>One settings line: "Image:img.to.jpg=2.71|fit.kb=0.9;Pdf:pdf.to.jpg=1".</summary>
        public override string ToString() =>
            string.Join(";", scores.Where(k => k.Value.Count > 0).OrderBy(k => k.Key).Select(k => k.Key + ":" +
                string.Join("|", k.Value.OrderBy(v => v.Key, StringComparer.Ordinal)
                    .Select(v => v.Key + "=" + v.Value.ToString("0.###", CultureInfo.InvariantCulture)))));

        public static Habits Parse(string text)
        {
            var h = new Habits();
            foreach (var part in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0 || !Enum.TryParse(part.Substring(0, colon).Trim(), out FileKind kind)) continue;
                var s = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var pair in part.Substring(colon + 1).Split('|'))
                {
                    int eq = pair.LastIndexOf('=');
                    if (eq > 0 && double.TryParse(pair.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        s[pair.Substring(0, eq).Trim()] = v;
                }
                h.scores[kind] = s;
            }
            return h;
        }
    }
}
