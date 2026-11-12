using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using DragShift.Core;

namespace DragShift
{
    /// <summary>
    /// Favourites without any key: during a drag a 2-pixel sensor sits on the screen's edge. Push a file against
    /// the edge and your favourites slide out as big tiles; drop on one. (Not the top edge: on many PCs that's
    /// where windows snap.) The last tile is a gear: drop on it to open DragShift's Settings.
    /// </summary>
    sealed class ShelfForm : LayeredForm
    {
        public event Action<Slice, List<string>> Perform;
        /// <summary>The file was dropped on the gear at the bottom of the shelf.</summary>
        public event Action OpenSettings;
        internal Action<string> Note = Log.Line;
        public Func<IList<string>, List<Slice>> Favorites;
        public string Side = "right"; // right | left | off

        const float Width_ = 112, TileH = 64, Gap = 8, Header = 34, Pad = 10, GearH = 40, NoneH = 36;
        const string GearGlyph = ""; // Settings, in Segoe MDL2 Assets and Segoe Fluent Icons
        enum Mode { Hidden, Sensor, Open }
        Mode mode = Mode.Hidden;
        List<Slice> tiles = new List<Slice>();
        List<string> files = new List<string>();
        int? hovered;
        double scale = 1;
        Rectangle screen, rect;
        readonly Timer collapse = new Timer { Interval = 450 };
        readonly Timer releaseGrace = new Timer { Interval = 300 };
        readonly DragImages dragImages = new DragImages();

        public ShelfForm()
        {
            AllowDrop = true;
            Text = "DragShift Favourites";
            collapse.Tick += (s, e) => { collapse.Stop(); if (mode == Mode.Open) Sensor(); };
            releaseGrace.Tick += (s, e) => HideShelf();
        }

        /// <summary>A drag started somewhere: put the sensor on the edge of that screen.</summary>
        public void DragStarted(Point cursor)
        {
            releaseGrace.Stop();
            if (Side == "off") return;
            UseScreenOf(cursor);
            Sensor();
            if (!Visible) Show();
        }

        internal void UseScreenOf(Point cursor) => UseScreen(Native.ScreenAt(cursor.X, cursor.Y), Native.ScaleAt(cursor.X, cursor.Y));

        internal void UseScreen(Rectangle bounds, double pixelScale)
        {
            screen = bounds;
            scale = pixelScale;
        }

        /// <summary>Lights up one row (the self-test draws the gear as it looks under the pointer).</summary>
        internal void Highlight(int? row) => hovered = row;

        public void DragEnded()
        {
            collapse.Stop();
            // The mouse hook can arrive before Windows sends OnDragDrop. Hiding the target here
            // would cancel that drop, including the Settings gear. A completed drop hides immediately.
            if (mode == Mode.Open)
            {
                releaseGrace.Stop();
                releaseGrace.Start();
            }
            else HideShelf();
        }

        void HideShelf()
        {
            releaseGrace.Stop();
            collapse.Stop();
            mode = Mode.Hidden;
            hovered = null;
            if (Visible) Hide();
        }

        void Sensor()
        {
            mode = Mode.Sensor;
            hovered = null;
            int w = 2;
            rect = new Rectangle(Side == "left" ? screen.Left : screen.Right - w, screen.Top + screen.Height / 8, w, screen.Height * 3 / 4);
            Render();
        }

        // Where things sit inside the shelf, in the same units as the drawing (before the screen scale).
        float TilesBottom => Header + (tiles.Count > 0 ? tiles.Count * (TileH + Gap) : NoneH);
        float TotalHeight => TilesBottom + GearH + Pad;

        /// <summary>Row i: a favourite, or (the last row) the gear. Top and height.</summary>
        (float Top, float Height) Span(int i) => i < tiles.Count ? (Header + i * (TileH + Gap), TileH) : (TilesBottom, GearH);

        /// <summary>As many favourites as fit above the gear on this screen, so the gear is never pushed off the bottom.</summary>
        int MaxTiles() => Math.Max(1, (int)Math.Floor((screen.Height / scale - 16 - Header - GearH - Pad) / (TileH + Gap)));

        /// <summary>A file reached the edge: slide the shelf out for it, centred on the pointer.</summary>
        internal void OpenFor(List<string> dropped, Point cursor)
        {
            releaseGrace.Stop();
            collapse.Stop();
            files = dropped;
            tiles = (Favorites?.Invoke(files) ?? new List<Slice>()).Take(MaxTiles()).ToList();
            Open(cursor);
        }

        void Open(Point cursor)
        {
            mode = Mode.Open;
            int w = (int)Math.Ceiling((Width_ + Pad * 2) * scale);
            int h = (int)Math.Ceiling(TotalHeight * scale);
            int y = Num.Clamp(cursor.Y - h / 2, screen.Top + 8, Math.Max(screen.Top + 8, screen.Bottom - h - 8));
            rect = new Rectangle(Side == "left" ? screen.Left : screen.Right - w, y, w, h);
            Render();
        }

        void Render()
        {
            using (var frame = new Bitmap(Math.Max(1, rect.Width), Math.Max(1, rect.Height), PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(frame))
                {
                    g.Clear(Color.Transparent);
                    if (mode == Mode.Sensor)
                        using (var b = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(b, 0, 0, rect.Width, rect.Height);
                    else if (mode == Mode.Open)
                    {
                        g.ScaleTransform((float)scale, (float)scale);
                        Draw(g, DarkMode);
                    }
                }
                Present(frame, rect);
            }
        }

        /// <summary>The open shelf as a picture, for the self-test.</summary>
        internal Bitmap Snapshot(bool dark)
        {
            var bmp = new Bitmap((int)Math.Ceiling((Width_ + Pad * 2) * scale), (int)Math.Ceiling(TotalHeight * scale), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(dark ? Color.FromArgb(70, 70, 90) : Color.FromArgb(200, 200, 215));
                g.ScaleTransform((float)scale, (float)scale);
                Draw(g, dark);
            }
            return bmp;
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Draw(Graphics g, bool dark)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var panel = new RectangleF(Side == "left" ? 0 : Pad, 0, Width_ + Pad, TotalHeight);
            using (var path = Rounded(panel, 18))
            {
                using (var b = new SolidBrush(dark ? Color.FromArgb(245, 32, 32, 36) : Color.FromArgb(245, 236, 236, 240))) g.FillPath(b, path);
                using (var p = new Pen(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255))) g.DrawPath(p, path);
            }
            var ink = dark ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(210, 0, 0, 0);
            var muted = dark ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(130, 0, 0, 0);
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var f = RingPainter.Text(12, FontStyle.Bold)) using (var b = new SolidBrush(ink))
                g.DrawString("★ Favorites", f, b, new RectangleF(panel.X, 0, panel.Width, Header), center);
            if (tiles.Count == 0)
                using (var f = RingPainter.Text(11)) using (var b = new SolidBrush(muted))
                    g.DrawString("None fit this file", f, b, new RectangleF(panel.X, Header, panel.Width, NoneH - Gap), center);
            for (int i = 0; i < tiles.Count; i++)
            {
                var r = Cell(panel, i);
                bool hot = hovered == i;
                using (var path = Rounded(r, 14))
                {
                    if (hot) using (var b = new LinearGradientBrush(r, RingPainter.HotA, RingPainter.HotB, LinearGradientMode.ForwardDiagonal)) g.FillPath(b, path);
                    else using (var b = new SolidBrush(dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(240, 255, 255, 255))) g.FillPath(b, path);
                }
                var color = hot ? Color.White : ink;
                using (var b = new SolidBrush(color))
                {
                    using (var f = RingPainter.Icon(18)) g.DrawString(tiles[i].Glyph, f, b, new RectangleF(r.X, r.Y + 6, r.Width, 26), center);
                    using (var f = RingPainter.Text(11.5f, FontStyle.Bold)) g.DrawString(tiles[i].Title, f, b, new RectangleF(r.X + 4, r.Y + 34, r.Width - 8, 20), center);
                }
            }
            DrawGear(g, Cell(panel, tiles.Count), hovered == tiles.Count, dark, ink);
        }

        /// <summary>The Settings gear: a slimmer tile at the bottom, a gear and its name side by side.</summary>
        static void DrawGear(Graphics g, RectangleF r, bool hot, bool dark, Color ink)
        {
            using (var path = Rounded(r, 12))
            {
                if (hot) using (var b = new LinearGradientBrush(r, RingPainter.HotA, RingPainter.HotB, LinearGradientMode.ForwardDiagonal)) g.FillPath(b, path);
                else using (var b = new SolidBrush(dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(240, 255, 255, 255))) g.FillPath(b, path);
            }
            var color = hot ? Color.White : ink;
            var middle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
            var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
            using (var b = new SolidBrush(color))
            {
                using (var f = RingPainter.Icon(17)) g.DrawString(GearGlyph, f, b, new RectangleF(r.X + 8, r.Y, 30, r.Height), middle);
                using (var f = RingPainter.Text(11.5f, FontStyle.Bold)) g.DrawString("Settings", f, b, new RectangleF(r.X + 40, r.Y, r.Width - 44, r.Height), left);
            }
        }

        /// <summary>Row i inside the drawn panel (a favourite, or the gear after the last one).</summary>
        RectangleF Cell(RectangleF panel, int i)
        {
            var (top, height) = Span(i);
            return new RectangleF(panel.X + Pad / 2 + 2, top, Width_ - 4, height);
        }

        /// <summary>Which row is under this screen point: 0.. for a favourite, the count for the gear, null for none.</summary>
        internal int? TileAt(Point screenPoint)
        {
            float y = (float)((screenPoint.Y - rect.Y) / scale);
            for (int i = 0; i <= tiles.Count; i++)
            {
                var (top, height) = Span(i);
                if (y >= top && y <= top + height) return i;
            }
            return null;
        }

        /// <summary>Row i on the screen, in pixels (the self-test points at the middle of it).</summary>
        internal Rectangle TileBounds(int i)
        {
            var (top, height) = Span(i);
            return new Rectangle(rect.X, rect.Y + (int)Math.Round(top * scale), rect.Width, (int)Math.Round(height * scale));
        }

        internal int TileCount => tiles.Count;
        internal bool IsOpen => mode == Mode.Open;

        /// <summary>The file was let go on a row: put the shelf away, then run the favourite or open Settings.</summary>
        internal void Choose(int? tile, List<string> list)
        {
            HideShelf();
            if (!tile.HasValue) return;
            if (tile.Value < tiles.Count) Perform?.Invoke(tiles[tile.Value], list);
            else if (tile.Value == tiles.Count) OpenSettings?.Invoke();
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            var dropped = (e.Data?.GetData(DataFormats.FileDrop) as string[])?.ToList();
            if (dropped == null || dropped.Count == 0 || Favorites == null) { e.Effect = DragDropEffects.None; return; }
            collapse.Stop();
            e.Effect = (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            if (mode != Mode.Open)
            {
                OpenFor(dropped, new Point(e.X, e.Y));
                Note("shelf: open with " + tiles.Count + " favourite(s)");
            }
            dragImages.Enter(Handle, e);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            if (mode != Mode.Open) { e.Effect = DragDropEffects.None; return; }
            e.Effect = (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            dragImages.Over(e);
            var t = TileAt(new Point(e.X, e.Y));
            if (t != hovered) { hovered = t; Render(); }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            dragImages.Leave();
            if (mode == Mode.Open) collapse.Start();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            releaseGrace.Stop();
            dragImages.Drop(e);
            var t = TileAt(new Point(e.X, e.Y));
            var list = files;
            if (t == tiles.Count) Note("shelf: Settings");
            BeginInvoke((Action)(() => Choose(t, list)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                releaseGrace.Dispose();
                collapse.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
