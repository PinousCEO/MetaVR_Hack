using System;
using System.Collections.Generic;

namespace LivingPartner.Shared
{
    // THE WORK QUEUE - everything the partner is making or doing for the user that takes longer than a sentence.
    //
    // Games take the partner into the PC. In the room the partner hammers while making a 3D object.
    // Other tasks only update the queue. Queued work has no character animation.
    // The host owns the list (StateSnapshot.work); clients only draw it and send WorkCommand (Msg.Work).

    public sealed class WorkJob
    {
        public static bool IsObject(string kind) => kind == "thing" || kind == "item" || kind == "model";
        public static bool AtBench(WorkJob job) => job != null && job.status == "working" && job.where == "mr" && IsObject(job.kind);
        public static bool InsidePc(WorkJob job) => job != null && job.kind == "game" && job.where == "pc" && job.status == "working";
        public string id = "";
        public string kind = "";            // thing | game | tool | skill | report | designs | model | pc (operating the computer) | task (developer work)
        public string title = "";           // short, English ("Clone Range", "New icon - 4 options")
        public string asked = "";           // the user's own words
        public string where = "mr";         // mr | pc (see above)
        public string status = "queued";    // queued | working | done | failed | cancelled
        public string step = "";            // ONE short English line: what is being done right now ("Play-testing round 2", "Fixing: the ball never comes back")
        public float progress;              // 0..1; never goes back while working
        public int round;                   // write-and-try rounds so far (0 = not started)
        public List<string> log = new List<string>();   // the last steps, newest last (at most 6 short English lines)
        public string resultKind = "";      // module | item | skill | doc | file | "" - what came of it
        public string resultId = "";        // its id (a module / item / skill / document id, or a file path for "file")
        public string note = "";            // done: one line about the result; failed: why, in plain words
        public string worker = "";          // which worker did it ("workshop gpt-5.x")
        public double startedAt, endedAt;   // host clock seconds (StateSnapshot.hostTime); endedAt 0 = still open
    }

    /// <summary>In every snapshot: the open jobs first (oldest first), then the finished ones of this sitting (newest first, at most 8).</summary>
    public sealed class WorkView
    {
        public int revision;
        public List<WorkJob> jobs = new List<WorkJob>();
    }

    /// <summary>Client -> host (Msg.Work).</summary>
    public sealed class WorkCommand
    {
        // cancel (id) | clear (take the finished ones off the list) | open (id: show what came of it - the game starts, the
        // document opens, the thing is put on the desk)
        public string cmd = "";
        public string id = "";
    }
}
