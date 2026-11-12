using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DragShift
{
    /// <summary>How the Settings window looks, the same on every tab and close to the Mac app's: a light grey page, one column of
    /// white rounded cards in the middle of it with a small title above each, the words of a row on the left and its box or
    /// list on the right.</summary>
    static class Look
    {
        public static readonly Color Page = Color.FromArgb(243, 243, 245), Card = Color.White, Edge = Color.FromArgb(221, 221, 226),
                                     Rule = Color.FromArgb(235, 235, 239), Good = Color.FromArgb(40, 167, 69);
        /// <summary>The width of the column of cards, and of what fits inside a card, at 100%.</summary>
        public const int Column = 580, Inside = Column - 2 * 16;

        /// <summary>A rectangle with rounded corners of the given diameter.</summary>
        public static GraphicsPath Rounded(RectangleF r, float diameter)
        {
            float d = Math.Min(diameter, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>How many pixels a 100% pixel is here.</summary>
        public static float Scale(Control c) => Math.Max(1f, c.DeviceDpi / 96f);
    }

    /// <summary>A tab control that does not show its own row of tabs: the window draws its own (<see cref="TabStrip"/>) and the
    /// pages fill the whole control.</summary>
    sealed class FlatTabs : TabControl
    {
        const int TCM_ADJUSTRECT = 0x1328;

        /// <summary>The names are the window's own <see cref="TabStrip"/>, which is where the keyboard stops: this one is no stop.</summary>
        public FlatTabs() { TabStop = false; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TCM_ADJUSTRECT && !DesignMode) { m.Result = (IntPtr)1; return; } // "the pages need no room for tabs"
            base.WndProc(ref m);
        }

        /// <summary>Showing a page makes every box on it lay itself out, and each of those layouts asks the boxes around it to do the
        /// same again: on the Printing page that was seventy layouts and two seconds. So every box on the page is held still while it
        /// is shown, and the page is laid out once afterwards (which costs nothing when its size has not changed).</summary>
        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            var page = SelectedTab;
            var held = new List<Control>();
            if (page != null) Hold(page, held);
            try { base.OnSelectedIndexChanged(e); }
            finally { for (int i = held.Count - 1; i >= 0; i--) held[i].ResumeLayout(false); }
            page?.PerformLayout();
        }

        static void Hold(Control box, List<Control> held)
        {
            box.SuspendLayout();
            held.Add(box);
            foreach (Control child in box.Controls) if (child.HasChildren) Hold(child, held);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new PagesAccessible(this);

        /// <summary>What a screen reader gets from this control: just the pages. The names across the top are the strip's, so none is
        /// read twice, and not from the hidden row of the control itself.</summary>
        sealed class PagesAccessible : ControlAccessibleObject
        {
            readonly FlatTabs tabs;
            public PagesAccessible(FlatTabs owner) : base(owner) { tabs = owner; }
            public override AccessibleRole Role => AccessibleRole.Pane;
            public override int GetChildCount() => tabs.TabCount;
            public override AccessibleObject GetChild(int index) => index >= 0 && index < tabs.TabCount ? tabs.TabPages[index].AccessibilityObject : null;
        }
    }

    /// <summary>The names of the pages across the top of the Settings window: one equal cell for each page, the open one underlined.
    /// Click a name, or use the arrow keys.</summary>
    sealed class TabStrip : Control
    {
        readonly TabControl tabs;
        int hover = -1;

        public TabStrip(TabControl tabs)
        {
            this.tabs = tabs;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable, true);
            TabStop = true;
            Dock = DockStyle.Top;
            Height = 44;
            BackColor = Color.White;
            Font = Ui.Bold;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PageTabList;
            AccessibleName = "Settings pages";
            Text = "Settings pages"; // what UI Automation reads as its name (the strip draws its own text)
            tabs.SelectedIndexChanged += (s, e) =>
            {
                Invalidate();
                if (!IsHandleCreated || tabs.SelectedIndex < 0) return;
                AccessibilityNotifyClients(AccessibleEvents.Selection, tabs.SelectedIndex);
            };
        }

        /// <summary>Opens page <paramref name="i"/> and keeps the keyboard on these names: the control that holds the pages takes the focus
        /// when its page changes (it is hidden, so that is no place for it), and the arrow keys would then be heard by it instead.</summary>
        void Open(int i)
        {
            tabs.SelectedIndex = i;
            Focus();
            if (IsHandleCreated && Focused) AccessibilityNotifyClients(AccessibleEvents.Focus, i);
        }

        int CellAt(int x) => tabs.TabCount == 0 ? -1 : Math.Min(tabs.TabCount - 1, Math.Max(0, (int)((long)x * tabs.TabCount / Math.Max(1, Width))));

        /// <summary>Where the name of page <paramref name="i"/> sits across the strip.</summary>
        Rectangle Cell(int i)
        {
            float cell = Width / (float)Math.Max(1, tabs.TabCount);
            int left = (int)Math.Round(i * cell), right = (int)Math.Round((i + 1) * cell);
            return new Rectangle(left, 0, right - left, Height);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new StripAccessible(this);

        /// <summary>What a screen reader gets: a list of pages, each one a named tab that can be switched to.</summary>
        sealed class StripAccessible : ControlAccessibleObject
        {
            readonly TabStrip strip;
            public StripAccessible(TabStrip owner) : base(owner) { strip = owner; }
            public override int GetChildCount() => strip.tabs.TabCount;
            public override AccessibleObject GetChild(int index) => index >= 0 && index < strip.tabs.TabCount ? new TabAccessible(strip, this, index) : null;
            public override AccessibleObject GetSelected() => GetChild(strip.tabs.SelectedIndex);
            public override AccessibleObject GetFocused() => strip.Focused ? GetChild(strip.tabs.SelectedIndex) : null;

            public override AccessibleObject HitTest(int x, int y)
            {
                var at = strip.PointToClient(new Point(x, y));
                for (int i = 0; i < strip.tabs.TabCount; i++) if (strip.Cell(i).Contains(at)) return GetChild(i);
                return base.HitTest(x, y);
            }

            public override AccessibleObject Navigate(AccessibleNavigation direction)
            {
                if (direction == AccessibleNavigation.FirstChild) return GetChild(0);
                if (direction == AccessibleNavigation.LastChild) return GetChild(strip.tabs.TabCount - 1);
                return base.Navigate(direction);
            }
        }

        /// <summary>One name across the strip, as a screen reader sees it.</summary>
        sealed class TabAccessible : AccessibleObject
        {
            readonly TabStrip strip;
            readonly AccessibleObject parent;
            readonly int index;
            public TabAccessible(TabStrip strip, AccessibleObject parent, int index) { this.strip = strip; this.parent = parent; this.index = index; }

            public override AccessibleObject Parent => parent;
            public override string Name { get => strip.tabs.TabPages[index].Text; set { } }
            public override AccessibleRole Role => AccessibleRole.PageTab;
            public override string DefaultAction => "Switch";
            public override Rectangle Bounds => strip.RectangleToScreen(strip.Cell(index));

            public override AccessibleStates State
            {
                get
                {
                    var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (strip.tabs.SelectedIndex == index) { state |= AccessibleStates.Selected; if (strip.Focused) state |= AccessibleStates.Focused; }
                    return state;
                }
            }

            public override void DoDefaultAction() => strip.Open(index);
            public override void Select(AccessibleSelection flags) { if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.TakeFocus)) != 0) DoDefaultAction(); }

            public override AccessibleObject Navigate(AccessibleNavigation direction)
            {
                switch (direction)
                {
                    case AccessibleNavigation.Next: case AccessibleNavigation.Right: return parent.GetChild(index + 1);
                    case AccessibleNavigation.Previous: case AccessibleNavigation.Left: return parent.GetChild(index - 1);
                }
                return base.Navigate(direction);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) { int cell = CellAt(e.X); if (cell != hover) { hover = cell; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int cell = CellAt(e.X);
            if (e.Button == MouseButtons.Left && cell >= 0) Open(cell);
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys key) => key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int last = tabs.TabCount - 1, at = tabs.SelectedIndex;
            int to = e.KeyCode == Keys.Left ? Math.Max(0, at - 1) : e.KeyCode == Keys.Right ? Math.Min(last, at + 1) : e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? last : at;
            if (to != at && to >= 0) { Open(to); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (tabs.TabCount == 0) return;
            float s = Look.Scale(this);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            for (int i = 0; i < tabs.TabCount; i++)
            {
                var box = Cell(i);
                box.Height -= 1;
                bool open = i == tabs.SelectedIndex;
                if (!open && i == hover) using (var wash = new SolidBrush(Color.FromArgb(247, 247, 249))) g.FillRectangle(wash, box);
                string name = tabs.TabPages[i].Text;
                var ink = open || i == hover ? Color.FromArgb(28, 28, 30) : Ui.Muted;
                TextRenderer.DrawText(g, name, Font, new Rectangle(box.X, box.Y, box.Width, box.Height - (int)(2 * s)), ink, flags);
                if (open)
                {
                    int text = TextRenderer.MeasureText(g, name, Font, Size.Empty, flags).Width, line = text + (int)(28 * s);
                    var bar = new RectangleF(box.X + (box.Width - line) / 2f, Height - 1 - 3 * s, line, 3 * s);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = Look.Rounded(bar, 3 * s))
                    using (var brush = new LinearGradientBrush(bar, RingPainter.HotA, RingPainter.HotB, LinearGradientMode.Horizontal)) g.FillPath(brush, path);
                    if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(box, -(int)(10 * s), -(int)(6 * s)));
                }
            }
            using (var edge = new Pen(Look.Edge)) g.DrawLine(edge, 0, Height - 1, Width, Height - 1);
        }
    }

    /// <summary>One Settings tab's page: a light grey with one column of cards of a fixed width in the middle of it, however wide
    /// the window is. It only scrolls when the window is too short for what is on it.</summary>
    sealed class Pane : TableLayoutPanel
    {
        /// <summary>Where the cards (and the titles and notes between them) go, top to bottom.</summary>
        public readonly FlowLayoutPanel Column;

        public Pane(TabPage tab)
        {
            tab.UseVisualStyleBackColor = false;
            tab.BackColor = Look.Page;
            Dock = DockStyle.Fill;
            BackColor = Look.Page;
            AutoScroll = true;
            ColumnCount = 3;
            RowCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Column = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 14, 0, 12), BackColor = Look.Page, MinimumSize = new Size(Look.Column, 0),
            };
            Controls.Add(Column, 1, 0);
            tab.Controls.Add(this);
        }
    }

    /// <summary>A white rounded box that holds rows one under the other, with a thin line between them.</summary>
    sealed class Card : TableLayoutPanel
    {
        public Card()
        {
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = MaximumSize = new Size(Look.Column, 0);
            Padding = new Padding(16, 2, 16, 2);
            Margin = new Padding(0);
            BackColor = Look.Card;
        }

        /// <summary>Puts a row at the bottom of the card, as wide as the card.</summary>
        public Card Add(Control row)
        {
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(0, 10, 0, 10);
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RowCount = RowStyles.Count;
            Controls.Add(row, 0, RowStyles.Count - 1);
            return this;
        }

        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Look.Page); // the corners show the page
            float s = Look.Scale(this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Look.Rounded(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 10 * s))
            using (var fill = new SolidBrush(Look.Card))
            using (var edge = new Pen(Look.Edge))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
            g.SmoothingMode = SmoothingMode.Default;
            // A thin line above every row that is showing, except the first one (a row that is hidden has no height and no line).
            int y = Padding.Top;
            bool first = true;
            using (var rule = new Pen(Look.Rule))
                foreach (int height in GetRowHeights())
                {
                    if (height <= 0) continue;
                    if (!first) g.DrawLine(rule, Padding.Left, y, Width - Padding.Right - 1, y);
                    first = false;
                    y += height;
                }
        }
    }

    /// <summary>The kinds of row a card holds, and the small texts between cards.</summary>
    static class Rows
    {
        /// <summary>The words on the left, the tick box on the right, and an optional line of help under the words.</summary>
        public static TableLayoutPanel Toggle(string text, string help, bool value, Action<bool> set, out CheckBox box)
        {
            var row = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, BackColor = Look.Card };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            box = new CheckBox
            {
                Text = text, Checked = value, AutoSize = false, Height = 22, Dock = DockStyle.Fill, Margin = new Padding(0), UseMnemonic = false,
                CheckAlign = ContentAlignment.MiddleRight, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand,
            };
            var c = box;
            box.CheckedChanged += (s, e) => set(c.Checked);
            row.Controls.Add(box, 0, 0);
            if (help != null)
            {
                row.Controls.Add(new Label
                {
                    Text = help, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, UseMnemonic = false,
                    MaximumSize = new Size(Look.Inside - 44, 0), Margin = new Padding(0, 1, 0, 0), Anchor = AnchorStyles.Left,
                }, 0, 1);
            }
            return row;
        }

        /// <summary>The words on the left and a control (a list to pick from, a box to type in…) on the right.</summary>
        public static Control Choice(string label, Control control)
        {
            // At least 28 px tall: a drop-down or a text box reports a few pixels less to the layout than it measures once its window
            // exists, and would be cut off at the top and the bottom.
            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Look.Card, MinimumSize = new Size(0, 28) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            // The words fill the whole row from the top to the bottom and sit in the middle of it, so they line up with the control
            // whatever its height (a drop-down, a slider…).
            var words = Ui.Text(label);
            words.AutoSize = false;
            words.Dock = DockStyle.Fill;
            words.Height = 22;
            words.TextAlign = ContentAlignment.MiddleLeft;
            words.AutoEllipsis = true;
            words.Margin = new Padding(0, 0, 10, 0);
            control.Anchor = AnchorStyles.Right;
            control.Margin = new Padding(0);
            row.Controls.Add(words, 0, 0);
            row.Controls.Add(control, 1, 0);
            return row;
        }

        /// <summary>A few small grey words, as wide as a card.</summary>
        public static Label Note(string text, int below = 0) => new Label
        {
            Text = text, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, UseMnemonic = false,
            MaximumSize = new Size(Look.Column - 12, 0), Margin = new Padding(6, 8, 0, below),
        };

        /// <summary>The small title above a card (the first one on a page sits closer to the top).</summary>
        public static Label Section(string title, bool first = false) => new Label
        {
            Text = title, Font = new Font("Segoe UI Semibold", 9f), ForeColor = Ui.Muted, AutoSize = true, UseMnemonic = false,
            Margin = new Padding(6, first ? 2 : 18, 0, 6),
        };
    }

    /// <summary>"✓ Text reading — on": a green tick or a grey dash, the name, and (greyer) how it stands. The name ends where the
    /// first " — " is.</summary>
    sealed class StatusRow : Label
    {
        bool good;

        public StatusRow() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); AutoSize = true; Font = Ui.Body; UseMnemonic = false; }

        public bool Good { get => good; set { good = value; Invalidate(); } }

        static readonly TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;

        public override Size GetPreferredSize(Size proposed)
        {
            float s = Look.Scale(this);
            var text = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), Flags);
            return new Size((int)(23 * s) + text.Width + 2, Math.Max(text.Height + 6, (int)(18 * s)));
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Look.Scale(this), d = 15 * s, top = (Height - d) / 2f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var circle = new RectangleF(1, top, d, d);
            if (good)
            {
                using (var green = new SolidBrush(Look.Good)) g.FillEllipse(green, circle);
                using (var tick = new Pen(Color.White, 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(tick, new[] { new PointF(circle.X + d * 0.27f, top + d * 0.53f), new PointF(circle.X + d * 0.44f, top + d * 0.70f), new PointF(circle.X + d * 0.74f, top + d * 0.32f) });
            }
            else
            {
                using (var ring = new Pen(Color.FromArgb(150, 150, 156), 1.4f * s)) g.DrawEllipse(ring, circle.X + 0.7f * s, circle.Y + 0.7f * s, d - 1.4f * s, d - 1.4f * s);
                using (var dash = new Pen(Color.FromArgb(150, 150, 156), 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(dash, circle.X + d * 0.3f, top + d / 2, circle.X + d * 0.7f, top + d / 2);
            }
            int at = Text.IndexOf(" — ", StringComparison.Ordinal);
            string name = at < 0 ? Text : Text.Substring(0, at), rest = at < 0 ? "" : Text.Substring(at);
            int x = (int)(23 * s);
            var box = new Rectangle(x, 0, Width - x, Height);
            TextRenderer.DrawText(g, name, Font, box, ForeColor, Flags);
            if (rest.Length > 0)
            {
                int used = TextRenderer.MeasureText(g, name, Font, Size.Empty, Flags).Width;
                TextRenderer.DrawText(g, rest, Font, new Rectangle(x + used, 0, Math.Max(1, Width - x - used), Height), Ui.Muted, Flags);
            }
        }
    }

    /// <summary>A line of text with one stretch of it in bold ("DragShift has fixed <b>214 files</b> for you…"). Its Text is the
    /// whole line, so it reads as one line to anyone looking.</summary>
    sealed class RichLine : Label
    {
        string bold = "";

        public RichLine() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); AutoSize = true; Font = Ui.Body; UseMnemonic = false; }

        /// <summary>The part of Text that is shown in bold.</summary>
        public string Bold
        {
            get => bold;
            set
            {
                bold = value ?? "";
                if (AutoSize) Size = GetPreferredSize(Size.Empty); // the bold part is wider than the plain one: measure again
                Invalidate();
            }
        }

        static readonly TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;

        void Pieces(out string before, out string middle, out string after)
        {
            int at = bold.Length == 0 ? -1 : Text.IndexOf(bold, StringComparison.Ordinal);
            if (at < 0) { before = Text; middle = after = ""; return; }
            before = Text.Substring(0, at); middle = bold; after = Text.Substring(at + bold.Length);
        }

        public override Size GetPreferredSize(Size proposed)
        {
            Pieces(out var a, out var b, out var c);
            var big = new Size(int.MaxValue, int.MaxValue);
            using (var heavy = new Font(Font, FontStyle.Bold))
            {
                var one = TextRenderer.MeasureText(a, Font, big, Flags);
                var two = b.Length == 0 ? Size.Empty : TextRenderer.MeasureText(b, heavy, big, Flags);
                var three = c.Length == 0 ? Size.Empty : TextRenderer.MeasureText(c, Font, big, Flags);
                return new Size(one.Width + two.Width + three.Width + 2, Math.Max(one.Height, two.Height) + 2);
            }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Pieces(out var a, out var b, out var c);
            var g = e.Graphics;
            int x = 0;
            using (var heavy = new Font(Font, FontStyle.Bold))
                foreach (var piece in new[] { (a, Font), (b, heavy), (c, Font) })
                {
                    if (piece.Item1.Length == 0) continue;
                    int w = TextRenderer.MeasureText(g, piece.Item1, piece.Item2, Size.Empty, Flags).Width;
                    TextRenderer.DrawText(g, piece.Item1, piece.Item2, new Rectangle(x, 0, Math.Max(1, Width - x), Height), ForeColor, Flags);
                    x += w;
                }
        }
    }

    /// <summary>The app's own icon, drawn sharp at any size from the biggest picture inside the exe.</summary>
    sealed class IconView : Control
    {
        readonly Bitmap picture;

        public IconView(int size)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(size, size);
            TabStop = false;
            picture = Load();
        }

        /// <summary>False when the program's icon could not be read (the space stays empty).</summary>
        public bool HasPicture => picture != null;

        static Bitmap Load()
        {
            try
            {
                var icons = new IntPtr[1];
                var ids = new int[1];
                uint got = Native.PrivateExtractIcons(Paths.Exe, 0, 256, 256, icons, ids, 1, 0);
                if (got >= 1 && got != uint.MaxValue && icons[0] != IntPtr.Zero)
                    try { using (var icon = Icon.FromHandle(icons[0])) return icon.ToBitmap(); }
                    finally { Native.DestroyIcon(icons[0]); }
            }
            catch (Exception) { }
            try { using (var icon = Icon.ExtractAssociatedIcon(Paths.Exe)) return icon?.ToBitmap(); }
            catch (Exception) { return null; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (picture == null) return;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(picture, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) picture?.Dispose();
            base.Dispose(disposing);
        }
    }
}
