using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// Monitor and window tracking in the logged-in user's session (read-only Win32 queries, physical pixels) plus
/// permitted window capture. Maps requested portal points to Windows pixels and to the window that owns them.
/// </summary>
public sealed class Desktop
{
    readonly Core _core;
    public List<MonitorDesc> Monitors { get; private set; } = new();
    double _nextPoll;
    readonly uint _pid = Win32.GetCurrentProcessId();

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    public Desktop(Core core) { _core = core; Monitors = Win32.EnumerateMonitors(); LogMonitors("initial"); }

    void LogMonitors(string why)
    {
        Log.Info($"monitors ({why}): " + string.Join(" | ", Monitors.Select(m => $"{m.id} {m.bounds} work={m.work} dpi={m.dpiScale:0.00} rot={m.rotation} phys={m.physWidthM * 1000:0}x{m.physHeightM * 1000:0}mm{(m.primary ? " primary" : "")}")));
    }

    /// <summary>The ordinary application window the user was last working in (never the desktop, the taskbar or the partner's own windows).</summary>
    public IntPtr LastWorkspace { get; private set; }

    public void Tick(double now)
    {
        if (now < _nextPoll) return;
        _nextPoll = now + 1.0;
        IntPtr fg = Win32.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != LastWorkspace && Win32.IsWorkspaceCandidate(fg, (uint)Environment.ProcessId) && Win32.WindowClass(fg) != "UnityWndClass") LastWorkspace = fg;
        var list = Win32.EnumerateMonitors();
        bool changed = list.Count != Monitors.Count;
        if (!changed)
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i]; var b = Monitors[i];
                if (a.id != b.id || a.bounds.x != b.bounds.x || a.bounds.y != b.bounds.y || a.bounds.w != b.bounds.w || a.bounds.h != b.bounds.h ||
                    a.work.h != b.work.h || a.work.w != b.work.w || Math.Abs(a.dpiScale - b.dpiScale) > 0.001f || a.rotation != b.rotation) { changed = true; break; }
            }
        if (!changed) return;
        Monitors = list;
        LogMonitors("changed");
        // a calibration only stays valid for the display mode it was measured with
        int removed = _core.State.calibrations.RemoveAll(c => { var m = ScreenMath.FindMonitor(list, c.monitorId); return m == null ? false : (m.bounds.w != c.pixelW || m.bounds.h != c.pixelH); });
        if (removed > 0) { Log.Warn($"{removed} calibration(s) invalidated by a display mode change"); _core.Say("The display settings changed, so the screens need calibrating again.", "worried", 6, "display mode changed"); }
        _core.MarkDirty();
    }

    public MonitorCalibration CalibrationFor(MonitorDesc m) =>
        _core.State.calibrations.Where(c => c.monitorId == m.id && c.pixelW == m.bounds.w && c.pixelH == m.bounds.h).OrderBy(c => c.simulated ? 1 : 0).FirstOrDefault();

    public float MetersPerPixel(MonitorDesc m, out string source)
    {
        ScreenMath.PhysicalSize(m, CalibrationFor(m), out float w, out float _, out source);
        return w / m.bounds.w;
    }

    /// <summary>Resolves a requested point to a validated portal target (monitor, pixel, owning window, floor).</summary>
    public TargetInfo Resolve(TargetQuery q, bool enterWindow = false)
    {
        var t = new TargetInfo { queryId = q.queryId, resolvedAt = HostClock.Now };
        var mons = Monitors;
        MonitorDesc m; float px, py;
        if (q.usePixel) { px = q.px; py = q.py; m = ScreenMath.FindMonitor(mons, px, py); }
        else { m = ScreenMath.FindMonitor(mons, q.monitorId); if (m != null) ScreenMath.UVToPixel(m, q.u, q.v, out px, out py); else { px = py = 0; } }
        if (m == null && q.usePixel && mons.Count > 0)
        {
            // off every display: use the nearest one, and say so
            m = mons.OrderBy(o => Dist(o.bounds, px, py)).First();
            t.adjusted = true; t.reason = "that point is outside every display";
        }
        if (m == null) { t.valid = false; t.reason = "unknown display"; return t; }
        t.reqPxX = px; t.reqPxY = py;
        float H = _core.State.character.heightM;
        float mpp = MetersPerPixel(m, out string src);
        t.metersPerPixel = mpp; t.sizeSource = src; t.monitorId = m.id; t.bottomEdgePxY = m.bounds.Bottom; t.topEdgePxY = m.bounds.y;
        float clearPx = PlanBuilder.ClearanceRadius(H) / mpp;
        if (!m.bounds.Contains(px, py))
        {
            px = Math.Clamp(px, m.bounds.x, m.bounds.Right - 1); py = Math.Clamp(py, m.bounds.y, m.bounds.Bottom - 1);
            t.adjusted = true; t.reason = "that point is off the lit screen area";
        }

        RectI allowed; float floor;
        if (q.direction == 0)
        {
            IntPtr hwnd = enterWindow ? Win32.WorkspaceWindowAt((int)px, (int)py, _pid) : IntPtr.Zero;
            RectI wr = default;
            bool win = hwnd != IntPtr.Zero && Win32.FrameBounds(hwnd, out wr);
            if (win)
            {
                var vis = RectI.Intersect(wr, m.bounds);
                if (vis.w < clearPx * 2.2f || vis.h < clearPx * 2 + 0.8f * H / mpp) win = false; // too small to climb into: treat as a screen point
                else
                {
                    t.anchorKind = "window"; t.hwnd = hwnd.ToInt64(); t.windowTitle = Win32.WindowTitle(hwnd); t.windowRect = wr;
                    try { Win32.GetWindowThreadProcessId(hwnd, out uint pid); t.processName = Process.GetProcessById((int)pid).ProcessName; } catch { }
                    allowed = vis; floor = Math.Min(wr.Bottom - 4, m.bounds.Bottom);
                    return Finish(t, m, allowed, floor, clearPx, px, py, H, mpp, wr);
                }
            }
            t.anchorKind = "screen"; allowed = m.work; floor = m.work.Bottom;
            return Finish(t, m, allowed, floor, clearPx, px, py, H, mpp, default);
        }
        else
        {
            var pc = _core.Pc;
            if (pc == null) { t.valid = false; t.reason = "the Windows character client is not running"; return t; }
            if (pc.monitorId != m.id)
            {
                var pm = ScreenMath.FindMonitor(mons, pc.monitorId);
                if (pm == null) { t.valid = false; t.reason = "the partner's display is unknown"; return t; }
                // exits are offered on the display the partner is actually on; a different display is never silently substituted
                ScreenMath.PixelToUV(m, px, py, out float u, out float v);
                m = pm; ScreenMath.UVToPixel(m, u, v, out px, out py);
                mpp = MetersPerPixel(m, out src); t.metersPerPixel = mpp; t.sizeSource = src; t.monitorId = m.id; t.bottomEdgePxY = m.bounds.Bottom; t.topEdgePxY = m.bounds.y;
                clearPx = PlanBuilder.ClearanceRadius(H) / mpp;
                t.adjusted = true; t.reason = "the partner is on another display; showing the matching exit on its display";
            }
            t.anchorKind = "screen"; allowed = m.work; floor = pc.pxY > m.bounds.y ? pc.pxY : m.work.Bottom;
            return Finish(t, m, allowed, floor, clearPx, px, py, H, mpp, default);
        }
    }

    TargetInfo Finish(TargetInfo t, MonitorDesc m, RectI allowed, float floor, float clearPx, float px, float py, float H, float mpp, RectI wr)
    {
        // the body centre must pass high enough above the surface it lands on / leaves from to turn upright (0.56 H)
        float maxY = floor - 0.56f * H / mpp;
        var a = allowed;
        if (a.Bottom - clearPx > maxY) a = new RectI(a.x, a.y, a.w, Math.Max(1, (int)(maxY + clearPx) - a.y));
        // and far enough below the top of the lit area for the body to stay on screen as it comes through
        int topInset = (int)(0.8f * H / mpp - clearPx);
        if (topInset > 0) { int ny = a.y + topInset; a = new RectI(a.x, ny, a.w, Math.Max(1, a.Bottom - ny)); }
        if (ScreenMath.ClampPortal(a, clearPx, px, py, out float ox, out float oy))
        {
            t.adjusted = true;
            if (string.IsNullOrEmpty(t.reason)) t.reason = t.anchorKind == "window" ? "not enough room at that spot in the window; nearest point with room" : "not enough room near the screen edge; nearest point with room";
        }
        t.pxX = ox; t.pxY = oy; t.floorPxY = floor;
        ScreenMath.PixelToUV(m, ox, oy, out t.u, out t.v);
        if (t.anchorKind == "window" && wr.w > 0) ScreenMath.PixelToClient(wr, ox, oy, out _, out _, out t.winU, out t.winV);
        t.valid = a.w > clearPx * 2 && a.h > clearPx * 1.5f;
        if (!t.valid) t.reason = "there is no room for the partner to pass there";
        return t;
    }

    static float Dist(RectI r, float px, float py)
    {
        float dx = Math.Max(Math.Max(r.x - px, 0), px - r.Right), dy = Math.Max(Math.Max(r.y - py, 0), py - r.Bottom);
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// Captures one permitted window (the one the user pointed at / bound as workspace) to a PNG. Returns null when the
    /// window cannot be captured (minimised, protected content, closed). Nothing else on the desktop is captured.
    /// </summary>
    public string CaptureWindow(IntPtr hwnd, out RectI rect, out string error)
    {
        rect = default; error = "";
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd)) { error = "window no longer exists"; return null; }
        if (Win32.IsIconic(hwnd)) { error = "window is minimised"; return null; }
        if (!Win32.FrameBounds(hwnd, out rect) || rect.w < 16 || rect.h < 16) { error = "window has no visible area"; return null; }
        try
        {
            Win32.GetWindowRect(hwnd, out var full);
            int w = full.right - full.left, h = full.bottom - full.top;
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bool ok;
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                ok = PrintWindow(hwnd, dc, 2 /* PW_RENDERFULLCONTENT */);
                g.ReleaseHdc(dc);
            }
            if (!ok) { error = "the window refused capture (possibly protected content)"; return null; }
            // crop the invisible resize border so pixel coordinates match the visible frame
            var crop = new Rectangle(rect.x - full.left, rect.y - full.top, Math.Min(rect.w, w), Math.Min(rect.h, h));
            using var cropped = bmp.Clone(crop, PixelFormat.Format32bppArgb);
            if (IsBlank(cropped)) { error = "capture came back blank (protected or hardware-overlay content)"; return null; }
            string path = Path.Combine(HostPaths.Captures, $"cap-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            // keep images small enough for analysis and avoid hoarding desktop pixels
            if (cropped.Width > 1600)
            {
                int nh = (int)(cropped.Height * (1600.0 / cropped.Width));
                using var small = new Bitmap(cropped, new Size(1600, nh));
                small.Save(path, ImageFormat.Png);
            }
            else cropped.Save(path, ImageFormat.Png);
            foreach (var old in new DirectoryInfo(HostPaths.Captures).GetFiles("cap-*.png").OrderByDescending(f => f.CreationTimeUtc).Skip(12)) { try { old.Delete(); } catch { } }
            return path;
        }
        catch (Exception e) { error = e.Message; return null; }
    }

    static bool IsBlank(Bitmap b)
    {
        int first = b.GetPixel(b.Width / 2, b.Height / 2).ToArgb(); int same = 0, n = 0;
        for (int y = 4; y < b.Height; y += Math.Max(1, b.Height / 24))
            for (int x = 4; x < b.Width; x += Math.Max(1, b.Width / 24)) { n++; if (b.GetPixel(x, y).ToArgb() == first) same++; }
        return n > 0 && same == n && (first & 0xFFFFFF) == 0;
    }
}
