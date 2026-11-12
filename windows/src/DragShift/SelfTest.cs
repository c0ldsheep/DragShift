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

            await Check("About tab: the coffee cup and its ring", async () =>
            {
                await Task.Yield();
                Expect(Tip.Line(0).Contains("0 files") && Tip.Line(214).Contains("214 files"), "the count is always in the line");
                var size = CoffeeCup.Natural;
                var looks = new[] { ("cup-closed", PointF.Empty, false), ("cup-ring-small", new PointF(-55, 25), true),
                                    ("cup-ring-medium", new PointF(0, 62), true), ("cup-ring-large", new PointF(55, 25), true) };
                foreach (var look in looks)
                    using (var bmp = new Bitmap(size.Width * 2, size.Height * 2, PixelFormat.Format32bppPArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                            CoffeeCup.Draw(g, bmp.Width, bmp.Height, new PointF(look.Item2.X * 2, look.Item2.Y * 2), look.Item3, SystemColors.Control, SystemColors.ControlText);
                        bmp.Save(Path.Combine(dir, look.Item1 + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        // The cup must be gold where it sits, in every picture: the middle of its body, which is left of and below the cup's centre.
                        float cx = bmp.Width / 2f + look.Item2.X * 2, cy = (22 + 6) * 2 + look.Item2.Y * 2;
                        var c = bmp.GetPixel((int)(cx - 9), (int)(cy + 18));
                        Expect(c.R > 230 && c.G > 190 && c.B < 60, look.Item1 + ": a gold cup, got " + c);
                        if (!look.Item3)
                        {
                            // The cup is on its own: this spot was inside the old gold circle and is plain window colour now.
                            var behind = bmp.GetPixel((int)(cx - 38), (int)(cy - 18));
                            Expect(behind.R == SystemColors.Control.R && behind.G == SystemColors.Control.G && behind.B == SystemColors.Control.B,
                                   look.Item1 + ": no circle behind the cup, got " + behind);
                        }
                    }
                return looks.Length + " pictures";
            });

            await Check("About information follows codec checks and completed files", () => CheckAboutRefresh(dir));

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

            Line(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            return failed == 0 ? 0 : 1;
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
        static void Picture(WinForms.Form form, string path)
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
                    var pc = labels.Single(l => l.Text.StartsWith("This PC:", StringComparison.Ordinal));
                    var tally = labels.Single(l => l.Text.StartsWith("DragShift has fixed ", StringComparison.Ordinal));
                    Expect(pc.Text.Contains("HEIC saving off") && tally.Text.Contains("0 files"), "initial capability and zero count");
                    var handle = form.Handle; // hidden real window for the background completion callback
                    Task.Run(() => caps.HeicWrite = true).GetAwaiter().GetResult();
                    var until = DateTime.UtcNow.AddSeconds(3);
                    while (!pc.Text.Contains("HEIC saving on") && DateTime.UtcNow < until)
                    {
                        WinForms.Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Expect(pc.Text.Contains("HEIC saving on"), "the completed codec check updates the existing window");
                    settings.AddFixed(2);
                    Expect(tally.Text.Contains("2 files"), "two completed files update the existing tally");
                    settings.AddFixed(0);
                    Expect(tally.Text.Contains("2 files"), "an action with no output does not increase the tally");
                }
                caps.HeicWrite = false; // the disposed form is no longer subscribed
                return "background codec check and current tally; no window shown";
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
                    for (int i = 0; i < tabs.TabPages.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        Picture(form, Path.Combine(dir, "settings-" + tabs.TabPages[i].Text.ToLowerInvariant() + ".png"));
                    }
                }
                var practice = Path.Combine(dir, "Practice", "Try Me.png");
                using (var welcome = new WelcomeForm(practice)) Picture(welcome, Path.Combine(dir, "welcome.png"));
                Expect(File.Exists(practice), "the test practice picture stays in the test folder");
                return tabCount + " Settings tabs and the Welcome window drawn";
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
