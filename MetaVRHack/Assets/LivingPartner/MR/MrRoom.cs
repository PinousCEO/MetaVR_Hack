using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using SN = System.Numerics;

namespace LivingPartner.MR
{
    /// <summary>
    /// What the MR app works out about the real room by itself. Nothing here asks the user to touch or mark anything.
    ///  - THE SCREENS, with the headset's own camera. The PC lights each display with a plain colour for a moment (the
    ///    colour says which display it is); in the camera's picture that is a patch with four straight edges, and as
    ///    the display reports how wide its lit area really is, the four corners follow exactly (ScreenFinder). The
    ///    user only has to have the display in view; the colour goes away the moment the display is found. (Before
    ///    this the displays were felt out with the depth sensor: right size, wrong place, and one or two of three.)
    ///  - THE DESK. Rays against the real room go straight down between the user and the screens; the level most of
    ///    them land on, below the lower edge of the screens, is the desk. The partner stands on that level.
    ///  - Where the user is looking on a known screen, for "this screen" said without pointing.
    /// </summary>
    public sealed partial class MrApp
    {
        // ------------------------------------------------------------------ the desk
        readonly List<Vector3> _deskHits = new List<Vector3>(100);
        float _nextDeskProbe, _nextDeskVote, _deskTarget, _nextDeskLog;
        bool _deskSensed, _deskHomeValid;
        Vector3 _deskHome;
        int _deskCasts, _deskMisses, _deskOutOfRange, _deskMonCount = -1;

        /// <summary>The standing surface was found by sensing the room (not estimated, not measured by touch).</summary>
        public bool DeskSensed => _deskSensed;
        /// <summary>The displays are being looked for with the camera right now.</summary>
        public bool FindingScreens => _lookCal;

        void UpdateRoom(float dt, bool hostOk)
        {
            if (Input.Simulated) return;
            UpdateDesk(dt);
            // The headset found its place in the room anew (put on, recentred, the boundary changed): everything measured
            // before is in the old coordinates. The screens are found again at once - on the first run this happened ten
            // seconds after two screens had been found, and "go in there" then had nowhere to go.
            if (Input.SpaceEpoch != _roomEpoch)
            {
                bool first = _roomEpoch == null; _roomEpoch = Input.SpaceEpoch;
                if (!first)
                {
                    // Not at once with colours on every display (that happened four times in five minutes, once in the
                    // middle of a tennis game): first the depth sensor is asked whether the screens are still where
                    // they were. Only if they are not are they found again.
                    Debug.Log("[MR] screens: the headset announced its tracking origin again: checking whether the screens are still where they were");
                    if (_lookCal) FinishFinding("");
                    _verifyUntil = Time.time + 20f; _verifyNext = Time.time + 0.6f; _refindAll = false; _deskHits.Clear();
                }
            }
            if (!_checkedAtLaunch && hostOk && !LocalMode && _snap.monitors.Count > 0 &&
                !_calibrating && !GameOn && _introDone && Application.isFocused && Input.HeadTracked && TransitionIdle)
            {
                _verifyUntil = -1f;
                _refindAll = false;
                _autoFinds = 1;
                _roomReadyAt = Time.time + 90f;
                BeginLookCalibration("checking the PC screens at launch", true);
                return;
            }
            if (!_checkedAtLaunch) return;
            if (_verifyUntil > 0f) VerifyScreens(hostOk);
            bool playing = _tn.active || _tnPendingAt >= 0f || _rpsOn || GomokuOn;
            if (_refindAll && hostOk && !_calibrating && !playing && Time.time > _roomReadyAt && _snap != null && _snap.monitors.Count > 0 && TransitionIdle && Input.HeadTracked)
            {
                _refindAll = false; _autoFinds = 1; _roomReadyAt = Time.time + 90f;
                BeginLookCalibration("the screens are not where they were", true);
                return;
            }
            if (playing || _verifyUntil > 0f) return;
            // a display the PC has and the room does not know yet: it is looked for without anybody asking
            if (hostOk && !_calibrating && _autoFinds < 2 && Time.time > _roomReadyAt && _snap != null && _snap.monitors.Count > _mons.Count && TransitionIdle && Input.HeadTracked)
            {
                _autoFinds++; _roomReadyAt = Time.time + 90f;
                BeginLookCalibration(_mons.Count == 0 ? "no screen is known yet" : "a screen is not known yet");
            }
        }
        int _autoFinds; float _roomReadyAt = 5f, _verifyUntil = -1f, _verifyNext; string _roomEpoch; bool _refindAll;
        bool _checkedAtLaunch;

        /// <summary>
        /// After the headset has announced its tracking origin anew: are the saved screens still where they were? Rays
        /// against the real room at five spots of each saved screen. One screen that is still there (and none that is
        /// not) keeps them all, under the new origin's name; a screen that is clearly not there starts the finding.
        /// </summary>
        void VerifyScreens(bool hostOk)
        {
            if (!hostOk || _snap == null || Time.time < _verifyNext) return;
            _verifyNext = Time.time + 0.3f;
            Vector3 head = Input.HeadPos;
            int total = 0, there = 0, gone = 0;
            foreach (var c in _snap.calibrations)
            {
                if (c.simulated) continue;
                var desc = ScreenMath.FindMonitor(_snap.monitors, c.monitorId);
                if (desc == null || desc.bounds.w != c.pixelW || desc.bounds.h != c.pixelH) continue;
                total++;
                var f = MonitorFrame.FromCalibration(c);
                int hits = 0, near = 0;
                for (int k = 0; k < 5; k++)
                {
                    float lx = k == 0 ? 0f : (k == 1 || k == 3 ? -0.3f : 0.3f) * f.widthM, ly = k == 0 ? 0f : (k <= 2 ? 0.3f : -0.3f) * f.heightM;
                    Vector3 pt = Conv.U(f.ToWorld(new SN.Vector3(lx, ly, 0f)));
                    Vector3 to = pt - head; float dist = to.magnitude;
                    if (dist < 0.2f || !Input.RaycastEnvironment(head, to / dist, dist + 0.6f, out Vector3 hit)) continue;
                    hits++;
                    if (Mathf.Abs(Vector3.Distance(head, hit) - dist) < 0.05f) near++;
                }
                if (hits >= 4 && near >= 4) there++; else if (hits >= 4 && near <= 1) gone++;
            }
            if (total == 0) { _verifyUntil = -1f; return; }                       // nothing was saved: the usual finding takes over
            if (gone > 0)
            {
                Debug.Log($"[MR] screens: {gone} of {total} saved screen(s) are not where they were: finding them again");
                _verifyUntil = -1f; _refindAll = true; _roomReadyAt = Time.time + 0.5f;
                return;
            }
            bool late = Time.time > _verifyUntil;
            if (there == 0 && !late) return;                                      // not looking at a screen yet: nothing can be told
            Debug.Log(there > 0 ? $"[MR] screens: still where they were ({there} of {total} checked with the depth sensor): kept, no colours needed" : $"[MR] screens: could not be checked (none was in view for 20 s): kept as they were");
            foreach (var c in _snap.calibrations)
            {
                if (c.simulated || c.spaceEpoch == Input.SpaceEpoch) continue;
                var desc = ScreenMath.FindMonitor(_snap.monitors, c.monitorId);
                if (desc == null || desc.bounds.w != c.pixelW || desc.bounds.h != c.pixelH) continue;
                _link.Send(Msg.CalibCommit, new MonitorCalibration { monitorId = c.monitorId, tl = c.tl, tr = c.tr, bl = c.bl, br = c.br, spaceEpoch = Input.SpaceEpoch, simulated = false, hasDesk = false });
            }
            _verifyUntil = -1f; _autoFinds = Mathf.Max(_autoFinds, 1); _roomReadyAt = Time.time + 8f;
        }

        /// <summary>The lowest point of the lit areas of the known screens: the desk is below that.</summary>
        bool LowestScreenEdge(out float y)
        {
            y = float.MaxValue;
            foreach (var mv in _mons.Values)
                y = Mathf.Min(y, mv.Origin.y - 0.5f * (Mathf.Abs(mv.X.y) * mv.frame.widthM + Mathf.Abs(mv.Y.y) * mv.frame.heightM));
            return _mons.Count > 0;
        }

        void UpdateDesk(float dt)
        {
            if (_hasDesk) return;
            if (_deskSensed) _deskY = Mathf.MoveTowards(_deskY, _deskTarget, 0.20f * dt);
            // the screens became known (or changed): what counted as the desk before is measured again, below them
            if (_mons.Count != _deskMonCount) { _deskMonCount = _mons.Count; _deskHits.Clear(); _nextDeskVote = 0; _deskCasts = _deskMisses = _deskOutOfRange = 0; _nextDeskLog = 0; }
            if (!Input.HeadTracked || Time.time < _nextDeskProbe) return;
            _nextDeskProbe = Time.time + (_deskHits.Count >= 60 ? 0.20f : 0.04f);
            Vector3 head = Input.HeadPos;
            Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 0.04f) return;                        // looking straight up or down: no "in front"
            fwd.Normalize();
            bool screens = LowestScreenEdge(out float lowest);
            Vector3 spot;
            if (screens)
            {
                // somewhere between the user and one of the screens: that is where the desk is
                int pick = Random.Range(0, _mons.Count); MonView mon = null;
                foreach (var mv in _mons.Values) { mon = mv; if (pick-- == 0) break; }
                Vector3 to = mon.Origin - head; to.y = 0;
                if (to.sqrMagnitude < 0.01f) return;
                Vector3 across = Vector3.Cross(Vector3.up, to.normalized);
                spot = head + to * Random.Range(0.35f, 0.95f) + across * Random.Range(-0.22f, 0.22f);
            }
            else
            {
                // once a desk is known, only readings taken while facing it count (turning to the bed must not move the desk)
                if (_deskSensed && _deskHomeValid) { Vector3 toHome = _deskHome - head; toHome.y = 0; if (toHome.sqrMagnitude > 1e-4f && Vector3.Dot(toHome.normalized, fwd) < 0.5f) return; }
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                spot = head + fwd * Random.Range(0.18f, 0.62f) + side * Random.Range(-0.32f, 0.32f);
            }
            _deskCasts++;
            if (Time.time > _nextDeskLog && _deskHits.Count < 24)
            {
                _nextDeskLog = Time.time + 5f;
                Debug.Log($"[MR] room: looking for the desk{(screens ? " below the screens (their lower edge is " + (head.y - lowest).ToString("0.00") + " m below the eyes)" : "")}: {_deskCasts} rays, {_deskMisses} hit nothing, {_deskOutOfRange} out of range, {_deskHits.Count} readings kept");
            }
            if (!Input.RaycastEnvironment(new Vector3(spot.x, head.y - 0.02f, spot.z), Vector3.down, 1.6f, out Vector3 hit)) { _deskMisses++; return; }
            float below = head.y - hit.y;
            if (below < 0.15f || below > 1.20f) { _deskOutOfRange++; return; }
            if (screens && (hit.y > lowest + 0.01f || hit.y < lowest - 0.55f)) { _deskOutOfRange++; return; }      // a screen, a shelf above it, the floor
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                if (hs.tracked && Vector3.Distance(hs.palmPos, hit) < 0.16f) return;      // a hand is not the desk
            }
            _deskHits.Add(hit);
            if (_deskHits.Count > 90) _deskHits.RemoveAt(0);
            if (_deskHits.Count < 24 || Time.time < _nextDeskVote) return;
            _nextDeskVote = Time.time + 0.5f;

            // the 4 cm band of height that most readings fall into
            var ys = new float[_deskHits.Count];
            for (int i = 0; i < ys.Length; i++) ys[i] = _deskHits[i].y;
            System.Array.Sort(ys);
            int best = 0, bestFrom = 0;
            for (int a = 0, b = 0; a < ys.Length; a++)
            {
                while (b < ys.Length && ys[b] - ys[a] <= 0.04f) b++;
                if (b - a > best) { best = b - a; bestFrom = a; }
            }
            if (best < ys.Length * 0.22f) return;                        // no level stands out: keep looking
            float level = 0; for (int i = bestFrom; i < bestFrom + best; i++) level += ys[i];
            level /= best;
            // somewhere that really is at that level, as near as possible to straight in front of the user
            Vector3 want = head + fwd * 0.40f; float bd = float.MaxValue; Vector3 home = default; bool any = false;
            foreach (var p in _deskHits)
            {
                if (Mathf.Abs(p.y - level) > 0.022f) continue;
                float d = new Vector2(p.x - want.x, p.z - want.z).sqrMagnitude;
                if (d < bd) { bd = d; home = new Vector3(p.x, level, p.z); any = true; }
            }
            if (!_deskSensed || Mathf.Abs(level - _deskTarget) > 0.03f)
            {
                bool first = !_deskSensed;
                _deskSensed = true; _deskTarget = level; if (first) _deskY = level;
                if (any) { _deskHome = home; _deskHomeValid = true; }
                _homeSet = false;                                       // stand there from now on
                Debug.Log($"[MR] room: desk {(first ? "found" : "is")} {head.y - level:0.00} m below the eyes (y={level:0.000}){(screens ? ", " + (lowest - level).ToString("0.00") + " m below the screens" : "")}, {best} of {ys.Length} readings agree, readings from {ys[0]:0.00} to {ys[ys.Length - 1]:0.00}; standing spot {(any ? Mathf.Sqrt(bd).ToString("0.00") + " m from straight ahead" : "not found")}");
            }
            else if (Mathf.Abs(level - _deskTarget) > 0.006f)
            {
                Debug.Log($"[MR] room: desk level {_deskTarget:0.000} -> {level:0.000} ({best} of {ys.Length} readings)");
                _deskTarget = level;
            }
        }

        // ------------------------------------------------------------------ where the user is looking on a known screen
        /// <summary>Where on a calibrated screen the user is looking (the centre of the view). False when at none.</summary>
        bool GazeTarget(out string mon, out float u, out float v)
        {
            mon = ""; u = v = 0; float best = float.MaxValue;
            Vector3 o = Input.HeadPos, d = Input.HeadRot * Vector3.forward;
            foreach (var mv in _mons.Values)
            {
                if (!mv.frame.Raycast(Conv.N(o), Conv.N(d), out SN.Vector3 local, out float dist) || dist > best) continue;
                ScreenMath.LocalToUV(mv.frame.widthM, mv.frame.heightM, local, out float gu, out float gv);
                if (gu < -0.05f || gu > 1.05f || gv < -0.05f || gv > 1.05f) continue;
                best = dist; mon = mv.desc.id; u = Mathf.Clamp01(gu); v = Mathf.Clamp01(gv);
            }
            return mon.Length > 0;
        }

        // ------------------------------------------------------------------ finding the screens with the camera
        sealed class Finding
        {
            public MonitorDesc desc; public int colour; public bool done, lit; public string why = "not looked at yet";
            public readonly List<Vector3[]> samples = new List<Vector3[]>();      // tl, tr, bl, br in the room
        }
        static readonly string[] ColourNames = { "red", "green", "blue" };
        static readonly int[] FlashLevels = { 150, 255, 90 };       // how bright the colour is: the camera may wash out a bright one

        readonly List<Finding> _finding = new List<Finding>();
        readonly ScreenFinder _finder = new ScreenFinder();
        byte[] _shotBuf;
        bool _lookCal, _findHinted, _shotFlip, _flipLocked;
        float _findStart, _findProgressAt, _nextFlash, _nextShot, _flashChangedAt, _findSteady, _nextFindLog, _lastQuadAt, _showOutlinesUntil;
        int _findLevel, _findFrames, _flipVotes, _dumpShots;
        Vector3 _findDirPrev, _findPosPrev;

        int FoundCount { get { int n = 0; foreach (var f in _finding) if (f.done) n++; return n; } }

        /// <summary>Starts finding the displays: all of them when asked for, otherwise the ones not known yet.</summary>
        void BeginLookCalibration(string why, bool all = false)
        {
            if (_snap == null || _snap.monitors.Count == 0 || _calibrating) return;
            if (_tn.active || _tnPendingAt >= 0f || _rpsOn || GomokuOn) { Caption("After the game - the screens would light up in colours.", 3f); return; }
            all |= why == "asked for";
            _finding.Clear();
            foreach (var m in _snap.monitors)
            {
                if (!all && _mons.ContainsKey(m.id)) continue;
                _finding.Add(new Finding { desc = m, colour = _finding.Count % 3 });
            }
            if (_finding.Count == 0) return;
            _calibrating = true; _lookCal = true; _findHinted = false;
            _findStart = _findProgressAt = _flashChangedAt = _lastQuadAt = Time.time; _nextFlash = 0; _nextShot = 0; _findSteady = 0; _findFrames = 0; _findLevel = 0;
            _findDirPrev = Input.HeadRot * Vector3.forward; _findPosPrev = Input.HeadPos;
            string list = "";
            foreach (var f in _finding) list += $" {f.desc.id.Replace("\\\\.\\", "")}={ColourNames[f.colour]}({f.desc.physWidthM * 1000:0}x{f.desc.physHeightM * 1000:0}mm)";
            Debug.Log($"[MR] screens: finding {_finding.Count} display(s) with the camera ({why}):{list}");
            Caption("Finding your screens - just glance at each one.", 3f);
        }

        /// <summary>"Skip" / giving up: the round ends with what has been found.</summary>
        void LookNext(bool giveUpOnCurrent) => FinishFinding(FoundCount > 0 ? "partial" : "none");

        void SendFlash(string stage)
        {
            var msg = new CalibFlash { stage = stage, total = _finding.Count, found = FoundCount };
            if (_lookCal && (stage.Length == 0 || stage == "hint"))
            {
                int level = FlashLevels[_findLevel];
                foreach (var f in _finding)
                    if (f.lit) msg.items.Add(new FlashItem { monitorId = f.desc.id, r = f.colour == 0 ? level : 0, g = f.colour == 1 ? level : 0, b = f.colour == 2 ? level : 0 });
            }
            _link.Send(Msg.CalibFlash, msg);
        }

        void FinishFinding(string stage)
        {
            if (!_lookCal) return;
            _lookCal = false;
            if (stage.Length > 0) _checkedAtLaunch = true;
            int found = FoundCount;
            _link.Send(Msg.CalibFlash, new CalibFlash { stage = stage, total = _finding.Count, found = found });
            string rest = "";
            foreach (var f in _finding) if (!f.done) rest += $" {f.desc.id.Replace("\\\\.\\", "")}: {f.why};";
            Debug.Log($"[MR] screens: finished ({(stage.Length > 0 ? stage : "put aside")}), {found} of {_finding.Count} found after {Time.time - _findStart:0.0} s and {_findFrames} pictures.{rest}");
            if (stage == "done") Caption(found > 1 ? $"Found all {found} screens." : "Found your screen.", 2.5f);
            else if (stage == "partial") Caption($"Found {found} of {_finding.Count} screens. Say \"find my screens\" to look again.", 4f);
            else if (stage == "none") Caption("I couldn't see your screens. Say \"find my screens\" while looking at them.", 4f);
            else if (stage == "nocamera") Caption("I can't use the headset's camera, so I can't find your screens.", 5f);
            _showOutlinesUntil = Time.time + 6f;
            EndCalibration();
        }

        void UpdateLookCalibration(float dt)
        {
            _calib.panel.Hide();
            float now = Time.time;
            int pending = 0, used = 0, found = 0;
            foreach (var f in _finding)
            {
                f.lit = false;
                if (f.done) { found++; continue; }
                pending++;
                if ((used & (1 << f.colour)) == 0) { used |= 1 << f.colour; f.lit = true; }
            }
            if (pending == 0) { FinishFinding("done"); return; }
            if (now - _findProgressAt > 22f) { FinishFinding(found > 0 ? "partial" : "none"); return; }
            if (now >= _nextFlash) { _nextFlash = now + 0.3f; SendFlash(""); }

            // pictures are taken while the head is still: where the camera was is then known well enough
            Vector3 dir = Input.HeadRot * Vector3.forward, pos = Input.HeadPos;
            float turning = Vector3.Angle(dir, _findDirPrev) / Mathf.Max(dt, 1e-4f), moving = (pos - _findPosPrev).magnitude / Mathf.Max(dt, 1e-4f);
            _findDirPrev = dir; _findPosPrev = pos;
            _findSteady = turning < 9f && moving < 0.08f ? _findSteady + dt : 0f;
            if (_findSteady < 0.2f || now < _nextShot || now - _flashChangedAt < 0.7f) return;
            _nextShot = now + 0.15f;
            if (!Input.CameraFrame(ref _shotBuf, out CameraShot shot))
            {
                _nextShot = now + 0.4f;
                if (_findFrames == 0 && now - _findStart > 8f) { Debug.Log("[MR] screens: no pictures from the headset's camera (" + Input.Diagnostics.Replace("\n", " ") + ")"); FinishFinding("nocamera"); }
                return;
            }
            _findFrames++;
            if (_dumpShots > 0) { _dumpShots--; DumpShot(_shotBuf, shot.width, shot.height); }
            bool any = false;
            foreach (var f in _finding)
            {
                if (!f.lit) continue;
                if (!_finder.FindQuad(_shotBuf, shot.width, shot.height, f.colour, out ScreenFinder.Quad q, out string why)) { f.why = why; continue; }
                if (!Locate(shot, q, f, out Vector3[] corners, out why)) { f.why = why; continue; }
                any = true; f.why = "seen";
                f.samples.Add(corners);
                if (f.samples.Count > 5) f.samples.RemoveAt(0);
                TryCommit(f);
            }
            if (any) _lastQuadAt = now;
            else if (now - Mathf.Max(_lastQuadAt, _flashChangedAt) > 2.2f)
            {
                // nothing recognised for a while: perhaps the camera washes the colour out, or it is too dim - try another brightness
                _findLevel = (_findLevel + 1) % FlashLevels.Length; _flashChangedAt = now; _nextFlash = 0;
            }
            if (!_findHinted && found == 0 && !any && now - _findStart > 6f) { _findHinted = true; SendFlash("hint"); }
            if (now > _nextFindLog)
            {
                _nextFindLog = now + 2f;
                string s = "";
                foreach (var f in _finding) s += $" {f.desc.id.Replace("\\\\.\\", "")}[{ColourNames[f.colour]}]: {(f.done ? "found" : f.lit ? f.why : "waits for its colour")};";
                Debug.Log($"[MR] screens: {_findFrames} pictures {shot.width}x{shot.height}, brightness {FlashLevels[_findLevel]}.{s}");
            }
        }

        /// <summary>From the four corners in the picture to the four corners in the room.</summary>
        bool Locate(CameraShot shot, ScreenFinder.Quad q, Finding f, out Vector3[] corners, out string why)
        {
            corners = null;
            float w = f.desc.physWidthM, hSaid = f.desc.physHeightM;
            bool sized = w > 0.05f && hSaid > 0.05f;
            if (!Solve(shot, q, _shotFlip, sized ? w : 0.5f, out corners, out float h, out float skew)) { why = "its corners do not make a flat shape"; return false; }
            // Is the top row of the picture first, or the bottom one? The depth sensor settles it once: only one of the
            // two readings puts the display where something solid is.
            if (!_flipLocked && Solve(shot, q, !_shotFlip, sized ? w : 0.5f, out Vector3[] other, out _, out _))
            {
                Vector3 ca = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f, cb = (other[0] + other[1] + other[2] + other[3]) * 0.25f;
                if (sized && Vector3.Angle(ca - shot.position, cb - shot.position) > 8f)
                {
                    bool fitsA = SolidAt(shot.position, ca), fitsB = SolidAt(shot.position, cb);
                    if (fitsA != fitsB)
                    {
                        _flipVotes += fitsA ? 1 : -1;
                        if (_flipVotes <= -2) { _shotFlip = !_shotFlip; _flipVotes = 1; corners = other; foreach (var x in _finding) x.samples.Clear(); Debug.Log($"[MR] screens: the camera's pictures arrive {(_shotFlip ? "bottom" : "top")} row first (the depth sensor says so)"); why = "picture orientation settled"; return false; }
                        if (_flipVotes >= 3) { _flipLocked = true; Debug.Log($"[MR] screens: picture orientation confirmed by the depth sensor ({(_shotFlip ? "bottom" : "top")} row first)"); }
                    }
                }
            }
            if (Mathf.Abs(skew) > 4f) { why = $"its corners are {skew:0.0} degrees off square"; return false; }
            Vector3 centre = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            if (sized)
            {
                if (Mathf.Abs(h / hSaid - 1f) > 0.07f) { why = $"seen as {w * 1000:0}x{h * 1000:0} mm but the display says {w * 1000:0}x{hSaid * 1000:0} mm (something is in front of it?)"; return false; }
            }
            else
            {
                // a display that does not say how large it is: its shape from the picture, its distance from the depth sensor
                float aspect = f.desc.bounds.w / (float)Mathf.Max(1, f.desc.bounds.h);
                if (Mathf.Abs(0.5f / h / aspect - 1f) > 0.07f) { why = "its shape does not match its resolution"; return false; }
                Vector3 to = centre - shot.position;
                if (!Input.RaycastEnvironment(shot.position, to.normalized, 4f, out Vector3 hit)) { why = "the display does not say its size and nothing solid is sensed there"; return false; }
                float k = Vector3.Distance(shot.position, hit) / to.magnitude;
                for (int i = 0; i < 4; i++) corners[i] = shot.position + (corners[i] - shot.position) * k;
                centre = shot.position + to * k;
            }
            float dist = Vector3.Distance(centre, shot.position);
            if (dist < 0.25f || dist > 3.5f) { why = $"{dist:0.00} m away: not believable"; return false; }
            why = ""; return true;
        }

        bool SolidAt(Vector3 from, Vector3 point)
        {
            Vector3 to = point - from; float len = to.magnitude;
            return Input.RaycastEnvironment(from, to / len, len + 1.5f, out Vector3 hit) && Mathf.Abs(Vector3.Distance(from, hit) - len) < 0.10f;
        }

        bool Solve(CameraShot shot, ScreenFinder.Quad q, bool flip, float width, out Vector3[] corners, out float height, out float skew)
        {
            SN.Vector2 tl = q.tl, tr = q.tr, br = q.br, bl = q.bl;
            if (flip)
            {
                // bottom row first: what was taken for the top is the bottom
                float H = shot.height;
                var a = new SN.Vector2(q.bl.X, H - q.bl.Y); var b = new SN.Vector2(q.br.X, H - q.br.Y);
                bl = new SN.Vector2(q.tl.X, H - q.tl.Y); br = new SN.Vector2(q.tr.X, H - q.tr.Y); tl = a; tr = b;
            }
            corners = null;
            SN.Vector3 Ray(SN.Vector2 p) => Conv.N(shot.rotation * Conv.U(ScreenFinder.PixelRay(p, shot.fx, shot.fy, shot.cx, shot.cy)));
            if (!ScreenFinder.SolveRectangle(Conv.N(shot.position), Ray(tl), Ray(tr), Ray(br), Ray(bl), width, out SN.Vector3 wtl, out SN.Vector3 wtr, out SN.Vector3 wbr, out SN.Vector3 wbl, out height, out skew)) return false;
            corners = new[] { Conv.U(wtl), Conv.U(wtr), Conv.U(wbl), Conv.U(wbr) };
            return true;
        }

        /// <summary>Three pictures in a row that put the display in the same place: that is where it is.</summary>
        void TryCommit(Finding f)
        {
            int n = f.samples.Count;
            if (n < 3) return;
            var mean = new Vector3[4];
            for (int s = n - 3; s < n; s++) for (int i = 0; i < 4; i++) mean[i] += f.samples[s][i] / 3f;
            float dev = 0;
            for (int s = n - 3; s < n; s++) for (int i = 0; i < 4; i++) dev = Mathf.Max(dev, Vector3.Distance(f.samples[s][i], mean[i]));
            if (dev > 0.012f) { f.why = $"seen, but not in the same place twice ({dev * 1000:0} mm apart)"; return; }
            // an exact rectangle of the size the display reports, laid into the measured corners
            Vector3 centre = (mean[0] + mean[1] + mean[2] + mean[3]) * 0.25f;
            Vector3 right = ((mean[1] - mean[0]) + (mean[3] - mean[2])).normalized;
            Vector3 up = (mean[0] - mean[2]) + (mean[1] - mean[3]);
            up = (up - right * Vector3.Dot(up, right)).normalized;
            float w = 0.5f * (Vector3.Distance(mean[0], mean[1]) + Vector3.Distance(mean[2], mean[3])), h = 0.5f * (Vector3.Distance(mean[0], mean[2]) + Vector3.Distance(mean[1], mean[3]));
            if (f.desc.physWidthM > 0.05f && f.desc.physHeightM > 0.05f) { w = f.desc.physWidthM; h = f.desc.physHeightM; }
            Vector3 rx = right * (w * 0.5f), ry = up * (h * 0.5f);
            _link.Send(Msg.CalibCommit, new MonitorCalibration
            {
                monitorId = f.desc.id, tl = Conv.V(centre - rx + ry), tr = Conv.V(centre + rx + ry), bl = Conv.V(centre - rx - ry), br = Conv.V(centre + rx - ry),
                spaceEpoch = Input.SpaceEpoch, simulated = false, hasDesk = false,
            });
            f.done = true; _findProgressAt = Time.time; _nextFlash = 0;
            Vector3 head = Input.HeadPos;
            float depthSays = Input.RaycastEnvironment(head, (centre - head).normalized, 4f, out Vector3 hit) ? Vector3.Distance(head, hit) : -1f;
            Debug.Log($"[MR] screens: {f.desc.id.Replace("\\\\.\\", "")} found: {w * 1000:0}x{h * 1000:0} mm, {Vector3.Distance(head, centre):0.00} m from the eyes (the depth sensor says {(depthSays > 0 ? depthSays.ToString("0.00") + " m" : "nothing")}), centre {(centre.y - head.y) * 100:0} cm {(centre.y > head.y ? "above" : "below")} eye level, leaning {Mathf.Asin(Mathf.Clamp(Vector3.Cross(right, up).y, -1f, 1f)) * Mathf.Rad2Deg:0} deg, three pictures agree to {dev * 1000:0} mm");
            _sparks.Burst(centre, right, up, 0.05f, 9);
            int found = FoundCount;
            if (found < _finding.Count) Caption($"Screen {found} of {_finding.Count} found.", 1.5f);
        }

        /// <summary>Verification only (lpctl debug screensDump n): the next n pictures are kept as files, half size.</summary>
        void DumpShot(byte[] rgba, int w, int h)
        {
            try
            {
                int ow = w / 2, oh = h / 2;
                byte[] head = System.Text.Encoding.ASCII.GetBytes($"P6\n{ow} {oh}\n255\n");
                var data = new byte[head.Length + ow * oh * 3];
                System.Array.Copy(head, data, head.Length);
                int o = head.Length;
                for (int y = 0; y < oh; y++)
                    for (int x = 0; x < ow; x++) { int i = (y * 2 * w + x * 2) * 4; data[o++] = rgba[i]; data[o++] = rgba[i + 1]; data[o++] = rgba[i + 2]; }
                string path = System.IO.Path.Combine(Application.persistentDataPath, $"lp-shot-{_dumpShots}.ppm");
                System.IO.File.WriteAllBytes(path, data);
                Debug.Log("[MR] screens: picture kept for checking at " + path);
            }
            catch (System.Exception e) { Debug.Log("[MR] screens: could not keep the picture: " + e.Message); }
        }
    }
}
