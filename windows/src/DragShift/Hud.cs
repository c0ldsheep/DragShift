using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DragShift
{
    /// <summary>The bubble near the pointer: progress while working, then the result with Show and Undo.
    /// It never takes the focus, and fades away on its own.</summary>
    sealed class Hud : LayeredForm
    {
        public enum State { Working, Done, Failed, Flash }

        public event Action ShowClicked, UndoClicked, CancelClicked;

        State state;
        string title = "", detail = "";
        double progress;
        bool canUndo, canShow;
        Point anchor;
        double scale = 1;
        float spin;
        bool hovering;
        readonly Timer spinner = new Timer { Interval = 33 };
        readonly Timer fade = new Timer();
        readonly Dictionary<string, RectangleF> buttons = new Dictionary<string, RectangleF>();
        const float W = 380, H = 92;

        public Hud()
        {
            Text = "DragShift";
            spinner.Tick += (s, e) => { spin = (spin + 14) % 360; Render(); };
            fade.Tick += (s, e) =>
            {
                fade.Stop();
                if (hovering) { fade.Interval = 1500; fade.Start(); return; }
                Hide();
            };
        }

        public void Begin(string what, int count, Point near)
        {
            state = State.Working;
            title = what + "…";
            detail = count > 1 ? "0 of " + count + " done" : "";
            progress = 0.03;
            canUndo = canShow = false;
            Place(near);
            fade.Stop();
            spinner.Start();
            Render();
            if (!Visible) Show();
        }

        public void Update(double fraction, string text = null)
        {
            if (state != State.Working) return;
            progress = Math.Max(progress, Math.Min(1, fraction));
            if (text != null) detail = text;
            Render();
        }

        public void Finish(string heading, string text, bool undo, bool show, bool failed)
        {
            spinner.Stop();
            state = failed ? State.Failed : State.Done;
            title = heading;
            detail = text;
            canUndo = undo;
            canShow = show;
            if (!Visible) Place(Cursor.Position);
            Render();
            if (!Visible) Show();
            fade.Interval = failed ? 7000 : 5000;
            fade.Start();
        }

        public void ShowFlash(string heading, string text, Point near)
        {
            spinner.Stop();
            state = State.Flash;
            title = heading;
            detail = text ?? "";
            canUndo = canShow = false;
            Place(near);
            Render();
            if (!Visible) Show();
            fade.Stop();
            fade.Interval = 2600;
            fade.Start();
        }

        void Place(Point near)
        {
            anchor = near;
            scale = Native.ScaleAt(near.X, near.Y);
        }

        Rectangle Bounds_()
        {
            int w = (int)Math.Ceiling((W + 24) * scale), h = (int)Math.Ceiling((H + 24) * scale);
            var work = Native.WorkAreaAt(anchor.X, anchor.Y);
            int x = anchor.X - w / 2;
            int y = anchor.Y + (int)(40 * scale);
            if (y + h > work.Bottom) y = anchor.Y - (int)(40 * scale) - h; // no room below: go above
            x = Core.Num.Clamp(x, work.Left, Math.Max(work.Left, work.Right - w));
            y = Core.Num.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        void Render()
        {
            var bounds = Bounds_();
            bool dark = DarkMode;
            using (var frame = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(frame))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.ScaleTransform((float)scale, (float)scale);
                    g.TranslateTransform(12, 10);
                    Draw(g, dark);
                }
                Present(frame, bounds);
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
            var box = new RectangleF(0, 0, W, H);
            for (int i = 6; i >= 1; i--) // soft shadow
                using (var path = Rounded(new RectangleF(-i, -i + 4, W + 2 * i, H + 2 * i), 16 + i))
                using (var b = new SolidBrush(Color.FromArgb(dark ? 16 : 9, 0, 0, 0)))
                    g.FillPath(b, path);
            var ink = dark ? Color.FromArgb(240, 255, 255, 255) : Color.FromArgb(225, 0, 0, 0);
            var secondary = dark ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(140, 0, 0, 0);
            using (var path = Rounded(box, 16))
            {
                using (var b = new SolidBrush(dark ? Color.FromArgb(248, 38, 38, 43) : Color.FromArgb(250, 252, 252, 253))) g.FillPath(b, path);
                using (var p = new Pen(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), 1)) g.DrawPath(p, path);
            }

            // Status mark
            var mark = new RectangleF(16, 22, 34, 34);
            switch (state)
            {
                case State.Working:
                    using (var p = new Pen(Color.FromArgb(50, ink), 3.5f)) g.DrawEllipse(p, mark);
                    using (var p = new Pen(RingPainter.HotB, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(p, mark, spin, 100);
                    break;
                case State.Done:
                    using (var b = new SolidBrush(Color.FromArgb(52, 199, 89))) g.FillEllipse(b, mark);
                    using (var p = new Pen(Color.White, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(p, new[] { new PointF(25, 39), new PointF(31, 45), new PointF(42, 32) });
                    break;
                case State.Failed:
                    using (var b = new SolidBrush(Color.FromArgb(255, 149, 0))) g.FillEllipse(b, mark);
                    using (var f = RingPainter.Text(20, FontStyle.Bold)) using (var b = new SolidBrush(Color.White))
                        g.DrawString("!", f, b, mark, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                    break;
                default:
                    using (var b = new LinearGradientBrush(mark, RingPainter.HotA, RingPainter.HotB, LinearGradientMode.ForwardDiagonal)) g.FillEllipse(b, mark);
                    using (var f = RingPainter.Icon(16)) using (var b = new SolidBrush(Color.White))
                        g.DrawString(Core.Catalog.Glyph.StarFill, f, b, mark, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                    break;
            }

            // Buttons on the right
            buttons.Clear();
            var labels = new List<string>();
            if (state == State.Working) labels.Add("Cancel");
            if (canShow) labels.Add("Show");
            if (canUndo) labels.Add("Undo");
            float right = W - 14;
            using (var f = RingPainter.Text(12, FontStyle.Bold))
            {
                for (int i = labels.Count - 1; i >= 0; i--)
                {
                    float bw = g.MeasureString(labels[i], f).Width + 22;
                    var r = new RectangleF(right - bw, H / 2 - 14, bw, 28);
                    buttons[labels[i]] = r;
                    right = r.X - 8;
                    bool primary = labels[i] == "Show";
                    using (var path = Rounded(r, 14))
                    {
                        if (primary)
                            using (var b = new LinearGradientBrush(r, RingPainter.HotA, RingPainter.HotB, LinearGradientMode.ForwardDiagonal)) g.FillPath(b, path);
                        else
                            using (var b = new SolidBrush(dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0))) g.FillPath(b, path);
                    }
                    using (var b = new SolidBrush(primary ? RingPainter.OnHot : ink))
                        g.DrawString(labels[i], f, b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }
            }

            // Text
            float textW = right - 62;
            var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var f = RingPainter.Text(14, FontStyle.Bold)) using (var b = new SolidBrush(ink))
                g.DrawString(title, f, b, new RectangleF(62, string.IsNullOrEmpty(detail) ? 34 : 20, textW, 20), fmt);
            if (!string.IsNullOrEmpty(detail))
            {
                var wrap = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
                using (var f = RingPainter.Text(11.5f)) using (var b = new SolidBrush(secondary))
                    g.DrawString(detail, f, b, new RectangleF(62, 41, textW, 32), wrap);
            }
            if (state == State.Working)
            {
                var track = new RectangleF(62, H - 14, textW, 4);
                using (var path = Rounded(track, 2)) using (var b = new SolidBrush(Color.FromArgb(30, ink))) g.FillPath(b, path);
                var done = new RectangleF(62, H - 14, Math.Max(4, (float)(textW * progress)), 4);
                using (var path = Rounded(done, 2))
                using (var b = new LinearGradientBrush(new RectangleF(62, H - 14, textW, 4), RingPainter.HotA, RingPainter.HotB, 0f))
                    g.FillPath(b, path);
            }
        }

        string ButtonAt(Point client)
        {
            float x = (float)(client.X / scale) - 12, y = (float)(client.Y / scale) - 10;
            foreach (var kv in buttons) if (kv.Value.Contains(x, y)) return kv.Key;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            switch (ButtonAt(e.Location))
            {
                case "Show": ShowClicked?.Invoke(); Hide(); break;
                case "Undo": UndoClicked?.Invoke(); break;
                case "Cancel": CancelClicked?.Invoke(); break;
                default: if (state != State.Working) Hide(); break;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; base.OnMouseLeave(e); }
    }
}
