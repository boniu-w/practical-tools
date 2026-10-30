using System.Globalization;
using System.Text;

namespace PracticalTools.TestFixtures;

internal static class PdfFixture
{
    public static byte[] Create(int pages = 2, int width = 200, int height = 100)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages} /Kids [" + string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{3 + 2 * i} 0 R")) + "] >>"
        };
        for (int i = 0; i < pages; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << >> /Contents {4 + 2 * i} 0 R >>");
            var content = (i % 2 == 0 ? "1 0 0" : "0 0 1") + $" rg 0 0 {width} {height} re f\n";
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}endstream");
        }
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
