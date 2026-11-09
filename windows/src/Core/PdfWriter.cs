using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DragShift.Core
{
    /// <summary>The smallest possible PDF for pages that are pictures: one JPEG per page, stored as is.</summary>
    public static class PdfWriter
    {
        public sealed class Page
        {
            public byte[] Jpeg;
            public int PixelWidth, PixelHeight;
            public bool Gray;
            /// <summary>Page size in points (1/72 inch).</summary>
            public double Width, Height;
        }

        public static byte[] Make(IList<Page> pages)
        {
            var ms = new MemoryStream();
            var offsets = new List<long>();
            void Put(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
            string N(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            void Obj(System.Action body)
            {
                offsets.Add(ms.Position);
                Put(offsets.Count + " 0 obj\n");
                body();
                Put("\nendobj\n");
            }

            Put("%PDF-1.4\n%");
            ms.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 }, 0, 4); // marks the file as binary
            Put("\n");
            // 1 catalog, 2 page tree, then per page: page, contents, image.
            var kids = new StringBuilder();
            for (int i = 0; i < pages.Count; i++) kids.Append(i == 0 ? "" : " ").Append(3 + i * 3).Append(" 0 R");
            Obj(() => Put("<< /Type /Catalog /Pages 2 0 R >>"));
            Obj(() => Put("<< /Type /Pages /Kids [" + kids + "] /Count " + pages.Count + " >>"));
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                int b = 3 + i * 3;
                var draw = "q " + N(p.Width) + " 0 0 " + N(p.Height) + " 0 0 cm /Im0 Do Q";
                Obj(() =>
                {
                    Put("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(p.Width) + " " + N(p.Height) + "] ");
                    Put("/Resources << /XObject << /Im0 " + (b + 2) + " 0 R >> >> /Contents " + (b + 1) + " 0 R >>");
                });
                Obj(() => Put("<< /Length " + draw.Length + " >>\nstream\n" + draw + "\nendstream"));
                Obj(() =>
                {
                    Put("<< /Type /XObject /Subtype /Image /Width " + p.PixelWidth + " /Height " + p.PixelHeight + " ");
                    Put("/ColorSpace /" + (p.Gray ? "DeviceGray" : "DeviceRGB") + " /BitsPerComponent 8 /Filter /DCTDecode ");
                    Put("/Length " + p.Jpeg.Length + " >>\nstream\n");
                    ms.Write(p.Jpeg, 0, p.Jpeg.Length);
                    Put("\nendstream");
                });
            }
            long xref = ms.Position;
            Put("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (var o in offsets) Put(o.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            Put("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            return ms.ToArray();
        }
    }
}
