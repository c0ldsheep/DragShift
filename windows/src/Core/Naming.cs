using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DragShift.Core
{
    /// <summary>New files are named the way Explorer would: "Photo.jpg", "Photo Compressed.jpg",
    /// "Photo 2.jpg". Originals are never touched.</summary>
    public static class Naming
    {
        public static string Base(string path) => Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));

        /// <summary>Where results go: next to the original unless the user picked a folder.</summary>
        public static string FolderFor(string input, string overrideDir)
        {
            if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;
            var dir = Path.GetDirectoryName(input.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
        }

        /// <summary>"Photo Suffix.ext" in dir, made unique.</summary>
        public static string Output(string input, string ext, string suffix, string dir)
        {
            var name = Base(input) + (string.IsNullOrEmpty(suffix) ? "" : " " + suffix);
            return Unique(Path.Combine(dir, name + "." + ext));
        }

        /// <summary>Adds " 2", " 3"… before the extension until the name is free.</summary>
        public static string Unique(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path) ?? "";
            var ext = Path.GetExtension(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            for (int i = 2; ; i++)
            {
                var candidate = Path.Combine(dir, stem + " " + i + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
        }

        /// <summary>Creates a new file through a temporary file in the same folder. An existing destination is never overwritten.</summary>
        public static void Atomically(string dest, Action<string> write)
        {
            WriteAtomically(dest, write, false);
        }

        /// <summary>Replaces an owned file atomically, or creates it if it does not exist. Used for settings only.</summary>
        public static void ReplaceAtomically(string dest, Action<string> write)
        {
            WriteAtomically(dest, write, true);
        }

        /// <summary>The same as <see cref="Atomically"/>, for work that has to wait (drawing PDF pages one by one). If the work
        /// fails or is cancelled, nothing is left behind: not the new file, and not the hidden temporary one either.</summary>
        public static async Task AtomicallyAsync(string dest, Func<string, Task> write)
        {
            var dir = Path.GetDirectoryName(dest) ?? ".";
            Directory.CreateDirectory(dir);
            var tmp = TempFor(dir, dest);
            try
            {
                await write(tmp).ConfigureAwait(false);
                File.Move(tmp, dest);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        /// <summary>The hidden name a file is built under before it is moved into place.</summary>
        static string TempFor(string dir, string dest) =>
            Path.Combine(dir, "." + Path.GetFileName(dest) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");

        static void WriteAtomically(string dest, Action<string> write, bool replace)
        {
            var dir = Path.GetDirectoryName(dest) ?? ".";
            Directory.CreateDirectory(dir);
            var tmp = TempFor(dir, dest);
            try
            {
                write(tmp);
                if (replace && File.Exists(dest)) File.Replace(tmp, dest, null);
                else File.Move(tmp, dest);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        /// <summary>Explorer's order: "Page 2" before "Page 10".</summary>
        public static readonly IComparer<string> NaturalOrder = new Natural();

        sealed class Natural : IComparer<string>
        {
            public int Compare(string a, string b)
            {
                if (a == null || b == null) return string.Compare(a, b, StringComparison.Ordinal);
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        int si = i, sj = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        var na = a.Substring(si, i - si).TrimStart('0');
                        var nb = b.Substring(sj, j - sj).TrimStart('0');
                        if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                        int c = string.CompareOrdinal(na, nb);
                        if (c != 0) return c;
                    }
                    else
                    {
                        int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                        if (c != 0) return c;
                        i++; j++;
                    }
                }
                return (a.Length - i).CompareTo(b.Length - j);
            }
        }
    }
}
