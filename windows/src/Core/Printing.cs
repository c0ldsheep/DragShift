using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DragShift.Core
{
    public enum Paper { PrinterDefault, A4, Letter, Legal, A5, A3, Photo4x6, Photo5x7 }

    /// <summary>How a picture sits on the page: whole picture, page filled (edges cropped), or its real printed size.</summary>
    public enum PrintFit { Fit, Fill, Actual }

    /// <summary>Saved print preferences: one tap prints with the printer, paper and options chosen before. Same presets
    /// and rules as the Mac.</summary>
    public sealed class PrintPreset
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Preset";
        /// <summary>null: Windows' default printer.</summary>
        public string Printer;
        public Paper Paper = Paper.PrinterDefault;
        public int Copies = 1;
        public bool Color = true;
        public bool TwoSided;
        public PrintFit Fit = PrintFit.Fit;
        /// <summary>Repeat each picture at its real size across the sheet, ready to cut (passport photos).</summary>
        public bool FillSheet;

        public static string Title(Paper p)
        {
            switch (p)
            {
                case Paper.A4: return "A4";
                case Paper.Letter: return "Letter";
                case Paper.Legal: return "Legal";
                case Paper.A5: return "A5";
                case Paper.A3: return "A3";
                case Paper.Photo4x6: return "4 × 6 in photo";
                case Paper.Photo5x7: return "5 × 7 in photo";
                default: return "Printer's paper";
            }
        }

        public static string Title(PrintFit f) => f == PrintFit.Fit ? "Fit" : f == PrintFit.Fill ? "Fill" : "Actual size";

        /// <summary>Portrait size in points (1/72 inch); null means "whatever the printer is set to".</summary>
        public static Size2? Size(Paper p)
        {
            const double mm = 72 / 25.4;
            switch (p)
            {
                case Paper.A4: return new Size2(210 * mm, 297 * mm);
                case Paper.Letter: return new Size2(612, 792);
                case Paper.Legal: return new Size2(612, 1008);
                case Paper.A5: return new Size2(148 * mm, 210 * mm);
                case Paper.A3: return new Size2(297 * mm, 420 * mm);
                case Paper.Photo4x6: return new Size2(288, 432);
                case Paper.Photo5x7: return new Size2(360, 504);
                default: return null;
            }
        }

        public static readonly PrintPreset[] BuiltIns =
        {
            new PrintPreset { Id = "everyday", Name = "Everyday" },
            new PrintPreset { Id = "bw", Name = "Black & white", Color = false, TwoSided = true },
            new PrintPreset { Id = "passport-4x6", Name = "Passport 4×6", Paper = Paper.Photo4x6, Fit = PrintFit.Actual, FillSheet = true },
            new PrintPreset { Id = "passport-a4", Name = "Passport A4", Paper = Paper.A4, Fit = PrintFit.Actual, FillSheet = true },
        };

        public PrintPreset Copy() => (PrintPreset)MemberwiseClone();

        /// <summary>Same printer, paper and options (the name and id don't count).</summary>
        public bool SameSettings(PrintPreset o) =>
            o != null && Printer == o.Printer && Paper == o.Paper && Copies == o.Copies && Color == o.Color && TwoSided == o.TwoSided
            && Fit == o.Fit && FillSheet == o.FillSheet;

        /// <summary>One line for lists: "EPSON L8050 · A4 · 2 copies · Black & white · Two-sided · Fit".</summary>
        public string Summary(string defaultPrinter = null)
        {
            var parts = new List<string> { Printer ?? (defaultPrinter != null ? defaultPrinter + " (default)" : "Default printer"), Title(Paper) };
            if (Copies > 1) parts.Add(Copies + " copies");
            parts.Add(Color ? "Colour" : "Black & white");
            if (TwoSided) parts.Add("Two-sided");
            parts.Add(FillSheet ? "Fill the sheet" : Title(Fit));
            return string.Join(" · ", parts);
        }

        /// <summary>The id of "whatever the print window sent last time", for double-tap repeat.</summary>
        public const string LastId = "last";
        public const string StepPrefix = "print.preset.";
        public string StepId => StepPrefix + Id;
        public static string IdFromStep(string step) =>
            step != null && step.StartsWith(StepPrefix, StringComparison.Ordinal) ? step.Substring(StepPrefix.Length) : null;
        public Slice Slice => new Slice(StepId, Name, Catalog.Glyph.Print, "Printing");

        // One settings line per preset: "id|name|printer|paper|copies|colour|two-sided|fit|fill" (name and printer escaped).
        public override string ToString() =>
            string.Join("|", Uri.EscapeDataString(Id), Uri.EscapeDataString(Name), Uri.EscapeDataString(Printer ?? ""), Paper,
                        Copies.ToString(CultureInfo.InvariantCulture), Color ? 1 : 0, TwoSided ? 1 : 0, Fit, FillSheet ? 1 : 0);

        public static PrintPreset Parse(string s)
        {
            var f = (s ?? "").Split('|');
            if (f.Length < 9) return null;
            try
            {
                var p = new PrintPreset
                {
                    Id = Uri.UnescapeDataString(f[0]), Name = Uri.UnescapeDataString(f[1]),
                    Printer = f[2].Length == 0 ? null : Uri.UnescapeDataString(f[2]),
                    Copies = Num.Clamp(int.Parse(f[4], CultureInfo.InvariantCulture), 1, 99),
                    Color = f[5] == "1", TwoSided = f[6] == "1", FillSheet = f[8] == "1",
                };
                if (Enum.TryParse(f[3], out Paper paper)) p.Paper = paper;
                if (Enum.TryParse(f[7], out PrintFit fit)) p.Fit = fit;
                return p.Id.Length > 0 ? p : null;
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }

        public static string Join(IEnumerable<PrintPreset> list) => string.Join(";", list.Select(p => p.ToString()));

        public static List<PrintPreset> ParseList(string s) =>
            (s ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(Parse).Where(p => p != null).ToList();
    }

    public struct Size2
    {
        public double Width, Height;
        public Size2(double w, double h) { Width = w; Height = h; }
        public Size2 Turned => new Size2(Height, Width);
    }

    /// <summary>Where things go on the paper, in points with y down. Pure geometry, so it's tested on its own.</summary>
    public static class PrintLayout
    {
        /// <summary>6 mm: inside what home printers can reach.</summary>
        public const double Margin = 6 * 72 / 25.4;
        /// <summary>2 mm between repeated photos, room for scissors.</summary>
        public const double Gap = 2 * 72 / 25.4;
        /// <summary>3 mm round a sheet of repeated photos: what photo printers reach, and what eight 3.5 × 4.5 cm photos on
        /// a 4 × 6 need (4 × 35 mm + 3 gaps = 146 of 152.4 mm).</summary>
        public const double SheetMargin = 3 * 72 / 25.4;

        /// <summary>The box a picture of <paramref name="item"/> size gets on <paramref name="page"/>, and whether it's turned 90°
        /// to use the page better. Fill covers the whole page (edges cropped); Actual keeps the real size but shrinks what
        /// doesn't fit.</summary>
        public static Box Place(Size2 item, Size2 page, PrintFit fit, out bool rotate, double margin = Margin)
        {
            rotate = false;
            if (item.Width <= 0 || item.Height <= 0) return new Box(0, 0, 0, 0);
            var area = fit == PrintFit.Fill ? new Box(0, 0, page.Width, page.Height)
                                            : new Box(margin, margin, page.Width - 2 * margin, page.Height - 2 * margin);
            rotate = fit != PrintFit.Actual && item.Width != item.Height && (item.Width > item.Height) != (area.Width > area.Height);
            var s = rotate ? item.Turned : item;
            double scale;
            switch (fit)
            {
                case PrintFit.Fill: scale = Math.Max(area.Width / s.Width, area.Height / s.Height); break;
                case PrintFit.Actual: scale = Math.Min(1, Math.Min(area.Width / s.Width, area.Height / s.Height)); break;
                default: scale = Math.Min(area.Width / s.Width, area.Height / s.Height); break;
            }
            double w = s.Width * scale, h = s.Height * scale;
            return new Box(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }

        /// <summary>Real-size copies of <paramref name="item"/> across a sheet, in whichever orientation fits more (the sheet
        /// comes back turned when sideways wins). Empty when not even two fit.</summary>
        public static List<Box> Grid(Size2 item, Size2 page, out Size2 sheet, double margin = SheetMargin, double gap = Gap)
        {
            List<Box> Layout(Size2 sh)
            {
                int cols = (int)((sh.Width - 2 * margin + gap) / (item.Width + gap));
                int rows = (int)((sh.Height - 2 * margin + gap) / (item.Height + gap));
                var list = new List<Box>();
                if (cols <= 0 || rows <= 0) return list;
                double bw = cols * item.Width + (cols - 1) * gap, bh = rows * item.Height + (rows - 1) * gap;
                double x0 = (sh.Width - bw) / 2, y0 = (sh.Height - bh) / 2;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        list.Add(new Box(x0 + c * (item.Width + gap), y0 + r * (item.Height + gap), item.Width, item.Height));
                return list;
            }
            var upright = Layout(page);
            var turned = Layout(page.Turned);
            var best = turned.Count > upright.Count ? turned : upright;
            sheet = turned.Count > upright.Count ? page.Turned : page;
            if (best.Count >= 2) return best;
            sheet = page;
            return new List<Box>();
        }

        /// <summary>A picture's real printed size in points, from its pixels and DPI (72 when the file doesn't say).</summary>
        public static Size2 RealSize(double pixelsW, double pixelsH, double dpiX, double dpiY)
        {
            double dx = dpiX > 1 ? dpiX : 72, dy = dpiY > 1 ? dpiY : 72;
            return new Size2(pixelsW * 72 / dx, pixelsH * 72 / dy);
        }
    }
}
