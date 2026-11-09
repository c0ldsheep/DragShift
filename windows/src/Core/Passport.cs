using System;
using System.Globalization;

namespace DragShift.Core
{
    public enum PhotoBackground { White, LightBlue, Keep }

    /// <summary>What a passport-size or form photo must look like. Default: 3.5 × 4.5 cm on white, under
    /// 50 KB (Indian passport size, and what SSC, IBPS and most exam and job portals ask for).</summary>
    public sealed class PhotoSpec
    {
        public double WidthMM = 35, HeightMM = 45;
        /// <summary>0 = no size limit.</summary>
        public int MaxKB = 50;
        public PhotoBackground Background = PhotoBackground.White;
        public double Dpi = 300;
        /// <summary>Head (crown to chin) as a share of the photo's height: passports ask 70–80%, US visas 50–69%.</summary>
        public double Head = 0.72;

        public int PixelWidth => (int)Math.Round(WidthMM / 25.4 * Dpi);
        public int PixelHeight => (int)Math.Round(HeightMM / 25.4 * Dpi);

        public string SizeTitle
        {
            get
            {
                if (Math.Abs(WidthMM - 50.8) < 0.1 && Math.Abs(HeightMM - 50.8) < 0.1) return "2 × 2 in";
                string Cm(double v) => (v / 10).ToString(v % 10 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
                return Cm(WidthMM) + " × " + Cm(HeightMM) + " cm";
            }
        }

        public static readonly (string Name, PhotoSpec Spec)[] Presets =
        {
            ("Passport size · 3.5 × 4.5 cm", new PhotoSpec()),
            ("US visa · 2 × 2 in", new PhotoSpec { WidthMM = 50.8, HeightMM = 50.8, MaxKB = 240, Head = 0.6 }),
            ("Square · 3.5 × 3.5 cm", new PhotoSpec { WidthMM = 35, HeightMM = 35, MaxKB = 50, Head = 0.62 }),
        };

        public PhotoSpec Copy() => (PhotoSpec)MemberwiseClone();

        /// <summary>"35|45|50|White|0.72" for the settings file.</summary>
        public override string ToString() =>
            string.Join("|", WidthMM.ToString(CultureInfo.InvariantCulture), HeightMM.ToString(CultureInfo.InvariantCulture),
                        MaxKB, Background, Head.ToString(CultureInfo.InvariantCulture));

        public static PhotoSpec Parse(string s)
        {
            var spec = new PhotoSpec();
            var p = (s ?? "").Split('|');
            if (p.Length < 5) return spec;
            double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out spec.WidthMM);
            double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out spec.HeightMM);
            int.TryParse(p[2], out spec.MaxKB);
            Enum.TryParse(p[3], out spec.Background);
            double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out spec.Head);
            if (spec.WidthMM < 10 || spec.HeightMM < 10 || spec.Head < 0.3 || spec.Head > 0.9) return new PhotoSpec();
            return spec;
        }
    }

    /// <summary>A rectangle in pixels, top-left origin (kept free of System.Drawing so it tests anywhere).</summary>
    public struct Box
    {
        public double X, Y, Width, Height;
        public Box(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public double MidX => X + Width / 2;
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#} {2:0.#}×{3:0.#}", X, Y, Width, Height);
    }

    public static class Passport
    {
        /// <summary>Face boxes run from the brows to the chin; the top of the hair sits about this far above
        /// (measured 0.51–0.54 on real portraits on the Mac; a little extra keeps hair off the edge).</summary>
        public const double CrownAbove = 0.55;

        public static double TopShare(PhotoSpec s) => s.Head >= 0.7 ? 0.08 : 0.1;

        /// <summary>The part of the picture that becomes the photo. It can reach past the picture's
        /// edges; that part is filled with the background colour.</summary>
        public static Box Frame(Box face, PhotoSpec spec)
        {
            double crown = face.Y - face.Height * CrownAbove;
            double head = face.Bottom - crown;
            double h = head / spec.Head;
            double w = h * spec.WidthMM / spec.HeightMM;
            return new Box(face.MidX - w / 2, crown - h * TopShare(spec), w, h);
        }
    }
}
