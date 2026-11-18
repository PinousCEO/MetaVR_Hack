using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LivingPartner.Windows
{
    /// <summary>
    /// THE WORK QUEUE ON THE PC. "A more complex piece of work: Bambi comes INTO the PC and hangs it in MY WORK QUEUE.
    /// So the program must be visible on the PC too" (the user, 2026-11-18). The host owns the list
    /// (StateSnapshot.work, Shared/Work.cs); here it is a storybook panel on the desktop:
    ///   - open jobs first: kind icon, title, the step line, a percent bar with the number, the time it has taken, the
    ///     last three log lines small, Cancel; queued ones dimmed; finished ones with a tick (or "Did not work out" and
    ///     why) and Open; Clear for the finished ones.
    ///   - it slides in from the right edge above the taskbar of the display the partner is on (else the home display)
    ///     when there is a job; its header DRAGS it anywhere (it stays there, also over the next start); the round
    ///     button folds it to a chip ("Workshop - 1 job, 62%"), a click on the chip unfolds it; 30 s after the last job
    ///     was finished and seen (or cleared) it slides away.
    ///   - while a job with where == "pc" is being worked on and the partner is inside this PC, it WORKS AT THE PANEL:
    ///     walks to it, hammers and types, looks its work over now and then; sparkles and paper bits fly at every new
    ///     step; when the job is done it cheers and the result's picture pops out of the panel.
    /// The panel is drawn in a window of its own (a LayeredOverlay the size of the panel, on its own Unity layer, so it
    /// is there whether or not the partner is drawn); that window is click-through like all of them, and exactly over
    /// the paper lies a ClickPad (PcDocs.cs) that takes the mouse - so only the panel is clickable, the rest of the
    /// desktop is untouched. One unit of PcCharacter/Work is one design pixel (0.27 mm on the glass).
    /// Debug (lpctl debug ...): workFake 0 0 0 one|two|done|step|finish|fail|empty|off - jobs made up here;
    /// workClick [1] 0 0 cancel|open|clear|fold|unfold - a press on that button (1: through the real hit test);
    /// workDrag 0 dx dy - the header is dragged by so many pixels; workShow - the panel is brought back.
    /// </summary>
    public sealed partial class PcApp
    {
        [Header("The work queue panel (PcCharacter/Work; one unit = one design pixel; a window of its own)")]
        [SerializeField] GameObject _wkRoot;
        [Tooltip("The paper's top-left corner: the open panel hangs under it (x to the right, y down).")]
        [SerializeField] Transform _wkTop;
        [SerializeField] FxQuad _wkPaper, _wkHead, _wkFoldBg, _wkClearBg;
        [SerializeField] UiSprite _wkHeadIcon;
        [SerializeField] StoryLabel _wkHeadTitle, _wkHeadCount, _wkFoldLabel, _wkClearLabel, _wkMore;
        [Tooltip("The pool of job cards (the app lays each out for what its job is: working, queued, finished).")]
        [SerializeField] Transform[] _wkCard;
        [SerializeField] FxQuad[] _wkCardBg, _wkBarBg, _wkBar, _wkBtnBg, _wkMark;
        [SerializeField] UiSprite[] _wkIcon;
        [SerializeField] StoryLabel[] _wkTitle, _wkTime, _wkStep, _wkPct, _wkLog, _wkBtn, _wkMarkLabel;
        [Tooltip("Folded: the chip, hanging from the same bottom-right corner.")]
        [SerializeField] Transform _wkChip;
        [SerializeField] FxQuad _wkChipBg, _wkChipBarBg, _wkChipBar;
        [SerializeField] StoryLabel _wkChipLabel;
        [Tooltip("Sparkles and paper bits at a new step, and the result's picture that pops out when a job is done.")]
        [SerializeField] UiSprite[] _wkSparks;
        [SerializeField] FxQuad[] _wkBits;
        [SerializeField] UiSprite _wkPop;
        [SerializeField] FxQuad _wkPopGlow;
        [Tooltip("Pictures of the kinds of work: thing, game, tool, skill, report, designs, model, pc, task.")]
        [SerializeField] Texture2D[] _wkKindTex;
        [Header("The partner at the panel (PcCharacter/WorkAt, in the partner's own window)")]


        public const int WorkLayer = 4;                     // (Unity's built-in "Water" layer: nothing else of this app is on it)
        static readonly string[] WkKinds = { "thing", "game", "tool", "skill", "report", "designs", "model", "pc", "task" };
        // the panel in design pixels: the paper, the room around it in its window (sparkles; the result popping out above)
        public const float WkPW = 540f, WkPad = 28f, WkPadTop = 124f, WkPadBottom = 24f, WkHeadH = 78f, WkMaxPH = 820f, WkCW = 512f, WkChipW = 430f, WkChipH = 70f;
        public const float WkBW = WkPW + 2f * WkPad, WkBH = WkMaxPH + WkPadTop + WkPadBottom;
        public const float WkEmTitle = 28f, WkEmStep = 25f, WkEmNote = 21f;
        const float WkSeenSeconds = 30f;
        const int WkFold = 1, WkCancel = 2, WkOpen = 3, WkClear = 4, WkHeadDrag = 5, WkChipHit = 6;

        struct WkHit { public float x0, y0, x1, y1; public int act; public string id; public Transform bg; public float bw, bh; }
        struct WkFit { public string s; public float w; }
        struct WkP { public float x, y, vx, vy, age, life, size, rot, spin; }

        LayeredOverlay _ovWork; ClickPad _wkPad; float _wkScale = 1f; float _wkIdle;
        MonitorDesc _wkMon; int _wkAx, _wkAy; bool _wkPlaced, _wkAnchored, _wkPrefsRead;
        float _wkIn, _wkPH = 200f, _wkPWNow = WkPW, _wkLayoutAt, _wkSeenT; bool _wkFolded = true, _wkDismissed, _wkRelocate, _wkLayoutDirty = true;
        int _wkRev = int.MinValue, _wkCount = -1, _wkFrame;
        readonly WkHit[] _wkHits = new WkHit[24]; int _wkHitN, _wkHover = -1;
        readonly Dictionary<StoryLabel, WkFit> _wkFits = new Dictionary<StoryLabel, WkFit>();
        readonly Dictionary<string, string> _wkLastStep = new Dictionary<string, string>(), _wkLastStatus = new Dictionary<string, string>();
        readonly List<string> _wkGone = new List<string>();
        string[] _wkSlotId; float[] _wkSlotP, _wkSlotY;
        readonly WkP[] _wkParts = new WkP[32];
        float _wkPopT = -1f, _wkPopX, _wkPopY; bool _wkKnown;
        // the mouse
        bool _wkMouseWas, _wkDragging; int _wkGrabX, _wkGrabY; float _wkDragMoved; RectI _wkRect, _wkPadSaid;
        float _wkDbgDragT = -1f; int _wkDbgFromX, _wkDbgFromY, _wkDbgDx, _wkDbgDy;
        // made-up jobs (debug)
        WorkView _wkFake;
        // the partner at the panel
        WorkJob _wkPcJob; float _wkPoseW, _wkCheerUntil, _wkWalkX = float.NaN; bool _wkSaidAt;
        readonly LivingPartner.Shared.Pose _wkPose = new LivingPartner.Shared.Pose();

        // The type's colour reaches the screen as linear light (the project is in linear colour): the palette's ink given
        // as it is came out a washed grey-brown. These are the ink (#3A2420) and the dimmer brown as they are to be SEEN -
        // dark on cream, for reading through a headset's passthrough.
        public static readonly Color WkInk = new Color(0.042f, 0.018f, 0.014f, 1f), WkDim = new Color(0.107f, 0.051f, 0.036f, 1f);      // (sRGB 0.23, 0.14, 0.13 and 0.36, 0.25, 0.21)

        static bool WkOpenJob(WorkJob j) => j.status == "working" || j.status == "queued";
        readonly List<WorkJob> _wkList = new List<WorkJob>();
        /// <summary>The jobs of the PC workshop (where == "pc"): a small thing hammered in the room is the room's to show.</summary>
        List<WorkJob> WorkJobs()
        {
            if (_wkFake != null) return _wkFake.jobs;
            var snap = _link.Connected ? _link.Snapshot : null;
            if (snap == null || snap.work == null || snap.work.jobs == null) return null;
            _wkList.Clear();
            var all = snap.work.jobs;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].where == "pc") _wkList.Add(all[i]);
            return _wkList;
        }
        MonitorDesc HomeMonitor()
        {
            string arg = Ui.Arg("-lp-monitor", null);
            MonitorDesc m = arg != null ? _mons.Find(x => x.id.EndsWith(arg, StringComparison.OrdinalIgnoreCase)) : null;
            return m ?? _mons.Find(x => x.primary) ?? (_mons.Count > 0 ? _mons[0] : null);
        }
        /// <summary>Where the paper (or the chip) is on the desktop, in pixels.</summary>
        RectI WorkPaperRect()
        {
            int w = Mathf.RoundToInt(_wkPWNow * _wkScale), h = Mathf.RoundToInt(_wkPH * _wkScale);
            return new RectI(_wkAx - w, _wkAy - h, w, h);
        }

        // ------------------------------------------------------------------ once a frame (Update)
        void WorkStep(float dt)
        {
            float now = Time.realtimeSinceStartup;
            if (_wkSlotId == null) { _wkSlotId = new string[_wkCard.Length]; _wkSlotP = new float[_wkCard.Length]; _wkSlotY = new float[_wkCard.Length]; for (int i = 0; i < _wkSlotId.Length; i++) _wkSlotId[i] = ""; }
            var jobs = WorkJobs();
            int count = jobs != null ? jobs.Count : 0, open = 0;
            _wkPcJob = null;
            bool firstList = !_wkKnown && jobs != null;
            // ---- what changed: a new job, a new step, one that is finished
            if (jobs != null)
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    var j = jobs[i]; if (j == null) continue;
                    if (WkOpenJob(j)) open++;
                    if (_wkPcJob == null && WorkJob.InsidePc(j)) _wkPcJob = j;
                    bool had = _wkLastStatus.TryGetValue(j.id, out string was);
                    if (!had || was != j.status)
                    {
                        _wkLastStatus[j.id] = j.status;
                        if (_wkKnown || WkOpenJob(j)) { _wkDismissed = false; _wkSeenT = 0f; }      // (what was finished before this client connected does not bring the panel up)
                        if (had && was == "working" && j.status == "done") WorkDone(j);
                        _wkLayoutDirty = true;
                    }
                    string step = j.step ?? "";
                    if (!_wkLastStep.TryGetValue(j.id, out string stepWas) || stepWas != step)
                    {
                        _wkLastStep[j.id] = step;
                        if (stepWas != null && j.status == "working" && step.Length > 0) WorkNewStep(j);
                        _wkLayoutDirty = true;
                    }
                }
                if (_wkLastStatus.Count > count)
                {
                    _wkGone.Clear();
                    foreach (var kv in _wkLastStatus) if (jobs.Find(x => x != null && x.id == kv.Key) == null) _wkGone.Add(kv.Key);
                    foreach (string id in _wkGone) { _wkLastStatus.Remove(id); _wkLastStep.Remove(id); }
                }
                _wkKnown = true;
                if (firstList && open == 0 && count > 0 && _wkFake == null) _wkDismissed = true;      // (only old finished work: nothing to come up for)
            }
            int rev = _wkFake != null ? _wkFake.revision : (_link.Snapshot != null && _link.Snapshot.work != null ? _link.Snapshot.work.revision : 0);
            if (rev != _wkRev || count != _wkCount)
            {
                if (count == 0 && _wkCount > 0) _wkSeenT = 0f;       // cleared: a while more, then it goes
                _wkRev = rev; _wkCount = count; _wkLayoutDirty = true;
            }

            // ---- is it to be seen
            bool live = _wkFake != null || (_link.Connected && _link.Snapshot != null);
            if (open > 0) { _wkSeenT = 0f; _wkDismissed = false; }
            else if (_wkIn >= 1f && (!_wkFolded || count == 0) && !_wkDragging && _wkHover < 0)
            {
                _wkSeenT += dt;      // (folded, a finished job has not been seen yet: the chip waits)
                if (_wkSeenT > WkSeenSeconds && !_wkDismissed) { _wkDismissed = true; Log($"work: nothing is open and the list was seen for {WkSeenSeconds:0} s - the panel slides away"); }
            }
            bool want = live && !_wkDismissed && _suspended.Length == 0 && _mons.Count > 0 && (count > 0 || (_wkIn > 0f && _wkAnchored)) && !_wkRelocate;
            if (count == 0 && _wkIn <= 0f) want = false;

            // ---- on which display, where
            if (want && !_wkAnchored) WorkAnchor();
            if (_wkAnchored && _wkMon != null)
            {
                var same = ScreenMath.FindMonitor(_mons, _wkMon.id);
                if (same == null) { _wkPlaced = false; _wkAnchored = false; _wkIn = 0f; }      // that display is gone
                else
                {
                    _wkMon = same;
                    if (!_wkPlaced && !_wkDragging)
                    {
                        // not put anywhere by the user: above the taskbar at the right edge - and with the partner, when it is on another display
                        if (_ch.owner == Env.Pc && _mon != null && _mon.id != _wkMon.id && _xferTo == null && _wkIn >= 1f) { _wkRelocate = true; Log($"work: the partner is on {_mon.id} - the panel moves there"); }
                        _wkAx = _wkMon.work.Right - Mathf.RoundToInt(16 * _wkScale); _wkAy = _wkMon.work.Bottom - Mathf.RoundToInt(12 * _wkScale);
                    }
                }
            }
            if (_wkRelocate && _wkIn <= 0f) { _wkRelocate = false; _wkAnchored = false; }
            float inWas = _wkIn;
            _wkIn = Mathf.MoveTowards(_wkIn, want ? 1f : 0f, dt / (want ? 0.45f : 0.3f));
            if (inWas <= 0f && _wkIn > 0f) { Log($"work: the panel slides in on {_wkMon?.id} at ({_wkAx},{_wkAy}) - {count} job(s), {open} open"); GlassSfx("open"); }
            if (inWas > 0f && _wkIn <= 0f) { Log("work: the panel is away"); _wkAnchored = false; }

            // ---- the mouse: a press on a button, the header dragged
            WorkMouse(dt);
            if (_wkAnchored && _wkMon != null && !_wkDragging)
            {
                // (unfolded near the top of a display: it stays whole on it)
                var pr = WorkPaperRect(); var mb = _wkMon.bounds;
                _wkAx = Mathf.Clamp(_wkAx, mb.x + Mathf.Min(pr.w, mb.w), mb.Right); _wkAy = Mathf.Clamp(_wkAy, mb.y + Mathf.Min(pr.h, mb.h), mb.Bottom);
            }

            // ---- the list as it is drawn
            if (_wkIn > 0f)
            {
                if (now >= _wkLayoutAt || _wkLayoutDirty) { _wkLayoutAt = now + 0.25f; _wkLayoutDirty = false; WorkLayout(jobs); }
                WorkAnimate(dt, jobs);
            }
        }

        /// <summary>The panel comes up: where it was put by the user, else above the taskbar of the partner's display (the home display when it is in the room).</summary>
        void WorkAnchor()
        {
            if (!_wkPrefsRead)
            {
                _wkPrefsRead = true;
                if (LayeredOverlay.ParkX == 0 && PlayerPrefs.GetInt("lp.work.placed", 0) == 1) { _wkPlaced = true; _wkAx = PlayerPrefs.GetInt("lp.work.x", 0); _wkAy = PlayerPrefs.GetInt("lp.work.y", 0); }
            }
            MonitorDesc m = null;
            if (_wkPlaced) { m = ScreenMath.FindMonitor(_mons, _wkAx - 40, _wkAy - 40); if (m == null) _wkPlaced = false; }
            if (m == null) m = _ch.owner == Env.Pc && _mon != null ? _mon : HomeMonitor();
            if (m == null) return;
            _wkMon = m; _wkAnchored = true;
            _wkScale = Mathf.Clamp(0.00019f / MetresPerPixel(m), 0.66f, 1.15f);
            float room = (m.work.h - 24f) / _wkScale - WkPadTop * 0.5f;      // what fits on this display
            _wkMaxPH = Mathf.Clamp(room, 360f, WkMaxPH);
            if (!_wkPlaced) { _wkAx = m.work.Right - Mathf.RoundToInt(16 * _wkScale); _wkAy = m.work.Bottom - Mathf.RoundToInt(12 * _wkScale); }
            _wkLayoutDirty = true;
        }
        float _wkMaxPH = WkMaxPH;

        // ------------------------------------------------------------------ the mouse
        void WorkMouse(float dt)
        {
            bool down = (Win32.GetAsyncKeyState(0x01) & 0x8000) != 0;
            bool have = Win32.GetCursorPos(out var cp);
            bool over = have && _wkPad != null && _wkPad.Shown && Win32.WindowFromPoint(cp) == _wkPad.Hwnd;
            int cx = cp.x - LayeredOverlay.ParkX, cy = cp.y;
            int hover = over ? WorkHitAt((cx - _wkRect.x) / _wkScale, (cy - _wkRect.y) / _wkScale) : -1;
            if (hover != _wkHover) { _wkHover = hover; }
            if (over) _wkSeenT = Mathf.Min(_wkSeenT, WkSeenSeconds - 8f);      // (the mouse is on it: it does not slide away under the hand)
            if (down && !_wkMouseWas && over) WorkPress((cx - _wkRect.x) / _wkScale, (cy - _wkRect.y) / _wkScale, cx, cy, "the mouse");
            else if (_wkDragging && _wkDbgDragT < 0f)
            {
                if (down && have) WorkDragTo(cx, cy);
                else WorkDragEnd();
            }
            _wkMouseWas = down;
            // a drag made up by the debug command: the same path, a point that moves for half a second
            if (_wkDbgDragT >= 0f)
            {
                _wkDbgDragT += dt;
                float u = M.Smooth(_wkDbgDragT / 0.5f);
                if (_wkDragging) WorkDragTo(_wkDbgFromX + Mathf.RoundToInt(_wkDbgDx * u), _wkDbgFromY + Mathf.RoundToInt(_wkDbgDy * u));
                if (_wkDbgDragT >= 0.5f) { _wkDbgDragT = -1f; WorkDragEnd(); }
            }
        }

        int WorkHitAt(float lx, float ly)
        {
            for (int i = _wkHitN - 1; i >= 0; i--) if (lx >= _wkHits[i].x0 && lx < _wkHits[i].x1 && ly >= _wkHits[i].y0 && ly < _wkHits[i].y1) return i;
            return -1;
        }

        /// <summary>The left button went down on the panel at (lx, ly) design pixels from the paper's top-left; (cx, cy) is that point on the desktop.</summary>
        void WorkPress(float lx, float ly, int cx, int cy, string how)
        {
            int i = WorkHitAt(lx, ly);
            _wkSeenT = 0f;
            if (i < 0) return;
            var h = _wkHits[i];
            switch (h.act)
            {
                case WkFold: _wkFolded = true; _wkLayoutDirty = true; GlassSfx("close"); Log($"work: folded to the chip ({how})"); break;
                case WkCancel: WorkSend("cancel", h.id, how); GlassSfx("tap"); break;
                case WkOpen: WorkSend("open", h.id, how); GlassSfx("pop"); break;
                case WkClear: WorkSend("clear", "", how); GlassSfx("page"); break;
                case WkHeadDrag:
                case WkChipHit:
                    _wkDragging = true; _wkGrabX = cx - _wkAx; _wkGrabY = cy - _wkAy; _wkDragMoved = 0f;
                    break;
            }
        }

        void WorkDragTo(int cx, int cy)
        {
            int ax = cx - _wkGrabX, ay = cy - _wkGrabY;
            _wkDragMoved += Mathf.Abs(ax - _wkAx) + Mathf.Abs(ay - _wkAy);
            if (_wkDragMoved < 5f) return;      // (a click, not a drag)
            // it stays whole on the display the mouse is on
            var m = ScreenMath.FindMonitor(_mons, cx, cy) ?? _wkMon;
            var r = WorkPaperRect(); var b = m.bounds;
            _wkAx = Mathf.Clamp(ax, b.x + r.w, b.Right); _wkAy = Mathf.Clamp(ay, b.y + r.h, b.Bottom);
            _wkMon = m; _wkPlaced = true;
        }

        void WorkDragEnd()
        {
            if (!_wkDragging) return;
            _wkDragging = false;
            if (_wkDragMoved < 5f)
            {
                if (_wkFolded) { _wkFolded = false; _wkLayoutDirty = true; _wkSeenT = 0f; GlassSfx("open"); Log("work: the chip was clicked - unfolded"); }
                return;
            }
            Log($"work: dragged to ({_wkAx},{_wkAy}) on {_wkMon.id} - it stays there");
            if (LayeredOverlay.ParkX == 0) { PlayerPrefs.SetInt("lp.work.placed", 1); PlayerPrefs.SetInt("lp.work.x", _wkAx); PlayerPrefs.SetInt("lp.work.y", _wkAy); PlayerPrefs.Save(); }
        }

        void WorkSend(string cmd, string id, string how)
        {
            if (_wkFake != null)
            {
                // made-up jobs: the list here answers as the host would
                if (cmd == "cancel") { var j = _wkFake.jobs.Find(x => x.id == id); if (j != null) { j.status = "cancelled"; j.endedAt = _link.HostNow; } }
                else if (cmd == "clear") _wkFake.jobs.RemoveAll(x => !WkOpenJob(x));
                _wkFake.revision++;
                Log($"work: {cmd} '{id}' ({how}) - on the made-up list, nothing sent");
                return;
            }
            _link.Send(Msg.Work, new WorkCommand { cmd = cmd, id = id ?? "" });
            Log($"work: work.command {cmd} '{id}' sent to the host ({how})");
        }

        // ------------------------------------------------------------------ laying the list out (four times a second, and when it changes)
        static void WkAt(Transform t, float x, float y) { t.localPosition = new Vector3(x, -y, t.localPosition.z); }
        static void WkShow(FxQuad q, bool on) { if (q.enabled != on) q.enabled = on; }
        static void WkShow(UiSprite s, bool on) { if (s.enabled != on) s.enabled = on; }

        void WkFitText(StoryLabel l, string s, float maxW)
        {
            s = s ?? "";
            if (_wkFits.TryGetValue(l, out var f) && f.s == s && Mathf.Approximately(f.w, maxW)) return;
            _wkFits[l] = new WkFit { s = s, w = maxW };
            string fit = s;
            if (l.Measure(s) > maxW)
                for (int n = s.Length - 1; n > 0; n--) { fit = s.Substring(0, n).TrimEnd() + "…"; if (l.Measure(fit) <= maxW) break; }
            l.text = fit;
        }

        void WkAddHit(float x0, float y0, float x1, float y1, int act, string id, Transform bg, float bw, float bh)
        {
            if (_wkHitN >= _wkHits.Length) return;
            _wkHits[_wkHitN++] = new WkHit { x0 = x0, y0 = y0, x1 = x1, y1 = y1, act = act, id = id, bg = bg, bw = bw, bh = bh };
        }

        Texture2D WorkKindTex(string kind)
        {
            int k = Array.IndexOf(WkKinds, kind ?? "");
            return _wkKindTex[k >= 0 && k < _wkKindTex.Length ? k : 0];
        }

        static string WkClock(double seconds)
        {
            int s = (int)Math.Max(0, seconds);
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        void WorkLayout(List<WorkJob> jobs)
        {
            _wkHitN = 0;
            int open = 0, finished = 0, count = jobs != null ? jobs.Count : 0; WorkJob lead = null;
            for (int i = 0; i < count; i++)
            {
                var j = jobs[i]; if (j == null) continue;
                if (WkOpenJob(j)) { open++; if (lead == null && j.status == "working") lead = j; } else finished++;
            }
            if (lead == null && open > 0) lead = jobs.Find(x => x != null && WkOpenJob(x));
            bool chip = _wkFolded;
            if (_wkChip.gameObject.activeSelf != chip) _wkChip.gameObject.SetActive(chip);
            if (_wkTop.gameObject.activeSelf == chip) _wkTop.gameObject.SetActive(!chip);
            if (chip)
            {
                // "Workshop - 1 job, 62%"
                string line = open > 0 ? $"Workshop – {open} job{(open == 1 ? "" : "s")}" + (lead != null && lead.status == "working" ? $", {Mathf.RoundToInt(Mathf.Clamp01(lead.progress) * 100f)}%" : "")
                    : finished > 0 ? $"Workshop – {finished} done" : "Workshop – all clear";
                WkFitText(_wkChipLabel, line, WkChipW - 96f);
                bool bar = lead != null && lead.status == "working";
                WkShow(_wkChipBarBg, bar); WkShow(_wkChipBar, bar);
                if (bar)
                {
                    float full = WkChipW - 110f, w = Mathf.Max(10f, full * Mathf.Clamp01(lead.progress));
                    Ui.SizePanel(_wkChipBar, w, 8f, 4f); _wkChipBar.transform.localPosition = new Vector3(-WkChipW + 78f + w * 0.5f, WkChipH - 55f, _wkChipBar.transform.localPosition.z);
                }
                _wkPH = WkChipH; _wkPWNow = WkChipW;
                WkAddHit(0, 0, WkChipW, WkChipH, WkChipHit, "", _wkChipBg.transform, WkChipW, WkChipH);
                return;
            }

            _wkPWNow = WkPW;
            WkFitText(_wkHeadCount, open > 0 ? $"{open} job{(open == 1 ? "" : "s")}" : finished > 0 ? $"{finished} done" : "all clear", 150f);
            float y = WkHeadH + 6f; int slot = 0, more = 0;
            bool footer = finished > 0;
            for (int i = 0; i < count; i++)
            {
                var j = jobs[i]; if (j == null) continue;
                float h = j.status == "working" ? 222f : j.status == "queued" ? 66f : 96f;
                if (slot >= _wkCard.Length || y + h + 64f > _wkMaxPH) { more++; continue; }
                WorkCard(slot, j, y, h);
                y += h + 10f; slot++;
            }
            for (int i = slot; i < _wkCard.Length; i++)
            {
                if (_wkCard[i].gameObject.activeSelf) _wkCard[i].gameObject.SetActive(false);
                _wkSlotId[i] = "";
            }
            if (count == 0) { _wkMore.text = "Nothing in the queue."; WkAt(_wkMore.transform, 24f, y + 22f); y += 50f; }
            else if (more > 0 || footer)
            {
                _wkMore.text = more > 0 ? $"+ {more} more" : "";
                WkAt(_wkMore.transform, 24f, y + 24f); y += 54f;
            }
            else _wkMore.text = "";
            WkShow(_wkClearBg, footer);
            _wkClearLabel.text = footer ? "Clear finished" : "";
            if (footer)
            {
                float cy = y - 54f + 24f, cx = WkPW - 18f - 105f;
                WkAt(_wkClearBg.transform, cx, cy); WkAt(_wkClearLabel.transform, cx, cy);
                WkAddHit(cx - 105f, cy - 22f, cx + 105f, cy + 22f, WkClear, "", _wkClearBg.transform, 210f, 44f);
            }
            _wkPH = y + 8f;
            Ui.SizePanel(_wkPaper, WkPW, _wkPH, 26f, 3f);
            _wkPaper.transform.localPosition = new Vector3(WkPW * 0.5f, -_wkPH * 0.5f, _wkPaper.transform.localPosition.z);
            // the header: all of it drags; the round button at its right folds
            WkAddHit(0, 0, WkPW, WkHeadH, WkHeadDrag, "", null, 0, 0);
            WkAddHit(WkPW - 76f, 12f, WkPW - 16f, 68f, WkFold, "", _wkFoldBg.transform, 48f, 48f);
        }

        void WorkCard(int slot, WorkJob j, float y, float h)
        {
            var c = _wkCard[slot];
            if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
            bool fresh = _wkSlotId[slot] != j.id;
            _wkSlotId[slot] = j.id; _wkSlotY[slot] = y;
            if (fresh) _wkSlotP[slot] = Mathf.Clamp01(j.progress);
            c.localPosition = new Vector3(14f, -y, 0f);
            bool working = j.status == "working", queued = j.status == "queued", done = j.status == "done", failed = j.status == "failed";
            var bg = _wkCardBg[slot];
            Ui.SizePanel(bg, WkCW, h, 20f, 2.4f);
            bg.transform.localPosition = new Vector3(WkCW * 0.5f, -h * 0.5f, bg.transform.localPosition.z);
            if (working) bg.SetColors(new Color(1f, 0.975f, 0.88f, 1f), new Color(1f, 0.93f, 0.76f, 1f));
            else if (queued) bg.SetColors(new Color(0.985f, 0.965f, 0.925f, 0.80f), new Color(0.96f, 0.93f, 0.88f, 0.80f));
            else if (done) bg.SetColors(new Color(0.95f, 0.985f, 0.93f, 1f), new Color(0.87f, 0.95f, 0.86f, 1f));
            else bg.SetColors(new Color(0.99f, 0.955f, 0.94f, 1f), new Color(0.97f, 0.89f, 0.87f, 1f));
            Color ink = WkInk, dim = WkDim, faint = new Color(dim.r, dim.g, dim.b, 0.60f);
            var icon = _wkIcon[slot]; var title = _wkTitle[slot]; var time = _wkTime[slot]; var step = _wkStep[slot]; var log = _wkLog[slot];
            var btn = _wkBtn[slot]; var btnBg = _wkBtnBg[slot]; var mark = _wkMark[slot]; var markLabel = _wkMarkLabel[slot];
            WkShow(_wkBarBg[slot], working); WkShow(_wkBar[slot], working);
            if (!working) _wkPct[slot].text = "";
            if (working)
            {
                WkShow(icon, true); icon.SetTexture(WorkKindTex(j.kind)); icon.SetColor(Color.white);
                icon.transform.localPosition = new Vector3(44f, -42f, icon.transform.localPosition.z); icon.transform.localScale = new Vector3(56f, 56f, 1f);
                WkAt(title.transform, 82f, 40f); title.color = ink; WkFitText(title, j.title, WkCW - 82f - 18f - 92f);
                WkAt(time.transform, WkCW - 18f, 40f); time.color = dim; time.text = WkClock(_link.HostNow - j.startedAt);
                step.characterSize = WkEmStep / 6.4f; step.color = ink; WkAt(step.transform, 20f, 88f); WkFitText(step, string.IsNullOrEmpty(j.step) ? "Getting ready ..." : j.step, WkCW - 40f);
                WkAt(_wkBarBg[slot].transform, 20f + 190f, 127f);
                WkAt(_wkPct[slot].transform, WkCW - 18f, 127f);
                // the last three lines of the log, newest last
                int n = j.log != null ? j.log.Count : 0;
                string a = n > 2 ? j.log[n - 3] : "", b = n > 1 ? j.log[n - 2] : "", d = n > 0 ? j.log[n - 1] : "";
                string src = a + "\n" + b + "\n" + d;
                if (!_wkFits.TryGetValue(log, out var lf) || lf.s != src)
                {
                    _wkFits[log] = new WkFit { s = src, w = 0 };
                    float maxW = WkCW - 40f - 140f;
                    log.text = (a.Length > 0 ? WkCut(log, a, maxW) + "\n" : "") + (b.Length > 0 ? WkCut(log, b, maxW) + "\n" : "") + WkCut(log, d, maxW);
                }
                WkAt(log.transform, 20f, 148f); log.color = dim;
                WorkButton(slot, "Cancel", WkCW - 18f - 62f, h - 14f - 22f, 124f, 44f, new Color(0.99f, 0.84f, 0.84f, 1f), new Color(0.96f, 0.71f, 0.72f, 1f), WkCancel, j.id, y);
                WkShow(mark, false); markLabel.text = "";
            }
            else if (queued)
            {
                WkShow(icon, true); icon.SetTexture(WorkKindTex(j.kind)); icon.SetColor(new Color(1f, 1f, 1f, 0.55f));
                icon.transform.localPosition = new Vector3(38f, -33f, icon.transform.localPosition.z); icon.transform.localScale = new Vector3(42f, 42f, 1f);
                WkAt(title.transform, 70f, 33f); title.color = faint; WkFitText(title, j.title, WkCW - 70f - 18f - 112f - 14f - 96f);
                WkAt(time.transform, WkCW - 18f - 112f - 14f, 33f); time.color = faint; time.text = "Queued";
                step.text = ""; _wkFits.Remove(step); log.text = ""; _wkFits.Remove(log);
                WorkButton(slot, "Cancel", WkCW - 18f - 56f, 33f, 112f, 40f, new Color(0.99f, 0.90f, 0.90f, 0.9f), new Color(0.97f, 0.80f, 0.81f, 0.9f), WkCancel, j.id, y);
                WkShow(mark, false); markLabel.text = "";
            }
            else
            {
                WkShow(icon, false);
                WkShow(mark, true);
                mark.transform.localPosition = new Vector3(40f, -36f, mark.transform.localPosition.z);
                if (done) mark.SetColors(new Color(0.62f, 0.84f, 0.62f, 1f), new Color(0.50f, 0.76f, 0.54f, 1f));
                else if (failed) mark.SetColors(new Color(0.97f, 0.72f, 0.70f, 1f), new Color(0.93f, 0.60f, 0.60f, 1f));
                else mark.SetColors(new Color(0.86f, 0.83f, 0.79f, 1f), new Color(0.80f, 0.76f, 0.72f, 1f));
                markLabel.text = done ? "✓" : failed ? "×" : "–"; WkAt(markLabel.transform, 40f, 36f);
                bool canOpen = done && !string.IsNullOrEmpty(j.resultKind);
                float right = canOpen ? 18f + 112f + 14f : 18f;
                WkAt(title.transform, 74f, 32f); title.color = done ? ink : dim; WkFitText(title, j.title, WkCW - 74f - right);
                time.text = "";
                string note = done ? (string.IsNullOrEmpty(j.note) ? "Done" : j.note) : failed ? "Did not work out" + (string.IsNullOrEmpty(j.note) ? "" : " – " + j.note) : "Cancelled";
                step.characterSize = WkEmNote / 6.4f; step.color = dim; WkAt(step.transform, 74f, 68f); WkFitText(step, note, WkCW - 74f - right);
                log.text = ""; _wkFits.Remove(log);
                if (canOpen) WorkButton(slot, "Open", WkCW - 18f - 56f, h * 0.5f, 112f, 46f, new Color(0.84f, 0.94f, 0.84f, 1f), new Color(0.71f, 0.86f, 0.72f, 1f), WkOpen, j.id, y);
                else { WkShow(btnBg, false); btn.text = ""; }
            }
        }

        string WkCut(StoryLabel l, string s, float maxW)
        {
            if (l.Measure(s) <= maxW) return s;
            for (int n = s.Length - 1; n > 0; n--) { string t = s.Substring(0, n).TrimEnd() + "…"; if (l.Measure(t) <= maxW) return t; }
            return "";
        }

        void WorkButton(int slot, string text, float cx, float cy, float w, float h, Color top, Color bottom, int act, string id, float cardY)
        {
            var bg = _wkBtnBg[slot]; var label = _wkBtn[slot];
            WkShow(bg, true);
            Ui.SizePanel(bg, w, h, h * 0.5f, 2.2f); bg.SetColors(top, bottom);
            WkAt(bg.transform, cx, cy); WkAt(label.transform, cx, cy + 1f);
            label.text = text;
            WkAddHit(14f + cx - w * 0.5f, cardY + cy - h * 0.5f, 14f + cx + w * 0.5f, cardY + cy + h * 0.5f, act, id, bg.transform, w, h);
        }

        // ------------------------------------------------------------------ what moves every frame
        void WorkAnimate(float dt, List<WorkJob> jobs)
        {
            float t = Time.time;
            // the bars fill smoothly; the number follows the bar
            if (!_wkFolded && jobs != null)
                for (int s = 0; s < _wkCard.Length; s++)
                {
                    if (_wkSlotId[s].Length == 0 || !_wkBar[s].enabled) continue;
                    WorkJob j = null;
                    for (int i = 0; i < jobs.Count; i++) if (jobs[i] != null && jobs[i].id == _wkSlotId[s]) { j = jobs[i]; break; }
                    if (j == null) continue;
                    float p = Mathf.Clamp01(j.progress);
                    _wkSlotP[s] = Mathf.MoveTowards(Mathf.Lerp(_wkSlotP[s], p, 1f - Mathf.Exp(-dt * 5f)), p, dt * 0.02f);
                    const float full = 380f;
                    float w = Mathf.Max(22f, full * _wkSlotP[s]);
                    Ui.SizePanel(_wkBar[s], w, 22f, 11f, 2f);
                    _wkBar[s].transform.localPosition = new Vector3(20f + w * 0.5f, -127f, _wkBar[s].transform.localPosition.z);
                    _wkBar[s].SetPhase(t * 1.2f);
                    int pct = Mathf.RoundToInt(_wkSlotP[s] * 100f);
                    if (_wkPctShown == null) { _wkPctShown = new int[_wkCard.Length]; for (int i = 0; i < _wkPctShown.Length; i++) _wkPctShown[i] = -1; }
                    if (_wkPctShown[s] != pct || _wkPct[s].text.Length == 0) { _wkPctShown[s] = pct; _wkPct[s].text = pct + "%"; }
                }
            // a button under the mouse grows a little
            for (int i = 0; i < _wkHitN; i++)
            {
                var h = _wkHits[i];
                if (h.bg == null || h.act == WkChipHit) continue;
                float k = i == _wkHover ? 1.07f : 1f;
                if (Mathf.Abs(h.bg.localScale.x - h.bw * k) > 0.01f) h.bg.localScale = new Vector3(h.bw * k, h.bh * k, 1f);
            }
            // sparkles and paper bits
            for (int i = 0; i < _wkParts.Length; i++)
            {
                bool spark = i < _wkSparks.Length; int bi = i - _wkSparks.Length;
                if (!spark && bi >= _wkBits.Length) break;
                ref WkP p = ref _wkParts[i];
                bool on = p.life > 0f && p.age < p.life;
                if (spark) WkShow(_wkSparks[i], on); else WkShow(_wkBits[bi], on);
                if (!on) continue;
                p.age += dt; p.vy += (spark ? 260f : 520f) * dt; p.x += p.vx * dt; p.y += p.vy * dt; p.rot += p.spin * dt;
                float u = p.age / p.life, a = Mathf.Clamp01((1f - u) * 2.2f), grow = Ui.Pop(Mathf.Clamp01(u * 5f));
                Transform tr = spark ? _wkSparks[i].transform : _wkBits[bi].transform;
                tr.localPosition = new Vector3(p.x, -p.y, tr.localPosition.z);
                tr.localRotation = Quaternion.Euler(0, 0, p.rot);
                tr.localScale = spark ? Vector3.one * p.size * grow * (1f - 0.4f * u) : new Vector3(p.size, p.size * 0.6f, 1f);
                if (spark) _wkSparks[i].SetColor(new Color(1f, 0.82f, 0.36f, a));
                else { var c = _wkBits[bi].color; c.a = a; _wkBits[bi].SetColors(c, c); }
            }
            // the result's picture: out of its card, up over the panel, a little dance, gone
            bool pop = _wkPopT >= 0f && !_wkFolded;
            WkShow(_wkPop, pop); WkShow(_wkPopGlow, pop);
            if (_wkPopT >= 0f)
            {
                _wkPopT += dt;
                if (_wkPopT > 3.4f) _wkPopT = -1f;
                else if (pop)
                {
                    float rise = M.Smooth(_wkPopT / 0.55f), a = Mathf.Clamp01((3.4f - _wkPopT) / 0.3f);
                    float x = Mathf.Lerp(_wkPopX, WkPW * 0.5f, rise), y = Mathf.Lerp(_wkPopY, -62f, rise) - 26f * Mathf.Sin(rise * Mathf.PI) + 5f * Mathf.Sin(t * 5f) * rise;
                    float size = Mathf.Lerp(48f, 104f, Ui.Pop(Mathf.Clamp01(_wkPopT / 0.5f)));
                    _wkPop.transform.localPosition = new Vector3(x, -y, _wkPop.transform.localPosition.z);
                    _wkPop.transform.localRotation = Quaternion.Euler(0, 0, 9f * Mathf.Sin(t * 6f) * rise);
                    _wkPop.transform.localScale = new Vector3(size, size, 1f);
                    _wkPop.SetColor(new Color(1f, 1f, 1f, a));
                    _wkPopGlow.transform.localPosition = new Vector3(x, -y, _wkPopGlow.transform.localPosition.z);
                    _wkPopGlow.transform.localScale = Vector3.one * size * (1.9f + 0.15f * Mathf.Sin(t * 4f));
                    _wkPopGlow.SetColors(new Color(1f, 0.84f, 0.42f, 0.55f * a * rise), new Color(1f, 0.96f, 0.84f, 0.55f * a * rise));
                }
            }
        }
        int[] _wkPctShown;

        /// <summary>Sparkles and bits of paper fly from a point of the paper (design pixels from its top-left).</summary>
        void WorkSparkle(float x, float y, int sparks, int bits, float spread = 60f)
        {
            for (int i = 0; i < _wkSparks.Length && i < sparks; i++)
            {
                float a = Mathf.Lerp(0.15f, 0.85f, (i + UnityEngine.Random.value * 0.6f) / Mathf.Max(1, sparks)) * Mathf.PI, v = UnityEngine.Random.Range(130f, 290f);
                _wkParts[i] = new WkP { x = x + UnityEngine.Random.Range(-spread, spread), y = y, vx = Mathf.Cos(a) * v, vy = -Mathf.Sin(a) * v, life = UnityEngine.Random.Range(0.8f, 1.3f), size = UnityEngine.Random.Range(28f, 50f), spin = UnityEngine.Random.Range(-160f, 160f) };
            }
            for (int i = 0; i < _wkBits.Length && i < bits; i++)
            {
                float a = UnityEngine.Random.Range(0.2f, 0.8f) * Mathf.PI, v = UnityEngine.Random.Range(160f, 330f);
                _wkParts[_wkSparks.Length + i] = new WkP { x = x + UnityEngine.Random.Range(-spread - 20f, spread + 20f), y = y, vx = Mathf.Cos(a) * v, vy = -Mathf.Sin(a) * v, life = UnityEngine.Random.Range(1.0f, 1.5f), size = UnityEngine.Random.Range(18f, 28f), rot = UnityEngine.Random.Range(0f, 180f), spin = UnityEngine.Random.Range(-420f, 420f) };
            }
        }

        int WorkSlotOf(string id) { for (int s = 0; s < _wkSlotId.Length; s++) if (_wkSlotId[s] == id) return s; return -1; }

        void WorkNewStep(WorkJob j)
        {
            int s = WorkSlotOf(j.id);
            if (s >= 0 && !_wkFolded && _wkIn > 0.5f) WorkSparkle(14f + 170f, _wkSlotY[s] + 88f, 7, 6, 110f);
            Log($"work: '{j.title}' - {j.step} ({Mathf.RoundToInt(j.progress * 100f)}%)");
        }

        void WorkDone(WorkJob j)
        {
            if (j.kind != "game") return;
            int s = WorkSlotOf(j.id);
            _wkPopX = 14f + 44f; _wkPopY = s >= 0 ? _wkSlotY[s] + 42f : WkHeadH + 40f; _wkPopT = 0f;
            Texture2D tex = null;
            var world = _link.Snapshot != null ? _link.Snapshot.world : null;
            if (j.resultKind == "item" && world != null && world.items != null) { var it = world.items.Find(x => x != null && x.id == j.resultId); if (it != null) tex = WorldIcon(it.icon); }
            _wkPop.SetTexture(tex != null ? tex : WorkKindTex(j.kind));
            if (!_wkFolded && _wkIn > 0.5f) WorkSparkle(WkPW * 0.5f, _wkPopY, 8, 6);
            bool mine = j.kind == "game" && _ch.owner == Env.Pc;
            if (mine) GlassSfx("ta-da");
            if (mine) { _wkCheerUntil = Time.realtimeSinceStartup + 2.6f; if (_rig.Visible) _sparks.Burst(_rig.HeadTopWorld + new Vector3(0, 0.01f, -0.01f), Vector3.right, Vector3.up, _H * 0.34f, 11); }
            Log($"work: '{j.title}' is done ({j.resultKind} {j.resultId}) - its picture pops out of the panel{(mine ? ", the partner cheers" : "")}");
        }

        // ------------------------------------------------------------------ the panel's window (LateUpdate)
        void WorkLate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool on = _wkIn > 0f && _wkAnchored && _wkMon != null && _suspended.Length == 0;
            if (!on)
            {
                if (_wkRoot.activeSelf) _wkRoot.SetActive(false);
                if (_wkPad != null && _wkPad.Shown) _wkPad.Hide();
                if (_ovWork != null)
                {
                    _ovWork.Hide(); _wkIdle += dt;
                    if (_wkIdle > 5f && _ovWork.Pending == 0) { _ovWork.Dispose(); _ovWork = null; _wkPad?.Dispose(); _wkPad = null; Log("work: the panel's window is given back"); }
                }
                return;
            }
            int bw = Mathf.CeilToInt(WkBW * _wkScale), bh = Mathf.CeilToInt(WkBH * _wkScale);
            if (_ovWork != null && (_ovWork.BufferW != bw || _ovWork.BufferH != bh) && _ovWork.Pending == 0) { _ovWork.Dispose(); _ovWork = null; _wkPad?.Dispose(); _wkPad = null; }
            if (_ovWork == null)
            {
                try { _ovWork = new LayeredOverlay(bw, bh, _encodeMaterial); _wkPad = new ClickPad(Mathf.CeilToInt(WkPW * _wkScale) + 2, Mathf.CeilToInt(WkMaxPH * _wkScale) + 2); }
                catch (Exception e) { Log("work: the panel's window could not be made: " + e.Message); _ovWork?.Dispose(); _ovWork = null; _wkPad = null; _wkDismissed = true; return; }
                Log($"work: panel window {bw}x{bh}px (scale {_wkScale:0.00}) and its click pad");
            }
            _wkIdle = 0f;
            if (!_wkRoot.activeSelf) _wkRoot.SetActive(true);
            // the paper (or the chip) hangs from the bottom-right corner of the buffer's inner frame
            _wkTop.localPosition = new Vector3(WkBW * 0.5f - WkPad - WkPW, -WkBH * 0.5f + WkPadBottom + _wkPH, 0f);
            _wkChip.localPosition = new Vector3(WkBW * 0.5f - WkPad, -WkBH * 0.5f + WkPadBottom, 0f);
            var r = WorkPaperRect();
            // it slides in from beyond the right edge of its display
            float slide = (1f - M.Smooth(_wkIn)) * (r.w + 48f * _wkScale + Mathf.Max(0, _wkMon.bounds.Right - _wkAx));
            var dest = new RectI(_wkAx - Mathf.RoundToInt((WkBW - WkPad) * _wkScale) + Mathf.RoundToInt(slide), _wkAy - Mathf.RoundToInt((WkBH - WkPadBottom) * _wkScale), bw, bh);
            RectI clip = _wkMon.bounds;
            if (_wkIn >= 1f) foreach (var m in _mons) clip = Union(clip, m.bounds);
            _wkRect = r;

            // every frame while something moves, else every third
            bool lively = _wkIn < 1f || _wkDragging || _wkPopT >= 0f || _wkHover >= 0 || WorkPartsAlive();
            if (lively || ++_wkFrame % 3 == 0)
            {
                var prevTarget = _cam.targetTexture; int prevMask = _cam.cullingMask;
                _cam.transform.SetPositionAndRotation(_wkRoot.transform.position + new Vector3(0, 0, -1f), Quaternion.identity);
                _cam.projectionMatrix = Matrix4x4.Ortho(-WkBW * 0.5f, WkBW * 0.5f, -WkBH * 0.5f, WkBH * 0.5f, _cam.nearClipPlane, _cam.farClipPlane);
                _cam.cullingMask = 1 << WorkLayer;
                _cam.targetTexture = _ovWork.Color;
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _ovWork.Color };
                if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
                else _cam.Render();
                _cam.cullingMask = prevMask; _cam.targetTexture = prevTarget;
                if (_ovWork.InsertAfter != Win32.HWND_TOPMOST) { _ovWork.InsertAfter = Win32.HWND_TOPMOST; _ovWork.ZOrderDirty = true; }
                _ovWork.Present(dest, clip);
            }
            // the pad that takes the mouse: exactly over the paper
            if (_wkIn >= 0.98f && !r.IsEmpty)
            {
                bool was = _wkPad.Shown;
                _wkPad.Place(r, LayeredOverlay.ParkX);
                if (!was || (!_wkDragging && Mathf.Abs(r.x - _wkPadSaid.x) + Mathf.Abs(r.y - _wkPadSaid.y) + Mathf.Abs(r.h - _wkPadSaid.h) > 80)) { _wkPadSaid = r; Log($"work: the click pad is over the {(_wkFolded ? "chip" : "panel")} at {r}{(LayeredOverlay.ParkX != 0 ? " (parked " + LayeredOverlay.ParkX + " px to the side)" : "")}"); }
            }
            else if (_wkPad.Shown) _wkPad.Hide();
        }

        bool WorkPartsAlive() { for (int i = 0; i < _wkParts.Length; i++) if (_wkParts[i].life > 0f && _wkParts[i].age < _wkParts[i].life) return true; return false; }

        /// <summary>For lpctl debug pcShot (PcGlass.cs): the panel's frame laid into the picture of a monitor.</summary>
        bool WorkBlend(Color32[] px, int w, int h, RectI area) => _ovWork != null && _ovWork.BlendInto(px, w, h, area);

        void WorkDispose() { _wkPad?.Dispose(); _wkPad = null; _ovWork?.Dispose(); _ovWork = null; }

        // ------------------------------------------------------------------ the partner at the panel (called by Roam)
        /// <summary>After the motor has moved the partner: while a job is worked on in this PC it goes to the panel and works at it.</summary>
        void WorkPose(float dt)
        {
            float now = Time.realtimeSinceStartup, t = Time.time;
            bool free = _ch.owner == Env.Pc && !_stOn && _dvT < 0f && !DocHeld && _xferTo == null && !_opOn && now > _opRestUntil && _mon != null;
            bool cheer = now < _wkCheerUntil && free;
            bool at = false;
            if (cheer) { PoseLib.Happy(_wkPose, t); _nextWander = Time.realtimeSinceStartupAsDouble + 5; }
            else if (_wkPcJob != null && free && _wkIn > 0.9f && _wkMon != null && !_wkDragging)
            {
                _nextWander = Time.realtimeSinceStartupAsDouble + 6;
                if (_bound != IntPtr.Zero) ComeOut("it works at the queue panel");
                bool here = _wkMon.id == _mon.id;
                float side = 1f; bool high = false;
                if (here)
                {
                    // beside the panel, on the side where there is room; under it when it hangs above its head
                    var r = WorkPaperRect();
                    float half = 0.40f * _H / Mpp;
                    LocalToPx(new Vector3(0, _floorYM, 0), out float _, out float floorPx);
                    high = r.Bottom < floorPx - 0.5f * _H / Mpp;      // (it was dragged up: the partner works under it)
                    side = r.x - 2.2f * half > _mon.bounds.x ? 1f : -1f;      // +1: the panel is at its right
                    float txPx = high ? r.x + r.w * 0.35f : side > 0f ? r.x - half : r.Right + half;
                    float tx = PxToLocal(txPx, 0).x;
                    if (Mathf.Abs(tx - _motor.Pos.x) > 0.10f * _H)
                    {
                        if (float.IsNaN(_wkWalkX) || Mathf.Abs(_wkWalkX - tx) > 0.05f * _H || !_motor.HasTarget)
                        {
                            _wkWalkX = tx;
                            _motor.MoveTo(new Vector2(tx, PlanBuilder.ResidentDepth(_H)), Mathf.Abs(tx - _motor.Pos.x) > 6f * _H, high ? 180f : 180f - side * 58f);
                            if (!_wkSaidAt) Log($"work: the partner walks to the panel (x {txPx:0} px) to work on '{_wkPcJob.title}'");
                        }
                    }
                    else if (!_motor.HasTarget) at = true;
                }
                else at = !_motor.HasTarget;      // the panel was put on another display: it works where it stands
                if (at)
                {
                    if (!_wkSaidAt) { _wkSaidAt = true; Log($"work: the partner is at the panel{(here ? "" : " (the panel is on another display)")} and works on '{_wkPcJob.title}'"); }
                    PoseLib.Work(_wkPose, t);
                }
            }
            if (!at) { if (_wkSaidAt && !cheer) { _wkSaidAt = false; _wkWalkX = float.NaN; if (_wkPcJob == null && _motor.Arrived) _motor.Face(180f); } }
            _wkPoseW = Mathf.MoveTowards(_wkPoseW, at || cheer ? 1f : 0f, dt / 0.25f);
            if (_wkPoseW > 0.001f) LivingPartner.Shared.Pose.Lerp(_pose, _wkPose, M.Smooth(_wkPoseW), _pose);
        }

        // ------------------------------------------------------------------ debug
        void WorkDebug(DebugCommand d)
        {
            string what = (d.text ?? "").Trim().ToLowerInvariant();
            switch (d.cmd)
            {
                case "workFake":
                {
                    double hn = _link.HostNow;
                    if (what == "off") { _wkFake = null; _wkLayoutDirty = true; Log("debug: the made-up jobs are gone - the host's list again"); break; }
                    if (_wkFake == null || what == "one" || what == "two" || what == "done" || what == "empty") _wkFake = new WorkView { revision = 1000 };
                    var jobs = _wkFake.jobs;
                    WorkJob Job(string id, string kind, string title, string status, float p, string step) => new WorkJob { id = id, kind = kind, title = title, where = "pc", status = status, progress = p, step = step, startedAt = hn - 84, asked = "made up" };
                    if (what == "one" || what == "two")
                    {
                        var a = Job("fake-1", "game", "Clone Range – a shooting game", "working", 0.62f, "Play-testing round 2");
                        a.round = 2; a.log.AddRange(new[] { "Wrote the first version (148 lines)", "Round 1: the clones never fire back", "Fixing: clones shoot every 2 s" });
                        jobs.Add(a);
                        if (what == "two") { var q = Job("fake-2", "report", "Report: desks in mixed reality", "queued", 0f, ""); q.startedAt = 0; jobs.Add(q); }
                    }
                    else if (what == "done")
                    {
                        var a = Job("fake-3", "designs", "New icon – 4 options", "done", 1f, "Done"); a.resultKind = "doc"; a.resultId = "fake-doc"; a.note = "4 options as PNG and one PDF sheet"; a.endedAt = hn;
                        var b = Job("fake-4", "model", "3D model of the shown mug", "failed", 0.4f, ""); b.note = "the picture was too dark to see the shape"; b.endedAt = hn;
                        jobs.Add(a); jobs.Add(b);
                    }
                    else if (what == "step")
                    {
                        var j = jobs.Find(x => x.status == "working");
                        if (j != null)
                        {
                            j.round++; j.progress = Mathf.Min(0.97f, j.progress + (d.value > 0f ? d.value : 0.12f));
                            j.step = j.round % 2 == 1 ? "Fixing: the ball never comes back" : "Play-testing round " + j.round;
                            j.log.Add(j.step); while (j.log.Count > 6) j.log.RemoveAt(0);
                        }
                    }
                    else if (what == "finish" || what == "fail")
                    {
                        var j = jobs.Find(x => x.status == "working");
                        if (j != null)
                        {
                            j.status = what == "finish" ? "done" : "failed"; j.progress = what == "finish" ? 1f : j.progress; j.endedAt = hn; j.step = "";
                            j.resultKind = what == "finish" ? "module" : ""; j.resultId = "fake-module"; j.note = what == "finish" ? "Ready to play – 3 rounds of testing" : "it kept crashing after 5 tries";
                            jobs.Remove(j); jobs.Insert(jobs.FindLastIndex(x => WkOpenJob(x)) + 1, j);
                            var next = jobs.Find(x => x.status == "queued"); if (next != null) { next.status = "working"; next.startedAt = hn; next.step = "Reading about it ..."; next.progress = 0.05f; }
                        }
                    }
                    _wkFake.revision++;
                    Log($"debug: made-up jobs '{what}' - {jobs.Count} in the list");
                    break;
                }
                case "workClick":
                {
                    int act = what == "cancel" ? WkCancel : what == "open" ? WkOpen : what == "clear" ? WkClear : what == "fold" ? WkFold : what == "unfold" ? WkChipHit : 0;
                    int i = -1; for (int k = 0; k < _wkHitN; k++) if (_wkHits[k].act == act) { i = k; break; }
                    if (i < 0) { Log($"debug: workClick - there is no '{what}' on the panel now"); break; }
                    float lx = (_wkHits[i].x0 + _wkHits[i].x1) * 0.5f, ly = (_wkHits[i].y0 + _wkHits[i].y1) * 0.5f;
                    int cx = _wkRect.x + Mathf.RoundToInt(lx * _wkScale), cy = _wkRect.y + Mathf.RoundToInt(ly * _wkScale);
                    if (d.value > 0.5f)
                    {
                        // through the hit test a real click goes through: is the pad the window Windows finds at that point?
                        if (_wkPad == null || !_wkPad.Shown) { Log("debug: workClick 1 - no click pad is shown"); break; }
                        IntPtr hit = Win32.WindowFromPoint(new Win32.POINT(cx + LayeredOverlay.ParkX, cy));
                        Log($"work: hit test at ({cx + LayeredOverlay.ParkX},{cy}) for '{what}': Windows finds {(hit == _wkPad.Hwnd ? "the click pad" : hit == IntPtr.Zero ? "no window" : "another window (" + Win32.WindowClass(hit) + ")")}");
                        if (hit != _wkPad.Hwnd) break;
                    }
                    WorkPress(lx, ly, cx, cy, "the debug command");
                    if (_wkDragging) WorkDragEnd();      // (the chip: a click without a drag)
                    break;
                }
                case "workDrag":
                    if (_wkIn < 1f || _wkHitN == 0) { Log("debug: workDrag - the panel is not up"); break; }
                    _wkDbgFromX = _wkRect.x + Mathf.RoundToInt(120 * _wkScale); _wkDbgFromY = _wkRect.y + Mathf.RoundToInt(30 * _wkScale);
                    _wkDbgDx = Mathf.RoundToInt(d.px); _wkDbgDy = Mathf.RoundToInt(d.py);
                    WorkPress(120f, 30f, _wkDbgFromX, _wkDbgFromY, "the debug command");
                    if (_wkDragging) { _wkDbgDragT = 0f; Log($"debug: the panel's header is dragged by ({_wkDbgDx},{_wkDbgDy}) px"); }
                    break;
                case "workShow": _wkDismissed = false; _wkSeenT = 0f; _wkFolded = false; _wkLayoutDirty = true; Log("debug: the panel is brought back"); break;
                case "workHome": _wkPlaced = false; if (LayeredOverlay.ParkX == 0) { PlayerPrefs.DeleteKey("lp.work.placed"); PlayerPrefs.Save(); } Log("debug: the panel goes back above the taskbar"); break;
            }
        }
    }
}
