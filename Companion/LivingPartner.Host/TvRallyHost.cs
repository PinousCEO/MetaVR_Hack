using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>Brings the Quest-hosted second screen onto the Windows display the player chose.</summary>
public sealed class TvRallyHost
{
    readonly Core _core;
    string _headset = "", _lastUrl = "";
    MonitorDesc _chosen;
    double _requested;
    public TvRallyHost(Core core) { _core = core; }
    public bool Active => _lastUrl.Length > 0 && _core.Clients.Values.Any(c => c.Authed && c.Role == Roles.Quest && c.ClientId == _headset);

    public (bool ok, string line) Ask(Client source, Utterance utterance, string command, bool ko)
    {
        var quest = source.Authed && source.Role == Roles.Quest ? source
            : _core.Clients.Values.FirstOrDefault(c => c.Authed && c.Role == Roles.Quest && c.ClientId == _headset)
            ?? _core.Clients.Values.FirstOrDefault(c => c.Authed && c.Role == Roles.Quest);
        if (quest == null) return (false, ko ? "Quest 앱을 열고 이 컴퓨터에 연결해 줘." : "Open the Quest app and connect it to this computer.");
        if (command == "stop")
        {
            _core.Send(quest, Msg.IntentResult, new IntentResultMsg { kind = "stopTvRally" });
            _lastUrl = ""; _chosen = null; _requested = 0;
            return (true, ko ? "TV 랠리를 끝낼게." : "Ending TV Rally.");
        }
        _chosen = _core.PcDo.OperateDisplay(quest, utterance).m;
        _headset = quest.ClientId; _lastUrl = ""; _requested = HostClock.Now;
        _core.Send(quest, Msg.IntentResult, new IntentResultMsg { kind = "playGame", text = "game-tv-rally" });
        return (true, ko ? "TV 랠리를 열게. TV가 연결되면 오른손 엄지와 검지를 맞대 줘." : "Opening TV Rally. Pinch your right thumb and index finger once the TV is connected.");
    }

    public void Session(Client source, string address)
    {
        if (!source.Authed || source.Role != Roles.Quest || address == null) return;
        if (address.Length == 0)
        {
            if (_headset == source.ClientId) { _lastUrl = ""; _chosen = null; _requested = 0; }
            return;
        }
        if (!TvRallyAddress.FromHeadset(address, source.Remote, source.Loopback, out Uri uri)) { Log.Warn("TV Rally: ignored an unexpected display address."); return; }
        if (source.ClientId == _headset && _lastUrl == uri.AbsoluteUri) return;
        var display = _headset == source.ClientId && HostClock.Now - _requested < 30 ? _chosen : null;
        display ??= _core.PcDo.OperateDisplay(source, null).m;
        _headset = source.ClientId; _lastUrl = uri.AbsoluteUri; _requested = 0;
        if (_core.NoAutoOpen || HostPaths.Trial) { Log.Info("TV Rally: display is ready; automatic opening disabled."); return; }
        _ = Task.Run(() => Open(uri, display));
    }

    void Open(Uri uri, MonitorDesc display)
    {
        try
        {
            var browser = WindowsShell.DefaultBrowserExe();
            string name = Path.GetFileNameWithoutExtension(browser).ToLowerInvariant();
            var start = new ProcessStartInfo { FileName = browser, UseShellExecute = false };
            if (name is "msedge" or "chrome" or "brave" or "vivaldi" or "chromium")
            {
                start.ArgumentList.Add("--app=" + uri.AbsoluteUri);
                start.ArgumentList.Add("--new-window");
                if (display != null && !display.work.IsEmpty)
                {
                    var r = display.work;
                    start.ArgumentList.Add($"--window-position={r.x},{r.y}");
                    start.ArgumentList.Add($"--window-size={r.w},{r.h}");
                }
            }
            else if (name == "firefox") { start.ArgumentList.Add("-new-window"); start.ArgumentList.Add(uri.AbsoluteUri); }
            else { start = new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }; }

            var before = BrowserWindows(gameOnly: false);
            using var launched = Process.Start(start);
            if (display != null && !display.work.IsEmpty)
            {
                // Browser processes often hand the launch to an existing process. Move only the new game window.
                var r = display.work;
                for (int i = 0; i < 40; i++)
                {
                    Thread.Sleep(150);
                    IntPtr window = BrowserWindows(gameOnly: true).FirstOrDefault(h => !before.Contains(h));
                    if (window == IntPtr.Zero) continue;
                    Win32.SetWindowPos(window, IntPtr.Zero, r.x, r.y, r.w, r.h, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                    break;
                }
            }
            Log.Info("TV Rally: opened the family game display.");
        }
        catch (Exception e)
        {
            Log.Warn("TV Rally: could not open the browser: " + e.GetType().Name);
            _core.Post(() => _core.Talk.Say("TV 브라우저에 헤드셋에 표시된 주소를 입력해 줘.", "neutral", 6f, "TV Rally display"));
        }
    }

    static HashSet<IntPtr> BrowserWindows(bool gameOnly)
    {
        var result = new HashSet<IntPtr>();
        Win32.EnumWindows((h, _) =>
        {
            if (Win32.IsWindowVisible(h) && Win32.WindowClass(h) is ("Chrome_WidgetWin_1" or "MozillaWindowClass")
                && (!gameOnly || Win32.WindowTitle(h).StartsWith("TV Rally · 밤비", StringComparison.Ordinal))) result.Add(h);
            return true;
        }, IntPtr.Zero);
        return result;
    }
}

public static class TvRallyAddress
{
    public static bool FromHeadset(string address, string remote, bool localSocket, out Uri uri)
    {
        uri = null;
        if (address == null || address.Length > 112 || !Uri.TryCreate(address, UriKind.Absolute, out var value) ||
            value.Scheme != "http" || value.UserInfo.Length > 0 || value.Query.Length > 0 || value.Fragment.Length > 0 ||
            value.Port < 1024 || !IPAddress.TryParse(value.Host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            value.AbsolutePath.Length != 7 || !value.AbsolutePath.Skip(1).All(c => c >= '0' && c <= '9') ||
            !IPAddress.TryParse(remote, out var peer)) return false;
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
        byte[] octets = ip.GetAddressBytes();
        // A LAN socket vouches for its peer. A USB-forwarded socket names loopback, so accept only a local network address.
        bool homeNetwork = octets[0] == 10 || octets[0] == 192 && octets[1] == 168 || octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31;
        if (!(localSocket && IPAddress.IsLoopback(peer) ? homeNetwork : ip.Equals(peer))) return false;
        uri = value; return true;
    }
}
