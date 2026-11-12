using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DragShift
{
    /// <summary>Draws the tip cup (the numbers are in <see cref="CupArt"/>) at any moment from "at rest" (0) to "pulled as far as
    /// it goes" (1). Same order, same easing and same colours as the Mac app's cup.</summary>
    static class CupPainter
    {
        public static readonly Color Yellow = Color.FromArgb(254, 212, 32);        // #FED420, the cup
        public static readonly Color ShadeYellow = Color.FromArgb(242, 188, 20);   // #F2BC14, its shade and the inside of its mouth
        public static readonly Color RimYellow = Color.FromArgb(254, 221, 82);     // #FEDD52, its rim
        public static readonly Color Coffee = Color.FromArgb(89, 45, 20);          // #592D14
        public static readonly Color CoffeeShine = Color.FromArgb(132, 73, 42);    // #84492A, the streaks on the coffee

        /// <summary>The drawing needs this much room around the cup's centre, in widths of the cup: the wave and the drops fly past it.</summary>
        public const float RoomAcross = 1.42f, RoomDown = 1.24f;

        /// <summary>Slow at both ends, quick in the middle; clamped to 0..1.</summary>
        public static float Ease(float x)
        {
            float c = Math.Min(1, Math.Max(0, x));
            return c * c * (3 - 2 * c);
        }

        static PointF[] Points(float[] v, float cx = 0, float cy = 0, float scale = 1)
        {
            var p = new PointF[v.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new PointF(cx + v[2 * i] * scale, cy + v[2 * i + 1] * scale);
            return p;
        }

        // The cup itself never changes: its outlines are turned into points once.
        static readonly PointF[] OutlinePoints = Points(CupArt.Outline), HolePoints = Points(CupArt.Hole), ShadePoints = Points(CupArt.Shade),
                                 RimPoints = Points(CupArt.Rim), OpeningPoints = Points(CupArt.Opening);

        /// <summary>For each drop, the point of the coffee's outline it leaves from: the one nearest its start in the full spill.</summary>
        static readonly int[] DropRoots = FindRoots();

        static int[] FindRoots()
        {
            var roots = new int[CupArt.Drops.Length];
            for (int d = 0; d < roots.Length; d++)
            {
                double best = double.MaxValue;
                for (int i = 0; i < CupArt.Spill.Length / 2; i++)
                {
                    double dist = Math.Sqrt(Math.Pow(CupArt.Spill[2 * i] - CupArt.Drops[d].From.X, 2) + Math.Pow(CupArt.Spill[2 * i + 1] - CupArt.Drops[d].From.Y, 2));
                    if (dist < best) { best = dist; roots[d] = i; }
                }
            }
            return roots;
        }

        static void Fill(Graphics g, PointF[] points, Color color)
        {
            using (var brush = new SolidBrush(color)) g.FillPolygon(brush, points, FillMode.Winding);
        }

        /// <summary>Draws the cup with the point it turns around at <paramref name="at"/>. <paramref name="width"/> is the cup's width
        /// from its left side to the outside of its handle. <paramref name="spill"/>: 0 = at rest, 1 = the full spill; anything outside
        /// that is the same as the nearest end. Leaves the graphics' own settings (transform, clip, smoothing) as it found them.</summary>
        public static void Draw(Graphics g, PointF at, float width, float spill)
        {
            float c = Math.Min(1, Math.Max(0, spill));
            float tip = 1 - (1 - c) * (1 - c);        // the cup answers the hand at once, then settles into its full tilt...
            float pour = Ease((c - 0.1f) / 0.9f);     // ...and the coffee follows a moment behind it
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(at.X, at.Y);
                g.ScaleTransform(width / 1000f, width / 1000f);
                g.RotateTransform(CupArt.Tilt * tip);

                // The cup, with the gap in its handle cut out (even-odd), then its shade, its rim and the inside of its mouth.
                using (var cup = new GraphicsPath(FillMode.Alternate))
                using (var yellow = new SolidBrush(Yellow))
                {
                    cup.AddPolygon(OutlinePoints);
                    cup.AddPolygon(HolePoints);
                    g.FillPath(yellow, cup);
                }
                Fill(g, ShadePoints, ShadeYellow);
                Fill(g, RimPoints, RimYellow);
                Fill(g, OpeningPoints, ShadeYellow);

                // The coffee: every point of its outline is part-way between "at rest" and "full spill".
                var now = new float[CupArt.Calm.Length];
                for (int i = 0; i < now.Length; i++) now[i] = CupArt.Calm[i] + (CupArt.Spill[i] - CupArt.Calm[i]) * pour;
                var liquid = Points(now);
                Fill(g, liquid, Coffee);

                // Drops leave the wave one after another, the farthest-flying first, and grow as they go.
                int n = CupArt.Drops.Length;
                for (int i = 0; i < n; i++)
                {
                    var drop = CupArt.Drops[i];
                    float wait = 0.25f + 0.5f * i / Math.Max(1, n - 1);
                    float k = Ease((pour - wait) / (1 - wait));
                    if (k <= 0.02f) continue;
                    float rx = now[2 * DropRoots[i]], ry = now[2 * DropRoots[i] + 1];
                    Fill(g, Points(drop.Shape, rx + (drop.To.X - rx) * k, ry + (drop.To.Y - ry) * k, k), Coffee);
                }

                // The lighter streaks come in at the end, and only where there is coffee to shine.
                float glint = Ease((pour - 0.7f) / 0.3f);
                if (glint > 0.02f)
                {
                    using (var clip = new GraphicsPath(FillMode.Winding))
                    {
                        clip.AddPolygon(liquid);
                        g.SetClip(clip, CombineMode.Intersect);
                        foreach (var streak in CupArt.Shine) Fill(g, Points(streak), Color.FromArgb((int)Math.Round(255 * glint), CoffeeShine));
                    }
                }
            }
            finally { g.Restore(state); }
        }
    }
}
