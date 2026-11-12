using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DragShift.Core;
using CoreFormat = DragShift.Core.ImageFormat;

namespace DragShift.Engine
{
    /// <summary>Settings the engine needs; the app fills this from the settings file.</summary>
    sealed class Options
    {
        /// <summary>null = save next to the original.</summary>
        public string OutputFolder;
        public double Quality = 0.85;
        public double CompressQuality = 0.7;
        public int CompressMaxEdge = 3000;
        public double PdfDpi = 200;
        public PhotoSpec Photo = new PhotoSpec();
        public string Watermark = "© " + Environment.UserName;
        /// <summary>The password typed into Unlock. It is only held for that run: never saved, logged or put in a message.</summary>
        public string Password;
        /// <summary>What Redact covers (pixels of the upright picture) and how it hides it.</summary>
        public IList<Box> RedactBoxes;
        public RedactStyle RedactStyle = RedactStyle.Bar;

        public Options Copy() => (Options)MemberwiseClone();
    }

    /// <summary>Runs a slice on files. Files go one by one, so a big batch can't eat all memory, and one
    /// bad file doesn't stop the rest. Originals are never changed.</summary>
    static class Runner
    {
        public static async Task<RunReport> Run(Slice slice, IList<string> inputs, Options o, IProgress<double> progress, CancellationToken ct)
        {
            var report = new RunReport();
            string Dir(string p) => Naming.FolderFor(p, o.OutputFolder);
            var ordered = inputs.OrderBy(p => Path.GetFileName(p), Naming.NaturalOrder).ToList();

            async Task<RunReport> Once(Func<Task<List<string>>> body)
            {
                try { report.Outputs.AddRange(await body()); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { report.Failures.Add(Message(e)); }
                progress?.Report(1);
                return report;
            }

            // Steps that take all inputs at once.
            switch (slice.Id)
            {
                case "img.to.pdf" when inputs.Count > 1:
                case "folder.pdf":
                    return await Once(async () =>
                    {
                        var images = slice.Id == "folder.pdf" ? inputs.SelectMany(Inspector.ImagesIn).ToList() : ordered;
                        if (images.Count == 0) throw new UserError("No pictures found.");
                        var named = slice.Id == "folder.pdf" ? inputs[0] : images[0];
                        var outPath = Naming.Output(named, "pdf", images.Count > 1 ? "(" + images.Count + " pages)" : null, Dir(named));
                        await Pdf.MakeFromImages(images, outPath, o.Quality, progress, ct);
                        return new List<string> { outPath };
                    });
                case "img.stitch":
                    return await Once(async () =>
                    {
                        if (ordered.Count < 2) throw new UserError("Pick two or more pictures to stitch.");
                        // Two passes, one picture in memory at a time: sizes first, then drawing.
                        var sizes = new List<(int W, int H)>();
                        foreach (var p in ordered) { ct.ThrowIfCancellationRequested(); using (var img = await Images.Load(p)) sizes.Add((img.Width, img.Height)); }
                        var plan = DragShift.Core.Stitch.Plan(sizes);
                        var outPath = Naming.Output(ordered[0], "jpg", "(" + ordered.Count + " stitched)", Dir(ordered[0]));
                        using (var sheet = Images.Canvas(plan.Width, plan.Heights.Sum(), Color.White))
                        {
                            int top = 0;
                            for (int i = 0; i < ordered.Count; i++)
                            {
                                ct.ThrowIfCancellationRequested();
                                using (var img = await Images.Load(ordered[i]))
                                using (var g = Images.Draw(sheet))
                                    g.DrawImage(img, new Rectangle(0, top, plan.Width, plan.Heights[i]));
                                top += plan.Heights[i];
                                progress?.Report((i + 1.0) / (ordered.Count + 1));
                            }
                            await Images.Save(sheet, outPath, CoreFormat.Jpg, Math.Max(o.Quality, 0.85));
                        }
                        return new List<string> { outPath };
                    });
                case "file.zip" when inputs.Count > 1:
                    return await Once(() => Task.Run(() => new List<string> { Zip(inputs, Dir(inputs[0])) }, ct));
                case "pdf.merge":
                case "mixed.merge":
                    // PDFs give all their pages, pictures one page each, in Explorer's name order.
                    return await Once(async () => new List<string> { await PdfRewrite.Merge(ordered, o, Dir, progress, ct, report) });
                case "pdf.unlock":
                    // Each PDF on its own, with the one password; every result is reported (the messages name their file).
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var locked = ordered[i];
                        int index = i;
                        var sub = new Progress<double>(f => progress?.Report((index + f) / ordered.Count));
                        try { report.Outputs.Add(await PdfRewrite.Unlock(locked, o.Password, o, Dir, sub, ct, report)); }
                        catch (OperationCanceledException) { throw; }
                        catch (UserError e) { report.Failures.Add(e.Message); }
                        catch (Exception e) { report.Failures.Add(Path.GetFileName(locked) + ": " + Message(e)); }
                        progress?.Report((i + 1) / (double)ordered.Count);
                    }
                    return report;
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var url = ordered[i];
                int index = i;
                var sub = new Progress<double>(f => progress?.Report((index + f) / ordered.Count));
                try
                {
                    var r = await One(slice, url, o, Dir(url), sub, ct);
                    report.Add(r);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    report.Failures.Add(Path.GetFileName(url) + ": " + Message(e));
                }
                progress?.Report((i + 1) / (double)ordered.Count);
            }
            return report;
        }

        public static string Message(Exception e)
        {
            if (e is UserError) return e.Message;
            if (e is UnauthorizedAccessException) return "Windows didn't allow saving there. Try Settings → Saving → Downloads.";
            if (e is IOException) return e.Message;
            if (e is OutOfMemoryException) return "The file is too big to open.";
            return e.Message.Split('\r', '\n')[0];
        }

        static RunReport Out(params string[] paths)
        {
            var r = new RunReport();
            r.Outputs.AddRange(paths);
            return r;
        }

        /// <summary>One step on one file.</summary>
        static async Task<RunReport> One(Slice slice, string path, Options o, string dir, IProgress<double> progress, CancellationToken ct)
        {
            var id = slice.Id;
            var parts = id.Split('.');
            var kind = Inspector.Kind(path);

            if (SizeLimit.KbFromStep(id) is int kb)
            {
                var r = new RunReport { BytesBefore = Inspector.Size(path) };
                bool isJpeg = new[] { ".jpg", ".jpeg" }.Contains(Path.GetExtension(path).ToLowerInvariant());
                if ((kind == FileKind.Pdf || isJpeg) && r.BytesBefore <= SizeLimit.Bytes(kb))
                {
                    r.BytesAfter = r.BytesBefore;
                    r.Notes.Add(Path.GetFileName(path) + " is already under " + SizeLimit.Title(kb) + ".");
                    return r;
                }
                byte[] bytes;
                string outPath;
                if (kind == FileKind.Pdf)
                {
                    bytes = await Pdf.Fit(path, SizeLimit.Bytes(kb), progress, ct);
                    outPath = Naming.Output(path, "pdf", "Under " + SizeLimit.Title(kb), dir);
                    r.Notes.Add("Pages were saved as pictures to fit, so text can't be selected.");
                }
                else if (kind == FileKind.Image)
                {
                    using (var img = await Images.Load(path)) bytes = FitJpeg(img, SizeLimit.Bytes(kb), DpiOf(img));
                    outPath = Naming.Output(path, "jpg", "Under " + SizeLimit.Title(kb), dir);
                }
                else throw new UserError("Size limits work on pictures and PDFs.");
                Naming.Atomically(outPath, tmp => File.WriteAllBytes(tmp, bytes));
                r.Outputs.Add(outPath);
                r.BytesAfter = bytes.Length;
                return r;
            }

            // Picture → picture formats
            if (parts.Length == 3 && parts[0] == "img" && parts[1] == "to" && Formats.FromExt(parts[2]) is CoreFormat f)
            {
                using (var img = await Images.Load(path))
                {
                    var outPath = Naming.Output(path, f.Ext(), null, dir);
                    await Images.Save(img, outPath, f, o.Quality, DpiOf(img));
                    return Out(outPath);
                }
            }
            // PDF → pictures
            if (parts.Length == 3 && parts[0] == "pdf" && parts[1] == "to" && Formats.FromExt(parts[2]) is CoreFormat pf)
                return Out((await Pdf.ToImages(path, pf, o.PdfDpi, o.Quality, dir, progress, ct)).ToArray());
            // Media
            if (parts.Length == 3 && parts[0] == "media" && parts[1] == "to")
                return Out(await Media.Convert(path, parts[2], null, false, false, dir, progress, ct));
            // Steps with numbers in them (used by recipes): img.size.1600, img.fill.1080x1350, img.fit.1080x1920
            if (parts.Length == 3 && parts[0] == "img" && parts[1] == "size" && int.TryParse(parts[2], out var edge))
            {
                using (var img = await Images.Load(path))
                using (var fit = Images.Fit(img, edge))
                    return Out(await SaveEdited(fit, path, fit.Width + "x" + fit.Height, dir, o.Quality));
            }
            if (parts.Length == 3 && parts[0] == "img" && parts[1] == "pct" && int.TryParse(parts[2], out var pct) && pct > 0)
            {
                using (var img = await Images.Load(path))
                using (var scaled = Images.Scale(img, Math.Max(1, img.Width * pct / 100), Math.Max(1, img.Height * pct / 100)))
                    return Out(await SaveEdited(scaled, path, scaled.Width + "x" + scaled.Height, dir, o.Quality));
            }
            if (parts.Length == 3 && parts[0] == "img" && (parts[1] == "fill" || parts[1] == "fit") && ParseSize(parts[2]) is Size size)
            {
                using (var img = await Images.Load(path))
                using (var outImg = parts[1] == "fill" ? Images.Fill(img, size.Width, size.Height) : Images.FitStory(img, size.Width, size.Height))
                    return Out(await SaveEdited(outImg, path, parts[2], dir, o.Quality, parts[1] == "fit" ? CoreFormat.Jpg : (CoreFormat?)null));
            }

            switch (id)
            {
                case "img.to.pdf":
                {
                    var outPath = Naming.Output(path, "pdf", null, dir);
                    await Pdf.MakeFromImages(new[] { path }, outPath, o.Quality, progress, ct);
                    return Out(outPath);
                }
                case "img.to.text":
                {
                    string text;
                    using (var img = await Images.Load(path)) text = await Vision.ReadText(img);
                    if (string.IsNullOrWhiteSpace(text)) throw new UserError("No text found in the picture.");
                    var outPath = Naming.Output(path, "txt", null, dir);
                    Naming.Atomically(outPath, tmp => File.WriteAllText(tmp, text));
                    var r = Out(outPath);
                    r.Text = text;
                    return r;
                }
                case "pdf.to.txt":
                {
                    var text = await Pdf.Text(path, progress, ct);
                    if (string.IsNullOrWhiteSpace(text)) throw new UserError("No text found in the PDF.");
                    var outPath = Naming.Output(path, "txt", null, dir);
                    Naming.Atomically(outPath, tmp => File.WriteAllText(tmp, text));
                    var r = Out(outPath);
                    r.Text = text;
                    return r;
                }
                case "img.passport":
                {
                    var r = new RunReport { BytesBefore = Inspector.Size(path) };
                    using (var img = await Images.Load(path))
                    {
                        var made = await Vision.Passport(img, o.Photo);
                        using (made.Image)
                        {
                            var bytes = o.Photo.MaxKB > 0
                                ? FitJpeg(made.Image, SizeLimit.Bytes(o.Photo.MaxKB), o.Photo.Dpi)
                                : Images.Jpeg(made.Image, 0.92, o.Photo.Dpi);
                            var outPath = Naming.Output(path, "jpg", "Passport", dir);
                            Naming.Atomically(outPath, tmp => File.WriteAllBytes(tmp, bytes));
                            r.Outputs.Add(outPath);
                            r.BytesAfter = bytes.Length;
                        }
                        if (made.Note != null) r.Notes.Add(made.Note);
                    }
                    return r;
                }
                case "img.compress":
                    return await CompressImage(path, o, dir);
                case "pdf.compress":
                {
                    // Pages as pictures at 150 dpi; kept only if it's actually smaller.
                    var r = new RunReport { BytesBefore = Inspector.Size(path) };
                    var target = (int)Math.Max(20_000, r.BytesBefore * 0.8);
                    byte[] bytes;
                    try { bytes = await Pdf.Fit(path, target, progress, ct); }
                    catch (UserError) { bytes = null; }
                    if (bytes == null || bytes.Length >= r.BytesBefore)
                    {
                        r.BytesAfter = r.BytesBefore;
                        r.Notes.Add(Path.GetFileName(path) + " is already as small as it gets.");
                        return r;
                    }
                    var outPath = Naming.Output(path, "pdf", "Compressed", dir);
                    Naming.Atomically(outPath, tmp => File.WriteAllBytes(tmp, bytes));
                    r.Outputs.Add(outPath);
                    r.BytesAfter = bytes.Length;
                    r.Notes.Add("Pages were saved as pictures, so text can't be selected.");
                    return r;
                }
                case "img.rotate":
                case "img.flip":
                {
                    using (var img = await Images.Load(path))
                    using (var turned = Images.Turned(img, id == "img.rotate" ? RotateFlipType.Rotate90FlipNone : RotateFlipType.RotateNoneFlipX))
                        return Out(await SaveEdited(turned, path, id == "img.rotate" ? "Rotated" : "Flipped", dir, o.Quality));
                }
                case "img.watermark":
                {
                    using (var img = await Images.Load(path))
                    {
                        Watermark(img, o.Watermark);
                        return Out(await SaveEdited(img, path, "Watermarked", dir, o.Quality));
                    }
                }
                case "img.redact":
                {
                    // The boxes come from the Redact window. The pixels under them are replaced in a copy; the original is never touched.
                    if (o.RedactBoxes == null || o.RedactBoxes.Count == 0) throw new UserError("Nothing is covered. Open the picture in Redact and pick what to hide.");
                    using (var img = await Images.Load(path))
                    {
                        RedactOps.Apply(img, o.RedactBoxes, o.RedactStyle);
                        return Out(await SaveEdited(img, path, "Redacted", dir, o.Quality));
                    }
                }
                case "media.compress":
                {
                    var r = new RunReport { BytesBefore = Inspector.Size(path) };
                    var outPath = await Media.Convert(path, kind == FileKind.Audio ? "m4a" : "mp4", "Compressed", true, false, dir, progress, ct);
                    r.BytesAfter = Inspector.Size(outPath);
                    if (r.BytesAfter >= r.BytesBefore)
                    {
                        File.Delete(outPath);
                        r.BytesAfter = r.BytesBefore;
                        r.Notes.Add(Path.GetFileName(path) + " is already as small as it gets.");
                    }
                    else r.Outputs.Add(outPath);
                    return r;
                }
                case "media.mute":
                    return Out(await Media.Convert(path, "mp4", "Muted", false, true, dir, progress, ct));
                case "media.still":
                    return Out(await Media.Still(path, dir, o.Quality));
                case "file.zip":
                    return Out(await Task.Run(() => Zip(new[] { path }, dir), ct));
                case "file.unzip":
                    return Out(await Task.Run(() => Unzip(path, dir), ct));
                case "folder.jpg":
                case "folder.compress":
                {
                    var images = Inspector.ImagesIn(path);
                    if (images.Count == 0) throw new UserError("No pictures in " + Path.GetFileName(path) + ".");
                    var folder = Naming.Unique(Path.Combine(dir, Path.GetFileName(path) + (id == "folder.jpg" ? " (JPG)" : " (Compressed)")));
                    Directory.CreateDirectory(folder);
                    var r = new RunReport();
                    for (int i = 0; i < images.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            if (id == "folder.jpg")
                            {
                                using (var img = await Images.Load(images[i]))
                                    await Images.Save(img, Naming.Output(images[i], "jpg", null, folder), CoreFormat.Jpg, o.Quality);
                            }
                            else
                            {
                                var c = await CompressImage(images[i], o, folder);
                                r.BytesBefore += c.BytesBefore;
                                r.BytesAfter += c.BytesAfter;
                                if (c.Outputs.Count == 0) File.Copy(images[i], Naming.Unique(Path.Combine(folder, Path.GetFileName(images[i]))));
                            }
                        }
                        catch (Exception e) when (!(e is OperationCanceledException))
                        {
                            r.Failures.Add(Path.GetFileName(images[i]) + ": " + Message(e));
                        }
                        progress?.Report((i + 1) / (double)images.Count);
                    }
                    r.Outputs.Add(folder);
                    return r;
                }
            }
            throw new UserError(slice.Title + " isn't available for " + Path.GetFileName(path) + ".");
        }

        static double? DpiOf(Bitmap img) => img.HorizontalResolution > 72.5 && img.HorizontalResolution < 2400 ? img.HorizontalResolution : (double?)null;

        /// <summary>JPEG at the highest quality that fits, shrinking the picture only when it must.</summary>
        static byte[] FitJpeg(Bitmap img, int maxBytes, double? dpi)
        {
            double lastScale = -1;
            Bitmap scaled = null;
            try
            {
                return SizeLimit.Fit((scale, q) =>
                {
                    if (scale != lastScale)
                    {
                        scaled?.Dispose();
                        scaled = scale >= 1 ? Images.Flatten(img) : Images.Scale(img, (int)Math.Round(img.Width * scale), (int)Math.Round(img.Height * scale));
                        lastScale = scale;
                    }
                    return Images.Jpeg(scaled, q, dpi);
                }, img.Width, img.Height, maxBytes);
            }
            finally { scaled?.Dispose(); }
        }

        /// <summary>Smaller copy. JPG stays JPG; other formats become JPG unless they're see-through (then PNG).
        /// Never keeps a bigger file.</summary>
        static async Task<RunReport> CompressImage(string path, Options o, string dir)
        {
            var r = new RunReport { BytesBefore = Inspector.Size(path) };
            using (var img = await Images.Load(path, o.CompressMaxEdge))
            {
                bool clear = Images.HasTransparency(img);
                var format = clear ? CoreFormat.Png : CoreFormat.Jpg;
                var outPath = Naming.Output(path, format.Ext(), "Compressed", dir);
                await Images.Save(img, outPath, format, o.CompressQuality);
                r.BytesAfter = Inspector.Size(outPath);
                if (r.BytesAfter >= r.BytesBefore)
                {
                    File.Delete(outPath);
                    r.BytesAfter = r.BytesBefore;
                    r.Notes.Add(Path.GetFileName(path) + " is already as small as it gets.");
                }
                else r.Outputs.Add(outPath);
            }
            return r;
        }

        static async Task<string> SaveEdited(Bitmap img, string like, string suffix, string dir, double quality, CoreFormat? force = null)
        {
            var format = force ?? Formats.FromExt(Path.GetExtension(like)) ?? CoreFormat.Png;
            if (format == CoreFormat.Heic || format == CoreFormat.Gif) format = Images.HasTransparency(img) ? CoreFormat.Png : CoreFormat.Jpg;
            var outPath = Naming.Output(like, format.Ext(), suffix, dir);
            await Images.Save(img, outPath, format, quality);
            return outPath;
        }

        static Size? ParseSize(string s)
        {
            var p = s.Split('x');
            return p.Length == 2 && int.TryParse(p[0], out var w) && int.TryParse(p[1], out var h) && w > 0 && h > 0 ? new Size(w, h) : (Size?)null;
        }

        /// <summary>Text in the bottom-right corner, sized to the picture, readable on light and dark photos.</summary>
        static void Watermark(Bitmap img, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            using (var g = Images.Draw(img))
            using (var font = new Font("Segoe UI Semibold", (float)Math.Max(10, Math.Max(img.Width, img.Height) * 0.032), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                var size = g.MeasureString(text, font);
                float margin = (float)(Math.Max(img.Width, img.Height) * 0.025);
                var at = new PointF(img.Width - size.Width - margin, img.Height - size.Height - margin);
                using (var shadow = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                using (var ink = new SolidBrush(Color.FromArgb(215, 255, 255, 255)))
                {
                    float o = Math.Max(1, font.Size / 18);
                    g.DrawString(text, font, shadow, at.X + o, at.Y + o);
                    g.DrawString(text, font, ink, at);
                }
            }
        }

        // MARK: Zip

        /// <summary>One item → "Name.zip"; several → "Archive.zip" (like Explorer's "Compress to ZIP").</summary>
        static string Zip(IList<string> items, string dir)
        {
            var outPath = items.Count == 1
                ? Naming.Unique(Path.Combine(dir, Path.GetFileName(items[0].TrimEnd('\\')) + ".zip"))
                : Naming.Unique(Path.Combine(dir, "Archive.zip"));
            Naming.Atomically(outPath, tmp =>
            {
                using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
                {
                    foreach (var item in items)
                    {
                        if (Directory.Exists(item))
                        {
                            var root = Path.GetFileName(item.TrimEnd('\\'));
                            foreach (var file in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
                            {
                                var rel = root + "/" + file.Substring(item.TrimEnd('\\').Length + 1).Replace('\\', '/');
                                zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
                            }
                        }
                        else zip.CreateEntryFromFile(item, Path.GetFileName(item), CompressionLevel.Optimal);
                    }
                }
            });
            return outPath;
        }

        /// <summary>Extracts into a new folder named after the archive. Every entry must stay inside that folder (a
        /// "../" name is skipped), and a "zip bomb" (a small file that unpacks to fill the disk) is stopped: the bytes
        /// are counted as they're written, because the sizes a zip declares can lie.</summary>
        internal static string Unzip(string path, string dir, long maxBytes = MaxUnzippedBytes)
        {
            Directory.CreateDirectory(dir);
            var stage = Path.Combine(dir, ".dragshift-unzip-" + Guid.NewGuid().ToString("N"));
            var root = Path.GetFullPath(stage + Path.DirectorySeparatorChar);
            long free = long.MaxValue;
            try { free = new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace; } catch (Exception) { /* network drive: no figure */ }
            long budget = Math.Min(maxBytes, free - KeepFreeBytes);
            Directory.CreateDirectory(stage);
            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    if (zip.Entries.Count > MaxUnzippedFiles)
                        throw new UserError(Path.GetFileName(path) + " has " + zip.Entries.Count + " files inside. That's too many to unpack safely.");
                    var buffer = new byte[81920];
                    foreach (var e in zip.Entries)
                    {
                        var target = Path.GetFullPath(Path.Combine(stage, e.FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        if (e.FullName.EndsWith("/", StringComparison.Ordinal) || e.FullName.EndsWith("\\", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var src = e.Open())
                        using (var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                        {
                            int n;
                            while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                budget -= n;
                                if (budget < 0)
                                    throw new UserError(Path.GetFileName(path) + " unpacks to more than this disk can hold (or over 20 GB), so it was stopped.");
                                dst.Write(buffer, 0, n);
                            }
                        }
                    }
                }
                // Publish the complete folder in one move. If another action takes the name meanwhile,
                // choose another; never adopt or remove a folder that was not made by this extraction.
                while (true)
                {
                    var folder = Naming.Unique(Path.Combine(dir, Naming.Base(path)));
                    try
                    {
                        Directory.Move(stage, folder);
                        stage = null;
                        return folder;
                    }
                    catch (IOException)
                    {
                        if (!File.Exists(folder) && !Directory.Exists(folder)) throw;
                    }
                }
            }
            finally
            {
                if (stage != null)
                    try { Directory.Delete(stage, true); } catch (Exception) { /* nothing was made */ }
            }
        }

        const long MaxUnzippedBytes = 20L * 1024 * 1024 * 1024, KeepFreeBytes = 1L * 1024 * 1024 * 1024;
        const int MaxUnzippedFiles = 100000;

        // MARK: Recipes

        /// <summary>Runs a recipe's steps in order. Each file goes through the chain on its own, unless a step joins
        /// files (PDF, zip), then the group goes through together. Only the final file is kept: "Photo WhatsApp Small.jpg".</summary>
        public static async Task<RunReport> RunRecipe(Recipe recipe, IList<string> inputs, Options options, IProgress<double> progress, CancellationToken ct)
        {
            var report = new RunReport();
            if (recipe.Steps.Count == 0) { report.Failures.Add(recipe.Name + " has no steps yet."); return report; }
            var root = Path.Combine(Path.GetTempPath(), "dragshift-recipe-" + Guid.NewGuid().ToString("N"));
            try
            {
                bool grouped = inputs.Count > 1 && recipe.Steps.Any(Recipe.ManyToOne.Contains);
                var batches = grouped ? new List<List<string>> { inputs.OrderBy(p => Path.GetFileName(p), Naming.NaturalOrder).ToList() }
                                      : inputs.Select(p => new List<string> { p }).ToList();
                int total = batches.Count * recipe.Steps.Count, done = 0;
                for (int bi = 0; bi < batches.Count; bi++)
                {
                    var current = batches[bi];
                    bool failed = false;
                    for (int si = 0; si < recipe.Steps.Count; si++)
                    {
                        var step = recipe.Steps[si];
                        var stepDir = Path.Combine(root, bi + "-" + si);
                        Directory.CreateDirectory(stepDir);
                        var o = options.Copy();
                        o.OutputFolder = stepDir;
                        var r = await Run(new Slice(step, Recipe.StepTitle(step), "", Recipe.StepTitle(step)), current, o, null, ct);
                        if (r.Text != null) report.Text = r.Text;
                        report.Notes.AddRange(r.Notes.Where(n => !n.Contains("already")));
                        progress?.Report(++done / (double)total);
                        if (r.Failures.Count > 0)
                        {
                            report.Failures.AddRange(r.Failures.Select(f => recipe.Name + " · " + Recipe.StepTitle(step) + ": " + f));
                            failed = true;
                            break;
                        }
                        if (r.Outputs.Count > 0) current = r.Outputs; // "already small" just passes the file on
                    }
                    if (failed) continue;
                    var original = batches[bi][0];
                    var dir = Naming.FolderFor(original, options.OutputFolder);
                    for (int k = 0; k < current.Count; k++)
                    {
                        var file = current[k];
                        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        var suffix = current.Count > 1 ? recipe.Name + " " + (k + 1) : recipe.Name;
                        var dest = Naming.Output(original, Path.GetExtension(file).TrimStart('.'), suffix, dir);
                        File.Move(file, dest);
                        report.Outputs.Add(dest);
                    }
                    report.BytesBefore += batches[bi].Sum(Inspector.Size);
                }
                report.BytesAfter = report.Outputs.Sum(Inspector.Size);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { }
            }
            return report;
        }
    }
}
