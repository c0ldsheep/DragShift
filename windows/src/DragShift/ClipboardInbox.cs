using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DragShift.Core;

namespace DragShift
{
    /// <summary>Owns clipboard imports only. Inputs selected from elsewhere are never registered or removed.
    /// Leases keep an import alive through an editor or asynchronous job; cleanup runs after the UI hand-off.</summary>
    sealed class ClipboardInbox : IDisposable
    {
        readonly string root;
        readonly Action changed;
        readonly Dictionary<string, int> owned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool disposed;
        public int StaleFailures { get; private set; }
        public int Remaining => owned.Count;

        public ClipboardInbox(string folder, Action changed)
        {
            root = Path.GetFullPath(folder);
            this.changed = changed;
            // This private folder was also used by older versions. Only their exact import names are ours.
            try
            {
                if (!Directory.Exists(root) || IsLink(root)) return;
                foreach (var path in Directory.GetFiles(root))
                    if (IsImport(path) && !IsLink(path) && !TryDelete(path)) StaleFailures++;
            }
            catch (IOException) { StaleFailures++; }
            catch (UnauthorizedAccessException) { StaleFailures++; }
        }

        static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        static bool IsImport(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) return false;
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, "Clipboard Image", StringComparison.OrdinalIgnoreCase)) return true;
            const string prefix = "Clipboard Image ";
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.Substring(prefix.Length), out var n) && n >= 2;
        }

        static bool TryDelete(string path)
        {
            try { File.Delete(path); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public string Save(Action<string> write)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ClipboardInbox));
            Directory.CreateDirectory(root);
            if (IsLink(root)) throw new IOException("The clipboard folder isn't available.");
            var path = Naming.Unique(Path.Combine(root, "Clipboard Image.png"));
            Naming.Atomically(path, write);
            owned.Add(path, 0);
            changed?.Invoke();
            return path;
        }

        public IDisposable Hold(IEnumerable<string> paths)
        {
            var held = paths.Where(owned.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var path in held) owned[path]++;
            return new Lease(() =>
            {
                foreach (var path in held)
                    if (owned.TryGetValue(path, out var count)) owned[path] = Math.Max(0, count - 1);
                if (!disposed) changed?.Invoke();
            });
        }

        public int CleanupUnheld() => Cleanup(false);

        int Cleanup(bool all)
        {
            try { if (Directory.Exists(root) && IsLink(root)) return owned.Count; }
            catch (IOException) { return owned.Count; }
            catch (UnauthorizedAccessException) { return owned.Count; }
            int failed = 0;
            foreach (var path in owned.Where(p => all || p.Value == 0).Select(p => p.Key).ToList())
            {
                if (TryDelete(path)) owned.Remove(path);
                else failed++;
            }
            return failed;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Cleanup(true); // Windows may still hold a file; the next start retries it.
        }

        sealed class Lease : IDisposable
        {
            Action release;
            public Lease(Action release) { this.release = release; }
            public void Dispose() { var action = release; release = null; action?.Invoke(); }
        }
    }
}
