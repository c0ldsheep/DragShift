using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DragShift.Core;

namespace DragShift.Engine
{
    /// <summary>
    /// Prints pictures and PDFs with a preset through Windows printing: the preset's printer, paper, copies, two-sided
    /// and colour, with no dialog unless "More options…" is chosen. The sheets are laid out by PrintLayout (Core),
    /// and the same drawing makes the preview, so what the preview shows is what prints.
    /// </summary>
    static class PrintJobs
    {
        /// <summary>One printed sheet: its size in points and the pictures on it.</summary>
        public sealed class Sheet
        {
            public Size2 Size;
            public readonly List<Item> Items = new List<Item>();
        }

        public sealed class Item
        {
            public Image Image;
            public Box Box;
            public bool Rotate, Hairline, Clip;
        }

        public sealed class Prepared : IDisposable
        {
            public readonly List<Sheet> Sheets = new List<Sheet>();
            public readonly List<string> Notes = new List<string>();
            public readonly List<Image> Owned = new List<Image>();
            public bool Gray;
            public void Dispose() { foreach (var i in Owned) i.Dispose(); Owned.Clear(); }
        }

        public static List<string> Printers()
        {
            try { return PrinterSettings.InstalledPrinters.Cast<string>().ToList(); }
            catch (Exception) { return new List<string>(); }
        }

        public static string DefaultPrinter
        {
            get
            {
                try { var s = new PrinterSettings(); return s.IsValid ? s.PrinterName : null; }
                catch (Exception) { return null; }
            }
        }

        static PrinterSettings SettingsFor(PrintPreset p)
        {
            var s = new PrinterSettings();
            if (!string.IsNullOrEmpty(p.Printer) && Printers().Contains(p.Printer)) s.PrinterName = p.Printer;
            return s;
        }

        /// <summary>The paper for a preset, portrait, in points: its own size, or the printer's.</summary>
        public static Size2 PaperFor(PrintPreset p)
        {
            var own = PrintPreset.Size(p.Paper);
            if (own.HasValue) return own.Value;
            try
            {
                var ps = SettingsFor(p).DefaultPageSettings.PaperSize; // hundredths of an inch
                double w = ps.Width * 0.72, h = ps.Height * 0.72;
                if (w > 0 && h > 0) return new Size2(Math.Min(w, h), Math.Max(w, h));
            }
            catch (Exception) { }
            return PrintPreset.Size(Paper.A4).Value;
        }

        /// <summary>Loads and lays out the files. Documents and other files are skipped with a note.</summary>
        public static async Task<Prepared> Prepare(IList<string> files, PrintPreset preset, Size2 paper)
        {
            var job = new Prepared { Gray = !preset.Color };
            foreach (var path in files)
            {
                try
                {
                    switch (Inspector.Kind(path))
                    {
                        case FileKind.Image: await AddPicture(job, path, preset, paper); break;
                        case FileKind.Pdf: await AddPdf(job, path, preset, paper); break;
                        default: job.Notes.Add("Skipped " + Path.GetFileName(path) + ": it can't be printed here"); break;
                    }
                }
                catch (Exception e)
                {
                    job.Notes.Add("Skipped " + Path.GetFileName(path) + ": " + e.Message);
                }
            }
            return job;
        }

        static async Task AddPicture(Prepared job, string path, PrintPreset preset, Size2 paper)
        {
            var img = await Images.Load(path, 4200);
            job.Owned.Add(img);
            // Real size from the file's pixels and DPI; the file may be stored turned (EXIF), so match the shape.
            var real = PrintLayout.RealSize(img.Width, img.Height, 72, 72);
            try
            {
                using (var fs = File.OpenRead(path))
                using (var head = Image.FromStream(fs, false, false))
                    real = PrintLayout.RealSize(head.Width, head.Height, head.HorizontalResolution, head.VerticalResolution);
            }
            catch (Exception) { /* HEIC and friends: GDI+ can't read the header, 72 dpi it is */ }
            if ((img.Width > img.Height) != (real.Width > real.Height)) real = real.Turned;

            if (preset.FillSheet)
            {
                var boxes = PrintLayout.Grid(real, paper, out Size2 sheetSize);
                if (boxes.Count > 0)
                {
                    var sheet = new Sheet { Size = sheetSize };
                    foreach (var b in boxes) sheet.Items.Add(new Item { Image = img, Box = b, Hairline = true });
                    job.Sheets.Add(sheet);
                    return;
                }
            }
            var size = preset.Fit == PrintFit.Actual ? real : new Size2(img.Width, img.Height);
            var box = PrintLayout.Place(size, paper, preset.Fit, out bool rotate);
            var one = new Sheet { Size = paper };
            one.Items.Add(new Item { Image = img, Box = box, Rotate = rotate, Clip = preset.Fit == PrintFit.Fill });
            job.Sheets.Add(one);
        }

        static async Task AddPdf(Prepared job, string path, PrintPreset preset, Size2 paper)
        {
            var doc = await Pdf.Open(path);
            var fit = preset.Fit == PrintFit.Fill ? PrintFit.Fit : preset.Fit;
            for (uint i = 0; i < doc.PageCount; i++)
            {
                double pw = 0, ph = 0;
                var bmp = await Pdf.Render(doc, i, 200, (w, h) => { pw = w; ph = h; });
                job.Owned.Add(bmp);
                var box = PrintLayout.Place(new Size2(pw, ph), paper, fit, out bool rotate);
                var sheet = new Sheet { Size = paper };
                sheet.Items.Add(new Item { Image = bmp, Box = box, Rotate = rotate });
                job.Sheets.Add(sheet);
            }
        }

        /// <summary>Draws one sheet, in points with (0, 0) at the paper's top-left corner.</summary>
        public static void Draw(Graphics g, Sheet sheet, bool gray)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var attrs = new ImageAttributes())
            {
                if (gray)
                    attrs.SetColorMatrix(new ColorMatrix(new[]
                    {
                        new[] { 0.299f, 0.299f, 0.299f, 0, 0 }, new[] { 0.587f, 0.587f, 0.587f, 0, 0 }, new[] { 0.114f, 0.114f, 0.114f, 0, 0 },
                        new[] { 0f, 0, 0, 1, 0 }, new[] { 0f, 0, 0, 0, 1 },
                    }));
                foreach (var it in sheet.Items)
                {
                    var state = g.Save();
                    if (it.Clip) g.SetClip(new RectangleF(0, 0, (float)sheet.Size.Width, (float)sheet.Size.Height));
                    var b = it.Box;
                    // Upper-left, upper-right and lower-left corners of the picture; turned pictures go a quarter clockwise.
                    var corners = it.Rotate
                        ? new[] { new PointF((float)b.Right, (float)b.Y), new PointF((float)b.Right, (float)b.Bottom), new PointF((float)b.X, (float)b.Y) }
                        : new[] { new PointF((float)b.X, (float)b.Y), new PointF((float)b.Right, (float)b.Y), new PointF((float)b.X, (float)b.Bottom) };
                    g.DrawImage(it.Image, corners, new RectangleF(0, 0, it.Image.Width, it.Image.Height), GraphicsUnit.Pixel, attrs);
                    g.Restore(state);
                    if (it.Hairline)
                        using (var pen = new Pen(Color.FromArgb(200, 200, 200), 0.3f))
                            g.DrawRectangle(pen, (float)b.X, (float)b.Y, (float)b.Width, (float)b.Height);
                }
            }
        }

        /// <summary>A sheet as a picture <paramref name="width"/> pixels wide, for the print window.</summary>
        public static Bitmap Preview(Sheet sheet, bool gray, int width)
        {
            double k = width / sheet.Size.Width;
            var bmp = new Bitmap(width, Math.Max(1, (int)Math.Round(sheet.Size.Height * k)), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.ScaleTransform((float)k, (float)k);
                Draw(g, sheet, gray);
            }
            return bmp;
        }

        /// <summary>Sends the sheets to the preset's printer. <paramref name="toFile"/>: print to that file instead (the
        /// self-test uses "Microsoft Print to PDF"). <paramref name="dialog"/>: show Windows' print dialog first.
        /// Returns false when the person cancels the dialog.</summary>
        public static bool Send(Prepared job, PrintPreset preset, Size2 paper, string title, bool dialog = false, string toFile = null,
                                string printer = null)
        {
            if (job.Sheets.Count == 0) throw new UserError("Nothing here can be printed. Pictures and PDFs can.");
            using (var doc = new PrintDocument { DocumentName = title, OriginAtMargins = false, PrintController = new StandardPrintController() })
            {
                var s = SettingsFor(preset);
                if (printer != null) s.PrinterName = printer;
                if (!s.IsValid) throw new UserError("That printer isn't set up on this PC.");
                s.Copies = (short)Num.Clamp(preset.Copies, 1, 99);
                s.Collate = true;
                if (preset.TwoSided && s.CanDuplex) s.Duplex = Duplex.Vertical;
                if (toFile != null) { s.PrintToFile = true; s.PrintFileName = toFile; }
                doc.PrinterSettings = s;
                doc.DefaultPageSettings.Color = preset.Color && s.SupportsColor;
                doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                if (PrintPreset.Size(preset.Paper).HasValue) doc.DefaultPageSettings.PaperSize = PaperSizeFor(s, paper);

                int next = 0;
                doc.QueryPageSettings += (o, e) =>
                {
                    // A sideways sheet (eight passport photos on a 4×6) prints in landscape.
                    var sh = job.Sheets[next].Size;
                    e.PageSettings.Landscape = sh.Width > sh.Height;
                };
                doc.PrintPage += (o, e) =>
                {
                    var g = e.Graphics;
                    g.PageUnit = GraphicsUnit.Point;
                    // The printer's origin is its printable corner; move ours to the paper's corner.
                    g.TranslateTransform(-(float)(e.PageSettings.HardMarginX * 0.72), -(float)(e.PageSettings.HardMarginY * 0.72));
                    Draw(g, job.Sheets[next], job.Gray);
                    next++;
                    e.HasMorePages = next < job.Sheets.Count;
                };
                if (dialog)
                {
                    using (var d = new PrintDialog { Document = doc, UseEXDialog = true, AllowSomePages = true })
                        if (d.ShowDialog() != DialogResult.OK) return false;
                }
                doc.Print();
                return true;
            }
        }

        /// <summary>The printer's own paper of that size (either way round), or a custom size when it has none.</summary>
        static PaperSize PaperSizeFor(PrinterSettings s, Size2 paper)
        {
            int w = (int)Math.Round(paper.Width / 0.72), h = (int)Math.Round(paper.Height / 0.72);
            foreach (PaperSize ps in s.PaperSizes)
                if ((Math.Abs(ps.Width - w) <= 8 && Math.Abs(ps.Height - h) <= 8) || (Math.Abs(ps.Width - h) <= 8 && Math.Abs(ps.Height - w) <= 8))
                    return ps;
            return new PaperSize("DragShift", w, h);
        }
    }
}
