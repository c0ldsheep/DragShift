using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DragShift.Core;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf.PdfDocument;

namespace DragShift.Engine
{
    /// <summary>PDF pages through Windows' own PDF renderer (the one Edge uses), and new PDFs through PdfWriter.</summary>
    static class Pdf
    {
        public static async Task<WinPdf> Open(string path)
        {
            try
            {
                return await WinPdf.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
            }
            catch (Exception e) when (e.HResult == unchecked((int)0x8007052B))
            {
                throw new UserError(Path.GetFileName(path) + " is password-protected. Open it in Edge and save a copy without the password first.");
            }
            catch (Exception)
            {
                throw new UserError(Path.GetFileName(path) + " isn't a readable PDF.");
            }
        }

        /// <summary>One page as pixels at <paramref name="dpi"/>, on white. <paramref name="points"/> = page size in 1/72 inch.</summary>
        public static async Task<Bitmap> Render(WinPdf doc, uint index, double dpi, Action<double, double> points = null)
        {
            using (var page = doc.GetPage(index))
            {
                var size = page.Size; // 1/96 inch, rotation applied
                if (double.IsNaN(size.Width) || double.IsNaN(size.Height) || double.IsInfinity(size.Width) || double.IsInfinity(size.Height)
                    || size.Width <= 0 || size.Height <= 0 || double.IsNaN(dpi) || double.IsInfinity(dpi) || dpi <= 0)
                    throw new UserError("This PDF page has an invalid image size.");
                double pixelWidth = Math.Max(1, Math.Round(size.Width / 96 * dpi)), pixelHeight = Math.Max(1, Math.Round(size.Height / 96 * dpi));
                Images.RequireSafeSize(pixelWidth, pixelHeight, "This PDF page");
                points?.Invoke(size.Width * 72 / 96, size.Height * 72 / 96);
                uint w = (uint)pixelWidth, h = (uint)pixelHeight;
                using (var ras = new InMemoryRandomAccessStream())
                {
                    await page.RenderToStreamAsync(ras, new PdfPageRenderOptions { DestinationWidth = w, DestinationHeight = h });
                    ras.Seek(0);
                    var dec = await BitmapDecoder.CreateAsync(ras);
                    Images.RequireSafeSize(dec.PixelWidth, dec.PixelHeight, "This PDF page");
                    var data = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                                                           ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                    using (var raw = Images.FromBgra(data.DetachPixelData(), (int)dec.PixelWidth, (int)dec.PixelHeight))
                        return Images.Flatten(raw);
                }
            }
        }

        /// <summary>Every page as a picture. One page → "Name.jpg"; more → a folder "Name (JPG)" with "Name 01.jpg"…</summary>
        public static async Task<List<string>> ToImages(string path, Core.ImageFormat format, double dpi, double quality, string dir,
                                                        IProgress<double> progress, CancellationToken ct)
        {
            var doc = await Open(path);
            var outs = new List<string>();
            string folder = dir;
            if (doc.PageCount > 1)
            {
                folder = Naming.Unique(Path.Combine(dir, Naming.Base(path) + " (" + format.Title() + ")"));
                Directory.CreateDirectory(folder);
            }
            int digits = Math.Max(2, doc.PageCount.ToString().Length);
            for (uint i = 0; i < doc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                using (var img = await Render(doc, i, dpi))
                {
                    var name = doc.PageCount > 1 ? Naming.Base(path) + " " + (i + 1).ToString().PadLeft(digits, '0') : Naming.Base(path);
                    var outPath = Naming.Unique(Path.Combine(folder, name + "." + format.Ext()));
                    await Images.Save(img, outPath, format, quality);
                    if (doc.PageCount == 1) outs.Add(outPath);
                }
                progress?.Report((i + 1) / (double)doc.PageCount);
            }
            if (doc.PageCount > 1) outs.Add(folder);
            return outs;
        }

        /// <summary>Pictures → one PDF. Pages get the picture's printed size when it has a DPI (scans come out A4-ish);
        /// photos are fitted to an A4-sized page.</summary>
        /// <summary>The print resolution the picture file itself states, or 0 when it states none that means anything
        /// (72 and 96 are just screens). The loaded bitmap can't be asked: a new bitmap takes this screen's dpi.</summary>
        static async Task<double> PrintDpi(string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using (var stream = await file.OpenReadAsync())
                {
                    var dec = await BitmapDecoder.CreateAsync(stream);
                    return dec.DpiX > 96.5 && dec.DpiX < 2400 ? dec.DpiX : 0;
                }
            }
            catch (Exception) { return 0; }
        }

        public static async Task MakeFromImages(IList<string> images, string outPath, double quality, IProgress<double> progress, CancellationToken ct)
        {
            var pages = new List<PdfWriter.Page>();
            for (int i = 0; i < images.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                using (var img = await Images.Load(images[i]))
                {
                    // A page is as big as the picture prints: a 300 dpi scan of an A4 sheet becomes an A4 page. Phone
                    // photos and screenshots state no real print size, so they get one point per pixel, at most
                    // A4's long side (like the Mac). Without that limit a phone photo became a page over a metre
                    // wide, and "Under KB" could not shrink the PDF enough.
                    double dpi = await PrintDpi(images[i]);
                    double w = img.Width, h = img.Height;
                    if (dpi > 0) { w = w * 72 / dpi; h = h * 72 / dpi; }
                    else
                    {
                        double s = Math.Min(1, 842 / Math.Max(w, h));
                        w *= s; h *= s;
                    }
                    pages.Add(new PdfWriter.Page
                    {
                        Jpeg = Images.Jpeg(img, quality), PixelWidth = img.Width, PixelHeight = img.Height, Width = w, Height = h,
                    });
                }
                progress?.Report((i + 1) / (double)images.Count);
            }
            var bytes = PdfWriter.Make(pages);
            Naming.Atomically(outPath, tmp => File.WriteAllBytes(tmp, bytes));
        }

        /// <summary>
        /// Redraws every page as a picture at the sharpest resolution and quality that fit under maxBytes.
        /// (Windows has no PDF rewriter, so text in the result can't be selected; the bubble says so.)
        /// </summary>
        public static async Task<byte[]> Fit(string path, int maxBytes, IProgress<double> progress, CancellationToken ct)
        {
            var doc = await Open(path);
            int n = (int)doc.PageCount;
            if (n == 0) throw new UserError(Path.GetFileName(path) + " has no pages.");
            var ladder = SizeLimit.PdfQualities;
            var dpis = SizeLimit.PdfDpis;
            for (int di = 0; di < dpis.Length; di++)
            {
                var totals = new int[ladder.Length];
                var pages = new List<PdfWriter.Page>[ladder.Length];
                for (int li = 0; li < ladder.Length; li++) { totals[li] = SizeLimit.PdfOverhead(n); pages[li] = new List<PdfWriter.Page>(); }
                for (uint pi = 0; pi < n; pi++)
                {
                    ct.ThrowIfCancellationRequested();
                    double pw = 0, ph = 0;
                    using (var img = await Render(doc, pi, dpis[di], (w, h) => { pw = w; ph = h; }))
                    {
                        // Scans and black-and-white pages: dropping the colour makes the JPEG noticeably smaller.
                        using (var pixels = Vision.IsGray(img) ? Vision.Desaturate(img) : new Bitmap(img))
                        {
                            for (int li = 0; li < ladder.Length; li++)
                            {
                                if (totals[li] > maxBytes) continue;
                                var jpeg = Images.Jpeg(pixels, ladder[li]);
                                totals[li] += jpeg.Length;
                                if (totals[li] <= maxBytes)
                                    pages[li].Add(new PdfWriter.Page
                                    {
                                        Jpeg = jpeg, PixelWidth = pixels.Width, PixelHeight = pixels.Height, Width = pw, Height = ph,
                                    });
                                else pages[li].Clear();
                            }
                        }
                    }
                    progress?.Report((di + (pi + 1) / (double)n) / dpis.Length);
                    if (Array.TrueForAll(totals, t => t > maxBytes)) break;
                }
                for (int li = 0; li < ladder.Length; li++)
                {
                    if (totals[li] > maxBytes) continue;
                    var bytes = PdfWriter.Make(pages[li]);
                    if (bytes.Length <= maxBytes) return bytes;
                }
            }
            throw new UserError("Couldn't get " + Path.GetFileName(path) + " under " + SizeLimit.Title(maxBytes / 1000) + ". Try a bigger limit.");
        }

        /// <summary>Text of every page, read off the rendered page (Windows can't pull text out of a PDF directly).</summary>
        public static async Task<string> Text(string path, IProgress<double> progress, CancellationToken ct)
        {
            var doc = await Open(path);
            var parts = new List<string>();
            for (uint i = 0; i < doc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                using (var img = await Render(doc, i, 200)) parts.Add(await Vision.ReadText(img));
                progress?.Report((i + 1) / (double)doc.PageCount);
            }
            return string.Join("\n\n", parts).Trim();
        }
    }
}
