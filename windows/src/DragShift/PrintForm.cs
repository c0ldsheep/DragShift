using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DragShift.Core;
using DragShift.Engine;

namespace DragShift
{
    /// <summary>The printer, paper and options for one preset. Used by the print window and by Settings → Printing.</summary>
    sealed class PrintOptionsPanel : TableLayoutPanel
    {
        public event Action Changed;
        readonly ComboBox printer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        readonly ComboBox paper = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        readonly NumericUpDown copies = new NumericUpDown { Minimum = 1, Maximum = 99, Width = 60 };
        readonly RadioButton colour = new RadioButton { Text = "Colour", AutoSize = true, Checked = true };
        readonly RadioButton mono = new RadioButton { Text = "Black & white", AutoSize = true, UseMnemonic = false }; // or Windows eats the "&"
        readonly CheckBox twoSided = new CheckBox { Text = "Print on both sides", AutoSize = true };
        readonly RadioButton fit = new RadioButton { Text = "Fit", AutoSize = true, Checked = true };
        readonly RadioButton fill = new RadioButton { Text = "Fill", AutoSize = true };
        readonly RadioButton actual = new RadioButton { Text = "Actual size", AutoSize = true };
        readonly CheckBox fillSheet = new CheckBox { Text = "Fill the sheet with copies (passport photos)", AutoSize = true };
        readonly List<string> printers = PrintJobs.Printers();
        PrintPreset preset = PrintPreset.BuiltIns[0].Copy();
        bool loading;

        public PrintOptionsPanel(bool showFill = true)
        {
            ColumnCount = 2;
            AutoSize = true;
            Margin = new Padding(0);
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var def = PrintJobs.DefaultPrinter;
            printer.Items.Add("Default (" + (def ?? "none set") + ")");
            foreach (var p in printers) printer.Items.Add(p);
            foreach (Paper p in Enum.GetValues(typeof(Paper))) paper.Items.Add(PrintPreset.Title(p));
            Row("Printer", printer);
            Row("Paper", paper);
            Row("Copies", copies);
            Row("Colour", Group(colour, mono));
            Row("Sides", twoSided);
            Row("Pictures", Group(fit, fill, actual));
            if (showFill) Row("", fillSheet);
            foreach (var c in new Control[] { printer, paper }) ((ComboBox)c).SelectedIndexChanged += (s, e) => Edit();
            copies.ValueChanged += (s, e) => Edit();
            foreach (var r in new[] { colour, mono, fit, fill, actual }) r.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) Edit(); };
            twoSided.CheckedChanged += (s, e) => Edit();
            fillSheet.CheckedChanged += (s, e) => Edit();
        }

        void Row(string label, Control c)
        {
            var l = Ui.Text(label, null, Ui.Muted);
            l.Anchor = AnchorStyles.Right;
            l.Margin = new Padding(0, 6, 6, 0);
            c.Margin = new Padding(0, 3, 0, 3);
            Controls.Add(l);
            Controls.Add(c);
        }

        static FlowLayoutPanel Group(params Control[] items)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            foreach (var i in items) { i.Margin = new Padding(0, 2, 12, 0); f.Controls.Add(i); }
            return f;
        }

        public PrintPreset Preset
        {
            get => preset.Copy();
            set
            {
                preset = value.Copy();
                loading = true;
                int pi = preset.Printer == null ? 0 : printers.IndexOf(preset.Printer) + 1;
                printer.SelectedIndex = Math.Max(0, pi);
                paper.SelectedIndex = (int)preset.Paper;
                copies.Value = Num.Clamp(preset.Copies, 1, 99);
                colour.Checked = preset.Color;
                mono.Checked = !preset.Color;
                twoSided.Checked = preset.TwoSided;
                fit.Checked = preset.Fit == PrintFit.Fit;
                fill.Checked = preset.Fit == PrintFit.Fill;
                actual.Checked = preset.Fit == PrintFit.Actual;
                fillSheet.Checked = preset.FillSheet;
                foreach (var r in new[] { fit, fill, actual }) r.Enabled = !preset.FillSheet;
                loading = false;
            }
        }

        void Edit()
        {
            if (loading) return;
            preset.Printer = printer.SelectedIndex <= 0 ? null : printers[printer.SelectedIndex - 1];
            preset.Paper = (Paper)Math.Max(0, paper.SelectedIndex);
            preset.Copies = (int)copies.Value;
            preset.Color = colour.Checked;
            preset.TwoSided = twoSided.Checked;
            preset.Fit = fill.Checked ? PrintFit.Fill : actual.Checked ? PrintFit.Actual : PrintFit.Fit;
            preset.FillSheet = fillSheet.Checked;
            foreach (var r in new[] { fit, fill, actual }) r.Enabled = !preset.FillSheet;
            Changed?.Invoke();
        }
    }

    /// <summary>Print, in two steps. First a short list of the presets: click one (or Enter) and it prints. "More
    /// options…" opens the full view (printer, paper, copies, colour, sides, fit, preview) only when asked for.</summary>
    sealed class PrintForm : Form
    {
        /// <summary>(settings to print with, show Windows' print dialog first)</summary>
        public event Action<PrintPreset, bool> Chosen;
        readonly IList<string> files;
        readonly Core.Settings settings;
        List<PrintPreset> presets;
        readonly List<PrintPreset> rows = new List<PrintPreset>();
        readonly FlowLayoutPanel quickPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18, 14, 18, 14), WrapContents = false };
        readonly FlowLayoutPanel fullPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18, 14, 18, 14), WrapContents = false, Visible = false };
        readonly ListBox list = new ListBox { DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 54, BorderStyle = BorderStyle.None, IntegralHeight = false, Width = 404 };
        readonly Label printsOn = Ui.Text("", Ui.Small, Ui.Muted);
        readonly FlowLayoutPanel chips = new FlowLayoutPanel { Width = 600, AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 10, 0, 6) };
        readonly PictureBox preview = new PictureBox { Width = 200, Height = 260, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 4, 18, 0) };
        readonly Label status = Ui.Text("Getting it ready…", Ui.Small, Ui.Muted);
        readonly Label notes = Ui.Text("", Ui.Small, Color.DarkOrange, 600);
        readonly PrintOptionsPanel options;
        readonly Timer debounce = new Timer { Interval = 200 };
        readonly Button quickPrint = Ui.Primary("Print");
        readonly Button quickCancel = Ui.Plain("Cancel");
        readonly Button more = Ui.Plain("More options…");
        readonly Button choose = Ui.Plain("Choose…");
        readonly Button print = Ui.Primary("Print");
        readonly Button save = Ui.Plain("Save as preset…");
        /// <summary>Picking which presets the list (and the ring's Print page) shows: rows are tick boxes.</summary>
        bool choosing;
        int build;

        public static Size QuickSize(int rows) => new Size(440, 150 + Math.Min(Math.Max(rows, 1), 8) * 54);

        public PrintForm(IList<string> files, Core.Settings settings)
        {
            this.files = files;
            this.settings = settings;
            presets = settings.PrintPresets;
            var last = settings.PrintLast;
            FillRows();
            var size = QuickSize(rows.Count);
            Ui.Dialog(this, "Print", size.Width, size.Height);
            KeyPreview = true;

            // The list
            string title = files.Count == 1 ? Path.GetFileName(files[0]) : files.Count + " files";
            quickPanel.Controls.Add(Ui.Text(title, Ui.Bold));
            quickPanel.Controls.Add(printsOn);
            list.Margin = new Padding(0, 10, 0, 6);
            list.DrawItem += DrawRow;
            list.MouseMove += (s, e) => { int i = list.IndexFromPoint(e.Location); if (i >= 0 && i != list.SelectedIndex) list.SelectedIndex = i; };
            list.MouseClick += (s, e) =>
            {
                int i = list.IndexFromPoint(e.Location);
                if (i < 0) return;
                if (choosing) { settings.ShowPreset(rows[i], settings.Hidden.Contains(rows[i].StepId)); list.Invalidate(); }
                else Print(rows[i], false);
            };
            list.SelectedIndexChanged += (s, e) =>
            {
                var r = list.SelectedIndex >= 0 ? rows[list.SelectedIndex] : null;
                printsOn.Text = choosing ? "Tick the presets to show here and on the ring's Print page"
                                         : "Prints on " + (r?.Printer ?? PrintJobs.DefaultPrinter ?? "no printer");
                list.Invalidate();
            };
            quickPanel.Controls.Add(list);
            if (PrintJobs.Printers().Count == 0)
                quickPanel.Controls.Add(Ui.Text("No printer is set up yet: Settings → Bluetooth & devices → Printers & scanners.", Ui.Small, Color.DarkOrange, 400));
            // More options… and Choose… on the left, Cancel and Print on the right.
            var quickButtons = new TableLayoutPanel { Width = 404, AutoSize = true, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            quickButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            quickButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            quickButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            quickButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            quickButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            quickButtons.Controls.Add(more, 0, 0);
            quickButtons.Controls.Add(choose, 1, 0);
            quickButtons.Controls.Add(quickCancel, 3, 0);
            quickButtons.Controls.Add(quickPrint, 4, 0);
            quickPanel.Controls.Add(quickButtons);
            quickPrint.Click += (s, e) => { if (list.SelectedIndex >= 0 && list.SelectedIndex < rows.Count) Print(rows[list.SelectedIndex], false); };
            quickCancel.Click += (s, e) => Close();
            more.Click += (s, e) => OpenFull();
            choose.Click += (s, e) => SetChoosing(!choosing);
            new ToolTip().SetToolTip(choose, "Pick which presets show in this list and on the ring's Print page");

            // All the options
            options = new PrintOptionsPanel(files.Any(f => Inspector.Kind(f) == FileKind.Image));
            fullPanel.Controls.Add(Ui.Text(title, Ui.Bold));
            fullPanel.Controls.Add(status);
            fullPanel.Controls.Add(chips);
            var middle = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            preview.BackColor = Color.White;
            preview.BorderStyle = BorderStyle.FixedSingle;
            middle.Controls.Add(preview);
            var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            right.Controls.Add(options);
            middle.Controls.Add(right);
            fullPanel.Controls.Add(middle);
            fullPanel.Controls.Add(notes);
            var buttons = new FlowLayoutPanel { Width = 600, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
            var cancel = Ui.Plain("Cancel");
            var back = Ui.Plain("‹ Presets");
            var dialog = Ui.Plain("System dialog…");
            buttons.Controls.Add(print);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            buttons.Controls.Add(dialog);
            buttons.Controls.Add(back);
            fullPanel.Controls.Add(buttons);
            print.Click += (s, e) => Print(options.Preset, false);
            dialog.Click += (s, e) => Print(options.Preset, true);
            cancel.Click += (s, e) => Close();
            back.Click += (s, e) => BackToList();
            save.Click += (s, e) => SavePreset();
            options.Changed += () => { DrawChips(); debounce.Stop(); debounce.Start(); };
            debounce.Tick += (s, e) => { debounce.Stop(); Rebuild(); };

            Controls.Add(fullPanel);
            Controls.Add(quickPanel);
            AcceptButton = quickPrint;
            CancelButton = quickCancel;
            int start = last == null ? 0 : Math.Max(0, rows.FindIndex(r => r.SameSettings(last)));
            list.SelectedIndex = Math.Min(start, rows.Count - 1);
            KeyDown += (s, e) =>
            {
                // Up and down move through the list even before it has the focus.
                if (!quickPanel.Visible || list.Focused) return;
                if (e.KeyCode == System.Windows.Forms.Keys.Down) { list.SelectedIndex = Math.Min(rows.Count - 1, list.SelectedIndex + 1); e.Handled = true; }
                if (e.KeyCode == System.Windows.Forms.Keys.Up) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
            };
            Shown += (s, e) => list.Focus();
            FormClosed += (s, e) => { preview.Image?.Dispose(); debounce.Dispose(); };
        }

        /// <summary>The list's rows: "Last settings" (when they aren't a preset) and the shown presets; while choosing,
        /// every preset.</summary>
        void FillRows()
        {
            rows.Clear();
            if (choosing) rows.AddRange(presets);
            else
            {
                var last = settings.PrintLast;
                if (last != null && !presets.Any(p => p.SameSettings(last))) { var l = last.Copy(); l.Name = "Last settings"; rows.Add(l); }
                var off = settings.Hidden;
                rows.AddRange(presets.Where(p => !off.Contains(p.StepId)));
            }
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var r in rows) list.Items.Add(r.Name);
            list.EndUpdate();
            list.Height = Math.Max(1, Math.Min(rows.Count, 8)) * list.ItemHeight;
        }

        void SetChoosing(bool on)
        {
            choosing = on;
            choose.Text = on ? "Done" : "Choose…";
            more.Visible = quickCancel.Visible = quickPrint.Visible = !on;
            AcceptButton = on ? choose : quickPrint;
            FillRows();
            list.SelectedIndex = rows.Count > 0 ? 0 : -1;
            if (rows.Count == 0) printsOn.Text = "Every preset is switched off. Choose… brings them back.";
            ClientSize = QuickSize(rows.Count);
            list.Focus();
        }

        /// <summary>One preset: name, a short line of what's different about it, "Last used", and the hot fill when picked.
        /// While choosing: a tick box instead, and no fill.</summary>
        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= rows.Count) return;
            var p = rows[e.Index];
            bool on = !choosing && (e.State & DrawItemState.Selected) != 0;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var bg = new SolidBrush(list.BackColor)) g.FillRectangle(bg, e.Bounds);
            var r = new Rectangle(e.Bounds.X, e.Bounds.Y + 2, e.Bounds.Width - 1, e.Bounds.Height - 4);
            using (var path = Rounded(r, 9))
            {
                if (on)
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(r, RingPainter.HotA, RingPainter.HotB, 0f)) g.FillPath(b, path);
                else
                    using (var b = new SolidBrush(Color.FromArgb(244, 244, 247))) g.FillPath(b, path);
            }
            var ink = on ? Color.White : SystemColors.ControlText;
            var muted = on ? Color.FromArgb(225, 255, 255, 255) : Ui.Muted;
            bool shown = !settings.Hidden.Contains(p.StepId);
            // Segoe tick box: filled with a tick, or empty.
            var glyph = choosing ? (shown ? "\ue73a" : "\ue739")
                      : p.FillSheet ? Catalog.Glyph.Person : !p.Color ? Catalog.Glyph.Photo : Catalog.Glyph.Print;
            using (var f = RingPainter.Icon(15)) using (var b = new SolidBrush(on ? Color.White : RingPainter.HotB))
                g.DrawString(glyph, f, b, new RectangleF(r.X + 10, r.Y, 28, r.Height), new StringFormat { LineAlignment = StringAlignment.Center });
            var last = settings.PrintLast;
            string name = p.Name + (!choosing && last != null && last.SameSettings(p) ? "   · Last used" : "");
            using (var f = Ui.Bold) using (var b = new SolidBrush(ink))
                g.DrawString(name, f, b, new RectangleF(r.X + 44, r.Y + 7, r.Width - 80, 20), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            using (var f = Ui.Small) using (var b = new SolidBrush(muted))
                g.DrawString(Short(p), f, b, new RectangleF(r.X + 44, r.Y + 27, r.Width - 80, 18), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            if (on && !choosing)
                using (var f = Ui.Small) using (var b = new SolidBrush(muted))
                    g.DrawString("Enter", f, b, new RectangleF(r.Right - 52, r.Y, 46, r.Height), new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far });
        }

        static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>"A4 · 2 copies · Black & white · Two-sided": what makes a preset different, without the printer unless set.</summary>
        static string Short(PrintPreset p)
        {
            var parts = new List<string>();
            if (p.Printer != null) parts.Add(p.Printer);
            parts.Add(PrintPreset.Title(p.Paper));
            if (p.Copies > 1) parts.Add(p.Copies + " copies");
            parts.Add(p.Color ? "Colour" : "Black & white");
            if (p.TwoSided) parts.Add("Two-sided");
            parts.Add(p.FillSheet ? "Fills the sheet with copies" : PrintPreset.Title(p.Fit));
            return string.Join(" · ", parts);
        }

        void OpenFull()
        {
            options.Preset = list.SelectedIndex >= 0 ? rows[list.SelectedIndex] : (presets.FirstOrDefault() ?? PrintPreset.BuiltIns[0]);
            DrawChips();
            quickPanel.Visible = false;
            fullPanel.Visible = true;
            AcceptButton = print;
            ClientSize = new Size(640, 520);
            Rebuild();
        }

        void BackToList()
        {
            FillRows();
            fullPanel.Visible = false;
            quickPanel.Visible = true;
            AcceptButton = quickPrint;
            int i = rows.FindIndex(r => r.SameSettings(options.Preset));
            if (i >= 0) list.SelectedIndex = i;
            ClientSize = QuickSize(rows.Count);
            list.Focus();
        }

        PrintPreset Matching => presets.FirstOrDefault(p => p.SameSettings(options.Preset));

        /// <summary>Every saved preset as a button; they wrap onto more rows instead of running off the edge.</summary>
        void DrawChips()
        {
            chips.SuspendLayout();
            chips.Controls.Clear();
            var current = options.Preset;
            var off = settings.Hidden;
            foreach (var p in presets.Where(p => !off.Contains(p.StepId)))
            {
                bool on = p.SameSettings(current);
                var b = new Button
                {
                    Text = p.Name, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 8, 6),
                    BackColor = on ? RingPainter.HotB : Color.FromArgb(236, 236, 240), ForeColor = on ? Color.White : SystemColors.ControlText,
                    Font = Ui.Bold, Cursor = Cursors.Hand, UseMnemonic = false, // or "Black & white" loses its "&"
                };
                b.FlatAppearance.BorderSize = 0;
                var preset = p;
                b.Click += (s, e) => { options.Preset = preset; DrawChips(); Rebuild(); };
                new ToolTip().SetToolTip(b, p.Summary(PrintJobs.DefaultPrinter));
                chips.Controls.Add(b);
            }
            chips.ResumeLayout();
            save.Visible = Matching == null;
        }

        /// <summary>Lays the files out again for the preview, with the same drawing that prints.</summary>
        async void Rebuild()
        {
            int id = ++build;
            var preset = options.Preset;
            var paper = PrintJobs.PaperFor(preset);
            try
            {
                using (var job = await PrintJobs.Prepare(files, preset, paper))
                {
                    if (id != build || IsDisposed) return;
                    var old = preview.Image;
                    preview.Image = job.Sheets.Count > 0 ? PrintJobs.Preview(job.Sheets[0], job.Gray, 400) : null;
                    old?.Dispose();
                    int n = job.Sheets.Count;
                    status.Text = n == 0 ? "Nothing here can be printed" :
                        n + (n == 1 ? " page · " : " pages · ") + PrintPreset.Title(preset.Paper) + " · " + (preset.Printer ?? PrintJobs.DefaultPrinter ?? "no printer");
                    notes.Text = string.Join("\n", job.Notes);
                    print.Enabled = n > 0;
                }
            }
            catch (Exception e)
            {
                if (id != build || IsDisposed) return;
                notes.Text = e.Message;
                print.Enabled = false;
            }
        }

        void SavePreset()
        {
            var name = Microsoft.VisualBasic.Interaction.InputBox("Name this preset:", "Save as preset", "My preset").Trim();
            if (name.Length == 0) return;
            var p = options.Preset;
            p.Id = Guid.NewGuid().ToString("N");
            p.Name = name;
            presets.Add(p);
            settings.PrintPresets = presets;
            DrawChips();
        }

        /// <summary>Prints now with these settings; a saved preset prints as itself, anything else as "Print again".</summary>
        void Print(PrintPreset chosen, bool dialog)
        {
            var saved = presets.FirstOrDefault(p => p.SameSettings(chosen));
            var last = chosen.Copy();
            last.Id = PrintPreset.LastId;
            last.Name = saved?.Name ?? "Print again";
            settings.PrintLast = last;
            Close();
            Chosen?.Invoke(dialog ? chosen : (saved ?? last), dialog);
        }
    }

    /// <summary>Settings → Printing: the saved presets, edit them, ★ one into the ring's Favorites, or print straight away.</summary>
    static class PrintingTab
    {
        public static TabPage Build(Core.Settings s)
        {
            var tab = new TabPage("Printing");
            var page = new Pane(tab).Column;
            page.Controls.Add(Rows.Note("Your print presets: the list in the Print window and the ring's Print page (drop a file on one and it prints). ★ one to put it in Favorites too.", 8));
            var presets = s.PrintPresets;
            var list = new ListBox { Width = 150, Height = 250, IntegralHeight = false, Margin = new Padding(0), BorderStyle = BorderStyle.FixedSingle };
            var name = new TextBox { Width = 250 };
            var options = new PrintOptionsPanel();
            var star = new CheckBox { Text = "★ In the ring's Favorites", AutoSize = true, Margin = new Padding(74, 6, 0, 0) };
            var shown = new CheckBox { Text = "In the Print list and on the ring", AutoSize = true, Margin = new Padding(74, 6, 0, 0) };
            bool loading = false;

            void Save() => s.PrintPresets = presets;
            void Fill()
            {
                int keep = Math.Max(0, list.SelectedIndex);
                list.Items.Clear();
                foreach (var p in presets) list.Items.Add(p.Name);
                if (list.Items.Count > 0) list.SelectedIndex = Math.Min(keep, list.Items.Count - 1);
            }
            PrintPreset Selected() => list.SelectedIndex >= 0 && list.SelectedIndex < presets.Count ? presets[list.SelectedIndex] : null;

            list.SelectedIndexChanged += (o, e) =>
            {
                var p = Selected();
                if (p == null) return;
                loading = true;
                name.Text = p.Name;
                options.Preset = p;
                star.Checked = s.Favorites.Contains(p.StepId);
                shown.Checked = !s.Hidden.Contains(p.StepId);
                loading = false;
            };
            name.TextChanged += (o, e) =>
            {
                var p = Selected();
                if (loading || p == null || name.Text.Trim().Length == 0) return;
                p.Name = name.Text.Trim();
                Save();
                list.Items[list.SelectedIndex] = p.Name;
            };
            options.Changed += () =>
            {
                var p = Selected();
                if (loading || p == null) return;
                var n = options.Preset;
                n.Id = p.Id;
                n.Name = p.Name;
                presets[list.SelectedIndex] = n;
                Save();
            };
            star.CheckedChanged += (o, e) =>
            {
                var p = Selected();
                if (loading || p == null) return;
                var f = s.Favorites.Where(x => x != p.StepId).ToList();
                if (star.Checked) f.Add(p.StepId);
                s.Set(Core.Settings.Keys.Favorites, f);
            };
            shown.CheckedChanged += (o, e) =>
            {
                var p = Selected();
                if (!loading && p != null) s.ShowPreset(p, shown.Checked);
            };

            // The presets on the left, the one picked on the right, all in one card.
            var top = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Look.Card, Margin = new Padding(0) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var left = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 16, 0), BackColor = Look.Card };
            left.Controls.Add(list);
            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0), BackColor = Look.Card };
            var add = Ui.Plain("+");
            var remove = Ui.Plain("−");
            var reset = Ui.Plain("Reset");
            // Small buttons that sit on one line under the list.
            foreach (var b in new[] { add, remove, reset })
            {
                b.AutoSize = false;
                b.Size = new Size(b == reset ? 64 : 36, 28);
                b.Margin = new Padding(0, 0, 6, 0);
            }
            add.Click += (o, e) =>
            {
                var n = (Selected() ?? PrintPreset.BuiltIns[0]).Copy();
                n.Id = Guid.NewGuid().ToString("N");
                n.Name = "My preset";
                presets.Add(n);
                Save();
                Fill();
                list.SelectedIndex = presets.Count - 1;
            };
            remove.Click += (o, e) =>
            {
                var p = Selected();
                if (p == null) return;
                s.Set(Core.Settings.Keys.Favorites, s.Favorites.Where(x => x != p.StepId));
                presets.Remove(p);
                Save();
                Fill();
            };
            reset.Click += (o, e) => { presets = PrintPreset.BuiltIns.Select(p => p.Copy()).ToList(); Save(); Fill(); };
            buttons.Controls.Add(add);
            buttons.Controls.Add(remove);
            buttons.Controls.Add(reset);
            left.Controls.Add(buttons);
            top.Controls.Add(left, 0, 0);

            var editor = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0), BackColor = Look.Card };
            var nameRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 4), BackColor = Look.Card };
            var nameLabel = Ui.Text("Name", null, Ui.Muted);
            nameLabel.Margin = new Padding(28, 6, 6, 0);
            nameRow.Controls.Add(nameLabel);
            nameRow.Controls.Add(name);
            editor.Controls.Add(nameRow);
            options.BackColor = Look.Card;
            editor.Controls.Add(options);
            editor.Controls.Add(shown);
            editor.Controls.Add(star);
            top.Controls.Add(editor, 1, 0);
            page.Controls.Add(new Card().Add(top));

            // Its own card, as on the Mac: the "print straight away" switch, with the double-tap Shift tip under it.
            var quickRow = Rows.Toggle("Print straight away when I pick Print (uses the first preset shown, no window)",
                                       "Double-tap Shift on a file prints it again with whatever you used last.",
                                       s.GetBool(Core.Settings.Keys.PrintQuick, false), v => s.Set(Core.Settings.Keys.PrintQuick, v), out var quick);
            page.Controls.Add(Rows.Section("When I pick Print"));
            page.Controls.Add(new Card().Add(quickRow));
            Fill();
            return tab;
        }
    }
}
