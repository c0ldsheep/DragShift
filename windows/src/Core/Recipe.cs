using System;
using System.Collections.Generic;
using System.Linq;

namespace DragShift.Core
{
    /// <summary>A one-drop chain of steps, e.g. "WhatsApp Small" = resize to 1600 px → compress.
    /// Each step's result feeds the next; only the final file is kept, named after the recipe.</summary>
    public sealed class Recipe
    {
        public string Id { get; }
        public string Name { get; }
        public string Glyph { get; }
        public IList<string> Steps { get; }

        public Recipe(string id, string name, string glyph, params string[] steps)
        {
            Id = id; Name = name; Glyph = glyph; Steps = steps;
        }

        /// <summary>What the first step works on decides which files the recipe shows up for.</summary>
        public ISet<FileKind> Kinds
        {
            get
            {
                var first = Steps.FirstOrDefault();
                if (first == null) return new HashSet<FileKind>();
                if (SizeLimit.KbFromStep(first) != null) return new HashSet<FileKind> { FileKind.Image, FileKind.Pdf };
                switch (first.Split('.')[0])
                {
                    case "img": return new HashSet<FileKind> { FileKind.Image };
                    case "pdf": return new HashSet<FileKind> { FileKind.Pdf };
                    case "media": return new HashSet<FileKind> { FileKind.Video };
                    default: return new HashSet<FileKind>((FileKind[])Enum.GetValues(typeof(FileKind)));
                }
            }
        }

        public Slice Slice => new Slice(Id, Name, Glyph, "Running " + Name);

        /// <summary>Steps that turn many files into one (so the chain runs once for the whole group).</summary>
        public static readonly ISet<string> ManyToOne = new HashSet<string> { "img.to.pdf", "img.stitch", "file.zip" };

        public static readonly IList<Recipe> BuiltIns = new[]
        {
            new Recipe("recipe.insta-post", "Insta Post", Catalog.Glyph.Camera, "img.fill.1080x1350", "img.to.jpg"),
            new Recipe("recipe.insta-story", "Insta Story", Catalog.Glyph.Page, "img.fit.1080x1920"),
            new Recipe("recipe.whatsapp", "WhatsApp Small", Catalog.Glyph.Share, "img.size.1600", "img.compress"),
            new Recipe("recipe.web", "Web Image", Catalog.Glyph.Globe, "img.size.2048", "img.compress"),
            new Recipe("recipe.watermark", "Watermark", Catalog.Glyph.Tag, "img.watermark", "img.to.jpg"),
            new Recipe("recipe.email-pdf", "Email PDF", Catalog.Glyph.Pdf, "img.to.pdf", "pdf.compress"),
        };

        /// <summary>Readable names for steps (Settings and error messages).</summary>
        public static string StepTitle(string id)
        {
            if (SizeLimit.KbFromStep(id) is int kb) return "Under " + SizeLimit.Title(kb);
            var p = id.Split('.');
            if (p.Length == 3 && p[0] == "img" && p[1] == "size") return "Resize to " + p[2] + " px";
            if (p.Length == 3 && p[0] == "img" && p[1] == "pct") return "Resize to " + p[2] + "%";
            if (p.Length == 3 && p[0] == "img" && p[1] == "fill") return "Crop to " + p[2].Replace("x", "×");
            if (p.Length == 3 && p[0] == "img" && p[1] == "fit") return "Fit in " + p[2].Replace("x", "×");
            if (p.Length == 3 && p[1] == "to") return "Save as " + p[2].ToUpperInvariant();
            switch (id)
            {
                case "img.compress": case "pdf.compress": return "Compress";
                case "img.watermark": return "Watermark";
                case "img.passport": return "Passport photo";
                case "img.rotate": return "Rotate right";
                case "img.flip": return "Flip";
                case "file.zip": return "Zip";
                default: return id;
            }
        }
    }
}
