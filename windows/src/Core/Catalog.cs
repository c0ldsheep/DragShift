using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragShift.Core
{
    /// <summary>What this PC can do. Some formats need a free Microsoft Store extension (HEIC),
    /// and text reading needs a language with OCR installed.</summary>
    public sealed class Capabilities
    {
        bool heicWrite;
        public event Action HeicWriteChanged;
        public bool HeicWrite
        {
            get => heicWrite;
            set
            {
                if (heicWrite == value) return;
                heicWrite = value;
                HeicWriteChanged?.Invoke();
            }
        }
        public bool Ocr { get; set; } = true;
        public bool Faces { get; set; } = true;
        public bool Media { get; set; } = true;

        public static Capabilities All => new Capabilities { HeicWrite = true };
    }

    /// <summary>Which slices show for which files. Same ids as the Mac version, so favourites,
    /// recipes and docs carry over.</summary>
    public static class Catalog
    {
        public const int MaxSlices = 8;

        // Segoe MDL2 Assets / Segoe Fluent Icons (same code points on Windows 10 and 11).
        public static class Glyph
        {
            public const string Photo = "", Picture = "", Pdf = "", Text = "", Globe = "";
            public const string Camera = "", Page = "", Rotate = "", Resize = "", Shrink = "";
            public const string Size = "", Person = "", Flip = "", Tag = "", Video = "";
            public const string Audio = "", Mute = "", Zip = "", Folder = "", Copy = "";
            public const string Share = "", Link = "", FolderOpen = "", Bolt = "", Star = "";
            public const string StarFill = "", Slides = "", Refresh = "";
            public const string Print = "", More = "";
            public const string Rows = "\uE8FD"; // BulletedList: rows stacked, rather than ViewAll's grid.
        }

        static Slice ToImage(ImageFormat f)
        {
            string g;
            switch (f)
            {
                case ImageFormat.Jpg: g = Glyph.Photo; break;
                case ImageFormat.Png: g = Glyph.Picture; break;
                case ImageFormat.WebP: g = Glyph.Globe; break;
                case ImageFormat.Heic: g = Glyph.Camera; break;
                case ImageFormat.Gif: g = Glyph.Slides; break;
                default: g = Glyph.Page; break;
            }
            return new Slice("img.to." + f.Ext(), f.Title(), g, "Converting to " + f.Title());
        }

        static Slice Media(string ext, string title, string glyph) =>
            new Slice("media.to." + ext, title, glyph, "Converting to " + title);

        // Pictures
        public static readonly Slice ImgPdf = new Slice("img.to.pdf", "PDF", Glyph.Pdf, "Converting to PDF");
        public static readonly Slice Ocr = new Slice("img.to.text", "Text", Glyph.Text, "Reading text");
        public static readonly Slice FitKB = new Slice("fit.kb", "Under KB", Glyph.Size, "Fitting under a size", Editor.FitKB);
        public static readonly Slice Passport = new Slice("img.passport", "Passport", Glyph.Person, "Making a passport photo");
        public static readonly Slice Compress = new Slice("img.compress", "Compress", Glyph.Shrink, "Compressing");
        public static readonly Slice Resize = new Slice("img.resize", "Resize", Glyph.Resize, "Resizing", Editor.Resize);
        public static readonly Slice Rotate = new Slice("img.rotate", "Rotate", Glyph.Rotate, "Rotating");
        public static readonly Slice Flip = new Slice("img.flip", "Flip", Glyph.Flip, "Flipping");
        public static readonly Slice StitchAll = new Slice("img.stitch", "Stitch", Glyph.Rows, "Stitching into one long picture");
        public static readonly Slice Watermark = new Slice("img.watermark", "Watermark", Glyph.Tag, "Adding the watermark");

        // PDFs
        public static readonly Slice PdfJpg = new Slice("pdf.to.jpg", "JPG", Glyph.Photo, "Saving pages as JPG");
        public static readonly Slice PdfPng = new Slice("pdf.to.png", "PNG", Glyph.Picture, "Saving pages as PNG");
        public static readonly Slice PdfText = new Slice("pdf.to.txt", "Text", Glyph.Text, "Reading text");
        public static readonly Slice PdfCompress = new Slice("pdf.compress", "Compress", Glyph.Shrink, "Compressing");

        // Video and audio
        public static readonly Slice MediaCompress = new Slice("media.compress", "Compress", Glyph.Shrink, "Compressing");
        public static readonly Slice Mute = new Slice("media.mute", "Mute", Glyph.Mute, "Removing sound");
        public static readonly Slice Still = new Slice("media.still", "Still", Glyph.Camera, "Saving a still");

        // Files and folders
        public static readonly Slice Zip = new Slice("file.zip", "Zip", Glyph.Zip, "Zipping");
        public static readonly Slice Unzip = new Slice("file.unzip", "Extract", Glyph.Folder, "Extracting");
        public static readonly Slice FolderJpg = new Slice("folder.jpg", "All → JPG", Glyph.Photo, "Converting every image to JPG");
        public static readonly Slice FolderPdf = new Slice("folder.pdf", "Images → PDF", Glyph.Pdf, "Putting the images in one PDF");
        public static readonly Slice FolderShrink = new Slice("folder.compress", "Shrink All", Glyph.Shrink, "Compressing every image");

        /// <summary>Opens the print window (printer, saved presets, preview); a preset itself is print.preset.&lt;id&gt;.</summary>
        public static readonly Slice Print = new Slice("file.print", "Print", Glyph.Print, "Printing", Editor.Print);
        /// <summary>The last slice of the Print page: the print window, for everything the presets don't cover.</summary>
        public static readonly Slice PrintMore = new Slice("file.print", "More…", Glyph.More, "Printing", Editor.Print);

        /// <summary>Files Windows can print here: pictures and PDFs.</summary>
        public static bool CanPrint(string path)
        {
            var k = Inspector.Kind(path);
            return k == FileKind.Image || k == FileKind.Pdf;
        }

        public static readonly RingPage FilesPage = new RingPage("share", "Files", new[]
        {
            new Slice("share.copy", "Copy", Glyph.Copy, "Copied", share: true),
            new Slice("share.share", "Share", Glyph.Share, "Share", share: true),
            Zip,
            new Slice("share.path", "Copy Path", Glyph.Link, "Copied the path", share: true),
            new Slice("share.reveal", "Show", Glyph.FolderOpen, "Shown in Explorer", share: true),
        });

        /// <summary>Same idea for different file types, so one favourite ("Compress") works for all.</summary>
        public static readonly string[][] Families =
        {
            new[] { "img.compress", "pdf.compress", "media.compress", "folder.compress" },
            new[] { "img.to.jpg", "pdf.to.jpg", "folder.jpg" },
            new[] { "img.to.png", "pdf.to.png" },
            new[] { "img.to.pdf", "folder.pdf" },
            new[] { "img.to.text", "pdf.to.txt" },
        };

        public static readonly string[] DefaultFavorites =
        {
            "img.to.jpg", "img.to.pdf", "fit.kb", "img.passport", "img.compress", "recipe.whatsapp", "file.zip", "share.share",
        };

        /// <summary>Pages for these files. Favourites (slice or recipe ids) become the first page,
        /// showing only the ones that fit these files; hidden slices are left out.</summary>
        public static List<RingPage> Pages(IList<string> paths, Capabilities caps, ICollection<string> hidden = null,
                                           IList<string> favorites = null, IList<Recipe> recipes = null,
                                           IList<PrintPreset> printPresets = null)
        {
            var pages = BasePages(paths, caps);
            var faves = FavoriteSlices(favorites ?? new string[0], pages.SelectMany(p => p.Slices).ToList(), paths,
                                       recipes ?? Recipe.BuiltIns, printPresets);
            var hide = hidden ?? new HashSet<string>();
            pages = pages.Select(p => new RingPage(p.Id, p.Title, p.Slices.Where(s => !hide.Contains(s.Id))))
                         .Where(p => p.Slices.Count > 0).ToList();
            // Print page: drop a file on a preset and it prints. Hidden presets stay off it.
            var shown = (printPresets ?? new PrintPreset[0]).Where(p => !hide.Contains(p.StepId)).ToList();
            if (shown.Count > 0 && paths.Count > 0 && paths.All(CanPrint))
            {
                var slices = shown.Take(MaxSlices - 1).Select(p => p.Slice).ToList();
                if (!hide.Contains(PrintMore.Id)) slices.Add(PrintMore);
                var files = pages.FindIndex(p => p.Id == FilesPage.Id);
                pages.Insert(files < 0 ? pages.Count : files, new RingPage("print", "Print", slices));
            }
            if (faves.Count > 0) pages.Insert(0, new RingPage("favorites", "Favorites", faves));
            return pages;
        }

        /// <summary>Favourites that apply to these files, in the user's order (at most eight).</summary>
        public static List<Slice> FavoriteSlices(IList<string> favorites, IList<Slice> available, IList<string> paths,
                                                 IList<Recipe> recipes, IList<PrintPreset> printPresets = null)
        {
            var byId = new Dictionary<string, Slice>();
            foreach (var s in available) if (!byId.ContainsKey(s.Id)) byId[s.Id] = s;
            var kind = Inspector.Kind(paths);
            var outList = new List<Slice>();
            var seen = new HashSet<string>();
            foreach (var f in favorites)
            {
                Slice found = null;
                if (f.StartsWith("recipe.", StringComparison.Ordinal))
                {
                    var r = recipes.FirstOrDefault(x => x.Id == f);
                    if (r != null && r.Kinds.Contains(kind)) found = r.Slice;
                }
                else if (byId.ContainsKey(f))
                {
                    found = byId[f];
                }
                else if (SizeLimit.KbFromStep(f) is int kb)
                {
                    if (kind == FileKind.Image || kind == FileKind.Pdf) found = SizeLimit.Slice(kb);
                }
                else if (PrintPreset.IdFromStep(f) is string pid)
                {
                    var preset = printPresets?.FirstOrDefault(p => p.Id == pid);
                    if (preset != null && paths.Count > 0 && paths.All(CanPrint)) found = preset.Slice;
                }
                else
                {
                    var fam = Families.FirstOrDefault(x => x.Contains(f));
                    if (fam != null) found = fam.Where(byId.ContainsKey).Select(id => byId[id]).FirstOrDefault();
                }
                if (found != null && seen.Add(found.Id)) outList.Add(found);
                if (outList.Count == MaxSlices) break;
            }
            return outList;
        }

        public static List<RingPage> BasePages(IList<string> paths, Capabilities caps)
        {
            var kind = Inspector.Kind(paths);
            bool many = paths.Count > 1;
            var pages = new List<RingPage>();
            void Page(string id, string title, IEnumerable<Slice> slices)
            {
                var list = slices.Take(MaxSlices).ToList();
                if (list.Count > 0) pages.Add(new RingPage(id, title, list));
            }

            switch (kind)
            {
                case FileKind.Image:
                {
                    var own = new HashSet<ImageFormat>(paths.Select(p => Formats.FromExt(Path.GetExtension(p)))
                                                            .Where(f => f.HasValue).Select(f => f.Value));
                    var targets = new List<ImageFormat> { ImageFormat.Jpg, ImageFormat.Png, ImageFormat.WebP };
                    if (caps.HeicWrite) targets.Add(ImageFormat.Heic);
                    targets.Add(ImageFormat.Tiff);
                    targets.Add(ImageFormat.Bmp);
                    var convert = targets.Where(f => !(own.Count == 1 && own.Contains(f))).Select(ToImage).ToList();
                    convert.Insert(Math.Min(3, convert.Count), many
                        ? new Slice("img.to.pdf", "PDF", Glyph.Pdf, "Putting the images in one PDF")
                        : ImgPdf);
                    if (caps.Ocr) convert.Add(Ocr);
                    Page("convert", "Convert", convert);
                    var edit = new List<Slice> { FitKB };
                    if (caps.Faces) edit.Add(Passport);
                    edit.AddRange(new[] { Compress, Resize, Rotate, Flip, Watermark });
                    if (many) edit.Insert(1, StitchAll); // several pictures into one long one
                    Page("edit", "Edit", edit);
                    break;
                }
                case FileKind.Pdf:
                {
                    var convert = new List<Slice> { PdfJpg, PdfPng };
                    if (caps.Ocr) convert.Add(PdfText);
                    convert.Add(FitKB);
                    convert.Add(PdfCompress);
                    Page("convert", "PDF", convert);
                    break;
                }
                case FileKind.Video:
                {
                    if (!caps.Media) break;
                    var own = paths[0].Split('.').Last().ToLowerInvariant();
                    var convert = new[]
                    {
                        Media("mp4", "MP4", Glyph.Video), Media("m4a", "M4A", Glyph.Audio),
                        Media("mp3", "MP3", Glyph.Audio), Media("wav", "WAV", Glyph.Audio),
                    }.Where(s => !s.Id.EndsWith("." + own, StringComparison.Ordinal)).ToList();
                    convert.AddRange(new[] { MediaCompress, Mute, Still });
                    Page("convert", "Video", convert);
                    break;
                }
                case FileKind.Audio:
                {
                    if (!caps.Media) break;
                    var own = paths[0].Split('.').Last().ToLowerInvariant();
                    Page("convert", "Audio", new[]
                    {
                        Media("m4a", "M4A", Glyph.Audio), Media("mp3", "MP3", Glyph.Audio), Media("wav", "WAV", Glyph.Audio),
                    }.Where(s => !s.Id.EndsWith("." + own, StringComparison.Ordinal)));
                    break;
                }
                case FileKind.Archive:
                    Page("files", "Zip", new[] { Unzip });
                    break;
                case FileKind.Folder:
                    Page("folder", "Folder", new[] { Zip, FolderJpg, FolderPdf, FolderShrink });
                    break;
            }
            var shown = new HashSet<string>(pages.SelectMany(p => p.Slices).Select(s => s.Id));
            // Pictures and PDFs get Print on the Files page, after Share.
            var files = FilesPage.Slices.ToList();
            if (paths.Count > 0 && paths.All(CanPrint)) files.Insert(2, Print);
            Page("share", "Files", files.Where(s => !shown.Contains(s.Id)));
            return pages;
        }

        /// <summary>Every slice that can ever appear, for Settings.</summary>
        public static List<Slice> Everything(Capabilities caps)
        {
            var seen = new HashSet<string>();
            var all = new List<Slice>();
            var samples = new[] { "a.jpg", "a.png", "a.pdf", "a.mp4", "a.mp3", "a.zip" };
            foreach (var s in samples)
            {
                foreach (var set in new[] { new[] { s }, new[] { s, s } })
                    foreach (var p in BasePages(set, caps))
                        foreach (var slice in p.Slices)
                            if (seen.Add(slice.Id)) all.Add(slice);
            }
            return all;
        }
    }
}
