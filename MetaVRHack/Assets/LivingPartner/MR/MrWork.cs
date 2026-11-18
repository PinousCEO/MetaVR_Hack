using System.Collections.Generic;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// The work queue as the headset sees it (Shared/Work.cs): the host owns the list (StateSnapshot.work); here it is
    /// only read. The hammering at the bench (MrCraft) reads the open job with where == "mr"; the work strip
    /// (MrWorkStrip) lists all of them. Without a PC (local mode) there is no queue.
    /// For captures in the simulator a made-up queue can be fed: lpctl debug workFake (see OnDebugWork).
    /// </summary>
    public sealed partial class MrApp
    {
        static readonly List<WorkJob> NoJobs = new List<WorkJob>();
        WorkView _workFake;      // (the simulator: a made-up queue - lpctl debug workFake)

        /// <summary>The jobs of the queue: the open ones first (oldest first), then the finished ones (newest first). Never null.</summary>
        List<WorkJob> WorkJobs
        {
            get
            {
                if (_workFake != null) return _workFake.jobs;
                if (LocalMode || _snap == null || _snap.work == null || _snap.work.jobs == null) return NoJobs;
                return _snap.work.jobs;
            }
        }

        /// <summary>The host's clock now (the clock WorkJob.startedAt / endedAt are in).</summary>
        double WorkClock => _workFake != null ? Time.timeAsDouble : _snap != null ? _snap.hostTime : 0.0;

        static bool WorkOpen(WorkJob j) => j != null && (j.status == "queued" || j.status == "working");

        /// <summary>The job that is being hammered in the room just now (open, where == "mr"), or null.</summary>
        WorkJob MrWorkJob
        {
            get
            {
                var jobs = WorkJobs;
                for (int i = 0; i < jobs.Count; i++) if (WorkJob.AtBench(jobs[i])) return jobs[i];
                return null;
            }
        }

        void SendWork(string cmd, string id)
        {
            if (_workFake != null)
            {
                // (the made-up queue answers itself)
                var j = _workFake.jobs.Find(x => x.id == id);
                if (cmd == "cancel" && j != null) { j.status = "cancelled"; j.endedAt = Time.timeAsDouble; j.note = "Cancelled"; }
                if (cmd == "clear") _workFake.jobs.RemoveAll(x => !WorkOpen(x));
                Debug.Log($"[MR] work: (made-up queue) {cmd} {id}");
                return;
            }
            _link.Send(Msg.Work, new WorkCommand { cmd = cmd, id = id ?? "" });
            Debug.Log($"[MR] work: sent '{cmd}' for job {id}");
        }

        /// <summary>
        /// lpctl debug workFake v px py text - a made-up queue for captures:
        ///   text "mr:Title|step"  adds (or updates, by title) an open job hammered in the room, progress = v (0..1)
        ///   text "pc:Title|step"  the same for a job in the PC workshop
        ///   text "done:Title|note" finishes the job of that title (v &lt; 0.5: it failed)
        ///   text "" with v = 0    the made-up queue is dropped (the host's own is shown again)
        /// </summary>
        bool OnDebugWork(DebugCommand d)
        {
            if (OnDebugWorkStrip(d)) return true;      // workStrip (MrWorkStrip)
            if (d.cmd != "workFake") return false;
            string t = d.text ?? "";
            if (t.Length == 0) { _workFake = d.value > 0.5f ? new WorkView() : null; return true; }
            if (_workFake == null) _workFake = new WorkView();
            int c = t.IndexOf(':'); string head = c > 0 ? t.Substring(0, c) : "mr", rest = c > 0 ? t.Substring(c + 1) : t;
            int bar = rest.IndexOf('|'); string title = bar >= 0 ? rest.Substring(0, bar) : rest, line = bar >= 0 ? rest.Substring(bar + 1) : "";
            var j = _workFake.jobs.Find(x => x.title == title);
            if (head == "done")
            {
                if (j == null) return true;
                j.status = d.value >= 0.5f ? "done" : "failed"; j.progress = d.value >= 0.5f ? 1f : j.progress; j.note = line; j.endedAt = Time.timeAsDouble;
                _workFake.jobs.Remove(j); _workFake.jobs.Add(j);
            }
            else
            {
                if (j == null)
                {
                    j = new WorkJob { id = "fake-" + (_workFake.jobs.Count + 1), kind = head == "pc" ? "game" : "thing", title = title, asked = title, where = head == "pc" ? "pc" : "mr", startedAt = Time.timeAsDouble };
                    int open = 0; while (open < _workFake.jobs.Count && WorkOpen(_workFake.jobs[open])) open++;
                    _workFake.jobs.Insert(open, j);
                }
                j.status = "working"; j.step = line; j.progress = Mathf.Clamp01(d.value); j.round = Mathf.Max(1, j.round);
            }
            _workFake.revision++;
            Debug.Log($"[MR] work: made-up job '{title}' {head} {d.value:0.00} '{line}' ({_workFake.jobs.Count} jobs)");
            return true;
        }
    }
}
