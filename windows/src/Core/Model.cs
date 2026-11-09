using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragShift.Core
{
    public enum FileKind { Image, Pdf, Document, Video, Audio, Archive, Folder, Other }

    /// <summary>Slices that need a choice open a small window instead of running straight away.</summary>
    public enum Editor { None, FitKB, Resize, Print }

    /// <summary>One slice on the ring.</summary>
    public sealed class Slice
    {
        public string Id { get; }
        public string Title { get; }
        /// <summary>Segoe MDL2 Assets / Segoe Fluent Icons character.</summary>
        public string Glyph { get; }
        /// <summary>Shown in the progress bubble: "Converting to JPG".</summary>
        public string Verb { get; }
        public Editor Editor { get; }
        /// <summary>Runs in the app (copy, share...) instead of the file engine.</summary>
        public bool IsShare { get; }

        public Slice(string id, string title, string glyph, string verb, Editor editor = Editor.None, bool share = false)
        {
            Id = id; Title = title; Glyph = glyph; Verb = verb; Editor = editor; IsShare = share;
        }

        public bool IsRecipe => Id.StartsWith("recipe.", StringComparison.Ordinal);

        public Slice WithId(string id) => new Slice(id, Title, Glyph, Verb, Editor, IsShare);

        public override string ToString() => Id;
    }

    /// <summary>A page of up to eight slices. Shift (or a finger tap) flips pages.</summary>
    public sealed class RingPage
    {
        public string Id { get; }
        public string Title { get; }
        public List<Slice> Slices { get; }

        public RingPage(string id, string title, IEnumerable<Slice> slices)
        {
            Id = id; Title = title; Slices = slices.ToList();
        }
    }

    /// <summary>What happened, for the progress bubble and Undo.</summary>
    public sealed class RunReport
    {
        public List<string> Outputs { get; } = new List<string>();
        public List<string> Failures { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();
        public long BytesBefore { get; set; }
        public long BytesAfter { get; set; }
        /// <summary>Text read by OCR, so the app can put it on the clipboard.</summary>
        public string Text { get; set; }

        public string Summary
        {
            get
            {
                if (Outputs.Count == 0 && Failures.Count > 0) return Failures[0];
                if (Outputs.Count == 0 && Notes.Count > 0) return Notes[0];
                var s = Outputs.Count == 1 ? "Saved " + Path.GetFileName(Outputs[0]) : "Saved " + Outputs.Count + " files";
                if (BytesBefore > 0 && BytesAfter > 0 && BytesAfter < BytesBefore)
                {
                    var pct = (int)((1 - (double)BytesAfter / BytesBefore) * 100);
                    s += " · " + Bytes.Format(BytesBefore) + " → " + Bytes.Format(BytesAfter) + " (−" + pct + "%)";
                }
                return s;
            }
        }

        public void Add(RunReport other)
        {
            Outputs.AddRange(other.Outputs);
            Failures.AddRange(other.Failures);
            Notes.AddRange(other.Notes);
            BytesBefore += other.BytesBefore;
            BytesAfter += other.BytesAfter;
            if (other.Text != null) Text = Text == null ? other.Text : Text + "\n\n" + other.Text;
        }
    }

    /// <summary>An error with a message that's fine to show people as is.</summary>
    public sealed class UserError : Exception
    {
        public UserError(string message) : base(message) { }
    }

    public static class Bytes
    {
        /// <summary>1,000-based like the size limits ("48 KB", "2.4 MB"), so the numbers match.</summary>
        public static string Format(long bytes)
        {
            if (bytes < 1000) return bytes + " bytes";
            double v = bytes / 1000.0;
            if (v < 1000) return (v < 10 ? v.ToString("0.#") : v.ToString("0")) + " KB";
            v /= 1000;
            if (v < 1000) return (v < 10 ? v.ToString("0.#") : v.ToString("0")) + " MB";
            return (v / 1000).ToString("0.##") + " GB";
        }
    }

    public static class Num
    {
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
