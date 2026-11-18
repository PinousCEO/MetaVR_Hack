using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using LivingPartner.Shared;

namespace LivingPartner.Host;

public sealed class Client
{
    public Guid Id = Guid.NewGuid();
    public WebSocket Ws;
    public string Role = "", ClientId = "", Device = "";
    public bool Authed, Loopback;
    public string Remote = "";
    public readonly Channel<Outgoing> Outbox = Channel.CreateBounded<Outgoing>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest });
    /// <summary>
    /// The newest snapshot put into this client's outbox. A snapshot replaces the one before it, so the sender skips
    /// any older one that is still waiting: a slow link then carries one 45 kB snapshot, not a queue of them - and the
    /// small messages that matter (what the partner says, an intent's result, a transition step) are not pushed out.
    /// </summary>
    public volatile string NewestSnapshot;
    public double ConnectedAt, LastSeen;
    public long MsgIn;
}

/// <summary>One message waiting in a client's outbox. Snapshot: it is a whole state snapshot (see Client.NewestSnapshot).</summary>
public readonly record struct Outgoing(string Json, bool Snapshot);

public sealed class PersistedState
{
    public CharacterInfo character = new();
    public List<MonitorCalibration> calibrations = new();
    public List<ModuleDef> modules = new();
    public List<TaskInfo> tasks = new();
    public SettingsMsg settings = new();
    public string returnPolicy = "restoreOrCall";
    public V3 lastMrFootLocal = new(0, -0.25f, -0.22f);
    public V3 lastWorldUpLocal = new(0, 1, 0);
    /// <summary>The partner's things, skills, tune and surroundings (Maker.cs): kept, so that what the user gave and taught is still there tomorrow.</summary>
    public WorldState world = new();
    /// <summary>The marketplace's books (Market.cs): the shelf, what is installed, the stars, what the user published.</summary>
    public MarketState market = new();
    /// <summary>Glass games (GlassHost.cs): the best score of each game pack, by its id.</summary>
    public Dictionary<string, int> glassBest = new();
}

/// <summary>
/// The single authority for task and long-term state. All state changes run on one core thread (an action queue plus a
/// 15 ms tick), so there are no data races and every client sees one consistent ordering of events.
/// </summary>
public sealed class Core
{
    public readonly Auth Auth = new();
    public readonly ConcurrentDictionary<Guid, Client> Clients = new();
    public PersistedState State = new();
    public readonly string SessionId = Guid.NewGuid().ToString("N");
    readonly Dictionary<string, string> _roomSessions = new();
    public Desktop Desktop;
    public Coordinator Coord;
    public TaskManager Tasks;
    public Conversation Talk;
    public TennisGame Tennis;
    /// <summary>The things the user gives the partner and what it learns to do with them (State.world).</summary>
    public Maker World;
    /// <summary>The marketplace: modules the partner can be given, what is installed, the stars (State.market).</summary>
    public Market Shop;
    /// <summary>A display of the PC brought into the room as a board (kept in memory only).</summary>
    public ScreenShare Screen;
    /// <summary>Glass games: which one is on, on which side of the glass, and what the side that runs it reports (the snapshot's glass).</summary>
    public GlassHost Glass;
    /// <summary>What the partner is visibly doing in the PC: reading a part of a display, the market stall, a parcel (the snapshot's show).</summary>
    public Shows Shows;
    /// <summary>What the partner does on the PC when asked: one fixed list of actions (PcRules.cs, PcActions.cs).</summary>
    public PcActions PcDo;
    public TvRallyHost TvRally;
    public readonly GomokuTable Table = new();
    Client tableHeadset;
    /// <summary>(H6) The documents the partner makes - a researched report as a PDF, design options as PNG + PDF - shown in the room and carried into the PC (the snapshot's docs).</summary>
    public DocsHost Docs;
    public WorkQueue Work;
    /// <summary>(W1) The workshop: every making is one job of the work queue, written by the agent loop (WorkshopHost.cs, Workshop.cs).</summary>
    public WorkshopHost Workshop;
    /// <summary>(C1) "이거 만들어 줘": the partner looks at what the user holds up (one picture from the headset), sculpts it and carries its 3D file into the PC (Sight.cs).</summary>
    public SightHost Sight;
    /// <summary>--no-auto-open: a finished review is not opened in a viewer by itself (unattended test runs).</summary>
    public readonly bool NoAutoOpen = Environment.GetCommandLineArgs().Contains("--no-auto-open");
    /// <summary>
    /// --usb-dev: a headset that reaches the host through `adb reverse` (so it shows up as a local connection, and only a
    /// PC the headset's owner has authorised for USB debugging can set that up) is accepted without typing the pairing
    /// code. For development with the cable in; a headset on the network still has to be paired.
    /// </summary>
    public readonly bool UsbDev = Environment.GetCommandLineArgs().Contains("--usb-dev");
    double _lastRejectLog = -100, _calibSaidAt = -1000, _flashAt = -1000;
    public readonly ScreenFlash Flash = new();
    public PcStatus Pc;
    public MrStatus Mr;
    public string CalibratingMonitor = "";
    /// <summary>When the MR client last reported (twice a second while it runs; nothing while the headset is off).</summary>
    public double MrSeenAt;
    public double StartedAt = HostClock.Now;
    public int Port = Proto.DefaultPort;
    public string ClientExePath = "";

    readonly BlockingCollection<Action> _queue = new();
    long _seq;
    bool _snapshotDirty, _snapshotUrgent, _saveDirty;
    double _lastSnapshot, _lastSave, _lastModuleTick;
    // A snapshot is about 45 kB and the headset parses each one on its main thread: at most four a second go out in
    // normal operation. What cannot wait (a transition step, what an intent has just changed) may follow the one
    // before it after 50 ms, as every snapshot used to.
    const double SnapshotGap = 0.25, UrgentSnapshotGap = 0.05;
    // where the PC partner stood in the last snapshot that went out, and whether it has walked on since (see Msg.PcStatus)
    float _pcSentX, _pcSentY; bool _pcMoved; double _pcMovedAt;
    readonly Dictionary<string, List<PointerSample>> _pointerHistory = new();

    public void Post(Action a) { try { _queue.Add(a); } catch { } }

    public void Init()
    {
        HostPaths.Ensure();
        Log.Init();
        Auth.Init();
        LoadState();
        Desktop = new Desktop(this);
        Coord = new Coordinator(this);
        Tasks = new TaskManager(this);
        Talk = new Conversation(this);
        Tennis = new TennisGame(this);
        World = new Maker(this);
        Screen = new ScreenShare(this);
        Glass = new GlassHost(this);
        Shows = new Shows(this);
        PcDo = new PcActions(this);
        TvRally = new TvRallyHost(this);
        Docs = new DocsHost(this);
        Work = new WorkQueue(this);
        Workshop = new WorkshopHost(this);
        Sight = new SightHost(this);    // (after the queue: it listens for its jobs being cancelled)
        Shop = new Market(this);       // after the Maker: it adds what is installed to the world the Maker has just put in order
        Coord.RestoreAfterRestart();
        new Thread(Loop) { IsBackground = true, Name = "core" }.Start();
    }

    void LoadState()
    {
        try
        {
            if (File.Exists(HostPaths.State)) State = Proto.FromJson<PersistedState>(File.ReadAllText(HostPaths.State)) ?? new PersistedState();
        }
        catch (Exception e) { Log.Warn("state load failed: " + e.Message); }
        State.character ??= new CharacterInfo(); State.settings ??= new SettingsMsg();
        State.calibrations ??= new(); State.modules ??= new(); State.tasks ??= new();
        // the partner was renamed twice (Momo, then Bambyeol): a state file from before still carries the old name
        if (State.character.name is "Momo" or "Bambyeol" or "밤별" or "모모" or "" or null) State.character.name = "Bambi";
        // tasks that were running when the host stopped did not finish: mark them honestly instead of resuming blindly
        foreach (var t in State.tasks)
            if (t.status is "AnalysisStarted" or "ChangeStarted" or "TestStarted" or "TaskQueued")
            { t.status = "TaskFailed"; t.detail = "host restarted while the task was running"; }
        if (State.character.activity is "analysis" or "modify" or "test") State.character.activity = "idle";
        State.character.talk = "";
        // (H1) what the partner heard last is of the moment: nothing of it is carried over a restart
        State.character.heard = ""; State.character.heardNote = ""; State.character.heardAt = 0;
        Log.Info($"state loaded: owner={State.character.owner} calibrations={State.calibrations.Count} modules={State.modules.Count} tasks={State.tasks.Count}");
    }

    /// <summary>
    /// The state changed: the clients get a snapshot (at most four a second, see SnapshotGap), and with save the state
    /// file is written. urgent: this change must not wait for the four-a-second pace (a transition step).
    /// </summary>
    public void MarkDirty(bool save = true, bool urgent = false) { _snapshotDirty = true; if (urgent) _snapshotUrgent = true; if (save) _saveDirty = true; }

    void Loop()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (_queue.TryTake(out var a, 15)) { a(); while (_queue.TryTake(out a)) a(); }
                Tick();
            }
            catch (Exception e) { Log.Error("core: " + e); }
        }
    }

    void Tick()
    {
        tableHeadset??=Clients.Values.FirstOrDefault(c=>c.Authed&&c.Role==Roles.Quest)??Clients.Values.FirstOrDefault(c=>c.Authed&&IsMr(c));
        Table.Update(tableHeadset!=null,Desktop.Monitors.Select(m=>new TableDisplay(m.id,m.label+" ("+m.bounds.w+"×"+m.bounds.h+")",
            State.calibrations.Any(c=>c.monitorId==m.id && c.pixelW==m.bounds.w && c.pixelH==m.bounds.h))).ToArray(),
            state=>{if(tableHeadset!=null)Send(tableHeadset,"gomoku.state",state);},
            id=>{Tennis.Stop("gomoku");Glass.Stop("gomoku");if(tableHeadset!=null)Send(tableHeadset,Msg.IntentResult,new {kind="playGame",text="game-gomoku",pointer=new PointerSample{valid=true,monitorId=id,u=.5f,v=.5f}});},
            ()=>{if(!NoAutoOpen&&!Environment.GetCommandLineArgs().Contains("--no-tray"))try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"http://127.0.0.1:{Port}/table/"){UseShellExecute=true});}catch(Exception ex){Log.Warn("the TV board could not open: "+ex.Message);}});
        double now = HostClock.Now;
        Desktop.Tick(now);
        Coord.Tick(now);
        Tasks.Tick(now);
        Talk.Tick(now);
        Tennis.Tick(now);
        World.Tick(now);
        Shop.Tick(now);
        Screen.Tick(now);
        Glass.Tick(now);
        Shows.Tick(now);
        Docs.Tick(now);
        DesktopCheck(now);
        Workshop.Tick(now);
        Sight.Tick(now);
        // The PC shows always-on-top markers at the corners of the display being calibrated. If the headset goes quiet
        // (taken off, app closed, cable out) they are taken away at once: on 2026-11-16 they stayed for twenty minutes
        // over the corners of the main display, the window-close button among them.
        if (Flash.Active && (!HasMr || now - _flashAt > 1.2)) Flash.Show(null, Desktop.Monitors);      // the headset stopped asking
        if (CalibratingMonitor.Length > 0 && (!HasMr || now - MrSeenAt > 4.0))
        {
            Log.Info("calibration markers removed: the headset stopped reporting");
            CalibratingMonitor = ""; MarkDirty(false);
        }
        if (now - _lastModuleTick > 0.2)
        {
            _lastModuleTick = now;
            long ms = HostClock.UnixMs;
            foreach (var m in State.modules)
                if (m.kind == "timer" && TimerLogic.Tick(m, ms))
                {
                    Log.Info($"timer done: {m.moduleId} '{m.name}'");
                    Broadcast(Msg.ModuleUpdate, m);
                    if (m.alert != "silent") Say($"{m.name} is done!", "happy", 6, "timer:" + m.moduleId);
                    MarkDirty();
                }
        }
        // the PC partner walked on: said at most twice a second (no smaller message carries where it stands, and a snapshot for every step would be five a second)
        if (_pcMoved && now - _pcMovedAt >= 0.5) { _pcMoved = false; _pcMovedAt = now; MarkDirty(false); }
        if (_snapshotDirty && now - _lastSnapshot > (_snapshotUrgent ? UrgentSnapshotGap : SnapshotGap))
        {
            _snapshotDirty = false; _snapshotUrgent = false; _lastSnapshot = now;
            Broadcast(Msg.Snapshot, BuildSnapshot());
            if (Pc != null) { _pcSentX = Pc.pxX; _pcSentY = Pc.pxY; }
            _pcMoved = false;       // (this snapshot said where it stands now)
        }
        if (_saveDirty && now - _lastSave > 1.0) { _saveDirty = false; _lastSave = now; Save(); }
    }

    public void Save()
    {
        try
        {
            string tmp = HostPaths.State + ".tmp";
            File.WriteAllText(tmp, Proto.ToJson(State, true));
            File.Move(tmp, HostPaths.State, true);
        }
        catch (Exception e) { Log.Warn("state save failed: " + e.Message); }
    }

    public StateSnapshot BuildSnapshot()
    {
        return new StateSnapshot
        {
            sessionId = SessionId, hostTime = HostClock.Now, seq = Interlocked.Read(ref _seq),
            character = State.character, monitors = Desktop.Monitors, calibrations = State.calibrations,
            transition = Coord.Info, pc = Pc, mr = Mr, tasks = SnapshotTasks(), modules = State.modules,
            settings = State.settings, calibratingMonitorId = CalibratingMonitor,
            connectedRoles = Clients.Values.Where(c => c.Authed).Select(c => c.Role).Distinct().ToList(),
            returnPolicy = State.returnPolicy, tennis = Tennis.S.active ? Tennis.S : null,
            world = State.world, market = Shop.View, screen = Screen.Board,
            glass = Glass.S, show = Shows.Info, docs = Docs.View, work = Work.View,
        };
    }

    const int SnapshotTaskText = 400;

    /// <summary>
    /// The tasks a snapshot carries: the last eight, made small. Every snapshot used to carry all of their events and
    /// texts (9 kB of 45). Now only the newest three keep their event lists, and the long texts of a task that is over
    /// are cut to 400 characters - the headset shows 190 of them at most. The state itself keeps everything: these
    /// are copies. (task.update still carries a task whole at the moment something happens to it.)
    /// </summary>
    List<TaskInfo> SnapshotTasks()
    {
        var last = State.tasks.TakeLast(8).ToList();
        for (int i = 0; i < last.Count; i++) last[i] = SlimTask(last[i], i >= last.Count - 3);
        return last;
    }

    static TaskInfo SlimTask(TaskInfo t, bool keepEvents)
    {
        if (t == null) return t;
        bool over = !(t.status is "TaskQueued" or "AnalysisStarted" or "EvidenceReady" or "ChangeStarted" or "TestStarted");
        static bool Long(string s) => s != null && s.Length > SnapshotTaskText;
        bool dropEvents = !keepEvents && t.events is { Count: > 0 };
        bool cut = over && (Long(t.detail) || Long(t.diffSummary) || Long(t.testOutput) || (keepEvents && t.events != null && t.events.Any(e => e != null && Long(e.detail)))
                            || (t.modules != null && t.modules.Any(m => m != null && (Long(m.uiEvidence) || Long(m.evidence) || Long(m.proposal) || Long(m.impact) || Long(m.testResult)))));
        if (!dropEvents && !cut) return t;
        var c = Proto.FromJson<TaskInfo>(Proto.ToJson(t));
        if (c == null) return t;
        if (!keepEvents) c.events = new List<TaskEventRec>();
        if (!over) return c;
        // (never cut in the middle of a surrogate pair)
        static string Cut(string s) => !Long(s) ? s : s[..(char.IsHighSurrogate(s[SnapshotTaskText - 1]) ? SnapshotTaskText - 1 : SnapshotTaskText)] + "…";
        c.detail = Cut(c.detail); c.diffSummary = Cut(c.diffSummary); c.testOutput = Cut(c.testOutput);
        foreach (var e in c.events ?? new List<TaskEventRec>()) if (e != null) e.detail = Cut(e.detail);
        foreach (var m in c.modules ?? new List<ProposalModule>())
            if (m != null) { m.uiEvidence = Cut(m.uiEvidence); m.evidence = Cut(m.evidence); m.proposal = Cut(m.proposal); m.impact = Cut(m.impact); m.testResult = Cut(m.testResult); }
        return c;
    }

    // ------------------------------------------------------------------ transport
    public void Send(Client c, string type, object body)
    {
        Enqueue(c, Proto.Pack(type, body, null, Interlocked.Increment(ref _seq), HostClock.Now), type == Msg.Snapshot);
    }

    public void Broadcast(string type, object body, Func<Client, bool> filter = null)
    {
        string json = Proto.Pack(type, body, null, Interlocked.Increment(ref _seq), HostClock.Now);
        foreach (var c in Clients.Values)
            if (c.Authed && (filter == null || filter(c))) Enqueue(c, json, type == Msg.Snapshot);
    }

    /// <summary>Into a client's outbox. A snapshot becomes the only one worth sending: older ones still waiting there are skipped by the sender (RunSocket); every other message keeps its place.</summary>
    static void Enqueue(Client c, string json, bool snapshot)
    {
        if (snapshot) c.NewestSnapshot = json;
        c.Outbox.Writer.TryWrite(new Outgoing(json, snapshot));
    }

    public bool HasRole(string role) => Clients.Values.Any(c => c.Authed && c.Role == role);
    public bool HasMr => Clients.Values.Any(c => c.Authed && (c.Role == Roles.Quest || c.Role == Roles.Sim));
    public bool HasPc => HasRole(Roles.Pc);
    public bool IsMr(Client c) => c.Role == Roles.Quest || c.Role == Roles.Sim;

    /// <summary>What the partner says: shown in its speech bubble and, when voice is set up and allowed, spoken.</summary>
    public void Say(string text, string mood = "neutral", float seconds = 4, string basis = "")
    {
        if (Talk != null && Talk.LastLanguage == "Korean") text = HostLines.Ko(text);      // the host's own lines, in the language being spoken
        if (Talk != null) Talk.Say(text, mood, seconds, basis); else SayText(text, mood, seconds, basis);
    }

    /// <summary>The speech bubble only (used directly by the conversation, which sends the sound alongside it).</summary>
    public void SayText(string text, string mood = "neutral", float seconds = 4, string basis = "")
    {
        Log.Info($"say[{mood}]: {text} (basis: {basis})");
        LastSaid = text ?? ""; LastSaidMs = HostClock.UnixMs;
        Broadcast(Msg.Say, new SayMsg { text = text, mood = mood, seconds = seconds, basis = basis });
    }

    public async Task RunSocket(WebSocket ws, bool loopback, string remote, CancellationToken ct)
    {
        var c = new Client { Ws = ws, Loopback = loopback, Remote = remote, ConnectedAt = HostClock.Now, LastSeen = HostClock.Now };
        Clients[c.Id] = c;
        var sender = Task.Run(async () =>
        {
            try
            {
                await foreach (var o in c.Outbox.Reader.ReadAllAsync(ct))
                {
                    if (ws.State != WebSocketState.Open) break;
                    // a snapshot that a newer one has been queued behind is not worth the wire: only the newest is sent
                    if (o.Snapshot && !ReferenceEquals(o.Json, c.NewestSnapshot)) continue;
                    await ws.SendAsync(Encoding.UTF8.GetBytes(o.Json), WebSocketMessageType.Text, true, ct);
                }
            }
            catch { }
        });
        var buf = new byte[64 * 1024];
        var ms = new MemoryStream();
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var res = await ws.ReceiveAsync(buf, ct);
                if (res.MessageType == WebSocketMessageType.Close) break;
                ms.Write(buf, 0, res.Count);
                if (ms.Length > 4 * 1024 * 1024) break; // oversized message: drop the connection
                if (res.EndOfMessage)
                {
                    string json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                    ms.SetLength(0);
                    var env = Proto.Parse(json);
                    if (env != null && !string.IsNullOrEmpty(env.type)) Post(() => OnMessage(c, env));
                }
            }
        }
        catch { }
        finally
        {
            Clients.TryRemove(c.Id, out _);
            c.Outbox.Writer.TryComplete();
            try { ws.Abort(); } catch { }
            Post(() => OnDisconnected(c));
        }
    }

    void OnDisconnected(Client c)
    {
        if (!c.Authed) return;
        Log.Info($"client disconnected: role={c.Role} device={c.Device} remote={c.Remote}");
        if (c.Role == Roles.Pc && !HasPc) Pc = null;
        if (IsMr(c) && !HasMr) Mr = null;
        if(tableHeadset==c){tableHeadset=null;Table.Disconnect();}
        Coord.OnClientLost(c.Role);
        MarkDirty(false);
    }

    // ------------------------------------------------------------------ dispatch
    void OnMessage(Client c, Envelope env)
    {
        c.LastSeen = HostClock.Now; c.MsgIn++;
        if (!c.Authed)
        {
            if (env.type == Msg.Hello)
            {
                var h = env.Body<Hello>();
                if (h == null || h.protocol != Proto.Version) { Send(c, Msg.Reject, new Reject { reason = $"protocol mismatch: host={Proto.Version} client={h?.protocol}" }); return; }
                bool usbHeadset = UsbDev && c.Loopback && h.role == Roles.Quest;       // adb reverse: the headset arrives as a local connection
                if (!usbHeadset && !Auth.CheckToken(h.token, c.Loopback))
                {
                    if (HostClock.Now - _lastRejectLog > 10) { _lastRejectLog = HostClock.Now; Log.Info($"client turned away (not paired): role={h.role} device={h.deviceName} remote={c.Remote} loopback={c.Loopback} token sent={!string.IsNullOrEmpty(h.token)}"); }
                    Send(c, Msg.Reject, new Reject { reason = "pairing-required" }); return;
                }
                if (usbHeadset && !Auth.CheckToken(h.token, c.Loopback)) Log.Info("headset accepted without pairing: --usb-dev and a local (USB-forwarded) connection");
                c.Authed = true; c.Role = h.role ?? ""; c.ClientId = h.clientId ?? ""; c.Device = h.deviceName ?? "";
                // one live connection per role instance: a reconnect replaces the stale socket instead of duplicating the client
                foreach (var old in Clients.Values.Where(o => o != c && o.Authed && o.ClientId == c.ClientId && c.ClientId.Length > 0).ToList())
                { try { old.Ws.Abort(); } catch { } }
                Log.Info($"client connected: role={c.Role} device={c.Device} remote={c.Remote} loopback={c.Loopback}");
                if (c.Role == Roles.Quest && Guid.TryParseExact(h.roomSession, "N", out _) &&
                    (!_roomSessions.TryGetValue(c.ClientId, out var previous) || previous != h.roomSession))
                {
                    _roomSessions[c.ClientId] = h.roomSession;
                    int packed = State.world.PackRoom();
                    if (packed > 0) World.Changed();
                    Log.Info($"room: new headset session, {packed} item(s) put away before the first snapshot");
                }
                Send(c, Msg.Welcome, new Welcome { sessionId = SessionId, hostTime = HostClock.Now, hostName = Environment.MachineName });
                Send(c, Msg.Snapshot, BuildSnapshot());
                Coord.OnClientJoined(c.Role);
                MarkDirty(false);
            }
            else if (env.type == Msg.PairRequest)
            {
                var p = env.Body<PairRequest>();
                var r = Auth.TryPair(p?.code, p?.deviceName);
                Log.Info($"pair attempt from {c.Remote}: {(r.ok ? "ok" : r.reason)}");
                Send(c, Msg.PairResult, r);
            }
            else if (env.type == Msg.Ping) { var p = env.Body<Ping>(); if (p != null) Send(c, Msg.Pong, new Pong { c = p.c, h = HostClock.Now }); }
            return;
        }

        if(env.type.StartsWith("gomoku.",StringComparison.Ordinal)){if(IsMr(c)&&(c==tableHeadset||tableHeadset==null)){tableHeadset=c;Table.Packet(env);}return;}
        switch (env.type)
        {
            case Msg.Ping: { var p = env.Body<Ping>(); if (p != null) Send(c, Msg.Pong, new Pong { c = p.c, h = HostClock.Now }); break; }
            case Msg.PcStatus:
                if (c.Role == Roles.Pc)
                {
                    var prev = Pc; Pc = env.Body<PcStatus>();
                    // other clients (the headset) act on where the PC partner is: tell them when it meaningfully changes.
                    // What it IS doing (mode, display, window) is told at once; that it merely walked on is told at most
                    // twice a second (Tick), measured from where the last snapshot said it stood - not five snapshots a second.
                    if (prev == null || Pc == null || prev.mode != Pc.mode || prev.monitorId != Pc.monitorId || prev.boundHwnd != Pc.boundHwnd || prev.boundMinimized != Pc.boundMinimized ||
                        prev.suspendedReason != Pc.suspendedReason) MarkDirty(false);
                    else if (Math.Abs(Pc.pxX - _pcSentX) > 24 || Math.Abs(Pc.pxY - _pcSentY) > 24) _pcMoved = true;
                    Coord.OnPcStatus();
                }
                break;
            case Msg.MrStatus: if (IsMr(c)) { Mr = env.Body<MrStatus>(); MrSeenAt = HostClock.Now; } break;
            case Msg.HeadPose: Broadcast(Msg.HeadPose, env.Body<HeadPose>(), o => o.Role == Roles.Pc); break;
            case Msg.TargetQuery: { var q = env.Body<TargetQuery>(); if (q != null) Send(c, Msg.TargetResult, Desktop.Resolve(q)); break; }
            case Msg.TransitionRequest: { var r = env.Body<TransitionRequest>(); if (r != null) { Tennis.Stop("the partner is asked to go somewhere"); Coord.OnRequest(c, r); } break; }
            case Msg.GameCommand: { var g = env.Body<GameCommand>(); if (g != null) Tennis.OnCommand(c, g); break; }
            case Msg.World: { var w = env.Body<WorldCommand>(); if (w != null) World.OnCommand(c, w); break; }
            case Msg.Market: { var m = env.Body<MarketCommand>(); if (m != null) Shop.OnCommand(c, m); break; }
            // glass games (GlassHost.cs): the hands in front of a display go on to the PC client, a command starts / stops / picks, the side that runs the game reports
            case Msg.GlassHand: { var h = env.Body<GlassHand>(); if (h != null) Glass.OnHand(c, h); break; }
            case Msg.GlassCommand: { var g = env.Body<GlassCommand>(); if (g != null) Glass.OnCommand(c, g); break; }
            case Msg.GlassState: { var g = env.Body<GlassState>(); if (g != null) Glass.OnState(c, g); break; }
            // through the glass: what a game sends out toward the user goes to the headsets, what became of it goes back to the side that runs the game
            case Msg.GlassOut: { var g = env.Body<GlassOut>(); if (g != null) Glass.OnOut(c, g); break; }
            case Msg.GlassOutResult: { var g = env.Body<GlassOutResult>(); if (g != null) Glass.OnOutResult(c, g); break; }
            // Screen Scamps (Swarm.cs): the headset runs it; what is behind the glass of a screen goes on to the PC client, which draws it inside the monitor
            // (and the PC client's answer - "I am drawing them" - goes back to the headsets)
            case Msg.SwarmState: { var g = env.Body<SwarmGlass>(); if (g != null) { bool fromMr = IsMr(c); if (fromMr || c.Role == Roles.Pc) Broadcast(Msg.SwarmState, g, o => fromMr ? o.Role == Roles.Pc : IsMr(o)); } break; }
            // documents (Docs.cs): the board in the headset, the file in the partner's hands on the PC, lpctl
            case Msg.Doc: { var d = env.Body<DocCommand>(); if (d != null) Docs.OnCommand(c, d); break; }
            case Msg.Work: { var w = env.Body<WorkCommand>(); if (w != null) Work.OnCommand(c, w); break; }
            // (C1) the one picture of what the user holds up, or why there is none - only a headset (or its simulator) is listened to
            case Msg.EyeFrame: { var f = env.Body<EyeFrame>(); if (f != null && IsMr(c)) Sight.OnFrame(c, f); break; }

            case Msg.TransitionEvent: { var e = env.Body<TransitionEventMsg>(); if (e != null) Coord.OnEvent(c, e); break; }
            case Msg.CalibBegin: { CalibratingMonitor = env.Body<MonitorCalibration>()?.monitorId ?? ""; MrSeenAt = HostClock.Now; Log.Info("calibration " + (CalibratingMonitor.Length > 0 ? "begin: " + CalibratingMonitor : "put aside (the headset was taken off)")); MarkDirty(false);
                break; }
            case Msg.CalibFlash:
            {
                var f = env.Body<CalibFlash>();
                if (f == null) break;
                _flashAt = HostClock.Now; MrSeenAt = HostClock.Now;
                Flash.Show(f, Desktop.Monitors);
                bool ko = Talk.LastLanguage != "English";
                // one line at most per stage and round: the user must never be nagged about their screens
                if (f.stage == "hint" && HostClock.Now - _calibSaidAt > 40) { _calibSaidAt = HostClock.Now; Say(ko ? "색이 켜진 모니터를 한 번씩만 쳐다봐 줘." : "Just glance at each screen that lit up.", "curious", 4, "calibration"); }
                else if (f.stage == "done") { _calibSaidAt = -1000; Say(ko ? (f.total > 1 ? $"화면 {f.total}개 다 찾았어!" : "화면 찾았어!") : (f.total > 1 ? $"Found all {f.total} screens!" : "Found your screen!"), "happy", 3, "calibration"); }
                else if (f.stage == "none") { _calibSaidAt = -1000; Say(ko ? "화면을 못 찾았어. 모니터를 보면서 \"화면 다시 찾아\"라고 말해 줘." : "I couldn't see your screens. Look at them and say \"find my screens\".", "worried", 6, "calibration"); }
                else if (f.stage == "nocamera") { _calibSaidAt = -1000; Say(ko ? "헤드셋 카메라를 쓸 수 없어서 화면을 못 찾겠어." : "I can't use the headset's camera, so I can't find your screens.", "worried", 6, "calibration"); }
                else if (f.stage == "partial") { _calibSaidAt = -1000; Say(ko ? $"화면 {f.total}개 중에 {f.found}개 찾았어. 나머지는 \"화면 다시 찾아\"라고 하면 다시 볼게." : $"I found {f.found} of {f.total} screens. Say \"find my screens\" and I'll look again.", "curious", 6, "calibration"); }
                break;
            }
            case Msg.CalibEnd: CalibratingMonitor = ""; MarkDirty(false); break;
            case Msg.CalibCommit: { var k = env.Body<MonitorCalibration>(); if (k != null) CommitCalibration(k); break; }
            case Msg.CalibClear: { State.calibrations.Clear(); MarkDirty(); break; }
            case Msg.PointerSample: { var s = env.Body<PointerSample>(); if (s != null) AddPointer(c, s); break; }
            case Msg.Utterance: { var u = env.Body<Utterance>(); if (u != null && !string.IsNullOrWhiteSpace(u.text)) Talk.OnText(c, u); break; }
            case Msg.VoiceListen: { var v = env.Body<VoiceListen>(); if (v != null) Talk.OnVoiceListen(c, v); break; }
            case Msg.VoiceClip: { var v = env.Body<VoiceClip>(); if (v != null) Talk.OnVoiceClip(c, v); break; }
            case Msg.ModuleCommand: { var m = env.Body<ModuleCommand>(); if (m != null) OnModuleCommand(m); break; }
            case Msg.TaskCommand: { var t = env.Body<TaskCommand>(); if (t != null) Tasks.OnCommand(t); break; }
            case Msg.Settings: { var s = env.Body<SettingsMsg>(); if (s != null) { State.settings = s; State.character.quietMode = s.quietMode; MarkDirty(); } break; }
            case Msg.Observation: { var o = env.Body<Observation>(); if (o != null) OnObservation(c, o); break; }
            case Msg.Debug: { var d = env.Body<DebugCommand>(); if (d != null) OnDebug(c, d); break; }
        }
    }

    void CommitCalibration(MonitorCalibration k)
    {
        var mon = ScreenMath.FindMonitor(Desktop.Monitors, k.monitorId);
        if (mon == null) { Log.Warn("calibration for unknown monitor " + k.monitorId); return; }
        var f = MonitorFrame.FromCalibration(k);
        k.widthM = f.widthM; k.heightM = f.heightM; k.pixelW = mon.bounds.w; k.pixelH = mon.bounds.h; k.stableId = mon.stableId;
        k.residualM = f.Residual(k.tl, k.tr, k.bl, k.br); k.savedUnixMs = HostClock.UnixMs;
        State.calibrations.RemoveAll(x => x.monitorId == k.monitorId && x.simulated == k.simulated);
        State.calibrations.Add(k);
        CalibratingMonitor = "";
        float aspectPx = mon.bounds.w / (float)mon.bounds.h, aspectM = k.widthM / Math.Max(1e-4f, k.heightM);
        Log.Info($"calibration saved: {k.monitorId} {k.widthM * 1000:0}x{k.heightM * 1000:0}mm residual={k.residualM * 1000:0.0}mm aspect px={aspectPx:0.000} m={aspectM:0.000} sim={k.simulated}");
        if (Math.Abs(aspectPx / aspectM - 1f) > 0.06f) Say("The calibrated screen shape doesn't match its resolution. Could you mark the corners again?", "worried", 6, "calibration aspect mismatch");
        MarkDirty();
    }

    void AddPointer(Client c, PointerSample s)
    {
        if (!_pointerHistory.TryGetValue(c.ClientId, out var list)) _pointerHistory[c.ClientId] = list = new List<PointerSample>();
        list.Add(s);
        if (list.Count > 600) list.RemoveRange(0, list.Count - 600);
    }

    /// <summary>
    /// Picks the pointed location for an utterance from the pointing history during the speech interval
    /// (not just the hand position when transcription finished): prefers pinch-confirmed samples, else the densest dwell.
    /// </summary>
    public PointerSample ResolvePointing(string clientId, double t0, double t1) =>
        _pointerHistory.TryGetValue(clientId, out var list) ? PointingDwell.Resolve(list, t0, t1) : null;

    /// <summary>
    /// The pointing and gaze samples of the headset the user wears, oldest first (Look.cs works out from them where
    /// the user means): those of c when it is a headset, otherwise - something typed at the PC, a button - those of
    /// the headset that is connected. Empty when there is none.
    /// </summary>
    public IReadOnlyList<PointerSample> PointerHistoryFor(Client c)
    {
        var mr = c != null && IsMr(c) ? c : Clients.Values.FirstOrDefault(x => x.Authed && IsMr(x));
        return mr != null && _pointerHistory.TryGetValue(mr.ClientId, out var list) ? list : Array.Empty<PointerSample>();
    }

    /// <summary>
    /// Carries out one understood intent (from the language model or from the built-in parser) and reports what really
    /// happened: ok = it was done or handed on, line = what the host itself has to say about it ("" when nothing).
    /// The caller decides what is said out loud. pt is what the user pointed at while speaking (may be null).
    /// </summary>
    public (bool ok, string line) ExecuteIntent(Client c, Intent it, Utterance u, PointerSample pt)
    {
        Log.Info($"utterance from {c.Role} [{u.source}]: '{u.text}' -> {it.kind} dur={it.durationMs} start={it.alsoStart} pointing={(pt != null && pt.valid ? $"{pt.monitorId} ({pt.u:0.00},{pt.v:0.00})" : "none")}");
        var res = new IntentResultMsg { text = u.text, kind = it.kind, durationMs = it.durationMs, alsoStart = it.alsoStart, pointer = pt, needsPointer = it.deictic && pt == null };
        bool ok = true; string line = "";
        bool toHeadsets = false;        // the result is for every headset, whoever asked (a stop, the menu)
        IntentSpoke = false;
        // (the user sends the partner somewhere: a show that was moving it - the market stall - is over; a show's own requests carry its source)
        if (u.source != Shows.ShowSource) Shows.UserMoved(it.kind);
        switch (it.kind)
        {
            case "createTimer":
            {
                var m = CreateTimer(it.durationMs > 0 ? it.durationMs : 25 * 60000);
                if (it.alsoStart) { TimerLogic.Start(m, HostClock.UnixMs); Broadcast(Msg.ModuleUpdate, m); }
                res.moduleId = m.moduleId;
                line = it.alsoStart ? $"I made a {TimerLogic.Format(m.durationMs)} timer and started it." : "I made a timer. Tell me when to start it.";
                break;
            }
            case "timerStart": case "timerPause": case "timerResume": case "timerReset": case "timerSetDuration":
            {
                var m = State.modules.LastOrDefault(x => x.kind == "timer");
                if (m == null) { ok = false; line = "There is no timer yet. Shall I make one first?"; break; }
                long now = HostClock.UnixMs;
                if (it.kind == "timerStart") ok = TimerLogic.Start(m, now, it.durationMs);
                else if (it.kind == "timerPause") ok = TimerLogic.Pause(m, now);
                else if (it.kind == "timerResume") ok = TimerLogic.Resume(m, now);
                else if (it.kind == "timerReset") ok = TimerLogic.Reset(m, now);
                else ok = it.durationMs > 0 && TimerLogic.SetDuration(m, it.durationMs, now);
                res.moduleId = m.moduleId;
                if (ok) { Broadcast(Msg.ModuleUpdate, m); MarkDirty(); line = TimerReply(it.kind, m); }
                else line = "The timer can't do that in its current state.";
                break;
            }
            case "quietOn": State.settings.quietMode = true; State.character.quietMode = true; MarkDirty(); break;
            case "quietOff": State.settings.quietMode = false; State.character.quietMode = false; MarkDirty(); break;
            case "rainOn": State.settings.rain = true; MarkDirty(); break;
            case "rainOff": State.settings.rain = false; MarkDirty(); break;
            case "cancel":
                // (W1) "cancel" with nothing crossing: what the workshop is making is stopped too (its model calls really stop)
                if (!Coord.Machine.IsActive) Workshop.CancelCurrent(say: false);
                Coord.CancelByUser("user said cancel"); Tasks.CancelRunning("user said cancel"); PcDo.CancelOperate("the user said cancel"); break;
            // going in, coming out and looking need the room's geometry and the pointing hand: the MR client acts on the
            // result below. What the host can already tell is refused here, so that the partner does not promise it.
            case "enterPortal":
                if (!IsMr(c)) { ok = false; line = "I need the headset on to go through the screen."; }
                else if (State.character.owner == Env.Pc) { ok = false; line = "I'm already inside the PC."; }
                else
                {
                    // WHERE it goes in ("sometimes it enters at a very strange place"): the spot pointed at ON PURPOSE
                    // round the sentence or right now, else where the user looks - never a hand ray that merely brushed
                    // a display, never an old sample (EnterRules). With nothing: no spot is sent, and the headset
                    // takes the display it looks at now, else the middle of the one nearest to the partner.
                    var spot = EnterRules.Target(PointerHistoryFor(c), u.t0, u.t1, HostClock.Now, out string why);
                    if (spot != null && ScreenMath.FindMonitor(Desktop.Monitors, spot.monitorId) == null) { why = "the display of that spot is not there (any more)"; spot = null; }
                    res.pointer = spot == null ? null : new PointerSample { t = HostClock.Now, monitorId = spot.monitorId, u = spot.u, v = spot.v, valid = true };
                    res.needsPointer = false;
                    Log.Info($"enter: target {(spot == null ? "left to the headset" : $"{spot.monitorId} ({spot.u:0.00}, {spot.v:0.00}) [{spot.source}]")} - {why}");
                }
                break;
            case "returnToMr":
                if (!IsMr(c)) { ok = false; line = "I need the headset on to come out of the screen."; }
                else if (State.character.owner == Env.Mr) { ok = false; line = "I'm already out here on the desk."; }
                break;
            // looking at a window and reading a file: with a headset the MR client says which window (the pointed one); with
            // no headset the partner is at the PC and the window is the one it lives in, or the one the user was last using
            // looking, feedback, reading: from where the partner is. With a headset the MR client says which window (the
            // one pointed or looked at); without one it is the window the user is working in.
            // LOOKING ("이 화면 뭐야?", "여기 어떤 폴더가 있어?") is the host's own, whoever asks: it works out where the
            // user means - the hand's dwell round the utterance, else the gaze's, else the newest sample, else the
            // partner's display - captures that whole display and answers (Look.cs). The headset gets the spot with the
            // result (to turn the partner towards it); a task.command "look" it may still send for it is dropped.
            case "lookScreen":
            {
                var (spot, refused) = Tasks.LookAsked(c, u);
                if (refused.Length > 0) { ok = false; line = refused; }
                else if (spot.Pointed) { res.pointer = new PointerSample { t = HostClock.Now, monitorId = spot.monitorId, u = spot.u, v = spot.v, valid = true }; res.needsPointer = false; }
                break;
            }
            case "analyze": case "readFile": case "revise":
                if (!IsMr(c)) Tasks.OnCommand(new TaskCommand { cmd = it.kind == "readFile" ? "read" : it.kind == "lookScreen" ? "look" : it.kind == "revise" ? "revise" : "analyze", prompt = u.text });
                break;
            case "playGame":
            {
                // No game comes with the partner: one that is not in the bag (world.games) is not started, whoever asks.
                // The partner says it does not have it yet, that it is free in the marketplace, and asks whether to get it.
                // WHICH game is meant is plain rules (GlassRules.PickGame): the pack whose title is named goes before the
                // kind of game the words name, and that before the model's own choice - and when nothing in the bag is
                // named, NOTHING is started ("오목 해보자" once started Vampire Night).
                var pick = GlassRules.PickGame(State.world.games, it.text, it.name, u.text);
                Log.Info($"game: '{WorldRules.Short(u.text, 60)}' (the model's game '{it.text}', its words '{WorldRules.Short(it.name, 40)}') -> {pick}");
                var pack = pick.Found ? State.world.games.FirstOrDefault(g => g != null && g.id == pick.packId) : null;
                if (pack?.id == SwarmRules.PackId && (c == null || !IsMr(c)))
                {
                    string path = "/demon-gate/";
                    bool interactive = !NoAutoOpen && !Environment.GetCommandLineArgs().Contains("--no-tray");
                    ok = !interactive || Program.OpenGamePage(Port, path);
                    line = interactive && ok ? "악마의 문을 열었어. 혼자 시작을 눌러 줘." : $"악마의 문 · http://127.0.0.1:{Port}{path}";
                    res.kind = "desktopGame";
                }
                else if(pack?.id==StarterGames.GomokuId){
                    string error=Table.Request(pt?.monitorId);ok=error.Length==0;line=ok?"TV에 오목판을 펼칠게.":error;res.kind="tableGame";
                }
                else if (pack != null && GlassRules.IsGlass(pack.template))
                {
                    // A GLASS game ("오목 하자", "뱀파이어 서바이벌 하자", "그림자 우산 하자") is started by the host - on the PC's
                    // side of the glass or on the desk, whoever asks (GlassHost.cs). The clients hear of it as glass.state,
                    // so the result carries a kind of its own: the headset must not start a game of its own for it as well.
                    var started = Glass.Start(c, pack.id, "voice");
                    ok = started.ok; line = started.line; res.kind = "glassGame"; res.text = Glass.S.packId;
                }
                else if (pack != null)
                {
                    // a game of the headset's own, or the ball game: the headset starts it by its name (rps | pop | redlight |
                    // tennis | keeper | stars) or by the pack's id - never by the sentence it was asked for with
                    if (!HasMr) { ok = false; line = "I need the headset on for that."; }
                    else if (Tennis.S.active) { ok = false; line = "We are already playing!"; }
                    else { res.text = pick.name; toHeadsets = true; }
                }
                else if (pick.offer.Length > 0 && Shop.MissingGame(pick.offer) is { Length: > 0 } noGame) { ok = false; line = noGame; }
                else { ok = false; line = "I don't have a game like that yet."; }
                break;
            }
            // tennis: the MR client knows the room (the desk, where the user is), so it starts the game - going into the
            // screen first when the partner is still on the desk
            case "calibrate":
                if (!HasMr) { ok = false; line = "I need the headset on for that."; }
                break;
            case "playTennis":
                if (Shop.MissingGame("tennis") is { Length: > 0 } noTennis) { ok = false; line = noTennis; }       // (not in the bag: see playGame)
                else if (!HasMr) { ok = false; line = "I need the headset on to play tennis."; }
                else if (Tennis.S.active) { ok = false; line = "We are already playing!"; }
                else toHeadsets = true;
                break;
            case "stopTennis":
                if(Table.Wanted.Length>0){Table.Stop();break;}
                if (Glass.Active) Glass.Stop("the user said so");       // ("stop the game" ends whatever game is on)
                else if (Tennis.S.active) Tennis.Stop("the user said so");
                else if (!IsMr(c)) { ok = false; line = "We are not playing right now."; }      // (a game on the desk is the headset's own: it is told)
                break;
            case "stop":
            {
                // The general STOP ("멈춰", "그만", "게임 멈춰", "stop"): whatever is on ends - the first of a glass game,
                // tennis, a crossing or task, then the activity / skill / tune (WorldRules.StopWhat). A game of the
                // headset's own (rock paper scissors, Star Pop ...) cannot be seen from here: every headset is told to
                // stop its game, exactly as "stop the game" always reached it (intent.result, kind "stopTennis").
                bool saidGame = it.name == "game", korean = Talk.LastLanguage == "Korean";
                // the partner is working on the computer (PcOperate.cs): THAT stops, at once, before anything else
                if (PcDo.CancelOperate("the user said stop"))
                {
                    Log.Info($"stop: '{WorldRules.Short(u.text, 40)}' ends: operating the computer");
                    line = OperateRules.CancelledLine(korean); IntentSpoke = true;      // (the run says that line itself as it ends)
                    res.kind = "stopTennis"; toHeadsets = true;
                    break;
                }
                var w = State.world;
                bool worldOn = w.doing.Length > 0 || w.doingSkill.Length > 0 || w.song != null || w.ambience != null;
                string what = WorldRules.StopWhat(Glass.Active, Tennis.S.active, Coord.Machine.IsActive || Tasks.Running, saidGame, HasMr, worldOn);
                Log.Info($"stop: '{WorldRules.Short(u.text, 40)}' ends: {what} (glass {(Glass.Active ? Glass.S.packId : "off")}, tennis {(Tennis.S.active ? "on" : "off")}, world {(worldOn ? "on" : "off")}, headset {(HasMr ? "connected" : "none")}{(saidGame ? ", a game was named" : "")})");
                switch (what)
                {
                    case "glass": Glass.Stop("the user said stop"); break;
                    case "tennis": Tennis.Stop("the user said stop"); break;
                    case "cancel": Coord.CancelByUser("user said stop"); Tasks.CancelRunning("user said stop"); break;
                    case "world": line = World.Run(c, new WorldCommand { cmd = "stop" }, true); IntentSpoke = line.Length > 0; break;      // (the Maker says what it put back)
                }
                if (line.Length == 0) line = WorldRules.StopLine(what, saidGame, korean);
                ok = what != "nothing";
                res.kind = "stopTennis"; toHeadsets = true;
                // (what a thing's script is doing in the headset cannot be seen from here: the headsets are told to stop even when nothing is on that the host knows of)
                if (!ok && HasMr) Broadcast(Msg.IntentResult, res, IsMr);
                break;
            }
            case "openMenu": case "closeMenu":
                // the bag (the menu board) is the headset's: it opens and closes it when it hears of this result
                if (!HasMr) { ok = false; line = "You can see it in the headset."; }
                else { toHeadsets = true; line = it.kind == "openMenu" ? "Here's my bag!" : "Okay, I closed it."; }
                break;
        }
        if (ok && toHeadsets) Broadcast(Msg.IntentResult, res, IsMr);
        else if (ok) Send(c, Msg.IntentResult, res);
        // what the intent has just changed (a timer, quiet mode, rain) is what the user asked for: its snapshot does not wait for the four-a-second pace
        if (_snapshotDirty) _snapshotUrgent = true;
        return (ok, line);
    }

    /// <summary>
    /// "이 MR 환경 지워 줘", "화면 닫아 줘", "상점 닫아", "다 치워": what is open is put away (CloseRules). what: screen |
    /// shop | menu | place | all, "" = whatever is open - the thing named when it is open, else the first of: the display
    /// brought into the room, the shop's board, the bag, the surroundings. The bag is the headset's and cannot be seen
    /// from here: it is told to close whenever it may be what is meant. Returns what was closed and the line for it.
    /// </summary>
    public (List<string> closed, string line) CloseThing(Client c, string what, Utterance u)
    {
        bool korean = Talk.LastLanguage == "Korean";
        what = CloseRules.What(what);
        bool screen = Screen.Shown.Length > 0, shop = Shop.BoardOpen, place = State.world.ambience != null;
        // (the bag: only counted when it is named - otherwise it would stand in front of the surroundings without anybody knowing whether it is open)
        var closed = CloseRules.Order(what, screen, shop, what is CloseRules.Menu or CloseRules.All && HasMr, place);
        foreach (string x in closed)
            switch (x)
            {
                case CloseRules.Screen: Screen.CloseBoard("asked to (" + (what.Length > 0 ? what : "whatever is open") + ")"); break;
                case CloseRules.Shop: Shop.CloseBoard(); break;
                case CloseRules.Place: State.world.ambience = null; World.Changed(); break;
            }
        // the bag is told to close when it was named, when everything goes, and when nothing else was found open
        bool menu = HasMr && (closed.Contains(CloseRules.Menu) || what == CloseRules.All || closed.Count == 0);
        if (menu) Broadcast(Msg.IntentResult, new IntentResultMsg { text = u?.text ?? "", kind = "closeMenu" }, IsMr);
        Log.Info($"close: '{WorldRules.Short(u?.text, 50)}' ({(what.Length > 0 ? what : "whatever is open")}; open: {(screen ? "screen " : "")}{(shop ? "shop " : "")}{(place ? "surroundings " : "")}{(HasMr ? "headset connected" : "no headset")}) -> closed: {(closed.Count > 0 ? string.Join(", ", closed) : "nothing")}{(menu && !closed.Contains(CloseRules.Menu) ? " (+ the bag was told to close)" : "")}");
        // (H6) the document board: put away with everything, and when nothing else was open
        if ((what == CloseRules.All || (what.Length == 0 && closed.Count == 0)) && Docs.IsOpen)
        {
            Docs.Close(false);
            if (closed.Count == 0) { MarkDirty(false, urgent: true); return (new List<string> { "document" }, DocsRules.Line("close", korean)); }
        }
        string line = closed.Count == 0 && menu ? (korean ? "가져온 화면은 없어. 가방은 닫아 뒀어." : "There's no screen out. I've closed the bag.") : CloseRules.Line(closed, korean);
        MarkDirty(false, urgent: true);
        return (closed, line);
    }

    // ------------------------------------------------------------------ the desktop (round 4)
    double _desktopAfterEnter = -1000, _desktopCheckAt = -1; string _desktopMonitor = "";

    /// <summary>
    /// "바탕화면으로 와": the partner is to be on the PC's DESKTOP. Inside the PC (bound to a window or a page, also a
    /// minimised one): the PC client is told to leave it (Msg.PcCommand "desktop"). In the room: it goes into the PC
    /// first (where EnterRules says) and is sent to the desktop when it has arrived. A moment later the host checks
    /// what the PC client reports and says so when it did not come free. Returns what was really started.
    /// </summary>
    public (bool ok, string line) GoDesktop(Client c, Utterance u, PointerSample pt)
    {
        bool ko = Talk.LastLanguage == "Korean";
        if (!HasPc) { Log.Info("desktop: asked, but no PC client is connected"); return (false, ko ? "PC 쪽 내 프로그램이 꺼져 있어서 바탕화면으로 못 가." : "My program on the PC isn't running, so I can't go to the desktop."); }
        if (State.character.owner == Env.Pc && !Coord.Machine.IsActive)
        {
            bool bound = Pc != null && Pc.mode == "WorkspaceBound";
            // the display: the one the user points or looks at when it is another one, else the one the partner is on
            var spot = Tasks.ResolveLook(c, u?.t0 ?? HostClock.Now - 2, u?.t1 ?? HostClock.Now);
            string monitor = spot.Pointed ? spot.monitorId : "";
            Broadcast(Msg.PcCommand, new PcCommand { cmd = "desktop", monitorId = monitor }, x => x.Role == Roles.Pc);
            Log.Info($"desktop: the PC client is told to leave {(bound ? (Pc.boundMinimized ? "the MINIMISED window it is bound to" : "the window it is bound to") : "nothing (it is " + (Pc?.mode ?? "unknown") + ")")} and walk the desktop of {(monitor.Length > 0 ? monitor : "the display it is on")}");
            _desktopCheckAt = HostClock.Now + 3; _desktopMonitor = monitor;
            return (true, bound ? (ko ? "응, 바탕화면으로 나올게!" : "Okay, coming out onto the desktop!") : (ko ? "나 지금 바탕화면에 있어!" : "I'm on the desktop now!"));
        }
        if (State.character.owner == Env.Pc) return (false, ko ? "지금 화면을 건너는 중이야. 조금만 기다려 줘." : "I'm in the middle of crossing. Give me a moment.");
        // in the room: into the PC first, then the desktop
        var went = ExecuteIntent(c, new Intent { kind = "enterPortal", text = u?.text ?? "" }, u ?? new Utterance { t0 = HostClock.Now - 2, t1 = HostClock.Now, source = "host" }, pt);
        if (!went.ok) return (false, ko ? HostLines.Ko(went.line) : went.line);
        _desktopAfterEnter = HostClock.Now;
        Log.Info("desktop: the partner is in the room: it goes into the PC first and is sent to the desktop when it has arrived");
        return (true, ko ? "응, 바탕화면으로 들어갈게!" : "Okay, I'm going in, onto the desktop!");
    }

    /// <summary>A crossing is over (Coordinator.Finish): when it was the way to the desktop, the PC client is now told to walk the desktop.</summary>
    public void AfterTransition(TransitionInfo info, bool completed)
    {
        if (HostClock.Now - _desktopAfterEnter > 60) return;
        _desktopAfterEnter = -1000;
        if (!completed || info == null || info.owner != Env.Pc) { Log.Info("desktop: the crossing did not end inside the PC: nothing more is done"); return; }
        string monitor = info.target?.monitorId ?? "";
        Broadcast(Msg.PcCommand, new PcCommand { cmd = "desktop", monitorId = monitor }, x => x.Role == Roles.Pc);
        Log.Info($"desktop: arrived inside the PC: the PC client is told to walk the desktop of {(monitor.Length > 0 ? monitor : "the display it is on")}");
        _desktopCheckAt = HostClock.Now + 3; _desktopMonitor = monitor;
    }

    /// <summary>Three seconds after the PC client was told to walk the desktop: is it free? When it is still bound, the partner says so - it never leaves "okay" standing with nothing done.</summary>
    void DesktopCheck(double now)
    {
        if (_desktopCheckAt < 0 || now < _desktopCheckAt) return;
        _desktopCheckAt = -1;
        bool free = Pc != null && Pc.mode == "DesktopRoaming";
        Log.Info($"desktop: three seconds on the PC client reports {(Pc == null ? "nothing (it is gone)" : Pc.mode + (Pc.boundMinimized ? " (minimised)" : ""))}{(free ? "" : " - it did NOT come onto the desktop")}");
        if (!free && Pc != null) Say(Talk.LastLanguage == "Korean" ? "어, 바탕화면으로 못 나왔어. 미안, 다시 말해 줄래?" : "Hm, I couldn't get out onto the desktop. Sorry - tell me again?", "worried", 4, "conversation: goDesktop check");
    }

    /// <summary>The line ExecuteIntent returned last has been SAID already (by the Maker, for a stop that put the room back): the caller does not say it again.</summary>
    public bool IntentSpoke;

    static string TimerReply(string kind, ModuleDef m) => kind switch
    {
        "timerStart" => $"{TimerLogic.Format(m.durationMs)} timer started!",
        "timerPause" => "I paused the timer.",
        "timerResume" => "The timer is running again.",
        "timerReset" => "I set the timer back to the start.",
        _ => $"I changed the timer to {TimerLogic.Format(m.durationMs)}.",
    };

    public ModuleDef CreateTimer(long durationMs, string name = null)
    {
        int n = State.modules.Count(x => x.kind == "timer") + 1;
        var m = TimerLogic.Create("mod-" + Guid.NewGuid().ToString("N")[..8], name ?? $"Timer {n}", durationMs, HostClock.UnixMs);
        State.modules.Add(m);
        Log.Info($"module created: {m.moduleId} kind=timer duration={durationMs}ms");
        Broadcast(Msg.ModuleUpdate, m);
        MarkDirty();
        return m;
    }

    void OnModuleCommand(ModuleCommand cmd)
    {
        long now = HostClock.UnixMs;
        if (cmd.cmd == "create") { var created = CreateTimer(cmd.durationMs > 0 ? cmd.durationMs : 25 * 60000, string.IsNullOrEmpty(cmd.name) ? null : cmd.name); if (cmd.value == "start") { TimerLogic.Start(created, now); Broadcast(Msg.ModuleUpdate, created); } return; }
        var m = string.IsNullOrEmpty(cmd.moduleId) ? State.modules.LastOrDefault(x => x.kind == cmd.kind) : State.modules.FirstOrDefault(x => x.moduleId == cmd.moduleId);
        if (m == null) return;
        bool ok = cmd.cmd switch
        {
            "start" => TimerLogic.Start(m, now, cmd.durationMs),
            "pause" => TimerLogic.Pause(m, now),
            "resume" => TimerLogic.Resume(m, now),
            "reset" => TimerLogic.Reset(m, now),
            "setDuration" => TimerLogic.SetDuration(m, cmd.durationMs, now),
            "setDisplay" => SetField(() => { m.display = cmd.value == "digits" ? "digits" : "ring"; m.version++; }),
            "setAlert" => SetField(() => { m.alert = cmd.value is "chime" or "silent" ? cmd.value : "wave"; m.version++; }),
            "place" => SetField(() => { m.placed = true; m.position = cmd.position; m.spaceEpoch = cmd.value; }),
            "delete" => SetField(() => State.modules.Remove(m)),
            _ => false,
        };
        if (ok) { Log.Info($"module {m.moduleId}: {cmd.cmd} -> status={m.status} v{m.version}"); Broadcast(Msg.ModuleUpdate, m); MarkDirty(); }
    }
    static bool SetField(Action a) { a(); return true; }

    void OnObservation(Client c, Observation o)
    {
        // Observations carry their source and confidence. Simulated ones are logged as such and never treated as real evidence.
        Log.Info($"observation [{o.source} conf={o.confidence:0.00}]: {o.fact} | {o.detail}");
        Log.Jsonl(Path.Combine(HostPaths.Logs, "observations.jsonl"), o);
        if (State.settings.quietMode || !State.settings.cameraEnabled) return;
        Tasks.OnObservation(o);
    }

    void OnDebug(Client c, DebugCommand d)
    {
        Log.Info($"debug from {c.Role}: {d.cmd} value={d.value} text='{d.text}' px=({d.px},{d.py})");
        switch (d.cmd)
        {
            case "freeze": case "play": case "slow": case "rewind": case "loop": case "stopLoop": Coord.OnDebug(d); break;
            case "overlay": State.settings.debugOverlay = d.value > 0.5f; MarkDirty(); break;
            case "say": Say(d.text, "neutral", 4, "debug"); break;
            case "cancel": Coord.CancelByUser("cancelled from lpctl"); Tasks.CancelRunning("cancelled from lpctl"); break;
            case "utter": OnUtteranceFromTool(c, d.text); break;
            // checks from lpctl on this PC only: `lpctl debug pcDo 0 0 0 "openApp|notepad"` / "scrollDown||<window handle>" (the window a key goes to is NAMED by the check: never the user's own)
            case "pcDo":
                if (c.Loopback && c.Role == Roles.Tool)
                {
                    string[] f = (d.text ?? "").Split('|');
                    IntPtr hwnd = f.Length > 2 && long.TryParse(f[2].Trim(), out long h) ? new IntPtr(h) : IntPtr.Zero;
                    var plan = PcRules.Plan(f[0], f.Length > 1 ? f[1] : "");
                    if (plan.Ok && plan.window && hwnd == IntPtr.Zero) { Log.Warn("pc: a check may only send a key to a window it names"); break; }
                    var o = PcDo.Do(c, f[0], f.Length > 1 ? f[1] : "", null, hwnd);
                    Say(o.line, o.ok ? "happy" : "worried", 4, "debug: pc");
                }
                break;
            // (H6) checks of documents from lpctl on this PC: a report on a topic; design options for what is at a pixel; the pixel that stands for the pointing hand for the next sentence
            case "docReport": if (c.Loopback && c.Role == Roles.Tool) Docs.ReportOn(d.text); break;
            case "docDesigns": if (c.Loopback && c.Role == Roles.Tool) Docs.DesignsAt(c, d.px, d.py, d.text); break;
            case "docPoint": if (c.Loopback && c.Role == Roles.Tool) Docs.TestPoint(d.px, d.py); break;
            // (C1) `lpctl debug sightExport 0 0 0 "<thing>"`: one of the partner's sculpted things is written as a .glb and carried into the PC ("" = the newest)
            case "sightExport": if (c.Loopback && c.Role == Roles.Tool) Sight.Act(c, SightRules.Export, d.text, null); break;
            // (C1) `lpctl debug eyeTest 0 0 0 "<picture>"`: a stand-in for the headset's picture, for checks with the simulator ("" = none again)
            case "eyeTest": if (c.Loopback && c.Role == Roles.Tool) Sight.SetTestImage(d.text); break;
            case "closeThing": if (c.Loopback && c.Role == Roles.Tool) Say(CloseThing(c, d.text, null).line, "neutral", 4, "debug: close"); break;
            // (round 4) checks from lpctl on this PC: the desktop; operating the computer for a goal (with LP_LOOK_TEST_IMAGE a picture stands in for the display and NO input is sent);
            // one real click + a typed text at a pixel the check names (a window of its own): `lpctl debug pcInputTest 0 <px> <py> "text"`
            case "goDesktop": if (c.Loopback && c.Role == Roles.Tool) { var g = GoDesktop(c, null, null); Say(g.line, g.ok ? "happy" : "worried", 4, "debug: desktop"); } break;
            case "pcOperate": if (c.Loopback && c.Role == Roles.Tool) { var o = PcDo.Operate(c, d.text, null); Say(o.line, o.ok ? "happy" : "worried", 4, "debug: operate"); } break;
            case "pcInputTest": if (c.Loopback && c.Role == Roles.Tool) PcDo.InputTest((int)d.px, (int)d.py, d.text); break;
            case "pointer":
                // the newest pointing sample of the MR client, for the pointing check (loopback tool only)
                if (c.Loopback && c.Role == Roles.Tool)
                {
                    var mrc = Clients.Values.FirstOrDefault(x => x.Authed && IsMr(x));
                    PointerSample last = mrc != null && _pointerHistory.TryGetValue(mrc.ClientId, out var hist) && hist.Count > 0 ? hist[^1] : null;
                    Send(c, Msg.Debug, new DebugCommand { cmd = "pointer", text = last == null ? "" : Proto.ToJson(last), value = (float)(last == null ? 0 : HostClock.Now - last.t) });
                }
                break;
            case "setOwner": if (!Coord.Machine.IsActive) { State.character.owner = d.text == Env.Pc ? Env.Pc : Env.Mr; Coord.Machine.Info.owner = State.character.owner; Coord.BroadcastTransition(); MarkDirty(); } break;
            case "allowProject":
                // projects are only ever added locally by the user (tray / lpctl on this PC), never by a remote client
                if (c.Loopback && c.Role == Roles.Tool) { var link = Proto.FromJson<ProjectLink>(d.text); if (link != null && Directory.Exists(link.path)) Tasks.AllowProject(link); else Log.Warn("allowProject rejected: path missing"); }
                break;
            case "setHeight": if (d.value > 0.03f && d.value < 0.4f) { State.character.heightM = d.value; MarkDirty(); } break;
            case "setDesign": if (Array.IndexOf(Designs.Ids, d.text) >= 0) { State.character.design = d.text; MarkDirty(); Log.Info("character design -> " + d.text); } break;
            case "nextDesign": State.character.design = Designs.Next(State.character.design); MarkDirty(); Log.Info("character design -> " + State.character.design); break;
            case "pairCode":
                // The code is what the tray shows on this PC. Only a loopback tool that already authenticated with the local
                // token gets it (used by the pairing-flow check); it is never broadcast, logged or put in a snapshot.
                if (c.Loopback && c.Role == Roles.Tool) Send(c, Msg.Debug, new DebugCommand { cmd = "pairCode", text = Auth.PairCode });
                break;
            default:
                // everything else is for a client (enter/return/pc*): relay it
                Broadcast(Msg.Debug, d, o => o != c);
                break;
        }
    }

    void OnUtteranceFromTool(Client tool, string text) => Typed(text, "lpctl", tool);

    /// <summary>The last line the partner said (the page on the PC shows it as its bubble).</summary>
    public string LastSaid = "";
    public long LastSaidMs;
    readonly Client _desk = new() { Role = "page", ClientId = "typed-at-the-pc", Device = "the page on this PC", Authed = true, Loopback = true, Remote = "127.0.0.1" };

    /// <summary>
    /// Something the user TYPED to the partner at the PC - in the box on the shop's page (POST /market/api/say), or with
    /// `lpctl say` / `lpctl utter`. It enters the conversation exactly as a sentence said to the partner by name:
    /// typed text is addressed by definition (Conversation.OnText), so nothing is put in front of it. It is carried
    /// out through the client WorldRules.TypedThrough names: the headset when one is connected (so that "들어가" or
    /// "테니스 하자" typed at the PC happens in the room), else the character on the PC, else nobody in particular -
    /// then the partner still answers, makes things and talks to the shop, and says what needs a headset.
    /// Core thread. false when there was nothing to say.
    /// </summary>
    public bool Typed(string text, string from, Client otherwise = null)
    {
        text = WorldRules.TypedText(text);
        if (text.Length == 0) return false;
        var authed = Clients.Values.Where(x => x.Authed).ToList();
        string role = WorldRules.TypedThrough(authed.Select(x => x.Role));
        var through = authed.FirstOrDefault(x => x.Role == role) ?? otherwise ?? _desk;      // (otherwise: lpctl itself, which may be watching for what comes back)
        double now = HostClock.Now;
        Log.Info($"typed to the partner ({from}), through {(through == _desk ? "no client" : through.Role)}: '{text}'");
        Talk.OnText(through, new Utterance { text = text, t0 = now - 1.5, t1 = now, source = "companion-text" });
        return true;
    }
}

public sealed class IntentResultMsg
{
    public string text = "", kind = "";
    public long durationMs;
    public bool alsoStart;
    public bool needsPointer;
    public PointerSample pointer;
    public string moduleId = "";
}
