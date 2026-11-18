using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// A display of the PC brought into the room: "1번 화면 가져와 봐", "이 화면 여기로 가져와". While the board is open the
/// display is captured every two seconds; the newest picture is kept in memory only (never written to disk) and handed
/// to the headset at /screen/&lt;revision&gt;.jpg. The snapshot's `screen` says which display is shown and counts up
/// whenever the picture has changed enough to be worth fetching again.
/// A whole display is captured here rather than one window (the only other place is a look at what the user points
/// at, Look.cs: one picture for one question), so it only ever happens because the user asked for exactly that, it
/// ends when they say so, and it ends by itself after ten minutes.
/// State is touched on the core thread; the capture itself runs on the thread pool and posts its result back.
/// </summary>
public sealed class ScreenShare
{
    const double RefreshSeconds = 2, MaxSeconds = 600;
    const int MaxWidth = 1600, SigW = 64, SigH = 36;
    const long JpegQuality = 80;

    readonly Core _core;
    readonly ScreenBoard _board = new();
    string _target = "";            // the display being captured ("" = none); the board names it once the first picture is there
    byte[] _jpeg, _sig;             // the newest picture, and a tiny grey copy of it to tell whether the next one differs
    double _openedAt, _nextShot;
    bool _shooting; int _session;   // _session: goes up with every open and close, so that a capture that comes back late is dropped
    string _captionTask = "";       // the review whose summary becomes the board's caption
    double _failLogged = -1000;

    public ScreenShare(Core core) { _core = core; }

    /// <summary>What the snapshot carries.</summary>
    public ScreenBoard Board => _board;
    /// <summary>The newest picture as a JPEG (null when no board is open). Read by the web thread.</summary>
    public byte[] Jpeg => Volatile.Read(ref _jpeg);
    public object Diagnostics => new { display = _target, revision = _board.revision, kiloBytes = (Jpeg?.Length ?? 0) / 1024, caption = _board.caption };
    /// <summary>The display that is shown in the room as a board ("" = none).</summary>
    public string Shown => _target;

    /// <summary>
    /// Something has just been done on the PC (PcActions: a page refreshed, a program opened): the board shows the
    /// result - the next picture is taken `seconds` from now instead of at the two-second pace, and one more after it
    /// (a page that loads slowly). Nothing when no board is open.
    /// </summary>
    public void Nudge(double seconds)
    {
        if (_target.Length == 0) return;
        double at = HostClock.Now + Math.Max(0.2, seconds);
        if (at < _nextShot || _nextShot - HostClock.Now > RefreshSeconds) _nextShot = at;
        _nextShot = Math.Min(_nextShot, at);
        _again = at + 1.2;
    }
    double _again = -1;

    /// <summary>The board is put away ("화면 닫아 줘", "이 MR 환경 지워 줘"). false when none was out.</summary>
    public bool CloseBoard(string why)
    {
        bool was = _target.Length > 0 || _board.monitorId.Length > 0;
        Close(why);
        return was;
    }

    bool Ko => _core.Talk.LastLanguage == "Korean";
    string Tell(string ko, string en, string mood = "happy", float seconds = 4)
    {
        string line = Ko ? ko : en;
        _core.Say(line, mood, seconds, "world: screen");
        return line;
    }

    /// <summary>
    /// what: "1" | "2" | "3" (the display's number), "this" (the one pointed or looked at), "close" - with " analyze"
    /// after it when the partner should also look at what is on it. words: what the user said ("" when the request came
    /// from a button or from lpctl: then nothing is said unless something is wrong). Returns the line said.
    /// </summary>
    public string Bring(Client c, string what, string words, PointerSample pt)
    {
        string t = (what ?? "").Trim().ToLowerInvariant();
        words ??= "";
        bool spoken = words.Length > 0;
        bool analyse = Regex.IsMatch(t, @"analy|review|분석|피드백") || Regex.IsMatch(words, @"분석|피드백|리뷰|평가|어때|analy[sz]|review|feedback|what do you think", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*(and\s+)?(analy[sz]e|review|분석|피드백).*$", "").Trim();
        Log.Info($"screen: '{what}' from {(c?.Role ?? "the web page")}{(spoken ? " (spoken)" : "")}{(analyse ? " + analyse" : "")}");
        // "close" - or, when the words came as they were said, a sentence that plainly puts the board away and names no display
        if (t is "close" or "off" or "stop" or "none" || (!Regex.IsMatch(t, @"\d") && Regex.IsMatch(t, @"치워|닫아|닫어|없애|집어\s*넣|close|put .*away|take .*away|hide")))
        {
            bool was = _target.Length > 0;
            Close("asked to");
            return !spoken ? "" : was ? Tell("화면 치웠어.", "I've put the screen away.", "neutral") : Tell("지금은 가져온 화면이 없어.", "There's no screen out right now.", "neutral");
        }
        var mons = _core.Desktop.Monitors;
        var m = Pick(c, t, pt, out int number);
        if (m == null) return Tell(mons.Count == 1 ? "화면은 하나뿐이야." : $"그런 화면은 없어. 화면은 {mons.Count}개 있어.", mons.Count == 1 ? "There's only one display." : $"There's no such display. I can see {mons.Count}.", "worried");

        _session++;
        bool same = _target == m.id && _board.monitorId == m.id;
        _target = m.id; _openedAt = HostClock.Now; _nextShot = 0; _captionTask = "";
        if (!same)
        {
            // the board shows nothing until the first picture of the new display is there (a moment from now)
            _sig = null; Volatile.Write(ref _jpeg, null);
            if (_board.monitorId.Length > 0 || _board.caption.Length > 0) { _board.monitorId = ""; _board.caption = ""; _core.MarkDirty(false); }
        }
        string line = "";
        if (spoken)
            line = !_core.HasMr ? Tell($"{number}번 화면 준비했어. 헤드셋을 쓰면 눈앞에 보일 거야.", $"Display {number} is ready. Put the headset on and it will be in front of you.", "neutral", 5)
                 : Tell($"{number}번 화면 가져올게!", $"Bringing display {number} over!");
        if (analyse) Analyse(m, words);
        return line;
    }

    /// <summary>The display a wish names. number: what the user would call it (1 = \\.\DISPLAY1).</summary>
    MonitorDesc Pick(Client c, string t, PointerSample pt, out int number)
    {
        var mons = _core.Desktop.Monitors;
        MonitorDesc m = null;
        var digits = Regex.Match(t, @"\d+");
        if (digits.Success && int.TryParse(digits.Value, out int n))
        {
            // Windows numbers its displays in their device names; when that number is not among them, the n-th in order
            m = mons.FirstOrDefault(x => NumberOf(x) == n);
            if (m == null && !mons.Any(x => NumberOf(x) > 0) && n >= 1 && n <= mons.Count) m = mons[n - 1];
        }
        else
        {
            // "this": what the user pointed or looked at while speaking; otherwise the display of the window they are working in; otherwise the main one
            if (pt != null && pt.valid) m = ScreenMath.FindMonitor(mons, pt.monitorId);
            // (no hand on a screen while speaking: where the hand or the gaze has been in the last seconds, as for looking - Look.cs)
            if (m == null && _core.Tasks.ResolveLook(c, HostClock.Now - 3, HostClock.Now) is { Pointed: true } spot) m = ScreenMath.FindMonitor(mons, spot.monitorId);
            if (m == null && Win32.FrameBounds(_core.Desktop.LastWorkspace, out RectI r)) m = ScreenMath.FindMonitor(mons, r.x + r.w / 2f, r.y + r.h / 2f);
            m ??= mons.FirstOrDefault(x => x.primary) ?? mons.FirstOrDefault();
        }
        number = m == null ? 0 : NumberOf(m) > 0 ? NumberOf(m) : m.index + 1;
        return m;
    }

    static int NumberOf(MonitorDesc m)
    {
        var d = Regex.Match(m.id ?? "", @"DISPLAY(\d+)", RegexOptions.IgnoreCase);
        return d.Success && int.TryParse(d.Groups[1].Value, out int n) ? n : 0;
    }

    void Close(string why)
    {
        if (_target.Length == 0 && _board.monitorId.Length == 0) return;
        Log.Info("screen: board closed (" + why + ")");
        _session++;
        _target = ""; _captionTask = ""; _sig = null; _again = -1;
        Volatile.Write(ref _jpeg, null);
        _board.monitorId = ""; _board.caption = ""; _board.revision++;
        _core.MarkDirty(false);
    }

    public void Tick(double now)
    {
        if (_target.Length == 0) return;
        if (now - _openedAt > MaxSeconds) { Close("ten minutes are over"); return; }
        if (!_shooting && now >= _nextShot)
        {
            _nextShot = now + RefreshSeconds;
            if (_again > 0) { _nextShot = Math.Max(now + 0.4, _again); _again = -1; }      // (after a PC action: once more soon, see Nudge)
            Shoot();
        }
        if (_captionTask.Length == 0) return;
        // the review that was started on this display: its summary is what the board says under the picture
        var t = _core.State.tasks.FirstOrDefault(x => x.taskId == _captionTask);
        if (t == null || t.status is "TaskFailed" or "TaskCancelled") { _captionTask = ""; Caption(""); }
        else if (t.status is "EvidenceReady" or "ProposalReady")
        {
            string line = t.kind == "review" ? t.detail : t.title;
            if (line.Length > 0 && !line.StartsWith("capture=") && !line.StartsWith("screenshot=")) Caption(WorldRules.Short(line, 160));
            if (t.status == "ProposalReady") _captionTask = "";
        }
    }

    void Caption(string line)
    {
        if (_board.caption == line) return;
        _board.caption = line;
        _core.MarkDirty(false);
    }

    void Shoot()
    {
        var m = ScreenMath.FindMonitor(_core.Desktop.Monitors, _target);
        if (m == null) { Close("the display is gone"); return; }
        _shooting = true;
        int session = _session; string id = m.id; var b = m.bounds;
        _ = Task.Run(() =>
        {
            byte[] jpeg = null, sig = null; string error = "";
            try { Capture(b, out jpeg, out sig); } catch (Exception e) { error = e.Message; }
            _core.Post(() =>
            {
                _shooting = false;
                if (session != _session || _target != id) return;           // closed, or another display asked for, while this one was being captured
                if (jpeg == null)
                {
                    // (the lock screen and the UAC prompt cannot be captured: the board keeps its last picture)
                    if (HostClock.Now - _failLogged > 30) { _failLogged = HostClock.Now; Log.Warn("screen: the display could not be captured: " + error); }
                    return;
                }
                bool first = _board.monitorId != id;
                if (!first && _sig != null && !Differs(_sig, sig)) return;  // nothing worth fetching again
                _sig = sig;
                Volatile.Write(ref _jpeg, jpeg);
                _board.monitorId = id; _board.revision++;
                _core.MarkDirty(false);
                if (first) Log.Info($"screen: board open on {id} ({b.w}x{b.h}, {jpeg.Length / 1024} KB)");
            });
        });
    }

    /// <summary>The display as a JPEG at most 1600 pixels wide, and a tiny grey copy of it.</summary>
    static void Capture(RectI b, out byte[] jpeg, out byte[] sig)
    {
        using var full = new Bitmap(b.w, b.h, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(full)) g.CopyFromScreen(b.x, b.y, 0, 0, new Size(b.w, b.h), CopyPixelOperation.SourceCopy);
        int w = Math.Min(MaxWidth, b.w), h = Math.Max(1, (int)Math.Round(b.h * (w / (double)b.w)));
        using var send = Scaled(full, w, h);
        var codec = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
        using var ms = new MemoryStream();
        send.Save(ms, codec, quality);
        jpeg = ms.ToArray();

        using var tiny = Scaled(send, SigW, SigH);
        var data = tiny.LockBits(new Rectangle(0, 0, SigW, SigH), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var px = new int[SigW * SigH];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            sig = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) sig[i] = (byte)((((px[i] >> 16) & 255) * 3 + ((px[i] >> 8) & 255) * 6 + (px[i] & 255)) / 10);
        }
        finally { tiny.UnlockBits(data); }
    }

    static Bitmap Scaled(Bitmap src, int w, int h)
    {
        var dst = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;      // (it averages when it shrinks, so one changed line of text still shows in the small copy)
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
        return dst;
    }

    /// <summary>Has the picture changed noticeably? A few cells of the small grey copy clearly different, or all of it a little (a fade, a video).</summary>
    internal static bool Differs(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return true;
        int clear = 0; long sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Math.Abs(a[i] - b[i]);
            sum += d;
            if (d > 14) clear++;
        }
        return clear >= 3 || sum > a.Length * 1.5;
    }

    /// <summary>The partner looks at what is on that display: the review that "이거 피드백 줘" starts, on the window in front there.</summary>
    void Analyse(MonitorDesc m, string words)
    {
        if (!_core.Tasks.ReviewEnabled) { Tell("화면을 살펴보는 건 PC에 AI 키가 있어야 할 수 있어.", "Looking at the screen needs an AI key on the PC.", "worried", 5); return; }
        IntPtr hwnd = WindowOn(m);
        if (hwnd == IntPtr.Zero) { Tell("그 화면에는 살펴볼 창이 없어.", "There's no window on that display to look at.", "curious"); return; }
        int before = _core.State.tasks.Count;
        _core.Tasks.OnCommand(new TaskCommand { cmd = "analyze", hwnd = hwnd.ToInt64(), prompt = words.Length > 0 ? words : "Please look at this and give me feedback." });
        if (_core.State.tasks.Count <= before) return;          // it is busy with something else, and has said so
        _captionTask = _core.State.tasks[^1].taskId;
        Caption(Ko ? "살펴보는 중 ..." : "Taking a look ...");
    }

    /// <summary>The window in front on a display: the one the user is working in when it is there, otherwise whatever lies topmost round its middle.</summary>
    internal IntPtr WindowOn(MonitorDesc m)
    {
        uint own = (uint)Environment.ProcessId;
        var b = m.bounds;
        IntPtr fg = _core.Desktop.LastWorkspace;
        if (fg != IntPtr.Zero && Win32.IsWorkspaceCandidate(fg, own) && !Win32.IsIconic(fg) && Win32.FrameBounds(fg, out RectI r) && b.Contains(r.x + r.w / 2f, r.y + r.h / 2f)) return fg;
        foreach (var (u, v) in new[] { (0.5f, 0.5f), (0.3f, 0.4f), (0.7f, 0.4f), (0.3f, 0.7f), (0.7f, 0.7f), (0.5f, 0.2f) })
        {
            IntPtr h = Win32.WorkspaceWindowAt((int)(b.x + b.w * u), (int)(b.y + b.h * v), own);
            if (h != IntPtr.Zero) return h;
        }
        return IntPtr.Zero;
    }
}
