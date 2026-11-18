using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace LivingPartner.Host;

/// <summary>
/// The pages of a document, drawn as pictures with GDI+ in the app's storybook look: A4 at 150 dpi (1240 x 1754), a
/// cream page, brown ink, a scalloped title band, pastel boxes, page numbers. This is what the user reads on the
/// board in the headset and what the PDF holds (DocsPdf), so it is laid out like a printed page: its own line
/// breaking (Hangul and Latin), paragraphs that run on over pages, tables that repeat their header.
/// Fonts: the headings are in Jua (the app's own font, next to the host in web\; it has Hangul), the text in Malgun
/// Gothic (Windows' Hangul font). HasHangulFont says whether the text font really is there.
/// </summary>
public sealed class DocsPages : IDisposable
{
    public const int W = 1240, H = 1754, SmallW = 620;
    const float M = 108, Top = 112, Bottom = H - 126;
    const float TextW = W - M * 2;

    static readonly Color Paper = Color.FromArgb(0xFF, 0xF8, 0xEA), Ink = Color.FromArgb(0x4A, 0x2F, 0x27), Dim = Color.FromArgb(0x8A, 0x6E, 0x62),
        Butter = Color.FromArgb(0xFB, 0xE2, 0xA6), Peach = Color.FromArgb(0xF2, 0xA6, 0x6B), Sage = Color.FromArgb(0xDD, 0xEC, 0xD3), SageDeep = Color.FromArgb(0x7D, 0xA8, 0x6F),
        Sky = Color.FromArgb(0xD6, 0xE9, 0xF2), Pink = Color.FromArgb(0xF9, 0xD5, 0xD3), Card = Color.FromArgb(0xFF, 0xFD, 0xF6), Line = Color.FromArgb(0xEA, 0xD8, 0xBB), Link = Color.FromArgb(0x3E, 0x73, 0x86);
    static readonly Color[] Badges = { Peach, Color.FromArgb(0x9C, 0xC5, 0x8B), Color.FromArgb(0x8F, 0xBF, 0xDA), Color.FromArgb(0xEE, 0x9C, 0x9A), Color.FromArgb(0xE3, 0xBB, 0x55), Color.FromArgb(0xB7, 0xA1, 0xD6) };

    // ---- fonts
    public const string TextFontName = "Malgun Gothic";
    static readonly object FontGate = new();
    static PrivateFontCollection _private;
    static FontFamily _display;
    static bool _displayTried;

    /// <summary>Jua, from the file next to the host (web\Jua-Regular.ttf) - or, in a test, from the host's source folder. null: not found (the text font is used, bold).</summary>
    static FontFamily Display()
    {
        lock (FontGate)
        {
            if (_displayTried) return _display;
            _displayTried = true;
            try
            {
                string dir = AppContext.BaseDirectory, file = "";
                for (int up = 0; up < 8 && dir != null && file.Length == 0; up++, dir = Path.GetDirectoryName(dir.TrimEnd('\\', '/')))
                    foreach (string rel in new[] { @"web\Jua-Regular.ttf", @"Companion\LivingPartner.Host\web\Jua-Regular.ttf", @"LivingPartner.Host\web\Jua-Regular.ttf" })
                        if (File.Exists(Path.Combine(dir, rel))) { file = Path.Combine(dir, rel); break; }
                if (file.Length == 0) return null;
                _private = new PrivateFontCollection();
                _private.AddFontFile(file);
                _display = _private.Families.FirstOrDefault(f => f.IsStyleAvailable(FontStyle.Regular));
            }
            catch { _display = null; }
            return _display;
        }
    }

    /// <summary>Is Windows' Hangul font there (otherwise GDI+ puts another in its place, which may have no Hangul)?</summary>
    public static bool HasHangulFont
    {
        // (on a Korean Windows the font's own name is "맑은 고딕": it is asked for its English one)
        get { try { using var f = new Font(TextFontName, 12f, FontStyle.Regular, GraphicsUnit.Pixel); return string.Equals(f.FontFamily.GetName(1033), TextFontName, StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    }
    public static bool HasDisplayFont => Display() != null;

    static Font Head(float px) => Display() is { } jua ? new Font(jua, px, FontStyle.Regular, GraphicsUnit.Pixel) : new Font(TextFontName, px * 0.94f, FontStyle.Bold, GraphicsUnit.Pixel);
    static Font Text(float px, bool bold = false) => new(TextFontName, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    readonly Font _kicker = Text(19f, true), _title = Head(60f), _h = Head(35f), _body = Text(23.5f), _bodyB = Text(23.5f, true), _lead = Text(25.5f), _small = Text(18f), _smallB = Text(18f, true),
                  _label = Text(17f, true), _value = Head(27f), _valueS = Head(21.5f), _cell = Text(20f), _cellB = Text(20f, true), _foot = Text(17f), _badge = Head(22f), _big = Head(46f), _pill = Text(17.5f, true);
    readonly SolidBrush _ink = new(Ink), _dim = new(Dim), _link = new(Link), _white = new(Color.White);
    readonly StringFormat _fmt;

    readonly List<Bitmap> _pages = new();
    Graphics _g; float _y;
    string _running = "";       // the running head of the pages after the first
    bool _finished;

    public DocsPages()
    {
        _fmt = new StringFormat(StringFormat.GenericTypographic);
        _fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
    }

    public int Count => _pages.Count;

    // ------------------------------------------------------------------ pages
    void NewPage(bool first = false)
    {
        _g?.Dispose();
        var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
        bmp.SetResolution(150, 150);
        _pages.Add(bmp);
        _g = Graphics.FromImage(bmp);
        _g.SmoothingMode = SmoothingMode.AntiAlias; _g.TextRenderingHint = TextRenderingHint.AntiAlias; _g.InterpolationMode = InterpolationMode.HighQualityBicubic; _g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        _g.Clear(Paper);
        _y = Top;
        if (!first && _running.Length > 0)
        {
            // the running head: the document's name, small, over a soft line
            _g.DrawString(Fit(_running, _foot, TextW - 40), _foot, _dim, M, 62, _fmt);
            using var pen = new Pen(Line, 2f);
            _g.DrawLine(pen, M, 92, W - M, 92);
            _y = Top + 14;
        }
    }

    /// <summary>Room for h more pixels on this page? Otherwise the next page begins. Returns whether a new page was begun.</summary>
    bool Ensure(float h)
    {
        if (_y + h <= Bottom || _y <= Top + 20) return false;
        NewPage();
        return true;
    }

    // ------------------------------------------------------------------ text
    float Width(string s, Font f) => string.IsNullOrEmpty(s) ? 0 : _g.MeasureString(s, f, 100000, _fmt).Width;

    string Fit(string s, Font f, float width)
    {
        if (Width(s, f) <= width) return s;
        while (s.Length > 1 && Width(s + "…", f) > width) s = s[..^1];
        return s.TrimEnd() + "…";
    }

    /// <summary>The text as lines no wider than width: broken at spaces; a word that is wider than a line (an address, a long run of Hangul) is broken inside.</summary>
    List<string> Wrap(string text, Font f, float width)
    {
        var lines = new List<string>();
        float space = Width(" ", f);
        foreach (string para in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = new System.Text.StringBuilder(); float used = 0;
            foreach (string word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                float w = Width(word, f);
                if (line.Length > 0 && used + space + w > width) { lines.Add(line.ToString()); line.Clear(); used = 0; }
                if (w <= width) { if (line.Length > 0) { line.Append(' '); used += space; } line.Append(word); used += w; continue; }
                // wider than a line: piece by piece
                string rest = word;
                while (rest.Length > 0)
                {
                    int take = rest.Length;
                    while (take > 1 && Width(rest[..take], f) > width - used - (line.Length > 0 ? space : 0)) take--;
                    if (take == 1 && line.Length > 0 && Width(rest[..1], f) > width - used - space) { lines.Add(line.ToString()); line.Clear(); used = 0; continue; }
                    if (char.IsHighSurrogate(rest[take - 1]) && take < rest.Length) take++;
                    if (line.Length > 0) { line.Append(' '); used += space; }
                    line.Append(rest[..take]); used += Width(rest[..take], f);
                    rest = rest[take..];
                    if (rest.Length > 0) { lines.Add(line.ToString()); line.Clear(); used = 0; }
                }
            }
            if (line.Length > 0) lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>At most n of the lines; when some are left out the last one ends in "…".</summary>
    List<string> Clip(List<string> lines, int n, Font f, float width)
    {
        if (lines.Count <= n) return lines;
        var kept = lines.Take(n).ToList();
        kept[n - 1] = Fit(kept[n - 1] + " …", f, width - 4);
        if (!kept[n - 1].EndsWith("…")) kept[n - 1] += "…";
        return kept;
    }

    /// <summary>A paragraph that runs on over pages line by line - never one line alone: not its first at the foot of a page, not its last at the head of the next.</summary>
    void Para(string text, Font f, Brush b, float lineH, float indent = 0, float after = 14, float width = 0)
    {
        var lines = Wrap(text, f, (width > 0 ? width : TextW) - indent);
        for (int i = 0; i < lines.Count; i++)
        {
            Ensure(lines.Count > 1 && (i == 0 || i == lines.Count - 2) ? lineH * 2 : lineH);
            _g.DrawString(lines[i], f, b, M + indent, _y, _fmt);
            _y += lineH;
        }
        if (lines.Count > 0) _y += after;
    }

    static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void Box(RectangleF r, float radius, Color fill, Color? border = null, float thick = 2f)
    {
        using var path = Round(r, radius);
        using (var b = new SolidBrush(fill)) _g.FillPath(b, path);
        if (border is { } c) { using var pen = new Pen(c, thick); _g.DrawPath(pen, path); }
    }

    /// <summary>A little four-pointed star: the storybook's ornament.</summary>
    void Star(float cx, float cy, float r, Color c)
    {
        var pts = new PointF[8];
        for (int i = 0; i < 8; i++) { double a = Math.PI / 4 * i - Math.PI / 2; float rr = i % 2 == 0 ? r : r * 0.38f; pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr); }
        using var b = new SolidBrush(c);
        _g.FillPolygon(b, pts);
    }

    /// <summary>The first page's band: the kind of document, its title, and under it the small print (the date, how it was researched). The band's lower edge is scalloped.</summary>
    void Band(string kicker, string title, string small, string pill, bool warn)
    {
        _running = title;
        NewPage(true);
        var lines = Wrap(title, _title, TextW - 20);
        if (lines.Count > 3) { lines = lines.Take(3).ToList(); lines[2] = Fit(lines[2] + "…", _title, TextW - 20); }
        // (the small print and the pill side by side, or the pill under it when the two do not fit)
        float pillW = string.IsNullOrEmpty(pill) ? 0 : Width(pill, _pill) + 34;
        bool pillBelow = pillW > 0 && M + Width(small, _small) + 22 + pillW > W - M;
        float titleH = lines.Count * 74f, bandH = 104 + 44 + titleH + 86 + (pillBelow ? 38 : 0);
        using (var b = new SolidBrush(Butter))
        {
            _g.FillRectangle(b, 0, 0, W, bandH);
            const float r = 31f;
            for (float x = r; x < W + r; x += r * 2) _g.FillEllipse(b, x - r, bandH - r, r * 2, r * 2);
        }
        Star(W - M - 20, 104, 30, Color.FromArgb(0xFF, 0xF6, 0xDD)); Star(W - M - 78, 152, 13, Color.FromArgb(0xFF, 0xF6, 0xDD)); Star(W - M + 22, 170, 9, Peach);
        float y = 96;
        // the kicker, letter-spaced
        float kx = M;
        foreach (char ch in kicker.ToUpperInvariant()) { _g.DrawString(ch.ToString(), _kicker, _dim, kx, y, _fmt); kx += Width(ch.ToString(), _kicker) + 3.5f; }
        y += 44;
        foreach (string l in lines) { _g.DrawString(l, _title, _ink, M - 2, y, _fmt); y += 74; }
        y += 16;
        _g.DrawString(small, _small, _dim, M, y + 5, _fmt);
        if (!string.IsNullOrEmpty(pill))
        {
            float sx = M + Width(small, _small) + 22, pw = pillW;
            if (pillBelow) { sx = M; y += 38; }
            Box(new RectangleF(sx, y, pw, 34), 17, warn ? Pink : Color.FromArgb(0xFF, 0xF6, 0xDD), warn ? Color.FromArgb(0xE8, 0x9A, 0x96) : null);
            _g.DrawString(pill, _pill, _ink, sx + 17, y + 6.5f, _fmt);
        }
        _y = bandH + 31 + 44;
    }

    void Heading(int n, string text)
    {
        _y += 20;
        var lines = Wrap(text, _h, TextW - 62);
        Ensure(lines.Count * 46 + 30 + 80);
        using (var b = new SolidBrush(Badges[(n - 1) % Badges.Length])) _g.FillEllipse(b, M, _y + 1, 44, 44);
        using (var c = new StringFormat(_fmt) { Alignment = StringAlignment.Center })
            _g.DrawString(n.ToString(), _badge, _white, new RectangleF(M, _y + 11, 44, 30), c);
        foreach (string l in lines) { _g.DrawString(l, _h, _ink, M + 60, _y + 3, _fmt); _y += 46; }
        _y += 8;
        using (var pen = new Pen(Line, 3f) { DashStyle = DashStyle.Dot, DashCap = DashCap.Round }) _g.DrawLine(pen, M, _y, W - M, _y);
        _y += 22;
    }

    /// <summary>A heading without a number (Sources).</summary>
    void PlainHeading(string text)
    {
        _y += 20;
        Ensure(46 + 30 + 60);
        Star(M + 18, _y + 22, 17, Peach);
        _g.DrawString(text, _h, _ink, M + 50, _y + 3, _fmt); _y += 54;
        using (var pen = new Pen(Line, 3f) { DashStyle = DashStyle.Dot, DashCap = DashCap.Round }) _g.DrawLine(pen, M, _y, W - M, _y);
        _y += 22;
    }

    void Bullets(IEnumerable<string> items)
    {
        foreach (string item in items)
        {
            var lines = Wrap(item, _body, TextW - 40);
            for (int i = 0; i < lines.Count; i++)
            {
                Ensure(lines.Count > 1 && (i == 0 || i == lines.Count - 2) ? 74 : 37);
                if (i == 0) using (var b = new SolidBrush(Peach)) _g.FillEllipse(b, M + 8, _y + 12, 11, 11);
                _g.DrawString(lines[i], _body, _ink, M + 40, _y, _fmt);
                _y += 37;
            }
            _y += 7;
        }
        _y += 8;
    }

    /// <summary>The summary: a card with a peach bar down its left side.</summary>
    void Summary(string text)
    {
        var lines = Wrap(text, _lead, TextW - 78);
        float h = lines.Count * 41 + 46;
        Ensure(h + 10);
        var r = new RectangleF(M, _y, TextW, h);
        Box(r, 22, Card, Line);
        using (var path = Round(new RectangleF(M + 16, _y + 18, 9, h - 36), 4.5f)) using (var b = new SolidBrush(Peach)) _g.FillPath(b, path);
        float y = _y + 22;
        foreach (string l in lines) { _g.DrawString(l, _lead, _ink, M + 46, y, _fmt); y += 41; }
        _y += h + 30;
    }

    /// <summary>The box of key dates and numbers: cards, two (or three) in a row.</summary>
    void Facts(string heading, IReadOnlyList<ReportFact> facts)
    {
        if (facts.Count == 0) return;
        int cols = facts.Count == 3 || facts.Count >= 5 ? 3 : 2;
        float gap = 18, pad = 26, cardW = (TextW - pad * 2 - gap * (cols - 1)) / cols;
        // measured first: every card of a row is as tall as its tallest
        var rows = new List<(List<(Font font, List<string> label, List<string> value)> cells, float h)>();
        for (int i = 0; i < facts.Count; i += cols)
        {
            var cells = facts.Skip(i).Take(cols).Select(f =>
            {
                // (a value with a word wider than the card - "9/24/2026-11/18/2026" - is set smaller rather than broken inside the word)
                var font = f.value.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => Width(w, _value) <= cardW - 36) ? _value : _valueS;
                return (font, Wrap(f.label.ToUpperInvariant(), _label, cardW - 36), Wrap(f.value, font, cardW - 36));
            }).ToList();
            rows.Add((cells, cells.Max(c => c.Item2.Count * 23 + c.Item3.Count * 35) + 40));
        }
        float h = 64 + rows.Sum(r => r.h + gap) - gap + pad;
        Ensure(h + 10);
        Box(new RectangleF(M, _y, TextW, h), 26, Sage);
        Star(M + pad + 12, _y + 37, 13, SageDeep);
        _g.DrawString(heading, _bodyB, _ink, M + pad + 36, _y + 21, _fmt);
        float y = _y + 64;
        foreach (var (cells, rh) in rows)
        {
            for (int c = 0; c < cells.Count; c++)
            {
                float x = M + pad + c * (cardW + gap);
                Box(new RectangleF(x, y, cardW, rh), 16, Card);
                float ty = y + 17;
                foreach (string l in cells[c].label) { _g.DrawString(l, _label, _dim, x + 18, ty, _fmt); ty += 23; }
                ty += 3;
                foreach (string l in cells[c].value) { _g.DrawString(l, cells[c].font, _ink, x + 18, ty + (cells[c].font == _value ? 0 : 4), _fmt); ty += 35; }
            }
            y += rh + gap;
        }
        _y += h + 26;
    }

    void Table(ReportTable t)
    {
        int n = t.columns.Count;
        const float pad = 14;
        // the columns share the width by what is in them: a column of short labels is narrow, the one with the sentences wide
        var want = new float[n];
        for (int c = 0; c < n; c++)
        {
            float widest = Width(t.columns[c], _cellB);
            foreach (var row in t.rows) if (c < row.Count) widest = Math.Max(widest, Width(row[c], c == 0 ? _cellB : _cell));
            want[c] = Math.Clamp(widest + pad * 2 + 6, TextW * 0.14f, TextW * 0.62f);
        }
        float sum = want.Sum();
        var colW = want.Select(w => w * TextW / sum).ToArray();
        var colX = new float[n];
        for (int c = 1; c < n; c++) colX[c] = colX[c - 1] + colW[c - 1];
        float RowH(IReadOnlyList<string> cells, Font f, out List<List<string>> wrapped)
        {
            wrapped = cells.Select((c, i) => Wrap(c, i == 0 ? _cellB : f, colW[Math.Min(i, n - 1)] - pad * 2)).ToList();
            return Math.Max(1, wrapped.Max(w => w.Count)) * 29 + 22;
        }
        void HeaderRow()
        {
            float h = RowH(t.columns, _cellB, out var cols);
            using (var path = Round(new RectangleF(M, _y, TextW, h), 12)) using (var b = new SolidBrush(Butter)) _g.FillPath(b, path);
            for (int c = 0; c < n; c++) { float ty = _y + 11; foreach (string l in cols[c]) { _g.DrawString(l, _cellB, _ink, M + colX[c] + pad, ty, _fmt); ty += 29; } }
            _y += h;
        }
        float first = RowH(t.columns, _cellB, out _) + RowH(t.rows[0], _cell, out _);
        Ensure(first + 12);
        HeaderRow();
        using var pen = new Pen(Line, 2f);
        for (int r = 0; r < t.rows.Count; r++)
        {
            float h = RowH(t.rows[r], _cell, out var cells);
            if (Ensure(h + 4)) HeaderRow();
            if (r % 2 == 1) using (var b = new SolidBrush(Color.FromArgb(0xFF, 0xFB, 0xF0))) _g.FillRectangle(b, M, _y, TextW, h);
            for (int c = 0; c < n; c++) { float ty = _y + 11; foreach (string l in cells[c]) { _g.DrawString(l, c == 0 ? _cellB : _cell, _ink, M + colX[c] + pad, ty, _fmt); ty += 29; } }
            _y += h;
            _g.DrawLine(pen, M, _y, W - M, _y);
        }
        _y += 26;
    }

    // ------------------------------------------------------------------ a report
    /// <summary>Lays a report out over as many pages as it takes.</summary>
    public void Report(ReportData r)
    {
        bool ko = r.korean;
        Band("Bambi's report", r.title, r.made.ToString("yyyy-MM-dd"),
            r.web ? (ko ? "웹에서 찾아본 내용" : "Researched on the web") : (ko ? "기억으로 작성 · 웹에서 확인하지 않음" : "Written from memory, not checked on the web"), !r.web);
        if (r.summary.Length > 0) Summary(r.summary);
        Facts(ko ? "핵심 날짜 · 숫자" : "Key dates and numbers", r.facts);
        int n = 0;
        foreach (var s in r.sections)
        {
            Heading(++n, s.heading);
            foreach (string p in s.paragraphs) Para(p, _body, _ink, 37.5f);
            if (s.bullets.Count > 0) Bullets(s.bullets);
            if (s.table.Any) Table(s.table);
        }
        if (r.sources.Count > 0)
        {
            // the list is kept together: when it does not fit on what is left of this page but fits on one, it begins the next
            float all = 20 + 46 + 30 + 22 + r.sources.Sum(s => Wrap(s.title, _cellB, TextW - 44).Count * 29 + Wrap(s.url, _small, TextW - 44).Count * 25 + 12);
            if (_y + all > Bottom && all < Bottom - Top - 20 && _y > Top + 20) NewPage();
            PlainHeading(ko ? "출처" : "Sources");
            int k = 0;
            foreach (var s in r.sources)
            {
                k++;
                var title = Wrap(s.title, _cellB, TextW - 44);
                var url = Wrap(s.url, _small, TextW - 44);
                Ensure(title.Count * 29 + url.Count * 25 + 12);
                _g.DrawString(k + ".", _cellB, _dim, M + 4, _y, _fmt);
                foreach (string l in title) { _g.DrawString(l, _cellB, _ink, M + 44, _y, _fmt); _y += 29; }
                foreach (string l in url) { _g.DrawString(l, _small, _link, M + 44, _y, _fmt); _y += 25; }
                _y += 12;
            }
        }
        else if (!r.web)
        {
            _y += 14;
            var note = Wrap(ko ? "이 문서는 웹 검색 없이 Bambi가 알고 있는 내용으로 쓴 것입니다. 날짜와 숫자는 공식 페이지에서 꼭 다시 확인하세요." : "This report was written from what Bambi knows, without a web search. Check dates and numbers on the official pages.", _small, TextW - 60);
            float h = note.Count * 27 + 30;
            Ensure(h + 8);
            Box(new RectangleF(M, _y, TextW, h), 16, Pink);
            float y = _y + 15;
            foreach (string l in note) { _g.DrawString(l, _small, _ink, M + 30, y, _fmt); y += 27; }
            _y += h + 20;
        }
    }

    // ------------------------------------------------------------------ a sheet of design options
    /// <summary>A picture on a white card with a soft border, kept in proportion.</summary>
    void PictureCard(Image img, RectangleF r, float pad)
    {
        Box(new RectangleF(r.X + 3, r.Y + 5, r.Width, r.Height), 26, Color.FromArgb(0xF1, 0xE4, 0xCB));
        Box(r, 26, Color.White, Line);
        if (img == null) return;
        float box = Math.Min(r.Width, r.Height) - pad * 2, k = Math.Min(box / img.Width, box / img.Height);
        float w = img.Width * k, h = img.Height * k;
        _g.DrawImage(img, r.X + (r.Width - w) / 2f, r.Y + (r.Height - h) / 2f, w, h);
    }

    /// <summary>
    /// Page 1: the original and the four options with their names and one line each; then one page per option, large.
    /// options: the name ("Option 2 - bolder"), the line that says why, the picture.
    /// </summary>
    public void DesignSheet(string title, string what, string weak, Image original, IReadOnlyList<(string name, string rationale, Image picture)> options, bool ko, DateTime made)
    {
        Band("Design options", title, made.ToString("yyyy-MM-dd"), ko ? $"시안 {options.Count}개" : $"{options.Count} options", false);
        // the original, and what it is / what is weak beside it
        float side = 250, top = _y;
        PictureCard(original, new RectangleF(M, top, side, side), 22);
        using (var c = new StringFormat(_fmt) { Alignment = StringAlignment.Center })
            _g.DrawString(ko ? "원본" : "Original", _smallB, _dim, new RectangleF(M, top + side + 12, side, 26), c);
        float tx = M + side + 44, tw = W - M - tx, y = top + 6;
        void Note(string label, string text, Color dot)
        {
            using (var b = new SolidBrush(dot)) _g.FillEllipse(b, tx, y + 5, 13, 13);
            _g.DrawString(label.ToUpperInvariant(), _label, _dim, tx + 24, y, _fmt); y += 30;
            var lines = Wrap(text, _body, tw);
            foreach (string l in Clip(lines, 3, _body, tw)) { _g.DrawString(l, _body, _ink, tx, y, _fmt); y += 36; }
            y += 18;
        }
        if (!string.IsNullOrWhiteSpace(what)) Note(ko ? "무엇인가" : "What it is", what, SageDeep);
        if (!string.IsNullOrWhiteSpace(weak)) Note(ko ? "아쉬운 점" : "What is weak", weak, Color.FromArgb(0xEE, 0x9C, 0x9A));
        _y = Math.Max(top + side + 48, y) + 26;
        using (var pen = new Pen(Line, 3f) { DashStyle = DashStyle.Dot, DashCap = DashCap.Round }) _g.DrawLine(pen, M, _y, W - M, _y);
        _y += 34;
        // the grid: two in a row
        float gap = 40, cellW = (TextW - gap) / 2f, rowsLeft = (float)Math.Ceiling(options.Count / 2.0);
        float rowH = Math.Min(470, (Bottom - _y - (rowsLeft - 1) * 26) / Math.Max(1, rowsLeft)), pic = Math.Max(160, rowH - 132);
        for (int i = 0; i < options.Count; i++)
        {
            int col = i % 2;
            if (col == 0 && i > 0) _y += rowH + 26;
            float x = M + col * (cellW + gap);
            PictureCard(options[i].picture, new RectangleF(x + (cellW - pic) / 2f, _y, pic, pic), 20);
            using var c = new StringFormat(_fmt) { Alignment = StringAlignment.Center };
            _g.DrawString(Fit(options[i].name, _bodyB, cellW), _bodyB, _ink, new RectangleF(x, _y + pic + 16, cellW, 34), c);
            float ly = _y + pic + 54;
            foreach (string l in Clip(Wrap(options[i].rationale, _small, cellW - 20), 2, _small, cellW - 20)) { _g.DrawString(l, _small, _dim, new RectangleF(x, ly, cellW, 26), c); ly += 26; }
        }
        // one page per option
        for (int i = 0; i < options.Count; i++)
        {
            NewPage();
            float big = 880, bx = (W - big) / 2f;
            _y += 24;
            PictureCard(options[i].picture, new RectangleF(bx, _y, big, big), 60);
            _y += big + 54;
            using var c = new StringFormat(_fmt) { Alignment = StringAlignment.Center };
            foreach (string l in Wrap(options[i].name, _big, TextW).Take(2)) { _g.DrawString(l, _big, _ink, new RectangleF(M, _y, TextW, 60), c); _y += 60; }
            _y += 14;
            foreach (string l in Clip(Wrap(options[i].rationale, _lead, TextW - 120), 5, _lead, TextW - 120)) { _g.DrawString(l, _lead, _dim, new RectangleF(M, _y, TextW, 40), c); _y += 40; }
            _y += 30;
            string file = $"option-{i + 1}.png";
            float fw = Width(file, _pill) + 36;
            Box(new RectangleF((W - fw) / 2f, _y, fw, 36), 18, Sky);
            _g.DrawString(file, _pill, _ink, new RectangleF(M, _y + 7.5f, TextW, 24), c);
        }
    }

    // ------------------------------------------------------------------ out
    /// <summary>The feet of the pages (they need the number of pages): a soft line, who made it, "2 / 4".</summary>
    void Finish()
    {
        if (_finished) return;
        _finished = true;
        _g?.Dispose(); _g = null;
        for (int i = 0; i < _pages.Count; i++)
        {
            using var g = Graphics.FromImage(_pages[i]);
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var pen = new Pen(Line, 2f);
            g.DrawLine(pen, M, H - 96, W - M, H - 96);
            g.DrawString("Made by Bambi", _foot, _dim, M, H - 78, _fmt);
            string num = $"{i + 1} / {_pages.Count}";
            g.DrawString(num, _foot, _dim, W - M - g.MeasureString(num, _foot, 1000, _fmt).Width, H - 78, _fmt);
        }
    }

    static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    /// <summary>The pages as JPEG (for the PDF).</summary>
    public List<byte[]> Jpegs(long quality = 90)
    {
        Finish();
        var list = new List<byte[]>();
        foreach (var p in _pages)
        {
            using var ms = new MemoryStream();
            using (var ep = new EncoderParameters(1)) { ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality); p.Save(ms, JpegCodec, ep); }
            list.Add(ms.ToArray());
        }
        return list;
    }

    /// <summary>Writes page-&lt;n&gt;.png (1240 wide) and page-&lt;n&gt;-s.png (620 wide, for the headset) into a folder. Returns the number of pages.</summary>
    public int SavePngs(string dir)
    {
        Finish();
        Directory.CreateDirectory(dir);
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Save(Path.Combine(dir, $"page-{i + 1}.png"), ImageFormat.Png);
            using var small = new Bitmap(SmallW, (int)Math.Round(H * (SmallW / (double)W)), PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using var wrap = new ImageAttributes();
                wrap.SetWrapMode(WrapMode.TileFlipXY);          // (no pale seam at the edges)
                g.DrawImage(_pages[i], new Rectangle(0, 0, small.Width, small.Height), 0, 0, W, H, GraphicsUnit.Pixel, wrap);
            }
            small.Save(Path.Combine(dir, $"page-{i + 1}-s.png"), ImageFormat.Png);
        }
        return _pages.Count;
    }

    public void Dispose()
    {
        _g?.Dispose();
        foreach (var p in _pages) p.Dispose();
        foreach (var f in new[] { _kicker, _title, _h, _body, _bodyB, _lead, _small, _smallB, _label, _value, _valueS, _cell, _cellB, _foot, _badge, _big, _pill }) f.Dispose();
        _ink.Dispose(); _dim.Dispose(); _link.Dispose(); _white.Dispose(); _fmt.Dispose();
    }
}
