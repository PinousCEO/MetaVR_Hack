using System.Globalization;
using System.Text;
using LivingPartner.Host;
using SkiaSharp;

namespace LivingPartner.Mac;

/// <summary>The same rendered pages supply the Mac PDF, the Quest reader and its small previews.</summary>
public static class MacDocumentPages
{
    /// <summary>Writes a small transparent PNG for a design option on the Quest board.</summary>
    public static void Thumbnail(string from, string to)
    {
        using var image = SKImage.FromEncodedData(from) ?? throw new InvalidDataException("Could not read the design picture.");
        float scale = Math.Min(1f, 384f / Math.Max(image.Width, image.Height));
        int w = Math.Max(1, (int)Math.Round(image.Width * scale)), h = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var scaled = surface.Snapshot(); using var png = scaled.Encode(SKEncodedImageFormat.Png, 100);
        string destination = Path.GetFullPath(to), pending = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        try
        {
            using (var f = File.Create(pending)) png.SaveTo(f);
            File.Move(pending, destination, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public static int Report(ReportData report, string folder, string pdfPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var book = new Pages(folder);
        bool ko = report.korean;
        book.Start(report.title, report.made.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "   ·   " +
            (report.web ? ko ? "웹 자료 조사" : "Web research" : ko ? "일반 지식 기반 · 웹 검증 없음" : "Written from general knowledge without web verification"));
        book.Paragraph(report.summary, 32, 47);
        if (report.facts.Count > 0)
        {
            book.Heading(ko ? "주요 정보" : "At a glance");
            foreach (var f in report.facts) book.Paragraph(f.label + "  ·  " + f.value, 26, 38);
        }
        foreach (var section in report.sections)
        {
            book.Heading(section.heading);
            foreach (string paragraph in section.paragraphs) book.Paragraph(paragraph);
            foreach (string bullet in section.bullets) book.Paragraph(bullet, bullet: true);
            if (section.table?.Any == true) book.Table(section.table);
        }
        if (report.sources.Count > 0)
        {
            book.Heading(ko ? "출처" : "Sources");
            foreach (var source in report.sources)
            {
                book.Ensure(86);
                book.Paragraph(source.title, 25, 36, after: 1);
                book.Paragraph(source.url, 21, 32, color: Pages.Link);
            }
        }
        return book.Save(pdfPath);
    }

    public static int Designs(string title, IReadOnlyList<string> pngPaths, string folder, string pdfPath)
    {
        ArgumentNullException.ThrowIfNull(pngPaths);
        if (pngPaths.Count == 0) throw new ArgumentException("At least one design is required.", nameof(pngPaths));
        using var book = new Pages(folder);
        book.Start(title, "Bambi  /  Design options");
        for (int i = 0; i < pngPaths.Count; i += 4)
        {
            if (i > 0) book.NewPage();
            book.ContactSheet(pngPaths, i);
        }
        return book.Save(pdfPath);
    }

    sealed class Pages : IDisposable
    {
        const int Width = 1240, Height = 1754, Margin = 94, Right = Width - Margin, Content = Width - Margin * 2, Bottom = Height - 118;
        static readonly SKColor Paper = new(255, 250, 240), Ink = new(49, 38, 39), Dim = new(100, 83, 76), Rule = new(220, 206, 187), Honey = new(246, 221, 165);
        public static readonly SKColor Link = new(46, 100, 114);
        readonly string _folder, _stage;
        readonly SKPaint _paint = new() { IsAntialias = true };
        // Use the project's display face for titles. Body text prefers the platform's full Korean family.
        readonly SKTypeface _display, _body;
        readonly Dictionary<int, SKTypeface> _missing = new();
        readonly Dictionary<(SKTypeface, int), SKFont> _fonts = new();
        readonly List<byte[]> _jpegs = new();
        SKSurface _surface;
        SKCanvas Canvas => _surface.Canvas;
        string _title = "";
        float _y;

        public Pages(string folder)
        {
            _folder = Path.GetFullPath(folder);
            using var embedded = typeof(MacDocumentPages).Assembly.GetManifestResourceStream("LivingPartner.Mac.Jua.ttf");
            _display = SKTypeface.FromStream(embedded ?? throw new InvalidDataException("The bundled Korean typeface is missing."))
                ?? throw new InvalidDataException("Could not load the bundled Korean typeface.");
            string family = OperatingSystem.IsMacOS() ? "Apple SD Gothic Neo" : OperatingSystem.IsWindows() ? "Malgun Gothic" : "Noto Sans CJK KR";
            var system = SKTypeface.FromFamilyName(family);
            if (system != null && system.ContainsGlyph('한')) _body = system;
            else { system?.Dispose(); _body = _display; }
            Directory.CreateDirectory(_folder);
            _stage = Path.Combine(_folder, ".render-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_stage);
        }

        SKFont Font(SKTypeface face, int size)
        {
            if (!_fonts.TryGetValue((face, size), out var font)) _fonts[(face, size)] = font = new SKFont(face, size) { Edging = SKFontEdging.Antialias };
            return font;
        }

        SKTypeface Face(int scalar, bool heading)
        {
            var first = heading ? _display : _body;
            if (first.ContainsGlyph(scalar)) return first;
            var second = heading ? _body : _display;
            if (second.ContainsGlyph(scalar)) return second;
            if (!_missing.TryGetValue(scalar, out var fallback))
                _missing[scalar] = fallback = SKFontManager.Default.MatchCharacter(scalar);
            return fallback ?? first;
        }

        // Measuring and drawing share the exact font runs, including mixed Hangul/Latin and missing-glyph fallback.
        float Text(string line, int size, bool heading = false, float? x = null, float baseline = 0, SKColor? color = null)
        {
            float advance = 0; int start = 0, at = 0;
            SKTypeface face = null;
            void Run(int end)
            {
                if (end <= start) return;
                var font = Font(face, size); string word = line.Substring(start, end - start);
                if (x.HasValue) { _paint.Color = color ?? Ink; Canvas.DrawText(word, x.Value + advance, baseline, SKTextAlign.Left, font, _paint); }
                advance += font.MeasureText(word);
            }
            foreach (Rune rune in line.EnumerateRunes())
            {
                var next = Face(rune.Value, heading);
                if (next != face) { Run(at); start = at; face = next; }
                at += rune.Utf16SequenceLength;
            }
            Run(at); return advance;
        }

        IEnumerable<string> Lines(string source, int size, float width, bool heading = false)
        {
            foreach (string paragraph in (source ?? "").Replace("\r", "").Normalize().Split('\n'))
            {
                string line = "";
                foreach (string word in paragraph.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                {
                    string next = line.Length == 0 ? word : line + " " + word;
                    if (Text(next, size, heading) <= width) { line = next; continue; }
                    if (line.Length > 0) { yield return line; line = ""; }
                    if (Text(word, size, heading) <= width) { line = word; continue; }
                    // A URL or an unspaced Hangul word can exceed the column width. Keep Unicode scalars intact.
                    foreach (Rune rune in word.EnumerateRunes())
                    {
                        string letter = rune.ToString();
                        if (line.Length > 0 && Text(line + letter, size, heading) > width) { yield return line; line = ""; }
                        line += letter;
                    }
                }
                yield return line;
            }
        }

        public void Start(string title, string note)
        {
            _title = title ?? "";
            NewPage();
            Paragraph(note, 22, 33, color: Dim);
            Paragraph(_title, 56, 73, heading: true, after: 20);
            Rect(Margin, _y, Margin + 106, _y + 7, Honey); _y += 38;
        }

        public void NewPage()
        {
            if (_surface != null) FlushPage();
            _surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque))
                ?? throw new IOException("Could not allocate a report page.");
            Canvas.Clear(Paper); _y = Margin;
            if (_jpegs.Count == 0) return;
            string running = Lines(_title, 19, Content).First();
            Text(running, 19, x: Margin, baseline: 67, color: Dim); Rect(Margin, 82, Right, 84, Rule); _y = 112;
        }

        void Rect(float l, float top, float r, float bottom, SKColor color)
        { _paint.Color = color; Canvas.DrawRect(new SKRect(l, top, r, bottom), _paint); }

        public void Ensure(float height) { if (_y + height > Bottom && _y > 112) NewPage(); }

        public void Heading(string heading)
        {
            if (string.IsNullOrWhiteSpace(heading)) return;
            var lines = Lines(heading, 38, Content, true).ToList();
            Ensure(lines.Count * 50f + 82f); _y += 13;
            Paragraph(heading, 38, 50, heading: true, after: 9);
        }

        public void Paragraph(string value, int size = 28, float leading = 42, bool bullet = false, bool heading = false, float after = 14, SKColor? color = null)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            bool first = true; int indent = bullet ? 40 : 0;
            foreach (string line in Lines(value, size, Content - indent, heading))
            {
                Ensure(leading);
                if (first && bullet) { _paint.Color = Dim; Canvas.DrawCircle(Margin + 10, _y + size * .67f, 4, _paint); }
                Text(line, size, heading, Margin + indent, _y + size, color);
                _y += leading; first = false;
            }
            _y += after;
        }

        public void Table(ReportTable table)
        {
            int columns = table.columns.Count;
            float cell = (float)Content / columns;
            List<string>[] Cells(IReadOnlyList<string> values, bool header) => Enumerable.Range(0, columns)
                .Select(i => Lines(i < values.Count ? values[i] : "", 23, cell - 26, header).ToList()).ToArray();
            float RowHeight(List<string>[] cells) => cells.Max(c => c.Count) * 34 + 24;
            void Draw(List<string>[] cells, bool header)
            {
                float h = RowHeight(cells); Rect(Margin, _y, Right, _y + h, header ? Honey : SKColors.White);
                for (int col = 0; col < columns; col++)
                {
                    float x = Margin + cell * col;
                    for (int j = 0; j < cells[col].Count; j++) Text(cells[col][j], 23, header, x + 13, _y + 34 + j * 34);
                    if (col > 0) Rect(x, _y, x + 1, _y + h, Rule);
                }
                _y += h; Rect(Margin, _y - 2, Right, _y, Rule);
            }
            var head = Cells(table.columns, true);
            bool newHeader = true;
            foreach (var values in table.rows)
            {
                var row = Cells(values, false);
                if (_y + RowHeight(row) + (newHeader ? RowHeight(head) : 0) > Bottom) { NewPage(); newHeader = true; }
                if (newHeader) { Draw(head, true); newHeader = false; }
                Draw(row, false);
            }
            _y += 25;
        }

        public void ContactSheet(IReadOnlyList<string> files, int start)
        {
            float gap = 30, card = (Content - gap) / 2;
            float available = Bottom - _y, rowHeight = Math.Min(card + 62, (available - gap) / 2);
            for (int i = 0; i < 4 && start + i < files.Count; i++)
            {
                using var image = SKImage.FromEncodedData(files[start + i]) ?? throw new InvalidDataException("Could not read design option " + (start + i + 1));
                float x = Margin + (i % 2) * (card + gap), y = _y + (i / 2) * (rowHeight + gap);
                Rect(x, y, x + card, y + rowHeight, SKColors.White);
                float scale = Math.Min((card - 48) / image.Width, (rowHeight - 94) / image.Height);
                float w = image.Width * scale, h = image.Height * scale;
                float left = x + (card - w) / 2, top = y + (rowHeight - 60 - h) / 2;
                Canvas.DrawImage(image, new SKRect(left, top, left + w, top + h), new SKSamplingOptions(SKFilterMode.Linear));
                Text((start + i + 1).ToString("00"), 31, true, x + 25, y + rowHeight - 23);
                Rect(x + 16, y + rowHeight - 59, x + card - 16, y + rowHeight - 57, Rule);
            }
        }

        void FlushPage()
        {
            int page = _jpegs.Count + 1;
            Rect(Margin, Height - 88, Right, Height - 86, Rule);
            Text("Bambi", 22, true, Margin, Height - 48, Dim);
            string number = page.ToString(CultureInfo.InvariantCulture);
            Text(number, 21, x: Right - Text(number, 21), baseline: Height - 48, color: Dim);
            using var image = _surface.Snapshot();
            using (var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 91)) _jpegs.Add(jpeg.ToArray());
            using (var full = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var f = File.Create(Path.Combine(_stage, $"page-{page}.png"))) full.SaveTo(f);
            using var small = SKSurface.Create(new SKImageInfo(Width / 2, Height / 2, SKColorType.Rgba8888, SKAlphaType.Opaque));
            small.Canvas.DrawImage(image, new SKRect(0, 0, Width / 2, Height / 2), new SKSamplingOptions(SKFilterMode.Linear));
            using (var thumb = small.Snapshot())
            using (var png = thumb.Encode(SKEncodedImageFormat.Png, 100))
            using (var f = File.Create(Path.Combine(_stage, $"page-{page}-s.png"))) png.SaveTo(f);
            _surface.Dispose(); _surface = null;
        }

        public int Save(string pdfPath)
        {
            FlushPage();
            string target = Path.GetFullPath(pdfPath); Directory.CreateDirectory(Path.GetDirectoryName(target));
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(pending, DocsPdf.Write(_jpegs, _title));
                foreach (var file in Directory.EnumerateFiles(_stage, "*.png")) File.Move(file, Path.Combine(_folder, Path.GetFileName(file)), true);
                File.Move(pending, target, true);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            return _jpegs.Count;
        }

        public void Dispose()
        {
            _surface?.Dispose(); _paint.Dispose();
            foreach (var font in _fonts.Values) font.Dispose();
            foreach (var face in _missing.Values.Distinct()) if (face != _body && face != _display) face?.Dispose();
            if (_body != _display) _body.Dispose(); _display.Dispose();
            if (Directory.Exists(_stage)) Directory.Delete(_stage, true);
        }
    }
}
