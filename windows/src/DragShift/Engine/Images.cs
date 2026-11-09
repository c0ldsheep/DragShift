using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using DragShift.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using CoreFormat = DragShift.Core.ImageFormat;
using GdiFormat = System.Drawing.Imaging.ImageFormat;

namespace DragShift.Engine
{
    /// <summary>Opening and saving pictures. Windows' own decoders (WIC) read everything Photos can,
    /// including HEIC and WebP when their free Store extensions are installed; phone photos come out upright.</summary>
    static class Images
    {
        static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == GdiFormat.Jpeg.Guid);

        /// <summary>The same limit as the Mac app: 250 megapixels (a 200-megapixel phone photo still opens).</summary>
        public const long MaxPixels = 250000000;

        /// <summary>Upright pixels, colour-managed to sRGB. maxEdge > 0 shrinks big pictures after decoding.</summary>
        public static async Task<Bitmap> Load(string path, int maxEdge = 0)
        {
            Bitmap bmp;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using (var stream = await file.OpenReadAsync())
                {
                    var dec = await BitmapDecoder.CreateAsync(stream);
                    // A tiny file can claim a huge size ("picture bomb"): refuse it before any pixels are decoded.
                    long pixels = (long)dec.PixelWidth * dec.PixelHeight;
                    if (pixels > MaxPixels)
                        throw new UserError(Path.GetFileName(path) + " is " + pixels / 1000000 + " megapixels, too big to open safely.");
                    var data = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                                                           ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
                    bmp = FromBgra(data.DetachPixelData(), (int)dec.OrientedPixelWidth, (int)dec.OrientedPixelHeight);
                }
            }
            catch (Exception e) when (!(e is OutOfMemoryException) && !(e is UserError))
            {
                bmp = LoadWithGdi(path) ?? throw new UserError(Path.GetFileName(path) + " isn't a picture this PC can open."
                    + (Path.GetExtension(path).ToLowerInvariant().StartsWith(".hei") ? " Install \"HEIF Image Extensions\" from the Microsoft Store." : ""));
            }
            if (maxEdge > 0 && Math.Max(bmp.Width, bmp.Height) > maxEdge)
            {
                var small = Fit(bmp, maxEdge);
                bmp.Dispose();
                return small;
            }
            return bmp;
        }

        static Bitmap LoadWithGdi(string path)
        {
            try
            {
                using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                using (var img = Image.FromStream(ms))
                {
                    var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(bmp)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                    if (img.PropertyIdList.Contains(0x0112))
                    {
                        var o = BitConverter.ToUInt16(img.GetPropertyItem(0x0112).Value, 0);
                        var turn = o == 3 ? RotateFlipType.Rotate180FlipNone : o == 6 ? RotateFlipType.Rotate90FlipNone
                                 : o == 8 ? RotateFlipType.Rotate270FlipNone : o == 2 ? RotateFlipType.RotateNoneFlipX
                                 : o == 4 ? RotateFlipType.RotateNoneFlipY : o == 5 ? RotateFlipType.Rotate90FlipX
                                 : o == 7 ? RotateFlipType.Rotate270FlipX : RotateFlipType.RotateNoneFlipNone;
                        bmp.RotateFlip(turn);
                    }
                    return bmp;
                }
            }
            catch (ArgumentException) { return null; }
            catch (OutOfMemoryException) { return null; } // GDI+ says this for "not a picture"
            catch (ExternalException) { return null; }
        }

        public static Bitmap FromBgra(byte[] bgraPremultiplied, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(bgraPremultiplied, y * w * 4, d.Scan0 + y * d.Stride, w * 4);
            }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }

        /// <summary>BGRA bytes, premultiplied (what Windows' encoders, OCR and face finder take).</summary>
        public static byte[] Bgra(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var bytes = new byte[w * h * 4];
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, bytes, y * w * 4, w * 4);
            }
            finally { bmp.UnlockBits(d); }
            return bytes;
        }

        /// <summary>Straight (not premultiplied) 0xAARRGGBB, for the WebP writer.</summary>
        public static uint[] Argb(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var px = new int[w * h];
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w, w);
            }
            finally { bmp.UnlockBits(d); }
            var outPx = new uint[px.Length];
            for (int i = 0; i < px.Length; i++) outPx[i] = (uint)px[i] >> 24 == 0 ? 0 : (uint)px[i];
            return outPx;
        }

        public static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp) =>
            SoftwareBitmap.CreateCopyFromBuffer(Bgra(bmp).AsBuffer(), BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);

        // MARK: Pixels

        public static Bitmap Canvas(int w, int h, Color? fill = null)
        {
            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppPArgb);
            if (fill.HasValue) using (var g = Graphics.FromImage(bmp)) g.Clear(fill.Value);
            return bmp;
        }

        public static Graphics Draw(Bitmap bmp)
        {
            var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            return g;
        }

        public static Bitmap Scale(Bitmap src, int w, int h)
        {
            var bmp = Canvas(w, h);
            using (var g = Draw(bmp))
            using (var wrap = new ImageAttributes())
            {
                wrap.SetWrapMode(WrapMode.TileFlipXY); // no grey fringe at the edges
                g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);
            }
            return bmp;
        }

        public static Bitmap Fit(Bitmap src, int longEdge)
        {
            double s = Math.Min(1, longEdge / (double)Math.Max(src.Width, src.Height));
            return Scale(src, (int)Math.Round(src.Width * s), (int)Math.Round(src.Height * s));
        }

        /// <summary>Crops to the target shape around the middle, then scales (Instagram 4:5 and friends).</summary>
        public static Bitmap Fill(Bitmap src, int w, int h)
        {
            double target = w / (double)h, have = src.Width / (double)src.Height;
            var crop = have > target
                ? new RectangleF((float)((src.Width - src.Height * target) / 2), 0, (float)(src.Height * target), src.Height)
                : new RectangleF(0, (float)((src.Height - src.Width / target) / 2), src.Width, (float)(src.Width / target));
            var bmp = Canvas(w, h);
            using (var g = Draw(bmp)) g.DrawImage(src, new Rectangle(0, 0, w, h), crop, GraphicsUnit.Pixel);
            return bmp;
        }

        /// <summary>The whole picture inside a 9:16 story, on a blurred copy of itself.</summary>
        public static Bitmap FitStory(Bitmap src, int w, int h)
        {
            var bmp = Canvas(w, h, Color.Black);
            using (var blur = Fill(src, Math.Max(1, w / 24), Math.Max(1, h / 24)))
            using (var g = Draw(bmp))
            {
                g.DrawImage(blur, new Rectangle(-w / 20, -h / 20, w + w / 10, h + h / 10));
                using (var dim = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(dim, 0, 0, w, h);
                double s = Math.Min(w / (double)src.Width, h / (double)src.Height);
                int fw = (int)Math.Round(src.Width * s), fh = (int)Math.Round(src.Height * s);
                g.DrawImage(src, new Rectangle((w - fw) / 2, (h - fh) / 2, fw, fh));
            }
            return bmp;
        }

        public static Bitmap Turned(Bitmap src, RotateFlipType how)
        {
            var bmp = new Bitmap(src);
            bmp.RotateFlip(how);
            return bmp;
        }

        /// <summary>True only if some pixel is actually see-through (many PNGs carry an unused alpha channel).</summary>
        public static bool HasTransparency(Bitmap bmp)
        {
            if (!Image.IsAlphaPixelFormat(bmp.PixelFormat)) return false;
            using (var small = Fit(bmp, 512))
            {
                var px = Bgra(small);
                for (int i = 3; i < px.Length; i += 4) if (px[i] < 250) return true;
            }
            return false;
        }

        /// <summary>On a solid colour, 24-bit: what JPEG needs.</summary>
        public static Bitmap Flatten(Bitmap src, Color? bg = null)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(bg ?? Color.White);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return bmp;
        }

        // MARK: Saving

        /// <summary>JPEG bytes. No camera data or location is ever written.</summary>
        public static byte[] Jpeg(Bitmap bmp, double quality, double? dpi = null)
        {
            using (var flat = Flatten(bmp))
            using (var ms = new MemoryStream())
            using (var ep = new EncoderParameters(1))
            {
                if (dpi.HasValue) flat.SetResolution((float)dpi.Value, (float)dpi.Value);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Round(Num.Clamp(quality, 0.05, 1) * 100));
                flat.Save(ms, JpegCodec, ep);
                return ms.ToArray();
            }
        }

        public static async Task Save(Bitmap bmp, string path, CoreFormat format, double quality, double? dpi = null)
        {
            byte[] bytes;
            switch (format)
            {
                case CoreFormat.Jpg:
                    bytes = Jpeg(bmp, quality, dpi);
                    break;
                case CoreFormat.WebP:
                    bytes = WebPLossless.Encode(Argb(bmp), bmp.Width, bmp.Height);
                    break;
                case CoreFormat.Heic:
                    bytes = await EncodeWithWindows(bmp, BitmapEncoder.HeifEncoderId, quality);
                    break;
                default:
                {
                    var gdi = format == CoreFormat.Png ? GdiFormat.Png : format == CoreFormat.Tiff ? GdiFormat.Tiff
                            : format == CoreFormat.Bmp ? GdiFormat.Bmp : GdiFormat.Gif;
                    using (var ms = new MemoryStream())
                    {
                        if (format == CoreFormat.Bmp && HasTransparency(bmp) == false)
                            using (var flat = Flatten(bmp)) flat.Save(ms, gdi);
                        else
                            bmp.Save(ms, gdi);
                        bytes = ms.ToArray();
                    }
                    break;
                }
            }
            Naming.Atomically(path, tmp => File.WriteAllBytes(tmp, bytes));
        }

        static async Task<byte[]> EncodeWithWindows(Bitmap bmp, Guid encoderId, double quality)
        {
            using (var ras = new InMemoryRandomAccessStream())
            {
                BitmapEncoder enc;
                try
                {
                    var props = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue((float)quality, Windows.Foundation.PropertyType.Single) } };
                    enc = await BitmapEncoder.CreateAsync(encoderId, ras, props);
                }
                catch (Exception)
                {
                    enc = await BitmapEncoder.CreateAsync(encoderId, ras);
                }
                enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bmp.Width, (uint)bmp.Height, 96, 96, Bgra(bmp));
                await enc.FlushAsync();
                var bytes = new byte[ras.Size];
                using (var reader = new DataReader(ras.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync((uint)ras.Size);
                    reader.ReadBytes(bytes);
                }
                return bytes;
            }
        }

        /// <summary>Can this PC save HEIC? (Needs "HEIF Image Extensions" + "HEVC Video Extensions" from the Store.)</summary>
        public static async Task<bool> CanWriteHeic()
        {
            try
            {
                using (var tiny = Canvas(16, 16, Color.Gray)) return (await EncodeWithWindows(tiny, BitmapEncoder.HeifEncoderId, 0.8)).Length > 0;
            }
            catch (Exception) { return false; }
        }
    }
}
