using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DragShift
{
    /// <summary>
    /// Counts fingers on a Precision Touchpad (almost every Windows laptop since 2016) through raw input,
    /// read-only. The touchpad reports "contact count" with each frame; when frames stop, nothing is touching.
    /// </summary>
    sealed class Touchpad : NativeWindow, IDisposable
    {
        public event Action<int, int> Changed; // old, new
        public int Count { get; private set; }
        public bool Available { get; private set; }

        readonly Dictionary<IntPtr, IntPtr> preparsed = new Dictionary<IntPtr, IntPtr>();
        readonly Timer quiet = new Timer { Interval = 120 };

        public Touchpad()
        {
            // A message-only window: never shown, just receives WM_INPUT.
            CreateHandle(new CreateParams { Parent = (IntPtr)(-3) });
            quiet.Tick += (s, e) => { quiet.Stop(); Set(0); };
        }

        public bool Start()
        {
            var dev = new[]
            {
                // Digitizer page, touch pad usage; INPUTSINK = also while other apps are in front.
                new Native.RAWINPUTDEVICE { UsagePage = 0x0D, Usage = 0x05, Flags = Native.RIDEV_INPUTSINK, Target = Handle },
            };
            Available = Native.RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
            Log.Line("touchpad: " + (Available ? "listening" : "none (error " + Marshal.GetLastWin32Error() + ")"));
            return Available;
        }

        void Set(int n)
        {
            if (n == Count) return;
            int old = Count;
            Count = n;
            Changed?.Invoke(old, n);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_INPUT) Read(m.LParam);
            base.WndProc(ref m);
        }

        void Read(IntPtr handle)
        {
            uint headerSize = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
            uint size = 0;
            Native.GetRawInputData(handle, Native.RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0) return;
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.GetRawInputData(handle, Native.RID_INPUT, buf, ref size, headerSize) != size) return;
                var header = (Native.RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(Native.RAWINPUTHEADER));
                if (header.Type != Native.RIM_TYPEHID) return;
                var pre = Preparsed(header.Device);
                if (pre == IntPtr.Zero) return;
                // RAWHID: report size, report count, then the reports.
                int reportSize = Marshal.ReadInt32(buf, (int)headerSize);
                int reports = Marshal.ReadInt32(buf, (int)headerSize + 4);
                for (int i = 0; i < reports; i++)
                {
                    var report = buf + (int)headerSize + 8 + i * reportSize;
                    // Contact count (digitizer page 0x0D, usage 0x54) is in the first report of each frame.
                    if (Native.HidP_GetUsageValue(0, 0x0D, 0, 0x54, out uint contacts, pre, report, (uint)reportSize) == Native.HIDP_STATUS_SUCCESS
                        && contacts > 0)
                    {
                        Set((int)Math.Min(contacts, 10));
                        quiet.Stop();
                        quiet.Start();
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        IntPtr Preparsed(IntPtr device)
        {
            if (preparsed.TryGetValue(device, out var p)) return p;
            uint size = 0;
            Native.GetRawInputDeviceInfo(device, Native.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
            p = IntPtr.Zero;
            if (size > 0)
            {
                p = Marshal.AllocHGlobal((int)size);
                if (Native.GetRawInputDeviceInfo(device, Native.RIDI_PREPARSEDDATA, p, ref size) == unchecked((uint)-1))
                {
                    Marshal.FreeHGlobal(p);
                    p = IntPtr.Zero;
                }
            }
            preparsed[device] = p;
            return p;
        }

        public void Dispose()
        {
            quiet.Dispose();
            foreach (var p in preparsed.Values) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            preparsed.Clear();
            DestroyHandle();
        }
    }
}
