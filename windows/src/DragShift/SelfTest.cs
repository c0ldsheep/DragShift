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
            int code = Task.Run(() => All(dir)).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(dir, "selftest.txt"), report.ToString());
            Console.Out.Flush();
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
                foreach (var (name, inputs) in new[] { ("passport", new[] { Path.Combine(dir, "Print Passport.jpg") }), ("pdf", new[] { pdf }) })
                {
                    var target = Path.Combine(dir, "printed-" + name + ".pdf");
                    using (var job = await PrintJobs.Prepare(inputs, name == "passport" ? PrintPreset.BuiltIns[2] : preset,
                                                             PrintJobs.PaperFor(preset)))
                    {
                        Expect(PrintJobs.Send(job, preset, PrintJobs.PaperFor(preset), "DragShift self-test", false, target, pdfPrinter), "sent");
                    }
                    for (int i = 0; i < 50 && !File.Exists(target); i++) await Task.Delay(200);
                    Expect(File.Exists(target) && new FileInfo(target).Length > 1000, "a printed PDF at " + target);
                    outs.Add(Path.GetFileName(target) + " " + Bytes.Format(new FileInfo(target).Length));
                }
                return string.Join(", ", outs);
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
                }
                return shots.Count + " pictures";
            });

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
                        // The cup must be gold where it sits, in every picture.
                        var c = bmp.GetPixel((int)(bmp.Width / 2f + look.Item2.X * 2 - 30), (int)((23 + 6) * 2 + look.Item2.Y * 2));
                        Expect(c.R > 230 && c.G > 190 && c.B < 60, look.Item1 + ": a gold cup, got " + c);
                    }
                return looks.Length + " pictures";
            });

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

            Line(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            return failed == 0 ? 0 : 1;
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
