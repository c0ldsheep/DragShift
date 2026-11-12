using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DragShift.Core;
using Keys = DragShift.Core.Settings.Keys;

namespace DragShift
{
    /// <summary>Settings: six pages, each one column of white cards in the middle of a light grey page, grouped and worded the way
    /// the Mac app groups them. The look itself (cards, rows, the row of page names) is in SettingsUi.cs.</summary>
    sealed class SettingsForm : Form
    {
        public event Action<bool> HotkeyChanged;
        public event Action<string> ShelfChanged;
        readonly Core.Settings S;
        readonly Capabilities caps;

        public SettingsForm(Core.Settings settings, Capabilities caps)
        {
            S = settings;
            this.caps = caps;
            // Wide enough for the column of cards with room on both sides, tall enough for About (the longest page) with no scrolling.
            Ui.Dialog(this, "DragShift Settings", 660, 650);
            TopMost = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Look.Page;
            var tabs = new FlatTabs { Dock = DockStyle.Fill, TabIndex = 1 };
            tabs.TabPages.Add(General());
            tabs.TabPages.Add(Triggers());
            tabs.TabPages.Add(Favorites());
            tabs.TabPages.Add(Saving());
            tabs.TabPages.Add(PrintingTab.Build(S));
            tabs.TabPages.Add(About());
            Controls.Add(tabs);                                       // first, so it fills what the row of names leaves
            Controls.Add(new TabStrip(tabs) { TabIndex = 0 });
        }

        /// <summary>A tick box in a card (its words on the left, the box on the right), with an optional line of help under it.</summary>
        static CheckBox Tick(Card card, string text, string help, bool value, Action<bool> set)
        {
            card.Add(Rows.Toggle(text, help, value, set, out var box));
            return box;
        }

        static ComboBox Pick(int width, params object[] items)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            box.Items.AddRange(items);
            return box;
        }

        TabPage General()
        {
            var tab = new TabPage("General");
            var page = new Pane(tab).Column;

            var start = new Card();
            Tick(start, "Start DragShift with Windows", null, Integration.StartsWithWindows, v => Integration.StartsWithWindows = v);
            Tick(start, "Add DragShift to the right-click \"Send to\" menu", "Explorer → right-click files → Send to → DragShift opens the ring for them.",
                 Integration.InSendTo, v => Integration.InSendTo = v);
            page.Controls.Add(start);

            page.Controls.Add(Rows.Section("Feel"));
            var feel = new Card();
            Tick(feel, "Play a sound when a file is ready", null, S.GetBool(Keys.Sound, false), v => S.Set(Keys.Sound, v));
            Tick(feel, "Make what I pick most glow when the ring opens", "After three picks in a row, that slice gets a soft pulsing edge.",
                 S.GetBool(Keys.UsualGlow, true), v => S.Set(Keys.UsualGlow, v));
            page.Controls.Add(feel);

            page.Controls.Add(Rows.Section("After it's done"));
            var after = new Card();
            Tick(after, "Put text it reads on the clipboard", "Picture → Text and PDF → Text.", S.GetBool(Keys.CopyText, true), v => S.Set(Keys.CopyText, v));
            Tick(after, "Show the new file in Explorer", null, S.GetBool(Keys.Reveal, false), v => S.Set(Keys.Reveal, v));
            page.Controls.Add(after);
            return tab;
        }

        TabPage Triggers()
        {
            var tab = new TabPage("Triggers");
            var page = new Pane(tab).Column;

            var ways = new Card();
            Tick(ways, "Press Shift while dragging", "Or hold Shift first, then grab the file. Press Shift again for the next page.", S.Shift, v => S.Set(Keys.Shift, v));

            var mouseRow = Rows.Toggle("Click another mouse button while dragging",
                                       "For mice: hold the file with the left button, then click one of these. If an app you drag from already uses a button, untick it here.",
                                       S.Mouse, v => S.Set(Keys.Mouse, v), out var mouse);
            // The four buttons, two by two under the switch; they stay ticked as they were while the switch is off, just greyed out.
            var buttons = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Look.Card, Margin = new Padding(20, 6, 0, 0) };
            var boxes = new List<CheckBox>();
            foreach (var (button, label) in new[] { (MouseTrigger.Right, "Right button"), (MouseTrigger.Middle, "Wheel click (middle button)"),
                                                    (MouseTrigger.Back, "Side button: back"), (MouseTrigger.Forward, "Side button: forward") })
            {
                var box = new CheckBox { Text = label, Checked = (S.TriggerButtons & button) != 0, AutoSize = true, Enabled = S.Mouse, Margin = new Padding(0, 2, 28, 2) };
                box.CheckedChanged += (s, e) => S.TriggerButtons = box.Checked ? S.TriggerButtons | button : S.TriggerButtons & ~button;
                buttons.Controls.Add(box, boxes.Count % 2, boxes.Count / 2);
                boxes.Add(box);
            }
            mouse.CheckedChanged += (s, e) => { foreach (var box in boxes) box.Enabled = mouse.Checked; };
            mouseRow.Controls.Add(buttons, 0, 2);
            ways.Add(mouseRow);

            Tick(ways, "Tap one more finger on the touchpad", "Drag with one finger, touch a second one down. Works on Precision Touchpads (most laptops since 2016).",
                 S.Finger, v => S.Set(Keys.Finger, v));
            page.Controls.Add(ways);

            page.Controls.Add(Rows.Section("Shortcuts"));
            var shortcuts = new Card();
            Tick(shortcuts, "Ctrl+Alt+V opens the clipboard in the ring", "Copy files in Explorer, or take a screenshot with Win+Shift+S, then press it.",
                 S.GetBool(Keys.Hotkey, true), v => { S.Set(Keys.Hotkey, v); HotkeyChanged?.Invoke(v); });
            var side = Pick(120, "Right edge", "Left edge", "Off");
            var current = S.Get(Keys.Shelf, "right");
            side.SelectedIndex = current == "left" ? 1 : current == "off" ? 2 : 0;
            side.SelectedIndexChanged += (s, e) =>
            {
                var v = side.SelectedIndex == 1 ? "left" : side.SelectedIndex == 2 ? "off" : "right";
                S.Set(Keys.Shelf, v);
                ShelfChanged?.Invoke(v);
            };
            shortcuts.Add(Rows.Choice("Favourites shelf (push a dragged file against the edge)", side));
            page.Controls.Add(shortcuts);

            page.Controls.Add(Rows.Note("Once the ring is open: push the file toward a slice and let go. Or let go in the middle and the ring stays open: glide to a slice and click it. Double-tap Shift to repeat the last thing you did. Esc closes it."));
            return tab;
        }

        TabPage Favorites()
        {
            var tab = new TabPage("Favorites");
            var page = new Pane(tab).Column;
            page.Controls.Add(Rows.Note("Ticked ones show first in the ring (★ Faves) and on the edge shelf. Only the ones that fit the dragged file appear.", 8));

            var all = new List<(string Id, string Title)>();
            foreach (var s in Catalog.Everything(Capabilities.All)) all.Add((s.Id, s.Title));
            foreach (var r in Recipe.BuiltIns) all.Add((r.Id, r.Name + " (recipe)"));
            foreach (var kb in new[] { 20, 50, 100, 200 }) all.Add((SizeLimit.StepId(kb), "Under " + SizeLimit.Title(kb)));
            var faves = S.Favorites;
            var ordered = faves.Select(id => all.FirstOrDefault(a => a.Id == id)).Where(a => a.Id != null)
                               .Concat(all.Where(a => !faves.Contains(a.Id))).GroupBy(a => a.Id).Select(g => g.First()).ToList();
            var list = new CheckedListBox
            {
                CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill,
                Margin = new Padding(0), MinimumSize = new Size(0, 330), MaximumSize = new Size(0, 330),
            };
            foreach (var a in ordered) list.Items.Add(new Item(a.Id, a.Title), faves.Contains(a.Id));
            void Save()
            {
                var ids = new List<string>();
                for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) ids.Add(((Item)list.Items[i]).Id);
                S.Set(Keys.Favorites, ids);
            }
            list.ItemCheck += (s, e) => BeginInvoke((Action)Save);

            var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Look.Card, Margin = new Padding(14, 0, 0, 0) };
            var up = Ui.Plain("Move up");
            var down = Ui.Plain("Move down");
            var reset = Ui.Plain("Reset");
            foreach (var b in new[] { up, down, reset }) { b.AutoSize = false; b.Size = new Size(104, 28); b.Margin = new Padding(0, 0, 0, 6); } // the same width
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

            var body = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Look.Card };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.Controls.Add(list, 0, 0);
            body.Controls.Add(side, 1, 0);
            page.Controls.Add(new Card().Add(body));

            page.Controls.Add(Rows.Note("Tip: right-click any slice while the ring is open to add or remove it."));
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
            var page = new Pane(tab).Column;

            page.Controls.Add(Rows.Section("Save new files", true));
            var save = new Card();
            var where = Pick(200, "Next to the original", "Downloads", "A folder I choose…");
            var mode = S.Get(Keys.OutputMode, "beside");
            where.SelectedIndex = mode == "downloads" ? 1 : mode == "folder" ? 2 : 0;
            var folderLabel = Ui.Text(S.Get(Keys.OutputFolder), Ui.Small, Ui.Muted, Look.Inside);
            folderLabel.Visible = where.SelectedIndex == 2; // the remembered folder is shown only while it is the one in use
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
                folderLabel.Visible = where.SelectedIndex == 2;
            };
            save.Add(Rows.Choice("Where", where));
            save.Add(folderLabel);
            save.Add(Ui.Text("Originals are never changed. If a name is taken, DragShift adds 2, 3… like Explorer does.", Ui.Small, Ui.Muted, Look.Inside));
            page.Controls.Add(save);

            page.Controls.Add(Rows.Section("Quality"));
            var slider = new TrackBar { Minimum = 50, Maximum = 100, Value = (int)(S.Quality * 100), TickStyle = TickStyle.None, AutoSize = false, Width = 220, Height = 26, Margin = new Padding(0) };
            var number = Ui.Text(slider.Value.ToString());
            number.MinimumSize = new Size(26, 0);
            number.Anchor = AnchorStyles.Left;
            number.Margin = new Padding(6, 0, 0, 0);
            slider.ValueChanged += (s, e) => { S.Set(Keys.Quality, slider.Value / 100.0); number.Text = slider.Value.ToString(); };
            var sliderBox = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Look.Card };
            sliderBox.Controls.Add(slider, 0, 0);
            sliderBox.Controls.Add(number, 1, 0);
            page.Controls.Add(new Card().Add(Rows.Choice("JPG quality", sliderBox)));

            page.Controls.Add(Rows.Section("Passport photo"));
            var spec = S.Photo;
            var size = Pick(250);
            foreach (var p in PhotoSpec.Presets) size.Items.Add(p.Name);
            size.SelectedIndex = Math.Max(0, Array.FindIndex(PhotoSpec.Presets, p => p.Spec.WidthMM == spec.WidthMM && p.Spec.HeightMM == spec.HeightMM));
            var limits = new[] { 20, 50, 100, 200, 240, 500, 0 };
            var limit = Pick(250);
            foreach (var kb in limits) limit.Items.Add(kb == 0 ? "No limit" : SizeLimit.Title(kb));
            limit.SelectedIndex = Math.Max(0, Array.IndexOf(limits, spec.MaxKB));
            var bg = Pick(250, "White", "Light blue", "Keep the original");
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
            page.Controls.Add(new Card().Add(Rows.Choice("Size", size)).Add(Rows.Choice("Keep it under", limit)).Add(Rows.Choice("Background", bg)));
            page.Controls.Add(Rows.Note("Finds the face, centres it the way passport rules ask and saves a JPG at 300 dpi, so it prints at exactly the right size. A plain wall behind the person is changed to the colour you pick; a busy background is kept."));

            page.Controls.Add(Rows.Section("Watermark"));
            var mark = new TextBox { Width = 250, Text = S.Get(Keys.Watermark, "© " + Environment.UserName) };
            mark.TextChanged += (s, e) => S.Set(Keys.Watermark, mark.Text);
            page.Controls.Add(new Card().Add(Rows.Choice("Watermark text", mark)));
            return tab;
        }

        /// <summary>One line of "what this PC can do": a green tick and "on", or a grey dash and (when there is something to do about
        /// it) how to turn it on, as on the Mac.</summary>
        static void Say(StatusRow row, string name, bool on, string how = null)
        {
            row.Good = on;
            row.Text = name + (on ? " — on" : how == null ? " — off" : " — optional: " + how);
        }

        TabPage About()
        {
            var tab = new TabPage("About");
            var page = new Pane(tab).Column;
            // Everything in one column, each thing in the middle of it, like the Mac's About.
            var about = new TableLayoutPanel
            {
                ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Look.Page, Margin = new Padding(0),
                MinimumSize = new Size(Look.Column, 0), MaximumSize = new Size(Look.Column, 0),
            };
            about.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            void Add(Control c, int above = 0, int below = 0)
            {
                c.Anchor = AnchorStyles.None;
                c.Margin = new Padding(0, above, 0, below);
                about.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                about.RowCount = about.RowStyles.Count;
                about.Controls.Add(c, 0, about.RowStyles.Count - 1);
            }

            Add(new IconView(56), 2);
            Add(Ui.Text("DragShift", new Font("Segoe UI Semibold", 17f)), 6);
            Add(Ui.Text("Version " + AppMain.Version, null, Ui.Muted));
            var what = Ui.Text("Change any file while you drag it. Everything happens on this PC: no internet, no account, no tracking. Text reading and face finding use Windows' own on-device features.",
                               null, null, 430);
            what.TextAlign = ContentAlignment.TopCenter;
            Add(what, 8);

            var ocr = new StatusRow { Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 2, 0, 2) };
            var faces = new StatusRow { Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 2, 0, 2) };
            var heic = new StatusRow { Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 2, 0, 2) };
            var status = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Look.Page };
            status.Controls.AddRange(new Control[] { ocr, faces, heic });
            Add(status, 10);

            var open = Ui.Plain("Open Log");
            open.Click += (s, e) => { try { Process.Start(Paths.LogFile); } catch (Exception) { Shell.Reveal(new[] { Paths.LogFile }); } };
            Add(open, 8);
            Add(new Panel { Width = Look.Column - 150, Height = 1, BackColor = Look.Edge }, 12);

            // Buy me a coffee: only here, never a popup. Drag the cup and press Shift for sizes, or use the button.
            var line = new RichLine();
            Add(line, 10);
            void Fill()
            {
                Say(ocr, "Text reading", caps.Ocr, "add a language with OCR in Windows Settings");
                Say(faces, "Face finding", caps.Faces);
                Say(heic, "HEIC saving", caps.HeicWrite, "free \"HEIF Image Extensions\" in the Store");
                line.Text = Tip.Line(S.Fixed);
                line.Bold = Tip.LineParts(S.Fixed).Count;
            }
            void RefreshAbout()
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired)
                {
                    // The codec check finishes in the background; the window may close before it does.
                    try { BeginInvoke((Action)RefreshAbout); } catch (InvalidOperationException) { }
                    return;
                }
                Fill();
            }
            Fill(); // the words are there from the start; they are read again once the window exists and whenever something changes
            HandleCreated += (s, e) => RefreshAbout();
            S.Changed += RefreshAbout;
            caps.HeicWriteChanged += RefreshAbout;
            Disposed += (s, e) => { S.Changed -= RefreshAbout; caps.HeicWriteChanged -= RefreshAbout; };

            // The button is for anyone who would rather click than drag (and for the keyboard); the cup, with room for its ring, comes last.
            var coffee = Ui.Plain("Buy me a coffee");
            coffee.Click += (s, e) => { using (var d = new TipForm(-1)) d.ShowDialog(FindForm()); };
            Add(coffee, 6);
            var hint = Ui.Text(CoffeeCup.Closed, Ui.Small, Ui.Muted);
            Add(hint, 6);
            var cup = new CoffeeCup();
            cup.Hint += text => hint.Text = text;
            cup.Picked += size => { using (var d = new TipForm(size)) d.ShowDialog(FindForm()); };
            Add(cup, 2);

            page.Controls.Add(about);
            return tab;
        }
    }
}
