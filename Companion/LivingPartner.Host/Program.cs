using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using LivingPartner.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LivingPartner.Host;

/// <summary>
/// Companion Host: a normal per-user process in the interactive session (tray app), not a Session 0 service.
/// It is also the launcher: starting the host starts the character client, so the user runs ONE thing.
/// </summary>
public static class Program
{
    public static Core Core;
    static NotifyIcon _tray;
    static ConnectionWindow _connection;
    static Process _client;
    static int _clientRestarts;
    static bool _loopbackOnly, _noClient, _quitting;
    static string _clientArgs = "";

    static string Arg(string[] a, string name, string fallback = null)
    {
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return fallback;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--demon-solo"))
        {
            Environment.ExitCode = SoloDemonDesktop.Run(!args.Contains("--no-browser")).GetAwaiter().GetResult();
            return;
        }
        // `--sculpt "<words>" --out <file.json>`: understand a thing and sculpt it out of simple parts, write the ItemDef,
        // exit (Sculptor.RunAsync). For trying the sculptor out: nothing else starts - no tray, no port, no window, and
        // no check for a second instance, because a host may well be running.
        if (Arg(args, "--sculpt") is { Length: > 0 } words)
        {
            Environment.ExitCode = Task.Run(() => Sculptor.RunAsync(words, Arg(args, "--out", "sculpt.json"))).GetAwaiter().GetResult();
            return;
        }
        // (C1) `--sculpt-photo <picture> [--say "words"] [--effort low|medium|high] --out <folder>`: looks at a picture as the host does
        // for "이거 만들어 줘", sculpts what it shows, writes the thing, its .glb and its preview (SightHost.RunAsync). Like --sculpt it starts nothing else.
        if (Arg(args, "--sculpt-photo") is { Length: > 0 } photo)
        {
            Environment.ExitCode = Task.Run(() => SightHost.RunAsync(photo, Arg(args, "--say", "이거 만들어 줘"), Arg(args, "--effort", ""), Arg(args, "--out", "sculpt-photo"))).GetAwaiter().GetResult();
            return;
        }
        // (C1) `--check-glb <file.glb>`: reads a 3D file back by the rules of glTF (GlbWriter.Check) and with the reader the headset uses, prints what it holds. 0 = it opens.
        if (Arg(args, "--check-glb") is { Length: > 0 } glbFile)
        {
            Environment.ExitCode = SightHost.CheckFile(glbFile);
            return;
        }
        // `--write-script "<words>" --out <path> [--effort low|medium] [--max-repairs n]`: the writer alone (OpenAiScriptModel.RunAsync);
        // like --sculpt it starts nothing else and installs nothing.
        if (Arg(args, "--write-script") is { Length: > 0 } wish)
        {
            Environment.ExitCode = Task.Run(() => OpenAiScriptModel.RunAsync(wish, Arg(args, "--out", "script-trial"), Arg(args, "--effort", ""), int.TryParse(Arg(args, "--max-repairs", "-1"), out int mr) ? mr : -1)).GetAwaiter().GetResult();
            return;
        }
        // `--work "<request>" [--kind game|thing|skill] [--out <folder>] [--effort e] [--model m]`: the WORKSHOP alone
        // (WorkshopCli.RunAsync): the agent loop with its tools, printing its steps as they happen; the final script and
        // the rendered frames go to the folder. Like --sculpt it starts nothing else and installs nothing.
        if (Arg(args, "--work") is { Length: > 0 } job)
        {
            Environment.ExitCode = Task.Run(() => WorkshopCli.RunAsync(job, Arg(args, "--kind", "game"), Arg(args, "--out", "work-trial"), Arg(args, "--effort", ""), Arg(args, "--model", ""))).GetAwaiter().GetResult();
            return;
        }
        // (`--work-try <file.lua | example id> [--words "<request>"] [--out <folder>]`: the harness's own part of that, without a model)
        if (Arg(args, "--work-try") is { Length: > 0 } tryIt)
        {
            Environment.ExitCode = WorkshopCli.TryScript(tryIt, Arg(args, "--words", ""), Arg(args, "--out", "work-try"));
            return;
        }
        // `--check-icons <folder> --out <file.txt>`: looks at every PNG in a folder for a round plate or ring behind the
        // object (IconArt.RoundPlate, the check the icon repair uses) and writes one line per picture. It only reads;
        // like --sculpt it starts nothing else, so it can be run next to a host that is running.
        if (Arg(args, "--check-icons") is { Length: > 0 } folder)
        {
            Environment.ExitCode = IconArt.Report(folder, Arg(args, "--out", "icons.txt"));
            return;
        }
        // (one host per data folder: a trial host with a folder of its own, LP_HOME, may run next to the real one)
        string instance = "Local\\LivingAIPartner.Host" + (HostPaths.Trial ? "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(HostPaths.Root.ToLowerInvariant())))[..12] : "");
        using var showConnection = new EventWaitHandle(false,EventResetMode.AutoReset,instance + ".ShowConnection");
        using var mutex = new Mutex(true, instance, out bool first);
        if (!first) { if (!args.Contains("--no-tray")) showConnection.Set(); return; }
        Win32.EnsureDpiAware();
        _loopbackOnly = args.Contains("--loopback-only");
        _noClient = args.Contains("--no-client");
        _clientArgs = Arg(args, "--client-args", "");
        Core = new Core { Port = int.Parse(Arg(args, "--port", Proto.DefaultPort.ToString())), ClientExePath = Arg(args, "--client", "") };
        Core.Init();
        Log.Info($"Living AI Partner host starting. port={Core.Port} loopbackOnly={_loopbackOnly} pid={Environment.ProcessId} session={Process.GetCurrentProcess().SessionId}");

        var web = StartWeb(out string portProblem);
        if (web == null)
        {
            // said once, in a box the user cannot miss (not with --no-tray: an unattended run has nobody to press OK; the log and the exit code say it)
            if (!args.Contains("--no-tray")) MessageBox.Show(portProblem, "Living AI Partner");
            Environment.ExitCode = 2;
            try { Core.Shop?.Stop(); } catch { }
            Log.Info("host stopped: it could not listen");
            return;
        }
        if (!_loopbackOnly) StartBeacon();
        if (!_noClient) StartClient();

        Application.EnableVisualStyles();
        bool interactive = !args.Contains("--no-tray");
        if (interactive)
        {
            BuildTray();
            if (!Core.NoAutoOpen) ShowConnection();
        }
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) =>
        {
            UpdateTrayText(); WatchClient();
            if (interactive && showConnection.WaitOne(0)) ShowConnection();
            if (_connection is { IsDisposed:false, Visible:true }) _connection.RefreshStatus();
        };
        timer.Start();
        Application.ApplicationExit += (_, _) => Shutdown();
        Application.Run();
        Shutdown();
    }

    /// <summary>Starts listening. null, with what to tell the user, when the port cannot be had.</summary>
    static Task StartWeb(out string problem)
    {
        problem = "";
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => { if (_loopbackOnly) o.ListenLocalhost(Core.Port); else o.ListenAnyIP(Core.Port); });
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(5) });
        app.Map("/ws", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            // A browser always says which page opens a socket (Origin); the partner's own clients - Unity, lpctl - are
            // not browsers and send none. So a socket with an Origin is some web page open on this PC reaching for
            // the host (with --usb-dev it could even have passed for the headset): it is turned away before the upgrade.
            if (ctx.Request.Headers.ContainsKey("Origin"))
            {
                Log.Warn($"socket turned away: it was opened by a web page ({WorldRules.Short(ctx.Request.Headers.Origin.ToString(), 80)})");
                ctx.Response.StatusCode = 403; return;
            }
            var ip = ctx.Connection.RemoteIpAddress;
            bool loopback = ip != null && IPAddress.IsLoopback(ip);
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await Core.RunSocket(ws, loopback, ip?.ToString() ?? "?", ctx.RequestAborted);
        });
        // local diagnostics only (no secrets); refused for non-loopback callers
        app.MapGet("/status", (HttpContext ctx) =>
        {
            var ip = ctx.Connection.RemoteIpAddress;
            if (ip == null || !IPAddress.IsLoopback(ip) || !MarketWeb.NamedLocally(ctx)) return Results.StatusCode(403);
            return Results.Text(Proto.ToJson(Diagnostics(), true), "application/json");
        });
        // The models (GLB) and icons (PNG) of the partner's things: what a snapshot names in item.model / item.icon /
        // skill.icon is fetched here. Who may: whoever may hold a socket. A local caller (the PC client, lpctl, and the
        // headset over the cable, which arrives through `adb reverse` as a local connection) - those files are this
        // user's own, in their own data folder - or, over the network, the address of a device that holds a paired,
        // authenticated socket at this moment. A plain GET carries no token, so the socket is what vouches for it.
        app.MapGet("/world/{file}", (HttpContext ctx, string file) =>
        {
            var ip = ctx.Connection.RemoteIpAddress;
            string remote = ip?.ToString() ?? "?";
            bool allowed = MayFetch(ctx, ip, remote);
            if (!allowed) return Results.StatusCode(403);
            // only a plain file name of the two kinds made here: nothing with a path in it gets as far as the disk
            if (!WorldRules.SafeFile(file)) return Results.NotFound();
            string path = Path.Combine(HostPaths.World, file);
            if (!File.Exists(path)) return Results.NotFound();
            // a file is written once under its id and never changed, so a client may keep it
            ctx.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(path, file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "application/octet-stream");
        });
        // (H6) The pictures of the documents the partner made (Docs.cs): a report's pages (page-<n>.png, and page-<n>-s.png
        // half as wide for the headset), a design's original and options. The same rule as for the world's files.
        app.MapGet("/docs/{id}/{file}", (HttpContext ctx, string id, string file) =>
        {
            var ip = ctx.Connection.RemoteIpAddress;
            if (!MayFetch(ctx, ip, ip?.ToString() ?? "?")) return Results.StatusCode(403);
            // only a document's id as it is made here and a plain PNG name: nothing with a path in it gets as far as the disk
            if (!DocsRules.SafeServed(id, file)) return Results.NotFound();
            string path = Path.Combine(DocsHost.StoreOf(id), file);
            if (!File.Exists(path)) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(path, "image/png");
        });
        // A display brought into the room as a board (ScreenShare.cs): the newest picture of it, to whoever may fetch the
        // world's files - a local caller, or a device that holds a paired socket. The number in the name is the board's
        // revision; it only makes the address new each time, the picture handed out is always the newest one.
        app.MapGet("/screen/{file}", (HttpContext ctx, string file) =>
        {
            var ip = ctx.Connection.RemoteIpAddress;
            string remote = ip?.ToString() ?? "?";
            bool allowed = MayFetch(ctx, ip, remote);
            if (!allowed) return Results.StatusCode(403);
            byte[] jpeg = Core.Screen.Jpeg;
            if (jpeg == null || !file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(jpeg, "image/jpeg");
        });
        // the shop on the PC: the page, its art and its small API (this machine only)
        GomokuWeb.Map(app,Core.Table,Path.Combine(AppContext.BaseDirectory,"web","table"));
        MarketWeb.Map(app, Core);
        LivingPartner.Demon.SoloDemonWeb.Map(app, Path.Combine(AppContext.BaseDirectory, "web", "demon-art"));
        // The port is taken here, before anything else starts: when it cannot be had (another program listens there)
        // the caller says so and the host ends - it used to carry on as a tray icon that nothing could connect to.
        try { app.StartAsync().GetAwaiter().GetResult(); }
        catch (Exception e)
        {
            var why = e.GetBaseException();
            Log.Error($"the host cannot listen on port {Core.Port}: {why.Message}");
            bool taken = e is IOException || why is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } || why.GetType().Name == "AddressInUseException";
            problem = taken
                ? $"Port {Core.Port} is already in use by another program, so the companion cannot start.\n\nClose the program that uses it (or start the companion with --port <number>) and try again."
                : $"The companion cannot listen on port {Core.Port}, so it cannot start.\n\n{why.Message}";
            return null;
        }
        var task = app.WaitForShutdownAsync();
        task.ContinueWith(t => { if (t.IsFaulted) Log.Error("web host failed: " + t.Exception?.GetBaseException().Message); });
        return task;
    }

    /// <summary>
    /// Who may fetch the world's files and the screen board's picture: a caller on this machine that names the host as
    /// this machine (see MarketWeb.NamedLocally: a web page under a name of its own is not that), or the address of a
    /// device that holds a paired, authenticated socket at this moment.
    /// </summary>
    static bool MayFetch(HttpContext ctx, IPAddress ip, string remote)
    {
        if (ip == null) return false;
        if (IPAddress.IsLoopback(ip)) return MarketWeb.NamedLocally(ctx);
        return Core.Clients.Values.Any(c => c.Authed && c.Remote == remote);
    }

    public static object Diagnostics() => new
    {
        host = Environment.MachineName, port = Core.Port, loopbackOnly = _loopbackOnly, uptimeSec = (int)(HostClock.Now - Core.StartedAt),
        clients = Core.Clients.Values.Where(c => c.Authed).Select(c => new { c.Role, c.Device, c.Remote, c.MsgIn }).ToArray(),
        owner = Core.State.character.owner, activity = Core.State.character.activity, transition = Core.Coord.Info.state,
        monitors = Core.Desktop.Monitors.Count, calibrations = Core.State.calibrations.Count, pairedDevices = Core.Auth.PairedCount,
        harness = Core.Tasks.Harness.Name, projects = Core.Tasks.Projects.Select(p => p.name).ToArray(),
        conversation = Core.Talk.Diagnostics, world = Core.World.Diagnostics, market = Core.Shop.Diagnostics, screen = Core.Screen.Diagnostics,
        glass = Core.Glass.Diagnostics, show = Core.Shows.Diagnostics, docs = Core.Docs.Diagnostics,
        pc = Core.Pc, mr = Core.Mr, modules = Core.State.modules.Count, tasks = Core.State.tasks.Count,
        clientExe = ResolveClientExe(), clientRunning = _client is { HasExited: false },
        recentLog = Log.Recent.TakeLast(12).ToArray(),
    };

    static void StartBeacon()
    {
        new Thread(() =>
        {
            try
            {
                using var udp = new UdpClient { EnableBroadcast = true };
                byte[] msg = Encoding.UTF8.GetBytes($"{Proto.BeaconMagic} {Core.Port} {Environment.MachineName}");
                while (!_quitting)
                {
                    try { udp.Send(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, Proto.BeaconPort)); } catch { }
                    Thread.Sleep(2000);
                }
            }
            catch (Exception e) { Log.Warn("beacon stopped: " + e.Message); }
        }) { IsBackground = true, Name = "beacon" }.Start();
    }

    static string ResolveClientExe()
    {
        if (!string.IsNullOrEmpty(Core.ClientExePath) && File.Exists(Core.ClientExePath)) return Core.ClientExePath;
        string baseDir = AppContext.BaseDirectory;
        foreach (var rel in new[] { @"..\LivingAIPartner-Windows\LivingAIPartner.exe", @"LivingAIPartner-Windows\LivingAIPartner.exe", @"..\..\..\..\..\dist\LivingAIPartner-Windows\LivingAIPartner.exe", @"..\..\..\..\..\..\dist\LivingAIPartner-Windows\LivingAIPartner.exe" })
        {
            string p = Path.GetFullPath(Path.Combine(baseDir, rel));
            if (File.Exists(p)) return p;
        }
        return "";
    }

    static void StartClient()
    {
        string exe = ResolveClientExe();
        if (exe.Length == 0) { Log.Warn("character client executable not found (build it or pass --client <path>)"); return; }
        try
        {
            _client = Process.Start(new ProcessStartInfo(exe, $"-lp-role pc -lp-port {Core.Port} -lp-return-focus {Win32.GetForegroundWindow().ToInt64()} {_clientArgs}") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
            Log.Info($"character client started: {exe} pid={_client?.Id}");
        }
        catch (Exception e) { Log.Error("failed to start character client: " + e.Message); }
    }

    static void WatchClient()
    {
        if (_noClient || _quitting || _client == null) return;
        if (_client.HasExited && _clientRestarts < 3)
        {
            _clientRestarts++;
            Log.Warn($"character client exited (code {_client.ExitCode}); restarting ({_clientRestarts}/3)");
            StartClient();
        }
    }

    static void BuildTray()
    {
        var menu = new ContextMenuStrip();
        var status = new ToolStripMenuItem("status") { Enabled = false };
        var pair = new ToolStripMenuItem("pairing");
        pair.Click += (_, _) => ShowConnection();
        var diag = new ToolStripMenuItem("Diagnostics...");
        diag.Click += (_, _) => MessageBox.Show(Proto.ToJson(Diagnostics(), true), "Living AI Partner - diagnostics");
        var overlay = new ToolStripMenuItem("Portal debug overlay") { CheckOnClick = true };
        overlay.Click += (_, _) => Core.Post(() => { Core.State.settings.debugOverlay = overlay.Checked; Core.MarkDirty(); });
        var quiet = new ToolStripMenuItem("Quiet mode") { CheckOnClick = true };
        quiet.Click += (_, _) => Core.Post(() => { Core.State.settings.quietMode = quiet.Checked; Core.State.character.quietMode = quiet.Checked; Core.MarkDirty(); });
        var logs = new ToolStripMenuItem("Open logs folder");
        logs.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(HostPaths.Logs) { UseShellExecute = true }); } catch { } };
        var restart = new ToolStripMenuItem("Restart character client");
        restart.Click += (_, _) => { try { if (_client is { HasExited: false }) _client.Kill(); } catch { } _clientRestarts = 0; StartClient(); };
        var talk = new ToolStripMenuItem("Talk to the partner (microphone)");
        talk.Click += (_, _) => Core.Post(() => Core.Broadcast(Msg.Debug, new DebugCommand { cmd = "listen", value = 1 }));
        // looking and reading without a headset: the window the user was last working in, or a file they pick
        var look = new ToolStripMenuItem("Look at my window: feedback + revised version");
        look.Click += (_, _) => Core.Post(() => Core.Tasks.OnCommand(new TaskCommand { cmd = "analyze", hwnd = Core.Desktop.LastWorkspace.ToInt64(), prompt = "Please look at this window and give me feedback." }));
        var read = new ToolStripMenuItem("Show a file to the partner (PDF, picture, text)...");
        read.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = "Show a file to Bambi", Filter = "PDF, pictures, text|*.pdf;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.txt;*.md|All files|*.*" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            string path = dlg.FileName;
            Core.Post(() => Core.Tasks.OnCommand(new TaskCommand { cmd = "read", projectPath = path, prompt = "Please read this file." }));
        };
        var reviews = new ToolStripMenuItem("Open the reviews folder");
        reviews.Click += (_, _) => { try { Directory.CreateDirectory(TaskManager.ReviewRoot); Process.Start(new ProcessStartInfo(TaskManager.ReviewRoot) { UseShellExecute = true }); } catch { } };
        // talking to the partner by typing: the box on the shop's page (that is also where new things are asked for)
        var chat = new ToolStripMenuItem("Talk to Bambi ...");
        chat.Click += (_, _) => { try { Process.Start(new ProcessStartInfo($"http://127.0.0.1:{Core.Port}/market#talk") { UseShellExecute = true }); } catch (Exception e) { Log.Warn("the page could not be opened: " + e.Message); } };
        var shop = new ToolStripMenuItem("Open the marketplace");
        shop.Click += (_, _) => { try { Process.Start(new ProcessStartInfo($"http://127.0.0.1:{Core.Port}/market") { UseShellExecute = true }); } catch (Exception e) { Log.Warn("the marketplace page could not be opened: " + e.Message); } };
        var solo = new ToolStripMenuItem("악마의 문 · 혼자 플레이");
        solo.Click += (_,_) => OpenGamePage(Core.Port, "/demon-gate/");
        var table = new ToolStripMenuItem("오목 · Quest와 두 사람");
        table.Click += (_,_) => OpenGamePage(Core.Port, "/table/");
        var cancel = new ToolStripMenuItem("Stop current task / transition");
        cancel.Click += (_, _) => Core.Post(() => { Core.Coord.CancelByUser("stopped from tray"); Core.Tasks.CancelRunning("stopped from tray"); });
        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => Application.Exit();
        menu.Items.AddRange(new ToolStripItem[] { status, new ToolStripSeparator(), chat, shop, solo, table, talk, look, read, reviews, new ToolStripSeparator(), pair, diag, overlay, quiet, cancel, restart, logs, new ToolStripSeparator(), quit });
        menu.Opening += (_, _) =>
        {
            status.Text = TrayStatus();
            pair.Text = $"Pair a headset (code {Core.Auth.PairCode})";
            overlay.Checked = Core.State.settings.debugOverlay; quiet.Checked = Core.State.settings.quietMode;
            look.Enabled = read.Enabled = Core.Talk.Enabled;
            talk.Enabled = Core.Talk.Enabled; talk.Text = Core.Talk.Enabled ? "Talk to the partner (microphone)" : "Talk to the partner (needs an OpenAI key)";
        };
        _tray = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "Living AI Partner", ContextMenuStrip = menu };
        _tray.DoubleClick += (_,_) => ShowConnection();
    }

    internal static bool OpenGamePage(int port, string path)
    {
        try { Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}{path}") { UseShellExecute = true }); return true; }
        catch (Exception e) { Log.Warn("The game could not be opened: " + e.Message); return false; }
    }

    static void ShowConnection()
    {
        if (_connection == null || _connection.IsDisposed) _connection = new ConnectionWindow(Core);
        _connection.RefreshStatus();
        _connection.Show();
        if (_connection.WindowState == FormWindowState.Minimized) _connection.WindowState = FormWindowState.Normal;
        _connection.Activate();
    }

    static string TrayStatus()
    {
        var c = Core.State.character;
        string roles = string.Join(",", Core.Clients.Values.Where(x => x.Authed).Select(x => x.Role).Distinct());
        return $"{c.name}: in {(c.owner == Env.Pc ? "PC" : "MR")} | {c.activity} | clients: {(roles.Length > 0 ? roles : "none")}";
    }

    static void UpdateTrayText()
    {
        if (_tray == null) return;
        string s = "Living AI Partner - " + TrayStatus();
        _tray.Text = s.Length > 63 ? s[..63] : s;
    }

    public static void TrayBalloon(string title, string text)
    {
        try { _tray?.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); } catch { }
    }

    static IEnumerable<string> LocalAddresses()
    {
        try { return Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    static void Shutdown()
    {
        if (_quitting) return;
        _quitting = true;
        try { Core.Save(); } catch { }
        try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        try { if (_client is { HasExited: false }) _client.Kill(); } catch { }
        try { Core.Shop?.Stop(); } catch { }      // the marketplace hub this host started for itself goes with it (HubLink.cs)
        Log.Info("host stopped");
    }
}
