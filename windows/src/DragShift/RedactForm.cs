using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DragShift.Core;
using DragShift.Engine;

namespace DragShift
{
    /// <summary>
    /// Redact. The picture with a box over everything private that was found (emails, phone and ID numbers, cards, bank
    /// accounts, and faces if asked). Click a box to turn it off; drag on the picture to add your own. Nothing is changed
    /// until Save Redacted, which saves a new copy: the original is never touched. Everything happens on this PC.
    /// </summary>
    sealed class RedactForm : Form
    {
        public const string Looking = "Looking for private details…";
        public const string Nothing = "Nothing found. Drag on the picture to cover anything.";
        public const string CantRead = "Windows couldn't read the text here. Draw boxes over what you want hidden.";
        public const string CantFindFaces = "This PC can't look for faces.";
        public const string Hint = "Drag on the picture to add a box. Click a box to turn it off.";

        /// <summary>Raised once, after the window has closed, with the boxes that are on (the picture's own pixels) and the style.</summary>
        public event Action<IList<Box>, RedactStyle> Chosen;

        internal sealed class Item
        {
            public string Label;
            public Box Rect; // the picture's own pixels
            public bool On = true;
            public CheckBox Check;
        }

        readonly string path;
        readonly Core.Settings settings;
        readonly Func<string, Action<RedactOps.Scan>, Task<RedactOps.Scan>> look;
        readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
        readonly PictureView view = new PictureView();
        readonly FlowLayoutPanel list;
        readonly Label note;
        readonly CheckBox faces;
        readonly Button save;
        readonly RadioButton[] styles = new RadioButton[3];
        RedactStyle style;
        RedactOps.Scan scan;
        bool scanning = true, started;
        string failure;
        List<Item> found = new List<Item>(), faceItems = new List<Item>(), mine = new List<Item>(), shown = new List<Item>();

        /// <param name="look">How the picture is read; the self-test hands in one that pretends there is no text reader.</param>
        public RedactForm(string path, Core.Settings settings, Func<string, Action<RedactOps.Scan>, Task<RedactOps.Scan>> look = null)
        {
            this.path = path;
            this.settings = settings;
            this.look = look ?? RedactOps.Look;
            style = settings.RedactStyle;
            // Big, but never bigger than the screen it opens on.
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            float k = DeviceDpi / 96f;
            int w = (int)Math.Max(640, Math.Min(940, work.Width / k - 40)), h = (int)Math.Max(460, Math.Min(600, work.Height / k - 90));
            Ui.Dialog(this, "Redact", w, h);
            TopMost = false;
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            view.Dock = DockStyle.Fill;
            view.Margin = new Padding(16);
            view.Style = style;
            root.Controls.Add(view, 0, 0);

            var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0, 16, 16, 16) };
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int textWidth = 268;
            Control Row(Control c, int top = 0, int bottom = 0)
            {
                c.Margin = new Padding(0, top, 0, bottom);
                side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                side.Controls.Add(c, 0, side.RowStyles.Count - 1);
                return c;
            }
            Row(Ui.Text(Names.Short(Path.GetFileName(path), 34), Ui.Bold, null, textWidth));
            Row(Ui.Text("Saved as a new copy with no hidden info", Ui.Small, Ui.Muted, textWidth), 0, 12);
            Row(Ui.Text("Hide", new Font("Segoe UI Semibold", 8.5f), Ui.Muted));
            note = Ui.Text(Looking, Ui.Small, Ui.Muted, textWidth);
            Row(note, 2, 2);
            list = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window, Padding = new Padding(6, 4, 4, 4),
            };
            list.Margin = new Padding(0, 2, 0, 8);
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            side.Controls.Add(list, 0, side.RowStyles.Count - 1);
            faces = new CheckBox { Text = "Find faces too", AutoSize = true, Checked = true, Enabled = false };
            Row(faces, 0, 8);
            Row(Ui.Text("Style", new Font("Segoe UI Semibold", 8.5f), Ui.Muted));
            var radios = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var all = new[] { RedactStyle.Bar, RedactStyle.Pixelate, RedactStyle.Blur };
            for (int i = 0; i < all.Length; i++)
            {
                var which = all[i];
                var rb = new RadioButton { Text = which.Title(), AutoSize = true, Checked = which == style, Margin = new Padding(0, 2, 12, 2) };
                rb.CheckedChanged += (s, e) =>
                {
                    if (!rb.Checked) return;
                    style = which;
                    view.Style = style;
                    view.Invalidate();
                };
                styles[i] = rb;
                radios.Controls.Add(rb);
            }
            Row(radios, 2, 8);
            Row(Ui.Text(Hint, Ui.Small, Ui.Muted, textWidth), 0, 10);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            save = Ui.Primary("Save Redacted").DimWhenOff();
            save.Enabled = false;
            var cancel = Ui.Plain("Cancel");
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            Row(buttons);
            root.Controls.Add(side, 1, 0);
            Controls.Add(root);
            AcceptButton = save;
            CancelButton = cancel;

            faces.CheckedChanged += (s, e) => Rebuild();
            save.Click += (s, e) => Go();
            cancel.Click += (s, e) => Close();
            view.Clicked += item => item.Check.Checked = !item.Check.Checked;
            view.Drew += box =>
            {
                mine.Add(new Item { Label = "Your box", Rect = box });
                Rebuild();
            };
        }

        // For the self-test.
        internal Task Ready => ready.Task;
        internal PictureView View => view;
        internal CheckBox FacesBox => faces;
        internal Button SaveButton => save;
        internal Label Note => note;
        internal RadioButton[] StyleChoices => styles;
        internal IList<string> Labels => shown.Select(i => i.Label).ToList();
        internal IList<Box> BoxesOn => shown.Where(i => i.On).Select(i => i.Rect).ToList();
        internal RedactStyle Style => style;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Begin();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                view.Picture = null;
                scan?.Dispose();
                ready.TrySetResult(true); // nobody is left waiting for a window that has gone
            }
            base.Dispose(disposing);
        }

        /// <summary>Runs <paramref name="work"/> on the window's own thread, whichever thread asks. False when the window is
        /// already gone. (It names the window rather than relying on a synchronization context, so it behaves the same
        /// whoever pumps the messages.)</summary>
        bool OnWindow(Action work)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return false;
                BeginInvoke(work);
                return true;
            }
            catch (InvalidOperationException) { return false; } // the window closed meanwhile (this includes ObjectDisposedException)
        }

        /// <summary>Opens the picture and reads it. Once only: the first time the window is shown.</summary>
        internal async void Begin()
        {
            if (started) return;
            started = true;
            if (!IsHandleCreated) CreateHandle(); // work is handed back to the window, so the window has to exist
            RedactOps.Scan result = null;
            Exception problem = null;
            try
            {
                // The picture appears as soon as it is open; what was found is added when the reading is done.
                result = await Task.Run(() => look(path, early => OnWindow(() => Opened(early)))).ConfigureAwait(false);
            }
            catch (Exception e) { problem = e; }
            // From here on this may be any thread.
            if (!OnWindow(() => Finish(result, problem)))
            {
                result?.Dispose();
                ready.TrySetResult(true);
            }
        }

        void Finish(RedactOps.Scan result, Exception problem)
        {
            try
            {
                if (IsDisposed) { result?.Dispose(); return; }
                if (problem != null)
                {
                    scanning = false;
                    failure = problem is UserError ? problem.Message : Runner.Message(problem);
                    Rebuild();
                    return;
                }
                Opened(result);
                found = result.Text.Select(f => new Item { Label = f.Label, Rect = f.Rect }).ToList();
                faceItems = Redaction.AddFaces(result.Text, result.Faces).Skip(result.Text.Count)
                    .Select(f => new Item { Label = f.Label, Rect = f.Rect }).ToList();
                faces.Enabled = result.CanFindFaces;
                faces.Checked = result.CanFindFaces; // the Mac's default: on
                scanning = false;
                Rebuild();
            }
            catch (Exception e)
            {
                // A mistake while drawing the list must never end the program: say so in the window instead.
                scanning = false;
                failure = Runner.Message(e);
                try { Rebuild(); } catch (Exception) { }
            }
            finally { ready.TrySetResult(true); }
        }

        void Opened(RedactOps.Scan s)
        {
            if (IsDisposed) return;
            scan = s;
            view.Picture = s.Preview;
            view.PixelSize = new Size(s.Width, s.Height);
            view.Invalidate();
        }

        /// <summary>Lists what is found (and what you drew) on the right, and on the picture.</summary>
        void Rebuild()
        {
            shown = found.Concat(faces.Checked ? faceItems : new List<Item>()).Concat(mine).ToList();
            list.SuspendLayout();
            list.Controls.Clear();
            foreach (var item in shown)
            {
                if (item.Check == null)
                {
                    var it = item;
                    it.Check = new CheckBox { Text = it.Label, AutoSize = true, Checked = it.On, Margin = new Padding(0, 2, 0, 2) };
                    it.Check.CheckedChanged += (s, e) =>
                    {
                        it.On = it.Check.Checked;
                        view.Invalidate();
                        UpdateSave();
                    };
                }
                list.Controls.Add(item.Check);
            }
            list.ResumeLayout();
            view.Items = shown;
            view.Busy = scanning;
            view.Invalidate();
            UpdateNote();
            UpdateSave();
        }

        void UpdateNote()
        {
            var lines = new List<string>();
            if (scanning) lines.Add(Looking);
            else if (failure != null) lines.Add(failure);
            else
            {
                if (scan != null && !scan.CanRead) lines.Add(CantRead);
                else if (shown.Count == 0) lines.Add(Nothing);
                if (scan != null && !scan.CanFindFaces) lines.Add(CantFindFaces);
            }
            note.Text = string.Join("\n", lines);
            note.Visible = lines.Count > 0;
        }

        /// <summary>Presses Save Redacted (when it can be pressed).</summary>
        internal void SaveNow() => Go();

        void UpdateSave() => save.Enabled = !scanning && failure == null && shown.Any(i => i.On);

        void Go()
        {
            if (!save.Enabled) return;
            settings.RedactStyle = style; // remembered for next time
            var boxes = BoxesOn;
            var chosenStyle = style;
            Close();
            Chosen?.Invoke(boxes, chosenStyle);
        }

        /// <summary>The picture, shrunk to fit, with the boxes over it.</summary>
        internal sealed class PictureView : Control
        {
            public Bitmap Picture;
            public Size PixelSize;
            public IList<Item> Items = new List<Item>();
            public RedactStyle Style = RedactStyle.Bar;
            public bool Busy;
            /// <summary>The box being dragged, in this control's own pixels.</summary>
            public Rectangle Band;

            /// <summary>A click on a box (not a drag), and a box drawn by dragging (the picture's own pixels).</summary>
            internal event Action<Item> Clicked;
            internal event Action<Box> Drew;

            Point? down;
            Item hit;
            bool dragging;

            public PictureView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(232, 232, 236);
                Cursor = Cursors.Cross;
                AccessibleName = "The picture, with a box over everything private";
            }

            /// <summary>Where the picture sits in this control (shrunk to fit, or up to four times bigger for a tiny one).</summary>
            public RectangleF Fit
            {
                get
                {
                    if (PixelSize.Width < 1 || PixelSize.Height < 1 || Width < 1 || Height < 1) return RectangleF.Empty;
                    float s = Math.Min(4f, Math.Min(Width / (float)PixelSize.Width, Height / (float)PixelSize.Height));
                    float w = PixelSize.Width * s, h = PixelSize.Height * s;
                    return new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
                }
            }

            public RectangleF ToView(Box b)
            {
                var fit = Fit;
                float s = fit.Width / PixelSize.Width;
                return new RectangleF(fit.X + (float)b.X * s, fit.Y + (float)b.Y * s, (float)b.Width * s, (float)b.Height * s);
            }

            public Box ToPixels(Rectangle r)
            {
                var fit = Fit;
                double s = PixelSize.Width / (double)fit.Width;
                return new Box((r.X - fit.X) * s, (r.Y - fit.Y) * s, r.Width * s, r.Height * s);
            }

            Point Inside(Point p)
            {
                var f = Fit;
                return new Point((int)Math.Max(f.Left, Math.Min(f.Right, p.X)), (int)Math.Max(f.Top, Math.Min(f.Bottom, p.Y)));
            }

            // Mouse: a press and release without moving is a click; moving more than a few pixels draws a new box.
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) Press(e.Location); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Drag(e.Location); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Release(e.Location); }

            internal void Press(Point p)
            {
                if (Picture == null || Fit.IsEmpty) return;
                down = p;
                dragging = false;
                hit = null;
                for (int i = Items.Count - 1; i >= 0; i--)
                    if (ToView(Items[i].Rect).Contains(p)) { hit = Items[i]; break; }
                Capture = true;
            }

            internal void Drag(Point p)
            {
                if (down == null) return;
                if (!dragging && (Math.Abs(p.X - down.Value.X) >= 4 || Math.Abs(p.Y - down.Value.Y) >= 4)) dragging = true;
                if (!dragging) return;
                Point a = Inside(down.Value), b = Inside(p);
                Band = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                Invalidate();
            }

            internal void Release(Point p)
            {
                if (down == null) return;
                Capture = false;
                Drag(p);
                var band = Band;
                bool drew = dragging, clicked = !dragging && hit != null;
                var target = hit;
                down = null; hit = null; dragging = false; Band = Rectangle.Empty;
                Invalidate();
                if (drew && band.Width > 6 && band.Height > 6) Drew?.Invoke(ToPixels(band));
                else if (clicked) Clicked?.Invoke(target);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                var fit = Fit;
                if (Picture != null && !fit.IsEmpty)
                {
                    g.InterpolationMode = fit.Width > PixelSize.Width ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(Picture, fit);
                    using (var edge = new Pen(Color.FromArgb(48, 0, 0, 0))) g.DrawRectangle(edge, fit.X, fit.Y, fit.Width - 1, fit.Height - 1);
                    g.SmoothingMode = SmoothingMode.None;
                    int fill = Style == RedactStyle.Bar ? 217 : 90; // the black bar is nearly solid; the others only tint
                    foreach (var m in Items)
                    {
                        var r = ToView(m.Rect);
                        if (m.On)
                        {
                            using (var b = new SolidBrush(Color.FromArgb(fill, Redaction.BarRed, Redaction.BarGreen, Redaction.BarBlue))) g.FillRectangle(b, r);
                            using (var p = new Pen(RingPainter.HotB, 2)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                        }
                        else
                        {
                            // Two colours, so the outline shows on a light picture and on a dark one.
                            using (var p = new Pen(Color.White, 2)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                            using (var p = new Pen(Color.FromArgb(70, 70, 76), 1) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                        }
                    }
                    if (!Band.IsEmpty)
                        using (var p = new Pen(RingPainter.HotA, 2)) g.DrawRectangle(p, Band);
                }
                if (Busy)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    using (var font = new Font("Segoe UI Semibold", 10f))
                    {
                        var size = g.MeasureString(RedactForm.Looking, font);
                        var pill = new RectangleF((Width - size.Width) / 2f - 16, (Height - size.Height) / 2f - 8, size.Width + 32, size.Height + 16);
                        using (var path = Rounded(pill, pill.Height / 2))
                        {
                            using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) g.FillPath(b, path);
                            using (var p = new Pen(Color.FromArgb(40, 0, 0, 0))) g.DrawPath(p, path);
                        }
                        using (var b = new SolidBrush(Color.FromArgb(40, 40, 45))) g.DrawString(RedactForm.Looking, font, b, pill.X + 16, pill.Y + 8);
                    }
                }
            }

            static GraphicsPath Rounded(RectangleF r, float d)
            {
                var path = new GraphicsPath();
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }
    }
}
