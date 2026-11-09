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
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Line("error: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Line("crash: " + e.ExceptionObject);
                Application.Run(new AppMain(args));
            }
            return 0;
        }
    }
}
