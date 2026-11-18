using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using W = LivingPartner.MR.WorkStripView;

namespace LivingPartner.MR
{
    /// <summary>
    /// THE WORK STRIP: the ONE place in the room that says what the partner is working on - the work queue of the PC
    /// (Shared/Work.cs, StateSnapshot.work) as a small stack of paper cards. ("A task bar for the logo being made
    /// appeared - let me GRAB it and MOVE it, like a work queue" - the user, on the headset, 2026-11-18. Before, each
    /// feature had a strip of its own that floated beside the partner and could not be moved.)
    ///  - SHOWN only while there is a job: the open ones (oldest first), then the ones that finished in the last
    ///    DoneKeep seconds. At most three cards; "+2 more" on the handle says the rest. No job: no strip. Without a PC
    ///    (local mode) there is no queue and nothing is shown.
    ///  - A CARD: title, where it is worked (in the room: the partner hammers it at its bench - MrCraft; in the PC
    ///    workshop), one line of what is being done, a bar and the percentage. A FINISHED one has a tick and is
    ///    pressed to open what came of it (WorkCommand open); a failed one says why. An OPEN one has a small round
    ///    button: cancel (WorkCommand cancel). Presses are pokes through the face, as on every board (MrUiRay), and
    ///    nothing is pressed during the first 0.6 s it is up, nor while it is being moved.
    ///  - WHERE: it comes up beside the partner (on the user's left of it), or over the left of the desk while the
    ///    partner is in the PC - and stays there; it does not follow anything. THUMB AND FINGER CLOSED ON ITS HANDLE
    ///    (right on it, or with the ray on it) take it; it goes with the hand until they open, keeps facing the user,
    ///    and is where it was put for the rest of the sitting, whenever it is shown again.
    /// lpctl debug workStrip x y z "move" - as if it had been taken and put x metres to the right, y up, z further away;
    /// "home": back where it comes up by itself; "info": the log says what it shows. A made-up queue: workFake (MrWork).
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("The work queue in the room (MrCore/Panels/WorkStrip)")]
        [SerializeField] WorkStripView _work;

        const float DoneKeep = 30f, WkGuard = 0.6f;
        bool _wkInit, _wkPlaced, _wkUserPlaced, _wkDrag, _wkDragRay; int _wkDragHand, _wkRows = -1; float _wkDragReach, _wkDragLost, _wkShownAt = -10f;
        Vector3 _wkPos, _wkDragOffset; Quaternion _wkRot = Quaternion.identity;      // where its HANDLE is
        readonly WorkJob[] _wkJob = new WorkJob[W.Rows];
        readonly string[] _wkTitle = new string[W.Rows], _wkStep = new string[W.Rows], _wkWhere = new string[W.Rows];
        readonly int[] _wkPct = new int[W.Rows], _wkLook = new int[W.Rows];
        readonly float[] _wkBar = new float[W.Rows];
        readonly List<string> _wkDoneId = new List<string>(); readonly List<float> _wkDoneAt = new List<float>();      // when each finished job was first seen finished
        bool _wkSeenQueue; int _wkMore = -1;

        void InitWorkStrip()
        {
            _wkInit = true;
            var v = _work;
            for (int r = 0; r < W.Rows; r++)
            {
                int k = r;
                v.rowBtn[r].press = () => WorkRowPressed(k);
                v.cancel[r].press = () => WorkCancelPressed(k);
                _wkPct[r] = -1; _wkLook[r] = -1;
            }
            v.panel.gameObject.SetActive(false);
        }

        void WorkHostLost() { if (_work != null) { _work.panel.Hide(); _wkDrag = false; } }

        void WorkRowPressed(int r)
        {
            var j = _wkJob[r];
            if (j == null || WorkOpen(j) || Time.time - _wkShownAt < WkGuard) return;
            if (j.status != "done") { Caption(string.IsNullOrEmpty(j.note) ? "That one did not work out." : Cut(j.note, 80), 3.5f); return; }
            SendWork("open", j.id);
            Sfx("sparkle", _work.rows[r].position, 0.4f);
        }

        void WorkCancelPressed(int r)
        {
            var j = _wkJob[r];
            if (j == null || !WorkOpen(j) || Time.time - _wkShownAt < WkGuard) return;
            SendWork("cancel", j.id);
            Caption("Okay - I stop working on " + Cut(j.title, 28) + ".", 3f);
        }

        /// <summary>When was this finished job first seen finished (seconds of this app)? One that was already old when the queue was first seen counts as long gone.</summary>
        float WorkDoneAt(WorkJob j)
        {
            int at = _wkDoneId.IndexOf(j.id);
            if (at >= 0) return _wkDoneAt[at];
            bool old = !_wkSeenQueue && j.endedAt > 0 && WorkClock - j.endedAt > DoneKeep;
            if (_wkDoneId.Count >= 24) { _wkDoneId.RemoveAt(0); _wkDoneAt.RemoveAt(0); }
            _wkDoneId.Add(j.id); _wkDoneAt.Add(old ? -1000f : Time.time);
            if (!old && _wkSeenQueue) Sfx("sparkle", _wkPos, j.status == "done" ? 0.4f : 0.15f, j.status == "done" ? 1f : 0.7f);      // (it has just finished)
            return old ? -1000f : Time.time;
        }

        /// <summary>Where the strip's handle comes up by itself: beside the partner, on the user's left of it - or over the left of the desk while it is in the PC.</summary>
        Vector3 WorkStripHome()
        {
            Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            if (Input.Simulated) return head + Input.HeadRot * new Vector3(-0.17f, 0.065f, 0.50f);      // (in the simulator's picture, left of the middle)
            if (_ch.owner == Env.Mr && _rig.Visible)
            {
                Vector3 away = _rig.HeadWorld - head; away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized : fwd;
                return _rig.HeadWorld - Vector3.Cross(Vector3.up, away) * (W.W * 0.5f + 0.075f) + Vector3.up * 0.075f;
            }
            Vector3 left = -Vector3.Cross(Vector3.up, fwd);
            if (_homeSet) { Vector3 at = _home + left * 0.24f; at.y = _deskY + 0.24f; return at; }
            return head + fwd * 0.45f + left * 0.22f + Vector3.down * 0.10f;
        }

        void UpdateWorkStrip(float dt)
        {
            if (_work == null) return;
            if (!_wkInit) InitWorkStrip();
            var v = _work; var p = v.panel;
            // what is shown: the open jobs, then the ones that have just finished
            var jobs = WorkJobs; int rows = 0, more = 0;
            bool allowed = !_calibrating && _introDone && !_link.NeedsPairing;
            if (allowed)
            {
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        var j = jobs[i];
                        if (j == null) continue;
                        bool open = WorkOpen(j);
                        if (open != (pass == 0)) continue;
                        if (!open && Time.time - WorkDoneAt(j) > DoneKeep) continue;
                        if (rows < W.Rows) _wkJob[rows++] = j; else more++;
                    }
                _wkSeenQueue = _wkSeenQueue || _snap != null || _workFake != null;
            }
            for (int r = rows; r < W.Rows; r++) _wkJob[r] = null;
            if (rows == 0) { if (p.Shown) { p.Hide(); Debug.Log("[MR] work: nothing is being worked on - the strip is put away"); } _wkDrag = false; _wkRows = -1; return; }

            if (!p.Shown)
            {
                // it comes up where it was put; never put anywhere, beside the partner
                if (!_wkUserPlaced || !_wkPlaced) { _wkPos = WorkStripHome(); _wkPlaced = true; }
                Vector3 to = _wkPos - Input.HeadPos;
                _wkRot = Quaternion.LookRotation(to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.forward, Vector3.up);
                _wkShownAt = Time.time; _wkRows = -1;
                p.Show();
                Sfx("open", _wkPos, 0.3f);
                Debug.Log($"[MR] work: the strip is up with {rows} job(s) at {_wkPos.ToString("0.00")}{(_wkUserPlaced ? " (where it was put)" : "")}");
            }
            WorkStripDrag(dt);

            // the stack: as tall as it has cards; the handle stays where it is, the cards hang under it
            if (rows != _wkRows)
            {
                _wkRows = rows;
                float h = W.Height(rows);
                p.height = h;
                Ui.SizePanel(p.bg, W.W, h, 0.016f, 0.0018f);
                p.glow.transform.localScale = new Vector3(W.W * 1.22f, h * 1.30f, 1f);
                v.top.localPosition = new Vector3(0f, h * 0.5f - W.Pad - W.HandleH * 0.5f, 0f);
                for (int r = 0; r < W.Rows; r++)
                {
                    bool on = r < rows;
                    if (v.rows[r].gameObject.activeSelf != on) v.rows[r].gameObject.SetActive(on);
                    v.rows[r].localPosition = new Vector3(0f, h * 0.5f - W.Pad - W.HandleH - W.Gap - W.RowH * 0.5f - r * (W.RowH + W.Gap), 0f);
                    v.rowBtn[r].homeSet = false; v.cancel[r].homeSet = false;
                }
            }
            if (more != _wkMore) { _wkMore = more; v.more.text = more > 0 ? "+" + more + " more" : ""; }
            float hNow = p.height;
            for (int r = 0; r < rows; r++) WorkRowFill(r, dt);

            // (the handle is where it was put: the sheet's middle is below it)
            Vector3 mid = _wkPos - (_wkRot * Vector3.up) * (hNow * 0.5f - W.Pad - W.HandleH * 0.5f);
            p.transform.SetPositionAndRotation(mid, _wkRot);
            bool guard = _wkDrag || Time.time - _wkShownAt < WkGuard;
            if (guard) { var bs = p.buttons; for (int i = 0; i < bs.Length; i++) bs[i].cool = Mathf.Max(bs[i].cool, 0.3f); }
            UpdateButtons(p, dt);
        }

        void WorkRowFill(int r, float dt)
        {
            var v = _work; var j = _wkJob[r];
            bool open = WorkOpen(j), done = j.status == "done";
            // 0 in the room, 1 in the PC workshop, 2 done, 3 failed, 4 cancelled
            int look = open ? (j.where == "pc" ? 1 : 0) : done ? 2 : j.status == "cancelled" ? 4 : 3;
            string title = string.IsNullOrEmpty(j.title) ? "Working on it" : j.title;
            if (title != _wkTitle[r]) { _wkTitle[r] = title; FitInto(v.rowTitle[r], title, 0.112f); }
            string step = open ? (string.IsNullOrEmpty(j.step) ? (j.status == "queued" ? "Waiting for its turn" : "Working ...") : j.step)
                        : done ? (string.IsNullOrEmpty(j.note) ? "Ready" : j.note) : look == 4 ? "Stopped" : string.IsNullOrEmpty(j.note) ? "It did not work out" : j.note;
            if (step != _wkStep[r]) { _wkStep[r] = step; FitInto(v.rowStep[r], step, open ? W.W - 2f * W.Pad - 0.018f : W.W - 2f * W.Pad - 0.040f); }
            string where = open ? (j.status == "queued" ? "queued" : WorkJob.AtBench(j) ? "in the room" : WorkJob.InsidePc(j) ? "in the PC workshop" : "working")
                                : done ? (string.IsNullOrEmpty(j.resultKind) ? "done" : "done - press to open") : look == 3 ? "failed" : "cancelled";
            if (look != _wkLook[r] || where != _wkWhere[r])
            {
                _wkLook[r] = look;
                _wkWhere[r] = where; v.rowWhere[r].text = where;
                Color c = look == 0 ? new Color(0.80f, 0.92f, 0.79f, 1f) : look == 1 ? new Color(0.78f, 0.89f, 0.96f, 1f) : look == 2 ? new Color(1f, 0.86f, 0.52f, 1f) : look == 3 ? new Color(0.97f, 0.78f, 0.78f, 1f) : new Color(0.88f, 0.86f, 0.82f, 1f);
                v.rowWhereBg[r].SetColors(c, c);
                float cw = v.rowWhere[r].Width + 0.012f;
                Ui.SizePanel(v.rowWhereBg[r], cw, 0.0128f, 0.0064f);
                // (the chip ends where the cancel button begins - or, on a finished card, at the card's edge)
                float right = (W.W - 2f * W.Pad) * 0.5f - (open ? 0.027f : 0.007f);
                var wp = v.rowWhereBg[r].transform.localPosition; v.rowWhereBg[r].transform.localPosition = new Vector3(right - cw * 0.5f, wp.y, wp.z);
                var lp = v.rowWhere[r].transform.localPosition; v.rowWhere[r].transform.localPosition = new Vector3(right - cw * 0.5f, lp.y, lp.z);
                if (v.cancel[r].gameObject.activeSelf != open) v.cancel[r].gameObject.SetActive(open);
                bool bar = open;
                if (v.rowBarBg[r].enabled != bar) { v.rowBarBg[r].enabled = bar; v.rowBar[r].enabled = bar; }
                if (!bar) { v.rowPct[r].text = ""; _wkPct[r] = -1; }
                bool mark = !open;
                if (v.rowMark[r].enabled != mark) v.rowMark[r].enabled = mark;
                if (mark) { Color m = done ? new Color(0.50f, 0.78f, 0.50f, 1f) : look == 3 ? new Color(0.90f, 0.55f, 0.55f, 1f) : new Color(0.72f, 0.69f, 0.65f, 1f); v.rowMark[r].SetColors(m, m); }
                if (v.rowTick[r].activeSelf != done) v.rowTick[r].SetActive(done);
                // the card: honey when it is ready to be opened
                var b = v.rowBtn[r];
                b.color = done ? HoneyPaper : Paper; b.color2 = done ? new Color(1f, 0.84f, 0.50f, 0.98f) : PaperDeep;
                b.hoverGrow = done ? 0.03f : 0f;
            }
            if (!open) return;
            // the bar eases to where the job is; the percentage is written anew only when its number changes
            float prog = Mathf.Clamp01(j.progress);
            _wkBar[r] = Mathf.Abs(_wkBar[r] - prog) > 0.5f ? prog : Mathf.Lerp(_wkBar[r], prog, 1f - Mathf.Exp(-dt * 6f));
            float full = W.BarW, x0 = v.rowBarBg[r].transform.localPosition.x - full * 0.5f, w, x;
            if (prog > 0.005f) { w = full * Mathf.Clamp(_wkBar[r], 0.045f, 1f); x = x0 + w * 0.5f; }
            else { w = full * 0.26f; x = x0 + w * 0.5f + (full - w) * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f + r)); }      // (nothing said yet: a piece of it going to and fro)
            Ui.SizePanel(v.rowBar[r], w, W.BarH, W.BarH * 0.5f);
            var bp = v.rowBar[r].transform.localPosition; v.rowBar[r].transform.localPosition = new Vector3(x, bp.y, bp.z);
            int pct = prog > 0.005f ? Mathf.RoundToInt(prog * 100f) : -2;
            if (pct != _wkPct[r]) { _wkPct[r] = pct; v.rowPct[r].text = pct >= 0 ? pct + "%" : ""; }
        }

        /// <summary>Thumb and finger closed on the handle (right on it, or with the ray on it) take the strip; it goes with the hand until they open.</summary>
        void WorkStripDrag(float dt)
        {
            var p = _work.panel;
            if (!_wkDrag)
            {
                if (!p.Shown || p.appear < 1f) return;
                for (int h = 0; h < 2; h++)
                {
                    var hs = UiHand(h);
                    if (!hs.tracked || !hs.pinchDown || HandBusy(h, hs)) continue;
                    Vector3 q = (hs.indexTip + hs.thumbTip) * 0.5f;
                    Vector3 l = Quaternion.Inverse(_wkRot) * (q - _wkPos);      // from the middle of the handle
                    bool direct = Mathf.Abs(l.z) < 0.045f && Mathf.Abs(l.x) < W.W * 0.5f + 0.01f && Mathf.Abs(l.y) < W.HandleH * 0.5f + 0.014f;
                    bool byRay = false;
                    if (!direct && _rayPanel[h] == p)
                    {
                        Vector3 rl = Quaternion.Inverse(_wkRot) * (_rayAt[h] - _wkPos);
                        byRay = Mathf.Abs(rl.x) < W.W * 0.5f && Mathf.Abs(rl.y) < W.HandleH * 0.5f + 0.008f;
                    }
                    if (!direct && !byRay) continue;
                    _wkDrag = true; _wkDragHand = h; _wkDragRay = byRay; _wkDragLost = 0f; _uiConsumedPinch = true;
                    _wkDragReach = byRay ? Vector3.Distance(hs.rayOrigin, _rayAt[h]) : 0f;
                    _wkDragOffset = _wkPos - (byRay ? _rayAt[h] : q);
                    Sfx("tap", _wkPos, 0.4f);
                    Debug.Log($"[MR] work: the strip is taken by its handle ({(byRay ? "the ray" : "the fingers")})");
                    break;
                }
                return;
            }
            var hd = UiHand(_wkDragHand);
            _wkDragLost = hd.tracked ? 0f : _wkDragLost + dt;
            if (hd.tracked ? !hd.pinch : _wkDragLost > 0.3f)
            {
                _wkDrag = false; _wkUserPlaced = true;
                Sfx("land", _wkPos, 0.3f);
                Debug.Log($"[MR] work: the strip is put down at {_wkPos.ToString("0.00")} - it stays there");
                return;
            }
            if (hd.tracked)
            {
                Vector3 hold = _wkDragRay ? hd.rayOrigin + hd.rayDir * _wkDragReach : (hd.indexTip + hd.thumbTip) * 0.5f;
                _wkPos = Vector3.Lerp(_wkPos, hold + _wkDragOffset, 1f - Mathf.Exp(-dt * 18f));
                Vector3 to = _wkPos - Input.HeadPos;
                if (to.sqrMagnitude > 0.01f) _wkRot = Quaternion.Slerp(_wkRot, Quaternion.LookRotation(to.normalized, Vector3.up), 1f - Mathf.Exp(-dt * 12f));
            }
        }

        bool OnDebugWorkStrip(DebugCommand d)
        {
            if (d.cmd != "workStrip") return false;
            string what = d.text ?? "";
            if (what == "home") { _wkUserPlaced = false; _wkPlaced = false; if (_work.panel.Shown) _work.panel.Hide(); }
            else if (what == "info")
            {
                var sb = new System.Text.StringBuilder();
                for (int r = 0; r < W.Rows; r++) if (_wkJob[r] != null) sb.Append($" [{r}: '{_wkJob[r].title}' {_wkJob[r].where} {_wkJob[r].status} {_wkJob[r].progress:0.00} '{_wkJob[r].step}']");
                Debug.Log($"[MR] work info: shown={_work.panel.Shown} rows={_wkRows} handle at {_wkPos.ToString("0.00")} placed by the user={_wkUserPlaced} jobs in the queue={WorkJobs.Count}{sb}");
            }
            else if (what == "open" || what == "cancel") { int r = Mathf.Clamp((int)d.value, 0, W.Rows - 1); _wkShownAt = -10f; if (what == "open") WorkRowPressed(r); else WorkCancelPressed(r); }
            else
            {
                // as if it had been taken by the handle and put down somewhere else
                _wkPos += Input.HeadRot * new Vector3(d.value, d.px, d.py);
                Vector3 to = _wkPos - Input.HeadPos;
                _wkRot = Quaternion.LookRotation(to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.forward, Vector3.up);
                _wkUserPlaced = true; _wkPlaced = true;
                Debug.Log($"[MR] work: (debug) the strip was moved to {_wkPos.ToString("0.00")}");
            }
            return true;
        }
    }
}
