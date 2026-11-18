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
    public static async Task<byte[]> CaptureSpotAsync(string displayId, float u, float v, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("실제 Mac 화면에서 아이콘을 가리켜 주세요.");
        if (!float.IsFinite(u) || !float.IsFinite(v) || u < 0 || u > 1 || v < 0 || v > 1)
            throw new InvalidOperationException("바꾸려는 아이콘을 다시 가리켜 주세요.");
        if (!CGPreflightScreenCaptureAccess() && !CGRequestScreenCaptureAccess())
            throw new InvalidOperationException("Mac 시스템 설정의 개인정보 보호 및 보안에서 화면 기록을 허용한 뒤 밤비를 다시 실행해 주세요.");
        var active = Active();
        uint id;
        if (displayId == "mac-tv")
        {
            var outside = active.Where(d => d.Name == "외부 디스플레이").ToArray();
            if (outside.Length > 1) throw new InvalidOperationException("TV 설정에서 아이콘이 있는 실제 디스플레이를 선택해 주세요.");
            id = outside.Length == 1 ? uint.Parse(outside[0].Id[4..]) : CGMainDisplayID();
        }
        else if (!active.Any(d => d.Id == displayId) || !uint.TryParse(displayId[4..], out id))
            throw new InvalidOperationException("선택한 TV가 연결되어 있는지 확인해 주세요.");
        var bounds = CGDisplayBounds(id);
        int width = (int)Math.Floor(bounds.size.width), height = (int)Math.Floor(bounds.size.height);
        if (width < 100 || height < 100) throw new InvalidOperationException("Mac 화면의 크기를 확인할 수 없어요.");
        int size = Math.Clamp(Math.Min(width,height)/6,96,260);
        int x = (int)Math.Round(bounds.x) + Math.Clamp((int)Math.Round(u * width) - size/2, 0, width-size);
        int y = (int)Math.Round(bounds.y) + Math.Clamp((int)Math.Round(v * height) - size/2, 0, height-size);
        string directory = Directory.CreateTempSubdirectory("bambi-icon-").FullName;
        string file = Path.Combine(directory,"spot.png");
        try
        {
            var start = new ProcessStartInfo("/usr/sbin/screencapture")
            { UseShellExecute=false, RedirectStandardError=true, ArgumentList={"-x","-t","png","-R",$"{x},{y},{size},{size}",file} };
            using var request = Process.Start(start);
            if (request == null) throw new InvalidOperationException("화면을 캡처할 수 없어요.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await request.WaitForExitAsync(timeout.Token); }
            catch(OperationCanceledException) { if (!request.HasExited) request.Kill(); throw; }
            if (request.ExitCode != 0 || !File.Exists(file)) throw new InvalidOperationException("Mac에서 가리킨 영역을 캡처할 수 없어요. 화면 기록 권한을 확인해 주세요.");
            var bytes = await File.ReadAllBytesAsync(file,ct);
            if (bytes.Length < 24 || bytes.Length > 8*1024*1024 || bytes[0]!=137 || bytes[1]!=80)
                throw new InvalidOperationException("화면 영역의 PNG를 읽을 수 없어요.");
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
            return new Display("mac-" + id, CGDisplayIsBuiltin(id) == 0 ? "외부 디스플레이" : "MacBook 내장 화면",
                (int)CGDisplayPixelsWide(id), (int)CGDisplayPixelsHigh(id), size.width, size.height);
        }).ToArray();
    }
}
