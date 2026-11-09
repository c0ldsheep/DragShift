using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DragShift
{
    /// <summary>
    /// A see-through, always-on-top window drawn with per-pixel transparency. It never takes the keyboard
    /// focus from the app you're in, and fully clear pixels let clicks and drops pass through to what's below.
    /// </summary>
    class LayeredForm : Form
    {
        public LayeredForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE; // clicks work, focus stays where it was
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Puts a premultiplied 32-bit picture on screen at <paramref name="bounds"/> (pixels).</summary>
        protected void Present(Bitmap frame, Rectangle bounds, byte opacity = 255)
        {
            if (!IsHandleCreated) CreateHandle();
            if (Bounds != bounds) SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            int w = frame.Width, h = frame.Height;
            var screen = Native.GetDC(IntPtr.Zero);
            var mem = Native.CreateCompatibleDC(screen);
            var info = new Native.BITMAPINFOHEADER
            {
                Size = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), Width = w, Height = -h, Planes = 1, BitCount = 32,
            };
            var dib = Native.CreateDIBSection(mem, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            var old = Native.SelectObject(mem, dib);
            try
            {
                var data = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        Marshal.Copy(row, 0, bits + y * w * 4, row.Length);
                    }
                }
                finally { frame.UnlockBits(data); }
                var dst = new Native.POINT(bounds.X, bounds.Y);
                var size = new Native.SIZE(w, h);
                var src = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(mem, old);
                Native.DeleteObject(dib);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>Follows Windows' "Choose your app mode" setting.</summary>
        public static bool DarkMode
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
                }
                catch (Exception) { return false; }
            }
        }
    }

    /// <summary>Asks Windows to draw the file's own drag picture over our windows too (like over Explorer).</summary>
    sealed class DragImages
    {
        [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDropTargetHelper
        {
            void DragEnter(IntPtr target, System.Runtime.InteropServices.ComTypes.IDataObject data, ref Native.POINT pt, int effect);
            void DragLeave();
            void DragOver(ref Native.POINT pt, int effect);
            void Drop(System.Runtime.InteropServices.ComTypes.IDataObject data, ref Native.POINT pt, int effect);
            void Show(bool show);
        }

        [ComImport, Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
        class DragDropHelper { }

        IDropTargetHelper helper;

        IDropTargetHelper Helper
        {
            get
            {
                if (helper == null) try { helper = (IDropTargetHelper)new DragDropHelper(); } catch (Exception) { }
                return helper;
            }
        }

        public void Enter(IntPtr hwnd, DragEventArgs e)
        {
            try
            {
                var pt = new Native.POINT(e.X, e.Y);
                if (e.Data is System.Runtime.InteropServices.ComTypes.IDataObject com) Helper?.DragEnter(hwnd, com, ref pt, (int)e.Effect);
            }
            catch (Exception) { }
        }

        public void Over(DragEventArgs e)
        {
            try { var pt = new Native.POINT(e.X, e.Y); Helper?.DragOver(ref pt, (int)e.Effect); } catch (Exception) { }
        }

        public void Leave()
        {
            try { Helper?.DragLeave(); } catch (Exception) { }
        }

        public void Drop(DragEventArgs e)
        {
            try
            {
                var pt = new Native.POINT(e.X, e.Y);
                if (e.Data is System.Runtime.InteropServices.ComTypes.IDataObject com) Helper?.Drop(com, ref pt, (int)e.Effect);
            }
            catch (Exception) { }
        }
    }
}
