using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DragShift.Core;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Media.Ocr;

namespace DragShift.Engine
{
    /// <summary>Windows' own on-device text reader and face finder. Nothing leaves the PC.</summary>
    static class Vision
    {
        public static bool OcrAvailable
        {
            get
            {
                try { return OcrEngine.TryCreateFromUserProfileLanguages() != null; }
                catch (Exception) { return false; }
            }
        }

        public static bool FacesAvailable
        {
            get
            {
                try { return FaceDetector.IsSupported; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Lines of text, top to bottom.</summary>
        public static async Task<string> ReadText(Bitmap img)
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? throw new UserError("Windows has no text-reading language yet: Settings → Time & language → Language → add one with \"Optical character recognition\".");
            int max = (int)OcrEngine.MaxImageDimension;
            using (var fitted = Math.Max(img.Width, img.Height) > max ? Images.Fit(img, max) : new Bitmap(img))
            using (var sb = Images.ToSoftwareBitmap(fitted))
            {
                var result = await engine.RecognizeAsync(sb);
                return string.Join("\n", result.Lines.Select(l => l.Text)).Trim();
            }
        }

        /// <summary>The biggest face (pixels, top-left origin).</summary>
        public static async Task<Box> Face(Bitmap img)
        {
            if (!FacesAvailable) throw new UserError("This PC can't look for faces (Windows 10 or newer is needed).");
            double scale = Math.Min(1, 1600.0 / Math.Max(img.Width, img.Height));
            using (var small = scale < 1 ? Images.Fit(img, 1600) : new Bitmap(img))
            using (var bgra = Images.ToSoftwareBitmap(small))
            using (var gray = SoftwareBitmap.Convert(bgra, BitmapPixelFormat.Gray8))
            {
                var detector = await FaceDetector.CreateAsync();
                var faces = await detector.DetectFacesAsync(gray);
                var best = faces.OrderByDescending(f => (double)f.FaceBox.Width * f.FaceBox.Height).FirstOrDefault()
                    ?? throw new UserError("Couldn't find a face. Use a photo where the face is clear and looking at the camera.");
                double k = small.Width / (double)img.Width;
                var b = best.FaceBox;
                return new Box(b.X / k, b.Y / k, b.Width / k, b.Height / k);
            }
        }

        // MARK: Passport photo

        public sealed class PhotoResult
        {
            public Bitmap Image;
            public string Note;
        }

        static Color Fill(PhotoBackground bg) => bg == PhotoBackground.LightBlue ? Color.FromArgb(217, 235, 255) : Color.White;

        /// <summary>Crops around the face to the spec's shape and size; a plain wall behind the person is
        /// turned into the chosen colour. <paramref name="known"/> skips face finding (self-test).</summary>
        public static async Task<PhotoResult> Passport(Bitmap img, PhotoSpec spec, Box? known = null)
        {
            var face = known ?? await Face(img);
            var frame = Core.Passport.Frame(face, spec);
            int outW = spec.PixelWidth, outH = spec.PixelHeight;
            double scale = outH / frame.Height;
            var fill = Fill(spec.Background);
            var photo = Images.Canvas(outW, outH, fill);
            using (var g = Images.Draw(photo))
            {
                var dest = new RectangleF((float)(-frame.X * scale), (float)(-frame.Y * scale), (float)(img.Width * scale), (float)(img.Height * scale));
                g.DrawImage(img, dest);
            }
            string note = null;
            if (spec.Background != PhotoBackground.Keep)
            {
                var faceInPhoto = new Box((face.X - frame.X) * scale, (face.Y - frame.Y) * scale, face.Width * scale, face.Height * scale);
                // The part of the photo the original picture covers (the rest is already the fill colour).
                int vx0 = Num.Clamp((int)Math.Ceiling(-frame.X * scale), 0, outW - 1), vy0 = Num.Clamp((int)Math.Ceiling(-frame.Y * scale), 0, outH - 1);
                int vx1 = Num.Clamp((int)Math.Floor((img.Width - frame.X) * scale) - 1, vx0, outW - 1);
                int vy1 = Num.Clamp((int)Math.Floor((img.Height - frame.Y) * scale) - 1, vy0, outH - 1);
                if (!ReplacePlainWall(photo, faceInPhoto, fill, Rectangle.FromLTRB(vx0, vy0, vx1 + 1, vy1 + 1)))
                    note = "Background kept: it isn't plain enough to change safely. A plain wall behind you works best.";
            }
            return new PhotoResult { Image = photo, Note = note };
        }

        /// <summary>
        /// Grows the background in from the top and sides, stopping at edges and never entering the face.
        /// Works for the usual "standing in front of a wall" photo; a busy background is left alone (returns false).
        /// </summary>
        public static bool ReplacePlainWall(Bitmap photo, Box face, Color fill, Rectangle visible)
        {
            int w = photo.Width, h = photo.Height;
            var px = Images.Bgra(photo); // opaque canvas, so premultiplied == straight
            var mask = new bool[w * h];
            var queue = new Queue<int>();
            int chin = Num.Clamp((int)face.Bottom, 0, h - 1);
            // The face and a margin around it are never background.
            int fx0 = Num.Clamp((int)(face.X - face.Width * 0.08), 0, w - 1), fx1 = Num.Clamp((int)(face.Right + face.Width * 0.08), 0, w - 1);
            int fy0 = Num.Clamp((int)face.Y, 0, h - 1);
            bool Protected(int x, int y) => x >= fx0 && x <= fx1 && y >= fy0;

            // Outside the original picture is already the fill colour.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (!visible.Contains(x, y)) mask[y * w + x] = true;

            // Reference colour: the top edge of the picture, where the wall almost always shows.
            long sr = 0, sg = 0, sb = 0; int sn = 0;
            for (int x = visible.Left; x < visible.Right; x++)
            {
                int o = (visible.Top * w + x) * 4;
                sb += px[o]; sg += px[o + 1]; sr += px[o + 2]; sn++;
            }
            int rr = (int)(sr / sn), rg = (int)(sg / sn), rb = (int)(sb / sn);

            int Dist(int o, int r, int g, int b) => Math.Abs(px[o + 2] - r) + Math.Abs(px[o + 1] - g) + Math.Abs(px[o] - b);
            void Seed(int x, int y)
            {
                int i = y * w + x;
                if (mask[i] || Protected(x, y) || Dist(i * 4, rr, rg, rb) > 90) return;
                mask[i] = true;
                queue.Enqueue(i);
            }
            for (int x = visible.Left; x < visible.Right; x++) Seed(x, visible.Top);
            for (int y = visible.Top; y <= Math.Min(chin, visible.Bottom - 1); y++) { Seed(visible.Left, y); Seed(visible.Right - 1, y); }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % w, y = i / w, o = i * 4;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (mask[j] || Protected(nx, ny)) continue;
                    int p = j * 4;
                    // Small step from the neighbour (no edge crossed) and still wall-coloured overall.
                    if (Dist(p, px[o + 2], px[o + 1], px[o]) > 24 || Dist(p, rr, rg, rb) > 120) continue;
                    mask[j] = true;
                    queue.Enqueue(j);
                }
            }

            int filled = 0, inside = 0;
            for (int y = visible.Top; y < visible.Bottom; y++)
                for (int x = visible.Left; x < visible.Right; x++) { inside++; if (mask[y * w + x]) filled++; }
            double share = filled / (double)Math.Max(1, inside);
            if (share < 0.12 || share > 0.8) return false; // busy background, or it leaked into the person

            // Soft edge: each pixel takes the share of background around it (3×3).
            var outPx = (byte[])px.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int count = 0, bgCount = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            count++;
                            if (mask[yy * w + xx]) bgCount++;
                        }
                    if (bgCount == 0) continue;
                    double a = mask[y * w + x] ? 1 : bgCount / (double)count * 0.6;
                    int o = (y * w + x) * 4;
                    outPx[o] = (byte)(px[o] * (1 - a) + fill.B * a);
                    outPx[o + 1] = (byte)(px[o + 1] * (1 - a) + fill.G * a);
                    outPx[o + 2] = (byte)(px[o + 2] * (1 - a) + fill.R * a);
                }
            var d = photo.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { for (int y = 0; y < h; y++) Marshal.Copy(outPx, y * w * 4, d.Scan0 + y * d.Stride, w * 4); }
            finally { photo.UnlockBits(d); }
            return true;
        }

        // MARK: Grey pages

        /// <summary>Under 0.25% coloured pixels = a black-and-white page.</summary>
        public static bool IsGray(Bitmap img)
        {
            using (var small = Images.Fit(img, 200))
            {
                var px = Images.Bgra(small);
                int colored = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    int b = px[i], g = px[i + 1], r = px[i + 2];
                    if (Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(g - b), Math.Abs(r - b))) > 24) colored++;
                }
                return colored * 400 < px.Length / 4;
            }
        }

        public static Bitmap Desaturate(Bitmap img)
        {
            var bmp = Images.Canvas(img.Width, img.Height);
            using (var g = Graphics.FromImage(bmp))
            using (var attrs = new ImageAttributes())
            {
                var m = new ColorMatrix(new[]
                {
                    new[] { 0.299f, 0.299f, 0.299f, 0, 0 }, new[] { 0.587f, 0.587f, 0.587f, 0, 0 },
                    new[] { 0.114f, 0.114f, 0.114f, 0, 0 }, new[] { 0f, 0, 0, 1, 0 }, new[] { 0f, 0, 0, 0, 1 },
                });
                attrs.SetColorMatrix(m);
                g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
            }
            return bmp;
        }
    }
}
