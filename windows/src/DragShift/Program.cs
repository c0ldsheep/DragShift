using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DragShift
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);

        [STAThread]
        static int Main(string[] args)
        {
            if (args.FirstOrDefault() == "--selftest")
            {
                AttachConsole(-1); // print to the terminal it was started from
                return SelfTest.Run(args.Skip(1).ToArray());
            }

            // One DragShift at a time. A second start (Send to, or opening the .exe with files) hands its
            // files to the running one and quits.
            using (var mutex = new Mutex(true, @"Local\DragShift.c0ldsheep", out bool first))
            {
                if (!first)
                {
                    var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
                    if (files.Count > 0) Receiver.SendToRunning(files);
                    else if (!args.Contains("--background")) Receiver.SendOpenSettings(); // opening DragShift again shows its Settings
                    Integration.FollowLastStart(); // Start with Windows and Send to follow the copy that was started last, even when it only hands over and quits
                    return 0;
                }
                Integration.FollowLastStart();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Line("error: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Line("crash: " + e.ExceptionObject);
                Application.Run(new AppMain(args));
                // Quit has already saved everything, taken the tray icon away and stopped the mouse and keyboard hooks.
                // End now instead of unwinding: on a PC with AMD graphics driver 32.0.21045.5002 the driver (amdxx64.dll)
                // crashed while the process shut down after picture, PDF or video work, which made Windows write an error
                // report and kept the process alive for about five seconds.
                Native.ExitNow(0);
            }
            return 0;
        }
    }
}
