using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DragShift.Core;
using DragShift.Engine;
using Microsoft.Win32;
using Keys = DragShift.Core.Settings.Keys;

namespace DragShift
{
    /// <summary>Lives in the tray. Wires the drag watcher, touchpad, ring, shelf and bubble together, and runs jobs.</summary>
    sealed class AppMain : ApplicationContext
    {
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public readonly Core.Settings Settings = new Core.Settings(Paths.Settings);
        public readonly Capabilities Caps = new Capabilities();
        readonly DragTracker tracker = new DragTracker();
        readonly Touchpad touch = new Touchpad();
        readonly RingForm ring = new RingForm();
        readonly ShelfForm shelf = new ShelfForm();
        readonly Hud hud = new Hud();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Receiver receiver;
        CancellationTokenSource job;
        List<string> lastOutputs = new List<string>();
        DateTime lastTrigger = DateTime.MinValue;
        bool repeatOnReveal;
        int fingerBase, lastFingers;
        DateTime settleUntil;

        public AppMain(string[] args)
        {
            Log.Start(Version);
            Caps.Ocr = Vision.OcrAvailable;
            Caps.Faces = Vision.FacesAvailable;
            Log.Line("can read text: " + Caps.Ocr + ", find faces: " + Caps.Faces);
            Images.CanWriteHeic().ContinueWith(t => Caps.HeicWrite = t.Status == TaskStatus.RanToCompletion && t.Result);

            ring.BuildPages = files => Catalog.Pages(files, Caps, Settings.Hidden, Settings.Favorites, Recipe.BuiltIns, Settings.PrintPresets);
            ring.ToggleFavorite = Settings.ToggleFavorite;
            ring.LastActionFor = LastAction;
            ring.UsualFor = Settings.Usual;
            // Picks from the ring and the shelf teach "your usual"; the size and resize windows finish a pick that
            // was already counted, so they call Perform directly.
            ring.Perform += (slice, files, temp) => { Picked(slice, files); Perform(slice, files, temp); };
            ring.Flash += text => hud.ShowFlash(text, null, Cursor.Position);

            shelf.Side = Settings.Get(Keys.Shelf, "right");
            shelf.Favorites = files => Catalog.FavoriteSlices(Settings.Favorites,
                Catalog.BasePages(files, Caps).SelectMany(p => p.Slices).ToList(), files, Recipe.BuiltIns, Settings.PrintPresets);
            shelf.Perform += (slice, files) => { Picked(slice, files); Perform(slice, files, false); };

            hud.ShowClicked += () => Shell.Reveal(lastOutputs);
            hud.UndoClicked += Undo;
            hud.CancelClicked += () => job?.Cancel();

            tracker.Triggered += Trigger;
            tracker.DragStarted += () =>
            {
                fingerBase = touch.Count;
                lastFingers = fingerBase;
                settleUntil = DateTime.Now.AddMilliseconds(300);
                if (!Settings.Paused) shelf.DragStarted(Cursor.Position);
            };
            tracker.Released += () =>
            {
                ring.DragEnded();
                shelf.DragEnded();
            };
            tracker.Pressed += (p, left) =>
            {
                if (ring.IsOpen && ring.Model.Mode != RingModel.RingMode.Drag && !ring.Contains(p)) ring.HideRing();
            };
            tracker.ShiftTapped += () =>
            {
                if (ring.IsOpen && ring.Model.Mode != RingModel.RingMode.Drag) ring.NextPage();
            };
            tracker.Escape = () =>
            {
                if (!ring.IsOpen) return false;
                ring.BeginInvoke((Action)ring.HideRing);
                return true;
            };
            touch.Changed += Fingers;

            receiver = new Receiver();
            receiver.Files += files => ShowRingAtCenter(files, false);
            receiver.Hotkey += OpenClipboard;
            receiver.ApplyHotkey(Settings.GetBool(Keys.Hotkey, true));

            SetUpTray();
            SystemEvents.PowerModeChanged += (s, e) =>
            {
                if (e.Mode != PowerModes.Resume) return;
                // Windows can drop hooks and raw input across sleep; set them up again.
                tracker.Restart();
                touch.Start();
            };

            tracker.Start();
            touch.Start();

            if (!Settings.GetBool(Keys.WelcomeDone, false)) ShowWelcome();
            var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && (File.Exists(a) || Directory.Exists(a))).ToList();
            if (files.Count > 0) ShowRingAtCenter(files, false);
        }

        // MARK: Triggers

        void Trigger(DragTracker.Trigger t)
        {
            if (Settings.Paused) return;
            if (t == DragTracker.Trigger.Shift && !Settings.Shift) return;
            if (t == DragTracker.Trigger.Mouse && !Settings.Mouse) return;
            Log.Line("trigger: " + t);
            var now = DateTime.Now;
            bool quick = (now - lastTrigger).TotalSeconds < 0.45;
            lastTrigger = now;
            if (ring.IsOpen)
            {
                // A second tap right after the first = do the last thing again; later taps flip pages.
                if (quick && ring.Model.Mode == RingModel.RingMode.Drag) ring.RepeatLast();
                else ring.NextPage();
            }
            else if (ring.IsProbing)
            {
                if (quick) repeatOnReveal = true;
            }
            else
            {
                repeatOnReveal = false;
                ring.Probe(Cursor.Position);
                WaitForReveal();
            }
        }

        async void WaitForReveal()
        {
            for (int i = 0; i < 40 && ring.IsProbing; i++) await Task.Delay(25);
            if (repeatOnReveal && ring.IsOpen) ring.RepeatLast();
            repeatOnReveal = false;
        }

        /// <summary>One more finger than the drag started with = trigger. Lifting and tapping again fires again
        /// (next page). Fingers that never go above the starting count are ignored, so holding the click with
        /// a thumb while moving with another finger is safe.</summary>
        void Fingers(int old, int now)
        {
            int previous = lastFingers;
            lastFingers = now;
            if (!tracker.Dragging || !Settings.Finger || Settings.Paused) return;
            if (DateTime.Now < settleUntil)
            {
                fingerBase = Math.Max(fingerBase, now);
                return;
            }
            if (previous <= fingerBase && now > fingerBase)
            {
                Log.Line("fingers " + previous + "→" + now + ", base " + fingerBase);
                Trigger(DragTracker.Trigger.Finger);
            }
        }

        Slice LastAction(FileKind kind)
        {
            var id = Settings.LastAction(kind);
            if (id == null) return null;
            var all = Catalog.Pages(ring.Files, Caps).SelectMany(p => p.Slices);
            return all.FirstOrDefault(s => s.Id == id)
                ?? Recipe.BuiltIns.FirstOrDefault(r => r.Id == id)?.Slice
                ?? (SizeLimit.KbFromStep(id) is int kb ? SizeLimit.Slice(kb) : null)
                ?? Settings.PrintPresetFor(id)?.Slice
                ?? (id.StartsWith("img.size.", StringComparison.Ordinal) || id.StartsWith("img.pct.", StringComparison.Ordinal)
                    ? new Slice(id, Recipe.StepTitle(id), Catalog.Glyph.Resize, "Resizing") : null);
        }

        // MARK: Doing things

        void Picked(Slice slice, List<string> files)
        {
            if (files != null && files.Count > 0) Settings.RecordPick(Inspector.Kind(files), slice.Id);
        }

        public void Perform(Slice slice, List<string> files, bool temporary)
        {
            if (files == null || files.Count == 0) return;
            Log.Line("run: " + slice.Id + " on " + files.Count + " file(s)");
            Settings.SetLastAction(Inspector.Kind(files), slice.Id);
            var preset = Settings.PrintPresetFor(slice.Id);
            if (preset != null) PrintFiles(files, preset, false);
            else if (slice.Editor == Editor.Print)
            {
                // "Print straight away" skips the window and uses the top preset.
                var top = Settings.ShownPrintPresets.FirstOrDefault();
                if (Settings.GetBool(Keys.PrintQuick, false) && top != null) Perform(top.Slice, files, temporary);
                else
                {
                    var form = new PrintForm(files, Settings);
                    form.Chosen += (p, dialog) =>
                    {
                        if (dialog) PrintFiles(files, p, true);
                        else Perform(Settings.PrintPresetFor(p.StepId) != null ? p.Slice : Settings.PrintLast.Slice, files, temporary);
                    };
                    form.Show();
                }
            }
            else if (slice.IsRecipe)
            {
                var recipe = Recipe.BuiltIns.FirstOrDefault(r => r.Id == slice.Id);
                if (recipe != null) RunJob(recipe.Name, files, temporary, (o, p, ct) => Engine.Runner.RunRecipe(recipe, files, o, p, ct));
            }
            else if (slice.IsShare) Share(slice, files);
            else if (slice.Editor == Editor.FitKB)
            {
                var form = new FitForm(files, Settings.GetInt(Keys.FitLast, 100));
                form.Chosen += kb =>
                {
                    Settings.Set(Keys.FitLast, kb);
                    Perform(SizeLimit.Slice(kb), files, temporary);
                };
                form.Show();
            }
            else if (slice.Editor == Editor.Resize)
            {
                var form = new ResizeForm(files);
                form.Chosen += step => Perform(new Slice(step, "Resize", Catalog.Glyph.Resize, "Resizing"), files, temporary);
                form.Show();
            }
            else RunJob(slice.Verb, files, temporary, (o, p, ct) => Engine.Runner.Run(slice, files, o, p, ct));
        }

        /// <summary>Lays the files out with the progress bubble, then sends them to the preset's printer.</summary>
        async void PrintFiles(List<string> files, PrintPreset preset, bool dialog)
        {
            var near = Cursor.Position;
            hud.Begin("Printing", files.Count, near);
            try
            {
                var paper = Engine.PrintJobs.PaperFor(preset);
                using (var job = await Engine.PrintJobs.Prepare(files, preset, paper))
                {
                    var printer = preset.Printer ?? Engine.PrintJobs.DefaultPrinter ?? "the printer";
                    if (Engine.PrintJobs.Send(job, preset, paper, Path.GetFileName(files[0]), dialog))
                    {
                        var detail = job.Sheets.Count + (job.Sheets.Count == 1 ? " page" : " pages");
                        if (preset.Copies > 1) detail += " · " + preset.Copies + " copies";
                        if (job.Notes.Count > 0) detail += " · skipped " + job.Notes.Count;
                        hud.ShowFlash("Sent to " + printer, detail, near);
                        Log.Line("print: " + job.Sheets.Count + " page(s) to " + printer + ", preset " + preset.Id);
                    }
                    else hud.ShowFlash("Not printed", "Cancelled", near);
                }
            }
            catch (Exception e)
            {
                Log.Line("print failed: " + e);
                hud.Finish("Couldn't print", e is UserError ? e.Message : "Windows said: " + e.Message, false, false, true);
            }
        }

        Options OptionsFor(bool temporary)
        {
            var o = new Options
            {
                Quality = Settings.Quality,
                Photo = Settings.Photo,
                Watermark = Settings.Get(Keys.Watermark, "© " + Environment.UserName),
            };
            switch (Settings.Get(Keys.OutputMode, "beside"))
            {
                case "downloads": o.OutputFolder = Paths.Downloads; break;
                case "folder":
                    var f = Settings.Get(Keys.OutputFolder);
                    if (Directory.Exists(f)) o.OutputFolder = f;
                    break;
                default:
                    if (temporary) o.OutputFolder = Paths.Downloads;
                    break;
            }
            return o;
        }

        async void RunJob(string verb, List<string> files, bool temporary,
                          Func<Options, IProgress<double>, CancellationToken, Task<RunReport>> work)
        {
            job?.Cancel();
            var cts = new CancellationTokenSource();
            job = cts;
            hud.Begin(verb, files.Count, Cursor.Position);
            var options = OptionsFor(temporary);
            var progress = new Progress<double>(f =>
            {
                if (job == cts) hud.Update(f, files.Count > 1 ? (int)(f * files.Count) + " of " + files.Count + " done" : null);
            });
            RunReport report;
            try
            {
                // Heavy work stays off this thread, so the mouse and keyboard watchers always answer at once.
                report = await Task.Run(() => work(options, progress, cts.Token));
            }
            catch (OperationCanceledException)
            {
                hud.ShowFlash("Cancelled", "Files already finished were kept.", Cursor.Position);
                return;
            }
            catch (Exception e)
            {
                Log.Line("job failed: " + e);
                report = new RunReport();
                report.Failures.Add(Engine.Runner.Message(e));
            }
            if (job == cts) Finish(report, verb);
        }

        static readonly Dictionary<string, string> Past = new Dictionary<string, string>
        {
            { "Converting", "Converted" }, { "Compressing", "Compressed" }, { "Resizing", "Resized" }, { "Rotating", "Rotated" },
            { "Flipping", "Flipped" }, { "Reading", "Read" }, { "Making", "Made" }, { "Putting", "Put" }, { "Zipping", "Zipped" },
            { "Extracting", "Extracted" }, { "Saving", "Saved" }, { "Removing", "Removed" }, { "Adding", "Added" },
            { "Fitting", "Fitted" }, { "Stitching", "Stitched" }, { "Running", "Done:" },
        };

        static string Done(string verb)
        {
            var first = verb.Split(' ')[0];
            return Past.TryGetValue(first, out var p) ? p + verb.Substring(first.Length) : verb;
        }

        void Finish(RunReport report, string verb)
        {
            Log.Line("done: " + report.Outputs.Count + " output(s), " + report.Failures.Count + " failure(s)");
            if (report.Outputs.Count > 0) lastOutputs = report.Outputs.ToList();
            Settings.AddFixed(report.Outputs.Count); // for the About tab's "fixed N files for you"
            var detail = report.Summary;
            if (report.Text != null && Settings.GetBool(Keys.CopyText, true))
            {
                try { Clipboard.SetText(report.Text); detail += " · Text copied"; } catch (ExternalException) { }
            }
            var note = report.Notes.FirstOrDefault(n => !detail.Contains(n));
            if (note != null && report.Outputs.Count > 0) detail += " · " + note;
            if (report.Outputs.Count > 0 && report.Failures.Count > 0) detail += " · " + report.Failures.Count + " didn't work: " + report.Failures[0];
            bool failed = report.Outputs.Count == 0 && report.Failures.Count > 0;
            if (report.Outputs.Count == 0 && !failed)
            {
                hud.ShowFlash(report.Notes.FirstOrDefault() ?? "Nothing to do", null, Cursor.Position);
                return;
            }
            hud.Finish(failed ? "That didn't work" : Done(verb), detail, report.Outputs.Count > 0, report.Outputs.Count > 0, failed);
            if (!failed && Settings.GetBool(Keys.Sound, false)) SystemSounds.Asterisk.Play();
            if (!failed && Settings.GetBool(Keys.Reveal, false)) Shell.Reveal(report.Outputs);
        }

        void Undo()
        {
            if (lastOutputs.Count == 0) return;
            Shell.Recycle(lastOutputs);
            hud.ShowFlash("Moved to the Recycle Bin", lastOutputs.Count == 1 ? Path.GetFileName(lastOutputs[0]) : lastOutputs.Count + " files",
                          Cursor.Position);
            lastOutputs = new List<string>();
        }

        void Share(Slice slice, List<string> files)
        {
            switch (slice.Id)
            {
                case "share.copy":
                    Shell.CopyFiles(files);
                    hud.ShowFlash("Copied", "Paste it anywhere: a chat, an email, a folder", Cursor.Position);
                    break;
                case "share.path":
                    Clipboard.SetText(string.Join("\r\n", files));
                    hud.ShowFlash("Copied the path", files.Count == 1 ? files[0] : files.Count + " paths", Cursor.Position);
                    break;
                case "share.reveal":
                    Shell.Reveal(files);
                    break;
                case "share.share":
                    // The Share window needs a real window to belong to: a tiny invisible one at the pointer.
                    var owner = new Form
                    {
                        FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                        Location = Cursor.Position, Size = new Size(1, 1), Opacity = 0.01, TopMost = true,
                    };
                    owner.Show();
                    owner.Activate();
                    var closer = new System.Windows.Forms.Timer { Interval = 120_000 };
                    closer.Tick += (s, e) => { closer.Dispose(); owner.Close(); };
                    closer.Start();
                    if (!Shell.Share(owner.Handle, files))
                    {
                        owner.Close();
                        Shell.CopyFiles(files);
                        hud.ShowFlash("Copied instead", "This Windows can't show the Share window; paste it anywhere", Cursor.Position);
                    }
                    break;
            }
        }

        // MARK: Other ways in

        void ShowRingAtCenter(List<string> files, bool temporary)
        {
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            ring.ShowForClick(files, temporary, new Point(work.X + work.Width / 2, work.Y + work.Height / 2 - 40));
        }

        /// <summary>Ctrl+Alt+V: files you copied in Explorer, or a screenshot on the clipboard, open in the ring.</summary>
        void OpenClipboard()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                    if (files.Count > 0) { ring.ShowForClick(files, false, Cursor.Position); return; }
                }
                if (Clipboard.ContainsImage())
                {
                    using (var img = Clipboard.GetImage())
                    {
                        Directory.CreateDirectory(Paths.Inbox);
                        var path = Naming.Unique(Path.Combine(Paths.Inbox, "Clipboard Image.png"));
                        img.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        ring.ShowForClick(new[] { path }, true, Cursor.Position);
                        return;
                    }
                }
            }
            catch (ExternalException) { }
            hud.ShowFlash("Nothing to open", "Copy a file in Explorer or take a screenshot (Win+Shift+S) first", Cursor.Position);
        }

        void ChooseFiles()
        {
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "Pick files, then pick what to do with them" })
            {
                if (dialog.ShowDialog() == DialogResult.OK && dialog.FileNames.Length > 0) ShowRingAtCenter(dialog.FileNames.ToList(), false);
            }
        }

        // MARK: Tray

        void SetUpTray()
        {
            tray.Icon = TrayIcon(DarkTaskbar());
            tray.Text = "DragShift: drag a file, press Shift";
            var menu = new ContextMenuStrip();
            menu.Items.Add("Convert Files…", null, (s, e) => ChooseFiles());
            menu.Items.Add(new ToolStripMenuItem("Open Clipboard in Ring", null, (s, e) => OpenClipboard()) { ShortcutKeyDisplayString = "Ctrl+Alt+V" });
            menu.Items.Add(new ToolStripSeparator());
            var pause = new ToolStripMenuItem("Pause DragShift") { Checked = Settings.Paused, CheckOnClick = true };
            pause.CheckedChanged += (s, e) => Settings.Set(Keys.Paused, pause.Checked);
            menu.Items.Add(pause);
            menu.Items.Add("Settings…", null, (s, e) => ShowSettings());
            menu.Items.Add("How to Use", null, (s, e) => ShowWelcome());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit DragShift", null, (s, e) => Quit());
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                // Left click opens the same menu (like the Mac menu bar icon).
                typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(tray, null);
            };
            tray.Visible = true;
        }

        static bool DarkTaskbar()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return !(key?.GetValue("SystemUsesLightTheme") is int v && v == 1);
            }
            catch (Exception) { return true; }
        }

        /// <summary>A little ring of slices with the hot one lit, drawn crisp at the tray's size.</summary>
        static Icon TrayIcon(bool darkTaskbar)
        {
            int size = SystemInformation.SmallIconSize.Width;
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float s = size / 16f;
                    var ink = darkTaskbar ? Color.White : Color.FromArgb(30, 30, 30);
                    using (var pen = new Pen(ink, 2.4f * s))
                        for (int i = 0; i < 4; i++) g.DrawArc(pen, 2.2f * s, 2.2f * s, 11.6f * s, 11.6f * s, -90 + 45 + i * 90 - 33, 66);
                    using (var hot = new Pen(RingPainter.HotB, 2.6f * s)) g.DrawArc(hot, 2.2f * s, 2.2f * s, 11.6f * s, 11.6f * s, -90 - 33 + 45, 66);
                    using (var b = new SolidBrush(ink)) g.FillEllipse(b, 6.2f * s, 6.2f * s, 3.6f * s, 3.6f * s);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        SettingsForm settingsForm;
        WelcomeForm welcomeForm;

        void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(this);
                settingsForm.HotkeyChanged += on => receiver.ApplyHotkey(on);
                settingsForm.ShelfChanged += side => shelf.Side = side;
            }
            settingsForm.Show();
            settingsForm.Activate();
        }

        void ShowWelcome()
        {
            if (welcomeForm == null || welcomeForm.IsDisposed)
            {
                welcomeForm = new WelcomeForm(this);
                welcomeForm.FormClosed += (s, e) => Settings.Set(Keys.WelcomeDone, true);
            }
            welcomeForm.Show();
            welcomeForm.Activate();
        }

        void Quit()
        {
            tray.Visible = false;
            tracker.Dispose();
            touch.Dispose();
            receiver.Dispose();
            ExitThread();
        }
    }

    /// <summary>A hidden window for the clipboard shortcut and for files handed over by a second copy
    /// (Explorer's "Send to", or opening the .exe with files).</summary>
    sealed class Receiver : NativeWindow, IDisposable
    {
        public const string Title = "DragShift.Receiver";
        const int MaxBytes = 4 << 20, MaxFiles = 1000;
        public event Action<List<string>> Files;
        public event Action Hotkey;
        bool registered;

        public Receiver()
        {
            CreateHandle(new CreateParams { Caption = Title, Parent = (IntPtr)(-3) });
        }

        public void ApplyHotkey(bool on)
        {
            if (registered) { Native.UnregisterHotKey(Handle, 1); registered = false; }
            if (!on) return;
            registered = Native.RegisterHotKey(Handle, 1, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)System.Windows.Forms.Keys.V);
            Log.Line("hotkey Ctrl+Alt+V: " + (registered ? "on" : "taken by another app"));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == 1) Hotkey?.Invoke();
            else if (m.Msg == Native.WM_COPYDATA)
            {
                var data = (Native.COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(Native.COPYDATASTRUCT));
                // only our own hand-off (marker 1, a sane size, paths that exist): any other program on this desktop
                // could send this window a message
                if (data.Data != (IntPtr)1 || data.Bytes <= 0 || data.Bytes > MaxBytes || data.Ptr == IntPtr.Zero)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
                var text = Marshal.PtrToStringUni(data.Ptr, data.Bytes / 2).TrimEnd('\0');
                var files = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => p.Length < 32768 && (File.Exists(p) || Directory.Exists(p))).Take(MaxFiles).ToList();
                if (files.Count > 0) Files?.Invoke(files);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>From a second copy: hand the files to the running one. False if none is running.</summary>
        public static bool SendToRunning(IEnumerable<string> files)
        {
            var target = Native.FindWindowEx((IntPtr)(-3), IntPtr.Zero, null, Title);
            if (target == IntPtr.Zero) return false;
            var text = string.Join("\n", files);
            var ptr = Marshal.StringToHGlobalUni(text);
            try
            {
                var data = new Native.COPYDATASTRUCT { Data = (IntPtr)1, Bytes = (text.Length + 1) * 2, Ptr = ptr };
                Native.SendMessage(target, Native.WM_COPYDATA, IntPtr.Zero, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return true;
        }

        public void Dispose()
        {
            if (registered) Native.UnregisterHotKey(Handle, 1);
            DestroyHandle();
        }
    }
}
