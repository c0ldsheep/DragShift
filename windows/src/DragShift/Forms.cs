using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DragShift.Core;
using Keys = DragShift.Core.Settings.Keys;

namespace DragShift
{
    static class Ui
    {
        public static Font Body => new Font("Segoe UI", 9.5f);
        public static Font Small => new Font("Segoe UI", 8.5f);
        public static Font Bold => new Font("Segoe UI Semibold", 10f);
        public static Font Title => new Font("Segoe UI Semibold", 13f);
        public static readonly Color Muted = Color.FromArgb(110, 110, 115);

        public static void Dialog(Form f, string title, int w, int h)
        {
            f.Text = title;
            f.Font = Body;
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.AutoScaleDimensions = new SizeF(96, 96);
            f.ClientSize = new Size(w, h);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.Manual;
            f.ShowInTaskbar = true;
            f.TopMost = true;
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            var c = Cursor.Position;
            f.Location = new Point(Num.Clamp(c.X - w / 2, work.Left, Math.Max(work.Left, work.Right - w - 20)),
                                   Num.Clamp(c.Y - h / 2, work.Top, Math.Max(work.Top, work.Bottom - h - 40)));
            try { f.Icon = Icon.ExtractAssociatedIcon(Paths.Exe); } catch (Exception) { }
        }

        public static Label Text(string text, Font font = null, Color? color = null, int width = 0)
        {
            // UseMnemonic off: Windows would turn the "&" in "Tom & Jerry.jpg" into a shortcut key and drop it.
            var l = new Label { Text = text, Font = font ?? Body, AutoSize = true, ForeColor = color ?? SystemColors.ControlText, UseMnemonic = false };
            if (width > 0) l.MaximumSize = new Size(width, 0);
            return l;
        }

        public static Button Primary(string text)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, Padding = new Padding(10, 3, 10, 3), FlatStyle = FlatStyle.Flat,
                BackColor = RingPainter.HotB, ForeColor = RingPainter.OnHot, Font = Bold, Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        public static Button Plain(string text) => new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };

        /// <summary>Shows a window and brings it to the front, even when another app has the focus (a second start of
        /// DragShift, a drop on the shelf's gear).</summary>
        public static void Raise(Form f)
        {
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Show();
            f.TopMost = true;  // to the top of the stack…
            f.TopMost = false; // …without staying there
            f.Activate();
        }
    }

    /// <summary>A big rounded choice: "50 KB / photos".</summary>
    sealed class Chip : Control
    {
        public string Big, Hint;
        public bool Picked;
        bool hover;

        public Chip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent?.BackColor ?? SystemColors.Control);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = new GraphicsPath())
            {
                float d = 22;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                if (Picked && Enabled)
                    using (var b = new LinearGradientBrush(r, RingPainter.HotA, RingPainter.HotB, LinearGradientMode.ForwardDiagonal)) g.FillPath(b, path);
                else
                    using (var b = new SolidBrush(Color.FromArgb(hover && Enabled ? 28 : 14, 0, 0, 0))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(24, 0, 0, 0))) g.DrawPath(p, path);
            }
            var ink = !Enabled ? Color.FromArgb(120, 0, 0, 0) : Picked ? RingPainter.OnHot : Color.FromArgb(30, 30, 30);
            var sub = !Enabled ? Color.FromArgb(100, 0, 0, 0) : Picked ? Color.FromArgb(220, 255, 255, 255) : Ui.Muted;
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var f = new Font("Segoe UI Semibold", 13f)) using (var b = new SolidBrush(ink))
                g.DrawString(Big, f, b, new RectangleF(0, 4, Width, Height * 0.55f), fmt);
            using (var f = new Font("Segoe UI", 8f)) using (var b = new SolidBrush(sub))
                g.DrawString(Hint ?? "", f, b, new RectangleF(0, Height * 0.52f, Width, Height * 0.4f), fmt);
        }
    }

    /// <summary>"Under ___ KB": one click on a size and it's done.</summary>
    sealed class FitForm : Form
    {
        public event Action<int> Chosen;
        static readonly Dictionary<int, string> Hints = new Dictionary<int, string>
        {
            { 20, "signatures" }, { 50, "photos" }, { 100, "photos" }, { 200, "documents" }, { 500, "documents" }, { 1000, "email" },
        };

        public FitForm(IList<string> files, int last)
        {
            Ui.Dialog(this, "Make it smaller than…", 440, 360);
            long total = files.Sum(Inspector.Size);
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };
            flow.Controls.Add(Ui.Text(files.Count == 1 ? Path.GetFileName(files[0]) : files.Count + " files", Ui.Bold));
            flow.Controls.Add(Ui.Text("Now " + Bytes.Format(total) + " · makes a JPG or PDF copy", Ui.Small, Ui.Muted));
            var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Width = 404, Height = 132, Margin = new Padding(0, 12, 0, 8) };
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            for (int i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            foreach (var kb in SizeLimit.Presets)
            {
                bool already = total > 0 && files.All(f => Inspector.Size(f) <= SizeLimit.Bytes(kb));
                var chip = new Chip
                {
                    Big = SizeLimit.Title(kb), Hint = already ? "already under" : Hints[kb], Picked = kb == last,
                    Dock = DockStyle.Fill, Margin = new Padding(4), Enabled = !already,
                };
                int k = kb;
                chip.Click += (s, e) => Go(k);
                grid.Controls.Add(chip);
            }
            flow.Controls.Add(grid);
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            var custom = new TextBox { Width = 70, Margin = new Padding(8, 3, 4, 0) };
            var go = Ui.Plain("Go");
            go.Click += (s, e) => { if (int.TryParse(custom.Text.Trim(), out var n) && n >= 5 && n <= 100_000) Go(n); };
            custom.KeyDown += (s, e) => { if (e.KeyCode == System.Windows.Forms.Keys.Enter) { go.PerformClick(); e.SuppressKeyPress = true; } };
            row.Controls.Add(Ui.Text("Other size"));
            row.Controls.Add(custom);
            row.Controls.Add(Ui.Text("KB", null, Ui.Muted));
            row.Controls.Add(go);
            flow.Controls.Add(row);
            flow.Controls.Add(Ui.Text("Keeps it as sharp as the size allows. It aims just under the limit (1 KB = 1,000 bytes), so it passes on every website, even ones that also ask for a minimum.",
                                      Ui.Small, Ui.Muted, 400));
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Width = 404, Margin = new Padding(0, 12, 0, 0) };
            var main = Ui.Primary("Under " + SizeLimit.Title(last));
            main.Click += (s, e) => Go(last);
            var cancel = Ui.Plain("Cancel");
            cancel.Click += (s, e) => Close();
            buttons.Controls.Add(main);
            buttons.Controls.Add(cancel);
            flow.Controls.Add(buttons);
            Controls.Add(flow);
            AcceptButton = main;
            CancelButton = cancel;
        }

        void Go(int kb)
        {
            Close();
            Chosen?.Invoke(kb);
        }
    }

    /// <summary>Resize presets, with the new size shown before you pick.</summary>
    sealed class ResizeForm : Form
    {
        public event Action<string> Chosen;

        public ResizeForm(IList<string> files)
        {
            Ui.Dialog(this, "Resize", 360, 400);
            Size size = Size.Empty;
            try
            {
                using (var fs = File.OpenRead(files[0]))
                using (var img = Image.FromStream(fs, false, false)) size = img.Size;
            }
            catch (Exception) { }
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };
            flow.Controls.Add(Ui.Text(files.Count == 1 ? Path.GetFileName(files[0]) : files.Count + " files", Ui.Bold));
            flow.Controls.Add(Ui.Text(size.IsEmpty ? "Longest side or scale" : "Now " + size.Width + " × " + size.Height + " px", Ui.Small, Ui.Muted));
            var preview = Ui.Text("", Ui.Bold);
            var choices = new List<(string Label, string Step, double Factor)>
            {
                ("25%", "img.pct.25", 0.25), ("50%", "img.pct.50", 0.5), ("75%", "img.pct.75", 0.75),
                ("1080 px", "img.size.1080", -1080), ("1600 px", "img.size.1600", -1600), ("2048 px", "img.size.2048", -2048),
                ("3840 px (4K)", "img.size.3840", -3840),
            };
            string step = "img.pct.50";
            void Show(double factor)
            {
                if (size.IsEmpty) { preview.Text = ""; return; }
                double s = factor > 0 ? factor : Math.Min(1, -factor / Math.Max(size.Width, size.Height));
                preview.Text = "→ " + (int)Math.Round(size.Width * s) + " × " + (int)Math.Round(size.Height * s) + " px";
            }
            foreach (var c in choices)
            {
                var rb = new RadioButton { Text = c.Label, AutoSize = true, Checked = c.Step == step, Margin = new Padding(0, 3, 0, 3) };
                rb.CheckedChanged += (s, e) => { if (rb.Checked) { step = c.Step; Show(c.Factor); } };
                flow.Controls.Add(rb);
            }
            Show(0.5);
            preview.Margin = new Padding(0, 10, 0, 0);
            flow.Controls.Add(preview);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Width = 324, Margin = new Padding(0, 14, 0, 0) };
            var ok = Ui.Primary("Resize");
            ok.Click += (s, e) => { Close(); Chosen?.Invoke(step); };
            var cancel = Ui.Plain("Cancel");
            cancel.Click += (s, e) => Close();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            flow.Controls.Add(buttons);
            Controls.Add(flow);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    /// <summary>The About tab's coffee cup, tipped with the app's own move: drag it, press Shift, and three sizes fan
    /// out under it; let go over one. A plain click opens the tip window too. The cup tips as it is pulled away from
    /// its saucer and spills its coffee by the time it reaches the edge of the ring, the same cup as on the Mac.</summary>
    sealed class CoffeeCup : Control
    {
        public const string Closed = "Drag the cup and press Shift, or just click it";
        // All at 100% scaling. The ring, the hole in its middle, and the circle you can grab the cup by (what is drawn is bigger).
        const float Outer = 96, Inner = 36, Cup = 44;
        // The cup as drawn: its width from its left side to the outside of its handle.
        const float Art = 54;
        // The area. The drawing reaches 0.69 cup widths left of the cup's middle, 0.55 right, 0.42 up and 0.39 down when the cup is
        // tipped (the wave and the drops), and the cup can be pulled 74 px in any direction below its saucer: this leaves them room
        // (the self-test pulls it everywhere to check).
        const float DesignWidth = 232, DesignHeight = 136, RestY = 32;
        const int ReturnMs = 250;
        public event Action<int> Picked;
        public event Action<string> Hint;

        readonly Timer shiftWatch = new Timer { Interval = 40 };
        readonly Timer settle = new Timer { Interval = 15 };
        readonly Stopwatch clock = new Stopwatch();
        Point down;
        PointF offset, released;
        bool dragging, open, moved;

        public CoffeeCup()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = Natural;
            Cursor = Cursors.Hand;
            AccessibleName = "Buy me a coffee";
            // Shift pressed while the cup is held still moves nothing, so look at the key itself while dragging.
            shiftWatch.Tick += (s, e) => { if (dragging) See(); };
            settle.Tick += (s, e) => Settle();
        }

        float K => Width / DesignWidth;
        PointF Rest => new PointF(Width / 2f, RestY * K);
        int Over => open ? Tip.Slice(offset.X / K, offset.Y / K, Inner) : -1;

        /// <summary>Where the cup is `ms` after it was let go at `released`: quick at first, gentle at the end, back on its saucer
        /// after a quarter of a second. (The coffee settles with it, because how far it is tipped follows how far it is pulled.)</summary>
        internal static PointF Back(PointF released, float ms)
        {
            float done = Math.Min(1f, Math.Max(0f, ms / ReturnMs)), left = (1 - done) * (1 - done) * (1 - done);
            return done >= 1f ? PointF.Empty : new PointF(released.X * left, released.Y * left);
        }

        void Settle()
        {
            long ms = clock.ElapsedMilliseconds;
            offset = Back(released, ms);
            if (ms >= ReturnMs) StopSettling();
            Invalidate();
        }

        void StopSettling() { settle.Stop(); clock.Reset(); }

        // For the self-test: where the saucer is, where the cup is, and whether it is on its way back.
        internal PointF Saucer => Rest;
        internal PointF Held => offset;
        internal bool Settling => settle.Enabled;

        void See()
        {
            if (!open && (ModifierKeys & System.Windows.Forms.Keys.Shift) != 0) open = true;
            Hint?.Invoke(!open ? Closed : Over < 0 ? "Let go over a size" : "Let go: " + Tip.Title(Over));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var r = Rest;
            if (Math.Sqrt(Math.Pow(e.X - r.X, 2) + Math.Pow(e.Y - r.Y, 2)) > Cup / 2 * K + 4) return;
            StopSettling(); // grabbed again while it was still on its way back
            down = e.Location; dragging = true; moved = false; open = false; offset = PointF.Empty;
            Capture = true;
            shiftWatch.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            float x = e.X - down.X, y = Math.Max(e.Y - down.Y, -4 * K);
            if (Math.Abs(x) + Math.Abs(y) > 4) moved = true;
            // The cup stays inside the ring's half-circle, so it can't leave this control.
            double far = Math.Sqrt(x * x + y * y), most = (Outer - Cup / 2) * K;
            if (far > most) { x = (float)(x * most / far); y = (float)(y * most / far); }
            offset = new PointF(x, y);
            See();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) return;
            int over = Over;
            bool click = !moved;
            dragging = false; open = false;
            Capture = false;
            shiftWatch.Stop();
            Hint?.Invoke(Closed);
            // Let go: the cup comes back to its saucer in a quarter of a second (at once when Windows is set to show no animations).
            released = offset;
            if (offset == PointF.Empty || !Native.Animations) offset = PointF.Empty;
            else { clock.Restart(); settle.Start(); }
            Invalidate();
            if (over >= 0) Picked?.Invoke(over);
            else if (click) Picked?.Invoke(-1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { shiftWatch.Dispose(); settle.Dispose(); }
            base.Dispose(disposing);
        }

        static GraphicsPath Wedge(PointF c, double from, double to, float inner, float outer)
        {
            // Angles in degrees, 0 = right, 90 = down, as on screen.
            var path = new GraphicsPath();
            path.AddArc(c.X - outer, c.Y - outer, outer * 2, outer * 2, (float)from, (float)(to - from));
            path.AddArc(c.X - inner, c.Y - inner, inner * 2, inner * 2, (float)to, (float)(from - to));
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e) =>
            Draw(e.Graphics, Width, offset, open, Parent?.BackColor ?? SystemColors.Control, ForeColor);

        /// <summary>The size the cup's area needs at 100% scaling.</summary>
        public static Size Natural => new Size((int)DesignWidth, (int)DesignHeight);

        /// <summary>How far the cup has been pulled from its saucer: 0 at rest, 1 at the edge of the ring.</summary>
        public static float Pulled(PointF offset, float scale) =>
            Math.Min(1f, (float)Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y) / ((Outer - Cup / 2) * scale));

        /// <summary>Draws the cup (and the ring, when open) with the cup moved by `offset` from its saucer, in an area `width` wide
        /// (<see cref="Natural"/> at 100%). Static so the self-test can save pictures of it without opening a window.</summary>
        public static void Draw(Graphics g, int width, PointF offset, bool open, Color back, Color fore)
        {
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            float k = width / DesignWidth;
            var rest = new PointF(width / 2f, RestY * k);
            if (open) DrawRing(g, rest, k, Tip.Slice(offset.X / k, offset.Y / k, Inner), fore);
            else
                // A faint saucer under the cup, as on the Mac. No circle behind it.
                using (var saucer = new SolidBrush(Color.FromArgb(26, fore)))
                    g.FillEllipse(saucer, rest.X - 23 * k, rest.Y + (Cup / 2 - 2 - 5) * k, 46 * k, 10 * k);
            CupPainter.Draw(g, new PointF(rest.X + offset.X, rest.Y + offset.Y), Art * k, Pulled(offset, k));
        }

        /// <summary>The three sizes, fanned out under the cup's saucer; the one under the cup is lit.</summary>
        static void DrawRing(Graphics g, PointF rest, float k, int over, Color fore)
        {
            using (var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                for (int i = 0; i < 3; i++)
                {
                    // Small on the left: slice 0 spans 120°..180°, 1 spans 60°..120°, 2 spans 0°..60°.
                    double from = 120 - 60 * i + 1.5, to = 180 - 60 * i - 1.5;
                    using (var path = Wedge(rest, from, to, Inner * k, Outer * k))
                    {
                        if (i == over)
                        {
                            using (var gold = new SolidBrush(Color.FromArgb(140, RingPainter.Gold))) g.FillPath(gold, path);
                            using (var rim = new Pen(RingPainter.GoldRim)) g.DrawPath(rim, path);
                        }
                        else
                        {
                            using (var fill = new SolidBrush(Color.FromArgb(18, fore))) g.FillPath(fill, path);
                            using (var edge = new Pen(Color.FromArgb(40, fore))) g.DrawPath(edge, path);
                        }
                    }
                    if (i == over) continue; // the cup covers this slice; the line above names it
                    double mid = (from + to) / 2 * Math.PI / 180, r = (Inner + Outer) / 2 * k;
                    var at = new PointF((float)(rest.X + Math.Cos(mid) * r), (float)(rest.Y + Math.Sin(mid) * r));
                    using (var name = new Font("Segoe UI Semibold", 11 * k, GraphicsUnit.Pixel))
                    using (var price = new Font("Segoe UI", 10 * k, GraphicsUnit.Pixel))
                    using (var ink = new SolidBrush(fore))
                    {
                        g.DrawString(Tip.Names[i], name, ink, new PointF(at.X, at.Y - 7 * k), centred);
                        g.DrawString("$" + Tip.Dollars[i], price, ink, new PointF(at.X, at.Y + 7 * k), centred);
                    }
                }
        }
    }

    /// <summary>The tip address, to copy into a wallet app. DragShift itself never goes online.</summary>
    sealed class TipForm : Form
    {
        public TipForm(int size)
        {
            Ui.Dialog(this, size >= 0 ? Tip.Title(size) : "Buy me a coffee", 440, 190);
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20), WrapContents = false };
            flow.Controls.Add(Ui.Text(size >= 0 ? Tip.Title(size) : "Buy me a coffee", Ui.Title));
            flow.Controls.Add(Ui.Text("Send ETH or USDC on Ethereum to this address. Any amount works.", null, null, 396));
            var address = new TextBox
            {
                Text = Tip.Address, ReadOnly = true, Width = 396, Font = new Font("Consolas", 10f), Margin = new Padding(3, 10, 0, 0),
                AccessibleName = "Tip address",
            };
            flow.Controls.Add(address);
            var copy = Ui.Primary("Copy address");
            copy.BackColor = RingPainter.Gold;
            copy.Margin = new Padding(3, 12, 0, 0);
            copy.Click += (s, e) => { try { Clipboard.SetText(Tip.Address); copy.Text = "Copied"; } catch (System.Runtime.InteropServices.ExternalException) { address.SelectAll(); address.Focus(); } };
            flow.Controls.Add(copy);
            Controls.Add(flow);
            AcceptButton = copy;
        }
    }

    /// <summary>First run: three steps, a file to practise on, and two switches.</summary>
    sealed class WelcomeForm : Form
    {
        public WelcomeForm(string practiceFile = null)
        {
            Ui.Dialog(this, "Welcome to DragShift", 640, 470);
            TopMost = false;
            StartPosition = FormStartPosition.CenterScreen;
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(24), WrapContents = false };
            flow.Controls.Add(Ui.Text("Change any file while you drag it", Ui.Title));
            flow.Controls.Add(Ui.Text("1.  Start dragging any file.\n"
                                      + "2.  Press Shift (or click the right mouse button, or touch a second finger on the touchpad).\n"
                                      + "3.  Push the file toward a slice and let go. Or let go in the middle: the ring stays open, so click a slice.",
                                      null, null, 580));
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
            var tile = new PracticeTile(practiceFile) { Width = 120, Height = 120 };
            row.Controls.Add(tile);
            row.Controls.Add(Ui.Text("Try it here: drag this picture a little, then press Shift. Results land next to it, in DragShift's own folder, so your files stay untouched.",
                                     Ui.Small, Ui.Muted, 420));
            flow.Controls.Add(row);
            flow.Controls.Add(Ui.Text("Also: push a dragged file against the right edge of the screen for your favourites. Ctrl+Alt+V opens what you copied. "
                                      + "Open DragShift again any time to change its settings.",
                                      Ui.Small, Ui.Muted, 580));
            var start = new CheckBox { Text = "Start DragShift with Windows", Checked = true, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
            var sendTo = new CheckBox { Text = "Add it to the right-click \"Send to\" menu", Checked = true, AutoSize = true };
            flow.Controls.Add(start);
            flow.Controls.Add(sendTo);
            var ok = Ui.Primary("Got it");
            ok.Margin = new Padding(0, 14, 0, 0);
            ok.Click += (s, e) =>
            {
                try { Integration.StartsWithWindows = start.Checked; } catch (Exception ex) { Log.Line("startup: " + ex.Message); }
                try { Integration.InSendTo = sendTo.Checked; } catch (Exception ex) { Log.Line("send to: " + ex.Message); }
                Close();
            };
            flow.Controls.Add(ok);
            Controls.Add(flow);
            AcceptButton = ok;
        }
    }

    /// <summary>A sample picture you can drag to try the ring.</summary>
    sealed class PracticeTile : Control
    {
        readonly string path;
        Point down;

        public PracticeTile(string practiceFile = null)
        {
            path = practiceFile ?? Path.Combine(Paths.Local, "Practice", "Try Me.png");
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path))
                    using (var bmp = new Bitmap(800, 600))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        using (var b = new LinearGradientBrush(new Rectangle(0, 0, 800, 600), RingPainter.HotA, Color.FromArgb(124, 96, 230), 35f))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.FillRectangle(b, 0, 0, 800, 600);
                            using (var f = new Font("Segoe UI Semibold", 90)) using (var w = new SolidBrush(Color.White))
                                g.DrawString("Try me", f, w, new RectangleF(0, 0, 800, 600), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                        }
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }
            }
            catch (Exception e) { Log.Line("practice file: " + e.Message); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            try
            {
                using (var img = Image.FromFile(path))
                {
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(img, new Rectangle(6, 18, Width - 12, (Width - 12) * 3 / 4));
                }
            }
            catch (Exception) { }
        }

        protected override void OnMouseDown(MouseEventArgs e) { down = e.Location; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !File.Exists(path)) return;
            if (Math.Abs(e.X - down.X) + Math.Abs(e.Y - down.Y) < 6) return;
            DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
        }
    }
}
