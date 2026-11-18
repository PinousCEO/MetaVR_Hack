using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// THE WORKSHOP'S EYES: one frame of a play (PlayFrame: the GlassSprite list and the state round it) drawn as a
/// PICTURE, so that the model that wrote the script SEES its layout - and the critic sees what the user would.
/// Plain shapes (rect, disc, hoop, the lines made of them), words, cards and the paper of the UI kit are drawn as
/// the clients draw them, in the storybook colours; a sticker is the real picture when it is found
/// (Art/Ui/glass-&lt;name&gt;.png beside or above the program), otherwise a labelled shape; a picture that is yet to be
/// painted for the module is a dashed box with its name. Bambi is a small figure with its pose written under it; the
/// player's hand is a soft shadow, their aim a grey cross; what has left the field for the room is an arrow at the
/// place it went through. A strip above shows what the frame round a game shows (score, lives, level, banner).
/// Never throws: a frame that cannot be drawn gives null.
/// </summary>
public static class WorkshopLook
{
    public const int FieldH = 540, Top = 44, Bottom = 30;
    static readonly Color Cream = Color.FromArgb(0xFF, 0xF8, 0xEA), Ink = Color.FromArgb(0x4A, 0x2F, 0x27), Desk = Color.FromArgb(0xF3, 0xE6, 0xCF);
    static readonly ConcurrentDictionary<string, Bitmap> Stickers = new();
    static string _artDir;

    /// <summary>Where the stickers' pictures are: LP_GLASS_ART, a folder "glass-art" beside the program, or the Unity project above it.</summary>
    static string ArtDir()
    {
        if (_artDir != null) return _artDir;
        string found = "";
        try
        {
            string env = Environment.GetEnvironmentVariable("LP_GLASS_ART");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) found = env;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); found.Length == 0 && d != null; d = d.Parent)
            {
                string beside = Path.Combine(d.FullName, "glass-art"), unity = Path.Combine(d.FullName, "MetaVRHack", "Assets", "LivingPartner", "Art", "Ui");
                if (Directory.Exists(beside)) found = beside; else if (File.Exists(Path.Combine(unity, "glass-star.png"))) found = unity;
            }
        }
        catch (Exception) { }
        return _artDir = found;
    }

    static Bitmap Sticker(string art)
    {
        return Stickers.GetOrAdd(art, a =>
        {
            try
            {
                string dir = ArtDir();
                if (dir.Length == 0 || a.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '-'))) return null;
                string file = Path.Combine(dir, "glass-" + a + ".png");
                if (!File.Exists(file)) return null;
                using var src = new Bitmap(file);
                // (kept small: a sticker is at most a third of the picture high)
                int h = Math.Min(src.Height, 192), w = Math.Max(1, src.Width * h / Math.Max(1, src.Height));
                var copy = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(copy);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
                return copy;
            }
            catch (Exception) { return null; }
        });
    }

    static Color Rgb(int c, float alpha, Color fallback)
    {
        int a = (int)Math.Round(255 * Math.Clamp(alpha, 0f, 1f));
        return c < 0 ? Color.FromArgb(a, fallback) : Color.FromArgb(a, (c >> 16) & 255, (c >> 8) & 255, c & 255);
    }

    static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(Math.Min(r.Width, r.Height), Math.Max(0.5f, radius * 2f));
        if (d < 1.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>The frame as a PNG (about 960 x 614), or null when it could not be drawn. caption: one line under the picture.</summary>
    public static byte[] Png(PlayFrame f, string caption = "")
    {
        if (f == null) return null;
        try
        {
            float S = FieldH;
            int w = (int)Math.Round(Math.Clamp(f.W, 0.5f, 4f) * S), h = FieldH + Top + Bottom;
            using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.Clear(Desk);
                using (var paper = new SolidBrush(Cream)) g.FillRectangle(paper, 0, Top, w, FieldH);
                PointF P(float x, float y) => new(x * S, Top + (1f - y) * S);
                var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
                g.SetClip(new Rectangle(0, Top, w, FieldH));
                foreach (var s in f.sprites)
                {
                    try { Draw(g, s, S, P, centre); } catch (Exception) { }
                }
                // Bambi: 0.2 tall, its feet at (petX, petY)
                {
                    var feet = P(f.petX, f.petY);
                    float bh = 0.2f * S, bw = 0.12f * S;
                    using var body = new SolidBrush(Color.FromArgb(235, 0xE9, 0xC9, 0x9A)); using var ink = new Pen(Ink, 2.5f); using var inkB = new SolidBrush(Ink);
                    var head = new RectangleF(feet.X - bw * 0.5f, feet.Y - bh, bw, bh * 0.62f);
                    var belly = new RectangleF(feet.X - bw * 0.36f, feet.Y - bh * 0.46f, bw * 0.72f, bh * 0.46f);
                    g.FillEllipse(body, belly); g.DrawEllipse(ink, belly);
                    g.FillEllipse(body, head); g.DrawEllipse(ink, head);
                    float look = f.petRight ? 1f : -1f, ey = head.Y + head.Height * 0.55f;
                    g.FillEllipse(inkB, feet.X + look * bw * 0.08f - 3, ey - 3, 6, 6); g.FillEllipse(inkB, feet.X + look * bw * 0.30f - 3, ey - 3, 6, 6);
                    using var small = new Font("Segoe UI", 9f, FontStyle.Bold);
                    string tag = "Bambi" + (f.petMotion.Length > 0 ? " [" + f.petMotion + "]" : f.petPose.Length > 0 && f.petPose != "idle" ? " (" + f.petPose + ")" : "");
                    g.ResetClip();
                    var size = g.MeasureString(tag, small);
                    float lx = Math.Clamp(feet.X - size.Width / 2, 2, w - size.Width - 2), ly = Math.Min(feet.Y + 1, Top + FieldH - size.Height);
                    using var tagBack = new SolidBrush(Color.FromArgb(200, Cream));
                    g.FillRectangle(tagBack, lx, ly, size.Width, size.Height); g.DrawString(tag, small, inkB, lx, ly);
                    if (f.petLine.Length > 0)
                    {
                        using var say = new Font("Segoe UI", 11f, FontStyle.Regular);
                        string line = f.petLine.Length > 60 ? f.petLine[..60] + "…" : f.petLine;
                        var ss = g.MeasureString(line, say);
                        float bx = Math.Clamp(feet.X - ss.Width / 2 - 8, 2, w - ss.Width - 18), by = Math.Max(Top + 2, feet.Y - bh - ss.Height - 14);
                        var bubble = new RectangleF(bx, by, ss.Width + 16, ss.Height + 8);
                        using var path = Round(bubble, 10); using var white = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
                        g.FillPath(white, path); g.DrawPath(ink, path); g.DrawString(line, say, inkB, bx + 8, by + 4);
                    }
                    g.SetClip(new Rectangle(0, Top, w, FieldH));
                }
                // what went out into the room
                foreach (var o in f.flying)
                {
                    var p = P(o.x, o.y);
                    using var red = new Pen(Color.FromArgb(220, 0xD9, 0x4B, 0x3C), 3f) { DashStyle = DashStyle.Dash }; using var redB = new SolidBrush(Color.FromArgb(0xD9, 0x4B, 0x3C));
                    g.DrawEllipse(red, p.X - 26, p.Y - 26, 52, 52);
                    using var small = new Font("Segoe UI", 9f, FontStyle.Bold);
                    g.DrawString("OUT -> at the player", small, redB, Math.Clamp(p.X - 58, 2, w - 124), p.Y + 28);
                }
                // the player: a hand's shadow, and where they point
                if (f.hand)
                {
                    var p = P(f.handX, f.handY); float rr = 0.085f * S;
                    using var shade = new SolidBrush(Color.FromArgb(70, 60, 50, 60)); using var small = new Font("Segoe UI", 9f, FontStyle.Italic); using var grey = new SolidBrush(Color.FromArgb(170, 60, 50, 60));
                    g.FillEllipse(shade, p.X - rr, p.Y - rr, rr * 2, rr * 2); g.DrawString("hand", small, grey, p.X - 14, p.Y - 7);
                }
                if (f.aim)
                {
                    var p = P(f.aimX, f.aimY);
                    using var pen = new Pen(Color.FromArgb(170, 70, 90, 130), 2f); using var small = new Font("Segoe UI", 8f, FontStyle.Italic); using var grey = new SolidBrush(Color.FromArgb(190, 70, 90, 130));
                    g.DrawLine(pen, p.X - 9, p.Y - 9, p.X + 9, p.Y + 9); g.DrawLine(pen, p.X - 9, p.Y + 9, p.X + 9, p.Y - 9); g.DrawString("aim", small, grey, p.X + 8, p.Y + 6);
                }
                g.ResetClip();
                // the frame round the game: what the app itself shows
                using (var strip = new SolidBrush(Color.FromArgb(0xF8, 0xDD, 0xA0))) g.FillRectangle(strip, 0, 0, w, Top);
                using (var hud = new Font("Segoe UI", 13f, FontStyle.Bold)) using (var inkB = new SolidBrush(Ink))
                {
                    g.DrawString($"Score {f.score}    Lives {f.lives}    Level {f.level}", hud, inkB, 10, 10);
                    string mid = f.phase == "over" ? "OVER: " + f.result : f.banner;
                    if (!string.IsNullOrEmpty(mid)) { if (mid.Length > 70) mid = mid[..70] + "…"; var ms = g.MeasureString(mid, hud); g.DrawString(mid, hud, inkB, Math.Max(300, w - ms.Width - 10), 10); }
                }
                using (var foot = new SolidBrush(Ink)) g.FillRectangle(foot, 0, Top + FieldH, w, Bottom);
                using (var cap = new Font("Segoe UI", 10f)) using (var light = new SolidBrush(Cream))
                {
                    string line = $"t = {f.t:0.0} s   {f.player}   {(f.flat ? "FLAT on the desk (seen from above; the player sits at the bottom edge)" : "upright (the floor is the bottom edge)")}   {caption}";
                    g.DrawString(line, cap, light, 8, Top + FieldH + 6);
                }
                using (var edge = new Pen(Ink, 2f)) g.DrawRectangle(edge, 1, Top, w - 2, FieldH - 1);
            }
            using var ms2 = new MemoryStream();
            bmp.Save(ms2, ImageFormat.Png);
            return ms2.ToArray();
        }
        catch (Exception) { return null; }
    }

    static void Draw(Graphics g, GlassSprite s, float S, Func<float, float, PointF> P, StringFormat centre)
    {
        if (!(s.alpha > 0.015f) || !(s.size > 0f) || string.IsNullOrEmpty(s.art)) return;
        var c = P(s.x, s.y);
        float hgt = s.size * S, wid = (s.w > 0f ? s.w : s.size) * S;
        var box = new RectangleF(c.X - wid / 2, c.Y - hgt / 2, wid, hgt);
        switch (s.art)
        {
            case GlassArt.Rect:
            {
                using var b = new SolidBrush(Rgb(s.color, s.alpha, Ink));
                if (Math.Abs(s.rot) < 0.01f) { g.FillRectangle(b, box); break; }
                var state = g.Save();
                g.TranslateTransform(c.X, c.Y); g.RotateTransform(-s.rot);
                g.FillRectangle(b, -wid / 2, -hgt / 2, wid, hgt);
                g.Restore(state);
                break;
            }
            case GlassArt.Paper:
            {
                using var b = new SolidBrush(Rgb(s.color, s.alpha, Cream)); using var path = Round(box, s.fill * Math.Min(wid, hgt));
                g.FillPath(b, path);
                break;
            }
            case GlassArt.Disc: { using var b = new SolidBrush(Rgb(s.color, s.alpha, Ink)); g.FillEllipse(b, box); break; }
            case GlassArt.RingShape: { using var p = new Pen(Rgb(s.color, s.alpha, Ink), Math.Max(2f, 0.012f * S)); g.DrawEllipse(p, box); break; }
            case GlassArt.Label:
            {
                if (string.IsNullOrEmpty(s.text)) break;
                using var font = new Font("Segoe UI", Math.Max(5f, s.size * S * 0.92f), FontStyle.Bold, GraphicsUnit.Pixel); using var b = new SolidBrush(Rgb(s.color, s.alpha, Ink));
                g.DrawString(s.text, font, b, c, centre);
                break;
            }
            case GlassArt.Card:
            {
                var card = new RectangleF(c.X - hgt * 0.38f, c.Y - hgt / 2, hgt * 0.76f, hgt);
                using var path = Round(card, 14); using var paper = new SolidBrush(Color.FromArgb((int)(255 * s.alpha), 255, 255, 255)); using var ink = new Pen(Ink, 3f); using var inkB = new SolidBrush(Ink);
                g.FillPath(paper, path); g.DrawPath(ink, path);
                using var t1 = new Font("Segoe UI", Math.Max(8f, hgt * 0.11f), FontStyle.Bold, GraphicsUnit.Pixel); using var t2 = new Font("Segoe UI", Math.Max(7f, hgt * 0.075f), FontStyle.Regular, GraphicsUnit.Pixel);
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(s.text ?? "", t1, inkB, new RectangleF(card.X + 4, card.Y + hgt * 0.12f, card.Width - 8, hgt * 0.3f), fmt);
                g.DrawString(s.text2 ?? "", t2, inkB, new RectangleF(card.X + 4, card.Y + hgt * 0.45f, card.Width - 8, hgt * 0.45f), fmt);
                if (s.fill > 0.01f) { using var ring = new Pen(Color.FromArgb(0x9F, 0xD8, 0xB4), 5f); g.DrawArc(ring, card.X + 6, card.Y + 6, 26, 26, -90, 360 * Math.Clamp(s.fill, 0, 1)); }
                break;
            }
            case GlassArt.Board: { using var b = new SolidBrush(Color.FromArgb((int)(255 * s.alpha), 0xE9, 0xC9, 0x9A)); using var p = new Pen(Ink, 3f); g.FillRectangle(b, box); g.DrawRectangle(p, box.X, box.Y, box.Width, box.Height); break; }
            case GlassArt.Cell: { using var b = new SolidBrush(Color.FromArgb(90, 0x9F, 0xD8, 0xB4)); g.FillRectangle(b, box); break; }
            default:
            {
                if (s.art.StartsWith(GlassArt.PackPrefix, StringComparison.Ordinal))
                {
                    // a picture of the module's own: painted after the script is accepted - here a dashed box with its name
                    using var p = new Pen(Color.FromArgb((int)(255 * s.alpha), Ink), 2f) { DashStyle = DashStyle.Dash }; using var b = new SolidBrush(Color.FromArgb((int)(150 * s.alpha), 0xC9, 0xB8, 0xF0)); using var inkB = new SolidBrush(Ink);
                    g.FillEllipse(b, box); g.DrawEllipse(p, box);
                    using var small = new Font("Segoe UI", Math.Clamp(hgt * 0.2f, 8f, 13f), FontStyle.Bold, GraphicsUnit.Pixel);
                    g.DrawString("[" + s.art[GlassArt.PackPrefix.Length..] + "]", small, inkB, c, centre);
                    break;
                }
                var pic = Sticker(s.art);
                if (pic != null)
                {
                    float pw = s.w > 0f ? wid : hgt * pic.Width / Math.Max(1, pic.Height);
                    var state = g.Save();
                    g.TranslateTransform(c.X, c.Y); if (Math.Abs(s.rot) > 0.01f) g.RotateTransform(-s.rot);
                    if (s.frame == 1) g.ScaleTransform(-1, 1);
                    using var attr = new ImageAttributes();
                    var m = new ColorMatrix { Matrix33 = Math.Clamp(s.alpha, 0f, 1f) };
                    if (s.color >= 0) { m.Matrix00 = ((s.color >> 16) & 255) / 255f; m.Matrix11 = ((s.color >> 8) & 255) / 255f; m.Matrix22 = (s.color & 255) / 255f; }
                    attr.SetColorMatrix(m);
                    g.DrawImage(pic, new Rectangle((int)(-pw / 2), (int)(-hgt / 2), (int)Math.Max(1, pw), (int)Math.Max(1, hgt)), 0, 0, pic.Width, pic.Height, GraphicsUnit.Pixel, attr);
                    g.Restore(state);
                    break;
                }
                // no picture at hand: a soft shape with the sticker's name
                using var fill = new SolidBrush(Color.FromArgb((int)(200 * s.alpha), 0xF2, 0xC1, 0x4E)); using var edge = new Pen(Color.FromArgb((int)(255 * s.alpha), Ink), 2f); using var inkB2 = new SolidBrush(Color.FromArgb((int)(255 * s.alpha), Ink));
                var sq = new RectangleF(c.X - hgt / 2, c.Y - hgt / 2, hgt, hgt);
                g.FillEllipse(fill, sq); g.DrawEllipse(edge, sq);
                using var tiny = new Font("Segoe UI", Math.Clamp(hgt * 0.24f, 7f, 12f), FontStyle.Bold, GraphicsUnit.Pixel);
                g.DrawString(s.art, tiny, inkB2, c, centre);
                break;
            }
        }
    }
}
