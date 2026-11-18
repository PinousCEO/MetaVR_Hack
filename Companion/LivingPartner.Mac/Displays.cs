using System.Runtime.InteropServices;
using System.Diagnostics;

namespace LivingPartner.Mac;

record Display(string Id, string Name, int Width, int Height, double WidthMm, double HeightMm);

static class Displays
{
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [StructLayout(LayoutKind.Sequential)]
    struct Size { public double width, height; }
    [StructLayout(LayoutKind.Sequential)]
    struct Bounds { public double x, y; public Size size; }
    [DllImport(CoreGraphics)] static extern int CGGetActiveDisplayList(uint count, [Out] uint[] displays, out uint found);
    [DllImport(CoreGraphics)] static extern Size CGDisplayScreenSize(uint id);
    [DllImport(CoreGraphics)] static extern nuint CGDisplayPixelsWide(uint id);
    [DllImport(CoreGraphics)] static extern nuint CGDisplayPixelsHigh(uint id);
    [DllImport(CoreGraphics)] static extern uint CGDisplayIsBuiltin(uint id);
    [DllImport(CoreGraphics)] static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] static extern Bounds CGDisplayBounds(uint id);
    [DllImport(CoreGraphics)][return: MarshalAs(UnmanagedType.I1)] static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(CoreGraphics)][return: MarshalAs(UnmanagedType.I1)] static extern bool CGRequestScreenCaptureAccess();

    // Capture a small rectangle in global Quartz display points; Retina and negative screen origins are supported.
    public static async Task<byte[]> CaptureSpotAsync(string displayId, float u, float v, CancellationToken ct, bool wholeDisplay = false)
    {
        if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("Allow Screen Recording on your Mac to use this feature.");
        if (!float.IsFinite(u) || !float.IsFinite(v) || u < 0 || u > 1 || v < 0 || v > 1)
            throw new InvalidOperationException("Point at the icon you want to change.");
        if (!CGPreflightScreenCaptureAccess() && !CGRequestScreenCaptureAccess())
            throw new InvalidOperationException("Allow Screen Recording in System Settings → Privacy & Security, then reopen Yonder.");
        var active = Active();
        uint id;
        if (displayId == "mac-tv")
        {
            var outside = active.Where(d => d.Name == "External display").ToArray();
            if (outside.Length > 1) throw new InvalidOperationException("Select a display in TV settings first.");
            id = outside.Length == 1 ? uint.Parse(outside[0].Id[4..]) : CGMainDisplayID();
        }
        else if (!active.Any(d => d.Id == displayId) || !uint.TryParse(displayId[4..], out id))
            throw new InvalidOperationException("Check that the selected TV is connected.");
        var bounds = CGDisplayBounds(id);
        int width = (int)Math.Floor(bounds.size.width), height = (int)Math.Floor(bounds.size.height);
        if (width < 100 || height < 100) throw new InvalidOperationException("Unable to read the display dimensions.");
        int size = Math.Clamp(Math.Min(width,height)/6,96,260);
        int x = (int)Math.Round(bounds.x) + Math.Clamp((int)Math.Round(u * width) - size/2, 0, width-size);
        int y = (int)Math.Round(bounds.y) + Math.Clamp((int)Math.Round(v * height) - size/2, 0, height-size);
        if (wholeDisplay) { x = (int)Math.Round(bounds.x); y = (int)Math.Round(bounds.y); }
        else { width = size; height = size; }
        string directory = Directory.CreateTempSubdirectory("bambi-screen-").FullName;
        string file = Path.Combine(directory,"spot.png");
        try
        {
            var start = new ProcessStartInfo("/usr/sbin/screencapture")
            { UseShellExecute=false, RedirectStandardError=true, ArgumentList={"-x","-t","png","-R",$"{x},{y},{width},{height}",file} };
            using var request = Process.Start(start);
            if (request == null) throw new InvalidOperationException("Unable to capture the display.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await request.WaitForExitAsync(timeout.Token); }
            catch(OperationCanceledException) { if (!request.HasExited) request.Kill(); throw; }
            if (request.ExitCode != 0 || !File.Exists(file)) throw new InvalidOperationException("Unable to capture that area. Check Screen Recording permission.");
            var bytes = await File.ReadAllBytesAsync(file,ct);
            if (bytes.Length < 24 || bytes.Length > 8*1024*1024 || bytes[0]!=137 || bytes[1]!=80)
                throw new InvalidOperationException("Unable to read the captured image.");
            return bytes;
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }

    public static Display[] Active()
    {
        if (!OperatingSystem.IsMacOS()) return Array.Empty<Display>();
        var ids = new uint[32];
        if (CGGetActiveDisplayList((uint)ids.Length, ids, out uint count) != 0) return Array.Empty<Display>();
        return ids.Take((int)count).Select(id =>
        {
            var size = CGDisplayScreenSize(id);
            return new Display("mac-" + id, CGDisplayIsBuiltin(id) == 0 ? "External display" : "Built-in MacBook display",
                (int)CGDisplayPixelsWide(id), (int)CGDisplayPixelsHigh(id), size.width, size.height);
        }).ToArray();
    }
}
