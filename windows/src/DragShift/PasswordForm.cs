using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DragShift
{
    /// <summary>
    /// "Unlock": one masked box for the PDF's password. The password is handed to Windows once, in memory, to open the PDF.
    /// It is never saved, logged, or put in a file name or a message. A wrong one says so and the window stays open to try
    /// again. With several PDFs the same password is tried on each.
    /// </summary>
    sealed class PasswordForm : Form
    {
        /// <summary>Raised once, after the window has closed, with the password to try.</summary>
        public event Action<string> Chosen;

        readonly Func<string, Task<string>> check;
        readonly TextBox box;
        readonly Label problem;
        readonly Button go;
        bool busy;

        /// <param name="check">Asked before the window closes: null means go ahead, otherwise the sentence to show (the window then
        /// stays open). Without one the window always closes and the job tells what happened.</param>
        public PasswordForm(IList<string> files, Func<string, Task<string>> check = null)
        {
            this.check = check;
            Ui.Dialog(this, "Unlock", 380, 240);
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };
            flow.Controls.Add(Ui.Text(files.Count == 1 ? Names.Short(Path.GetFileName(files[0])) : files.Count + " PDFs", Ui.Bold, null, 348));
            flow.Controls.Add(Ui.Text("Type the current password", Ui.Small, Ui.Muted));
            box = new TextBox { Width = 348, UseSystemPasswordChar = true, Margin = new Padding(0, 14, 0, 0), AccessibleName = "Password" };
            flow.Controls.Add(box);
            problem = Ui.Text("", Ui.Small, Color.FromArgb(185, 28, 28), 348);
            problem.Margin = new Padding(0, 6, 0, 0);
            flow.Controls.Add(problem);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, MinimumSize = new Size(348, 0), Margin = new Padding(0, 14, 0, 0) };
            go = Ui.Primary("Unlock").DimWhenOff();
            go.Enabled = false;
            var cancel = Ui.Plain("Cancel");
            buttons.Controls.Add(go);
            buttons.Controls.Add(cancel);
            flow.Controls.Add(buttons);
            Controls.Add(flow);
            AcceptButton = go;
            CancelButton = cancel;

            box.TextChanged += (s, e) => { problem.Text = ""; go.Enabled = !busy && box.TextLength > 0; };
            go.Click += (s, e) => Go();
            cancel.Click += (s, e) => Close();
            Shown += (s, e) => box.Focus();
            FormClosed += (s, e) => box.Clear();
        }

        // For the self-test.
        internal TextBox Box => box;
        internal Label Problem => problem;
        internal Button UnlockButton => go;

        /// <summary>Presses Unlock; completes when the window has either closed or said what was wrong.</summary>
        internal Task Press() => Go();

        Task Go()
        {
            if (busy || box.TextLength == 0) return Task.CompletedTask;
            string password = box.Text;
            busy = true;
            go.Enabled = false;
            go.Text = "Checking…";
            problem.Text = "";
            var finished = new TaskCompletionSource<bool>();
            Ask(password, finished);
            return finished.Task;
        }

        /// <summary>Asks the check, then hands the answer back to the window's own thread. It names the window rather than
        /// relying on a synchronization context, so it behaves the same whoever pumps the messages.</summary>
        async void Ask(string password, TaskCompletionSource<bool> finished)
        {
            string said = null;
            try { if (check != null) said = await check(password).ConfigureAwait(false); }
            catch (Exception) { said = null; } // a check that can't run must not stop the job: the job says what happened
            bool handedOver = false;
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(new Action(() => Answered(password, said, finished)));
                    handedOver = true;
                }
            }
            catch (InvalidOperationException) { } // the window closed meanwhile (this includes ObjectDisposedException)
            if (!handedOver) finished.TrySetResult(true);
        }

        void Answered(string password, string said, TaskCompletionSource<bool> finished)
        {
            try
            {
                busy = false;
                if (IsDisposed) return;
                if (said != null)
                {
                    problem.Text = said;
                    go.Text = "Unlock";
                    go.Enabled = box.TextLength > 0;
                    box.SelectAll();
                    box.Focus();
                    return;
                }
                Close();
                Chosen?.Invoke(password);
            }
            finally { finished.TrySetResult(true); }
        }
    }

    static class Names
    {
        /// <summary>A long file name shortened in the middle, so the end (the type) stays visible.</summary>
        public static string Short(string name, int max = 40)
        {
            if (name == null || name.Length <= max) return name;
            int keep = max / 2 - 1;
            return name.Substring(0, keep) + "…" + name.Substring(name.Length - keep);
        }

        /// <summary>A button that looks switched off when it is: the flat coloured one doesn't change by itself.</summary>
        public static Button DimWhenOff(this Button b)
        {
            Color on = b.BackColor, ink = b.ForeColor;
            void Look()
            {
                b.BackColor = b.Enabled ? on : Color.FromArgb(214, 214, 218);
                b.ForeColor = b.Enabled ? ink : Color.FromArgb(120, 120, 125);
                b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
            }
            b.EnabledChanged += (s, e) => Look();
            return b;
        }
    }
}
