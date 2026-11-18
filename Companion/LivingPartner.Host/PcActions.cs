using System.Diagnostics;
using System.Runtime.InteropServices;
using LivingPartner.Shared;

namespace LivingPartner.Host;

// PC ACTIONS, carried out (the rules are PcRules.cs: one fixed list, no command line, nothing deleted or overwritten).
// What touches Windows is behind IPcShell (PcRules.cs, with PcRun which carries a plan out), so that the tests do it against a fake:
//   a program of the allow-list or an http(s) address is started through the shell;
//   a key goes to ONE window - the window is brought to the front first and the key is only sent when it really is
//   in front (a key must never land in another window);
//   a new folder / a new note is made on the Desktop under a name that is free.

/// <summary>The PC actions of the host: which window is meant, the plan carried out on the real Windows, the board brought up to date.</summary>
public sealed class PcActions
{
    readonly Core _core;
    readonly IPcShell _shell;
    public PcActions(Core core, IPcShell shell = null) { _core = core; _shell = shell ?? new WindowsShell(); }
    public int Done { get; private set; }
    public string Last { get; private set; } = "";
    public object Diagnostics => new { done = Done, last = Last };

    /// <summary>
    /// The window the user means, in this order: the one under the spot they point or look at (Look.cs: hand, then
    /// gaze, then the newest sample) - the display that was brought into the room: the window in front on it - the
    /// window they last worked in.
    /// </summary>
    public (IntPtr hwnd, string how) TargetWindow(Client c, Utterance u)
    {
        uint own = (uint)Environment.ProcessId;
        var mons = _core.Desktop.Monitors;
        double now = HostClock.Now;
        var spot = _core.Tasks.ResolveLook(c, u != null && u.t1 > 0 ? u.t0 : now - 3, u != null && u.t1 > 0 ? u.t1 : now);
        if (spot.Pointed && ScreenMath.FindMonitor(mons, spot.monitorId) is { } m)
        {
            ScreenMath.UVToPixel(m, spot.u, spot.v, out float px, out float py);
            IntPtr h = Win32.WorkspaceWindowAt((int)px, (int)py, own);
            if (h != IntPtr.Zero) return (h, $"under the spot the user {(spot.source == LookRules.Gaze ? "looked" : "pointed")} at on {m.id}");
            // (the spot is on the bare desktop of that display: the window in front there)
            h = _core.Screen.WindowOn(m);
            if (h != IntPtr.Zero) return (h, "in front on the display the user pointed at, " + m.id);
        }
        if (_core.Screen.Shown.Length > 0 && ScreenMath.FindMonitor(mons, _core.Screen.Shown) is { } shown)
        {
            IntPtr h = _core.Screen.WindowOn(shown);
            if (h != IntPtr.Zero) return (h, "in front on the display that is shown in the room, " + shown.id);
        }
        IntPtr last = _core.Desktop.LastWorkspace;
        if (last != IntPtr.Zero && Win32.IsWorkspaceCandidate(last, own)) return (last, "the window the user last worked in");
        return (IntPtr.Zero, "no window");
    }

    static readonly string[] Browsers = { "chrome", "msedge", "firefox", "whale", "brave", "opera", "vivaldi", "iexplore", "arc" };

    static bool IsBrowser(IntPtr hwnd)
    {
        try { Win32.GetWindowThreadProcessId(hwnd, out uint pid); string n = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); return Array.IndexOf(Browsers, n) >= 0; }
        catch { return false; }
    }

    /// <summary>pcDo: one action of the fixed list. Returns what really happened. Core thread. hwnd: a window given by a check (lpctl), else worked out.</summary>
    public PcOutcome Do(Client c, string @do, string arg, Utterance u, IntPtr hwnd = default)
    {
        bool ko = _core.Talk.LastLanguage == "Korean";
        var plan = PcRules.Plan(@do, arg, ko);
        string how = "";
        IntPtr window = IntPtr.Zero;
        if (plan.Ok && plan.window)
        {
            if (hwnd != IntPtr.Zero) { window = hwnd; how = "the window named by the check"; }
            else (window, how) = TargetWindow(c, u);
            // A key that means something else in other programs (Ctrl+W, F5 ...) goes to a window the user did NOT point at
            // only when that window is a web browser: "새로고침" with nothing pointed at must not land in an editor that
            // happens to be the window the user last worked in.
            if (window != IntPtr.Zero && how.StartsWith("the window the user last worked in") && !IsBrowser(window))
            { window = IntPtr.Zero; how = "nothing was pointed at and the last window is not a web browser: no key sent"; }
        }
        var o = PcRun.Execute(plan, _shell, window, ko);
        Done += o.ok ? 1 : 0; Last = plan.@do;
        // (what is logged: the action, never the text that was typed or written, never a window's title)
        Log.Info($"pc: {(plan.@do.Length > 0 ? plan.@do : "'" + WorldRules.Short(@do, 20) + "'")}{(plan.@do is "openApp" or "newFolder" ? " '" + WorldRules.Short(plan.text, 40) + "'" : "")} -> {(o.ok ? "done" : "NOT done")}: {o.detail}{(how.Length > 0 ? "; window: " + how : "")}");
        // the board in the room shows the result: a fresh picture a moment from now (the page needs that long to change)
        if (o.ok) _core.Screen.Nudge(plan.kind == PcPlan.Start || plan.kind == PcPlan.Note ? 1.5 : 0.7);
        return o;
    }

    // ------------------------------------------------------------------ operating by seeing and acting (PcOperate.cs)
    CancellationTokenSource _opCts; WorkJob _opJob; bool _hooked;
    /// <summary>A run of operating the computer is on.</summary>
    public bool Operating => _opJob != null;
    public int Operated { get; private set; }

    /// <summary>The model that looks at the screen and chooses each step: the vision model unless LP_OPENAI_OPERATE_MODEL names another.</summary>
    string OperateModel => Environment.GetEnvironmentVariable("LP_OPENAI_OPERATE_MODEL") is { Length: > 0 } m ? m : _core.Talk.Ai.VisionModel;

    /// <summary>Stops the run that is on, at once (the user said stop, or cancelled the job in the work queue). false: none was on.</summary>
    public bool CancelOperate(string why)
    {
        if (_opCts == null || _opJob == null) return false;
        Log.Info($"operate: {_opJob.id} cancel - {why}");
        try { _opCts.Cancel(); } catch { }
        return true;
    }

    /// <summary>
    /// The display that is operated, in this order: the one the user points or looks at - the one the partner is in -
    /// the one that is shown in the room - the one of the window the user last worked in - the main one.
    /// </summary>
    public (MonitorDesc m, string how) OperateDisplay(Client c, Utterance u)
    {
        var mons = _core.Desktop.Monitors;
        double now = HostClock.Now;
        var spot = _core.Tasks.ResolveLook(c, u != null && u.t1 > 0 ? u.t0 : now - 3, u != null && u.t1 > 0 ? u.t1 : now);
        if (spot.Pointed && ScreenMath.FindMonitor(mons, spot.monitorId) is { } pm) return (pm, $"the display the user {(spot.source == LookRules.Gaze ? "looked" : "pointed")} at");
        if (_core.State.character.owner == Env.Pc && _core.Pc != null && ScreenMath.FindMonitor(mons, _core.Pc.monitorId) is { } inside) return (inside, "the display the partner is in");
        if (_core.Screen.Shown.Length > 0 && ScreenMath.FindMonitor(mons, _core.Screen.Shown) is { } shown) return (shown, "the display that is shown in the room");
        IntPtr last = _core.Desktop.LastWorkspace;
        if (last != IntPtr.Zero && Win32.FrameBounds(last, out RectI r) && ScreenMath.FindMonitor(mons, r.x + r.w / 2f, r.y + r.h / 2f) is { } lm) return (lm, "the display of the window the user last worked in");
        return (mons.FirstOrDefault(x => x.primary) ?? mons.FirstOrDefault(), "the main display");
    }

    /// <summary>
    /// pcOperate: the goal in the user's words is worked on by looking at the display and clicking / typing in it, one
    /// step at a time (OperateLoop). A job in the work queue and a show ("operate") tell every step. Returns at once:
    /// ok = it has started (the line says so), else why not; the result is said when the run ends. Core thread.
    /// </summary>
    public PcOutcome Operate(Client c, string goal, Utterance u)
    {
        bool ko = _core.Talk.LastLanguage == "Korean";
        var o = new PcOutcome();
        goal = PcRules.SafeText(goal, 300, false);
        if (goal.Length == 0) goal = PcRules.SafeText(NameRules.Strip(u?.text ?? ""), 300, false);
        string risk = OperateRules.GoalRisk(goal + " " + (u?.text ?? ""));
        if (goal.Length == 0) { o.line = PcRules.ProblemLine(new PcPlan { problem = "empty" }, ko); o.detail = "no goal"; }
        else if (!_core.Talk.Enabled) { o.line = OperateRules.NoKeyLine(ko); o.detail = "no AI key"; }
        else if (Operating) { o.line = OperateRules.BusyLine(ko); o.detail = "a run is on"; }
        else if (risk.Length > 0) { o.line = OperateRules.RiskLine(risk, ko, false); o.detail = "refused before anything was done: " + risk; }
        var (m, how) = o.line.Length == 0 ? OperateDisplay(c, u) : (null, "");
        if (o.line.Length == 0 && m == null) { o.line = OperateRules.NoScreenLine(ko); o.detail = "no display"; }
        if (o.line.Length > 0) { Log.Info($"operate: NOT started: {o.detail}"); return o; }

        if (!_hooked) { _hooked = true; _core.Work.Cancelled += j => { if (j != null && j == _opJob) CancelOperate("cancelled in the work queue"); }; }
        string model = OperateModel;
        var job = _core.Work.Begin("pc", OperateRules.Title(goal), u?.text ?? goal, "pc", "operator " + model);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(OperateRules.MaxSeconds + 30));
        _opJob = job; _opCts = cts;
        // A CHECK (LP_LOOK_TEST_IMAGE): a picture stands in for the display, so a click would land on whatever really is
        // on the screen there - NO input is sent in a check, it is only logged.
        bool dry = LookShots.StandInSet;
        IOperateScreen screen = new DisplayScreen(m);
        IOperateInput input = dry ? new DryInput() : _shell as IOperateInput ?? new DryInput();
        IOperateModel brain = new OpenAiOperateModel(_core.Talk.Ai, model);
        // (what is logged: the display and the steps' short lines - never the goal's words, what was typed or a window's title)
        Log.Info($"operate: {job.id} begins on {m.id} ({how}); a goal of {goal.Length} characters; model {model}{(dry ? "; TEST: a picture stands in for the display and NO input is sent" : "")}");
        _core.Shows.Operate(m.id, false, 0.5f, 0.5f, "Looking at the screen", "look");
        _ = Task.Run(async () =>
        {
            var res = await OperateLoop.RunAsync(goal, ko, screen, input, brain, p => _core.Post(() => OnOperateStep(job, p)), cts.Token, settle: Settle);
            _core.Post(() => OperateOver(job, goal, res));
        });
        o.ok = true; o.line = OperateRules.StartLine(ko); o.detail = "started " + job.id;
        return o;
    }

    /// <summary>How long the screen is given after a step before it is looked at again (and before a click: the moment the partner needs to get to the spot).</summary>
    static Task Settle(string act, CancellationToken ct) => Task.Delay(act switch
    {
        "before-click" => 450, "click" => 1500, "doubleClick" => 1900, "keys" => 1200, "type" => 350, "scroll" => 600, _ => 1500,
    }, ct);

    void OnOperateStep(WorkJob job, OperateProgress p)
    {
        if (job != _opJob) return;
        _core.Work.Step(job, p.line, Math.Min(0.92f, p.n / (float)(OperateRules.MaxSteps + 1)), p.n);
        _core.Shows.Operate(p.monitorId, p.hasSpot, p.u, p.v, p.line, p.stage);
        if (p.stage != "look") Log.Info($"operate: {job.id} step {p.n}: {p.line}{(p.hasSpot ? $" at ({p.u:0.000}, {p.v:0.000}) of {p.monitorId}" : "")}");
    }

    void OperateOver(WorkJob job, string goal, OperateResult res)
    {
        bool ko = _core.Talk.LastLanguage == "Korean";
        if (job == _opJob) { _opJob = null; try { _opCts?.Dispose(); } catch { } _opCts = null; }
        _core.Shows.OperateEnd();
        string how = res.ok ? $"Done in {res.steps} step{(res.steps == 1 ? "" : "s")}" : res.cancelled ? "" : res.refused ? "Stopped: that is not allowed" : "Could not finish it";
        _core.Work.End(job, res.ok, "", "", how, res.cancelled);
        if (res.ok) Operated++;
        string line = string.IsNullOrWhiteSpace(res.line) ? (res.ok ? (ko ? "다 했어!" : "Done!") : OperateRules.TooLongLine(ko)) : res.line.Trim();
        Log.Info($"operate: {job.id} over: {(res.ok ? "done" : res.cancelled ? "cancelled" : res.refused ? "refused (a hard rule)" : "not finished")} after {res.steps} step(s); the last: {WorldRules.Short(System.Text.RegularExpressions.Regex.Replace(res.log.LastOrDefault() ?? "", "\"[^\"]*\"", "\"...\""), 80)}");
        _core.Talk.Remember($"You worked on the user's computer for the goal \"{WorldRules.Short(goal, 120)}\": {string.Join("; ", res.log.TakeLast(6))}. Result: {(res.ok ? "done" : res.cancelled ? "the user stopped it" : "not finished")} - you said: {WorldRules.Short(line, 120)}");
        _core.Say(line, res.ok ? "happy" : res.cancelled ? "neutral" : "worried", 5, "conversation: pcOperate");
        _core.Screen.Nudge(0.7);
    }

    /// <summary>A check from lpctl on this PC: one real click at a pixel and a text typed after it (the check names a window of its own).</summary>
    public bool InputTest(int px, int py, string text)
    {
        if (_shell is not IOperateInput input) return false;
        bool ok = input.Click(px, py, 1);
        Thread.Sleep(250);
        if (ok && !string.IsNullOrEmpty(text)) ok = input.Type(PcRules.SafeText(text, 40, false));
        Log.Info($"operate: TEST - a real click at ({px}, {py}) and {(text ?? "").Length} characters typed: {(ok ? "sent" : "NOT sent")}");
        return ok;
    }
}

/// <summary>The display as the operating model sees it: the whole display (Look.cs takes the picture), at most OperateRules.ShotWidth wide, as a JPEG.</summary>
sealed class DisplayScreen : IOperateScreen
{
    readonly MonitorDesc _m;
    public DisplayScreen(MonitorDesc m) { _m = m; }

    public OperateShot Capture(int n)
    {
        var b = _m.bounds;
        using var full = LookShots.Grab(b, n);
        var (w, h) = OperateRules.ShotSize(full.Width, full.Height);
        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(full, new System.Drawing.Rectangle(0, 0, w, h), 0, 0, full.Width, full.Height, System.Drawing.GraphicsUnit.Pixel);
        }
        // the fingerprint: a 24 x 14 thumbnail, each channel in 16 steps (a blinking cursor or a clock's digit does not change it much - a new page does)
        ulong hash = 14695981039346656037UL;
        using (var tiny = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(24, 14)))
            for (int y = 0; y < tiny.Height; y++)
                for (int x = 0; x < tiny.Width; x++)
                {
                    var p = tiny.GetPixel(x, y);
                    hash = (hash ^ (ulong)((p.R >> 4) << 8 | (p.G >> 4) << 4 | (p.B >> 4))) * 1099511628211UL;
                }
        var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
        using var quality = new System.Drawing.Imaging.EncoderParameters(1);
        quality.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
        using var ms = new MemoryStream();
        bmp.Save(ms, codec, quality);
        byte[] jpeg = ms.ToArray();
        // (LP_LOOK_KEEP=1, checks only: the pictures of the last run are kept in the captures folder)
        if (Environment.GetEnvironmentVariable("LP_LOOK_KEEP") == "1") try { File.WriteAllBytes(Path.Combine(HostPaths.Captures, $"operate-{n}.jpg"), jpeg); } catch { }
        return new OperateShot { image = jpeg, mime = "image/jpeg", width = w, height = h, monitorId = _m.id, bounds = b, hash = hash };
    }
}

/// <summary>A check: nothing is sent, every step is logged.</summary>
sealed class DryInput : IOperateInput
{
    public bool Click(int px, int py, int count) { Log.Warn($"operate: TEST - a {(count == 2 ? "double " : "")}click at ({px}, {py}) was NOT sent"); return true; }
    public bool Scroll(int px, int py, int notches) { Log.Warn($"operate: TEST - the wheel at ({px}, {py}), {notches} notches, was NOT sent"); return true; }
    public bool Type(string text) { Log.Warn($"operate: TEST - {(text ?? "").Length} characters were NOT typed"); return true; }
    public bool Keys(ushort[] hold, ushort key) { Log.Warn($"operate: TEST - the key 0x{key:X2} was NOT pressed"); return true; }
}

/// <summary>The vision model that chooses each step (the question and the answer's shape are OperateRules').</summary>
sealed class OpenAiOperateModel : IOperateModel
{
    readonly OpenAiClient _ai; readonly string _model;
    public OpenAiOperateModel(OpenAiClient ai, string model) { _ai = ai; _model = model; }

    public async Task<OperateStep> NextAsync(string goal, bool korean, OperateShot shot, IReadOnlyList<string> done, string note, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var json = await _ai.AskJsonAsync(OperateRules.System, OperateRules.Question(goal, korean, shot.width, shot.height, done, note),
            new[] { (shot.mime, shot.image, "screen.jpg") }, OperateRules.Schema(), 3000, ct, _model, Environment.GetEnvironmentVariable("LP_OPENAI_OPERATE_EFFORT") is { Length: > 0 } e ? e : "low");
        var step = json.ToObject<OperateStep>() ?? new OperateStep { act = "cannot" };
        Log.Info($"operate: the model answered in {sw.ElapsedMilliseconds} ms ({shot.image.Length / 1024} kB sent): {step.act}{(step.act is "click" or "doubleClick" or "scroll" ? $" ({step.x}, {step.y}) of {shot.width}x{shot.height}" : "")} risk={step.risk}");
        return step;
    }
}

/// <summary>The real Windows behind IPcShell.</summary>
public sealed class WindowsShell : IPcShell, IOperateInput
{
    // ---- the mouse (operating by seeing and acting, PcOperate.cs): the pointer is put on the pixel and pressed there. Nothing is put back.
    const uint InputMouse = 0, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseWheel = 0x0800;
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    static INPUT Mouse(uint flags, int data = 0) => new() { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)data) } } };

    public bool Click(int px, int py, int count)
    {
        if (!SetCursorPos(px, py)) return false;
        Thread.Sleep(40);       // (the window under the pointer hears of it before the button)
        for (int i = 0; i < Math.Clamp(count, 1, 2); i++)
        {
            if (i > 0) Thread.Sleep(70);
            if (!Send(new List<INPUT> { Mouse(MouseLeftDown), Mouse(MouseLeftUp) })) return false;
        }
        return true;
    }

    public bool Scroll(int px, int py, int notches)
    {
        if (!SetCursorPos(px, py)) return false;
        Thread.Sleep(40);
        return Send(new List<INPUT> { Mouse(MouseWheel, -120 * Math.Clamp(notches, -10, 10)) });
    }

    public bool Keys(ushort[] hold, ushort key) => key != 0 && Press(hold, key, 1);

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    const uint InputKeyboard = 1, KeyUp = 0x0002, KeyUnicode = 0x0004, KeyExtended = 0x0001;

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);

    static INPUT Key(ushort vk, bool up)
    {
        // (the arrows, Page Up / Down and the media keys are "extended" keys)
        bool ext = vk is PcRules.VkLeft or PcRules.VkRight or PcRules.VkPrior or PcRules.VkNext or >= 0xAD and <= 0xB3;
        return new INPUT { type = InputKeyboard, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? KeyUp : 0) | (ext ? KeyExtended : 0) } } };
    }

    static bool Send(List<INPUT> list) => list.Count == 0 || SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>()) == (uint)list.Count;

    /// <summary>
    /// The user's default browser, as the full path of a program file that exists; "" when Windows names none.
    /// Read from the registry - the user's choice for https (then http), that program id's "open" command, the
    /// program in it (PcRules.ExeOfCommand): nothing is started by asking. (AssocQueryString answered "no
    /// association" for https, http and .html alike on the PC this was written on.)
    /// </summary>
    public static string DefaultBrowserExe()
    {
        try
        {
            foreach (string scheme in new[] { "https", "http" })
            {
                string id = "";
                using (var choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice"))
                    id = choice?.GetValue("ProgId") as string ?? "";
                // (no choice was ever made: the scheme's own entry carries the command)
                foreach (string key in id.Length > 0 ? new[] { id, scheme } : new[] { scheme })
                {
                    using var command = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(key + @"\shell\open\command");
                    string exe = PcRules.ExeOfCommand(command?.GetValue(null) as string);
                    if (exe.Length > 0 && File.Exists(exe)) return exe;
                }
            }
        }
        catch (Exception e) { Log.Warn("pc: the default browser could not be looked up: " + e.GetType().Name); }
        return "";
    }

    public bool Start(string target, string argument)
    {
        try
        {
            // openApp "browser": the default browser by itself, on the page it starts with (no site is chosen for the user)
            if (target == PcRules.DefaultBrowser)
            {
                string exe = DefaultBrowserExe();
                if (exe.Length == 0) { Log.Warn("pc: Windows names no default browser: nothing is started"); return false; }
                using var b = Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
                return true;
            }
            var info = new ProcessStartInfo { FileName = target, UseShellExecute = true };
            if (!string.IsNullOrEmpty(argument)) info.ArgumentList.Add(argument);
            using var p = Process.Start(info);
            return true;
        }
        catch (Exception e) { Log.Warn("pc: could not be started: " + e.GetType().Name); return false; }
    }

    static bool InFront(IntPtr hwnd)
    {
        IntPtr fg = Win32.GetForegroundWindow();
        return fg == hwnd || (fg != IntPtr.Zero && Win32.GetAncestor(fg, Win32.GA_ROOTOWNER) == hwnd);
    }

    public bool Foreground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd)) return false;
        if (Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        for (int attempt = 0; attempt < 3 && !InFront(hwnd); attempt++)
        {
            if (attempt == 0) Win32.SetForegroundWindow(hwnd);
            else if (attempt == 1)
            {
                // Windows lets only the thread that has the input bring another window forward: borrow it for a moment
                uint me = Win32.GetCurrentThreadId(), other = Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out _);
                bool joined = other != 0 && other != me && AttachThreadInput(me, other, true);
                BringWindowToTop(hwnd); Win32.SetForegroundWindow(hwnd);
                if (joined) AttachThreadInput(me, other, false);
            }
            else
            {
                // ... or a process that has just had a key of its own: a bare Alt, pressed and let go
                Send(new List<INPUT> { Key(PcRules.VkMenu, false), Key(PcRules.VkMenu, true) });
                Win32.SetForegroundWindow(hwnd);
            }
            for (int i = 0; i < 8 && !InFront(hwnd); i++) Thread.Sleep(25);
        }
        if (!InFront(hwnd)) return false;
        Thread.Sleep(60);       // (the window takes the keyboard a moment after it comes forward)
        return true;
    }

    public bool Press(ushort[] hold, ushort key, int times)
    {
        var list = new List<INPUT>();
        for (int n = 0; n < Math.Clamp(times, 1, 10); n++)
        {
            foreach (ushort h in hold ?? Array.Empty<ushort>()) list.Add(Key(h, false));
            list.Add(Key(key, false)); list.Add(Key(key, true));
            foreach (ushort h in (hold ?? Array.Empty<ushort>()).Reverse()) list.Add(Key(h, true));
        }
        return Send(list);
    }

    public bool Type(string text)
    {
        var list = new List<INPUT>();
        foreach (char ch in text ?? "")
        {
            list.Add(new INPUT { type = InputKeyboard, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KeyUnicode } } });
            list.Add(new INPUT { type = InputKeyboard, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KeyUnicode | KeyUp } } });
        }
        return Send(list);
    }

    public string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public bool Exists(string path) => Directory.Exists(path) || File.Exists(path);
    public void CreateFolder(string path) => Directory.CreateDirectory(path);
    public void WriteNew(string path, string text)
    {
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var w = new StreamWriter(fs, new System.Text.UTF8Encoding(true));
        w.Write((text ?? "").Replace("\n", "\r\n"));
    }
    public string Title(IntPtr hwnd) => Win32.WindowTitle(hwnd);
}
