using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// THE WORK QUEUE (the contract and the user's words: Shared/Work.cs). Everything the partner makes or does that takes
/// longer than a sentence is a job here: a thing hammered in the room (where "mr"), or real work in the PC workshop
/// (where "pc": a game, an app, a report, designs, a 3D model, operating the computer). The snapshot carries the list
/// (StateSnapshot.work), the PC client draws it as a panel, the headset as a strip the user can put anywhere.
/// All of it is touched on the core thread only (workers post: core.Post(() => core.Work.Step(...))).
/// </summary>
public sealed class WorkQueue
{
    readonly Core _core;
    readonly WorkView _view = new();
    const int KeepDone = 8, KeepLog = 6;

    public WorkQueue(Core core) { _core = core; }

    public WorkView View => _view;
    public object Diagnostics => new { open = _view.jobs.Count(j => j.endedAt == 0), done = _view.jobs.Count(j => j.endedAt > 0), revision = _view.revision };
    public IEnumerable<WorkJob> Open => _view.jobs.Where(j => j.endedAt == 0);
    public WorkJob Find(string id) => _view.jobs.FirstOrDefault(j => j.id == id);

    /// <summary>Asked to stop a job (the user pressed Cancel in the queue, or said so): whoever runs it listens here.</summary>
    public event Action<WorkJob> Cancelled;
    /// <summary>The user pressed a finished job: show what came of it.</summary>
    public event Action<WorkJob> Opened;

    void Changed() { _view.revision++; Sort(); _core.MarkDirty(false, urgent: true); }

    void Sort()
    {
        var open = _view.jobs.Where(j => j.endedAt == 0).OrderBy(j => j.startedAt).ToList();
        var done = _view.jobs.Where(j => j.endedAt > 0).OrderByDescending(j => j.endedAt).Take(KeepDone).ToList();
        _view.jobs.Clear(); _view.jobs.AddRange(open); _view.jobs.AddRange(done);
    }

    /// <summary>A new job. where: "mr" (hammered in the room) or "pc" (the PC workshop).</summary>
    public WorkJob Begin(string kind, string title, string asked, string where, string worker = "")
    {
        var j = new WorkJob
        {
            id = "job-" + Guid.NewGuid().ToString("N")[..8], kind = kind ?? "", title = title ?? "", asked = asked ?? "", where = where == "pc" ? "pc" : "mr",
            status = "working", worker = worker ?? "", startedAt = HostClock.Now,
        };
        _view.jobs.Add(j);
        Log.Info($"work: {j.id} begins - {j.kind} '{j.title}' ({j.where})");
        Changed();
        return j;
    }

    /// <summary>A job that has to WAIT its turn (status "queued": the workshop makes one thing at a time). Start takes it up.</summary>
    public WorkJob Queue(string kind, string title, string asked, string where, string worker = "")
    {
        var j = Begin(kind, title, asked, where, worker);
        j.status = "queued"; j.step = "Waiting for its turn";
        Changed();
        return j;
    }

    /// <summary>The turn of a queued job has come: it is being worked on from now (its time counts from here).</summary>
    public void Start(WorkJob j, string title = "")
    {
        if (j == null || j.endedAt > 0) return;
        j.status = "working"; j.step = ""; j.startedAt = HostClock.Now;
        if (!string.IsNullOrEmpty(title)) j.title = title;
        Log.Info($"work: {j.id} starts - {j.kind} '{j.title}' ({j.where})");
        Changed();
    }

    /// <summary>Who is doing it ("workshop gpt-5.6-sol").</summary>
    public void Worker(WorkJob j, string worker) { if (j != null && !string.IsNullOrEmpty(worker) && j.worker != worker) { j.worker = worker; Changed(); } }

    /// <summary>While Opened is raised: the client that pressed the job (null when the host itself did).</summary>
    public Client OpenedBy { get; private set; }

    /// <summary>What is being done right now (one short ENGLISH line) and how far it is (0..1; it never goes back; negative = leave it).</summary>
    public void Step(WorkJob j, string step, float progress = -1f, int round = -1)
    {
        if (j == null || j.endedAt > 0) return;
        if (!string.IsNullOrEmpty(step) && step != j.step)
        {
            j.step = step;
            j.log.Add(step);
            while (j.log.Count > KeepLog) j.log.RemoveAt(0);
            Log.Info($"work: {j.id} {step}");
        }
        if (progress >= 0 && float.IsFinite(progress)) j.progress = Math.Max(j.progress, Math.Clamp(progress, 0f, 0.99f));
        if (round >= 0) j.round = round;
        Changed();
    }

    public void Rename(WorkJob j, string title) { if (j != null && !string.IsNullOrEmpty(title) && j.title != title) { j.title = title; Changed(); } }

    /// <summary>The job is over. ok: it was made (resultKind / resultId say what); else note says why not, in plain words.</summary>
    public void End(WorkJob j, bool ok, string resultKind = "", string resultId = "", string note = "", bool cancelled = false)
    {
        if (j == null || j.endedAt > 0) return;
        j.endedAt = HostClock.Now;
        j.status = cancelled ? "cancelled" : ok ? "done" : "failed";
        if (ok) j.progress = 1f;
        j.resultKind = resultKind ?? ""; j.resultId = resultId ?? ""; j.note = note ?? "";
        j.step = cancelled ? "Cancelled" : ok ? "Done" : "It did not work out";
        Log.Info($"work: {j.id} {j.status} after {j.endedAt - j.startedAt:0}s - {j.resultKind} {j.resultId} {j.note}");
        Changed();
    }

    public void OnCommand(Client c, WorkCommand w)
    {
        if (w == null) return;
        switch (w.cmd)
        {
            case "cancel": { var j = Find(w.id); if (j != null && j.endedAt == 0) { Log.Info($"work: {j.id} cancel asked"); Cancelled?.Invoke(j); } break; }
            case "clear": { if (_view.jobs.RemoveAll(j => j.endedAt > 0) > 0) Changed(); break; }
            case "open": { var j = Find(w.id); if (j != null && j.status == "done") { OpenedBy = c; try { Opened?.Invoke(j); } finally { OpenedBy = null; } } break; }
        }
    }
}
