using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using DragShift.Core;
using G = DragShift.Core.Geometry;

namespace DragShift
{
    sealed class RingModel
    {
        public enum RingMode { Drag, Pinned, Click }

        public List<RingPage> Pages = new List<RingPage>();
        public int Page;
        public int? Hovered, HoveredTab;
        public string Title = "", Subtitle = "";
        public Image Thumbnail;
        public RingMode Mode = RingMode.Drag;
        /// <summary>Set by double-tap: letting go anywhere repeats this.</summary>
        public Slice Armed;
        /// <summary>"Your usual" for these files (a slice id): it gets a soft pulsing edge. Pulse is the breath, 0..1.</summary>
        public string Usual;
        public double Pulse = 1;

        public List<Slice> Slices => Page >= 0 && Page < Pages.Count ? Pages[Page].Slices : new List<Slice>();
        public Slice HoveredSlice => Hovered.HasValue && Hovered.Value < Slices.Count ? Slices[Hovered.Value] : null;

        /// <summary>"Under 50 KB" on Favorites counts as the Under KB slice.</summary>
        public bool IsUsual(Slice s) => Usual != null && Habits.SliceId(s.Id) == Usual;

        /// <summary>When the usual isn't on this page, the first page that has it glows instead.</summary>
        public int? UsualTab
        {
            get
            {
                if (Usual == null || Slices.Any(IsUsual)) return null;
                int i = Pages.FindIndex(p => p.Slices.Any(IsUsual));
                return i >= 0 ? i : (int?)null;
            }
        }

        /// <summary>True while something on screen breathes, so the ring keeps redrawing.</summary>
        public bool Glowing => Armed == null && (UsualTab != null || Slices.Where((s, i) => i != Hovered).Any(IsUsual));
    }

    /// <summary>Draws the ring in ring units (440 × 440, see Geometry). Same look as the Mac ring.</summary>
    static class RingPainter
    {
        public static readonly Color HotA = Color.FromArgb(255, 140, 56), HotB = Color.FromArgb(255, 61, 110);
        /// <summary>Text on the hot gradient: dark ink (white on orange is 2.3:1; this is 5.2–7.7:1), as on the Mac.</summary>
        public static readonly Color OnHot = Color.FromArgb(36, 19, 28);
        /// <summary>The coffee cup: one flat golden yellow (#FFDD00), no gradient and no shine.</summary>
        public static readonly Color Gold = Color.FromArgb(255, 221, 0), GoldRim = Color.FromArgb(204, 163, 0);

        static readonly string IconFont = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        static readonly string TextFont = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

        static bool FontExists(string name)
        {
            try { using (var test = new FontFamily(name)) return test.Name == name; }
            catch (ArgumentException) { return false; }
        }

        public static Font Icon(float px) => new Font(IconFont, px, FontStyle.Regular, GraphicsUnit.Pixel);
        public static Font Text(float px, FontStyle style = FontStyle.Regular) => new Font(TextFont, px, style, GraphicsUnit.Pixel);

        struct Palette
        {
            public Color Disc, Rim, Slice, Ink, Secondary, Hub, Tab;
        }

        static Palette Colors(bool dark) => dark
            ? new Palette
            {
                Disc = Color.FromArgb(242, 30, 30, 34), Rim = Color.FromArgb(46, 255, 255, 255), Slice = Color.FromArgb(34, 255, 255, 255),
                Ink = Color.FromArgb(235, 255, 255, 255), Secondary = Color.FromArgb(150, 255, 255, 255),
                Hub = Color.FromArgb(250, 50, 50, 56), Tab = Color.FromArgb(235, 52, 52, 58),
            }
            : new Palette
            {
                Disc = Color.FromArgb(238, 232, 232, 237), Rim = Color.FromArgb(160, 255, 255, 255), Slice = Color.FromArgb(235, 255, 255, 255),
                Ink = Color.FromArgb(205, 0, 0, 0), Secondary = Color.FromArgb(130, 0, 0, 0),
                Hub = Color.FromArgb(252, 250, 250, 252), Tab = Color.FromArgb(235, 250, 250, 252),
            };

        public static void Paint(Graphics g, RingModel m, bool dark)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var c = Colors(dark);
            float cx = (float)G.CenterX, cy = (float)G.CenterY, disc = (float)G.Disc;

            // Soft shadow under the disc.
            using (var path = new GraphicsPath())
            {
                float r = disc + 26;
                path.AddEllipse(cx - r, cy - r + 12, r * 2, r * 2);
                using (var shadow = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(dark ? 110 : 70, 0, 0, 0),
                    SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) },
                    FocusScales = new PointF(0.82f, 0.82f),
                })
                    g.FillPath(shadow, path);
            }
            using (var b = new SolidBrush(c.Disc)) g.FillEllipse(b, cx - disc, cy - disc, disc * 2, disc * 2);
            using (var p = new Pen(c.Rim, 1)) g.DrawEllipse(p, cx - disc + 0.5f, cy - disc + 0.5f, disc * 2 - 1, disc * 2 - 1);

            var slices = m.Slices;
            PaintPetals(g, m, c, slices.Count);
            for (int i = 0; i < slices.Count; i++) PaintSlice(g, m, c, i, slices.Count, slices[i]);
            PaintHub(g, m, c);
            if (m.Pages.Count > 1) PaintTabs(g, m, c);
            PaintHint(g, m, c);
        }

        /// <summary>An annular slice with rounded corners: drawn inset by the corner radius, then stroked
        /// with a round join of the same width, which rounds every corner exactly.</summary>
        public static GraphicsPath Petal(double start, double end, double inner, double outer, double gap = 3, double corner = 9)
        {
            double cx = G.CenterX, cy = G.CenterY;
            double ro = outer - corner, ri = inner + corner, off = gap / 2 + corner;
            double Inset(double r) => Math.Asin(Math.Min(1, off / r)) * 180 / Math.PI;
            double a1o = start + Inset(ro), a2o = end - Inset(ro), a1i = start + Inset(ri), a2i = end - Inset(ri);
            var path = new GraphicsPath();
            if (a2o <= a1o || a2i <= a1i) return path;
            // GDI+ angles: 0 = +x, clockwise. Ours: 0 = up, clockwise.
            path.AddArc((float)(cx - ro), (float)(cy - ro), (float)(ro * 2), (float)(ro * 2), (float)(a1o - 90), (float)(a2o - a1o));
            path.AddArc((float)(cx - ri), (float)(cy - ri), (float)(ri * 2), (float)(ri * 2), (float)(a2i - 90), (float)-(a2i - a1i));
            path.CloseFigure();
            return path;
        }

        /// <summary>The quiet slices go on one layer, drawn solid and then faded as a whole, so the fill and
        /// the rounding stroke don't add up to a darker edge. The hot slice is drawn on top.</summary>
        static void PaintPetals(Graphics g, RingModel m, Palette c, int n)
        {
            if (n == 0) return;
            float s = Math.Max(0.5f, g.Transform.Elements[0]);
            int size = (int)Math.Ceiling(G.Size * s);
            using (var layer = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var lg = Graphics.FromImage(layer))
                using (var brush = new SolidBrush(Color.FromArgb(255, c.Slice)))
                using (var pen = new Pen(brush, 18) { LineJoin = LineJoin.Round })
                {
                    lg.SmoothingMode = SmoothingMode.AntiAlias;
                    lg.ScaleTransform(s, s);
                    double span = 360.0 / n;
                    for (int i = 0; i < n; i++)
                    {
                        if (m.Hovered == i && m.Armed == null) continue;
                        using (var petal = Petal(i * span - span / 2, i * span + span / 2, G.Inner, G.Outer))
                        {
                            lg.FillPath(brush, petal);
                            lg.DrawPath(pen, petal);
                        }
                    }
                }
                float alpha = c.Slice.A / 255f * (m.Armed == null ? 1 : 0.35f);
                using (var attrs = new System.Drawing.Imaging.ImageAttributes())
                {
                    attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha });
                    g.DrawImage(layer, new Rectangle(0, 0, (int)G.Size, (int)G.Size), 0, 0, size, size, GraphicsUnit.Pixel, attrs);
                }
            }
            if (m.Armed == null)
                for (int i = 0; i < n; i++)
                    if (i != m.Hovered && m.IsUsual(m.Slices[i])) PaintUsualEdge(g, m, i, n);
            if (m.Hovered is int hot && hot < n && m.Armed == null)
            {
                double span = 360.0 / n;
                var state = g.Save();
                g.TranslateTransform((float)G.CenterX, (float)G.CenterY);
                g.ScaleTransform(1.045f, 1.045f);
                g.TranslateTransform((float)-G.CenterX, (float)-G.CenterY);
                using (var petal = Petal(hot * span - span / 2, hot * span + span / 2, G.Inner, G.Outer))
                {
                    var bounds = petal.GetBounds();
                    bounds.Inflate(10, 10);
                    for (int k = 4; k >= 1; k--) // pink glow
                        using (var glow = new Pen(Color.FromArgb(18, HotB), 18 + k * 6) { LineJoin = LineJoin.Round })
                            g.DrawPath(glow, petal);
                    using (var brush = new LinearGradientBrush(bounds, HotA, HotB, LinearGradientMode.ForwardDiagonal))
                    using (var pen = new Pen(brush, 18) { LineJoin = LineJoin.Round })
                    {
                        g.FillPath(brush, petal);
                        g.DrawPath(pen, petal);
                    }
                }
                g.Restore(state);
            }
        }

        /// <summary>The usual slice's edge: a 2.5-unit band just inside the slice's outline, with a soft glow. The
        /// slice is its inset path widened by 9, so the band is the outline of that path widened by 7.75.</summary>
        static void PaintUsualEdge(Graphics g, RingModel m, int i, int n)
        {
            double span = 360.0 / n, k = m.Pulse;
            using (var edge = Outline(i * span - span / 2, i * span + span / 2, G.Inner, G.Outer, 7.75))
            {
                var bounds = edge.GetBounds();
                for (int w = 3; w >= 1; w--) // glow
                    using (var glow = new Pen(Color.FromArgb((int)(k * 22), HotB), 2.5f + w * 4) { LineJoin = LineJoin.Round })
                        g.DrawPath(glow, edge);
                using (var brush = new LinearGradientBrush(bounds, Color.FromArgb((int)(255 * (0.4 + 0.6 * k)), HotA),
                                                           Color.FromArgb((int)(255 * (0.4 + 0.6 * k)), HotB), LinearGradientMode.ForwardDiagonal))
                using (var pen = new Pen(brush, 2.5f) { LineJoin = LineJoin.Round })
                    g.DrawPath(pen, edge);
            }
        }

        /// <summary>The slice's inset path (see Petal) grown outward by d, with round corners: what a round-joined
        /// stroke of width 2d would cover, as one clean outline.</summary>
        public static GraphicsPath Outline(double start, double end, double inner, double outer, double d, double gap = 3, double corner = 9)
        {
            double cx = G.CenterX, cy = G.CenterY;
            double ro = outer - corner, ri = inner + corner, off = gap / 2 + corner;
            double Inset(double r) => Math.Asin(Math.Min(1, off / r)) * 180 / Math.PI;
            double a1o = start + Inset(ro), a2o = end - Inset(ro), a1i = start + Inset(ri), a2i = end - Inset(ri);
            var path = new GraphicsPath();
            if (a2o <= a1o || a2i <= a1i) return path;
            // Our angles: 0 = up, clockwise. GDI+: 0 = +x, clockwise, so subtract 90.
            void Arc(double x, double y, double r, double from, double sweep) =>
                path.AddArc((float)(x - r), (float)(y - r), (float)(r * 2), (float)(r * 2), (float)(from - 90), (float)sweep);
            double Px(double r, double a) => cx + r * Math.Sin(a * Math.PI / 180);
            double Py(double r, double a) => cy - r * Math.Cos(a * Math.PI / 180);
            Arc(cx, cy, ro + d, a1o, a2o - a1o);                                         // outer edge
            Arc(Px(ro, a2o), Py(ro, a2o), d, a2o, end + 90 - a2o);                       // outer corner, far side
            Arc(Px(ri, a2i), Py(ri, a2i), d, end + 90, a2i + 90 - end);                  // inner corner, far side
            Arc(cx, cy, ri - d, a2i, -(a2i - a1i));                                      // inner edge
            Arc(Px(ri, a1i), Py(ri, a1i), d, a1i + 180, start + 90 - a1i);               // inner corner, near side
            Arc(Px(ro, a1o), Py(ro, a1o), d, start - 90, a1o - start + 90);              // outer corner, near side
            path.CloseFigure();
            return path;
        }

        static void PaintSlice(Graphics g, RingModel m, Palette c, int i, int n, Slice slice)
        {
            double span = 360.0 / n, mid = i * span;
            bool hot = m.Hovered == i && m.Armed == null;
            var state = g.Save();
            if (hot)
            {
                g.TranslateTransform((float)G.CenterX, (float)G.CenterY);
                g.ScaleTransform(1.045f, 1.045f);
                g.TranslateTransform((float)-G.CenterX, (float)-G.CenterY);
            }
            var (lx, ly) = G.LabelCenter(i, n);
            double r = (G.Inner + G.Outer) / 2 + 2;
            float width = (float)Math.Min(78, 2 * Math.PI * r / n - 10);
            var ink = hot ? Color.White : (m.Armed == null ? c.Ink : Color.FromArgb(c.Ink.A / 3, c.Ink));
            using (var brush = new SolidBrush(ink))
            using (var icon = Icon(16))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var label = slice.Title.ToUpperInvariant();
                // Shrink a little to fit on one line; long names ("WhatsApp Small") go on two lines instead of being cut.
                float size = 10.5f;
                Font font = Text(size, FontStyle.Bold);
                while (g.MeasureString(label, font).Width > width && size > 9f)
                {
                    font.Dispose();
                    size -= 0.5f;
                    font = Text(size, FontStyle.Bold);
                }
                bool twoLines = g.MeasureString(label, font).Width > width && label.Contains(" ");
                float lift = twoLines ? 6 : 0;
                g.DrawString(slice.Glyph, icon, brush, new RectangleF((float)lx - 20, (float)ly - 20 - lift, 40, 22), fmt);
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                if (!twoLines) fmt.FormatFlags = StringFormatFlags.NoWrap;
                var box = twoLines ? new RectangleF((float)lx - width / 2, (float)ly + 1 - lift, width, 28)
                                   : new RectangleF((float)lx - width / 2, (float)ly + 1, width, 16);
                g.DrawString(label, font, brush, box, fmt);
                font.Dispose();
            }
            g.Restore(state);
        }

        static void PaintHub(Graphics g, RingModel m, Palette c)
        {
            float cx = (float)G.CenterX, cy = (float)G.CenterY, r = (float)G.Hub - 2;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - r - 2, cy - r + 1, r * 2 + 4, r * 2 + 4);
                using (var shadow = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(40, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) },
                    FocusScales = new PointF(0.85f, 0.85f),
                })
                    g.FillPath(shadow, path);
            }
            using (var b = new SolidBrush(c.Hub)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(c.Rim, 1)) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);

            var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap,
            };
            float w = r * 2 - 20;
            using (var ink = new SolidBrush(c.Ink))
            using (var secondary = new SolidBrush(c.Secondary))
            {
                if (m.Armed != null)
                {
                    using (var icon = Icon(14)) using (var hot = new SolidBrush(HotB))
                        g.DrawString(Catalog.Glyph.Refresh, icon, hot, new RectangleF(cx - 20, cy - 30, 40, 18), fmt);
                    using (var f = Fitting(g, m.Armed.Title, 16, w, FontStyle.Bold))
                        g.DrawString(m.Armed.Title, f, ink, new RectangleF(cx - w / 2, cy - 11, w, 22), fmt);
                    using (var f = Text(10)) g.DrawString("again", f, secondary, new RectangleF(cx - w / 2, cy + 11, w, 14), fmt);
                }
                else if (m.HoveredSlice != null)
                {
                    using (var f = Fitting(g, m.HoveredSlice.Title, 17, w, FontStyle.Bold))
                        g.DrawString(m.HoveredSlice.Title, f, ink, new RectangleF(cx - w / 2, cy - 14, w, 24), fmt);
                    using (var f = Text(10)) g.DrawString(m.Subtitle, f, secondary, new RectangleF(cx - w / 2, cy + 10, w, 14), fmt);
                }
                else
                {
                    float top = cy - 8;
                    if (m.Thumbnail != null)
                    {
                        double s = Math.Min(34.0 / m.Thumbnail.Width, 30.0 / m.Thumbnail.Height);
                        float tw = (float)(m.Thumbnail.Width * s), th = (float)(m.Thumbnail.Height * s);
                        g.DrawImage(m.Thumbnail, cx - tw / 2, cy - 30 + (30 - th) / 2, tw, th);
                        top = cy + 3;
                    }
                    fmt.Trimming = StringTrimming.EllipsisPath;
                    using (var f = Text(10.5f, FontStyle.Bold))
                        g.DrawString(m.Title, f, ink, new RectangleF(cx - w / 2 - 4, top, w + 8, 15), fmt);
                    fmt.Trimming = StringTrimming.EllipsisCharacter;
                    using (var f = Text(9.5f)) g.DrawString(m.Subtitle, f, secondary, new RectangleF(cx - w / 2, top + 14, w, 13), fmt);
                }
            }
        }

        static Font Fitting(Graphics g, string text, float size, float width, FontStyle style)
        {
            var f = Text(size, style);
            while (g.MeasureString(text, f).Width > width + 6 && size > 10)
            {
                f.Dispose();
                size -= 0.5f;
                f = Text(size, style);
            }
            return f;
        }

        static GraphicsPath Capsule(RectangleF r)
        {
            var p = new GraphicsPath();
            float d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }

        static void PaintTabs(Graphics g, RingModel m, Palette c)
        {
            var rects = G.TabRects(m.Pages.Count);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
            for (int i = 0; i < rects.Count; i++)
            {
                var r = new RectangleF((float)rects[i].X, (float)rects[i].Y, (float)rects[i].Width, (float)rects[i].Height);
                bool on = i == m.Page;
                using (var path = Capsule(r))
                {
                    if (on)
                        using (var b = new LinearGradientBrush(r, HotA, HotB, LinearGradientMode.ForwardDiagonal)) g.FillPath(b, path);
                    else
                    {
                        using (var b = new SolidBrush(c.Tab)) g.FillPath(b, path);
                        using (var p = new Pen(c.Rim, 1)) g.DrawPath(p, path);
                    }
                }
                if (m.UsualTab == i && m.Armed == null)
                    using (var path = Capsule(RectangleF.Inflate(r, -1, -1)))
                    {
                        for (int w = 3; w >= 1; w--)
                            using (var glow = new Pen(Color.FromArgb((int)(m.Pulse * 22), HotB), 2 + w * 3)) g.DrawPath(glow, path);
                        using (var b = new LinearGradientBrush(r, Color.FromArgb((int)(255 * (0.4 + 0.6 * m.Pulse)), HotA),
                                                               Color.FromArgb((int)(255 * (0.4 + 0.6 * m.Pulse)), HotB), LinearGradientMode.ForwardDiagonal))
                        using (var p = new Pen(b, 2)) g.DrawPath(p, path);
                    }
                var title = m.Pages[i].Id == "favorites" ? "★ Faves" : m.Pages[i].Title;
                var ink = on ? OnHot : (m.HoveredTab == i ? c.Ink : c.Secondary);
                using (var f = Text(11.5f, FontStyle.Bold))
                using (var b = new SolidBrush(ink))
                    g.DrawString(title, f, b, r, fmt);
            }
        }

        public static string Hint(RingModel m)
        {
            if (m.Armed != null) return "Let go anywhere to repeat";
            switch (m.Mode)
            {
                case RingModel.RingMode.Drag: return "Let go on a slice, or in the middle to keep it open";
                case RingModel.RingMode.Pinned: return "Click a slice  ·  Shift next page  ·  right-click ★  ·  Esc";
                default: return "Click a slice  ·  right-click ★  ·  Esc to close";
            }
        }

        static void PaintHint(Graphics g, RingModel m, Palette c)
        {
            var text = Hint(m);
            using (var f = Text(11f))
            {
                var size = g.MeasureString(text, f);
                var r = new RectangleF((float)G.CenterX - size.Width / 2 - 10, (float)G.HintY - 11, size.Width + 20, 22);
                using (var path = Capsule(r))
                using (var b = new SolidBrush(c.Tab))
                    g.FillPath(b, path);
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
                using (var b = new SolidBrush(c.Secondary)) g.DrawString(text, f, b, r, fmt);
            }
        }
    }
}
