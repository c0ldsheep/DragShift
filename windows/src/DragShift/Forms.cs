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
            var l = new Label { Text = text, Font = font ?? Body, AutoSize = true, ForeColor = color ?? SystemColors.ControlText };
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

    sealed class SettingsForm : Form
    {
        public event Action<bool> HotkeyChanged;
        public event Action<string> ShelfChanged;
        readonly AppMain app;
        Core.Settings S => app.Settings;

        public SettingsForm(AppMain app)
        {
            this.app = app;
            Ui.Dialog(this, "DragShift Settings", 560, 520);
            TopMost = false;
            StartPosition = FormStartPosition.CenterScreen;
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
            tabs.TabPages.Add(General());
            tabs.TabPages.Add(Triggers());
            tabs.TabPages.Add(Favorites());
            tabs.TabPages.Add(Saving());
            tabs.TabPages.Add(PrintingTab.Build(S));
            tabs.TabPages.Add(About());
            Controls.Add(tabs);
        }

        static FlowLayoutPanel Page(TabPage tab)
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(14), WrapContents = false, AutoScroll = true };
            tab.Controls.Add(flow);
            return flow;
        }

        CheckBox Check(FlowLayoutPanel flow, string text, string help, bool value, Action<bool> set)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            c.CheckedChanged += (s, e) => set(c.Checked);
            flow.Controls.Add(c);
            if (help != null) flow.Controls.Add(new Label { Text = help, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(18, 0, 0, 4) });
            return c;
        }

        TabPage General()
        {
            var tab = new TabPage("General");
            var f = Page(tab);
            Check(f, "Start DragShift with Windows", null, Integration.StartsWithWindows, v => Integration.StartsWithWindows = v);
            Check(f, "Add DragShift to the right-click \"Send to\" menu", "Explorer → right-click files → Send to → DragShift opens the ring for them.",
                  Integration.InSendTo, v => Integration.InSendTo = v);
            Check(f, "Put text it reads on the clipboard", "Picture → Text and PDF → Text.", S.GetBool(Keys.CopyText, true), v => S.Set(Keys.CopyText, v));
            Check(f, "Show the new file in Explorer", null, S.GetBool(Keys.Reveal, false), v => S.Set(Keys.Reveal, v));
            Check(f, "Play a sound when a file is ready", null, S.GetBool(Keys.Sound, false), v => S.Set(Keys.Sound, v));
            Check(f, "Make what I pick most glow when the ring opens", "After three picks in a row, that slice gets a soft pulsing edge.",
                  S.GetBool(Keys.UsualGlow, true), v => S.Set(Keys.UsualGlow, v));
            return tab;
        }

        TabPage Triggers()
        {
            var tab = new TabPage("Triggers");
            var f = Page(tab);
            Check(f, "Press Shift while dragging", "Or hold Shift first, then grab the file. Press Shift again for the next page.", S.Shift, v => S.Set(Keys.Shift, v));
            Check(f, "Click the mouse wheel while dragging", "For mice: hold the file with the left button, click the wheel (or a side button).", S.Mouse, v => S.Set(Keys.Mouse, v));
            Check(f, "Tap one more finger on the touchpad", "Drag with one finger, touch a second one down. Works on Precision Touchpads (most laptops since 2016).",
                  S.Finger, v => S.Set(Keys.Finger, v));
            Check(f, "Ctrl+Alt+V opens the clipboard in the ring", "Copy files in Explorer, or take a screenshot with Win+Shift+S, then press it.",
                  S.GetBool(Keys.Hotkey, true), v => { S.Set(Keys.Hotkey, v); HotkeyChanged?.Invoke(v); });
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
            row.Controls.Add(new Label { Text = "Favourites shelf (push a dragged file against the edge):", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            var side = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            side.Items.AddRange(new object[] { "Right edge", "Left edge", "Off" });
            var current = S.Get(Keys.Shelf, "right");
            side.SelectedIndex = current == "left" ? 1 : current == "off" ? 2 : 0;
            side.SelectedIndexChanged += (s, e) =>
            {
                var v = side.SelectedIndex == 1 ? "left" : side.SelectedIndex == 2 ? "off" : "right";
                S.Set(Keys.Shelf, v);
                ShelfChanged?.Invoke(v);
            };
            row.Controls.Add(side);
            f.Controls.Add(row);
            f.Controls.Add(new Label
            {
                Text = "Once the ring is open: push the file toward a slice and let go. Or let go in the middle and the ring stays open: glide to a slice and click it. Double-tap Shift to repeat the last thing you did. Esc closes it.",
                Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(0, 16, 0, 0),
            });
            return tab;
        }

        TabPage Favorites()
        {
            var tab = new TabPage("Favorites");
            var f = Page(tab);
            f.Controls.Add(Ui.Text("Ticked ones show first in the ring (★ Faves) and on the edge shelf. Only the ones that fit the dragged file appear.", Ui.Small, Ui.Muted, 500));
            var all = new List<(string Id, string Title)>();
            foreach (var s in Catalog.Everything(Capabilities.All)) all.Add((s.Id, s.Title));
            foreach (var r in Recipe.BuiltIns) all.Add((r.Id, r.Name + " (recipe)"));
            foreach (var kb in new[] { 20, 50, 100, 200 }) all.Add((SizeLimit.StepId(kb), "Under " + SizeLimit.Title(kb)));
            var faves = S.Favorites;
            var ordered = faves.Select(id => all.FirstOrDefault(a => a.Id == id)).Where(a => a.Id != null)
                               .Concat(all.Where(a => !faves.Contains(a.Id))).GroupBy(a => a.Id).Select(g => g.First()).ToList();
            var list = new CheckedListBox { Width = 400, Height = 330, CheckOnClick = true, Margin = new Padding(0, 8, 0, 0) };
            foreach (var a in ordered) list.Items.Add(new Item(a.Id, a.Title), faves.Contains(a.Id));
            void Save()
            {
                var ids = new List<string>();
                for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) ids.Add(((Item)list.Items[i]).Id);
                S.Set(Keys.Favorites, ids);
            }
            list.ItemCheck += (s, e) => BeginInvoke((Action)Save);
            var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
            var up = Ui.Plain("Move up");
            var down = Ui.Plain("Move down");
            var reset = Ui.Plain("Reset");
            void Move(int d)
            {
                int i = list.SelectedIndex, j = i + d;
                if (i < 0 || j < 0 || j >= list.Items.Count) return;
                var item = list.Items[i];
                bool c = list.GetItemChecked(i);
                list.Items.RemoveAt(i);
                list.Items.Insert(j, item);
                list.SetItemChecked(j, c);
                list.SelectedIndex = j;
                Save();
            }
            up.Click += (s, e) => Move(-1);
            down.Click += (s, e) => Move(1);
            reset.Click += (s, e) => { S.Set(Keys.Favorites, Catalog.DefaultFavorites); Close(); };
            side.Controls.AddRange(new Control[] { up, down, reset });
            var row = new FlowLayoutPanel { AutoSize = true };
            row.Controls.Add(list);
            row.Controls.Add(side);
            f.Controls.Add(row);
            f.Controls.Add(Ui.Text("Tip: right-click any slice while the ring is open to add or remove it.", Ui.Small, Ui.Muted, 500));
            return tab;
        }

        sealed class Item
        {
            public readonly string Id, Title;
            public Item(string id, string title) { Id = id; Title = title; }
            public override string ToString() => Title;
        }

        TabPage Saving()
        {
            var tab = new TabPage("Saving");
            var f = Page(tab);
            f.Controls.Add(Ui.Text("Save new files", Ui.Bold));
            var where = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(0, 4, 0, 0) };
            where.Items.AddRange(new object[] { "Next to the original", "Downloads", "A folder I choose…" });
            var mode = S.Get(Keys.OutputMode, "beside");
            where.SelectedIndex = mode == "downloads" ? 1 : mode == "folder" ? 2 : 0;
            var folderLabel = Ui.Text(S.Get(Keys.OutputFolder), Ui.Small, Ui.Muted, 480);
            where.SelectedIndexChanged += (s, e) =>
            {
                if (where.SelectedIndex == 2)
                {
                    using (var d = new FolderBrowserDialog { Description = "New files go here" })
                    {
                        if (d.ShowDialog(this) == DialogResult.OK) { S.Set(Keys.OutputFolder, d.SelectedPath); folderLabel.Text = d.SelectedPath; }
                        else { where.SelectedIndex = 0; return; }
                    }
                }
                S.Set(Keys.OutputMode, where.SelectedIndex == 1 ? "downloads" : where.SelectedIndex == 2 ? "folder" : "beside");
            };
            f.Controls.Add(where);
            f.Controls.Add(folderLabel);
            f.Controls.Add(Ui.Text("Originals are never changed. If a name is taken, DragShift adds 2, 3… like Explorer does.", Ui.Small, Ui.Muted, 500));

            f.Controls.Add(new Label { Text = "JPG quality: " + (int)(S.Quality * 100), AutoSize = true, Margin = new Padding(0, 12, 0, 0), Name = "q" });
            var quality = new TrackBar { Minimum = 50, Maximum = 100, Value = (int)(S.Quality * 100), Width = 300, TickFrequency = 10 };
            quality.ValueChanged += (s, e) => { S.Set(Keys.Quality, quality.Value / 100.0); f.Controls["q"].Text = "JPG quality: " + quality.Value; };
            f.Controls.Add(quality);

            f.Controls.Add(Ui.Text("Passport photo", Ui.Bold));
            var spec = S.Photo;
            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            var size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
            foreach (var p in PhotoSpec.Presets) size.Items.Add(p.Name);
            size.SelectedIndex = Math.Max(0, Array.FindIndex(PhotoSpec.Presets, p => p.Spec.WidthMM == spec.WidthMM && p.Spec.HeightMM == spec.HeightMM));
            var limit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            var limits = new[] { 20, 50, 100, 200, 240, 500, 0 };
            foreach (var kb in limits) limit.Items.Add(kb == 0 ? "No limit" : SizeLimit.Title(kb));
            limit.SelectedIndex = Math.Max(0, Array.IndexOf(limits, spec.MaxKB));
            var bg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            bg.Items.AddRange(new object[] { "White", "Light blue", "Keep the original" });
            bg.SelectedIndex = (int)spec.Background;
            void SavePhoto()
            {
                var p = PhotoSpec.Presets[Math.Max(0, size.SelectedIndex)].Spec.Copy();
                p.MaxKB = limits[Math.Max(0, limit.SelectedIndex)];
                p.Background = (PhotoBackground)Math.Max(0, bg.SelectedIndex);
                S.Set(Keys.Photo, p.ToString());
            }
            size.SelectedIndexChanged += (s, e) => SavePhoto();
            limit.SelectedIndexChanged += (s, e) => SavePhoto();
            bg.SelectedIndexChanged += (s, e) => SavePhoto();
            grid.Controls.Add(new Label { Text = "Size", AutoSize = true, Margin = new Padding(0, 6, 10, 0) });
            grid.Controls.Add(size);
            grid.Controls.Add(new Label { Text = "Keep it under", AutoSize = true, Margin = new Padding(0, 6, 10, 0) });
            grid.Controls.Add(limit);
            grid.Controls.Add(new Label { Text = "Background", AutoSize = true, Margin = new Padding(0, 6, 10, 0) });
            grid.Controls.Add(bg);
            f.Controls.Add(grid);
            f.Controls.Add(Ui.Text("Finds the face, centres it the way passport rules ask and saves a JPG at 300 dpi, so it prints at exactly the right size. A plain wall behind the person is changed to the colour you pick; a busy background is kept.",
                                   Ui.Small, Ui.Muted, 500));

            f.Controls.Add(new Label { Text = "Watermark text", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
            var mark = new TextBox { Width = 300, Text = S.Get(Keys.Watermark, "© " + Environment.UserName) };
            mark.TextChanged += (s, e) => S.Set(Keys.Watermark, mark.Text);
            f.Controls.Add(mark);
            return tab;
        }

        TabPage About()
        {
            var tab = new TabPage("About");
            var f = Page(tab);
            f.Controls.Add(Ui.Text("DragShift " + AppMain.Version, Ui.Title));
            f.Controls.Add(Ui.Text("Change any file while you drag it. Everything happens on this PC: no internet, no account, no tracking. Text reading and face finding use Windows' own on-device features.",
                                   null, Ui.Muted, 500));
            f.Controls.Add(Ui.Text("This PC: text reading " + (app.Caps.Ocr ? "on" : "off (add a language with OCR in Windows Settings)")
                                   + " · faces " + (app.Caps.Faces ? "on" : "off") + " · HEIC saving " + (app.Caps.HeicWrite ? "on" : "off (free \"HEIF Image Extensions\" in the Store)"),
                                   Ui.Small, Ui.Muted, 500));
            var open = Ui.Plain("Open Log");
            open.Margin = new Padding(0, 14, 0, 0);
            open.Click += (s, e) => { try { Process.Start(Paths.LogFile); } catch (Exception) { Shell.Reveal(new[] { Paths.LogFile }); } };
            f.Controls.Add(open);

            // Buy me a coffee: only here, never a popup. Drag the cup and press Shift for sizes, or use the button.
            var line = Ui.Text(Tip.Line(S.Fixed), null, null, 500);
            line.Margin = new Padding(0, 22, 0, 0);
            f.Controls.Add(line);
            var hint = Ui.Text(CoffeeCup.Closed, Ui.Small, Ui.Muted, 500);
            f.Controls.Add(hint);
            var cup = new CoffeeCup { Margin = new Padding(0, 6, 0, 0) };
            cup.Hint += text => hint.Text = text;
            cup.Picked += size => { using (var d = new TipForm(size)) d.ShowDialog(FindForm()); };
            f.Controls.Add(cup);
            var coffee = Ui.Plain("Buy me a coffee");
            coffee.Click += (s, e) => { using (var d = new TipForm(-1)) d.ShowDialog(FindForm()); };
            f.Controls.Add(coffee);
            return tab;
        }
    }

    /// <summary>The About tab's coffee cup, tipped with the app's own move: drag it, press Shift, and three sizes fan
    /// out under it; let go over one. A plain click opens the tip window too.</summary>
    sealed class CoffeeCup : Control
    {
        public const string Closed = "Drag the cup and press Shift, or just click it";
        const float Outer = 96, Inner = 36, Cup = 44;
        public event Action<int> Picked;
        public event Action<string> Hint;

        readonly Timer shiftWatch = new Timer { Interval = 40 };
        Point down;
        PointF offset;
        bool dragging, open, moved;

        public CoffeeCup()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = Natural;
            Cursor = Cursors.Hand;
            AccessibleName = "Buy me a coffee";
            // Shift pressed while the cup is held still moves nothing, so look at the key itself while dragging.
            shiftWatch.Tick += (s, e) => { if (dragging) See(); };
        }

        float K => Width / (Outer * 2 + 8);
        PointF Rest => new PointF(Width / 2f, (Cup / 2 + 6) * K);
        int Over => open ? Tip.Slice(offset.X / K, offset.Y / K, Inner) : -1;

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
            dragging = false; open = false; offset = PointF.Empty;
            Capture = false;
            shiftWatch.Stop();
            Hint?.Invoke(Closed);
            Invalidate();
            if (over >= 0) Picked?.Invoke(over);
            else if (click) Picked?.Invoke(-1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) shiftWatch.Dispose();
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
            Draw(e.Graphics, Width, Height, offset, open, Parent?.BackColor ?? SystemColors.Control, ForeColor);

        /// <summary>The size the cup's area needs at 100% scaling.</summary>
        public static Size Natural => new Size((int)(Outer * 2 + 8), (int)(Outer + Cup / 2 + 12));

        /// <summary>Draws the cup (and the ring, when open) with the cup moved by `offset` from its saucer. Static so
        /// the self-test can save pictures of it without opening a window.</summary>
        public static void Draw(Graphics g, int width, int height, PointF offset, bool open, Color back, Color fore)
        {
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            float k = width / (Outer * 2 + 8);
            var rest = new PointF(width / 2f, (Cup / 2 + 6) * k);
            int over = open ? Tip.Slice(offset.X / k, offset.Y / k, Inner) : -1;
            var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            if (open)
            {
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
            var cup = new RectangleF(rest.X + offset.X - Cup / 2 * k, rest.Y + offset.Y - Cup / 2 * k, Cup * k, Cup * k);
            using (var gold = new SolidBrush(RingPainter.Gold)) g.FillEllipse(gold, cup); // flat, no shine
            using (var rim = new Pen(RingPainter.GoldRim)) g.DrawEllipse(rim, cup);
            using (var emoji = new Font("Segoe UI Emoji", 20 * k, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(RingPainter.OnHot))
                g.DrawString("\u2615", emoji, ink, new RectangleF(cup.X, cup.Y + 1 * k, cup.Width, cup.Height), centred);
        }
    }

    /// <summary>The tip address, to copy into a wallet app. DragShift itself never goes online.</summary>
    sealed class TipForm : Form
    {
        public TipForm(int size)
        {
            Ui.Dialog(this, "Buy me a coffee", 440, 190);
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
        public WelcomeForm(AppMain app)
        {
            Ui.Dialog(this, "Welcome to DragShift", 640, 430);
            TopMost = false;
            StartPosition = FormStartPosition.CenterScreen;
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(24), WrapContents = false };
            flow.Controls.Add(Ui.Text("Change any file while you drag it", Ui.Title));
            flow.Controls.Add(Ui.Text("1.  Start dragging any file.\n"
                                      + "2.  Press Shift (or click the mouse wheel, or touch a second finger on the touchpad).\n"
                                      + "3.  Push the file toward a slice and let go. Or let go in the middle: the ring stays open, so click a slice.",
                                      null, null, 580));
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
            var tile = new PracticeTile { Width = 120, Height = 120 };
            row.Controls.Add(tile);
            row.Controls.Add(Ui.Text("Try it here: drag this picture a little, then press Shift. Results land next to it, in DragShift's own folder, so your files stay untouched.",
                                     Ui.Small, Ui.Muted, 420));
            flow.Controls.Add(row);
            flow.Controls.Add(Ui.Text("Also: push a dragged file against the right edge of the screen for your favourites. Ctrl+Alt+V opens what you copied.",
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
        readonly string path = Path.Combine(Paths.Local, "Practice", "Try Me.png");
        Point down;

        public PracticeTile()
        {
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
