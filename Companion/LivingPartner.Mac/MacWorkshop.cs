using System.Diagnostics;
using System.Net;
using LivingPartner.Host;
using LivingPartner.Shared;

namespace LivingPartner.Mac;

sealed partial class Stage
{
    sealed record QueuedWork(WorkJob Job, Func<WorkJob,CancellationToken,Task<Action>> Run, CancellationTokenSource Stop);
    readonly List<QueuedWork> waiting = new();
    readonly DocsBook documents = new();
    QueuedWork running;
    string enteredFor = "";
    bool needReturn;
    double enterAt;
    OpenAiClient contentModel;
    OpenAiClient Model
    {
        get
        {
            if (contentModel != null) return contentModel;
            string key = ServiceKey("OPENAI_API_KEY", "LivingPartner.OpenAI");
            return string.IsNullOrWhiteSpace(key) ? null : contentModel = new OpenAiClient(key.Trim());
        }
    }

    static string ServiceKey(string variable, string service)
    {
        string key = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(key) && OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo("/usr/bin/security") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "find-generic-password", "-s", service, "-w" }) start.ArgumentList.Add(arg);
            using var p = Process.Start(start);
            if (p != null && p.WaitForExit(1200) && p.ExitCode == 0) key = p.StandardOutput.ReadToEnd().Trim();
            else if (p != null && !p.HasExited) p.Kill();
        }
        return key?.Trim();
    }

    sealed class PhotoError(string message) : Exception(message);

    void InitWorkshop() { snap.work = new WorkView(); snap.docs = documents.View; }

    string QueueWork(string kind, string title, string words, Func<WorkJob,CancellationToken,Task<Action>> run)
    {
        if (waiting.Count >= 4) return "The queue is full. Try again when a job finishes.";
        var job = new WorkJob { id = Guid.NewGuid().ToString("N"), kind = kind, title = title, asked = words,
            where = WorkJob.IsObject(kind) ? Env.Mr : Env.Pc, worker = "Mac", step = "Queued", startedAt = Now };
        waiting.Add(new(job, run, new()));
        snap.work.jobs.Add(job);
        RefreshWork();
        return "Added to the queue.";
    }

    void RefreshWork()
    {
        var jobs = snap.work.jobs;
        snap.work.jobs = jobs.Where(j => j.status is "working" or "queued")
            .Concat(jobs.Where(j => j.status is not ("working" or "queued")).OrderByDescending(j => j.endedAt).Take(8)).ToList();
        snap.work.revision++;
    }

    void WorkStep(WorkJob job, string label, float progress)
    {
        lock (Sync)
        {
            if (job.status != "working") return;
            job.step = label; job.progress = Math.Clamp(Math.Max(job.progress,progress),0,.99f);
            job.log.Add(label);
            if (job.log.Count > 6) job.log.RemoveAt(0);
            snap.work.revision++;
        }
    }

    void TickWorkshop()
    {
        if (running == null && waiting.Count != 0)
        {
            running = waiting[0]; waiting.RemoveAt(0);
            running.Job.status = "working"; running.Job.startedAt = Now;
            WorkStep(running.Job, "Starting", .02f);
            var current = running;
            _ = Task.Run(() => ExecuteWork(current));
        }
        if (machine.IsActive) return;
        if (running != null && WorkJob.InsidePc(running.Job))
        {
            if (enteredFor != running.Job.id && T.owner == Env.Mr && Command("enter") == "")
            { enteredFor = running.Job.id; needReturn = true; enterAt = Now; }
        }
        else if (needReturn && Now - enterAt >= 4 && T.owner == Env.Pc)
        { if (Command("return") == "") needReturn = false; }
        else if (needReturn && T.owner == Env.Mr && Now - enterAt > 10) needReturn = false;
    }

    async Task ExecuteWork(QueuedWork task)
    {
        var j = task.Job;
        try
        {
            task.Stop.CancelAfter(TimeSpan.FromMinutes(24));
            var finish = await task.Run(j, task.Stop.Token);
            lock (Sync)
            {
                task.Stop.Token.ThrowIfCancellationRequested();
                finish(); j.status = "done"; j.step = "Done"; j.progress = 1;
            }
        }
        catch (OperationCanceledException) { lock (Sync) { j.status = "cancelled"; j.step = j.note = "The job stopped."; } }
        catch (Exception e)
        {
            // Provider replies can include request details. Only local user-facing errors enter snapshots.
            lock (Sync)
            {
                j.status = "failed";
                j.step = j.note = e is PhotoError || e is InvalidOperationException && e.Message.Any(c => c >= '가' && c <= '힣')
                    ? e.Message : "The job could not finish. Check the connection and creation settings.";
                if (e is PhotoError) Say(j.note);
            }
        }
        finally
        {
            string temp;
            lock (Sync) { pendingDocument.Remove(j.id,out temp); j.endedAt = Now; running = null; RefreshWork(); }
            if (temp != null && Directory.Exists(temp))
                try { Directory.Delete(temp,true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            task.Stop.Dispose();
        }
    }

    public object WorkFrame() => snap.work.jobs.Select(j => new { j.id,j.kind,j.title,j.step,j.status,j.progress,j.note,
        files = FilesOf(j).Select(f => new { f.name,url = "/work-file/" + j.id + "/" + Uri.EscapeDataString(f.name) }).ToArray() }).ToArray();

    IEnumerable<DocFile> FilesOf(WorkJob j) => j?.resultKind == "doc" && finishedDocuments.TryGetValue(j.resultId,out var doc) ? doc.files :
        j?.resultKind == "item" && GeneratedModelFile(j.resultId) is {} model ? new[] { model } :
        j?.resultKind == "file" && File.Exists(j.resultId) ? new[] { new DocFile { name = Path.GetFileName(j.resultId),path = j.resultId } } : Array.Empty<DocFile>();

    public IResult WorkFile(string id,string name)
    {
        var f = FilesOf(snap.work.jobs.Find(j => j.id == id)).FirstOrDefault(f => f.name == name);
        return f != null && File.Exists(f.path) ? Results.File(f.path, fileDownloadName: f.name) : Results.NotFound();
    }

    public IResult PreviewFor(IPAddress ip,string id,string name)
    {
        if (!MayFetch(ip)) return Results.StatusCode(403);
        if (!DocsRules.SafeServed(id,name) || documents.Find(id) is not {status: DocsRules.Ready} doc) return Results.NotFound();
        var path = Path.Combine(doc.folder,name);
        if (doc.options.Count == 1 && name == doc.options[0].picture && doc.files.Count == 1 && doc.files[0].kind == "png")
            path = doc.files[0].path;
        return File.Exists(path) ? Results.File(path,"image/png") : Results.NotFound();
    }

    public string WorkButton(string cmd,string id)
    {
        if (cmd == "clear") { snap.work.jobs.RemoveAll(j => j.status is not ("working" or "queued")); RefreshWork(); return ""; }
        var j = snap.work.jobs.Find(x => x.id == id);
        if (j == null) return "This job is missing from the queue.";
        if (cmd == "cancel")
        {
            var task = waiting.FirstOrDefault(x => x.Job == j);
            if (running?.Job == j) running.Stop.Cancel();
            else if (task != null) { waiting.Remove(task); task.Stop.Dispose(); }
            else return "";
            j.status = "cancelled"; j.step = "Cancel"; j.endedAt = Now; RefreshWork(); return "";
        }
        if (cmd != "open" || j.status != "done") return "Select a completed job.";
        switch (j.resultKind)
        {
            case "doc":
                if (documents.Find(j.resultId) == null && finishedDocuments.TryGetValue(j.resultId,out var saved))
                {
                    documents.Begin(saved.id,saved.kind,saved.title,saved.asked,saved.madeUnixMs,"",out _);
                    documents.Ready(saved.id,saved.title,saved.pages,saved.options,saved.files,saved.folder);
                }
                return documents.OpenDoc(j.resultId) ? "" : "Unable to open the document.";
            case "module": case "game": return GameButton(j.resultId);
            case "item": bag.Handle(Msg.World,new WorldCommand { cmd = "place",id = j.resultId }); return "";
            default: return "";
        }
    }

    bool WorkMessage(Envelope e)
    {
        if (e.type == Msg.PointerSample)
        {
            var s = e.Body<PointerSample>();
            if (s != null && screen != null)
            {
                if (!s.valid && s.gazeValid) s = new PointerSample { valid=true,monitorId=s.gazeMonitorId,u=s.gazeU,v=s.gazeV };
                if (s.valid && s.monitorId == screen.id && float.IsFinite(s.u) && s.u is >= 0 and <= 1 && float.IsFinite(s.v) && s.v is >= 0 and <= 1)
                { lastSpot = s; spotAt = Now; }
            }
            return true;
        }
        if (e.type == Msg.Work && e.Body<WorkCommand>() is {} w) { WorkButton(w.cmd,w.id); return true; }
        if (e.type != Msg.Doc) return false;
        var d = e.Body<DocCommand>(); if (d == null) return true;
        switch (d.cmd)
        {
            case "open": documents.OpenDoc(d.id); break;
            case "close": documents.Close(); break;
            case "page": documents.Page("",d.n); break;
            case "pick": documents.Pick(d.id,d.n); break;
            case "deliver": case "reveal": Say(DeliverDocument(d.id)); break;
            case "delete": documents.Delete(d.id); break;
        }
        return true;
    }
}
