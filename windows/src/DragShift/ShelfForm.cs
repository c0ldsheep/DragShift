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
    /// where windows snap.)
    /// </summary>
    sealed class ShelfForm : LayeredForm
    {
        public event Action<Slice, List<string>> Perform;
        public Func<IList<string>, List<Slice>> Favorites;
        public string Side = "right"; // right | left | off

        const float Width_ = 112, TileH = 64, Gap = 8, Header = 34, Pad = 10;
        enum Mode { Hidden, Sensor, Open }
        Mode mode = Mode.Hidden;
        List<Slice> tiles = new List<Slice>();
        List<string> files = new List<string>();
        int? hovered;
        double scale = 1;
        Rectangle screen, rect;
        readonly Timer collapse = new Timer { Interval = 450 };
        readonly DragImages dragImages = new DragImages();

        public ShelfForm()
        {
            AllowDrop = true;
            Text = "DragShift Favourites";
            collapse.Tick += (s, e) => { collapse.Stop(); if (mode == Mode.Open) Sensor(); };
        }

        /// <summary>A drag started somewhere: put the sensor on the edge of that screen.</summary>
        public void DragStarted(Point cursor)
        {
            if (Side == "off") return;
            screen = Native.ScreenAt(cursor.X, cursor.Y);
            scale = Native.ScaleAt(cursor.X, cursor.Y);
            Sensor();
            if (!Visible) Show();
        }

        public void DragEnded()
        {
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

        void Open(Point cursor)
        {
            mode = Mode.Open;
            int w = (int)Math.Ceiling((Width_ + Pad * 2) * scale);
            int h = (int)Math.Ceiling((Header + tiles.Count * (TileH + Gap) + Pad) * scale);
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
            float h = Header + tiles.Count * (TileH + Gap) + Pad;
            var panel = new RectangleF(Side == "left" ? 0 : Pad, 0, Width_ + Pad, h);
            using (var path = Rounded(panel, 18))
            {
                using (var b = new SolidBrush(dark ? Color.FromArgb(245, 32, 32, 36) : Color.FromArgb(245, 236, 236, 240))) g.FillPath(b, path);
                using (var p = new Pen(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255))) g.DrawPath(p, path);
            }
            var ink = dark ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(210, 0, 0, 0);
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var f = RingPainter.Text(12, FontStyle.Bold)) using (var b = new SolidBrush(ink))
                g.DrawString("★ Favorites", f, b, new RectangleF(panel.X, 0, panel.Width, Header), center);
            for (int i = 0; i < tiles.Count; i++)
            {
                var r = new RectangleF(panel.X + Pad / 2 + 2, Header + i * (TileH + Gap), Width_ - 4, TileH);
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
        }

        int? TileAt(Point screenPoint)
        {
            float y = (float)((screenPoint.Y - rect.Y) / scale) - Header;
            if (y < 0) return null;
            int i = (int)(y / (TileH + Gap));
            return i >= 0 && i < tiles.Count && y - i * (TileH + Gap) <= TileH ? i : (int?)null;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            var dropped = (e.Data?.GetData(DataFormats.FileDrop) as string[])?.ToList();
            if (dropped == null || dropped.Count == 0 || Favorites == null) { e.Effect = DragDropEffects.None; return; }
            collapse.Stop();
            e.Effect = (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            if (mode != Mode.Open)
            {
                files = dropped;
                tiles = Favorites(files);
                if (tiles.Count == 0) { e.Effect = DragDropEffects.None; return; }
                Open(new Point(e.X, e.Y));
                Log.Line("shelf: open with " + tiles.Count + " favourite(s)");
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
            dragImages.Drop(e);
            var t = TileAt(new Point(e.X, e.Y));
            var list = files;
            BeginInvoke((Action)(() =>
            {
                DragEnded();
                if (t.HasValue && t.Value < tiles.Count) Perform?.Invoke(tiles[t.Value], list);
            }));
        }
    }
}
