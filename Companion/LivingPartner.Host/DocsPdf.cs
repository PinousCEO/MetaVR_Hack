using System.Text;
using System.Text.RegularExpressions;

namespace LivingPartner.Host;

/// <summary>
/// A PDF whose pages are pictures: one JPEG per A4 page, embedded as it is (DCTDecode) - no external program, and it
/// opens in any reader. The pages are drawn by DocsPages; the text is in the pictures (so every language the PC has a
/// font for can be written), which is why the title is also put into the file's information.
/// </summary>
public static class DocsPdf
{
    public const double A4W = 595.28, A4H = 841.89;     // points

    /// <summary>The size of a JPEG out of its header (0, 0 when it is none).</summary>
    public static (int w, int h) JpegSize(byte[] jpeg)
    {
        if (jpeg == null || jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return (0, 0);
        int i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF) { i++; continue; }
            byte marker = jpeg[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
            int len = (jpeg[i + 2] << 8) | jpeg[i + 3];
            // SOF0..SOF15 but DHT (C4), JPG (C8) and DAC (CC): height, then width
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                return ((jpeg[i + 7] << 8) | jpeg[i + 8], (jpeg[i + 5] << 8) | jpeg[i + 6]);
            i += 2 + len;
        }
        return (0, 0);
    }

    /// <summary>The PDF of those pages (JPEG, each filling one A4 page). title: what a reader shows as the document's name.</summary>
    public static byte[] Write(IReadOnlyList<byte[]> jpegPages, string title = "")
    {
        if (jpegPages == null || jpegPages.Count == 0) throw new ArgumentException("a PDF needs at least one page");
        using var ms = new MemoryStream();
        var offsets = new List<long>();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Obj() { offsets.Add(ms.Position); W($"{offsets.Count} 0 obj\n"); }
        string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        W("%PDF-1.4\n");
        ms.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });       // (the line that tells a reader the file is binary)
        int n = jpegPages.Count;
        // objects: 1 catalog, 2 pages, 3 info, then per page: page, image, content
        Obj(); W("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Obj(); W($"<< /Type /Pages /Count {n} /Kids [{string.Join(" ", Enumerable.Range(0, n).Select(i => $"{4 + i * 3} 0 R"))}] >>\nendobj\n");
        // (the title as UTF-16BE with its byte order mark, in hex: any language)
        string hex = "FEFF" + string.Concat(Encoding.BigEndianUnicode.GetBytes(title ?? "").Select(b => b.ToString("X2")));
        Obj(); W($"<< /Title <{hex}> /Producer (Bambi - Living AI Partner) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>\nendobj\n");
        for (int i = 0; i < n; i++)
        {
            int page = 4 + i * 3, image = page + 1, content = page + 2;
            var (w, h) = JpegSize(jpegPages[i]);
            if (w <= 0 || h <= 0) throw new ArgumentException($"page {i + 1} is not a JPEG picture");
            Obj(); W($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(A4W)} {F(A4H)}] /Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {content} 0 R >>\nendobj\n");
            Obj(); W($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegPages[i].Length} >>\nstream\n");
            ms.Write(jpegPages[i], 0, jpegPages[i].Length); W("\nendstream\nendobj\n");
            string draw = $"q {F(A4W)} 0 0 {F(A4H)} 0 0 cm /Im0 Do Q";
            Obj(); W($"<< /Length {draw.Length} >>\nstream\n{draw}\nendstream\nendobj\n");
        }
        long xref = ms.Position;
        W($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long o in offsets) W($"{o:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>
    /// A minimal reading of a PDF made here (for checks): the number of pages its page tree names, and whether the
    /// file holds together - the header, as many page objects and pictures as that, and a cross-reference table whose
    /// place the trailer gives rightly. -1: not such a file.
    /// </summary>
    public static int PageCount(byte[] pdf)
    {
        if (pdf == null || pdf.Length < 64) return -1;
        string s = Encoding.Latin1.GetString(pdf);
        if (!s.StartsWith("%PDF-") || !s.TrimEnd().EndsWith("%%EOF")) return -1;
        var count = Regex.Match(s, @"/Type /Pages /Count (\d+)");
        if (!count.Success) return -1;
        int n = int.Parse(count.Groups[1].Value);
        if (Regex.Matches(s, @"/Type /Page /Parent").Count != n || Regex.Matches(s, @"/Subtype /Image").Count != n) return -1;
        var start = Regex.Match(s, @"startxref\n(\d+)\n%%EOF");
        if (!start.Success || !long.TryParse(start.Groups[1].Value, out long at) || at <= 0 || at + 4 > s.Length || s.Substring((int)at, 4) != "xref") return -1;
        return n;
    }
}
