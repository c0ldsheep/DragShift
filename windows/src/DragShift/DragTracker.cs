using System;
using System.Drawing;
using System.Threading;
using DragShift.Core;

namespace DragShift
{
    /// <summary>
    /// Watches the mouse and keyboard everywhere (low-level hooks: no admin rights, no special permission)
    /// and turns "left button held + moved" plus Shift or another mouse button (right, wheel, side) into "open the ring".
    /// The ring window itself then confirms it's a real file drag: Windows only hands it files when it is.
    /// Hooks must answer fast, so everything else is posted to the UI thread.
    /// </summary>
    sealed class DragTracker : IDisposable
    {
        public enum Trigger { Shift, Finger, Mouse }

        /// <summary>A trigger while the left button holds something (a file, if the ring then confirms it).</summary>
        public event Action<Trigger> Triggered;
        /// <summary>Shift tapped while nothing is being dragged (flips pages of an open ring).</summary>
        public event Action ShiftTapped;
        public event Action DragStarted;
        public event Action Released;
        /// <summary>Any left or right press, so an open ring can close when you click elsewhere.</summary>
        public event Action<Point, bool> Pressed;
        /// <summary>Asked inside the hook: return true when Esc was used (the ring closes and Esc goes no further).</summary>
        public Func<bool> Escape;

        public bool Down { get; private set; }
        public bool Dragging { get; private set; }
        public Point DownAt { get; private set; }

        /// <summary>The mouse buttons that may open the ring while the left button holds something. A button that is not
        /// in here is never touched: the app you drag from gets it as usual. (None while paused or switched off.)</summary>
        public MouseTrigger Buttons
        {
            get => (MouseTrigger)buttons;
            set => buttons = (int)value;
        }

        // Answers the hook needs from Windows, replaceable so the self-test can drive the real logic without a real mouse
        // (and without writing to the real log).
        internal Func<bool> TextCursor, LeftHeld, FullScreen;
        internal Action<string> Note = Log.Line;

        readonly Native.HookProc mouseProc, keyProc; // kept in fields so they are never garbage-collected
        IntPtr mouseHook, keyHook;
        SynchronizationContext ui;
        bool ignorePress, shiftHeld;
        volatile int buttons = (int)MouseTrigger.All;
        int heldBack; // buttons whose press we swallowed: their release must be swallowed too, or the app sees half a click
        DateTime fullScreenNoted = DateTime.MinValue;
        readonly IntPtr ibeam = Native.LoadCursor(IntPtr.Zero, (IntPtr)Native.IDC_IBEAM);
        readonly int dragX = Math.Max(4, Native.GetSystemMetrics(Native.SM_CXDRAG));
        readonly int dragY = Math.Max(4, Native.GetSystemMetrics(Native.SM_CYDRAG));

        public DragTracker()
        {
            mouseProc = OnMouse;
            keyProc = OnKey;
            TextCursor = IsTextCursor;
            LeftHeld = Native.LeftButtonHeld;
            FullScreen = Native.FullScreenAppInFront;
        }

        public void Start()
        {
            ui = SynchronizationContext.Current ?? new SynchronizationContext();
            Down = Dragging = false;
            heldBack = 0;
            var module = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
            keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, module, 0);
            Log.Line("hooks: mouse " + (mouseHook != IntPtr.Zero) + ", keyboard " + (keyHook != IntPtr.Zero));
        }

        /// <summary>Windows drops a hook that ever answers too slowly; reinstalling after sleep keeps them alive.</summary>
        public void Restart()
        {
            Stop();
            Start();
        }

        void Post(Action a)
        {
            var context = ui;
            if (context == null) a(); // no hooks installed (the self-test): nothing to hop to
            else context.Post(_ => a(), null);
        }

        bool IsTextCursor()
        {
            var ci = new Native.CURSORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.CURSORINFO)) };
            return Native.GetCursorInfo(ref ci) && ci.Cursor == ibeam;
        }

        IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var info = (Native.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                if (Handle(wParam.ToInt32(), new Point(info.Pt.X, info.Pt.Y), info.MouseData)) return (IntPtr)1;
            }
            return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        /// <summary>One mouse event. True = swallow it, so the app underneath never sees it.</summary>
        internal bool Handle(int msg, Point p, uint mouseData)
        {
            switch (msg)
            {
                case Native.WM_LBUTTONDOWN:
                    Down = true;
                    Dragging = false;
                    DownAt = p;
                    // Selecting text (I-beam cursor) is never a file drag.
                    ignorePress = TextCursor();
                    Post(() => Pressed?.Invoke(p, true));
                    break;
                case Native.WM_MOUSEMOVE:
                    if (Down && !Dragging && !ignorePress && (Math.Abs(p.X - DownAt.X) >= dragX || Math.Abs(p.Y - DownAt.Y) >= dragY))
                    {
                        Dragging = true;
                        bool shiftAlready = (Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0;
                        Post(() =>
                        {
                            DragStarted?.Invoke();
                            // "Hold Shift first, then grab the file" works too.
                            if (shiftAlready) Triggered?.Invoke(Trigger.Shift);
                        });
                    }
                    break;
                case Native.WM_LBUTTONUP:
                    bool was = Down;
                    Down = false;
                    Dragging = false;
                    if (was) Post(() => Released?.Invoke());
                    break;
                case Native.WM_RBUTTONDOWN:
                case Native.WM_MBUTTONDOWN:
                case Native.WM_XBUTTONDOWN:
                {
                    var button = ButtonOf(msg, mouseData);
                    heldBack &= ~(int)button; // a release that never came (hooks restarted) must not eat this press's release
                    if (Down && Dragging && (buttons & (int)button) != 0)
                    {
                        if (!LeftHeld())
                        {
                            // The left button went up without us seeing it (secure desktop, a restart): this is no drag,
                            // and the right button must keep working.
                            Down = Dragging = false;
                        }
                        else if (FullScreen())
                        {
                            // A game or a full-screen video is in front: its buttons are its own.
                            if ((DateTime.UtcNow - fullScreenNoted).TotalSeconds > 30)
                            {
                                fullScreenNoted = DateTime.UtcNow;
                                Post(() => Note("mouse button left alone: a full-screen app is in front"));
                            }
                        }
                        else
                        {
                            heldBack |= (int)button;
                            // Swallowed, so the app you're dragging from never sees it (most cancel the drag on a second button).
                            Post(() => Triggered?.Invoke(Trigger.Mouse));
                            return true;
                        }
                    }
                    if (msg == Native.WM_RBUTTONDOWN) Post(() => Pressed?.Invoke(p, false));
                    break;
                }
                case Native.WM_RBUTTONUP:
                case Native.WM_MBUTTONUP:
                case Native.WM_XBUTTONUP:
                {
                    var button = ButtonOf(msg, mouseData);
                    if ((heldBack & (int)button) != 0)
                    {
                        heldBack &= ~(int)button;
                        return true;
                    }
                    break;
                }
            }
            return false;
        }

        /// <summary>Which button a mouse message is about. The two side buttons share one message; the high word of the
        /// data says which (1 = back, 2 = forward).</summary>
        static MouseTrigger ButtonOf(int msg, uint mouseData)
        {
            switch (msg)
            {
                case Native.WM_RBUTTONDOWN:
                case Native.WM_RBUTTONUP:
                    return MouseTrigger.Right;
                case Native.WM_MBUTTONDOWN:
                case Native.WM_MBUTTONUP:
                    return MouseTrigger.Middle;
                default:
                    switch (mouseData >> 16)
                    {
                        case 1: return MouseTrigger.Back;
                        case 2: return MouseTrigger.Forward;
                        default: return MouseTrigger.None;
                    }
            }
        }

        IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = wParam.ToInt32();
                var info = (Native.KBDLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                int vk = (int)info.VkCode;
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                if (vk == Native.VK_LSHIFT || vk == Native.VK_RSHIFT || vk == Native.VK_SHIFT)
                {
                    if (down && !shiftHeld)
                    {
                        shiftHeld = true; // held keys repeat; only the first press counts
                        if (Down && Dragging && !ignorePress) Post(() => Triggered?.Invoke(Trigger.Shift));
                        else if (!Down) Post(() => ShiftTapped?.Invoke());
                    }
                    else if (up) shiftHeld = false;
                }
                else if (vk == Native.VK_ESCAPE && down && Escape != null && Escape())
                {
                    return (IntPtr)1;
                }
            }
            return Native.CallNextHookEx(keyHook, code, wParam, lParam);
        }

        void Stop()
        {
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            mouseHook = keyHook = IntPtr.Zero;
        }

        public void Dispose() => Stop();
    }
}
