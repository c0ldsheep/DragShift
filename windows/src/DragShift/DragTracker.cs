using System;
using System.Drawing;
using System.Threading;

namespace DragShift
{
    /// <summary>
    /// Watches the mouse and keyboard everywhere (low-level hooks: no admin rights, no special permission)
    /// and turns "left button held + moved" plus Shift or a mouse wheel click into "open the ring".
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

        readonly Native.HookProc mouseProc, keyProc; // kept in fields so they are never garbage-collected
        IntPtr mouseHook, keyHook;
        SynchronizationContext ui;
        bool ignorePress, shiftHeld;
        int swallowUp;
        readonly IntPtr ibeam = Native.LoadCursor(IntPtr.Zero, (IntPtr)Native.IDC_IBEAM);
        readonly int dragX = Math.Max(4, Native.GetSystemMetrics(Native.SM_CXDRAG));
        readonly int dragY = Math.Max(4, Native.GetSystemMetrics(Native.SM_CYDRAG));

        public DragTracker()
        {
            mouseProc = OnMouse;
            keyProc = OnKey;
        }

        public void Start()
        {
            ui = SynchronizationContext.Current ?? new SynchronizationContext();
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

        void Post(Action a) => ui.Post(_ => a(), null);

        IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = wParam.ToInt32();
                var info = (Native.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                var p = new Point(info.Pt.X, info.Pt.Y);
                switch (msg)
                {
                    case Native.WM_LBUTTONDOWN:
                        Down = true;
                        Dragging = false;
                        DownAt = p;
                        // Selecting text (I-beam cursor) is never a file drag.
                        var ci = new Native.CURSORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.CURSORINFO)) };
                        ignorePress = Native.GetCursorInfo(ref ci) && ci.Cursor == ibeam;
                        Post(() => Pressed?.Invoke(p, true));
                        break;
                    case Native.WM_RBUTTONDOWN:
                        Post(() => Pressed?.Invoke(p, false));
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
                    case Native.WM_MBUTTONDOWN:
                    case Native.WM_XBUTTONDOWN:
                        if (Down && Dragging)
                        {
                            // Swallowed, so the app you're dragging from never sees it (some cancel the drag on a second button).
                            swallowUp = msg + 1;
                            Post(() => Triggered?.Invoke(Trigger.Mouse));
                            return (IntPtr)1;
                        }
                        break;
                    case Native.WM_MBUTTONUP:
                    case Native.WM_XBUTTONUP:
                        if (swallowUp == msg)
                        {
                            swallowUp = 0;
                            return (IntPtr)1;
                        }
                        break;
                }
            }
            return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
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
