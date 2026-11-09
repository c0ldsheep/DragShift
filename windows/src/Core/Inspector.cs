using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragShift.Core
{
    /// <summary>Picture formats the ring can save to.</summary>
    public enum ImageFormat { Jpg, Png, WebP, Heic, Tiff, Bmp, Gif }

    public static class Formats
    {
        public static string Ext(this ImageFormat f)
        {
            switch (f)
            {
                case ImageFormat.Jpg: return "jpg";
                case ImageFormat.Png: return "png";
                case ImageFormat.WebP: return "webp";
                case ImageFormat.Heic: return "heic";
                case ImageFormat.Tiff: return "tiff";
                case ImageFormat.Bmp: return "bmp";
                default: return "gif";
            }
        }

        public static string Title(this ImageFormat f) => f == ImageFormat.WebP ? "WebP" : f.Ext().ToUpperInvariant();

        public static bool SupportsAlpha(this ImageFormat f) =>
            f == ImageFormat.Png || f == ImageFormat.WebP || f == ImageFormat.Tiff || f == ImageFormat.Heic || f == ImageFormat.Gif;

        public static ImageFormat? FromExt(string ext)
        {
            switch ((ext ?? "").TrimStart('.').ToLowerInvariant())
            {
                case "jpg": case "jpeg": case "jfif": return ImageFormat.Jpg;
                case "png": return ImageFormat.Png;
                case "webp": return ImageFormat.WebP;
                case "heic": case "heif": return ImageFormat.Heic;
                case "tif": case "tiff": return ImageFormat.Tiff;
                case "bmp": return ImageFormat.Bmp;
                case "gif": return ImageFormat.Gif;
                default: return null;
            }
        }
    }

    /// <summary>What kind of file this is, from its name (fast: nothing is opened).</summary>
    public static class Inspector
    {
        static readonly HashSet<string> Images = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "jpg", "jpeg", "jfif", "png", "webp", "heic", "heif", "avif", "tif", "tiff", "bmp", "gif", "ico",
            "jxr", "wdp", "dng", "cr2", "cr3", "nef", "arw", "orf", "rw2", "raf",
        };
        static readonly HashSet<string> Videos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mp4", "m4v", "mov", "avi", "wmv", "mkv", "webm", "3gp", "mts", "m2ts",
        };
        static readonly HashSet<string> Audios = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mp3", "m4a", "aac", "wav", "wma", "flac", "ogg", "opus", "aiff", "aif",
        };
        static readonly HashSet<string> Docs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "doc", "docx", "rtf", "odt", "txt", "md", "html", "htm",
        };

        public static FileKind Kind(string path)
        {
            if (Directory.Exists(path)) return FileKind.Folder;
            var ext = Path.GetExtension(path).TrimStart('.');
            if (Images.Contains(ext)) return FileKind.Image;
            if (ext.Equals("pdf", StringComparison.OrdinalIgnoreCase)) return FileKind.Pdf;
            if (Videos.Contains(ext)) return FileKind.Video;
            if (Audios.Contains(ext)) return FileKind.Audio;
            if (Docs.Contains(ext)) return FileKind.Document;
            if (ext.Equals("zip", StringComparison.OrdinalIgnoreCase)) return FileKind.Archive;
            return FileKind.Other;
        }

        /// <summary>One kind for a whole drop: mixed drops count as "other".</summary>
        public static FileKind Kind(IList<string> paths)
        {
            if (paths.Count == 0) return FileKind.Other;
            var first = Kind(paths[0]);
            return paths.All(p => Kind(p) == first) ? first : FileKind.Other;
        }

        public static string Label(FileKind k)
        {
            switch (k)
            {
                case FileKind.Pdf: return "PDF";
                case FileKind.Other: return "File";
                default: return k.ToString();
            }
        }

        public static long Size(string path)
        {
            try
            {
                if (File.Exists(path)) return new FileInfo(path).Length;
                if (Directory.Exists(path))
                    return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return 0;
        }

        /// <summary>Images directly inside a folder, in Explorer's order.</summary>
        public static List<string> ImagesIn(string folder)
        {
            try
            {
                return Directory.EnumerateFiles(folder)
                    .Where(p => Kind(p) == FileKind.Image && !Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal))
                    .OrderBy(p => Path.GetFileName(p), Naming.NaturalOrder).ToList();
            }
            catch (IOException) { return new List<string>(); }
            catch (UnauthorizedAccessException) { return new List<string>(); }
        }
    }
}
