using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DragShift.Core;
using DragShift.Engine;
using CoreFormat = DragShift.Core.ImageFormat;
using WinForms = System.Windows.Forms;

namespace DragShift
{
    /// <summary>
    /// DragShift.exe --selftest [folder]: runs every engine step on made-up files and draws the ring to PNGs.
    /// Used on a real Windows PC or by the GitHub Actions check; prints PASS/FAIL/SKIP lines, exit code 1 on any failure.
    /// </summary>
    static class SelfTest
    {
        static int failed;
        static StringBuilder report = new StringBuilder();

        public static int Run(string[] args)
        {
            // Draw text the way the app does (Program.Main sets the same two things before its first window), so the pictures
            // show what a person sees. It has to happen before the first window of the process is made.
            try
            {
                WinForms.Application.EnableVisualStyles();
                WinForms.Application.SetCompatibleTextRenderingDefault(false);
            }
            catch (InvalidOperationException) { } // a window already exists: the text keeps the way it was drawn
            var dir = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "dragshift-selftest");
            Directory.CreateDirectory(dir);
            // Recipes and native converters must keep their temporary work with these made-up test files.
            var temp = Path.Combine(dir, "Temp");
            Directory.CreateDirectory(temp);
            Environment.SetEnvironmentVariable("TEMP", temp);
            Environment.SetEnvironmentVariable("TMP", temp);
            int code = Task.Run(() => All(dir)).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(dir, "selftest.txt"), report.ToString());
            Console.Out.Flush();
            // End now instead of unwinding. On a PC with AMD graphics driver 32.0.21045.5002 the driver (amdxx64.dll) crashed
            // while the process was shutting down after the face and PDF checks, so the exit code was a crash code instead
            // of 0 or 1. The report and the output are already written.
            Native.ExitNow(code);
            return code;
        }

        static void Line(string s)
        {
            Console.WriteLine(s);
            report.AppendLine(s);
        }

        static async Task Check(string name, Func<Task<string>> body)
        {
            try
            {
                var detail = await body();
                if (detail != null && detail.StartsWith("SKIP", StringComparison.Ordinal)) Line("SKIP " + name + ": " + detail.Substring(4).Trim());
                else Line("PASS " + name + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")"));
            }
            catch (Exception e)
            {
                failed++;
                Line("FAIL " + name + ": " + e.GetType().Name + ": " + e.Message);
            }
        }

        static void Expect(bool ok, string what) { if (!ok) throw new Exception("expected " + what); }

        static Bitmap Noise(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var r = new Random(7);
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4)
            {
                int x = i / 4 % w;
                px[i] = (byte)(x * 255 / w / 2 + r.Next(0, 128));
                px[i + 1] = (byte)r.Next(0, 256);
                px[i + 2] = (byte)(200 - x * 150 / w + r.Next(0, 50));
                px[i + 3] = 255;
            }
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            System.Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length);
            bmp.UnlockBits(d);
            return bmp;
        }

        /// <summary>A made-up portrait with nobody in it: tall dark hair far above a tight face box, a neck and shoulders, a wall
        /// that gets darker toward the bottom, and a ring that fences off a bit of wall. A green wall also has a hard-edged shadow.</summary>
        static Bitmap TallHairPortrait(bool green)
        {
            var bmp = new Bitmap(1200, 1600);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var top = green ? Color.FromArgb(100, 215, 152) : Color.FromArgb(226, 216, 200);
                var bottom = green ? Color.FromArgb(76, 184, 130) : Color.FromArgb(206, 196, 181);
                using (var wall = new LinearGradientBrush(new Rectangle(0, 0, 1200, 1600), top, bottom, 90f)) g.FillRectangle(wall, 0, 0, 1200, 1600);
                if (green) using (var shade = new SolidBrush(Color.FromArgb(110, 0, 40, 25))) g.FillRectangle(shade, 840, 560, 90, 300);
                using (var b = new SolidBrush(Color.FromArgb(150, 110, 90))) g.FillRectangle(b, 470, 40, 110, 100); // a poster on the wall, above the head: not hair
                using (var ring = new Pen(Color.FromArgb(70, 60, 55), 14)) g.DrawEllipse(ring, 290, 640, 80, 160); // wall inside it can't be reached
                using (var b = new SolidBrush(Color.FromArgb(128, 122, 100))) g.FillEllipse(b, 300, 1150, 600, 700); // shoulders
                using (var b = new SolidBrush(Color.FromArgb(190, 135, 105))) g.FillRectangle(b, 530, 800, 140, 360); // neck
                using (var b = new SolidBrush(Color.FromArgb(35, 28, 24))) g.FillEllipse(b, 385, 215, 430, 470); // tall hair
                using (var b = new SolidBrush(Color.FromArgb(200, 140, 110))) g.FillEllipse(b, 450, 520, 300, 300); // face
            }
            return bmp;
        }

        static Bitmap TextPicture(string text)
        {
            var bmp = new Bitmap(1400, 260);
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Segoe UI", 64))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(text, f, Brushes.Black, 40, 60);
            }
            return bmp;
        }

        static async Task<int> All(string dir)
        {
            Line("DragShift " + AppMain.Version + " self-test on " + Environment.OSVersion.VersionString + ", 64-bit: " + Environment.Is64BitProcess);
            var o = new Options();
            var src = Path.Combine(dir, "Holiday.png");
            using (var n = Noise(2400, 1600)) n.Save(src, System.Drawing.Imaging.ImageFormat.Png);
            var ct = CancellationToken.None;

            foreach (var f in new[] { CoreFormat.Jpg, CoreFormat.Png, CoreFormat.WebP, CoreFormat.Tiff, CoreFormat.Bmp })
            {
                await Check("picture → " + f.Title(), async () =>
                {
                    var r = await Runner.Run(new Slice("img.to." + f.Ext(), f.Title(), "", ""), new[] { src }, o, null, ct);
                    Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                    var outPath = r.Outputs.Single();
                    try
                    {
                        using (var back = await Images.Load(outPath)) Expect(back.Width == 2400 && back.Height == 1600, "same size after reopening");
                    }
                    catch (UserError) when (f == CoreFormat.WebP) { return "saved " + Bytes.Format(Inspector.Size(outPath)) + "; this PC has no WebP viewer to reopen it"; }
                    return Bytes.Format(Inspector.Size(outPath));
                });
            }

            await Check("HEIC saving", async () => await Images.CanWriteHeic() ? "available" : "SKIP needs the Store's HEIF + HEVC extensions");

            foreach (var kb in new[] { 50, 200 })
            {
                await Check("under " + kb + " KB", async () =>
                {
                    var r = await Runner.Run(SizeLimit.Slice(kb), new[] { src }, o, null, ct);
                    Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                    long size = Inspector.Size(r.Outputs.Single());
                    Expect(size <= SizeLimit.Bytes(kb), "≤ " + SizeLimit.Bytes(kb) + " bytes, got " + size);
                    return size + " bytes";
                });
            }

            string pdf = null;
            await Check("pictures → one PDF", async () =>
            {
                var second = Path.Combine(dir, "Scan.png");
                using (var t = TextPicture("Roll number 1234 DragShift")) t.Save(second, System.Drawing.Imaging.ImageFormat.Png);
                var r = await Runner.Run(Catalog.ImgPdf, new[] { src, second }, o, null, ct);
                Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                pdf = r.Outputs.Single();
                var doc = await Pdf.Open(pdf);
                Expect(doc.PageCount == 2, "2 pages, got " + doc.PageCount);
                return Path.GetFileName(pdf) + ", " + Bytes.Format(Inspector.Size(pdf));
            });

            await Check("PDF → JPG pages", async () =>
            {
                var r = await Runner.Run(Catalog.PdfJpg, new[] { pdf }, o, null, ct);
                Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                Expect(Directory.GetFiles(r.Outputs.Single()).Length == 2, "two pictures");
                return null;
            });

            await Check("PDF under 100 KB", async () =>
            {
                var r = await Runner.Run(SizeLimit.Slice(100), new[] { pdf }, o, null, ct);
                Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                long size = Inspector.Size(r.Outputs.Single());
                Expect(size <= 100_000, "≤ 100,000 bytes, got " + size);
                Expect((await Pdf.Open(r.Outputs.Single())).PageCount == 2, "still 2 pages");
                return size + " bytes";
            });

            await Check("reading text (OCR)", async () =>
            {
                if (!Vision.OcrAvailable) return "SKIP no OCR language installed on this PC";
                using (var t = TextPicture("Roll number 1234 DragShift"))
                {
                    var text = await Vision.ReadText(t);
                    Expect(text.Contains("1234"), "the number, got \"" + text + "\"");
                    return "\"" + text + "\"";
                }
            });

            await Check("passport framing (known face)", async () =>
            {
                using (var portrait = new Bitmap(1200, 1600))
                {
                    using (var g = Graphics.FromImage(portrait))
                    {
                        g.Clear(Color.FromArgb(214, 205, 190)); // a plain beige wall
                        g.FillEllipse(new SolidBrush(Color.FromArgb(150, 100, 80)), 360, 1000, 480, 700); // shoulders
                        g.FillRectangle(new SolidBrush(Color.FromArgb(200, 140, 110)), 450, 420, 300, 360); // face
                        g.FillEllipse(new SolidBrush(Color.FromArgb(40, 30, 25)), 430, 230, 340, 260); // hair
                    }
                    var face = new Box(450, 420, 300, 360);
                    var made = await Vision.Passport(portrait, new PhotoSpec(), face);
                    using (made.Image)
                    {
                        Expect(made.Image.Width == 413 && made.Image.Height == 531, "413 × 531");
                        var corner = made.Image.GetPixel(4, 4);
                        Expect(corner.R > 245 && corner.G > 245 && corner.B > 245, "the plain wall became white, got " + corner + (made.Note != null ? " (" + made.Note + ")" : ""));
                        var bytes = Images.Jpeg(made.Image, 0.9, 300);
                        var path = Path.Combine(dir, "Passport check.jpg");
                        File.WriteAllBytes(path, bytes);
                        return Bytes.Format(bytes.Length);
                    }
                }
            });

            await Check("passport photo: tall hair kept whole, shaded and fenced-off wall turned white (green and beige walls)", async () =>
            {
                var done = new List<string>();
                foreach (var green in new[] { true, false })
                {
                    var wall = green ? "green wall" : "beige wall";
                    using (var portrait = TallHairPortrait(green))
                    {
                        // What a detector gives: brows to chin, with the hair a full face-height above it (the usual estimate is 0.55).
                        var face = new Box(450, 520, 300, 300);
                        var spec = new PhotoSpec();
                        var top = Vision.HairTop(portrait, face);
                        Expect(top.HasValue && Math.Abs(top.Value - 215) < 12, wall + ": the top of the hair is found at 215, not the poster's top at 40, got " + (top.HasValue ? top.Value.ToString("0") : "nothing"));
                        var frame = Core.Passport.Frame(face, spec, top);
                        double k = spec.PixelHeight / frame.Height;
                        Point At(double x, double y) => new Point((int)Math.Round((x - frame.X) * k), (int)Math.Round((y - frame.Y) * k));
                        Func<Color, int> light = c => (c.R + c.G + c.B) / 3;
                        var made = await Vision.Passport(portrait, spec, face);
                        using (made.Image)
                        {
                            Expect(made.Image.Width == 413 && made.Image.Height == 531, wall + ": 413 × 531");
                            Expect(made.Note == null, wall + ": the wall was changed, not kept (" + made.Note + ")");
                            int darkest = 255;
                            for (int y = 0; y < 20; y++)
                                for (int x = 0; x < made.Image.Width; x++) darkest = Math.Min(darkest, light(made.Image.GetPixel(x, y)));
                            Expect(darkest > 240, wall + ": nothing cut off at the top of the picture, darkest pixel up there is " + darkest);
                            int hairRow = -1, mid = At(600, 0).X;
                            for (int y = 0; y < made.Image.Height && hairRow < 0; y++) if (light(made.Image.GetPixel(mid, y)) < 100) hairRow = y;
                            Expect(hairRow >= 30 && hairRow <= 60, wall + ": about 8% of the height above the hair, hair starts at row " + hairRow);
                            Action<string, Point> white = (what, p) =>
                            {
                                var c = made.Image.GetPixel(p.X, p.Y);
                                Expect(c.R > 245 && c.G > 245 && c.B > 245, wall + ": " + what + " became white, got " + c + " at " + p);
                            };
                            white("the top left corner", new Point(4, 4));
                            white("the bottom right corner", new Point(made.Image.Width - 5, made.Image.Height - 5));
                            white("the wall beside the head", At(330, 400));
                            white("the wall inside the ring", At(330, 720));
                            if (green) white("the shadow on the wall", At(880, 700));
                            var skin = made.Image.GetPixel(At(600, 680).X, At(600, 680).Y);
                            var hair = made.Image.GetPixel(At(600, 330).X, At(600, 330).Y);
                            Expect(skin.B < 200 && skin.R > 150, wall + ": the face is still skin, got " + skin);
                            Expect(light(hair) < 80, wall + ": the hair is still dark, got " + hair);
                            made.Image.Save(Path.Combine(dir, "Passport tall hair (" + (green ? "green" : "beige") + ").png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        done.Add(wall + ": crown " + top.Value.ToString("0"));
                    }
                }
                return string.Join(", ", done);
            });

            await Check("face finder", async () =>
            {
                if (!Vision.FacesAvailable) return "SKIP not supported on this PC";
                using (var blank = new Bitmap(400, 400))
                {
                    try { await Vision.Face(blank); throw new Exception("found a face in a blank picture"); }
                    catch (UserError) { return "runs (no face in a blank picture, as it should)"; }
                }
            });

            await Check("recipe: WhatsApp Small", async () =>
            {
                var recipe = Recipe.BuiltIns.First(r => r.Id == "recipe.whatsapp");
                var r = await Runner.RunRecipe(recipe, new[] { src }, o, null, ct);
                Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                Expect(Path.GetFileName(r.Outputs.Single()).StartsWith("Holiday WhatsApp Small", StringComparison.Ordinal), "named after the recipe");
                return Path.GetFileName(r.Outputs.Single());
            });

            await Check("zip and extract", async () =>
            {
                var r = await Runner.Run(Catalog.Zip, new[] { src }, o, null, ct);
                Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                var back = await Runner.Run(Catalog.Unzip, r.Outputs, o, null, ct);
                Expect(File.Exists(Path.Combine(back.Outputs.Single(), "Holiday.png")), "the file inside");
                return null;
            });

            await Check("audio: WAV → M4A and MP3", async () =>
            {
                var wav = Path.Combine(dir, "Tone.wav");
                WriteTone(wav, 2);
                var outs = new List<string>();
                foreach (var ext in new[] { "m4a", "mp3" })
                {
                    var r = await Runner.Run(new Slice("media.to." + ext, ext, "", ""), new[] { wav }, o, null, ct);
                    // Windows N editions and Windows Server ship without Media Foundation until it's added.
                    if (r.Failures.Count > 0) return "SKIP video/audio converters unavailable here: " + r.Failures[0];
                    outs.Add(Path.GetFileName(r.Outputs.Single()) + " " + Bytes.Format(Inspector.Size(r.Outputs.Single())));
                }
                return string.Join(", ", outs);
            });

            await Check("cancelled conversion leaves nothing behind, not even in the Recycle Bin", async () =>
            {
                var folder = Path.Combine(dir, "cancel-check");
                Directory.CreateDirectory(folder);
                var input = Path.Combine(folder, "Cancel Me.wav");
                WriteTone(input, 2);
                int before = int.Parse(await OnSta(() => RecycleBinCount(folder).ToString()));
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    bool stopped = false, unavailable = false;
                    try { await Media.Convert(input, "m4a", "Cancelled", false, false, folder, null, cancelled.Token); }
                    catch (OperationCanceledException) { stopped = true; }
                    catch (UserError) { unavailable = true; } // Windows N editions ship without Media Foundation
                    if (unavailable) return "SKIP video/audio converters unavailable here";
                    Expect(stopped, "the conversion stops when it is cancelled");
                }
                Expect(Directory.GetFiles(folder).Length == 1, "only the original is left in the folder, got " + string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
                int after = int.Parse(await OnSta(() => RecycleBinCount(folder).ToString()));
                Expect(after == before, "nothing from this folder went to the Recycle Bin (" + before + " before, " + after + " after)");
                return "stopped, folder clean, Recycle Bin unchanged";
            });

            await Check("print layout: passport photos on a 4×6", async () =>
            {
                var photo = Path.Combine(dir, "Print Passport.jpg");
                using (var b = Noise(413, 531)) { b.SetResolution(300, 300); b.Save(photo, System.Drawing.Imaging.ImageFormat.Jpeg); }
                var preset = PrintPreset.BuiltIns[2];
                using (var job = await PrintJobs.Prepare(new[] { photo }, preset, PrintJobs.PaperFor(preset)))
                {
                    Expect(job.Sheets.Count == 1 && job.Sheets[0].Items.Count == 8, "one sheet of eight, got " +
                           job.Sheets.Count + " sheet(s), " + job.Sheets.Sum(x => x.Items.Count) + " photo(s)");
                    using (var pic = PrintJobs.Preview(job.Sheets[0], job.Gray, 900)) pic.Save(Path.Combine(dir, "print-passport-sheet.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                return "8 on one sheet";
            });

            await Check("printing to Microsoft Print to PDF", async () =>
            {
                const string pdfPrinter = "Microsoft Print to PDF";
                if (!PrintJobs.Printers().Contains(pdfPrinter)) return "SKIP no Microsoft Print to PDF on this PC";
                var preset = PrintPreset.BuiltIns[1];
                var outs = new List<string>();
                foreach (var (name, inputs) in new[] { ("pdf", new[] { pdf }), ("passport", new[] { Path.Combine(dir, "Print Passport.jpg") }) })
                {
                    var target = Path.Combine(dir, "printed-" + name + ".pdf");
                    var active = name == "passport" ? PrintPreset.BuiltIns[2] : preset;
                    var paper = PrintJobs.PaperFor(active);
                    Size2 sheet;
                    using (var job = await PrintJobs.Prepare(inputs, active, paper))
                    {
                        sheet = job.Sheets[0].Size;
                        if (name == "passport") Expect(job.Sheets[0].Items.Count == 8, "eight passport photos on a 4×6 sheet");
                        Expect(PrintJobs.Send(job, active, paper, "DragShift self-test", false, target, pdfPrinter), "sent");
                    }
                    for (int i = 0; i < 50 && !File.Exists(target); i++) await Task.Delay(200);
                    Expect(File.Exists(target) && new FileInfo(target).Length > 1000, "a printed PDF at " + target);
                    var printed = await Pdf.Open(target);
                    Expect(printed.PageCount == (name == "passport" ? 1u : 2u), "the printed page count");
                    using (var page = printed.GetPage(0))
                        Expect(Math.Abs(page.Size.Width * 72 / 96 - sheet.Width) < 2 &&
                               Math.Abs(page.Size.Height * 72 / 96 - sheet.Height) < 2, "the printed paper size matches the preset");
                    outs.Add(Path.GetFileName(target) + " " + Bytes.Format(new FileInfo(target).Length));
                }
                return string.Join(", ", outs);
            });

            await Check("PDF paper: correct size or a plain refusal", async () =>
            {
                const string pdfPrinter = "Microsoft Print to PDF";
                if (!PrintJobs.Printers().Contains(pdfPrinter)) return "SKIP no Microsoft Print to PDF on this PC";
                var preset = PrintPreset.BuiltIns[2];
                var paper = PrintJobs.PaperFor(preset);
                var target = Path.Combine(dir, "printed-paper-check.pdf");
                Size2 sheet;
                using (var job = await PrintJobs.Prepare(new[] { Path.Combine(dir, "Print Passport.jpg") }, preset, paper))
                {
                    sheet = job.Sheets[0].Size;
                    try
                    {
                        Expect(PrintJobs.Send(job, preset, paper, "DragShift paper check", false, target, pdfPrinter), "sent");
                    }
                    catch (UserError e)
                    {
                        Expect(e.Message == "Microsoft Print to PDF doesn't support that paper size. Choose A4 or Letter in the Print window.",
                               "a plain unsupported-paper message");
                        Expect(!File.Exists(target), "no PDF with the wrong paper size");
                        return "refused before printing; no PDF written";
                    }
                }
                for (int i = 0; i < 50 && !File.Exists(target); i++) await Task.Delay(200);
                var printed = await Pdf.Open(target);
                Expect(printed.PageCount == 1, "one printed page");
                using (var page = printed.GetPage(0))
                    Expect(Math.Abs(page.Size.Width * 72 / 96 - sheet.Width) < 2 &&
                           Math.Abs(page.Size.Height * 72 / 96 - sheet.Height) < 2, "the requested paper size, without substitution");
                return "printed at the requested size";
            });

            await Check("ring pictures", async () =>
            {
                await Task.Yield();
                var shots = new List<string>();
                foreach (var dark in new[] { false, true })
                {
                    var m = new RingModel
                    {
                        Pages = Catalog.Pages(new[] { src }, Capabilities.All, favorites: Catalog.DefaultFavorites),
                        Title = "Holiday.png", Subtitle = "2.4 MB",
                    };
                    for (int page = 0; page < m.Pages.Count; page++)
                    {
                        m.Page = page;
                        m.Hovered = page == 0 ? 1 : (int?)null;
                        shots.Add(Snapshot(m, dark, Path.Combine(dir, "ring-" + (dark ? "dark" : "light") + "-p" + page + ".png")));
                    }
                    m.Page = 1;
                    m.Mode = RingModel.RingMode.Pinned;
                    m.Hovered = 2;
                    shots.Add(Snapshot(m, dark, Path.Combine(dir, "ring-" + (dark ? "dark" : "light") + "-pinned.png")));
                    m.Armed = m.Pages[1].Slices[0];
                    shots.Add(Snapshot(m, dark, Path.Combine(dir, "ring-" + (dark ? "dark" : "light") + "-armed.png")));
                    var many = new RingModel
                    {
                        Pages = Catalog.Pages(new[] { src, Path.Combine(dir, "Holiday.jpg"), Path.Combine(dir, "Scan.png") }, Capabilities.All,
                                              favorites: Catalog.DefaultFavorites),
                        Title = "3 pictures", Subtitle = "Stitch",
                    };
                    many.Page = many.Pages.FindIndex(p => p.Id == "edit");
                    Expect(many.Page >= 0 && many.Slices.Any(s => s.Id == Catalog.StitchAll.Id), "Stitch on the Edit page");
                    shots.Add(Snapshot(many, dark, Path.Combine(dir, "ring-" + (dark ? "dark" : "light") + "-stitch.png")));
                }
                return shots.Count + " pictures";
            });

            await Check("Show multiple outputs: child item lists for Explorer", () => OnSta(() =>
            {
                int called = 0;
                Shell.RevealTogether(dir, new[] { src, Path.Combine(dir, "Scan.png") }, (folder, count, items, flags) =>
                {
                    called++;
                    Expect(folder != IntPtr.Zero && count == 2 && items.Length == 2 && flags == 0, "the folder and both files reach Explorer");
                    foreach (var item in items)
                    {
                        int length = unchecked((ushort)System.Runtime.InteropServices.Marshal.ReadInt16(item));
                        Expect(length > 0 && System.Runtime.InteropServices.Marshal.ReadInt16(item, length) == 0,
                               "a selected item is one child ID, not an absolute path list");
                    }
                    return 0;
                });
                Expect(called == 1, "one selection request");
                return "two real files; native argument contract checked without opening Explorer";
            }));

            await Check("names with an & show as typed (labels and the print window's preset chips)", () => OnSta(() =>
            {
                // Windows turns "& " into a shortcut key and drops it: "Black & white" read "Black  white", "Tom & Jerry.jpg" too.
                Expect(!Ui.Text("Tom & Jerry.jpg").UseMnemonic, "a label shows its text as typed");
                var ini = Path.Combine(dir, "print-window-test.ini");
                File.Delete(ini);
                using (var form = new PrintForm(new[] { src }, new DragShift.Core.Settings(ini)))
                {
                    var handle = form.Handle; // never shown
                    typeof(PrintForm).GetMethod("DrawChips", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(form, null);
                    var chips = Descendants(form).OfType<WinForms.Button>().Where(b => b.Text.Contains("&")).ToList();
                    Expect(chips.Count >= 1, "the Black & white preset chip exists");
                    Expect(chips.All(b => !b.UseMnemonic), "the chip " + string.Join(", ", chips.Select(b => b.Text)) + " shows its & as typed");
                }
                return "labels and preset chips keep their &";
            }));

            await Check("ring centre click cancels without running an action", CheckRingDismissal);
            await Check("mouse buttons: right, wheel and side buttons open the ring, and only the ticked ones", CheckMouseButtons);
            await Check("second start with no files asks the running DragShift to open Settings", () => CheckOpenSettings(src));
            await Check("clipboard imports: action, cancellation and quit cleanup", () => CheckClipboardInputs(dir));
            await Check("favourites shelf: the gear opens Settings, the favourites still run", () => CheckShelfGear(dir, src));
            await Check("Settings and Welcome windows: mouse buttons tick and untick, nothing is cut off", () => CheckWindows(dir));

            await Check("stitch 3 pictures into one long JPG", async () =>
            {
                // Three "pages" in solid colours and different sizes, so the order and the shared width can be read back.
                var pages = new List<string>();
                var looks = new[] { (800, 600, Color.Red), (800, 400, Color.Lime), (400, 300, Color.Blue) };
                for (int i = 0; i < looks.Length; i++)
                {
                    var p = Path.Combine(dir, "Page " + (i + 1) + ".png");
                    using (var b = new Bitmap(looks[i].Item1, looks[i].Item2))
                    {
                        using (var g = Graphics.FromImage(b)) g.Clear(looks[i].Item3);
                        b.Save(p, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    pages.Add(p);
                }
                pages.Reverse(); // given out of order, as a drag can be
                var r = await Runner.Run(Catalog.StitchAll, pages, o, null, ct);
                Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                var outPath = r.Outputs.Single();
                Expect(Path.GetFileName(outPath) == "Page 1 (3 stitched).jpg", "named after the first page, got " + Path.GetFileName(outPath));
                using (var back = await Images.Load(outPath))
                {
                    Expect(back.Width == 800 && back.Height == 1600, "800 × 1600 (the narrow page grows to the shared width), got " + back.Width + " × " + back.Height);
                    Color top = back.GetPixel(400, 300), mid = back.GetPixel(400, 800), low = back.GetPixel(400, 1300);
                    Expect(top.R > 200 && top.G < 80 && mid.G > 200 && mid.R < 80 && low.B > 200 && low.R < 80, "page 1 on top, then 2, then 3");
                }
                foreach (var p in pages) Expect(File.Exists(p), "the originals stay");
                return "800 × 1600";
            });

            await Check("About tab: the coffee cup tips and spills as it is pulled, and its ring", () => CheckCup(dir));

            await Check("About information follows codec checks and completed files", () => CheckAboutRefresh(dir));

            await Check("Settings window: the page names are one keyboard stop and a tab list a screen reader can read", () => CheckSettingsTabs(dir));

            await Check("Settings window: every page shows at once, without a storm of layouts", () => CheckSettingsSpeed(dir));

            await Check("Settings window: every setting is saved under its own name, and put back", () => CheckSettingsWiring(dir));

            await Check("Saving tab: the remembered folder is shown only while it is in use", () => CheckSavingFolderLabel(dir));

            await Check("Start with Windows and Send to follow the copy that was started last", () => CheckFollowLastStart(dir));

            await Check("tip windows: selected coffee size in the title", () => OnSta(() =>
            {
                var titles = new[] { "Buy me a coffee", "Small coffee · $2", "Medium coffee · $5", "Large coffee · $10" };
                for (int i = 0; i < titles.Length; i++)
                    using (var form = new TipForm(i - 1))
                        Expect(form.Text == titles[i], "window title " + titles[i]);
                return "generic click and all three sizes; no window shown";
            }));

            await Check("bubble: a long title is drawn whole beside its buttons", () => OnSta(() =>
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(Hud);
                var fTitle = type.GetField("title", flags); var fDetail = type.GetField("detail", flags); var fState = type.GetField("state", flags);
                var fShow = type.GetField("canShow", flags); var fUndo = type.GetField("canUndo", flags); var fWidth = type.GetField("W", flags);
                var draw = type.GetMethod("Draw", flags);
                Expect(new[] { fTitle, fDetail, fState, fShow, fUndo, fWidth }.All(f => f != null) && draw != null, "the bubble still has the fields this check reads");
                using (var hud = new Hud())
                {
                    Bitmap Render(string heading, Hud.State state, bool show, bool undo, float width)
                    {
                        fTitle.SetValue(hud, heading); fDetail.SetValue(hud, "Saved Scan A (3 stitched).jpg"); fState.SetValue(hud, state);
                        fShow.SetValue(hud, show); fUndo.SetValue(hud, undo); fWidth.SetValue(hud, width);
                        var bmp = new Bitmap(620, 130, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            g.TranslateTransform(12, 10); // as the bubble does
                            draw.Invoke(hud, new object[] { g, false });
                        }
                        return bmp;
                    }
                    // The title's own area: right of the status mark, on the title's line (the detail line starts below it).
                    bool SameTitle(Bitmap a, Bitmap b, int width)
                    {
                        for (int y = 10 + 18; y < 10 + 41; y++)
                            for (int x = 12 + 62; x < 12 + 62 + width; x++)
                                if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
                        return true;
                    }
                    var cases = new[]
                    {
                        ("Stitched into one long picture", Hud.State.Done, new[] { "Show", "Undo" }),
                        ("Stitching into one long picture…", Hud.State.Working, new[] { "Cancel" }),
                        ("Converted to JPG", Hud.State.Done, new[] { "Show", "Undo" }),
                    };
                    foreach (var c in cases)
                    {
                        bool show = c.Item3.Contains("Show"), undo = c.Item3.Contains("Undo");
                        float w = Hud.WidthFor(c.Item1, c.Item3);
                        int titleWidth;
                        using (var probe = new Bitmap(1, 1)) using (var g = Graphics.FromImage(probe)) using (var f = RingPainter.Text(14, FontStyle.Bold))
                            titleWidth = (int)Math.Min(400, g.MeasureString(c.Item1, f).Width);
                        using (var fit = Render(c.Item1, c.Item2, show, undo, w))
                        using (var wide = Render(c.Item1, c.Item2, show, undo, 560f))
                            Expect(SameTitle(fit, wide, titleWidth), "\"" + c.Item1 + "\" is not cut at " + (int)w + " wide");
                    }
                    var longTitle = cases[0];
                    Expect(Hud.WidthFor(longTitle.Item1, longTitle.Item3) > 380, "the long title makes the bubble wider than 380");
                    Expect(Hud.WidthFor("Copied", new string[0]) == 380 && Hud.WidthFor("Converted to JPG", new[] { "Show", "Undo" }) == 380, "short titles keep the usual 380");
                    using (var old = Render(longTitle.Item1, longTitle.Item2, true, true, 380f))
                    using (var wide = Render(longTitle.Item1, longTitle.Item2, true, true, 560f))
                        Expect(!SameTitle(old, wide, 200), "at the old fixed width this long title really was cut (the check can see a cut)");
                }
                return "long titles widen the bubble up to 560; short ones stay 380; no window shown";
            }));

            await Check("booby-trapped zip: nothing lands outside its folder", async () =>
            {
                // A zip can name its files "../somewhere-else". Extract must keep every file inside the new folder.
                var trap = Path.Combine(dir, "Trap.zip");
                File.Delete(trap);
                using (var z = ZipFile.Open(trap, ZipArchiveMode.Create))
                    foreach (var name in new[] { "inside.txt", "../ESCAPED-1.txt", "..\\ESCAPED-2.txt", "sub/../../ESCAPED-3.txt" })
                        using (var w = new StreamWriter(z.CreateEntry(name).Open())) w.Write("test");
                var r = await Runner.Run(Catalog.Unzip, new[] { trap }, o, null, ct);
                Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
                Expect(File.Exists(Path.Combine(r.Outputs.Single(), "inside.txt")), "inside.txt in the new folder");
                var up = Path.GetDirectoryName(Path.GetFullPath(dir).TrimEnd('\\', '/'));
                Expect(Directory.GetFiles(dir, "ESCAPED-*").Length == 0, "no ESCAPED file next to the zip");
                Expect(up == null || Directory.GetFiles(up, "ESCAPED-*").Length == 0, "no ESCAPED file one folder up");
                return null;
            });

            await Check("oversized zip: stopped, and nothing half-made is left", async () =>
            {
                await Task.Yield();
                // The real limit is 20 GB (or the free space); the same code runs here with a 100 KB limit.
                var big = Path.Combine(dir, "Big.zip");
                File.Delete(big);
                using (var z = ZipFile.Open(big, ZipArchiveMode.Create))
                using (var s = z.CreateEntry("zeros.bin").Open()) s.Write(new byte[300000], 0, 300000);
                bool stopped = false;
                try { Runner.Unzip(big, dir, 100000); } catch (UserError) { stopped = true; }
                Expect(stopped, "a plain message instead of unpacking");
                Expect(!Directory.Exists(Path.Combine(dir, "Big")), "no half-made folder");
                return null;
            });

            await Check("zip extraction preserves existing folders on failure and success", async () =>
            {
                await Task.Yield();
                var test = Path.Combine(dir, "zip-preservation");
                Directory.CreateDirectory(test);
                var original = Path.Combine(test, "Archive");
                Directory.CreateDirectory(original);
                var sentinel = Path.Combine(original, "sentinel.txt");
                File.WriteAllText(sentinel, "existing original");
                var archive = Path.Combine(test, "Archive.zip");
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                {
                    zip.CreateEntry("hold/");
                    zip.CreateEntry("hold"); // A file cannot replace the directory extracted just before it.
                }
                bool stopped = false;
                try { Runner.Unzip(archive, test); }
                catch (IOException) { stopped = true; }
                catch (UnauthorizedAccessException) { stopped = true; }
                Expect(stopped, "the malformed archive is rejected");
                Expect(File.ReadAllText(sentinel) == "existing original", "the existing folder survives failed extraction");
                Expect(!Directory.Exists(Path.Combine(test, "Archive 2")) &&
                       Directory.GetDirectories(test, ".dragshift-unzip-*").Length == 0, "no partial extraction remains");
                File.Delete(archive);
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry("marker.txt");
                var output = Runner.Unzip(archive, test);
                Expect(Path.GetFileName(output) == "Archive 2" && File.Exists(Path.Combine(output, "marker.txt")),
                       "a complete extraction gets a separate unique folder");
                Expect(File.ReadAllText(sentinel) == "existing original", "the existing folder also survives successful extraction");
                Expect(Directory.GetDirectories(test, ".dragshift-unzip-*").Length == 0, "successful extraction leaves no stage");
                return "original preserved; failed extraction cleaned; complete extraction published separately";
            });

            await Check("giant picture: refused before it is decoded", async () =>
            {
                // 16,000 × 16,000 = 256 megapixels in a file of a few KB: it would need about 1 GB once decoded.
                var giant = Path.Combine(dir, "Giant.png");
                using (var b = new Bitmap(16000, 16000, PixelFormat.Format1bppIndexed)) b.Save(giant, System.Drawing.Imaging.ImageFormat.Png);
                var r = await Runner.Run(Catalog.Compress, new[] { giant }, o, null, ct);
                Expect(r.Outputs.Count == 0, "no output");
                Expect(r.Failures.Count == 1 && r.Failures[0].Contains("megapixels"), "a plain message, got: " + string.Join("; ", r.Failures));
                return new FileInfo(giant).Length / 1000 + " KB file, 256 megapixels";
            });

            await Check("decoded dimensions: bounds checked without allocating pixels", async () =>
            {
                await Task.Yield();
                Images.RequireSafeSize(20000, 12500, "Boundary"); // Exactly 250 megapixels.
                foreach (var size in new[] { new[] { 20000d, 12501d }, new[] { (double)uint.MaxValue, (double)uint.MaxValue },
                                            new[] { double.NaN, 1d }, new[] { double.PositiveInfinity, 1d }, new[] { 0d, 1d } })
                {
                    bool stopped = false;
                    try { Images.RequireSafeSize(size[0], size[1], "Synthetic picture"); }
                    catch (UserError) { stopped = true; }
                    Expect(stopped, "invalid or oversized dimensions are refused without overflow");
                }
                return "250 MP boundary, oversized uint dimensions, nonfinite and zero sizes";
            });

            await Check("giant PDF page: refused before native rendering", async () =>
            {
                var huge = Path.Combine(dir, "LargePage.pdf");
                using (var pixel = new Bitmap(1, 1))
                    File.WriteAllBytes(huge, PdfWriter.Make(new[] { new PdfWriter.Page
                    {
                        Jpeg = Images.Jpeg(pixel, 0.8), PixelWidth = 1, PixelHeight = 1, Width = 14400, Height = 14400,
                    } }));
                var doc = await Pdf.Open(huge);
                bool stopped = false, reachedRender = false;
                try
                {
                    using (var image = await Pdf.Render(doc, 0, 96, (w, h) =>
                    {
                        // Even if the guard regresses, stop before the native renderer can allocate the huge raster.
                        reachedRender = true;
                        throw new InvalidOperationException("Oversized page reached the render boundary.");
                    })) { }
                }
                catch (UserError e) { stopped = e.Message.Contains("megapixels, too big to open safely"); }
                Expect(stopped && !reachedRender, "a plain size error before the render boundary");
                return new FileInfo(huge).Length + " byte PDF; no oversized raster requested";
            });

            // Merge, Unlock and Redact. The locked PDF is the one in windows\tests\fixtures (found next to this copy or the folder it runs in).
            var lockedPdf = FindFixture("locked-1234.pdf");
            await Check("Merge: two PDFs and a picture make one PDF; each page keeps its size and place; nothing that was there changes", () => CheckMerge(dir, lockedPdf));
            await Check("Merge: cancelling half-way leaves nothing behind, not even a hidden file", () => CheckMergeCancel(dir));
            await Check("Unlock: the right password, a wrong one, a PDF with no password, and several PDFs at once", () => CheckUnlock(dir, lockedPdf));
            await Check("Redact: the email and the phone number are found, and gone from the saved copy (Black bar, Pixelate, Blur)", () => CheckRedact(dir));
            await Check("Window checker: it stays quiet for a fine window and notices a cut-off label, a cut-off button, a control sticking out and an overlap", () => CheckWindowChecker());
            await Check("Redact window: boxes, click to turn one off, drag to add one, style remembered, no text reader, picture it can't open", () => CheckRedactWindow(dir));
            await Check("Unlock window: a wrong password keeps it open and says so, the right one closes it", () => CheckPasswordWindow(dir, lockedPdf));

            Line(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            return failed == 0 ? 0 : 1;
        }

        // MARK: Merge, Unlock and Redact

        /// <summary>Hands every progress report on the spot, so a test can cancel at an exact point.</summary>
        sealed class Instant : IProgress<double>
        {
            readonly Action<double> act;
            public Instant(Action<double> act) { this.act = act; }
            public void Report(double value) => act(value);
        }

        static string Hash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(fs));
        }

        /// <summary>A test file that lives in the repository (windows\tests\fixtures), looked for from this copy and from the folder it
        /// was started in, so it works for the build on the Windows check and on a PC alike. Null when it isn't there.</summary>
        static string FindFixture(string name)
        {
            var env = Environment.GetEnvironmentVariable("DRAGSHIFT_FIXTURES");
            if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, name))) return Path.Combine(env, name);
            foreach (var start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
                for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                    foreach (var relative in new[] { @"windows\tests\fixtures", @"tests\fixtures" })
                    {
                        var path = Path.Combine(d.FullName, relative, name);
                        if (File.Exists(path)) return path;
                    }
            return null;
        }

        static string Folder(string dir, string name)
        {
            var folder = Path.Combine(dir, name);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>A page of one colour, <paramref name="width"/> × <paramref name="height"/> points.</summary>
        static PdfWriter.Page SolidPage(Color colour, double width, double height)
        {
            using (var b = Images.Canvas(40, (int)Math.Round(40 * height / width), colour))
                return new PdfWriter.Page { Jpeg = Images.Jpeg(b, 0.9), PixelWidth = b.Width, PixelHeight = b.Height, Width = width, Height = height };
        }

        static string MakePdf(string path, params PdfWriter.Page[] pages)
        {
            File.WriteAllBytes(path, PdfWriter.Make(pages));
            return path;
        }

        /// <summary>Every page of a PDF as Windows' own reader sees it: its size in points and the colour in its middle.</summary>
        static async Task<List<(double Width, double Height, Color Middle)>> PagesOf(string path)
        {
            var doc = await Pdf.Open(path);
            var pages = new List<(double, double, Color)>();
            for (uint i = 0; i < doc.PageCount; i++)
            {
                double w = 0, h = 0;
                using (var img = await Pdf.Render(doc, i, 24, (pw, ph) => { w = pw; h = ph; }))
                    pages.Add((w, h, img.GetPixel(img.Width / 2, img.Height / 2)));
            }
            return pages;
        }

        static bool Near(Color c, Color want, int slack = 30) =>
            Math.Abs(c.R - want.R) <= slack && Math.Abs(c.G - want.G) <= slack && Math.Abs(c.B - want.B) <= slack;

        static async Task<string> CheckMerge(string dir, string locked)
        {
            var folder = Folder(dir, "merge-check");
            Color red = Color.FromArgb(220, 40, 40), blue = Color.FromArgb(40, 60, 220), green = Color.FromArgb(40, 180, 60), gold = Color.FromArgb(240, 200, 30);
            var a = MakePdf(Path.Combine(folder, "Merge A.pdf"), SolidPage(red, 595, 842), SolidPage(blue, 842, 595));
            var b = MakePdf(Path.Combine(folder, "Merge B.pdf"), SolidPage(green, 612, 792));
            var c = Path.Combine(folder, "Merge C.png");
            using (var pic = Images.Canvas(600, 400, gold)) pic.Save(c, System.Drawing.Imaging.ImageFormat.Png);
            var originals = new[] { a, b, c };
            var hashes = originals.Select(Hash).ToList();
            var o = new Options();

            // Dragged in any order, joined in Explorer's name order: 2 pages + 1 page + 1 picture = 4 pages.
            var r = await Runner.Run(Catalog.PdfMerge, new[] { c, b, a }, o, null, CancellationToken.None);
            Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
            var merged = r.Outputs.Single();
            Expect(Path.GetFileName(merged) == "Merge A Merged.pdf" && Path.GetDirectoryName(merged) == folder, "named after the first file and saved beside it, got " + merged);
            Expect(r.Notes.Contains(PdfRewrite.PicturesNote), "the note that text can't be selected");
            var pages = await PagesOf(merged);
            Expect(pages.Count == 4, "4 pages, got " + pages.Count);
            double[] widths = { 595, 842, 612, 600 }, heights = { 842, 595, 792, 400 };
            Color[] colours = { red, blue, green, gold };
            for (int i = 0; i < 4; i++)
            {
                Expect(Math.Abs(pages[i].Width - widths[i]) <= 1 && Math.Abs(pages[i].Height - heights[i]) <= 1,
                       "page " + (i + 1) + " is " + widths[i] + " × " + heights[i] + " pt like its original, got " + pages[i].Width.ToString("0.#") + " × " + pages[i].Height.ToString("0.#"));
                Expect(Near(pages[i].Middle, colours[i]), "page " + (i + 1) + " is the right page, in the right place (its colour is " + pages[i].Middle + ")");
            }
            Expect(originals.Select(Hash).SequenceEqual(hashes), "the three originals are byte for byte what they were");

            // A second merge gets its own name; the first result is left alone.
            var mergedHash = Hash(merged);
            var again = await Runner.Run(Catalog.PdfMerge, new[] { a, b, c }, o, null, CancellationToken.None);
            Expect(again.Failures.Count == 0 && again.Outputs.Single() != merged && Hash(merged) == mergedHash, "a second merge doesn't take the first one's place, got " + string.Join(", ", again.Outputs));

            // A picture and a PDF together (the Files page): one PDF too.
            var one = await Runner.Run(Catalog.OnePdf, new[] { c, b }, o, null, CancellationToken.None);
            Expect(one.Failures.Count == 0, string.Join("; ", one.Failures));
            var onePages = await PagesOf(one.Outputs.Single());
            Expect(Path.GetFileName(one.Outputs.Single()).StartsWith("Merge B Merged", StringComparison.Ordinal) && onePages.Count == 2
                   && Near(onePages[0].Middle, green) && Near(onePages[1].Middle, gold), "One PDF: the PDF page, then the picture, named after the first file");

            // A password-protected PDF stops the whole job, says what to do, and makes nothing.
            string lockedNote = "locked PDF not checked here (the test file isn't next to this copy)";
            if (locked != null)
            {
                var l = Path.Combine(folder, "locked-1234.pdf");
                File.Copy(locked, l);
                int files = Directory.GetFiles(folder).Length;
                var stopped = await Runner.Run(Catalog.PdfMerge, new[] { a, l }, o, null, CancellationToken.None);
                Expect(stopped.Outputs.Count == 0 && stopped.Failures.Count == 1 && stopped.Failures[0] == "locked-1234.pdf is password-protected. Use Unlock first.",
                       "the job stops with the Unlock hint, got " + string.Join("; ", stopped.Failures));
                Expect(Directory.GetFiles(folder).Length == files, "nothing made");
                lockedNote = "a locked PDF stops it with the Unlock hint";
            }
            return "4 pages (595×842, 842×595, 612×792 and 600×400 pt) in the right order; originals unchanged; a second merge gets its own name; " + lockedNote;
        }

        static async Task<string> CheckMergeCancel(string dir)
        {
            var folder = Folder(dir, "merge-cancel-check");
            var long24 = MakePdf(Path.Combine(folder, "Long A.pdf"), Enumerable.Range(0, 24).Select(i => SolidPage(Color.FromArgb(40 + i * 8, 90, 200 - i * 6), 595, 842)).ToArray());
            var one = MakePdf(Path.Combine(folder, "Long B.pdf"), SolidPage(Color.Gray, 595, 842));
            var before = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(x => x).ToList();
            double reached = 0;
            bool stopped = false;
            using (var cts = new CancellationTokenSource())
            {
                var progress = new Instant(f => { reached = f; if (f >= 0.5) cts.Cancel(); });
                try { await Runner.Run(Catalog.PdfMerge, new[] { long24, one }, new Options(), progress, cts.Token); }
                catch (OperationCanceledException) { stopped = true; }
            }
            Expect(stopped, "cancelling stops the merge");
            Expect(reached >= 0.5 && reached < 1, "it was cancelled half-way, at " + reached.ToString("0.00"));
            var after = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(x => x).ToList();
            Expect(after.SequenceEqual(before), "no new file in the folder, not even a hidden one; it now holds: " + string.Join(", ", after));
            return "cancelled at " + (int)Math.Round(reached * 100) + "%; the folder is exactly as it was";
        }

        static async Task<string> CheckUnlock(string dir, string fixture)
        {
            if (fixture == null) return "SKIP the locked test PDF (windows\\tests\\fixtures\\locked-1234.pdf) isn't next to this copy";
            var folder = Folder(dir, "unlock-check");
            var locked = Path.Combine(folder, "locked-1234.pdf");
            File.Copy(fixture, locked);
            var plain = MakePdf(Path.Combine(folder, "Plain.pdf"), SolidPage(Color.Gray, 595, 842));
            var lockedHash = Hash(locked);
            var ct = CancellationToken.None;

            // The right password: a copy that opens with no password, the same two A4 pages.
            var r = await Runner.Run(Catalog.PdfUnlock, new[] { locked }, new Options { Password = "1234" }, null, ct);
            Expect(r.Failures.Count == 0, string.Join("; ", r.Failures));
            var unlocked = r.Outputs.Single();
            Expect(Path.GetFileName(unlocked) == "locked-1234 Unlocked.pdf" && Path.GetDirectoryName(unlocked) == folder, "named <name> Unlocked.pdf, beside the original, got " + unlocked);
            Expect(r.Notes.Contains(PdfRewrite.PicturesNote), "the note that text can't be selected");
            var pages = await PagesOf(unlocked); // PagesOf opens it with no password
            Expect(pages.Count == 2 && pages.All(p => Math.Abs(p.Width - 595) <= 1 && Math.Abs(p.Height - 842) <= 1), "two A4 pages, got " + string.Join(", ", pages.Select(p => p.Width.ToString("0") + "×" + p.Height.ToString("0"))));
            Expect(Encoding.GetEncoding("iso-8859-1").GetString(File.ReadAllBytes(unlocked)).IndexOf("/Encrypt", StringComparison.Ordinal) < 0, "no encryption is left in the copy");
            Expect(Hash(locked) == lockedHash, "the locked original is byte for byte what it was");

            // A wrong password: the plain message (it never repeats the password), and nothing is made.
            int files = Directory.GetFiles(folder).Length;
            const string typed = "zq-secret-7";
            var wrong = await Runner.Run(Catalog.PdfUnlock, new[] { locked }, new Options { Password = typed }, null, ct);
            Expect(wrong.Outputs.Count == 0 && wrong.Failures.Count == 1 && wrong.Failures[0] == "Wrong password for locked-1234.pdf.", "the Wrong password message, got " + string.Join("; ", wrong.Failures));
            Expect(!string.Join(" ", wrong.Failures.Concat(wrong.Notes)).Contains(typed), "the message never repeats the password");
            Expect(Directory.GetFiles(folder).Length == files, "no file is made");

            // A PDF with no password: it says so, and nothing is made.
            var none = await Runner.Run(Catalog.PdfUnlock, new[] { plain }, new Options { Password = "1234" }, null, ct);
            Expect(none.Outputs.Count == 0 && none.Failures.Count == 1 && none.Failures[0] == "Plain.pdf has no password.", "the has-no-password message, got " + string.Join("; ", none.Failures));
            Expect(Directory.GetFiles(folder).Length == files, "no file is made");

            // Several PDFs, one password tried on each, each result reported.
            var both = await Runner.Run(Catalog.PdfUnlock, new[] { plain, locked }, new Options { Password = "1234" }, null, ct);
            Expect(both.Outputs.Count == 1 && both.Failures.Count == 1 && both.Failures[0] == "Plain.pdf has no password.", "one unlocked, one reported, got " + both.Outputs.Count + " and " + string.Join("; ", both.Failures));
            Expect(Directory.GetFiles(folder).Length == files + 1, "one new file");

            // What the password window asks before it closes.
            var asked = await PdfRewrite.CheckPassword(new[] { locked }, typed);
            Expect(asked == "Wrong password for locked-1234.pdf.", "the window is told what to say, got " + asked);
            Expect(await PdfRewrite.CheckPassword(new[] { locked }, "1234") == null, "the right password goes ahead");
            return "1234 gives a 2-page A4 PDF with no password; a wrong password and a PDF with no password each say so and make nothing; several PDFs each get their answer";
        }

        /// <summary>Two big lines with an email address and a phone number on a white picture.</summary>
        static void ContactPicture(string path)
        {
            using (var bmp = new Bitmap(1500, 420))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Segoe UI", 64))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString("Email: test@example.com", f, Brushes.Black, 40, 30);
                g.DrawString("Phone: +91 98765 43210", f, Brushes.Black, 40, 215);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// <summary>The kinds of chunks a PNG file holds, in order.</summary>
        static List<string> PngChunks(string path)
        {
            var b = File.ReadAllBytes(path);
            var types = new List<string>();
            for (int p = 8; p + 8 <= b.Length;)
            {
                int length = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
                types.Add(Encoding.ASCII.GetString(b, p + 4, 4));
                p += 12 + length;
            }
            return types;
        }

        static async Task<string> CheckRedact(string dir)
        {
            if (!Vision.OcrAvailable) return "SKIP no text-reading language is installed on this PC, so nothing can be found or read back";
            var folder = Folder(dir, "redact-check");
            var pic = Path.Combine(folder, "Contact.png");
            ContactPicture(pic);
            var hash = Hash(pic);
            var report = new List<string>();
            using (var look = await RedactOps.Look(pic))
            {
                Expect(look.CanRead, "this PC's text reader opens the picture");
                var email = look.Text.FirstOrDefault(f => f.Label == "Email");
                var phone = look.Text.FirstOrDefault(f => f.Label == "Phone");
                Expect(email != null && phone != null, "the email and the phone number are both found, got " + string.Join(", ", look.Text.Select(f => f.Label + " " + f.Rect)));
                Expect(email.Rect.Y + email.Rect.Height / 2 < 210 && phone.Rect.Y + phone.Rect.Height / 2 > 210, "each box sits on its own line (email " + email.Rect + ", phone " + phone.Rect + ")");
                report.Add("found " + string.Join(" and ", look.Text.Select(f => f.Label)));
                var boxes = look.Text.Select(f => f.Rect).ToList();
                foreach (var style in new[] { RedactStyle.Bar, RedactStyle.Pixelate, RedactStyle.Blur })
                {
                    var r = await Runner.Run(Catalog.Redact, new[] { pic }, new Options { RedactBoxes = boxes, RedactStyle = style }, null, CancellationToken.None);
                    Expect(r.Failures.Count == 0, style.Title() + ": " + string.Join("; ", r.Failures));
                    var outPath = r.Outputs.Single();
                    Expect(Path.GetExtension(outPath) == ".png" && Path.GetFileName(outPath).StartsWith("Contact Redacted", StringComparison.Ordinal), style.Title() + ": named <name> Redacted.<same type>, got " + Path.GetFileName(outPath));
                    var chunks = PngChunks(outPath);
                    Expect(!chunks.Any(t => t == "tEXt" || t == "zTXt" || t == "iTXt" || t == "eXIf"), style.Title() + ": no hidden info in the file, its chunks are " + string.Join(" ", chunks));
                    using (var back = await Images.Load(outPath))
                    using (var original = await Images.Load(pic))
                    {
                        Expect(back.Width == look.Width && back.Height == look.Height, style.Title() + ": the same size");
                        var lines = await Vision.ReadLines(back);
                        var raw = string.Join(" ", lines.Select(l => l.Text));
                        Expect(!raw.Contains("example") && !raw.Contains("98765") && !raw.Contains("43210") && !raw.Contains("test@"), style.Title() + ": the text can still be read: \"" + raw + "\"");
                        var left = Redaction.FindText(lines);
                        Expect(left.Count == 0, style.Title() + ": the finder still finds " + string.Join(", ", left.Select(f => f.Label)));
                        var now = Images.Bgra(back);
                        var was = Images.Bgra(original);
                        long inside = 0, bar = 0, changed = 0;
                        foreach (var box in boxes)
                        {
                            Expect(Redaction.Clip(box, back.Width, back.Height, out int x, out int y, out int w, out int h), "the box lies on the picture");
                            for (int yy = y; yy < y + h; yy++)
                                for (int xx = x; xx < x + w; xx++)
                                {
                                    int i = (yy * back.Width + xx) * 4;
                                    inside++;
                                    if (now[i] == Redaction.BarBlue && now[i + 1] == Redaction.BarGreen && now[i + 2] == Redaction.BarRed && now[i + 3] == 255) bar++;
                                    if (Math.Abs(now[i] - was[i]) + Math.Abs(now[i + 1] - was[i + 1]) + Math.Abs(now[i + 2] - was[i + 2]) > 24) changed++;
                                }
                        }
                        if (style == RedactStyle.Bar) Expect(bar == inside, "Black bar: every pixel inside a box is the bar colour, " + bar + " of " + inside + " are");
                        else Expect(changed * 100 > inside * 2 && bar == 0, style.Title() + ": the pixels inside the boxes were really replaced, " + changed + " of " + inside + " changed");
                    }
                    report.Add(style.Title() + " ok");
                }
            }
            Expect(Hash(pic) == hash, "the original picture is byte for byte what it was");
            return string.Join(", ", report) + "; the original is unchanged";
        }

        /// <summary>Shows a window far outside every screen without taking the focus (so it paints and reacts) and lays it out.</summary>
        static void OpenOffScreen(WinForms.Form form)
        {
            form.StartPosition = WinForms.FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            var handle = form.Handle;
            Native.ShowWindow(handle, Native.SW_SHOWNOACTIVATE);
            WinForms.Application.DoEvents();
            form.PerformLayout();
        }

        /// <summary>Lets the window's own message loop run until the condition holds.</summary>
        static void Pump(Func<bool> done, int seconds = 60)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!done() && clock.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                WinForms.Application.DoEvents();
                Thread.Sleep(15);
            }
            Expect(done(), "finished within " + seconds + " seconds");
        }

        /// <summary>What is wrong with how a small window is laid out (an empty list means nothing): text that doesn't fit its box,
        /// controls that overlap each other, or controls that stick out of the window. Call it while the window is shown.</summary>
        static List<string> WindowProblems(WinForms.Form form)
        {
            var bad = new List<string>();
            var leaves = Descendants(form).Where(c => c.Visible && (c is WinForms.Label || c is WinForms.ButtonBase || c is WinForms.TextBox)).ToList();
            Rectangle Where(WinForms.Control c) => form.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
            string Name(WinForms.Control c) => c.GetType().Name + " \"" + (c.Text.Length > 24 ? c.Text.Substring(0, 24) : c.Text) + "\"";
            foreach (var c in leaves)
            {
                var r = Where(c);
                if (r.Left < -1 || r.Top < -1 || r.Right > form.ClientSize.Width + 1 || r.Bottom > form.ClientSize.Height + 1) bad.Add(Name(c) + " sticks out of the window");
                if (c is WinForms.Label l && l.Text.Length > 0)
                {
                    // Ask the label itself how much room its words need at the width it has: it measures them the way it draws them.
                    var need = l.GetPreferredSize(new Size(l.Width, 0));
                    if (need.Height > l.Height + 1 || need.Width > l.Width + 1)
                        bad.Add(Name(c) + " is cut off: it needs " + need.Width + " x " + need.Height + " px and has " + l.Width + " x " + l.Height);
                }
                else if (c is WinForms.ButtonBase b && b.Text.Length > 0)
                {
                    var need = WinForms.TextRenderer.MeasureText(b.Text, b.Font);
                    if (need.Width > b.Width - (b is WinForms.Button ? 8 : 14)) bad.Add(Name(c) + " is cut off: its text needs " + need.Width + " px and it is " + b.Width + " wide");
                }
            }
            for (int i = 0; i < leaves.Count; i++)
                for (int j = i + 1; j < leaves.Count; j++)
                    if (Where(leaves[i]).IntersectsWith(Where(leaves[j]))) bad.Add(Name(leaves[i]) + " overlaps " + Name(leaves[j]));
            return bad;
        }

        /// <summary>A clean answer from <see cref="WindowProblems"/> only means something if it speaks up when it should: a label whose
        /// box is too small for its words, a control that sticks out of the window, and two controls on top of each other.
        /// It must also stay quiet for a window that is fine.</summary>
        static Task<string> CheckWindowChecker()
        {
            return OnSta(() =>
            {
                using (var fine = new WinForms.Form { ClientSize = new Size(300, 130) })
                {
                    fine.Controls.Add(new WinForms.Label { Text = "Type the current password", AutoSize = true, UseMnemonic = false, Location = new Point(10, 10) });
                    fine.Controls.Add(new WinForms.TextBox { Location = new Point(10, 40), Width = 200 });
                    fine.Controls.Add(new WinForms.Button { Text = "Unlock", Location = new Point(10, 80), Size = new Size(90, 32) });
                    OpenOffScreen(fine);
                    var quiet = WindowProblems(fine);
                    Expect(quiet.Count == 0, "a window that is fine has no problems, got " + string.Join("; ", quiet));
                }
                using (var bad = new WinForms.Form { ClientSize = new Size(300, 130) })
                {
                    bad.Controls.Add(new WinForms.Label { Text = "This sentence is much too long for a box that is only this small", AutoSize = false, UseMnemonic = false, Location = new Point(10, 10), Size = new Size(100, 16) });
                    bad.Controls.Add(new WinForms.TextBox { Location = new Point(10, 60), Width = 120 });
                    bad.Controls.Add(new WinForms.TextBox { Location = new Point(60, 62), Width = 120 });
                    bad.Controls.Add(new WinForms.Button { Text = "Unlock", Location = new Point(250, 90), Size = new Size(90, 32) });
                    bad.Controls.Add(new WinForms.Button { Text = "A caption far too long for this little button", Location = new Point(10, 95), Size = new Size(70, 28) });
                    OpenOffScreen(bad);
                    var found = WindowProblems(bad);
                    Expect(found.Any(p => p.StartsWith("Label") && p.Contains("is cut off")), "it notices a label that is too small for its words, got " + string.Join("; ", found));
                    Expect(found.Any(p => p.StartsWith("Button \"Unlock\"") && p.Contains("sticks out of the window")), "it notices a button that sticks out of the window, got " + string.Join("; ", found));
                    Expect(found.Any(p => p.StartsWith("TextBox") && p.Contains("overlaps")), "it notices two boxes on top of each other, got " + string.Join("; ", found));
                    Expect(found.Any(p => p.StartsWith("Button \"A caption") && p.Contains("is cut off")), "it notices a button whose caption is cut off, got " + string.Join("; ", found));
                }
                return "it stays quiet for a window that is fine and notices a cut-off label, a cut-off button caption, a control sticking out and two controls overlapping";
            });
        }

        static Task<string> CheckRedactWindow(string dir)
        {
            return OnSta(() =>
            {
                var folder = Folder(dir, "redact-window-check");
                var pic = Path.Combine(folder, "Contact.png");
                ContactPicture(pic);
                var ini = Path.Combine(folder, "settings.ini"); // its own file: the real settings are never touched
                var settings = new DragShift.Core.Settings(ini);
                var did = new List<string>();
                var problems = new List<string>();

                if (Vision.OcrAvailable)
                {
                    IList<Box> sent = null;
                    RedactStyle sentStyle = RedactStyle.Bar;
                    using (var form = new RedactForm(pic, settings))
                    {
                        form.Chosen += (boxes, style) => { sent = boxes; sentStyle = style; };
                        OpenOffScreen(form);
                        Expect(form.Note.Text == RedactForm.Looking && !form.SaveButton.Enabled, "while it looks, it says so and Save Redacted waits");
                        form.Begin();
                        Pump(() => form.Ready.IsCompleted);
                        Expect(form.Labels.SequenceEqual(new[] { "Email", "Phone" }), "the email and the phone number are listed, got " + string.Join(", ", form.Labels));
                        Expect(form.SaveButton.Enabled && !form.Note.Visible, "Save Redacted is ready and there is no message");
                        Expect(form.FacesBox.Text == "Find faces too" && form.FacesBox.Enabled == Vision.FacesAvailable && form.FacesBox.Checked == Vision.FacesAvailable, "Find faces too is ticked when this PC can look for faces");
                        Expect(form.StyleChoices.Select(b => b.Text).SequenceEqual(new[] { "Black bar", "Pixelate", "Blur" }) && form.Style == RedactStyle.Bar, "the styles are Black bar, Pixelate, Blur, and the black bar is first");
                        Expect(form.BoxesOn.Count == 2, "both boxes start on");

                        var view = form.View;
                        Point Centre(Box b) { var r = view.ToView(b); return new Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2)); }
                        Point At(double x, double y) { var r = view.ToView(new Box(x, y, 0, 0)); return new Point((int)Math.Round(r.X), (int)Math.Round(r.Y)); }
                        var emailBox = form.BoxesOn[0];
                        var phoneBox = form.BoxesOn[1];
                        view.Press(Centre(emailBox)); view.Release(Centre(emailBox));
                        Expect(form.BoxesOn.Count == 1, "clicking a box turns it off");
                        view.Press(Centre(emailBox)); view.Release(Centre(emailBox));
                        Expect(form.BoxesOn.Count == 2, "clicking it again turns it on");
                        did.Add("a click turns a box off and on");

                        // Drag on the picture: a box of your own. A drag of a few pixels is not a box.
                        var from = At(60, 330);
                        var to = At(400, 395);
                        view.Press(from); view.Drag(to); view.Release(to);
                        Expect(form.Labels.Count == 3 && form.Labels[2] == "Your box" && form.BoxesOn.Count == 3, "dragging adds a box called Your box");
                        var mine = form.BoxesOn[2];
                        Expect(Math.Abs(mine.X - 60) < 6 && Math.Abs(mine.Y - 330) < 6 && Math.Abs(mine.Width - 340) < 10 && Math.Abs(mine.Height - 65) < 10, "the new box is where it was dragged, in the picture's own pixels, got " + mine);
                        var small = At(700, 340);
                        view.Press(small); view.Drag(new Point(small.X + 5, small.Y + 5)); view.Release(new Point(small.X + 5, small.Y + 5));
                        Expect(form.Labels.Count == 3, "a drag of a few pixels is not a box");
                        did.Add("a drag adds a box of your own");

                        // Phone off, style Blur: the picture shows an on box, an off box and your own.
                        view.Press(Centre(phoneBox)); view.Release(Centre(phoneBox));
                        Expect(form.BoxesOn.Count == 2, "the phone box is off");
                        form.StyleChoices[2].Checked = true;
                        Expect(form.Style == RedactStyle.Blur && view.Style == RedactStyle.Blur, "Blur is chosen");
                        Picture(form, Path.Combine(dir, "redact-window.png"), () => problems.AddRange(WindowProblems(form).Select(p => "Redact: " + p)));
                        Expect(form.Note.Parent != null, "the window still has its parts");

                        form.SaveNow();
                        Expect(sent != null && sent.Count == 2 && sentStyle == RedactStyle.Blur, "Save Redacted hands over the two boxes that are on and the style, got " + (sent == null ? "nothing" : sent.Count + " boxes"));
                        Expect(form.IsDisposed, "the window closes");
                        Expect(File.ReadAllText(ini).Contains("redact.style = blur"), "the style is remembered in the settings, in the Mac's words");
                        did.Add("Save hands over what is on, and the style is remembered");
                    }
                    using (var next = new RedactForm(pic, settings))
                        Expect(next.StyleChoices[2].Checked && next.Style == RedactStyle.Blur, "the style from last time is already chosen");
                }

                // No text reader: the window still opens, says exactly this, and lets you draw.
                using (var form = new RedactForm(pic, settings, async (path, opened) =>
                {
                    var scan = await RedactOps.Look(path, opened);
                    scan.CanRead = false;
                    scan.Text = new List<Finding>();
                    return scan;
                }))
                {
                    OpenOffScreen(form);
                    form.Begin();
                    Pump(() => form.Ready.IsCompleted);
                    Expect(form.Note.Visible && form.Note.Text.StartsWith("Windows couldn't read the text here. Draw boxes over what you want hidden.", StringComparison.Ordinal), "the no-reader sentence, got \"" + form.Note.Text + "\"");
                    Expect(!form.SaveButton.Enabled, "nothing to save before a box is drawn");
                    var view = form.View;
                    var a = view.ToView(new Box(60, 40, 0, 0));
                    var b = view.ToView(new Box(500, 130, 0, 0));
                    view.Press(new Point((int)a.X, (int)a.Y)); view.Drag(new Point((int)b.X, (int)b.Y)); view.Release(new Point((int)b.X, (int)b.Y));
                    Expect(form.SaveButton.Enabled && form.Labels.Count == 1, "a box drawn by hand can be saved");
                    Picture(form, Path.Combine(dir, "redact-window-no-reader.png"), () => problems.AddRange(WindowProblems(form).Select(p => "Redact without a text reader: " + p)));
                    did.Add("no text reader: the sentence, and drawing still works");
                }

                // A file that isn't a picture: the window says so and nothing can be saved.
                var broken = Path.Combine(folder, "Broken.png");
                File.WriteAllText(broken, "this is not a picture");
                using (var form = new RedactForm(broken, settings))
                {
                    OpenOffScreen(form);
                    form.Begin();
                    Pump(() => form.Ready.IsCompleted);
                    Expect(form.Note.Visible && form.Note.Text.Contains("isn't a picture") && !form.SaveButton.Enabled, "a file that isn't a picture is said so, got \"" + form.Note.Text + "\"");
                    did.Add("a file that isn't a picture is said so");
                }
                Expect(problems.Count == 0, "the window is laid out properly: " + string.Join("; ", problems));
                return string.Join("; ", did) + "; pictures saved (redact-window.png, redact-window-no-reader.png), nothing cut off or overlapping" + (Vision.OcrAvailable ? "" : " (the found-and-listed half was skipped: no text reader on this PC)");
            });
        }

        static Task<string> CheckPasswordWindow(string dir, string fixture)
        {
            return OnSta(() =>
            {
                if (fixture == null) return "SKIP the locked test PDF (windows\\tests\\fixtures\\locked-1234.pdf) isn't next to this copy";
                var folder = Folder(dir, "password-window-check");
                var locked = Path.Combine(folder, "locked-1234.pdf");
                File.Copy(fixture, locked);
                var files = new[] { locked };
                var problems = new List<string>();
                string heard = null;
                int heardCount = 0;
                const string typed = "zq-secret-7";
                using (var form = new PasswordForm(files, pw => PdfRewrite.CheckPassword(files, pw)))
                {
                    form.Chosen += pw => { heard = pw; heardCount++; };
                    OpenOffScreen(form);
                    Expect(!form.UnlockButton.Enabled, "Unlock waits for a password");
                    Expect(form.Box.UseSystemPasswordChar, "the box is masked");
                    Picture(form, Path.Combine(dir, "password-window.png"), () => problems.AddRange(WindowProblems(form).Select(p => "Unlock: " + p)));

                    form.Box.Text = typed;
                    Expect(form.UnlockButton.Enabled, "Unlock is ready once something is typed");
                    var press = form.Press();
                    Pump(() => press.IsCompleted);
                    Expect(!form.IsDisposed && heardCount == 0, "a wrong password keeps the window open");
                    Expect(form.Problem.Text == "Wrong password for locked-1234.pdf.", "it says so, got \"" + form.Problem.Text + "\"");
                    Expect(!form.Problem.Text.Contains(typed), "the message never repeats the password");
                    Expect(form.UnlockButton.Enabled && form.UnlockButton.Text == "Unlock", "the person can try again");
                    Picture(form, Path.Combine(dir, "password-window-wrong.png"), () => problems.AddRange(WindowProblems(form).Select(p => "Unlock, wrong password: " + p)));

                    form.Box.Text = "1234";
                    Expect(form.Problem.Text == "", "typing again clears the message");
                    press = form.Press();
                    Pump(() => press.IsCompleted);
                    Expect(form.IsDisposed && heard == "1234" && heardCount == 1, "the right password closes the window and is handed over once");
                }
                // A long name and several PDFs.
                var longName = Path.Combine(folder, "A statement with a very long name that goes on and on and on for the whole year 2025.pdf");
                File.Copy(fixture, longName);
                using (var form = new PasswordForm(new[] { longName }))
                {
                    OpenOffScreen(form);
                    Picture(form, Path.Combine(dir, "password-window-long-name.png"), () => problems.AddRange(WindowProblems(form).Select(p => "Unlock, long name: " + p)));
                }
                using (var form = new PasswordForm(new[] { locked, longName }))
                {
                    OpenOffScreen(form);
                    Expect(Descendants(form).OfType<WinForms.Label>().Any(l => l.Text == "2 PDFs"), "several PDFs are shown as a count");
                }
                Expect(problems.Count == 0, "the window is laid out properly: " + string.Join("; ", problems));
                return "a wrong password keeps the window open and says so without repeating it; the right one closes it and is handed over once; pictures saved (password-window.png, password-window-wrong.png, password-window-long-name.png), nothing cut off or overlapping";
            });
        }

        static Task<string> CheckRingDismissal()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // Exercise the real mouse handler on an STA without showing a window or installing hooks.
                    using (var ring = new RingForm())
                    {
                        var handle = ring.Handle;
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        var origin = ring.PointToScreen(Point.Empty);
                        typeof(RingForm).GetField("ringRect", flags).SetValue(ring,
                            new Rectangle(origin.X, origin.Y, (int)Geometry.Size, (int)Geometry.Size));
                        ring.Model.Pages.Add(new RingPage("test", "Test", new[] { Catalog.Compress }));
                        int performed = 0;
                        ring.Perform += (slice, files, temporary) => performed++;
                        var mouseDown = typeof(RingForm).GetMethod("OnMouseDown", flags);
                        foreach (var mode in new[] { RingModel.RingMode.Drag, RingModel.RingMode.Click, RingModel.RingMode.Pinned })
                            foreach (var button in new[] { System.Windows.Forms.MouseButtons.Right, System.Windows.Forms.MouseButtons.Left })
                            {
                                ring.Model.Mode = mode;
                                ring.Model.Armed = Catalog.Compress;
                                ring.Model.Hovered = 0;
                                ring.Model.HoveredTab = 0;
                                mouseDown.Invoke(ring, new object[] { new System.Windows.Forms.MouseEventArgs(button, 1,
                                    (int)Geometry.CenterX, (int)Geometry.CenterY, 0) });
                                bool cancel = mode != RingModel.RingMode.Drag && button == System.Windows.Forms.MouseButtons.Left;
                                Expect((ring.Model.Armed == null) == cancel && (ring.Model.Hovered == null) == cancel &&
                                       (ring.Model.HoveredTab == null) == cancel, mode + " / " + button + " centre dismissal");
                                Expect(performed == 0, "cancelling never runs a file action");
                                Expect(!ring.Visible, "the regression window is never shown");
                            }
                    }
                }
                catch (Exception e) { failure = e; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
            return Task.FromResult("click and pinned modes; drag and right-click unchanged; hidden STA window");
        }

        /// <summary>The real mouse-watcher logic fed made-up mouse events: no hook is installed and nothing on this PC is touched.</summary>
        sealed class MouseProbe
        {
            public readonly DragTracker Tracker;
            public readonly List<string> Notes = new List<string>();
            public int Triggers, ClosingClicks;
            static readonly Point Here = new Point(160, 140);

            public MouseProbe(MouseTrigger buttons, bool textCursor = false, bool leftReallyDown = true, bool fullScreen = false)
            {
                Tracker = new DragTracker
                {
                    Buttons = buttons, TextCursor = () => textCursor, LeftHeld = () => leftReallyDown, FullScreen = () => fullScreen,
                    Note = text => Notes.Add(text), // never the real log
                };
                Tracker.Triggered += t => { if (t == DragTracker.Trigger.Mouse) Triggers++; };
                Tracker.Pressed += (p, left) => { if (!left) ClosingClicks++; };
            }

            /// <summary>Press the left button and move far enough to count as a drag.</summary>
            public MouseProbe Grab()
            {
                Tracker.Handle(Native.WM_LBUTTONDOWN, new Point(100, 100), 0);
                Tracker.Handle(Native.WM_MOUSEMOVE, Here, 0);
                return this;
            }

            public void LetGo() => Tracker.Handle(Native.WM_LBUTTONUP, Here, 0);
            public bool Press(Button b) => Tracker.Handle(b.Down, Here, b.Data);
            public bool Release(Button b) => Tracker.Handle(b.Up, Here, b.Data);
        }

        sealed class Button
        {
            public int Down, Up;
            public uint Data;
            public MouseTrigger Which;
            public string Name;
        }

        static readonly Button[] MouseButtonsToTry =
        {
            new Button { Down = Native.WM_RBUTTONDOWN, Up = Native.WM_RBUTTONUP, Which = MouseTrigger.Right, Name = "right button" },
            new Button { Down = Native.WM_MBUTTONDOWN, Up = Native.WM_MBUTTONUP, Which = MouseTrigger.Middle, Name = "wheel click" },
            new Button { Down = Native.WM_XBUTTONDOWN, Up = Native.WM_XBUTTONUP, Data = 1u << 16, Which = MouseTrigger.Back, Name = "side button (back)" },
            new Button { Down = Native.WM_XBUTTONDOWN, Up = Native.WM_XBUTTONUP, Data = 2u << 16, Which = MouseTrigger.Forward, Name = "side button (forward)" },
        };

        static Task<string> CheckMouseButtons()
        {
            // 1. Ticked buttons: swallowed while a drag holds something, open the ring once, and the release is swallowed too.
            foreach (var b in MouseButtonsToTry)
            {
                var m = new MouseProbe(MouseTrigger.All).Grab();
                Expect(m.Press(b), b.Name + ": swallowed while dragging, so the app you drag from never sees it");
                Expect(m.Triggers == 1, b.Name + ": opens the ring once");
                Expect(m.Release(b), b.Name + ": its release is swallowed too (no half a click)");
                Expect(!m.Release(b), b.Name + ": a second release is not");
                m.LetGo();
            }
            // 2. Only the ticked ones: with just the right button ticked, the others reach the app untouched.
            foreach (var b in MouseButtonsToTry)
            {
                bool ticked = b.Which == MouseTrigger.Right;
                var m = new MouseProbe(MouseTrigger.Right).Grab();
                Expect(m.Press(b) == ticked, b.Name + (ticked ? ": taken" : ": left alone when not ticked"));
                Expect(m.Triggers == (ticked ? 1 : 0), b.Name + ": ring only for a ticked button");
                Expect(m.Release(b) == ticked, b.Name + ": release follows the press");
            }
            // 3. Nothing ticked, or switched off or paused (which gives None): no button is ever touched.
            foreach (var b in MouseButtonsToTry)
            {
                var m = new MouseProbe(MouseTrigger.None).Grab();
                Expect(!m.Press(b) && !m.Release(b) && m.Triggers == 0, b.Name + ": untouched when nothing is ticked");
            }
            // 4. Not dragging: a plain click is never touched, and the click that closes an open ring is still reported.
            var plain = new MouseProbe(MouseTrigger.All);
            Expect(!plain.Press(MouseButtonsToTry[0]) && !plain.Release(MouseButtonsToTry[0]), "a plain right click passes through");
            Expect(plain.Triggers == 0 && plain.ClosingClicks == 1, "no ring, but the closing click is reported");
            // 5. A game or full-screen video in front: its buttons are its own.
            var game = new MouseProbe(MouseTrigger.All, fullScreen: true).Grab();
            Expect(!game.Press(MouseButtonsToTry[0]) && game.Triggers == 0, "a full-screen app keeps its right button");
            Expect(game.Notes.Count == 1 && !game.Press(MouseButtonsToTry[0]) && game.Notes.Count == 1, "and it is noted once, not on every click");
            // 6. The left button went up without being seen: that is no drag, and the right button keeps working.
            var stale = new MouseProbe(MouseTrigger.All, leftReallyDown: false).Grab();
            Expect(!stale.Press(MouseButtonsToTry[0]) && stale.Triggers == 0 && !stale.Tracker.Down, "a stuck drag is dropped, the click passes");
            // 7. Selecting text (the I-beam) is never a file drag.
            var text = new MouseProbe(MouseTrigger.All, textCursor: true).Grab();
            Expect(!text.Press(MouseButtonsToTry[0]) && text.Triggers == 0, "no ring while selecting text");
            // 8. Two buttons held together: both releases are swallowed.
            var both = new MouseProbe(MouseTrigger.All).Grab();
            Expect(both.Press(MouseButtonsToTry[0]) && both.Press(MouseButtonsToTry[2]) && both.Triggers == 2, "two buttons, two triggers");
            Expect(both.Release(MouseButtonsToTry[0]) && both.Release(MouseButtonsToTry[2]), "both releases swallowed");
            // 9. A release that never came must not eat a later, ordinary right click.
            var lost = new MouseProbe(MouseTrigger.All).Grab();
            Expect(lost.Press(MouseButtonsToTry[0]), "swallowed during the drag");
            lost.LetGo(); // …and its release is never seen
            Expect(!lost.Press(MouseButtonsToTry[0]) && !lost.Release(MouseButtonsToTry[0]), "the next ordinary right click works in full");
            return Task.FromResult("4 buttons × (ticked, not ticked, none); plain click, full-screen, stuck drag, text, two buttons, lost release");
        }

        /// <summary>How many Recycle Bin items came from this folder (read-only, through Windows' own Shell.Application).</summary>
        static int RecycleBinCount(string originalFolder)
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return 0;
            const System.Reflection.BindingFlags call = System.Reflection.BindingFlags.InvokeMethod, get = System.Reflection.BindingFlags.GetProperty;
            object shell = Activator.CreateInstance(shellType);
            object bin = shellType.InvokeMember("NameSpace", call, null, shell, new object[] { 10 });
            if (bin == null) return 0;
            object items = bin.GetType().InvokeMember("Items", call, null, bin, null);
            int count = (int)items.GetType().InvokeMember("Count", get, null, items, null);
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                object item = items.GetType().InvokeMember("Item", call, null, items, new object[] { i });
                var from = ((string)bin.GetType().InvokeMember("GetDetailsOf", call, null, bin, new object[] { item, 1 }) ?? "").Trim('‎', '‏', ' ');
                if (string.Equals(from, originalFolder, StringComparison.OrdinalIgnoreCase)) n++;
            }
            return n;
        }

        static Task<string> OnSta(Func<string> work)
        {
            string result = null;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { result = work(); }
                catch (Exception e) { failure = e; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
            return Task.FromResult(result);
        }

        static Task<string> CheckOpenSettings(string file)
        {
            return OnSta(() =>
            {
                // A receiver with a name of its own: the running DragShift can never get this test's messages.
                var title = "DragShift.Receiver.SelfTest." + Guid.NewGuid().ToString("N");
                using (var receiver = new Receiver(title))
                {
                    int settings = 0, handed = 0;
                    receiver.OpenSettings += () => settings++;
                    receiver.Files += list => handed += list.Count;
                    Expect(Receiver.SendOpenSettings(title), "the running copy is found");
                    Expect(settings == 1 && handed == 0, "a second start with no files opens Settings, once");
                    Expect(Receiver.SendToRunning(new[] { file }, title), "the running copy is found again");
                    Expect(settings == 1 && handed == 1, "a second start with a file still opens the ring, not Settings");
                    var target = Native.FindWindowEx((IntPtr)(-3), IntPtr.Zero, null, title);
                    var stranger = new Native.COPYDATASTRUCT { Data = (IntPtr)3, Bytes = 0, Ptr = IntPtr.Zero };
                    Native.SendMessage(target, Native.WM_COPYDATA, IntPtr.Zero, ref stranger);
                    var padded = new Native.COPYDATASTRUCT { Data = (IntPtr)2, Bytes = 6, Ptr = IntPtr.Zero }; // "open Settings" carries nothing
                    Native.SendMessage(target, Native.WM_COPYDATA, IntPtr.Zero, ref padded);
                    Expect(settings == 1 && handed == 1, "any other message is ignored");
                }
                Expect(!Receiver.SendOpenSettings("DragShift.Receiver.SelfTest.nobody-home"), "nobody running: false, no error");
                return "Settings once, files still reach the ring, strangers ignored";
            });
        }

        static Task<string> CheckShelfGear(string dir, string src)
        {
            return OnSta(() =>
            {
                var files = new List<string> { src };
                int ran = 0, opened = 0;
                Slice lastRun = null;
                using (var shelf = new ShelfForm { Side = "right", Note = _ => { } })
                {
                    var handle = shelf.Handle; // a window that is never shown
                    shelf.Perform += (slice, list) => { ran++; lastRun = slice; };
                    shelf.OpenSettings += () => opened++;
                    shelf.UseScreen(new Rectangle(0, 0, 1920, 1080), 1);

                    shelf.Favorites = f => new List<Slice> { Catalog.Compress, Catalog.StitchAll };
                    shelf.OpenFor(files, new Point(1900, 500));
                    Expect(shelf.TileCount == 2, "two favourites, then the gear");
                    for (int i = 0; i <= 2; i++)
                    {
                        var b = shelf.TileBounds(i);
                        Expect(shelf.TileAt(new Point(b.X + b.Width / 2, b.Y + b.Height / 2)) == i, "the middle of row " + i + " is row " + i);
                    }
                    Expect(shelf.TileBounds(2).Top >= shelf.TileBounds(1).Bottom, "the gear sits below the last favourite");
                    Expect(shelf.TileAt(new Point(shelf.Bounds.X + 10, shelf.Bounds.Y + 4)) == null, "the title above the tiles is not a row");
                    shelf.Choose(0, files);
                    Expect(ran == 1 && lastRun == Catalog.Compress && opened == 0, "dropping on a favourite runs it");
                    shelf.OpenFor(files, new Point(1900, 500));
                    shelf.Choose(2, files);
                    Expect(ran == 1 && opened == 1, "dropping on the gear opens Settings and runs nothing on the file");
                    shelf.OpenFor(files, new Point(1900, 500));
                    shelf.Choose(null, files);
                    Expect(ran == 1 && opened == 1, "dropping on the title does nothing");

                    shelf.Favorites = f => new List<Slice>();
                    shelf.OpenFor(files, new Point(1900, 500));
                    Expect(shelf.TileCount == 0, "no favourite fits this file");
                    var only = shelf.TileBounds(0);
                    Expect(shelf.TileAt(new Point(only.X + only.Width / 2, only.Y + only.Height / 2)) == 0, "the gear is still there to drop on");
                    shelf.Choose(0, files);
                    Expect(opened == 2 && ran == 1, "and it opens Settings");

                    // The global mouse hook can report button-up before OLE delivers the drop.
                    // Keep the real drop target available through that order, not just Choose() in isolation.
                    void ReleaseThenDrop(int row)
                    {
                        var b = shelf.TileBounds(row);
                        shelf.DragEnded();
                        Expect(shelf.IsOpen, "button release leaves the shelf available for the pending OLE drop");
                        var data = new WinForms.DataObject(WinForms.DataFormats.FileDrop, files.ToArray());
                        var drop = new WinForms.DragEventArgs(data, 0, b.X + b.Width / 2, b.Y + b.Height / 2,
                            WinForms.DragDropEffects.Copy, WinForms.DragDropEffects.Copy);
                        typeof(ShelfForm).GetMethod("OnDragDrop", System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic).Invoke(shelf, new object[] { drop });
                        WinForms.Application.DoEvents();
                        Expect(!shelf.IsOpen && !shelf.Visible, "the completed drop closes the shelf without showing the test window");
                    }
                    shelf.OpenFor(files, new Point(1900, 500));
                    ReleaseThenDrop(0);
                    Expect(opened == 3 && ran == 1, "a gear drop still opens Settings when the release hook arrives first");
                    shelf.Favorites = f => new List<Slice> { Catalog.Compress };
                    shelf.OpenFor(files, new Point(1900, 500));
                    ReleaseThenDrop(0);
                    Expect(opened == 3 && ran == 2 && lastRun == Catalog.Compress, "a favourite also runs once in that event order");
                    shelf.OpenFor(files, new Point(1900, 500));
                    shelf.DragEnded();
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (shelf.IsOpen && wait.ElapsedMilliseconds < 1500)
                    {
                        WinForms.Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Expect(!shelf.IsOpen && opened == 3 && ran == 2, "a cancelled drag closes after the grace period without an action");

                    // A short, scaled screen (1366 × 768 at 125%): fewer favourites so the gear stays on the screen.
                    shelf.UseScreen(new Rectangle(0, 0, 1366, 768), 1.25);
                    shelf.Favorites = f => Enumerable.Repeat(Catalog.Compress, 8).ToList();
                    shelf.OpenFor(files, new Point(1360, 700));
                    Expect(shelf.TileCount == 7, "eight favourites do not fit on a short screen; seven and the gear do, got " + shelf.TileCount);
                    Expect(shelf.Bounds.Top >= 0 && shelf.Bounds.Bottom <= 768, "the whole shelf is on the screen: " + shelf.Bounds);
                    Expect(shelf.TileBounds(7).Bottom <= 768, "the gear is on the screen");

                    // Pictures of it, in both looks, and the gear under the pointer.
                    shelf.UseScreen(new Rectangle(0, 0, 1920, 1080), 1.5);
                    shelf.Favorites = f => new List<Slice> { Catalog.Compress, Catalog.StitchAll, Catalog.Unzip };
                    shelf.OpenFor(files, new Point(1900, 500));
                    foreach (var dark in new[] { false, true })
                        using (var bmp = shelf.Snapshot(dark)) bmp.Save(Path.Combine(dir, "shelf-" + (dark ? "dark" : "light") + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    shelf.Highlight(3);
                    using (var bmp = shelf.Snapshot(false)) bmp.Save(Path.Combine(dir, "shelf-gear-hot.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                return "gear below the favourites; 2 pictures + 1 with the gear lit";
            });
        }

        static IEnumerable<WinForms.Control> Descendants(WinForms.Control c)
        {
            foreach (WinForms.Control child in c.Controls)
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        /// <summary>A window as a picture. Windows only paints the controls of a window that is shown, so it is shown far
        /// outside every screen and without taking the focus, drawn, and hidden again.</summary>
        static void Picture(WinForms.Form form, string path, Action whileShown = null)
        {
            form.StartPosition = WinForms.FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            var handle = form.Handle;
            Native.ShowWindow(handle, Native.SW_SHOWNOACTIVATE);
            WinForms.Application.DoEvents();
            form.PerformLayout();
            try
            {
                whileShown?.Invoke(); // measurements need the window shown: a hidden window reports every control as not visible
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            finally { Native.ShowWindow(handle, Native.SW_HIDE); }
        }

        static Task<string> CheckClipboardInputs(string dir)
        {
            return OnSta(() =>
            {
                var root = Path.Combine(dir, "clipboard-input-test");
                Directory.CreateDirectory(root);
                var original = Path.Combine(dir, "clipboard-original.png");
                var other = Path.Combine(root, "keep.txt");
                var stale = Path.Combine(root, "Clipboard Image 2.png");
                File.WriteAllText(original, "original");
                File.WriteAllText(other, "keep");
                File.WriteAllText(stale, "old import");
                using (var inbox = new ClipboardInbox(root, () => { }))
                using (var ring = new RingForm())
                {
                    Expect(!File.Exists(stale) && File.ReadAllText(other) == "keep", "startup removes only old import names");
                    IDisposable ringHold = null;
                    ring.InputsReleased += () => { ringHold?.Dispose(); ringHold = null; };
                    var first = inbox.Save(p => File.WriteAllText(p, "first"));
                    ring.SetInputs(new[] { first }, true);
                    ringHold = inbox.Hold(new[] { first });
                    inbox.CleanupUnheld();
                    Expect(File.Exists(first), "the open ring keeps its source");
                    var second = inbox.Save(p => File.WriteAllText(p, "second"));
                    ring.SetInputs(new[] { second }, true);
                    ringHold = inbox.Hold(new[] { second });
                    inbox.CleanupUnheld();
                    Expect(!File.Exists(first) && File.Exists(second), "replacing the ring removes its old import");
                    var work = inbox.Hold(new[] { second });
                    ring.HideRing();
                    inbox.CleanupUnheld();
                    Expect(File.Exists(second), "the action or editor keeps the source after the ring closes");
                    var output = Path.Combine(dir, "clipboard-kept-output.png");
                    Naming.Atomically(output, p => File.Copy(second, p));
                    work.Dispose();
                    inbox.CleanupUnheld();
                    Expect(!File.Exists(second) && File.ReadAllText(output) == "second", "finishing keeps the output and removes the import");
                    var cancelled = inbox.Save(p => File.WriteAllText(p, "cancelled"));
                    ring.SetInputs(new[] { cancelled }, true);
                    ringHold = inbox.Hold(new[] { cancelled });
                    ring.HideRing();
                    inbox.CleanupUnheld();
                    Expect(!File.Exists(cancelled), "cancelling the ring removes its import");
                    var locked = inbox.Save(p => File.WriteAllText(p, "locked"));
                    using (var file = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                        Expect(inbox.CleanupUnheld() == 1 && File.Exists(locked), "a file still in use waits for a safe retry");
                    Expect(inbox.CleanupUnheld() == 0 && !File.Exists(locked), "the retry removes the released file");
                    var last = inbox.Save(p => File.WriteAllText(p, "quit"));
                    using (inbox.Hold(new[] { last, original }))
                    {
                        inbox.Dispose();
                        Expect(!File.Exists(last), "quitting removes even a still-open ring import");
                    }
                }
                Expect(File.ReadAllText(original) == "original" && File.ReadAllText(other) == "keep", "originals and unrelated files are unchanged");
                return "ring replacement/cancel, action hand-off, locked-file retry, quit; originals and outputs kept";
            });
        }

        // ---- the About tab's cup ----

        /// <summary>A picture's pixels as bytes: blue, green, red, alpha for each, row after row.</summary>
        static byte[] Raw(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * bmp.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { bmp.UnlockBits(data); }
        }

        // The cup's colours as the drawing makes them. An antialiased edge is a blend of two colours and matches neither.
        static bool IsCoffee(byte[] px, int i) => Math.Abs(px[i + 2] - 89) <= 20 && Math.Abs(px[i + 1] - 45) <= 16 && Math.Abs(px[i] - 20) <= 16;
        static bool IsYellow(byte[] px, int i) => px[i + 2] >= 225 && px[i + 1] >= 150 && px[i + 1] <= 235 && px[i] <= 130 && px[i + 2] - px[i] >= 110;

        /// <summary>The cup alone on white, `t` of the way from sitting to spilled and `width` wide, in a picture the size of the room
        /// it needs (the Mac's frame for it). `centre` is the point the cup turns around.</summary>
        static Bitmap CupOnWhite(float t, float width, out PointF centre)
        {
            int w = (int)Math.Ceiling(width * CupPainter.RoomAcross), h = (int)Math.Ceiling(width * CupPainter.RoomDown);
            centre = new PointF(w / 2f, h / 2f);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                CupPainter.Draw(g, centre, width, t);
            }
            return bmp;
        }

        /// <summary>The cup's outline where it lies in a picture: `width` wide, turning around `centre`, tipped by `tilt` degrees.</summary>
        static GraphicsPath CupOutline(PointF centre, float width, float tilt)
        {
            var points = new PointF[CupArt.Outline.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new PointF(CupArt.Outline[2 * i], CupArt.Outline[2 * i + 1]);
            var path = new GraphicsPath();
            path.AddPolygon(points);
            using (var m = new Matrix())
            {
                m.Translate(centre.X, centre.Y);
                m.Scale(width / 1000f, width / 1000f);
                m.Rotate(tilt);
                path.Transform(m);
            }
            return path;
        }

        static PointF MiddleOfYellow(Bitmap bmp)
        {
            var px = Raw(bmp);
            double sx = 0, sy = 0;
            int n = 0;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (IsYellow(px, (y * bmp.Width + x) * 4)) { sx += x + 0.5; sy += y + 0.5; n++; }
            Expect(n > 0, "there is yellow in the picture");
            return new PointF((float)(sx / n), (float)(sy / n));
        }

        /// <summary>Where the coffee-coloured pixels of a picture of the cup are: outside the cup's outline, left of it, and how far left the coffee reaches.</summary>
        static void CoffeeBeyond(Bitmap bmp, GraphicsPath outline, out int outside, out int leftOf, out float farthestLeft)
        {
            var px = Raw(bmp);
            var bounds = outline.GetBounds();
            outside = leftOf = 0;
            farthestLeft = float.MaxValue;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (!IsCoffee(px, (y * bmp.Width + x) * 4)) continue;
                    if (!outline.IsVisible(x + 0.5f, y + 0.5f)) outside++;
                    if (x + 1 <= bounds.Left) leftOf++;
                    farthestLeft = Math.Min(farthestLeft, x);
                }
        }

        static bool Same(byte[] a, byte[] b) => a.Length == b.Length && a.SequenceEqual(b);

        static Task<string> CheckCup(string dir)
        {
            return OnSta(() =>
            {
                Expect(Tip.Line(0).Contains("0 files") && Tip.Line(214).Contains("214 files"), "the count is always in the line");
                var invariant = System.Globalization.CultureInfo.InvariantCulture;
                const float big = 216; // four times the real width (54), so every curve can be looked at

                // Six pictures on white, from sitting still to the full spill. The coffee must travel left and keep going.
                var reach = new List<float>();
                foreach (var t in new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f })
                    using (var bmp = CupOnWhite(t, big, out var centre))
                    using (var outline = CupOutline(centre, big, CupArt.Tilt * (1 - (1 - t) * (1 - t))))
                    {
                        bmp.Save(Path.Combine(dir, "cup-t" + t.ToString("0.0", invariant) + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        CoffeeBeyond(bmp, outline, out _, out _, out var farthest);
                        reach.Add(farthest);
                    }
                for (int i = 1; i < reach.Count; i++) Expect(reach[i] <= reach[i - 1] + 0.5f, "the coffee only moves one way as the cup is pulled: " + string.Join(", ", reach));
                Expect(reach[0] - reach[reach.Count - 1] > 40, "the coffee travels a long way left between sitting and spilled: " + string.Join(", ", reach));

                // At rest the coffee is all inside the cup; spilled, some of it is out past the cup's left side.
                int outsideAtRest, leftAtRest, outsideSpilled, leftSpilled;
                using (var bmp = CupOnWhite(0, big, out var centre)) using (var outline = CupOutline(centre, big, 0))
                    CoffeeBeyond(bmp, outline, out outsideAtRest, out leftAtRest, out _);
                using (var bmp = CupOnWhite(1, big, out var centre)) using (var outline = CupOutline(centre, big, CupArt.Tilt))
                    CoffeeBeyond(bmp, outline, out outsideSpilled, out leftSpilled, out _);
                Expect(outsideAtRest == 0, "at rest no coffee-coloured pixel lies outside the cup's outline, " + outsideAtRest + " do");
                Expect(leftSpilled >= 30, "spilled, coffee-coloured pixels lie left of the cup's outline, " + leftSpilled + " do");

                // Tipping and spilling happen around one point: the middle of the yellow stays put (less than a pixel at the real size).
                double shiftReal = 0, shiftBig = 0;
                foreach (var width in new[] { 54f, big })
                    using (var a = CupOnWhite(0, width, out _)) using (var b = CupOnWhite(1, width, out _))
                    {
                        PointF p = MiddleOfYellow(a), q = MiddleOfYellow(b);
                        double moved = Math.Sqrt(Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2));
                        Expect(moved < width / 54, "the middle of the yellow moves less than " + (width / 54).ToString("0.#", invariant) + " px at width " + width + ", it moved " + moved.ToString("0.00", invariant));
                        if (width == 54) shiftReal = moved; else shiftBig = moved;
                    }

                // Anything below 0 or above 1 is the nearest end, exactly.
                using (var low = CupOnWhite(-0.5f, 54, out _)) using (var zero = CupOnWhite(0, 54, out _))
                using (var high = CupOnWhite(1.7f, 54, out _)) using (var one = CupOnWhite(1, 54, out _))
                    Expect(Same(Raw(low), Raw(zero)) && Same(Raw(high), Raw(one)), "t below 0 draws the same as 0, and t above 1 the same as 1");

                // The drawing leaves the graphics as it found them.
                using (var bmp = new Bitmap(120, 100, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.None;
                    g.TranslateTransform(3, 4);
                    g.SetClip(new Rectangle(10, 10, 50, 50));
                    var matrix = g.Transform.Elements;
                    var clip = g.ClipBounds;
                    CupPainter.Draw(g, new PointF(60, 50), 54, 1);
                    Expect(g.SmoothingMode == SmoothingMode.None && g.Transform.Elements.SequenceEqual(matrix) && g.ClipBounds == clip,
                           "the cup's drawing puts the transform, the clip and the smoothing back");
                }

                // The cup in its area, as the About tab shows it: three pictures with the ring open, and one at rest.
                var size = CoffeeCup.Natural;
                int scale = 2;
                var window = SystemColors.Control;
                var looks = new[] { ("cup-closed", PointF.Empty, false), ("cup-ring-small", new PointF(-55, 25), true),
                                    ("cup-ring-medium", new PointF(0, 62), true), ("cup-ring-large", new PointF(55, 25), true) };
                foreach (var look in looks)
                    using (var bmp = new Bitmap(size.Width * scale, size.Height * scale, PixelFormat.Format32bppArgb))
                    using (var alone = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb))
                    using (var cup = new CoffeeCup { Size = new Size(bmp.Width, bmp.Height) })
                    {
                        var offset = new PointF(look.Item2.X * scale, look.Item2.Y * scale);
                        using (var g = Graphics.FromImage(bmp)) CoffeeCup.Draw(g, bmp.Width, offset, look.Item3, window, SystemColors.ControlText);
                        bmp.Save(Path.Combine(dir, look.Item1 + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        // The same cup drawn alone on a clear picture: wherever that is solid, the picture of the area shows exactly that.
                        var at = new PointF(cup.Saucer.X + offset.X, cup.Saucer.Y + offset.Y);
                        using (var g = Graphics.FromImage(alone)) { g.Clear(Color.Transparent); CupPainter.Draw(g, at, 54 * scale, CoffeeCup.Pulled(offset, scale)); }
                        byte[] shown = Raw(bmp), cupOnly = Raw(alone);
                        int solid = 0, wrong = 0;
                        for (int i = 0; i < cupOnly.Length; i += 4)
                            if (cupOnly[i + 3] == 255)
                            {
                                solid++;
                                // (Within 3 of 255 in any colour: a pixel built from several edges of the drawing is rounded differently over a window colour.)
                                if (Math.Abs(cupOnly[i] - shown[i]) > 3 || Math.Abs(cupOnly[i + 1] - shown[i + 1]) > 3 || Math.Abs(cupOnly[i + 2] - shown[i + 2]) > 3) wrong++;
                            }
                        Expect(solid > 2000 && wrong == 0, look.Item1 + ": the cup is drawn where it is held (" + solid + " solid pixels, " + wrong + " different)");
                        if (!look.Item3)
                        {
                            // Nothing but the cup and the faint saucer under it: no circle behind the cup, as the old picture had.
                            using (var outline = CupOutline(at, 54 * scale, 0))
                            using (var margin = new Pen(Color.Black, 6))
                            using (var nearCup = (GraphicsPath)outline.Clone())
                            {
                                nearCup.Widen(margin);
                                var saucer = new RectangleF(cup.Saucer.X - 23 * scale - 2, cup.Saucer.Y + 15 * scale - 2, 46 * scale + 4, 10 * scale + 4);
                                int stray = 0;
                                for (int y = 0; y < bmp.Height; y++)
                                    for (int x = 0; x < bmp.Width; x++)
                                    {
                                        int i = (y * bmp.Width + x) * 4;
                                        bool plain = shown[i] == window.B && shown[i + 1] == window.G && shown[i + 2] == window.R;
                                        if (!plain && !outline.IsVisible(x + 0.5f, y + 0.5f) && !nearCup.IsVisible(x + 0.5f, y + 0.5f) && !saucer.Contains(x + 0.5f, y + 0.5f)) stray++;
                                    }
                                Expect(stray == 0, look.Item1 + ": only the cup and its saucer are drawn, " + stray + " other pixels are");
                            }
                        }
                    }

                // Pulled as far as it goes in every direction it can go, the drawing never reaches the edge of its area (no clipping), at both sizes.
                int poses = 0;
                foreach (int sc in new[] { 1, 2 })
                    foreach (bool open in new[] { false, true })
                    {
                        var offsets = new List<PointF> { new PointF(-73.9f, -4), new PointF(73.9f, -4), new PointF(0, -4) };
                        for (int a = 0; a <= 180; a += 15) offsets.Add(new PointF((float)(74 * Math.Cos(a * Math.PI / 180)), (float)(74 * Math.Sin(a * Math.PI / 180))));
                        foreach (var o in offsets)
                            using (var bmp = new Bitmap(size.Width * sc, size.Height * sc, PixelFormat.Format32bppArgb))
                            {
                                using (var g = Graphics.FromImage(bmp)) CoffeeCup.Draw(g, bmp.Width, new PointF(o.X * sc, o.Y * sc), open, Color.White, Color.Black);
                                var px = Raw(bmp);
                                int touching = 0;
                                for (int y = 0; y < bmp.Height; y++)
                                    for (int x = 0; x < bmp.Width; x++)
                                    {
                                        if (x > 1 && y > 1 && x < bmp.Width - 2 && y < bmp.Height - 2) continue;
                                        int i = (y * bmp.Width + x) * 4;
                                        if (px[i] != 255 || px[i + 1] != 255 || px[i + 2] != 255) touching++;
                                    }
                                Expect(touching == 0, "cup pulled to " + o + (open ? " with the ring open" : "") + " at " + sc + "00%: " + touching + " pixels at the edge of its area");
                                poses++;
                            }
                    }

                // Held exactly under the pointer, kept inside the ring, and back on its saucer in about a quarter of a second when let go.
                var mouse = new Func<string, System.Reflection.MethodInfo>(name => typeof(WinForms.Control).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                long took = 0;
                using (var cup = new CoffeeCup())
                {
                    var made = cup.Handle; // a real window that is never shown, so it can take the mouse
                    float k = cup.Width / (float)size.Width;
                    var saucer = Point.Round(cup.Saucer);
                    void Mouse(string what, int dx, int dy) => mouse(what).Invoke(cup, new object[] { new WinForms.MouseEventArgs(WinForms.MouseButtons.Left, 1, saucer.X + dx, saucer.Y + dy, 0) });
                    Mouse("OnMouseDown", 40, 40);
                    Mouse("OnMouseMove", 50, 50);
                    Expect(cup.Held == PointF.Empty, "a press away from the cup's saucer takes nothing");
                    Mouse("OnMouseUp", 50, 50);
                    Mouse("OnMouseDown", 0, 0);
                    Mouse("OnMouseMove", -30, 10);
                    Expect(cup.Held == new PointF(-30, 10), "held, the cup is exactly under the pointer, no easing: " + cup.Held);
                    Mouse("OnMouseMove", -500, 0);
                    Expect(Math.Abs(cup.Held.X + 74 * k) < 0.01 && cup.Held.Y == 0 && CoffeeCup.Pulled(cup.Held, k) == 1, "pulled far, it stops at the edge of the ring: " + cup.Held);
                    Mouse("OnMouseMove", 0, -50);
                    Expect(cup.Held == new PointF(0, -4 * k), "it can't go above its saucer by more than 4 px: " + cup.Held);
                    Mouse("OnMouseMove", -20, 40);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Mouse("OnMouseUp", -20, 40);
                    if (!Native.Animations) Expect(cup.Held == PointF.Empty, "Windows is set to show no animations: the cup is back at once");
                    else
                    {
                        Expect(cup.Settling && cup.Held != PointF.Empty, "let go, the cup starts back");
                        while (cup.Settling && watch.ElapsedMilliseconds < 3000) { WinForms.Application.DoEvents(); Thread.Sleep(4); }
                        Expect(!cup.Settling && cup.Held == PointF.Empty, "the cup ends on its saucer");
                    }
                    took = watch.ElapsedMilliseconds;
                    Expect(!Native.Animations || (took >= 200 && took <= 900), "about a quarter of a second to get back, took " + took + " ms");
                }
                var away = new PointF(-50, 20);
                Expect(CoffeeCup.Back(away, 0) == away && CoffeeCup.Back(away, 250) == PointF.Empty && CoffeeCup.Back(away, 5000) == PointF.Empty, "the way back starts where the cup was let go and ends on the saucer");
                float last = float.MaxValue;
                for (int ms = 0; ms <= 250; ms += 25)
                {
                    var p = CoffeeCup.Back(away, ms);
                    float far = (float)Math.Sqrt(p.X * p.X + p.Y * p.Y);
                    Expect(far < last || far == 0, "the cup only moves home, never away, at " + ms + " ms");
                    last = far;
                }
                Expect(CoffeeCup.Pulled(PointF.Empty, 1) == 0 && CoffeeCup.Pulled(new PointF(-37, 0), 1) == 0.5f && CoffeeCup.Pulled(new PointF(0, 300), 1) == 1 && CoffeeCup.Pulled(new PointF(0, 148), 2) == 1,
                       "how far it is tipped follows how far it is pulled, 0 at the saucer and 1 at the edge of the ring, at any scale");

                return "6 pictures; coffee " + outsideAtRest + " px outside the cup at rest, " + leftSpilled + " px left of it spilled; "
                       + "yellow middle moved " + shiftReal.ToString("0.00", invariant) + " px (" + shiftBig.ToString("0.00", invariant) + " px at 4 times the size); "
                       + poses + " pulled poses not clipped; back in " + took + " ms";
            });
        }

        static Task<string> CheckAboutRefresh(string dir)
        {
            return OnSta(() =>
            {
                var ini = Path.Combine(dir, "about-refresh-test.ini");
                File.Delete(ini);
                var settings = new DragShift.Core.Settings(ini);
                var caps = new Capabilities();
                using (var form = new SettingsForm(settings, caps))
                {
                    var labels = Descendants(form).OfType<WinForms.Label>().ToList();
                    // About lists what this PC can do as lines of their own (a green tick and "on", or a grey dash and what to do about it).
                    StatusRow Row(string name) => labels.OfType<StatusRow>().Single(l => l.Text.StartsWith(name, StringComparison.Ordinal));
                    var heic = Row("HEIC saving");
                    var tally = labels.Single(l => l.Text.StartsWith("DragShift has fixed ", StringComparison.Ordinal));
                    Expect(!heic.Good && !heic.Text.Contains("— on") && tally.Text.Contains("0 files"), "initial capability and zero count");
                    Expect(Row("Text reading").Text == "Text reading — on" && Row("Face finding").Text == "Face finding — on" && Row("Text reading").Good && Row("Face finding").Good,
                           "text reading and face finding start on, as this check's Capabilities say");
                    var handle = form.Handle; // hidden real window for the background completion callback
                    Task.Run(() => caps.HeicWrite = true).GetAwaiter().GetResult();
                    var until = DateTime.UtcNow.AddSeconds(3);
                    while (!heic.Good && DateTime.UtcNow < until)
                    {
                        WinForms.Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Expect(heic.Good && heic.Text == "HEIC saving — on", "the completed codec check updates the existing window");
                    settings.AddFixed(2);
                    Expect(tally.Text.Contains("2 files"), "two completed files update the existing tally");
                    Expect(((RichLine)tally).Bold == "2 files" && tally.Text.Contains("fixed 2 files for you"), "the count is the part shown in bold");
                    settings.AddFixed(0);
                    Expect(tally.Text.Contains("2 files"), "an action with no output does not increase the tally");
                }
                caps.HeicWrite = false; // the disposed form is no longer subscribed
                return "background codec check and current tally; no window shown";
            });
        }

        static Task<string> CheckSettingsTabs(string dir)
        {
            return OnSta(() =>
            {
                var ini = Path.Combine(dir, "settings-tabs-test.ini");
                File.Delete(ini);
                using (var form = new SettingsForm(new DragShift.Core.Settings(ini), Capabilities.All))
                {
                    var pages = form.Controls.OfType<WinForms.TabControl>().Single();
                    var strip = form.Controls.OfType<TabStrip>().Single();
                    Expect(pages.TabCount == 6, "six pages, got " + pages.TabCount);
                    // The keyboard stops once, on the visible names; the control that holds the pages (its own names are hidden) is no stop.
                    Expect(strip.TabStop && !pages.TabStop && strip.TabIndex < pages.TabIndex, "the names are the one stop, ahead of the pages");

                    // A screen reader gets the six names, in order, as tabs of a tab list, the open one marked.
                    var list = strip.AccessibilityObject;
                    Expect(list.Role == WinForms.AccessibleRole.PageTabList && list.GetChildCount() == 6, "a tab list of six");
                    for (int i = 0; i < 6; i++)
                    {
                        var tab = list.GetChild(i);
                        Expect(tab != null && tab.Name == pages.TabPages[i].Text && tab.Role == WinForms.AccessibleRole.PageTab && tab.Parent == list,
                               "tab " + i + " is named " + pages.TabPages[i].Text);
                        Expect(tab.Bounds.Width > 0 && tab.Bounds.Height > 0, "tab " + i + " has a place on the screen");
                    }
                    Expect(list.GetChild(6) == null && list.GetChild(-1) == null, "no seventh tab");
                    Expect(pages.AccessibilityObject.GetChildCount() == 6 && pages.AccessibilityObject.GetChild(0) != null, "the pages control tells about its pages, not a second row of names");

                    bool Open(int i) => (list.GetChild(i).State & WinForms.AccessibleStates.Selected) != 0;
                    for (int i = 0; i < 6; i++)
                    {
                        pages.SelectedIndex = i;
                        int open = 0;
                        for (int j = 0; j < 6; j++) if (Open(j)) open++;
                        Expect(open == 1 && Open(i) && list.GetSelected().Name == pages.TabPages[i].Text, "only " + pages.TabPages[i].Text + " is marked open");
                    }
                    list.GetChild(4).DoDefaultAction();
                    Expect(pages.SelectedIndex == 4, "choosing a tab from a screen reader opens its page");

                    // Arrow keys, Home and End on the names switch pages, and stop at the ends.
                    var onKeyDown = typeof(WinForms.Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    int Press(WinForms.Keys key) { onKeyDown.Invoke(strip, new object[] { new WinForms.KeyEventArgs(key) }); return pages.SelectedIndex; }
                    Expect(Press(WinForms.Keys.Right) == 5 && Press(WinForms.Keys.Right) == 5, "right goes to the next page and stops at the last");
                    Expect(Press(WinForms.Keys.Left) == 4 && Press(WinForms.Keys.Home) == 0 && Press(WinForms.Keys.Left) == 0, "left goes back, Home goes to the first and left stops there");
                    Expect(Press(WinForms.Keys.End) == 5, "End goes to the last");

                    // The keyboard stays on the names while the pages change: the control that holds the pages is hidden, and it used to
                    // take the focus on the first arrow key (the arrows still worked, but the focus mark was gone and a screen reader
                    // was pointed at a control nobody can see). This needs the window shown, far outside every screen.
                    form.StartPosition = WinForms.FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.ShowInTaskbar = false;
                    var handle = form.Handle;
                    Native.ShowWindow(handle, Native.SW_SHOWNOACTIVATE);
                    WinForms.Application.DoEvents();
                    try
                    {
                        pages.SelectedIndex = 0;
                        strip.Focus();
                        Expect(strip.Focused, "the names can take the keyboard");
                        foreach (var key in new[] { WinForms.Keys.Right, WinForms.Keys.Right, WinForms.Keys.End, WinForms.Keys.Home, WinForms.Keys.Right })
                        {
                            Press(key);
                            Expect(strip.Focused && !pages.Focused, "after " + key + " the keyboard is still on the names, not on the hidden control behind them");
                        }
                        var focusedTab = list.GetFocused();
                        Expect(focusedTab != null && focusedTab.Name == pages.TabPages[pages.SelectedIndex].Text, "a screen reader is told that the open page's name has the focus");
                        pages.Focus(); // however it got there, the control that holds the pages must hand the keyboard on when a page is chosen from the names
                        list.GetChild(3).DoDefaultAction();
                        Expect(pages.SelectedIndex == 3 && strip.Focused, "choosing a name from a screen reader leaves the keyboard on the names");
                    }
                    finally { Native.ShowWindow(handle, Native.SW_HIDE); }
                }
                return "six named tabs, one keyboard stop, arrow keys that keep the keyboard on the names";
            });
        }

        /// <summary>Showing a Settings page used to set off a storm of layouts (seventy on the Printing page, over two seconds before it
        /// appeared). Count the layouts and time the switch, on every page, the first time and when it is already built.</summary>
        static Task<string> CheckSettingsSpeed(string dir)
        {
            return OnSta(() =>
            {
                var ini = Path.Combine(dir, "settings-speed-test.ini");
                File.Delete(ini);
                using (var form = new SettingsForm(new DragShift.Core.Settings(ini), Capabilities.All))
                {
                    form.StartPosition = WinForms.FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.ShowInTaskbar = false;
                    var handle = form.Handle;
                    Native.ShowWindow(handle, Native.SW_SHOWNOACTIVATE);
                    WinForms.Application.DoEvents();
                    try
                    {
                        var pages = form.Controls.OfType<WinForms.TabControl>().Single();
                        int layouts = 0;
                        foreach (var box in Descendants(form).OfType<WinForms.Panel>()) box.Layout += (s, e) => layouts++; // tables, flows, cards, panes, pages
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        int mostLayouts = 0;
                        long slowest = 0;
                        for (int round = 0; round < 2; round++)
                            for (int i = 0; i < pages.TabCount; i++)
                            {
                                layouts = 0;
                                clock.Restart();
                                pages.SelectedIndex = i;
                                WinForms.Application.DoEvents();
                                long took = clock.ElapsedMilliseconds;
                                Expect(layouts <= 12, "the " + pages.TabPages[i].Text + " page (round " + (round + 1) + ") set off " + layouts + " layouts");
                                Expect(took < 1500, "the " + pages.TabPages[i].Text + " page (round " + (round + 1) + ") took " + took + " ms to show");
                                mostLayouts = Math.Max(mostLayouts, layouts);
                                slowest = Math.Max(slowest, took);
                            }
                        return "all 6 pages shown twice: at most " + mostLayouts + " layouts and " + slowest + " ms for one";
                    }
                    finally { Native.ShowWindow(handle, Native.SW_HIDE); }
                }
            });
        }

        /// <summary>The Settings window was rebuilt, so each setting in it is changed here the way a click would change it and read back from the
        /// settings: it must save its own value, and putting the old value back must restore it. The two switches that act on Windows itself
        /// (Start with Windows, Send to) are left alone: they change the real registry and Send to folder.</summary>
        static Task<string> CheckSettingsWiring(string dir)
        {
            return OnSta(() =>
            {
                var ini = Path.Combine(dir, "settings-wiring-test.ini");
                File.Delete(ini);
                var settings = new DragShift.Core.Settings(ini); // its own file: the real settings are never touched
                var onClick = typeof(WinForms.Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                int checks = 0;
                using (var form = new SettingsForm(settings, Capabilities.All))
                {
                    Expect(form.Handle != IntPtr.Zero, "the window exists (the Favorites list saves through BeginInvoke, which needs it)");
                    bool? hotkeyHeard = null;
                    string shelfHeard = null;
                    form.HotkeyChanged += on => hotkeyHeard = on;
                    form.ShelfChanged += side => shelfHeard = side;
                    var pages = form.Controls.OfType<WinForms.TabControl>().Single().TabPages.Cast<WinForms.TabPage>().ToDictionary(p => p.Text);
                    WinForms.CheckBox Box(string page, string text) => Descendants(pages[page]).OfType<WinForms.CheckBox>().Single(b => b.Text == text);
                    WinForms.ComboBox Combo(string page, object item) => Descendants(pages[page]).OfType<WinForms.ComboBox>().Single(c => c.Items.Contains(item));
                    WinForms.Button Button(string page, string text) => Descendants(pages[page]).OfType<WinForms.Button>().Single(b => b.Text == text);
                    void Press(WinForms.Control c) => onClick.Invoke(c, new object[] { EventArgs.Empty }); // a hidden window refuses PerformClick
                    void Good(bool ok, string what) { Expect(ok, what); checks++; }

                    // A tick box: it shows what is saved, ticking it saves the opposite, unticking it saves the first value again.
                    void Flip(string page, string text, Func<bool> read, Func<bool, bool> told = null)
                    {
                        var box = Box(page, text);
                        bool before = read();
                        Good(box.Checked == before, page + ", \"" + text + "\": the box shows what is saved");
                        box.Checked = !before;
                        Good(read() == !before, page + ", \"" + text + "\": changing it is saved");
                        if (told != null) Good(told(!before), page + ", \"" + text + "\": the running app is told");
                        box.Checked = before;
                        Good(read() == before, page + ", \"" + text + "\": putting it back is saved");
                    }

                    Flip("General", "Play a sound when a file is ready", () => settings.GetBool(Core.Settings.Keys.Sound, false));
                    Flip("General", "Make what I pick most glow when the ring opens", () => settings.GetBool(Core.Settings.Keys.UsualGlow, true));
                    Flip("General", "Put text it reads on the clipboard", () => settings.GetBool(Core.Settings.Keys.CopyText, true));
                    Flip("General", "Show the new file in Explorer", () => settings.GetBool(Core.Settings.Keys.Reveal, false));

                    Flip("Triggers", "Press Shift while dragging", () => settings.Shift);
                    Flip("Triggers", "Tap one more finger on the touchpad", () => settings.Finger);
                    Flip("Triggers", "Ctrl+Alt+V opens the clipboard in the ring", () => settings.GetBool(Core.Settings.Keys.Hotkey, true), on => hotkeyHeard == on);
                    var shelf = Combo("Triggers", "Right edge");
                    Good(shelf.SelectedIndex == 0 && settings.Get(Core.Settings.Keys.Shelf, "right") == "right", "Triggers: the shelf starts on the right edge");
                    foreach (var pick in new[] { (Index: 1, Saved: "left"), (Index: 2, Saved: "off"), (Index: 0, Saved: "right") })
                    {
                        shelf.SelectedIndex = pick.Index;
                        Good(settings.Get(Core.Settings.Keys.Shelf) == pick.Saved && shelfHeard == pick.Saved, "Triggers: the shelf choice '" + pick.Saved + "' is saved and the running app is told");
                    }

                    // Favorites: tick one, move, reset.
                    var fav = Descendants(pages["Favorites"]).OfType<WinForms.CheckedListBox>().Single();
                    var original = settings.Favorites.ToList();
                    Good(fav.CheckedItems.Count == original.Count, "Favorites: the ticked ones are the saved favourites");
                    int extra = Enumerable.Range(0, fav.Items.Count).First(i => !fav.GetItemChecked(i));
                    fav.SetItemChecked(extra, true);
                    WinForms.Application.DoEvents(); // the list saves a moment after the tick
                    Good(settings.Favorites.Count == original.Count + 1 && original.All(settings.Favorites.Contains), "Favorites: ticking one adds it to the saved favourites");
                    fav.SetItemChecked(extra, false);
                    WinForms.Application.DoEvents();
                    Good(settings.Favorites.SequenceEqual(original), "Favorites: unticking it takes it out again");
                    fav.SelectedIndex = 1;
                    Press(Button("Favorites", "Move up"));
                    Good(settings.Favorites.Take(2).SequenceEqual(new[] { original[1], original[0] }), "Favorites: Move up puts the second one first");
                    Press(Button("Favorites", "Move down"));
                    Good(settings.Favorites.SequenceEqual(original), "Favorites: Move down puts it back");

                    // Saving.
                    var where = Combo("Saving", "Downloads");
                    where.SelectedIndex = 1;
                    Good(settings.Get(Core.Settings.Keys.OutputMode) == "downloads", "Saving: Downloads is saved");
                    where.SelectedIndex = 0;
                    Good(settings.Get(Core.Settings.Keys.OutputMode) == "beside", "Saving: Next to the original is saved"); // ("A folder I choose…" opens a dialog)
                    var slider = Descendants(pages["Saving"]).OfType<WinForms.TrackBar>().Single();
                    int quality = slider.Value;
                    Good(Math.Abs(settings.Quality - quality / 100.0) < 1e-9, "Saving: the slider shows the saved JPG quality");
                    slider.Value = 70;
                    Good(Math.Abs(settings.Quality - 0.7) < 1e-9 && Descendants(pages["Saving"]).OfType<WinForms.Label>().Any(l => l.Text == "70"), "Saving: 70 on the slider is saved as 0.7 and shown as 70");
                    slider.Value = quality;
                    var size = Combo("Saving", PhotoSpec.Presets[0].Name);
                    var limit = Combo("Saving", "No limit");
                    var background = Combo("Saving", "Keep the original");
                    int other = size.SelectedIndex == 1 ? 2 : 1;
                    size.SelectedIndex = other;
                    var wanted = PhotoSpec.Presets[other].Spec;
                    Good(settings.Photo.WidthMM == wanted.WidthMM && settings.Photo.HeightMM == wanted.HeightMM, "Saving: the passport size is saved");
                    limit.SelectedIndex = limit.Items.IndexOf("No limit");
                    Good(settings.Photo.MaxKB == 0, "Saving: No limit is saved");
                    background.SelectedIndex = background.Items.IndexOf("Light blue");
                    Good(settings.Photo.Background == PhotoBackground.LightBlue, "Saving: the light blue background is saved");
                    Good(settings.Photo.WidthMM == wanted.WidthMM && settings.Photo.MaxKB == 0, "Saving: changing one passport choice keeps the others");
                    var watermark = Descendants(pages["Saving"]).OfType<WinForms.TextBox>().Single();
                    watermark.Text = "© wiring test";
                    Good(settings.Get(Core.Settings.Keys.Watermark) == "© wiring test", "Saving: the watermark text is saved");

                    // Printing.
                    var printing = pages["Printing"];
                    var list = Descendants(printing).OfType<WinForms.ListBox>().Single();
                    var name = Descendants(printing).OfType<WinForms.TextBox>().Single(t => t.GetType() == typeof(WinForms.TextBox)); // not the spin box's own
                    var copies = Descendants(printing).OfType<WinForms.NumericUpDown>().Single();
                    Good(list.Items.Count == settings.PrintPresets.Count && list.SelectedIndex == 0, "Printing: the list shows the saved presets, the first one picked");
                    name.Text = "Everyday 2";
                    Good(settings.PrintPresets[0].Name == "Everyday 2" && list.Items[0].ToString() == "Everyday 2", "Printing: a new name is saved and shows in the list");
                    copies.Value = 3;
                    Good(settings.PrintPresets[0].Copies == 3, "Printing: the number of copies is saved in the preset");
                    Flip("Printing", "Print on both sides", () => settings.PrintPresets[0].TwoSided);
                    Flip("Printing", "In the Print list and on the ring", () => !settings.Hidden.Contains(settings.PrintPresets[0].StepId));
                    Flip("Printing", "★ In the ring's Favorites", () => settings.Favorites.Contains(settings.PrintPresets[0].StepId));
                    int count = settings.PrintPresets.Count;
                    Press(Button("Printing", "+"));
                    Good(settings.PrintPresets.Count == count + 1 && list.Items.Count == count + 1 && list.SelectedIndex == count, "Printing: + adds a preset and picks it");
                    Press(Button("Printing", "−"));
                    Good(settings.PrintPresets.Count == count && list.Items.Count == count, "Printing: − takes the picked preset away");
                    Flip("Printing", "Print straight away when I pick Print (uses the first preset shown, no window)", () => settings.GetBool(Core.Settings.Keys.PrintQuick, false));
                    Press(Button("Printing", "Reset"));
                    Good(settings.PrintPresets.Count == PrintPreset.BuiltIns.Length && settings.PrintPresets[0].Name == "Everyday" && settings.PrintPresets[0].Copies == 1, "Printing: Reset gives back the standard presets");

                    // Last, because it closes the window.
                    Press(Button("Favorites", "Reset"));
                    Good(settings.Favorites.SequenceEqual(Catalog.DefaultFavorites), "Favorites: Reset gives back the standard favourites");
                }
                return checks + " things checked: every tick box, menu, slider and text box on General, Triggers, Favorites, Saving and Printing saves its own value and puts it back";
            });
        }

        static Task<string> CheckSavingFolderLabel(string dir)
        {
            return OnSta(() =>
            {
                var folder = Path.Combine(dir, "Chosen Output Folder");
                // A control's own visible flag (not "is it on screen": the window is never shown here).
                var getState = typeof(WinForms.Control).GetMethod("GetState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                                                                  null, new[] { typeof(int) }, null);
                Expect(getState != null, "Windows Forms still has the visible flag to read");
                bool Shown(WinForms.Control c) => (bool)getState.Invoke(c, new object[] { 0x00000002 });
                foreach (var mode in new[] { "beside", "downloads", "folder" })
                {
                    var ini = Path.Combine(dir, "saving-label-test.ini");
                    File.Delete(ini);
                    var settings = new DragShift.Core.Settings(ini);
                    settings.Set(Core.Settings.Keys.OutputMode, mode);
                    settings.Set(Core.Settings.Keys.OutputFolder, folder);
                    using (var form = new SettingsForm(settings, Capabilities.All))
                    {
                        var label = Descendants(form).OfType<WinForms.Label>().Single(l => l.Text == folder);
                        Expect(Shown(label) == (mode == "folder"), "mode " + mode + ": the folder line is " + (mode == "folder" ? "shown" : "hidden"));
                    }
                }
                return "hidden for Next to the original and Downloads, shown for a chosen folder; no window shown";
            });
        }

        static Task<string> CheckFollowLastStart(string dir)
        {
            return OnSta(() =>
            {
                // Made-up places only: a throwaway key in this user's own registry hive and a Send to folder inside the test folder.
                // The real startup entry and the real Send to link are never touched.
                var drive = Path.GetPathRoot(Environment.SystemDirectory);
                var oldExe = Path.Combine(drive, "DragShiftSelfTest", "Old copy", "DragShift.exe");
                var newExe = Path.Combine(drive, "DragShiftSelfTest", "New copy (v2)", "DragShift.exe");
                const string runKey = @"Software\DragShiftSelfTest\Run";
                var linkDir = Path.Combine(dir, "send-to-test");
                var link = Path.Combine(linkDir, "DragShift.lnk");
                string RunValue() { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey)) return k?.GetValue("DragShift") as string; }
                void SetRun(string v) { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runKey)) k.SetValue("DragShift", v); }
                bool Same(string a, string b) { try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; } }
                string Quoted(string exe) => "\"" + exe + "\" --background";
                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\DragShiftSelfTest", false);
                    Directory.CreateDirectory(linkDir);
                    File.Delete(link);

                    // This copy's own path uses plain backslashes (a folder with a # in its name used to give a forward slash).
                    Expect(!Paths.Exe.Contains("/"), "the path of this .exe has only backslashes: " + Paths.Exe);

                    // Nothing switched on: a start must not create anything.
                    var said = Integration.FollowLastStart(newExe, runKey, link);
                    Expect(said.Count == 0 && RunValue() == null && !File.Exists(link), "a start creates no startup entry and no Send to link by itself");

                    // Switched on from the old copy, then DragShift is started from the new copy.
                    Integration.WriteRun(runKey, oldExe);
                    Integration.WriteLink(link, oldExe);
                    Expect(Same(Integration.LinkTarget(link), oldExe), "the made-up link opens the old copy");
                    said = Integration.FollowLastStart(newExe, runKey, link);
                    Expect(said.Count == 2, "it says what it changed (the startup entry and the link): " + string.Join(" / ", said));
                    Expect(RunValue() == Quoted(newExe), "Start with Windows now starts the copy that was started last, not " + RunValue());
                    Expect(Same(Integration.LinkTarget(link), newExe), "Send to now opens the copy that was started last, not " + Integration.LinkTarget(link));

                    // Already right: nothing is written again (the link keeps its old time stamp).
                    var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    File.SetLastWriteTimeUtc(link, stamp);
                    said = Integration.FollowLastStart(newExe, runKey, link);
                    Expect(said.Count == 0 && File.GetLastWriteTimeUtc(link) == stamp, "a link that already opens this copy is not written again");

                    // The same copy written another way (other letter case, no quotes, an extra argument): left as it is.
                    SetRun(newExe.ToUpperInvariant() + " --background --test");
                    var before = RunValue();
                    said = Integration.FollowLastStart(newExe, runKey, link);
                    Expect(said.Count == 0 && RunValue() == before, "an entry that already starts this copy is left as it is");

                    // A copy that runs from somewhere temporary or off this PC does not take the entries along.
                    Integration.WriteRun(runKey, oldExe);
                    Integration.WriteLink(link, oldExe);
                    foreach (var transient in new[] { Path.Combine(Path.GetTempPath(), "Temp1_DragShift.zip", "DragShift.exe"), @"\\server\share\DragShift.exe" })
                    {
                        said = Integration.FollowLastStart(transient, runKey, link);
                        Expect(said.Count == 0 && RunValue() == Quoted(oldExe) && Same(Integration.LinkTarget(link), oldExe), "a copy running from " + transient + " leaves the entries where they are");
                    }

                    // A link that cannot be read is written again.
                    Integration.WriteRun(runKey, newExe);
                    File.WriteAllText(link, "not a shortcut");
                    said = Integration.FollowLastStart(newExe, runKey, link);
                    Expect(said.Count == 1 && Same(Integration.LinkTarget(link), newExe), "a damaged Send to link is repaired: " + string.Join(" / ", said));

                    // Switched off by the person: a start does not bring them back.
                    using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runKey)) k.DeleteValue("DragShift", false);
                    File.Delete(link);
                    said = Integration.FollowLastStart(newExe, runKey, link);
                    Expect(said.Count == 0 && RunValue() == null && !File.Exists(link), "entries that were switched off stay off");
                    return "both entries follow the copy started last; none made, none moved from a temporary or network place; made-up places only";
                }
                finally
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\DragShiftSelfTest", false);
                    try { Directory.Delete(linkDir, true); } catch (Exception) { }
                }
            });
        }

        /// <summary>What is wrong with how one Settings page is laid out (an empty list means nothing). Call it while the window is shown.
        /// A page is right when its column of cards is in the middle of it, every card has the same left edge and width, nothing sticks
        /// out of the box it sits in, and it needs no scroll bar at the size the window opens at. On About every part is in the middle
        /// of its column and the app's icon is there.</summary>
        static List<string> SettingsLayoutProblems(WinForms.TabPage page)
        {
            var bad = new List<string>();
            var pane = page.Controls.OfType<Pane>().SingleOrDefault();
            if (pane == null) return new List<string> { "there is no page of cards" };
            var column = pane.Column;
            int off = column.Left + column.Width / 2 - pane.ClientSize.Width / 2;
            if (Math.Abs(off) > 2) bad.Add("the column is " + off + " px off the middle");
            int end = column.Bottom + column.Margin.Bottom;
            if (end > pane.ClientSize.Height) bad.Add("it ends at " + end + " but the page is only " + pane.ClientSize.Height + " tall");
            if (pane.VerticalScroll.Visible || pane.HorizontalScroll.Visible) bad.Add("a scroll bar shows");
            var cards = column.Controls.OfType<Card>().ToList();
            if (cards.Select(c => c.Left).Distinct().Count() > 1 || cards.Select(c => c.Width).Distinct().Count() > 1) bad.Add("the cards do not line up");
            foreach (var c in Descendants(column).Where(c => c.Visible && c.Parent != null))
            {
                var box = c.Parent.ClientSize;
                if (c.Left < -1 || c.Right > box.Width + 1 || c.Top < -1 || c.Bottom > box.Height + 1)
                    bad.Add(c.GetType().Name + " \"" + (c.Text.Length > 24 ? c.Text.Substring(0, 24) : c.Text) + "\" sticks out of its " + c.Parent.GetType().Name);
            }
            if (page.Text == "About")
            {
                var about = column.Controls.OfType<WinForms.TableLayoutPanel>().FirstOrDefault(t => !(t is Card));
                if (about == null) bad.Add("About has no column");
                else
                {
                    foreach (var c in about.Controls.Cast<WinForms.Control>().Where(c => c.Visible))
                    {
                        int d = c.Left + c.Width / 2 - about.Width / 2;
                        if (Math.Abs(d) > 2) bad.Add(c.GetType().Name + " \"" + c.Text + "\" is " + d + " px off the middle");
                    }
                    var icon = Descendants(about).OfType<IconView>().SingleOrDefault();
                    if (icon == null || !icon.HasPicture) bad.Add("the app's icon is missing");
                }
            }
            return bad;
        }

        static Task<string> CheckWindows(string dir)
        {
            return OnSta(() =>
            {
                var ini = Path.Combine(dir, "settings-window-test.ini");
                File.Delete(ini);
                var settings = new DragShift.Core.Settings(ini); // its own file: the real settings are never touched
                settings.Set(Core.Settings.Keys.Watermark, "© c0ldsheep");
                int tabCount;
                using (var form = new SettingsForm(settings, Capabilities.All))
                {
                    var boxes = Descendants(form).OfType<WinForms.CheckBox>().ToList();
                    WinForms.CheckBox Box(string text) => boxes.Single(b => b.Text == text);
                    var master = Box("Click another mouse button while dragging");
                    var names = new[] { "Right button", "Wheel click (middle button)", "Side button: back", "Side button: forward" };
                    Expect(master.Checked && names.All(n => Box(n).Checked && Box(n).Enabled), "all four buttons are ticked and usable at first");
                    Box("Right button").Checked = false;
                    Expect(File.ReadAllText(ini).Contains("trigger.mouse.buttons = middle,back,forward"), "unticking the right button is saved in plain words");
                    Expect(settings.ActiveTriggerButtons == (MouseTrigger.Middle | MouseTrigger.Back | MouseTrigger.Forward), "and the other three stay active");
                    master.Checked = false;
                    Expect(names.All(n => !Box(n).Enabled), "the four are greyed out while the mouse switch is off");
                    Expect(settings.ActiveTriggerButtons == MouseTrigger.None && settings.TriggerButtons != MouseTrigger.None, "nothing is active, but the ticks are kept");
                    master.Checked = true;
                    Box("Right button").Checked = true;
                    Expect(settings.ActiveTriggerButtons == MouseTrigger.All, "ticking everything again gives every button back");

                    var tabs = form.Controls.OfType<WinForms.TabControl>().Single();
                    tabCount = tabs.TabPages.Count;
                    var problems = new List<string>();
                    for (int i = 0; i < tabs.TabPages.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        var page = tabs.TabPages[i];
                        Picture(form, Path.Combine(dir, "settings-" + page.Text.ToLowerInvariant() + ".png"),
                                () => problems.AddRange(SettingsLayoutProblems(page).Select(p => page.Text + ": " + p)));
                    }
                    Expect(problems.Count == 0, "every Settings page is laid out properly: " + string.Join("; ", problems));
                }
                var practice = Path.Combine(dir, "Practice", "Try Me.png");
                using (var welcome = new WelcomeForm(practice)) Picture(welcome, Path.Combine(dir, "welcome.png"));
                Expect(File.Exists(practice), "the test practice picture stays in the test folder");
                return tabCount + " Settings tabs and the Welcome window drawn; each page centred, nothing sticking out of its box, nothing needing a scroll bar";
            });
        }

        /// <summary>The ring at 1.5× on a stand-in desktop, the way it looks on screen.</summary>
        public static string Snapshot(RingModel m, bool dark, string path)
        {
            const float s = 1.5f;
            int size = (int)(Geometry.Size * s);
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    using (var bg = new LinearGradientBrush(new Rectangle(0, 0, size, size), Color.FromArgb(250, 158, 77), Color.FromArgb(128, 115, 217), 60f))
                        g.FillRectangle(bg, 0, 0, size, size);
                    g.ScaleTransform(s, s);
                    RingPainter.Paint(g, m, dark);
                }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            return path;
        }

        /// <summary>A plain 440 Hz tone as 16-bit mono PCM.</summary>
        static void WriteTone(string path, int seconds)
        {
            const int rate = 44100;
            int samples = rate * seconds;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples * 2);
                for (int i = 0; i < samples; i++) w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
            }
        }
    }
}
