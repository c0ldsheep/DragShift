using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DragShift
{
    static class Paths
    {
        public static string Roaming => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DragShift");
        public static string Local => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragShift");
        public static string Settings => Path.Combine(Roaming, "settings.ini");
        public static string LogFile => Path.Combine(Local, "DragShift.log");
        /// <summary>Clipboard pictures land here before the ring opens (their results go to Downloads).</summary>
        public static string Inbox => Path.Combine(Local, "Inbox");
        /// <summary>This .exe's full path. Windows Forms hands back a forward slash after a folder with a # in its name
        /// (C:\Tools\R&amp;D #1/DragShift.exe), so it goes through GetFullPath to get plain backslashes.</summary>
        public static string Exe => Path.GetFullPath(Application.ExecutablePath);

        public static string Downloads
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
                    {
                        var v = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                        if (!string.IsNullOrEmpty(v)) return Environment.ExpandEnvironmentVariables(v);
                    }
                }
                catch (Exception) { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }
    }

    /// <summary>A small log for "it didn't work" reports (%LOCALAPPDATA%\DragShift\DragShift.log, kept under 512 KB).</summary>
    static class Log
    {
        static readonly object gate = new object();

        public static void Start(string version)
        {
            try
            {
                Directory.CreateDirectory(Paths.Local);
                if (File.Exists(Paths.LogFile) && new FileInfo(Paths.LogFile).Length > 512_000) File.Delete(Paths.LogFile);
            }
            catch (IOException) { }
            Line("DragShift " + version + " started on " + Environment.OSVersion.VersionString);
        }

        public static void Line(string s)
        {
            lock (gate)
            {
                try { File.AppendAllText(Paths.LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + s + "\r\n", Encoding.UTF8); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Show in Explorer, copy, share, recycle.</summary>
    static class Shell
    {
        public static void Reveal(IList<string> paths)
        {
            if (paths.Count == 0) return;
            try
            {
                var folder = Path.GetDirectoryName(paths[0].TrimEnd('\\'));
                var same = paths.Where(p => string.Equals(Path.GetDirectoryName(p.TrimEnd('\\')), folder, StringComparison.OrdinalIgnoreCase)).ToList();
                if (same.Count > 1)
                {
                    RevealTogether(folder, same, Native.SHOpenFolderAndSelectItems);
                    return;
                }
                Process.Start("explorer.exe", "/select,\"" + paths[0] + "\"");
            }
            catch (Exception e) { Log.Line("reveal failed: " + e.Message); }
        }

        internal static void RevealTogether(string folder, IList<string> paths, Func<IntPtr, uint, IntPtr[], uint, int> open)
        {
            var dir = Native.ILCreateFromPath(folder);
            var items = paths.Select(Native.ILCreateFromPath).Where(p => p != IntPtr.Zero).ToArray();
            try
            {
                // The folder is absolute; each selected item must be relative to that folder.
                // The child pointers borrow the absolute lists, which stay alive until the call returns.
                var children = items.Select(Native.ILFindLastID).ToArray();
                Marshal.ThrowExceptionForHR(open(dir, (uint)children.Length, children, 0));
            }
            finally { Native.ILFree(dir); foreach (var item in items) Native.ILFree(item); }
        }

        public static void CopyFiles(IList<string> paths)
        {
            var list = new StringCollection();
            list.AddRange(paths.ToArray());
            Clipboard.SetFileDropList(list);
        }

        public static void Recycle(IList<string> paths)
        {
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                                                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else if (File.Exists(p))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                                           Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch (Exception e) { Log.Line("recycle failed: " + e.Message); }
            }
        }

        // Windows' own Share sheet (Nearby sharing, Mail, Teams…) for a desktop app.
        [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow(IntPtr appWindow, [In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        static Windows.ApplicationModel.DataTransfer.DataTransferManager manager;
        static List<string> sharing = new List<string>();

        /// <summary>Returns false when this Windows can't show the Share sheet (the caller then copies instead).</summary>
        public static bool Share(IntPtr owner, IList<string> paths)
        {
            try
            {
                var interop = (IDataTransferManagerInterop)System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal
                    .GetActivationFactory(typeof(Windows.ApplicationModel.DataTransfer.DataTransferManager));
                var iid = new Guid("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");
                var ptr = interop.GetForWindow(owner, ref iid);
                manager = (Windows.ApplicationModel.DataTransfer.DataTransferManager)Marshal.GetObjectForIUnknown(ptr);
                Marshal.Release(ptr);
                sharing = paths.ToList();
                manager.DataRequested += OnDataRequested;
                interop.ShowShareUIForWindow(owner);
                return true;
            }
            catch (Exception e)
            {
                Log.Line("share sheet unavailable: " + e.Message);
                return false;
            }
        }

        static async void OnDataRequested(Windows.ApplicationModel.DataTransfer.DataTransferManager sender,
                                          Windows.ApplicationModel.DataTransfer.DataRequestedEventArgs args)
        {
            sender.DataRequested -= OnDataRequested;
            var deferral = args.Request.GetDeferral();
            try
            {
                args.Request.Data.Properties.Title = sharing.Count == 1 ? Path.GetFileName(sharing[0]) : sharing.Count + " files";
                var items = new List<Windows.Storage.IStorageItem>();
                foreach (var p in sharing)
                {
                    if (File.Exists(p)) items.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(p));
                    else if (Directory.Exists(p)) items.Add(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(p));
                }
                args.Request.Data.SetStorageItems(items);
            }
            catch (Exception e) { Log.Line("share failed: " + e.Message); }
            finally { deferral.Complete(); }
        }
    }

    /// <summary>"Start with Windows" and the right-click "Send to → DragShift" entry. Both per-user, no admin.</summary>
    static class Integration
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static string RunCommand(string exe) => "\"" + exe + "\" --background";

        public static bool StartsWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue("DragShift") != null;
            }
            set
            {
                if (value) WriteRun(RunKey, Paths.Exe);
                else using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue("DragShift", false);
            }
        }

        internal static void WriteRun(string runKey, string exe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(runKey)) k.SetValue("DragShift", RunCommand(exe));
        }

        static string SendToLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "DragShift.lnk");

        public static bool InSendTo
        {
            get => File.Exists(SendToLink);
            set
            {
                if (!value) { if (File.Exists(SendToLink)) File.Delete(SendToLink); return; }
                WriteLink(SendToLink, Paths.Exe);
            }
        }

        internal static void WriteLink(string path, string exe)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(exe);
                link.SetDescription("Open in the DragShift ring");
                link.SetIconLocation(exe, 0);
                link.SetWorkingDirectory(Path.GetDirectoryName(exe));
                ((IPersistFile)link).Save(path, true);
            }
            finally { Marshal.ReleaseComObject(link); }
        }

        /// <summary>The .exe a Send to link opens, or "" when the link cannot be read.</summary>
        internal static string LinkTarget(string path)
        {
            IShellLinkW link = null;
            try
            {
                link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(path, 0);
                var target = new StringBuilder(520);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                return target.ToString();
            }
            catch (Exception) { return ""; }
            finally { if (link != null) Marshal.ReleaseComObject(link); }
        }

        static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        /// <summary>Does this "Start with Windows" command start that .exe? Quoted or not, with or without arguments after it.</summary>
        internal static bool RunPointsAt(string command, string exe)
        {
            command = (command ?? "").Trim();
            if (command.Length == 0) return false;
            if (command[0] == '"')
            {
                int end = command.IndexOf('"', 1);
                return SamePath(end > 0 ? command.Substring(1, end - 1) : command.Substring(1), exe);
            }
            if (command.StartsWith(exe, StringComparison.OrdinalIgnoreCase) && (command.Length == exe.Length || command[exe.Length] == ' ')) return true;
            int space = command.IndexOf(' ');
            return SamePath(space > 0 ? command.Substring(0, space) : command, exe);
        }

        /// <summary>Is this .exe somewhere it will still be tomorrow? Not in a temporary folder (such as inside a zip that was
        /// opened), not on a USB drive or CD, and not on a network share.</summary>
        internal static bool LastingPlace(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
                foreach (var temp in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") })
                    if (full.StartsWith(Path.GetFullPath(temp).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                return new DriveInfo(Path.GetPathRoot(full)).DriveType == DriveType.Fixed;
            }
            catch (Exception) { return false; }
        }

        /// <summary>On every normal start: if "Start with Windows" or the Send to entry already exists but points at another copy of
        /// DragShift, point it at this copy. Both then follow wherever DragShift was started last, so a moved or updated .exe keeps
        /// working. It never creates an entry that was not switched on in Settings, and it leaves them alone when this copy runs
        /// from a place that will not last (see LastingPlace).</summary>
        public static void FollowLastStart()
        {
            foreach (var line in FollowLastStart(Paths.Exe, RunKey, SendToLink)) Log.Line(line);
        }

        /// <summary>The same, for the places it is given. Returns what it did (or why it could not), so the caller can log it.</summary>
        internal static List<string> FollowLastStart(string exe, string runKey, string link)
        {
            var said = new List<string>();
            if (!LastingPlace(exe)) return said;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (k != null && k.GetValue("DragShift") is string now && !RunPointsAt(now, exe))
                    {
                        k.SetValue("DragShift", RunCommand(exe));
                        said.Add("start with Windows: now starts this copy");
                    }
                }
            }
            catch (Exception ex) { said.Add("start with Windows: " + ex.Message); }
            try
            {
                if (File.Exists(link) && !SamePath(LinkTarget(link), exe))
                {
                    WriteLink(link, exe);
                    said.Add("send to: now opens this copy");
                }
            }
            catch (Exception ex) { said.Add("send to: " + ex.Message); }
            return said;
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int cmd);
            void SetShowCmd(int cmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}
