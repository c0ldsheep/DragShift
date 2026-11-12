using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DragShift.Core;
using Windows.Storage;
using WinPdf = Windows.Data.Pdf.PdfDocument;

namespace DragShift.Engine
{
    /// <summary>
    /// Merge and Unlock. Windows has no PDF writer, so every page is drawn with Windows' own PDF renderer (the one Edge uses)
    /// and written back as a picture page through PdfWriter's layout, the same way Under KB and Compress do. The new PDF is
    /// written page by page straight to a hidden file that is moved into place when it is complete: a long PDF is never all
    /// in memory, and cancelling leaves nothing behind, not even the hidden file.
    /// </summary>
    static class PdfRewrite
    {
        /// <summary>What the bubble says when pages were redrawn as pictures.</summary>
        public const string PicturesNote = "Pages were saved as pictures, so text can't be selected.";

        /// <summary>One piece of the new PDF: a whole PDF, or a single picture (Doc is null).</summary>
        sealed class Source
        {
            public string Name;
            public WinPdf Doc;
            public int Pages => Doc == null ? 1 : (int)Doc.PageCount;
        }

        // MARK: Merge

        /// <summary>Joins the files, already in Explorer's name order, into "First Merged.pdf" beside the first one (or in the
        /// folder chosen in Settings). PDFs give all their pages, pictures one page each; anything else is left out. A
        /// password-protected PDF stops the whole job before anything is drawn.</summary>
        public static async Task<string> Merge(IList<string> ordered, Options o, Func<string, string> dirFor, IProgress<double> progress,
                                               CancellationToken ct, RunReport report)
        {
            // Every PDF is opened first: a locked one is found before any work, and the page count is known up front.
            var parts = new List<Source>();
            foreach (var path in ordered)
            {
                ct.ThrowIfCancellationRequested();
                var kind = Inspector.Kind(path);
                if (kind == FileKind.Pdf)
                {
                    var doc = await Pdf.Open(path);
                    if (doc.PageCount == 0) throw new UserError(Path.GetFileName(path) + " has no pages.");
                    parts.Add(new Source { Name = path, Doc = doc });
                }
                else if (kind == FileKind.Image) parts.Add(new Source { Name = path });
            }
            if (parts.Count == 0) throw new UserError("Nothing to merge.");
            var first = parts[0].Name;
            var outPath = Naming.Output(first, "pdf", "Merged", dirFor(first));
            await Write(outPath, parts, o, progress, ct);
            if (parts.Any(p => p.Doc != null)) AddNote(report);
            return outPath;
        }

        // MARK: Unlock

        /// <summary>How a PDF answers to a password.</summary>
        internal enum Answer { NoPassword, Opened, Wrong, Unreadable }

        /// <summary>Opens a PDF without a password first: if that works there is nothing to unlock. Only a locked one is tried with
        /// the password. The password goes straight to Windows; it is never kept, logged or put in a message.</summary>
        internal static async Task<(Answer Kind, WinPdf Doc)> Probe(string path, string password)
        {
            StorageFile file;
            try { file = await StorageFile.GetFileFromPathAsync(path); }
            catch (Exception) { return (Answer.Unreadable, null); }
            try
            {
                await WinPdf.LoadFromFileAsync(file);
                return (Answer.NoPassword, null);
            }
            catch (Exception e) when (Pdf.IsLocked(e)) { /* locked, as expected: try the password below */ }
            catch (Exception) { return (Answer.Unreadable, null); }
            try { return (Answer.Opened, await WinPdf.LoadFromFileAsync(file, password ?? "")); }
            catch (Exception e) when (Pdf.IsLocked(e)) { return (Answer.Wrong, null); }
            catch (Exception) { return (Answer.Unreadable, null); }
        }

        static string Said(Answer a, string path)
        {
            var name = Path.GetFileName(path);
            switch (a)
            {
                case Answer.NoPassword: return name + " has no password.";
                case Answer.Wrong: return "Wrong password for " + name + ".";
                default: return name + " isn't a readable PDF.";
            }
        }

        /// <summary>What the password window asks before it closes: null when the job may go ahead (at least one PDF opens with
        /// this password, or there is nothing a different password could change), else the sentence to show so it can be retried.</summary>
        public static async Task<string> CheckPassword(IList<string> files, string password)
        {
            int opened = 0, wrong = 0;
            string wrongName = null;
            foreach (var f in files)
            {
                var (kind, _) = await Probe(f, password);
                if (kind == Answer.Opened) opened++;
                else if (kind == Answer.Wrong) { wrong++; wrongName = wrongName ?? Path.GetFileName(f); }
            }
            if (opened > 0 || wrong == 0) return null;
            return files.Count == 1 ? "Wrong password for " + wrongName + "." : "That password didn't open any of these PDFs.";
        }

        /// <summary>Saves "Name Unlocked.pdf", a copy that opens without a password. A PDF with no password, or a wrong
        /// password, says so and makes nothing.</summary>
        public static async Task<string> Unlock(string path, string password, Options o, Func<string, string> dirFor, IProgress<double> progress,
                                                CancellationToken ct, RunReport report)
        {
            var (kind, doc) = await Probe(path, password);
            if (kind != Answer.Opened) throw new UserError(Said(kind, path));
            if (doc.PageCount == 0) throw new UserError(Path.GetFileName(path) + " has no pages.");
            var outPath = Naming.Output(path, "pdf", "Unlocked", dirFor(path));
            await Write(outPath, new List<Source> { new Source { Name = path, Doc = doc } }, o, progress, ct);
            AddNote(report);
            return outPath;
        }

        static void AddNote(RunReport report)
        {
            if (!report.Notes.Contains(PicturesNote)) report.Notes.Add(PicturesNote);
        }

        // MARK: Writing

        /// <summary>Draws every page of every part, one at a time, into the new PDF. Each PDF page keeps its size in points; a
        /// picture gets the page it would get in "Images → PDF". Pages are JPEG at the quality in Settings.</summary>
        static async Task Write(string outPath, IList<Source> parts, Options o, IProgress<double> progress, CancellationToken ct)
        {
            int total = parts.Sum(p => p.Pages);
            await Naming.AtomicallyAsync(outPath, async tmp =>
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    var pdf = new PdfStream(fs, total);
                    int done = 0;
                    foreach (var part in parts)
                    {
                        if (part.Doc == null)
                        {
                            ct.ThrowIfCancellationRequested();
                            using (var img = await Images.Load(part.Name)) pdf.Add(await Pdf.PageFor(part.Name, img, o.Quality));
                            progress?.Report(++done / (double)total);
                            continue;
                        }
                        for (uint i = 0; i < part.Doc.PageCount; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                double pw = 0, ph = 0;
                                using (var img = await Pdf.Render(part.Doc, i, o.PdfDpi, (w, h) => { pw = w; ph = h; }))
                                    pdf.Add(new PdfWriter.Page
                                    {
                                        Jpeg = Images.Jpeg(img, o.Quality), PixelWidth = img.Width, PixelHeight = img.Height, Width = pw, Height = ph,
                                    });
                            }
                            catch (Exception e) when (!(e is OperationCanceledException))
                            {
                                throw new UserError(Path.GetFileName(part.Name) + " (page " + (i + 1) + "): " + Runner.Message(e));
                            }
                            progress?.Report(++done / (double)total);
                        }
                    }
                    pdf.Finish();
                }
            });
        }
    }
}
