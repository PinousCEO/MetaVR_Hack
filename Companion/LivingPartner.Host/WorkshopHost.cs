using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// THE WORKSHOP IN THE HOST (core.Workshop): every making is ONE JOB of the work queue (core.Work) from start to end.
///   - An object is made in the room. Only game development takes the partner into a calibrated display.
///     Other jobs report their progress in the queue wherever the partner is.
///   - ONE job runs at a time: what is asked for meanwhile is QUEUED (status "queued") and started in order.
///   - Cancel (the queue's button, or a word) really stops the model calls: the job's token is cancelled.
///   - Opening a finished job starts the game / puts the thing on the desk / opens the document.
/// The makers (Market.MakeGame, Maker.MakeItem, Maker.Teach) call Open / WriteAsync / Close; WriteAsync runs the agent
/// loop (Workshop.cs) and says its steps in the job. Everything but WriteAsync is touched on the core thread only.
/// </summary>
public sealed class WorkshopHost
{
    readonly Core _core;
    sealed class Ticket { public WorkJob job; public CancellationTokenSource cts; public Action onCancel; }
    readonly Dictionary<string, Ticket> _open = new();
    readonly List<(WorkJob job, Action start)> _waiting = new();
    readonly HashSet<string> _cancelled = new(), _sentIn = new();
    WorkJob _adopt;
    bool _wentIn; double _wentInAt, _nextLook;
    OpenAiWorkshopModel _model;
    ClaudeWorkshopModel _games;
    internal ClaudeWorkshopModel GameModel => _games ??= ClaudeGameClient.Open() is {} client ? new ClaudeWorkshopModel(client) : null;
    public const int MaxWaiting = 4;

    public WorkshopHost(Core core)
    {
        _core = core;
        core.Work.Cancelled += OnCancelled;
        core.Work.Opened += OnOpened;
    }

    bool Ko => _core.Talk.LastLanguage == "Korean";
    public object Diagnostics => new { working = _open.Count, waiting = _waiting.Count, wentIn = _wentIn, model = _model?.Name ?? WorkshopOptions.For(ScriptRequest.Thing).model, gameModel = ClaudeGameClient.Model };

    /// <summary>A making of the workshop is running: the next one waits its turn.</summary>
    public bool Occupied => _open.Count > 0;

    /// <summary>Would a job in the PC make the partner go in right now? (So that the maker can say so in its one line.)</summary>
    public bool WouldGoIn => _core.HasPc && _core.HasMr && _core.State.character.owner == Env.Mr && !_core.Coord.Machine.IsActive && _core.Shows.Calibrated().Count > 0;

    /// <summary>What the partner says when it takes a job: into the workshop, or right here.</summary>
    public (string ko, string en) StartLine(string kind) => kind == ScriptRequest.Thing
        ? ("응, 바로 뚝딱 만들어 줄게!", "Okay, I'll hammer it together right now!")
        : kind == ScriptRequest.Skill ? ("응, 배워 볼게! 작업 큐에 걸어 뒀어.", "Okay, let me learn that - it's in your work queue.")
        : kind == ScriptRequest.Game && WouldGoIn ? ("응, 이건 PC 작업실에 들어가서 만들게! 작업 큐에 걸어 둘게.", "I'll do this one in the workshop inside the PC - it's in your work queue.")
        : ("응, 작업 목록에 올려 뒀어.", "Okay, it's in your work queue.");

    /// <summary>A name for the queue, which shows English: the name itself when it is written in Latin letters, else the fallback.</summary>
    public static string EnglishOr(string name, string fallback) => !string.IsNullOrWhiteSpace(name) && name.All(ch => ch < 0x250) ? name.Trim() : fallback;

    /// <summary>
    /// A making begins (or the queued one whose turn has come is taken up). cts: cancelled when the user cancels the
    /// job. onCancel: what else has to happen then (the half-made thing is taken away), on the core thread.
    /// </summary>
    public WorkJob Open(string kind, string title, string asked, CancellationTokenSource cts = null, Action onCancel = null)
    {
        string where = kind == ScriptRequest.Thing ? "mr" : "pc";
        WorkJob job;
        if (_adopt != null) { job = _adopt; _adopt = null; job.kind = kind; job.where = where; _core.Work.Start(job, title); }      // (what it is was not always known when it was queued)
        else job = _core.Work.Begin(kind, title, asked, where, "workshop");
        _open[job.id] = new Ticket { job = job, cts = cts ?? new CancellationTokenSource(), onCancel = onCancel };
        if (WorkJob.InsidePc(job)) GoIn(job);
        return job;
    }

    /// <summary>Asked for while something else is being made: it waits in the queue. Returns the line said, null when the queue is full (the caller says its own "one at a time").</summary>
    public string Later(string kind, string title, string asked, Action start)
    {
        if (_waiting.Count >= MaxWaiting || start == null) return null;
        var job = _core.Work.Queue(kind, title, asked, kind == ScriptRequest.Thing ? "mr" : "pc", "workshop");
        _waiting.Add((job, start));
        Log.Info($"workshop: {job.id} waits its turn ({_waiting.Count} waiting)");
        string line = Ko ? "지금 하던 거 끝나면 바로 할게. 작업 큐에 걸어 뒀어!" : "Right after this one - it's in your work queue!";
        _core.Say(line, "happy", 4, "workshop: queued");
        return line;
    }

    public void Step(WorkJob job, string text, float progress = -1f, int round = -1) { if (job != null) _core.Work.Step(job, text, progress, round); }
    public void Rename(WorkJob job, string title) { if (job != null) _core.Work.Rename(job, title); }
    public bool WasCancelled(WorkJob job) => job != null && _cancelled.Contains(job.id);
    public CancellationToken Token(WorkJob job) => job != null && _open.TryGetValue(job.id, out var t) ? t.cts.Token : CancellationToken.None;

    /// <summary>The making is over (the maker says its own line). ok: resultKind (module | item | skill) and resultId say what came of it; else note says why not.</summary>
    public void Close(WorkJob job, bool ok, string resultKind = "", string resultId = "", string note = "")
    {
        if (job == null) return;
        _open.Remove(job.id);
        _core.Work.End(job, ok, resultKind, resultId, note, cancelled: _cancelled.Contains(job.id));
        _nextLook = 0;
    }

    void OnCancelled(WorkJob job) => Cancel(job, true);

    void Cancel(WorkJob job, bool say)
    {
        int at = _waiting.FindIndex(w => w.job == job);
        if (at >= 0) { _waiting.RemoveAt(at); _core.Work.End(job, false, note: "Taken off the queue.", cancelled: true); return; }
        if (!_open.TryGetValue(job.id, out var t)) return;        // (somebody else's job: its owner listens to the same event)
        _cancelled.Add(job.id);
        Log.Info($"workshop: {job.id} is cancelled: the model calls are stopped");
        try { t.cts.Cancel(); } catch (ObjectDisposedException) { }
        try { t.onCancel?.Invoke(); } catch (Exception e) { Log.Warn("workshop: cancel: " + e.Message); }
        _open.Remove(job.id);
        _core.Work.End(job, false, note: "Stopped.", cancelled: true);
        if (say) _core.Say(Ko ? "알겠어, 그건 그만둘게." : "Okay, I stopped that one.", "neutral", 4, "workshop: cancelled");
    }

    /// <summary>"Stop making that": the job that is running (else the newest waiting one) is cancelled. False when there is none.</summary>
    public bool CancelCurrent(bool say = true)
    {
        var job = _open.Values.Select(t => t.job).FirstOrDefault() ?? (_waiting.Count > 0 ? _waiting[^1].job : null);
        if (job == null) return false;
        Cancel(job, say);
        return true;
    }

    void OnOpened(WorkJob job)
    {
        var c = _core.Work.OpenedBy ?? _core.Clients.Values.FirstOrDefault(x => x.Authed);
        Log.Info($"workshop: {job.id} opened: {job.resultKind} {job.resultId}");
        try
        {
            switch (job.resultKind)
            {
                case "module":
                    if (_core.State.world.games.FirstOrDefault(g => g.id == job.resultId) is { } pack && c != null) _core.Shop.PlayPack(c, pack, null, null);
                    break;
                case "item":
                    if (_core.State.world.items.FirstOrDefault(i => i.id == job.resultId) is { } item) _core.Shop.UseThing(c, item);
                    break;
                case "skill":
                    _core.World.Run(c, new WorldCommand { cmd = "useSkill", id = job.resultId }, true);
                    break;
                case "music": case "ambience":
                    _core.World.Run(c, new WorldCommand { cmd = job.resultKind, id = job.resultId }, true);
                    break;
                case "ability":
                    var ability = _core.State.world.abilities.FirstOrDefault(a => a.id == job.resultId);
                    if (ability != null) _core.Say(ability.examples?.FirstOrDefault() ?? ability.name, "happy", 6, "workshop: how to ask");
                    break;
                case "file":
                    if (!_core.NoAutoOpen && File.Exists(job.resultId))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(job.resultId) { UseShellExecute = true });
                    break;
                case "doc":
                    _core.Docs.OnCommand(c, new DocCommand { cmd = "open", id = job.resultId });
                    break;
            }
        }
        catch (Exception e) { Log.Warn($"workshop: {job.id} could not be opened: {e.Message}"); }
    }

    // ------------------------------------------------------------------ into the PC and back
    void GoIn(WorkJob job)
    {
        if (!WouldGoIn || !_sentIn.Add(job.id)) return;
        var known = _core.Shows.Calibrated();
        var last = _core.Coord.Info.target;
        bool same = last != null && known.Any(m => m.id == last.monitorId) && (last.u != 0 || last.v != 0);
        string monitor = same ? last.monitorId : known.FirstOrDefault()?.id ?? "";
        var pointer = new PointerSample { t = HostClock.Now, monitorId = monitor, u = same ? Math.Clamp(last.u, 0.05f, 0.95f) : 0.5f, v = same ? Math.Clamp(last.v, 0.05f, 0.95f) : 0.6f, valid = true };
        Log.Info($"workshop: {job.id} is work for the PC: the partner goes in through {monitor} at ({pointer.u:0.00}, {pointer.v:0.00}){(same ? " (where it last went through)" : "")}");
        _core.Broadcast(Msg.IntentResult, new IntentResultMsg { text = "", kind = "enterPortal", pointer = pointer }, _core.IsMr);
        _wentIn = true; _wentInAt = HostClock.Now;
    }

    public void Tick(double now)
    {
        if (now < _nextLook) return;
        _nextLook = now + 0.3;
        // A game can start before the headset has finished finding its screen.
        foreach (var j in _core.Work.Open)
            if (WorkJob.InsidePc(j) && !_sentIn.Contains(j.id)) GoIn(j);
        bool pcWork = _core.Work.Open.Any(WorkJob.InsidePc);
        if (_wentIn && !_core.Coord.Machine.IsActive)
        {
            string owner = _core.State.character.owner;
            // the user called it out meanwhile (or it never got in): the work goes on in the queue, nobody is fetched
            if (owner == Env.Mr && now - _wentInAt > 10) _wentIn = false;
            else if (owner == Env.Pc && !pcWork && now - _wentInAt > 4)
            {
                _wentIn = false;
                if (_core.HasMr && _core.HasPc)
                {
                    Log.Info("workshop: the PC queue is empty: the partner comes back out with what it made");
                    _core.Broadcast(Msg.IntentResult, new IntentResultMsg { text = "", kind = "returnToMr" }, _core.IsMr);
                }
            }
        }
        // the next one of the queue, when nothing is being made any more
        if (_waiting.Count > 0 && _open.Count == 0 && !_core.World.Making && !_core.Shop.MakingNow)
        {
            var (job, start) = _waiting[0];
            _waiting.RemoveAt(0);
            Log.Info($"workshop: {job.id} '{job.asked}' has its turn");
            _adopt = job;
            try { start(); } catch (Exception e) { Log.Warn($"workshop: {job.id} could not be started: {e.Message}"); }
            if (_adopt != null) { _adopt = null; _core.Work.End(job, false, note: "It could not be started."); }
        }
    }

    // ------------------------------------------------------------------ the writing itself
    /// <summary>
    /// Writes the script for a job in the agent loop and says every step in the job. Called from the thread pool; the
    /// result has the shape the makers always got (ScriptResult). stage: the old workbench's stages (world.making).
    /// scale: how much of the job's bar the writing is (pictures may follow); a negative scale leaves the bar alone.
    /// With no key for the workshop, or when its model cannot be reached at all, the plain writer does it (ScriptWriter).
    /// </summary>
    public async Task<ScriptResult> WriteAsync(WorkJob job, ScriptRequest r, Action<string, int, float> stage, Action<string> log, CancellationToken ct, float scale = 0.9f)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Token(job));
        var o = WorkshopOptions.For(r.kind);
        bool game = r.kind == ScriptRequest.Game && job?.kind != "tool";
        IWorkshopModel model;
        if (game)
        {
            model = GameModel ?? throw new InvalidOperationException("게임을 만들려면 ANTHROPIC_API_KEY가 필요해.");
            o.model = model.Name;
            o.store = false;
        }
        else model = _model ??= OpenAiWorkshopModel.Open(o.model);
        int fixes = 0; string last = "";
        void Said(string text, float p, int round)
        {
            string st = text.StartsWith("Fixing") ? ScriptWriter.Fix : text.StartsWith("Critic") ? ScriptWriter.Review : text.StartsWith("Play-testing") || text.StartsWith("Checking") || text.StartsWith("Looking") ? ScriptWriter.Try : ScriptWriter.Write;
            if (text != last) { last = text; if (st == ScriptWriter.Fix) fixes++; }
            stage?.Invoke(st, Math.Max(1, fixes), p);
            _core.Post(() => _core.Work.Step(job, text, scale < 0 ? -1f : p * scale, round));
        }
        WorkshopResult res = null;
        if (model != null)
        {
            _core.Post(() => _core.Work.Worker(job, "workshop " + model.Name));
            res = await Workshop.MakeAsync(model, r, o, Said, log, linked.Token);
            if (res.cancelled) throw new OperationCanceledException(linked.Token);
            _core.Post(() => _core.Work.Worker(job, "workshop " + res.model));
            if (game || res.ok || res.versions.Count > 0) return res;
            log($"workshop: the agent loop got nowhere ({res.why}); the plain writer takes over");
        }
        _core.Post(() => _core.Work.Step(job, "Writing the code", scale < 0 ? -1f : 0.1f));
        var ai = _core.Talk.Ai;
        return await ScriptWriter.WriteAsync(new OpenAiScriptModel(ai, r.kind == ScriptRequest.Game), r, r.kind == ScriptRequest.Game ? ScriptWriter.GameExamples() : ScriptWriter.RoomExamples(r.kind == ScriptRequest.Skill),
            (st, n, p) => { stage?.Invoke(st, n, p); _core.Post(() => _core.Work.Step(job, ScriptWriter.StageText(st, n), scale < 0 ? -1f : p * scale)); }, log, linked.Token);
    }
}
