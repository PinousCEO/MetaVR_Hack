using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LivingPartner.Host;

/// <summary>Makes a drawn icon ready for the headset: a see-through background and a size a badge needs.</summary>
public static class IconArt
{
    /// <summary>The picture file as a size x size PNG with a transparent background. Throws when the file is not a picture.</summary>
    public static byte[] Finish(byte[] file, int size)
    {
        {
            using var loaded = new Bitmap(new MemoryStream(file));
            int w = loaded.Width, h = loaded.Height;
            using var src = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(src)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(loaded, new Rectangle(0, 0, w, h)); }
            var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var px = new int[w * h];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
                if (CutBackground(px, w, h)) System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
            }
            finally { src.UnlockBits(data); }
            using var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            using (var wrap = new ImageAttributes())
            {
                wrap.SetWrapMode(WrapMode.TileFlipXY);      // without it the outermost pixels are blended with nothing and the edge goes dark
                g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, w, h, GraphicsUnit.Pixel, wrap);
            }
            using var ms = new MemoryStream();
            dst.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Has this finished icon (a PNG with a see-through background) a round plate behind the object, or a ring round
    /// it? Told by its outline (IconCheck.LooksRound). False for a file that is not a picture.
    /// </summary>
    public static bool RoundPlate(byte[] png) => RoundPlate(png, out _, out _);

    /// <summary>
    /// For `--check-icons`: one line per PNG of a folder (the pictures the repair would look at: item-*, skill-*,
    /// market-*) saying whether its outline is round, and a count at the end. Reads only. Returns 0, or 1 when the
    /// folder cannot be read.
    /// </summary>
    public static int Report(string folder, string outFile)
    {
        var lines = new List<string>();
        int seen = 0, round = 0;
        try
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*.png").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.GetFileName(path);
                if (!(file.StartsWith("item-", StringComparison.Ordinal) || file.StartsWith("skill-", StringComparison.Ordinal) || file.StartsWith("market-", StringComparison.Ordinal))) continue;
                bool plate = RoundPlate(File.ReadAllBytes(path), out float spread, out float offSquare);
                seen++; if (plate) round++;
                lines.Add($"{(plate ? "ROUND" : "ok   ")} {file}  radii differ by {spread * 100:0.0}%, box off square by {offSquare * 100:0.0}%");
            }
        }
        catch (Exception e) { lines.Add("the folder could not be read: " + e.Message); File.WriteAllLines(outFile, lines); return 1; }
        lines.Add($"{round} of {seen} picture(s) have a round outline (a plate or ring behind the object, or a thing that is round)");
        File.WriteAllLines(outFile, lines);
        return 0;
    }

    public static bool RoundPlate(byte[] png, out float spread, out float offSquare)
    {
        spread = 1f; offSquare = 1f;
        try
        {
            using var loaded = new Bitmap(new MemoryStream(png));
            int w = loaded.Width, h = loaded.Height;
            using var src = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(src)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(loaded, new Rectangle(0, 0, w, h)); }
            var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[w * h];
            try { System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length); }
            finally { src.UnlockBits(data); }
            var alpha = new byte[w * h];
            for (int i = 0; i < px.Length; i++) alpha[i] = (byte)(px[i] >>> 24);
            return IconCheck.LooksRound(alpha, w, h, out spread, out offSquare);
        }
        catch { return false; }
    }

    /// <summary>
    /// When the picture came on a plain background instead of a see-through one (the image model refused transparency),
    /// that background is taken away: everything of its colour that can be reached from the border, and the larger
    /// patches of exactly that colour which the drawing closes in. The sticker's dark outline stops the flood, so the
    /// light fills inside it stay. Returns false when there is nothing to cut.
    /// </summary>
    static bool CutBackground(int[] px, int w, int h)
    {
        int[] corners = { px[0], px[w - 1], px[(h - 1) * w], px[h * w - 1] };
        if (corners.Any(c => (c >>> 24) < 250)) return false;                       // already see-through
        int r = (int)corners.Average(c => (c >> 16) & 255), g = (int)corners.Average(c => (c >> 8) & 255), b = (int)corners.Average(c => c & 255);
        int Far(int c) => Math.Max(Math.Abs(((c >> 16) & 255) - r), Math.Max(Math.Abs(((c >> 8) & 255) - g), Math.Abs((c & 255) - b)));
        const int Tol = 34;
        if (corners.Any(c => Far(c) > Tol)) return false;                           // the corners do not agree: no plain background

        var gone = new bool[w * h];
        var todo = new Stack<int>();
        void Try(int i) { if (!gone[i] && Far(px[i]) <= Tol) { gone[i] = true; todo.Push(i); } }
        for (int x = 0; x < w; x++) { Try(x); Try((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Try(y * w); Try(y * w + w - 1); }
        int n = 0;
        while (todo.Count > 0)
        {
            int i = todo.Pop(); n++;
            int x = i % w, y = i / w;
            if (x > 0) Try(i - 1);
            if (x < w - 1) Try(i + 1);
            if (y > 0) Try(i - w);
            if (y < h - 1) Try(i + w);
        }
        if (n < w * h / 50) return false;                                           // hardly any: that was not a background
        // What the flood cannot reach: gaps the drawing closes in (between the slats of a chair's back, inside a handle).
        // They are exactly the background's colour - far closer to it than any fill, the image model having been told
        // to paint nothing of the object in that colour - and they are not small. Both are asked of a patch before it
        // goes, so that a little highlight on the object stays.
        const int Exact = 8;
        int least = Math.Max(64, w * h / 700);
        var seen = new bool[w * h];
        var patch = new List<int>();
        void Next(int k) { if (!gone[k] && !seen[k] && Far(px[k]) <= Exact) { seen[k] = true; todo.Push(k); } }
        for (int start = 0; start < px.Length; start++)
        {
            if (gone[start] || seen[start] || Far(px[start]) > Exact) continue;
            patch.Clear(); seen[start] = true; todo.Push(start);
            while (todo.Count > 0)
            {
                int i = todo.Pop(); patch.Add(i);
                int x = i % w, y = i / w;
                if (x > 0) Next(i - 1);
                if (x < w - 1) Next(i + 1);
                if (y > 0) Next(i - w);
                if (y < h - 1) Next(i + w);
            }
            if (patch.Count >= least) foreach (int i in patch) gone[i] = true;
        }
        for (int i = 0; i < px.Length; i++)
        {
            if (gone[i]) { px[i] = 0; continue; }
            int x = i % w, y = i / w;
            bool edge = (x > 0 && gone[i - 1]) || (x < w - 1 && gone[i + 1]) || (y > 0 && gone[i - w]) || (y < h - 1 && gone[i + w]);
            if (!edge) continue;
            // the rim between drawing and background is a blend of both: it keeps as much of itself as it differs from the background
            int far = Far(px[i]);
            if (far < Tol * 3) px[i] = (px[i] & 0x00FFFFFF) | ((far * 255 / (Tol * 3)) << 24);
        }
        return true;
    }
}
