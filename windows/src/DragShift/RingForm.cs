using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DragShift.Core;
using G = DragShift.Core.Geometry;

namespace DragShift
{
    /// <summary>
    /// The ring. While you drag, it is a drop target under the pointer: push the file toward a slice and let go,
    /// or let go in the middle and it stays open (pinned) so you can lift your fingers, glide and click.
    /// </summary>
    sealed class RingForm : LayeredForm
    {
        public readonly RingModel Model = new RingModel();
        /// <summary>slice, files, files are temporary (clipboard)</summary>
        public event Action<Slice, List<string>, bool> Perform;
        public event Action<string> Flash;
        public Func<IList<string>, List<RingPage>> BuildPages;
        public Func<string, bool> ToggleFavorite;
        public Func<FileKind, Slice> LastActionFor;
        public Func<FileKind, string> UsualFor;

        public bool IsOpen => Visible && !probe;
        public bool IsProbing => Visible && probe;
        public DateTime OpenedAt { get; private set; }
        public List<string> Files { get; private set; } = new List<string>();
        /// <summary>The last probe found something that isn't files (text, a window): don't try again this press.</summary>
        public bool RejectedThisPress;

        bool probe, temporary, dropped, catchAll, dark;
        double scale = 1;
        Rectangle ringRect, windowRect;
        float appear = 1;
        DateTime appearStart;
        readonly Timer anim = new Timer { Interval = 15 };
        readonly Timer dwell = new Timer { Interval = 260 };
        readonly Timer grace = new Timer { Interval = 300 };
        readonly Timer breathe = new Timer { Interval = 50 };
        int dwellTab = -1;
        readonly DragImages dragImages = new DragImages();

        public RingForm()
        {
            AllowDrop = true;
            Text = "DragShift Ring";
            anim.Tick += (s, e) =>
            {
                appear = (float)Math.Min(1, (DateTime.Now - appearStart).TotalMilliseconds / 140);
                if (appear >= 1) anim.Stop();
                Render();
            };
            dwell.Tick += (s, e) =>
            {
                dwell.Stop();
                if (dwellTab >= 0 && dwellTab < Model.Pages.Count && dwellTab != Model.Page)
                {
                    Model.Page = dwellTab;
                    Model.Hovered = null;
                    Render();
                }
            };
            breathe.Tick += (s, e) =>
            {
                if (!IsOpen) { breathe.Stop(); return; }
                if (!Model.Glowing || anim.Enabled) return;
                Model.Pulse = (1 - Math.Cos(Environment.TickCount % 1600 / 1600.0 * 2 * Math.PI)) / 2;
                Render();
            };
            grace.Tick += (s, e) =>
            {
                grace.Stop();
                if (!dropped && Model.Mode == RingModel.RingMode.Drag) HideRing();
            };
        }

        // MARK: Showing

        /// <summary>A trigger fired during a mouse drag: wait, invisibly, under the pointer for Windows to hand us
        /// the files. Text selections and window moves never get that far.</summary>
        public void Probe(Point cursor)
        {
            if (Visible || RejectedThisPress) return;
            probe = true;
            dropped = false;
            catchAll = false;
            Model.Armed = null;
            Model.Mode = RingModel.RingMode.Drag;
            scale = Native.ScaleAt(cursor.X, cursor.Y);
            int r = (int)(70 * scale);
            windowRect = new Rectangle(cursor.X - r, cursor.Y - r, r * 2, r * 2);
            Render();
            Show();
            Log.Line("ring: probing at " + cursor);
        }

        void Reveal(List<string> files, Point cursor)
        {
            probe = false;
            Files = files;
            temporary = false;
            Configure();
            Place(cursor, false);
            OpenedAt = DateTime.Now;
            StartAppear();
            Log.Line("ring: " + files.Count + " file(s), " + Inspector.Kind(files));
        }

        /// <summary>Clipboard shortcut, Send To, the tray menu: the ring opens already pinned, for clicking.</summary>
        public void ShowForClick(IList<string> files, bool temp, Point at)
        {
            probe = false;
            dropped = false;
            catchAll = false;
            Files = files.ToList();
            temporary = temp;
            Model.Mode = RingModel.RingMode.Click;
            Model.Armed = null;
            Configure();
            Place(at, false);
            OpenedAt = DateTime.Now;
            StartAppear();
        }

        void Configure()
        {
            dark = DarkMode;
            Model.Pages = BuildPages(Files);
            Model.Usual = UsualFor?.Invoke(Inspector.Kind(Files));
            Model.Pulse = 1;
            Model.Page = 0;
            Model.Hovered = null;
            Model.HoveredTab = null;
            long size = Files.Sum(Inspector.Size);
            Model.Title = Files.Count == 1 ? Path.GetFileName(Files[0].TrimEnd('\\')) : Files.Count + " files";
            Model.Subtitle = Files.Count == 1 ? Bytes.Format(size) : Inspector.Label(Inspector.Kind(Files)) + " · " + Bytes.Format(size);
            Model.Thumbnail?.Dispose();
            Model.Thumbnail = null;
            var first = Files.FirstOrDefault();
            if (first == null) return;
            if (Inspector.Kind(first) == FileKind.Image)
            {
                var forFiles = Files;
                Task.Run(() => Engine.Images.Load(first, 96)).ContinueWith(t =>
                {
                    if (t.Status != TaskStatus.RanToCompletion) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (Files != forFiles || !Visible) { t.Result.Dispose(); return; }
                        Model.Thumbnail = t.Result;
                        Render();
                    }));
                });
            }
            else if (File.Exists(first))
            {
                try { using (var icon = Icon.ExtractAssociatedIcon(first)) Model.Thumbnail = icon?.ToBitmap(); } catch (Exception) { }
            }
        }

        /// <summary>Ring centred on the point, kept inside the screen. catchAll covers the whole screen (armed repeat:
        /// "let go anywhere") with an almost-clear layer, so the drop lands on us wherever it happens.</summary>
        void Place(Point center, bool full)
        {
            scale = Native.ScaleAt(center.X, center.Y);
            int size = (int)Math.Ceiling(G.Size * scale);
            var work = Native.WorkAreaAt(center.X, center.Y);
            int left = Num.Clamp(center.X - (int)Math.Round(G.CenterX * scale), work.Left, Math.Max(work.Left, work.Right - size));
            int top = Num.Clamp(center.Y - (int)Math.Round(G.CenterY * scale), work.Top, Math.Max(work.Top, work.Bottom - size));
            ringRect = new Rectangle(left, top, size, size);
            catchAll = full;
            windowRect = full ? Native.ScreenAt(center.X, center.Y) : ringRect;
        }

        Point RingCenter => new Point(ringRect.X + (int)Math.Round(G.CenterX * scale), ringRect.Y + (int)Math.Round(G.CenterY * scale));

        void StartAppear()
        {
            appear = 0;
            appearStart = DateTime.Now;
            Render();
            if (!Visible) Show();
            anim.Start();
            // "Your usual" breathes slowly (holds still when Windows animations are off).
            if (Model.Usual != null && Native.Animations) breathe.Start();
        }

        void Render()
        {
            if (windowRect.Width <= 0 || windowRect.Height <= 0) return;
            using (var frame = new Bitmap(windowRect.Width, windowRect.Height, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(frame))
                {
                    g.Clear(Color.Transparent);
                    using (var catcher = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                    {
                        // Almost clear, but still "there" for Windows: while dragging, the whole square catches the
                        // drop and picks a slice by direction, so you don't have to land exactly on one.
                        if (probe) g.FillEllipse(catcher, 0, 0, windowRect.Width, windowRect.Height);
                        else if (catchAll || Model.Mode == RingModel.RingMode.Drag) g.FillRectangle(catcher, 0, 0, windowRect.Width, windowRect.Height);
                    }
                    if (!probe)
                    {
                        float ease = 1 - (1 - appear) * (1 - appear) * (1 - appear);
                        float s = (float)scale * (0.86f + 0.14f * ease);
                        g.TranslateTransform(ringRect.X - windowRect.X + (float)(G.CenterX * scale), ringRect.Y - windowRect.Y + (float)(G.CenterY * scale));
                        g.ScaleTransform(s, s);
                        g.TranslateTransform((float)-G.CenterX, (float)-G.CenterY);
                        RingPainter.Paint(g, Model, dark);
                    }
                }
                Present(frame, windowRect, probe ? (byte)255 : (byte)Math.Round(255 * Math.Min(1, appear * 1.4)));
            }
        }

        // MARK: Pointer

        G.Hit HitAt(Point screen, bool byDirection)
        {
            double x = (screen.X - ringRect.X) / scale, y = (screen.Y - ringRect.Y) / scale;
            return G.HitTest(x, y, Model.Slices.Count, Model.Pages.Count, byDirection);
        }

        void Hover(G.Hit hit, bool dwellTabs)
        {
            int? slice = hit.Kind == G.HitKind.Slice ? hit.Index : (int?)null;
            int? tab = hit.Kind == G.HitKind.Tab ? hit.Index : (int?)null;
            if (tab != Model.HoveredTab)
            {
                dwell.Stop();
                dwellTab = tab ?? -1;
                if (dwellTabs && tab.HasValue) dwell.Start();
            }
            if (slice == Model.Hovered && tab == Model.HoveredTab) return;
            Model.Hovered = slice;
            Model.HoveredTab = tab;
            Render();
        }

        static List<string> FilesIn(IDataObject data) =>
            (data?.GetData(DataFormats.FileDrop) as string[])?.Where(p => !string.IsNullOrEmpty(p)).ToList();

        static DragDropEffects CopyIfAllowed(DragEventArgs e) =>
            (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;

        protected override void OnDragEnter(DragEventArgs e)
        {
            var files = FilesIn(e.Data);
            if (files == null || files.Count == 0)
            {
                e.Effect = DragDropEffects.None;
                if (probe)
                {
                    RejectedThisPress = true; // text, a link, a picture from a web page without a file…
                    HideRing();
                }
                return;
            }
            // Never "move": the original must stay where it is.
            e.Effect = CopyIfAllowed(e);
            if (probe) Reveal(files, new Point(e.X, e.Y));
            dragImages.Enter(Handle, e);
            Hover(HitAt(new Point(e.X, e.Y), true), true);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            e.Effect = probe ? DragDropEffects.None : CopyIfAllowed(e);
            if (probe) return;
            dragImages.Over(e);
            if (Model.Armed == null) Hover(HitAt(new Point(e.X, e.Y), true), true);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            dragImages.Leave();
            if (probe) return;
            Hover(G.Hit.Of(G.HitKind.None), false);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            dragImages.Drop(e);
            dropped = true;
            var files = FilesIn(e.Data) ?? Files;
            var hit = HitAt(new Point(e.X, e.Y), true);
            // Act once Windows has finished the drop, so the app you dragged from is never kept waiting.
            BeginInvoke((Action)(() =>
            {
                if (Model.Armed != null)
                {
                    var a = Model.Armed;
                    HideRing();
                    Perform?.Invoke(a, files, false);
                }
                else if (hit.Kind == G.HitKind.Slice && hit.Index < Model.Slices.Count)
                {
                    var s = Model.Slices[hit.Index];
                    HideRing();
                    Perform?.Invoke(s, files, false);
                }
                else Pin(files);
            }));
        }

        /// <summary>Let go in the middle: the ring stays open. Lift your fingers, glide to a slice, click.</summary>
        void Pin(List<string> files)
        {
            Files = files;
            Model.Mode = RingModel.RingMode.Pinned;
            Model.Hovered = null;
            Place(RingCenter, false);
            Render();
            Log.Line("ring: pinned");
        }

        /// <summary>The left button went up somewhere. A drop on the ring is handled by OnDragDrop (which can
        /// arrive a moment later); anything else closes the ring.</summary>
        public void DragEnded()
        {
            RejectedThisPress = false;
            if (!Visible) return;
            if (probe) { HideRing(); return; }
            if (Model.Mode != RingModel.RingMode.Drag) return;
            grace.Stop();
            grace.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (probe || Model.Mode == RingModel.RingMode.Drag) return;
            Hover(HitAt(PointToScreen(e.Location), false), false);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!probe && Model.Mode != RingModel.RingMode.Drag) Hover(G.Hit.Of(G.HitKind.None), false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (probe || Model.Mode == RingModel.RingMode.Drag) return;
            var hit = HitAt(PointToScreen(e.Location), false);
            if (e.Button == MouseButtons.Right && hit.Kind == G.HitKind.Slice && hit.Index < Model.Slices.Count)
            {
                var s = Model.Slices[hit.Index];
                bool added = ToggleFavorite?.Invoke(s.Id) ?? false;
                Flash?.Invoke(added ? "★ " + s.Title + " added to favourites" : s.Title + " removed from favourites");
                var pageId = Model.Pages[Model.Page].Id;
                Model.Pages = BuildPages(Files);
                Model.Page = Math.Max(0, Model.Pages.FindIndex(p => p.Id == pageId));
                Render();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (hit.Kind == G.HitKind.Slice && hit.Index < Model.Slices.Count)
            {
                var s = Model.Slices[hit.Index];
                var files = Files;
                bool temp = temporary;
                HideRing();
                Perform?.Invoke(s, files, temp);
            }
            else if (hit.Kind == G.HitKind.Tab)
            {
                Model.Page = hit.Index;
                Model.Hovered = null;
                Render();
            }
        }

        // MARK: Pages and repeat

        public void NextPage()
        {
            if (!IsOpen || Model.Pages.Count < 2) return;
            Model.Page = (Model.Page + 1) % Model.Pages.Count;
            Model.Hovered = null;
            Render();
        }

        /// <summary>Double-tap: arm the last thing done to this kind of file; letting go anywhere repeats it.</summary>
        public void RepeatLast()
        {
            var last = LastActionFor?.Invoke(Inspector.Kind(Files));
            if (last == null)
            {
                Flash?.Invoke("Nothing to repeat yet. Pick something once, then double-tap to repeat it.");
                return;
            }
            Model.Armed = last;
            Model.Hovered = null;
            if (Model.Mode == RingModel.RingMode.Drag) Place(RingCenter, true);
            Render();
            Log.Line("ring: armed " + last.Id);
        }

        public bool Contains(Point screen) => Visible && !probe && ringRect.Contains(screen);

        public void HideRing()
        {
            anim.Stop();
            breathe.Stop();
            dwell.Stop();
            grace.Stop();
            probe = false;
            catchAll = false;
            Model.Armed = null;
            Model.Hovered = null;
            Model.HoveredTab = null;
            if (Visible) Hide();
        }
    }
}
