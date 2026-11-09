using System;
using System.Collections.Generic;

namespace DragShift.Core
{
    /// <summary>Ring sizes in device-independent pixels (multiply by the screen's scale), shared by the
    /// drawing and the hit-testing so they always agree. Same numbers as the Mac ring.</summary>
    public static class Geometry
    {
        public const double Size = 440;
        public const double CenterX = 220, CenterY = 200;
        public const double Outer = 148, Inner = 64, Hub = 56;
        public const double Disc = Outer + 10;
        public const double TabsY = CenterY + Disc + 26;
        public const double TabWidth = 70, TabHeight = 26, TabGap = 6;
        public const double HintY = TabsY + 30;

        public static List<Box> TabRects(int n)
        {
            double total = n * TabWidth + Math.Max(0, n - 1) * TabGap;
            double x0 = CenterX - total / 2;
            var list = new List<Box>();
            for (int i = 0; i < n; i++) list.Add(new Box(x0 + i * (TabWidth + TabGap), TabsY - TabHeight / 2, TabWidth, TabHeight));
            return list;
        }

        public enum HitKind { None, Hub, Slice, Tab }

        public struct Hit
        {
            public HitKind Kind;
            public int Index;
            public static Hit Of(HitKind k, int i = 0) => new Hit { Kind = k, Index = i };
            public override string ToString() => Kind + (Kind == HitKind.Slice || Kind == HitKind.Tab ? " " + Index : "");
        }

        /// <summary>Point in ring coordinates (top-left origin). While dragging, every point outside the hub
        /// picks a slice by direction, so you only have to push the file the right way, not land on it.</summary>
        public static Hit HitTest(double x, double y, int slices, int tabs, bool byDirection)
        {
            if (tabs > 1)
            {
                var rects = TabRects(tabs);
                for (int i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    if (x >= r.X - 3 && x <= r.Right + 3 && y >= r.Y - 6 && y <= r.Bottom + 6) return Hit.Of(HitKind.Tab, i);
                }
            }
            double dx = x - CenterX, dy = y - CenterY;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < Hub) return Hit.Of(HitKind.Hub);
            if (slices <= 0) return Hit.Of(HitKind.None);
            if (!byDirection && d > Outer + 6) return Hit.Of(HitKind.None);
            double span = 360.0 / slices;
            double a = Math.Atan2(dy, dx) * 180 / Math.PI + 90; // 0 = straight up, clockwise
            a = (a + 360 + span / 2) % 360;
            return Hit.Of(HitKind.Slice, Math.Min(slices - 1, (int)(a / span)));
        }

        /// <summary>Middle of slice i's label, in ring coordinates.</summary>
        public static (double X, double Y) LabelCenter(int i, int n)
        {
            double span = 360.0 / n;
            double rad = (i * span - 90) * Math.PI / 180;
            double r = (Inner + Outer) / 2 + 2;
            return (CenterX + Math.Cos(rad) * r, CenterY + Math.Sin(rad) * r);
        }
    }

    /// <summary>Sizes for stacking pictures top to bottom at one shared width (the widest one's), shrunk together
    /// if the stack would be too big for a JPG (65,000 on a side) or for memory. Same rule as the Mac app.</summary>
    public static class Stitch
    {
        public const int JpegMaxSide = 65000;
        public const long MaxPixels = 60000000;

        public static (int Width, int[] Heights) Plan(IList<(int W, int H)> sizes)
        {
            double widest = 0;
            foreach (var s in sizes) widest = Math.Max(widest, s.W);
            if (widest <= 0) return (0, new int[0]);
            var tall = new double[sizes.Count];
            double total = 0;
            for (int i = 0; i < sizes.Count; i++) { tall[i] = sizes[i].W > 0 ? sizes[i].H * widest / sizes[i].W : 0; total += tall[i]; }
            double shrink = Math.Min(Math.Min(1, JpegMaxSide / Math.Max(total, 1)),
                                     Math.Min(JpegMaxSide / widest, Math.Sqrt(MaxPixels / Math.Max(widest * total, 1))));
            var heights = new int[sizes.Count];
            for (int i = 0; i < sizes.Count; i++) heights[i] = Math.Max(1, (int)(shrink < 1 ? Math.Floor(tall[i] * shrink) : Math.Round(tall[i])));
            return (Math.Max(1, (int)Math.Floor(widest * shrink)), heights);
        }
    }
}
