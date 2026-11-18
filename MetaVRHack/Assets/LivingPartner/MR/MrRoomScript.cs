using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// What a THING DOES, in the room: the headset's side of the room scripts (Shared/RoomScript.cs). A thing may carry
    /// a small Lua program (ItemDef.script), written on the PC when the thing was made - "a baseball bat: when Nilo
    /// holds it, a swing is added". Here the room such a script lives in is made real:
    ///  - the PARTNER does what the script asks while it asks (RoomScriptPose, called from StagePose): it walks to a
    ///    spot, turns, takes a built-in pose, sits or lies on a thing, or plays a MOTION the script defined itself
    ///    (LuaMotion) - and is its own again the moment the script lets go, or something else needs it;
    ///  - the THING is drawn where its script has it: rolling over the desk, hopping, turning, glowing - and in the
    ///    partner's hand, on its head or on its back while it is worn (ItemDef.wornOn; world commands wear / unwear);
    ///  - its LITTLE OBJECTS (a pitched ball, a floating star) are a pool of 24 authored objects, its labels a pool of 4;
    ///  - sounds are SfxSynth's, sparks the room's sparks, lines the partner's caption;
    ///  - PRESSING a scripted thing (MrPoke) calls on_use and, when it offers acts or can be worn, brings a strip of
    ///    buttons up over it: "Wear" / "Take off" and its acts. The PC asks for the same by voice (intent.result "act").
    /// A skill may be a script as well (SkillDef.script: on_use when it is asked for, on_stop when it ends).
    /// A script machine costs about 4 MB (MoonSharp's stacks), so a thing's script is LOADED only when it is needed -
    /// the thing is pressed, worn, asked to act - and put to sleep again when it has done nothing for half a minute;
    /// at most MaxLive are loaded at once. What a sleeping thing keeps is plain data: where it is worn, what it offers.
    /// (on_make therefore runs when the script is first loaded, not when the thing appears.)
    /// For trying things out from the PC: lpctl debug roomScript / roomAct / roomKinds / roomSlow (OnDebugRoomScript).
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Room scripts")]
        [Tooltip("The little objects of the things' scripts (a pool; each is given one of five simple shapes).")]
        [SerializeField] PropView[] _rsObjects;
        [Tooltip("Small paper labels a script floats in the room.")]
        [SerializeField] StoryLabel[] _rsLabels;
        [SerializeField] FxQuad[] _rsLabelBgs;
        [Tooltip("The light of a thing that glows.")]
        [SerializeField] FxQuad[] _rsGlows;
        [Tooltip("The strip of buttons over a scripted thing that was pressed: [0] Wear / Take off, [1..4] its acts.")]
        [SerializeField] UiPanel _rsStrip;
        [SerializeField] UiButton[] _rsButtons;

        const int RoomMaxLive = 4;             // script machines loaded at once
        const float RoomSleepAfter = 30f;      // seconds a loaded script may do nothing before it is put to sleep

        sealed class RsEntry
        {
            public string key = "", itemId = "", name = "", source = "";
            public bool skill, seen, dead, made;
            public RoomScript rs;              // null: asleep (not loaded)
            // what is kept while it sleeps
            public string worn = "", wearSlot = "";
            public readonly List<RoomOffer> offers = new List<RoomOffer>();
            public bool known;                 // offers / wearSlot are what the script declared (it has been loaded once)
            public string hostWorn = "";
            public float placeSentAt = -1f; public int placeRev;
            public float petSince = -1f, lastUsed;
        }

        readonly List<RsEntry> _rs = new List<RsEntry>();
        readonly Dictionary<string, string> _rsLocal = new Dictionary<string, string>();      // scripts attached from the PC for a try-out (by item id / "skill:" + id)
        float _rsSlow = 1f;            // (captures from the PC: the scripts' time drawn out, so that a picture can catch a swing)
        bool _rsKinds;                 // things that have no script get the script of their kind (lpctl debug roomKinds 1)
        RsEntry _rsLead;               // the script the partner is following now
        float _rsTraceT0; int _rsTraceN;      // the scripts' trace lines: at most five a second (RoomLive)
        bool _rsWalking; Vector3 _rsWalkTo;
        string _rsSkillSeen = ""; double _rsSkillAt = -1;
        string _rsStripKey = "", _rsStripShown = ""; float _rsStripUntil;
        bool _rsStripArmed = true; float _rsStripSince;      // the strip comes up only once the finger that pressed the thing has gone (a button must never appear under a finger)
        readonly Dictionary<string, Mesh> _rsMeshes = new Dictionary<string, Mesh>();

        // ------------------------------------------------------------------ the room's frame
        Vector3 RoomOrigin => new Vector3(_worldHome.x, _deskY, _worldHome.z);
        Vector3 RoomToWorld(float x, float y, float z) => RoomOrigin + _worldRight * x + _worldFwd * z + Vector3.up * y;
        void WorldToRoom(Vector3 w, out float x, out float y, out float z) { Vector3 d = w - RoomOrigin; x = Vector3.Dot(d, _worldRight); z = Vector3.Dot(d, _worldFwd); y = d.y; }

        // ------------------------------------------------------------------ the room as a script sees it
        sealed class RsHost : IRoomHost
        {
            readonly MrApp a; readonly RsEntry e;
            public RsHost(MrApp app, RsEntry entry) { a = app; e = entry; }

            public void PetAt(out float x, out float z) { a.WorldToRoom(new Vector3(a._motor.Pos.x, a._deskY, a._motor.Pos.y), out x, out _, out z); }

            public int Hands(RoomHand[] into)
            {
                int n = 0;
                for (int h = 0; h < 2 && n < into.Length; h++)
                {
                    var hs = h == 0 ? a.Input.Right : a.Input.Left;
                    if (!hs.tracked) continue;
                    a.WorldToRoom(hs.indexTip, out float x, out float y, out float z);
                    into[n++] = new RoomHand { x = x, y = y, z = z, pinch = hs.pinch };
                }
                return n;
            }

            public void UserHead(out float x, out float y, out float z) { a.WorldToRoom(a.Input.HeadPos, out x, out y, out z); }
            public void Say(string text, float seconds) { a.Caption(text, seconds); }
            public void Sound(string name, float x, float y, float z) { a.Sfx(name, a.RoomToWorld(x, y, z), 0.7f); }

            public void Sparks(float x, float y, float z, int n, string color)
            {
                Vector3 right = a.Input.HeadRot * Vector3.right; right.y = 0f;
                a._sparks.Burst(a.RoomToWorld(x, y, z), right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right, Vector3.up, 0.028f + 0.0012f * n, Mathf.Clamp(n, 1, 24));
            }

            public void Wear(string slot)
            {
                e.worn = slot ?? "";
                if (e.skill || e.itemId.Length == 0) return;
                // the PC keeps where a thing is worn (a PC app from before this simply does not answer: what is kept here then is what is shown)
                a.SendWorld(slot.Length > 0 ? "wear" : "unwear", e.itemId, slot);
                Debug.Log($"[MR] room script '{e.name}': {(slot.Length > 0 ? "worn on " + slot : "taken off")} (world command {(slot.Length > 0 ? "wear" : "unwear")})");
            }

            public void Things(List<RoomThingInfo> into)
            {
                foreach (var d in a.World.items)
                {
                    if (d == null || !d.inRoom || d.status == "failed") continue;
                    into.Add(new RoomThingInfo { id = d.id, name = d.name ?? "", kind = d.kind ?? "", uses = d.uses ?? new string[0], x = d.x, y = d.y, z = d.z, size = d.sizeM, seat = a.TopOf(d, true), top = a.TopOf(d, false) });
                }
            }

            // Surroundings and tunes are the PC's: it is asked for one the user HAS, by its id. A name the PC does not
            // list is not passed on (the PC would take it for a description and have a new one made).
            public void Ambience(string what)
            {
                if (string.IsNullOrEmpty(what)) { a.SendWorld("ambience", "", "clear"); return; }
                string id = "";
                foreach (var m in a.World.ambiences) if (m != null && (string.Equals(m.id, what, StringComparison.OrdinalIgnoreCase) || string.Equals(m.name, what, StringComparison.OrdinalIgnoreCase))) id = m.id;
                if (id.Length == 0) foreach (var m in WorldDefaults.Ambiences()) if (string.Equals(m.id, what, StringComparison.OrdinalIgnoreCase) || string.Equals(m.name, what, StringComparison.OrdinalIgnoreCase)) id = m.id;
                if (id.Length == 0) { Debug.Log($"[MR] room script '{e.name}': there are no surroundings called '{what}'"); return; }
                a.SendWorld("ambience", "", id);
            }

            public void Music(string what)
            {
                if (string.IsNullOrEmpty(what)) { a.SendWorld("music", "", "stop"); return; }
                string id = "";
                foreach (var s in a.RecordList()) if (string.Equals(s.id, what, StringComparison.OrdinalIgnoreCase) || string.Equals(s.name, what, StringComparison.OrdinalIgnoreCase)) id = s.id;
                if (id.Length == 0) foreach (var s in RecBuiltin) if (string.Equals(s.id, what, StringComparison.OrdinalIgnoreCase) || string.Equals(s.name, what, StringComparison.OrdinalIgnoreCase)) id = s.id;
                if (id.Length == 0) { Debug.Log($"[MR] room script '{e.name}': there is no record called '{what}'"); return; }
                a.SendWorld("music", "", id);
            }
        }

        // ------------------------------------------------------------------ which scripts there are, and which are loaded
        void InitRoomScripts()
        {
            for (int i = 0; i < _rsButtons.Length; i++) { int k = i; _rsButtons[i].press = () => RoomStripPress(k); }
            _rsStrip.gameObject.SetActive(false);
            RoomHideAll();
        }

        void RoomHideAll()
        {
            foreach (var o in _rsObjects) { o.Visible = false; if (o.shadow != null) o.shadow.enabled = false; }
            for (int i = 0; i < _rsLabels.Length; i++) { if (_rsLabels[i].text.Length > 0) _rsLabels[i].text = ""; _rsLabelBgs[i].enabled = false; }
            foreach (var g in _rsGlows) g.enabled = false;
        }

        RsEntry RoomEntry(string key) { foreach (var e in _rs) if (e.key == key) return e; return null; }

        string RoomSource(ItemDef d)
        {
            if (_rsLocal.TryGetValue(d.id, out string local)) return local;
            if (!string.IsNullOrEmpty(d.script)) return d.script;
            return _rsKinds ? RoomScriptExamples.ForKind(d.kind) : "";
        }

        /// <summary>A thing (or skill) with a script is known from here on; its script is not loaded yet.</summary>
        void RoomEnsure(string key, string source, ItemDef d, SkillDef skill)
        {
            var e = RoomEntry(key);
            if (e != null && e.source == source) { e.seen = true; return; }
            if (e != null) { RoomDrop(e, "its script changed"); _rs.Remove(e); }
            e = new RsEntry { key = key, itemId = d != null ? d.id : "", name = d != null ? (string.IsNullOrEmpty(d.name) ? d.kind : d.name) : skill.name, source = source, skill = skill != null, seen = true };
            e.hostWorn = e.worn = d != null ? d.wornOn ?? "" : "";
            _rs.Add(e);
        }

        /// <summary>The loaded script of an entry: loads it when it is asleep (and puts the one used longest ago to sleep when too many are loaded). Null: its script cannot run.</summary>
        RoomScript RoomLive(RsEntry e)
        {
            if (e == null || e.dead) return null;
            e.lastUsed = Time.time;
            if (e.rs != null) return e.rs;
            int live = 0; RsEntry oldest = null, oldestBusy = null;
            foreach (var o in _rs)
                if (o.rs != null)
                {
                    live++;
                    if (o == _rsLead || o == e) continue;
                    if (!o.rs.Active) { if (oldest == null || o.lastUsed < oldest.lastUsed) oldest = o; }
                    else if (oldestBusy == null || o.lastUsed < oldestBusy.lastUsed) oldestBusy = o;
                }
            // (the cap holds even when every loaded script is doing something: the one used longest ago is stopped and put to sleep)
            if (live >= RoomMaxLive)
            {
                if (oldest != null) { RoomSleep(oldest, "another script is needed"); live--; }
                else if (oldestBusy != null) { RoomSleep(oldestBusy, "another script is needed, and all " + live + " loaded ones were busy: this one was used longest ago"); live--; }
            }
            var d = e.skill ? null : Item(e.itemId);
            var rs = new RoomScript(e.source, d, new RsHost(this, e));
            if (!rs.Ok)
            {
                e.dead = true;
                Debug.Log($"[MR] room script '{e.name}': it cannot run - line {rs.ErrorLine}: {rs.Error}. The thing stays an ordinary thing.");
                return null;
            }
            rs.SetWorn(e.worn);
            // (what a script does, line by line: for the simulator and debug builds only, and never more than five lines a second -
            //  a script that calls something in every frame wrote a line in every frame)
            if (Debug.isDebugBuild || Input.Simulated)
                rs.Trace = (fn, what) =>
                {
                    if (fn == "put" || fn == "me_move" || fn == "me_turn") return;
                    float now = Time.unscaledTime;
                    if (now - _rsTraceT0 >= 1f) { _rsTraceT0 = now; _rsTraceN = 0; }
                    if (_rsTraceN++ < 5) Debug.Log($"[MR] room script '{e.name}': {fn} {what}");
                };
            e.rs = rs; e.known = true; e.wearSlot = rs.WearSlot;
            e.offers.Clear(); foreach (var o in rs.Offers) e.offers.Add(new RoomOffer { name = o.name, label = o.label });
            Debug.Log($"[MR] room script '{e.name}' ({(e.skill ? "a skill" : "a thing")}): loaded, {e.source.Split('\n').Length} lines; offers [{string.Join(", ", e.offers.ConvertAll(o => o.name))}]{(rs.Wearable ? ", wearable on " + rs.WearSlot : "")}, motions [{string.Join(", ", rs.Motions)}]; {live + 1} loaded");
            if (!e.made) { e.made = true; rs.Make(); }
            return rs;
        }

        /// <summary>A loaded script that is doing nothing is unloaded: where the thing is worn and what it offers are kept.</summary>
        void RoomSleep(RsEntry e, string why)
        {
            if (e.rs == null) return;
            if (_rsLead == e) _rsLead = null;
            e.worn = e.rs.Ok ? e.rs.WornOn : e.worn;
            if (e.rs.Ok) { if (e.rs.Active) e.rs.Stop(); e.rs.Dispose(); }
            e.rs = null; e.petSince = -1f;
            Debug.Log($"[MR] room script '{e.name}': asleep ({why})");
        }

        void RoomDrop(RsEntry e, string why)
        {
            if (e.rs != null) RoomSleep(e, why);
        }

        /// <summary>Does the thing show something only its loaded script knows (a light, objects of its own, a size, a turn)?</summary>
        static bool RoomHasShow(RoomScript rs)
        {
            if (rs.Me.glow.Length > 0 || Mathf.Abs(rs.Me.scale - 1f) > 0.001f || Mathf.Abs(rs.Me.spin) > 0.01f || rs.Me.moved) return true;
            foreach (var o in rs.Objects) if (o.on) return true;
            return false;
        }

        /// <summary>Is this skill carried out by a script (then its list of acts is not played: MrWorld.UpdateDoing)?</summary>
        bool RoomSkillScripted(SkillDef s) => s != null && (_rsLocal.ContainsKey("skill:" + s.id) || !string.IsNullOrEmpty(s.script));

        // ------------------------------------------------------------------ every frame
        void UpdateRoomScripts(float dt, bool hostOk)
        {
            if (!hostOk || !_worldFrame)
            {
                if (_rs.Count > 0) { foreach (var e in _rs) RoomDrop(e, "the PC is gone"); _rs.Clear(); RoomHideAll(); _rsSkillSeen = ""; }
                if (_rsStripKey.Length > 0 || _rsStrip.Shown) { _rsStripKey = ""; _rsStrip.Hide(); }
                return;
            }
            var w = World;
            foreach (var e in _rs) e.seen = false;
            foreach (var d in w.items)
            {
                if (d == null || !d.inRoom || d.status == "making" || d.status == "failed") continue;
                string src = RoomSource(d);
                if (src.Length > 0) RoomEnsure(d.id, src, d, null);
            }
            foreach (var s in w.skills)
            {
                if (s == null || !RoomSkillScripted(s)) continue;
                RoomEnsure("skill:" + s.id, _rsLocal.TryGetValue("skill:" + s.id, out string local) ? local : s.script, null, s);
            }
            for (int i = _rs.Count - 1; i >= 0; i--) if (!_rs[i].seen) { RoomDrop(_rs[i], "the thing has left the room"); _rs.RemoveAt(i); }

            // a skill that is a script: it begins when the PC says the skill is being done, and is stopped when that ends
            string skillNow = w.doingSkill ?? "";
            if (skillNow != _rsSkillSeen || (skillNow.Length > 0 && w.doingAt != _rsSkillAt))
            {
                var old = _rsSkillSeen.Length > 0 ? RoomEntry("skill:" + _rsSkillSeen) : null;
                if (old != null && old.rs != null && old.rs.Ok && old.rs.Active) old.rs.Stop();
                _rsSkillSeen = skillNow; _rsSkillAt = w.doingAt;
                var now = skillNow.Length > 0 ? RoomEntry("skill:" + skillNow) : null;
                var nrs = RoomLive(now);
                if (nrs != null) { Debug.Log($"[MR] room script '{now.name}': the skill is asked for"); nrs.Use(); }
            }

            // something else needs the partner (a game, the user's hand, a thing the PC sent it to, the screen): the scripts let go of it
            bool taken = _ch.owner != Env.Mr || _mode != Mode.Free || !TransitionIdle || _game.Length > 0 || _tn.active || (_doingNow.Length > 0 && _doingNow != "craft");
            RsEntry lead = null;
            for (int i = 0; i < _rs.Count; i++)
            {
                var e = _rs[i]; var rs = e.rs;
                var d = e.skill ? null : Item(e.itemId);
                bool hostSaid = d != null && (d.wornOn ?? "") != e.hostWorn;      // (the PC's word on where it is worn, when that changes)
                if (hostSaid) { e.hostWorn = d.wornOn ?? ""; e.worn = e.hostWorn; }
                if (rs == null) continue;
                if (!rs.Ok) { Debug.Log($"[MR] room script '{e.name}': switched off - line {rs.ErrorLine}: {rs.Error}"); e.dead = true; e.rs = null; if (_rsLead == e) _rsLead = null; continue; }
                if (d != null) { if (hostSaid) rs.SetWorn(e.hostWorn); rs.SetPlace(d.x, d.y, d.z, d.yaw); }
                if (taken && rs.Pet.active) { Debug.Log($"[MR] room script '{e.name}': the partner is needed elsewhere - stopped"); rs.Stop(); }
                rs.Tick(dt * _rsSlow);
                if (!rs.Ok) continue;
                e.worn = rs.WornOn;
                if (rs.Pet.active) { if (e.petSince < 0f) e.petSince = Time.time; if (lead == null || e.petSince > lead.petSince) lead = e; }
                else e.petSince = -1f;
                // the script has put the thing somewhere: the PC is told when it has come to rest there
                if (d != null && rs.Me.moved && !rs.Me.body.flying && !rs.Active && rs.WornOn.Length == 0 && Mathf.Abs(rs.Me.spin) < 0.01f)
                {
                    if (e.placeSentAt < 0f)
                    {
                        e.placeSentAt = Time.time; e.placeRev = w.revision;
                        _link.Send(Msg.World, new WorldCommand { cmd = "place", id = d.id, x = Mathf.Clamp(rs.Me.body.x, -0.6f, 0.6f), z = Mathf.Clamp(rs.Me.body.z, -0.45f, 0.45f), y = rs.Me.body.y, yaw = rs.Me.yaw });
                    }
                    else if (w.revision != e.placeRev || Time.time - e.placeSentAt > 1.5f) { rs.PlaceTaken(); e.placeSentAt = -1f; }
                }
                // a script that does nothing and shows nothing is put to sleep after a while
                if (rs.Active || RoomHasShow(rs) || _rsStripKey == e.key) e.lastUsed = Time.time;
                else if (Time.time - e.lastUsed > RoomSleepAfter) RoomSleep(e, "nothing to do for " + RoomSleepAfter + " s");
            }
            // one script at a time has the partner: the one that asked last
            foreach (var e in _rs) if (e != lead && e.rs != null && e.rs.Ok && e.rs.Pet.active) { Debug.Log($"[MR] room script '{e.name}': '{lead.name}' has the partner now - stopped"); e.rs.Stop(); e.petSince = -1f; }
            if (_rsLead != null && lead == null) RoomPetReleased();
            _rsLead = lead;
            if (lead != null) _idleSince = Time.time;      // (it is busy: it does not wander off to do something by itself - MrWorld.UpdateDoing)
            RoomDraw();
        }

        /// <summary>No script asks anything of the partner any more: back on its feet where it is, and home.</summary>
        void RoomPetReleased()
        {
            _rsWalking = false;
            if (_ch.owner != Env.Mr || _mode != Mode.Free) return;
            _pos.y = _deskY; _rot = Quaternion.Euler(0, _rot.eulerAngles.y, 0);
            _motor.Teleport(new Vector2(_pos.x, _pos.z), _rot.eulerAngles.y);
            if (_homeSet && _doingNow.Length == 0) _motor.MoveTo(new Vector2(_home.x, _home.z), false, YawTo(Input.HeadPos - _home));
        }

        /// <summary>
        /// The partner's pose while a room script asks something of it (walking where it was sent included). False: no
        /// script asks anything - it walks / idles as usual. Called from StagePose, after the games and before what it
        /// does with its things by itself.
        /// </summary>
        bool RoomScriptPose(float dt)
        {
            var e = _rsLead;
            if (e == null || e.rs == null || !e.rs.Ok || !e.rs.Pet.active || !_worldFrame || _ch.owner != Env.Mr) return false;
            var rs = e.rs; var pet = rs.Pet; float time = Time.time;
            Vector3 here = new Vector3(_motor.Pos.x, _deskY, _motor.Pos.y);
            if (pet.walk)
            {
                Vector3 to = RoomToWorld(pet.gx, 0f, pet.gz);
                if (!_rsWalking || (to - _rsWalkTo).sqrMagnitude > 1e-6f)
                {
                    _rsWalking = true; _rsWalkTo = to;
                    _pos.y = _deskY;
                    _motor.MoveTo(new Vector2(to.x, to.z), Vector3.Distance(here, to) > 0.12f);
                }
                if (pet.motion.Length == 0)
                {
                    // the usual walk takes it there
                    _mixer.Set(Emote.Idle);
                    _mixer.Evaluate(time, dt, _pose);
                    _motor.Update(dt, _H, _pose);
                    _pos = new Vector3(_motor.Pos.x, _deskY, _motor.Pos.y);
                    _rot = Quaternion.Euler(0f, _motor.Yaw, 0f);
                    return true;
                }
            }
            else _rsWalking = false;

            // which way it looks
            float yaw = _motor.Yaw, want = yaw;
            if (pet.face == 2) { Vector3 f = Input.HeadPos - here; f.y = 0f; if (f.sqrMagnitude > 1e-6f) want = YawTo(f); }
            else if (pet.face == 1) { Vector3 f = RoomToWorld(pet.fx, 0f, pet.fz) - here; f.y = 0f; if (f.sqrMagnitude > 1e-6f) want = YawTo(f); }
            yaw = Mathf.MoveTowardsAngle(yaw, want, 540f * dt);
            Vector3 pos = here; Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            if (pet.motion.Length > 0 && rs.MotionPose(_pose)) { }                         // a motion of the script's own
            else if (pet.pose.Length > 0)
            {
                float since = rs.Time - pet.poseSince;
                ItemDef seat = pet.on.Length > 0 ? Item(pet.onId.Length > 0 ? pet.onId : e.itemId) : null;
                if (seat != null && seat.inRoom && pet.on == "lie")
                {
                    // on its back on top of the thing, as it naps on a bed
                    Vector3 spot = ItemPos(seat); float rise = TopOf(seat, true);
                    PoseLib.Sleep(_pose, time);
                    rot = Quaternion.Euler(-90f, YawTo(ItemRot(seat) * Vector3.right), 0f);
                    pos = new Vector3(spot.x, _deskY + seat.y + rise + _H * 0.16f, spot.z) + ItemRot(seat) * Vector3.right * (_H * 0.48f);
                    here = spot;
                }
                else if (seat != null && seat.inRoom)
                {
                    // seated on the thing: the hips at the height of its seat
                    Vector3 spot = ItemPos(seat); float rise = TopOf(seat, true);
                    if (pet.pose == "study") PoseLib.Study(_pose, time);
                    else if (pet.pose == "read") PoseLib.ReadBook(_pose, time, true);
                    else { PoseLib.SitOn(_pose, time); if (pet.face != 1) LookAtUser(_pose); }
                    _pose.HipsOffset.Y = rise / Mathf.Max(0.01f, _H) - Skeleton.HipsHeight + 0.02f;
                    pos = new Vector3(spot.x, _deskY + seat.y, spot.z);
                    here = spot;
                }
                else if (pet.pose == "dance")
                {
                    float beats = _music.isPlaying && _songNow != null ? _music.time * _songNow.bpm / 60f : since * 2f;      // with the tune, when one plays
                    PoseLib.Dance(_pose, time, beats);
                    rot = Quaternion.Euler(0f, yaw + 22f * Mathf.Sin(beats * Mathf.PI * 0.25f), 0f);
                }
                else RoomScriptApi.Pose(pet.pose, _pose, time, since);
            }
            else { PoseLib.Idle(_pose, time); if (pet.face != 1) LookAtUser(_pose); }      // (between two things it was asked to do)
            _pos = pos; _rot = rot;
            _motor.Teleport(new Vector2(here.x, here.z), yaw);
            return true;
        }

        // ------------------------------------------------------------------ what is seen
        Mesh RoomMesh(string shape)
        {
            if (_rsMeshes.TryGetValue(shape, out var have)) return have;
            var parts = new List<ShapePart>();
            switch (shape)
            {
                case "box": parts.Add(new ShapePart { shape = "box", sx = 1f, sy = 1f, sz = 1f, round = 0.18f, color = "#FFFFFF" }); break;
                case "disc": parts.Add(new ShapePart { shape = "cylinder", sx = 1f, sy = 0.24f, sz = 1f, round = 0.4f, color = "#FFFFFF" }); break;
                case "ring": parts.Add(new ShapePart { shape = "torus", sx = 1f, sy = 0.22f, sz = 1f, color = "#FFFFFF" }); break;
                case "star":
                    // five points round a soft middle, standing upright (it is turned to the user)
                    parts.Add(new ShapePart { shape = "sphere", sx = 0.46f, sy = 0.46f, sz = 0.30f, color = "#FFFFFF" });
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k * 72f * Mathf.Deg2Rad;
                        parts.Add(new ShapePart { shape = "cone", x = Mathf.Sin(a) * 0.30f, y = Mathf.Cos(a) * 0.30f, sx = 0.30f, sy = 0.42f, sz = 0.24f, rz = -k * 72f, color = "#FFFFFF" });
                    }
                    break;
                default: parts.Add(new ShapePart { shape = "sphere", sx = 1f, sy = 1f, sz = 1f, color = "#FFFFFF" }); break;
            }
            var mesh = PropMeshes.BuildParts(parts, out _);      // (mesh DATA: the objects that show it are authored)
            if (mesh != null) mesh.name = "RoomScript " + shape;
            _rsMeshes[shape] = mesh;
            return mesh;
        }

        static Color RoomColor(string hex, float alpha = 1f)
        {
            Color c = ColorUtility.TryParseHtmlString(hex ?? "", out Color parsed) ? parsed : Color.white;
            c.a = alpha; return c;
        }

        void RoomDraw()
        {
            int oi = 0, li = 0, gi = 0;
            Vector3 head = Input.HeadPos;
            bool petHere = _ch.owner == Env.Mr && _rig.Visible;
            foreach (var e in _rs)
            {
                var rs = e.rs != null && e.rs.Ok ? e.rs : null;
                // ---- the thing itself
                var d = e.skill ? null : Item(e.itemId); var p = d != null ? PropOf(d.id) : null;
                string worn = rs != null ? rs.WornOn : e.worn;
                if (p != null && p.Visible)
                {
                    float size = d.sizeM;
                    if (worn.Length > 0)
                    {
                        if (petHere) RoomAttach(p, d, worn, rs);
                        else p.Visible = false;                      // (worn by a partner that is inside the screen)
                        p.shadow.enabled = false;
                    }
                    else if (rs != null)
                    {
                        var me = rs.Me;
                        if (me.moved || me.body.flying)
                        {
                            // where the script has it (the PC learns of it when it has come to rest)
                            Vector3 at = RoomToWorld(me.body.x, me.body.y, me.body.z);
                            p.shown = at;
                            Quaternion turned = DeskRot(me.yaw);
                            if (me.body.flying || me.body.roll > 0f)
                            {
                                Vector3 v = _worldRight * me.body.vx + _worldFwd * me.body.vz;
                                Vector3 axis = v.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, v.normalized) : _worldFwd;
                                turned = Quaternion.AngleAxis(me.body.roll / Mathf.Max(0.01f, size * 0.5f) * Mathf.Rad2Deg, axis) * turned;      // it rolls
                            }
                            p.transform.SetPositionAndRotation(at, turned);
                            p.shadow.transform.position = new Vector3(at.x, _deskY + 0.0012f, at.z);
                        }
                        float hop = (rs.Time - me.bounceAt) / 0.32f;
                        if (hop >= 0f && hop < 1f) p.transform.position += Vector3.up * (Mathf.Sin(hop * Mathf.PI) * Mathf.Max(0.012f, size * 0.25f));
                        if (Mathf.Abs(me.scale - 1f) > 0.001f) p.transform.localScale *= me.scale;
                    }
                    if (rs != null && rs.Me.glow.Length > 0 && gi < _rsGlows.Length && p.Visible)
                    {
                        var g = _rsGlows[gi++];
                        Vector3 mid = p.transform.position + Vector3.up * (PokeTall(p, d) * 0.55f);
                        float pulse = 0.92f + 0.08f * Mathf.Sin(Time.time * 3.1f);
                        g.enabled = true;
                        g.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(mid - head, Vector3.up));
                        g.transform.localScale = Vector3.one * (Mathf.Max(size, 0.05f) * 2.6f * pulse);
                        g.SetColors(RoomColor(rs.Me.glow, 0.62f), RoomColor(rs.Me.glow, 0.62f));
                    }
                }
                if (rs == null) continue;
                // ---- its little objects
                foreach (var o in rs.Objects)
                {
                    if (!o.on || oi >= _rsObjects.Length) continue;
                    var v = _rsObjects[oi++];
                    var mesh = RoomMesh(o.shape);
                    if (mesh != null && !ReferenceEquals(v.Mesh, mesh)) v.Mesh = mesh;
                    Vector3 at = RoomToWorld(o.x, o.y, o.z);
                    Quaternion rot;
                    if (o.shape == "star") rot = Quaternion.LookRotation(at - head, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2.4f + oi) * 14f);      // it faces the user, rocking
                    else
                    {
                        Vector3 vel = _worldRight * o.vx + _worldFwd * o.vz;
                        Vector3 axis = vel.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, vel.normalized) : _worldRight;
                        rot = o.shape == "ball" ? Quaternion.AngleAxis(o.roll / Mathf.Max(0.004f, o.size * 0.5f) * Mathf.Rad2Deg, axis) : DeskRot(0f);
                    }
                    float grow = Ui.Pop(Mathf.Clamp01((rs.Time - o.madeAt) / 0.18f));
                    v.transform.SetPositionAndRotation(at, rot);
                    v.transform.localScale = Vector3.one * (o.size * (o.shape == "star" ? 1f / 1.06f : 1f) * Mathf.Max(0.05f, grow));
                    v.texture = null; v.SetLook(RoomColor(o.color), 0f, 2f);
                    v.Visible = true;
                    if (v.shadow != null)
                    {
                        float up = Mathf.Max(0f, o.y);
                        v.shadow.enabled = up < 0.30f;
                        v.shadow.transform.SetPositionAndRotation(new Vector3(at.x, _deskY + 0.0012f, at.z), Quaternion.Euler(90f, 0f, 0f));
                        v.shadow.transform.localScale = Vector3.one * (o.size * 1.3f * Mathf.Lerp(1f, 0.55f, up / 0.30f));
                    }
                }
                // ---- its labels
                foreach (var l in rs.Labels)
                {
                    if (!l.on || li >= _rsLabels.Length) continue;
                    var label = _rsLabels[li]; var bg = _rsLabelBgs[li]; li++;
                    float pop = Mathf.Max(0.001f, Ui.Pop(Mathf.Clamp01((rs.Time - l.since) / 0.18f)) * Mathf.Clamp01((l.until - rs.Time) / 0.15f));
                    label.transform.localScale = Vector3.one * pop;
                    Place(label, RoomToWorld(l.x, l.y, l.z), l.text);
                    bg.enabled = true;
                    bg.transform.SetPositionAndRotation(label.transform.position + label.transform.forward * 0.002f, label.transform.rotation);
                    Ui.SizePanel(bg, (label.Width + 0.018f) * pop, (label.Height + 0.009f) * pop, 0.007f * pop, 0.0010f);
                }
            }
            for (; oi < _rsObjects.Length; oi++) { var v = _rsObjects[oi]; if (v.Visible) { v.Visible = false; if (v.shadow != null) v.shadow.enabled = false; } }
            for (; li < _rsLabels.Length; li++) { if (_rsLabels[li].text.Length > 0) _rsLabels[li].text = ""; if (_rsLabelBgs[li].enabled) _rsLabelBgs[li].enabled = false; }
            for (; gi < _rsGlows.Length; gi++) if (_rsGlows[gi].enabled) _rsGlows[gi].enabled = false;
        }

        /// <summary>
        /// A thing the partner wears: in a hand (it is held by one end and lies along the arm - turned up when the arm
        /// hangs, so that a bat does not go through the desk), on the head, or on the back.
        /// </summary>
        void RoomAttach(PropView p, ItemDef d, string slot, RoomScript script)
        {
            // the thing's long side, and the end it is held by
            Bounds mb = p.Mesh != null ? p.Mesh.bounds : new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            int ax = mb.size.y >= mb.size.x && mb.size.y >= mb.size.z ? 1 : mb.size.x >= mb.size.z ? 0 : 2;
            Vector3 along = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            float scale = d.sizeM * Mathf.Min(1f, 0.09f / Mathf.Max(0.01f, d.sizeM));      // nothing it wears is larger than itself
            Vector3 at; Quaternion rot;
            if (slot == "head")
            {
                Transform headBone = _rig.Bones[(int)Bone.Head];
                scale = d.sizeM * Mathf.Min(1f, 0.06f / Mathf.Max(0.01f, d.sizeM));
                rot = headBone.rotation;
                at = _rig.HeadTopWorld - rot * Vector3.up * (0.045f * _H) - rot * new Vector3(mb.center.x, mb.min.y, mb.center.z) * scale;
            }
            else if (slot == "handR" && d.kind == "cup" && script?.Pet.motion == "sip")
            {
                float u = script.Pet.MotionU;
                float lift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, .4f, u)) *
                             (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.68f, .96f, u)));
                scale = Mathf.Min(scale, .35f * _H);
                rot = _rot * Quaternion.Euler(-27f * lift, 0, 0);
                Vector3 grip = new Vector3(mb.max.x, mb.center.y, mb.center.z);
                Vector3 rim = new Vector3(mb.center.x, mb.max.y, mb.min.z);
                Vector3 mouth = _rig.Bones[(int)Bone.Head].TransformPoint(new Vector3(.035f, .13f, .245f));
                at = Vector3.Lerp(_rig.HandRWorld - rot * grip * scale, mouth - rot * rim * scale, lift);
            }
            else if (slot == "back")
            {
                rot = _rot * Quaternion.Euler(-14f, 0f, 0f) * Quaternion.FromToRotation(along, Vector3.up);
                at = _rig.ChestWorld - _rot * Vector3.forward * (0.30f * _H) - Vector3.up * (0.08f * _H) - rot * (along * mb.min[ax] + Vector3.Scale(mb.center, Vector3.one - along)) * scale;
            }
            else
            {
                Vector3 hand, dir;
                if (slot == "handL") { Transform b = _rig.Bones[(int)Bone.ArmLL]; hand = b.TransformPoint(new Vector3(0f, -Skeleton.ArmLower * 0.6f, 0f)); dir = b.TransformDirection(Vector3.down); }
                else { hand = _rig.HandRWorld; dir = _rig.HandRDir; }
                Vector3 axis = (dir.normalized + Vector3.up * (Mathf.Max(0f, -dir.normalized.y) * 1.7f)).normalized;
                rot = Quaternion.FromToRotation(along, axis);
                at = hand - rot * (along * (mb.min[ax] + 0.14f * mb.size[ax]) + Vector3.Scale(mb.center, Vector3.one - along)) * scale;
            }
            p.transform.SetPositionAndRotation(at, rot);
            p.transform.localScale = Vector3.one * scale * Mathf.Max(0.001f, Ui.Pop(p.appear));
            p.shown = at;
        }

        // ------------------------------------------------------------------ pressing a scripted thing
        /// <summary>
        /// A thing was pressed (MrPoke.PokeAct). True when its script took it: on_use was called and / or its buttons
        /// came up. False: it has no script (or the script has nothing for a press) - what the press did before happens.
        /// </summary>
        bool RoomScriptPressed(ItemDef d)
        {
            var e = d != null ? RoomEntry(d.id) : null;
            var rs = RoomLive(e);
            if (rs == null) return false;
            RoomClaim();
            bool used = rs.Use();
            bool buttons = e.offers.Count > 0 || e.wearSlot.Length > 0;
            if (buttons)
            {
                if (_rsStripKey == e.key && !used && _rsStripArmed && _rsStrip.Shown) { _rsStripKey = ""; }      // pressed again while the buttons are UP: they go away (not while they wait for the finger to leave)
                else { _rsStripKey = e.key; _rsStripUntil = Time.time + 9f; _rsStripShown = ""; _rsStrip.Hide(); _rsStripArmed = false; _rsStripSince = Time.time; }
            }
            Debug.Log($"[MR] room script '{e.name}': pressed -> {(used ? "on_use" : "no on_use")}{(buttons ? ", its buttons " + (_rsStripKey == e.key ? "come up" : "go away") : "")}");
            return used || buttons;
        }

        /// <summary>
        /// The user asks something of a thing's script: what the partner was doing BY ITSELF (left alone, it had gone to
        /// sit or to play) ends, so that it is free for the script. What the PC asked it to do is not touched.
        /// </summary>
        void RoomClaim()
        {
            if (!_selfDoing) return;
            Debug.Log("[MR] room script: what the partner was doing by itself (" + _doingNow + ") ends - a thing's script is asked for");
            _selfDoing = false; SetDoing(""); _idleSince = Time.time;
        }

        void RoomStripPress(int k)
        {
            var e = RoomEntry(_rsStripKey);
            if (e == null || e.dead) return;
            _rsStripUntil = Time.time + 9f; _rsStripShown = "";
            if (k == 0) RoomDo(e, e.worn.Length > 0 ? "unwear" : "wear", "its button");
            else if (k - 1 < e.offers.Count) RoomDo(e, e.offers[k - 1].name, "its button");
        }

        /// <summary>One thing asked of a script: use | wear | unwear | stop | the name of an act it offers.</summary>
        bool RoomDo(RsEntry e, string what, string how)
        {
            if (what == "stop") { if (e != null && e.rs != null && e.rs.Ok) e.rs.Stop(); return true; }
            var rs = RoomLive(e);
            if (rs == null) return false;
            RoomClaim();
            bool ok;
            switch (what)
            {
                case "use": ok = rs.Use(); break;
                case "wear": ok = rs.Wear(); break;
                case "unwear": ok = rs.Unwear(); break;
                default:
                    ok = rs.Act(what);
                    if (!ok) foreach (var o in rs.Offers) if (string.Equals(o.label, what, StringComparison.OrdinalIgnoreCase) || string.Equals(o.name, what, StringComparison.OrdinalIgnoreCase)) { ok = rs.Act(o.name); break; }
                    break;
            }
            e.worn = rs.WornOn;
            Debug.Log($"[MR] room script '{e.name}': {what} asked by {how} -> {(ok ? "done" : "it has nothing for that")}");
            if (ok && _ch.owner != Env.Mr) Caption("I'm inside the screen - call me out and ask again.", 3.5f);
            return ok;
        }

        /// <summary>"item id or name|what": asked by the PC (intent.result kind act, lpctl debug roomAct).</summary>
        void RoomAct(string text, string how)
        {
            text = text ?? "";
            int bar = text.IndexOf('|');
            string which = (bar >= 0 ? text.Substring(0, bar) : text).Trim(), what = bar >= 0 ? text.Substring(bar + 1).Trim() : "use";
            var e = RoomFind(which);
            if (what == "sip" && bar > 0)
            {
                var item = Item(e?.itemId ?? which);
                if (item?.kind == "cup" || (item == null && e == null && which.StartsWith("item-", StringComparison.Ordinal)))
                {
                    if (_sipPending != null) StopCoroutine(_sipPending);
                    _sipPending = StartCoroutine(SipWhenPlaced(item?.id ?? which, how));
                    return;
                }
            }
            if (e == null) { Debug.Log($"[MR] room script: nothing with a script is called '{which}' ({how} asked for '{what}')"); return; }
            RoomDo(e, what.Length > 0 ? what : "use", how);
        }

        Coroutine _sipPending;
        System.Collections.IEnumerator SipWhenPlaced(string id, string how)
        {
            // A spoken act can arrive before the world snapshot placing the cup, or before its GLB download.
            float until = Time.unscaledTime + 20f;
            yield return null;
            while (Time.unscaledTime < until && _ch.owner == Env.Mr && _game.Length == 0 && !_tn.active)
            {
                var entry = RoomEntry(id);
                var item = Item(id);
                var prop = PropOf(id);
                if (entry != null && item != null && item.inRoom && prop != null && prop.Visible &&
                    (string.IsNullOrEmpty(item.model) || (_shapes.TryGetValue("glb:" + item.model, out var shape) && shape.mesh != null && prop.Mesh == shape.mesh)))
                {
                    RoomDo(entry, "sip", how);
                    _sipPending = null;
                    yield break;
                }
                yield return null;
            }
            if (_ch.owner == Env.Mr && _game.Length == 0 && !_tn.active) Caption("I couldn't bring the cup out. Try again.", 3f);
            _sipPending = null;
        }

        /// <summary>The scripted thing (or skill) with this id or name; with no text, the first one.</summary>
        RsEntry RoomFind(string which)
        {
            which = (which ?? "").Trim();
            foreach (var e in _rs) if (which.Length == 0 || string.Equals(e.key, which, StringComparison.OrdinalIgnoreCase) || string.Equals(e.itemId, which, StringComparison.OrdinalIgnoreCase) || e.key == "skill:" + which) return e;
            foreach (var e in _rs) if (string.Equals(e.name, which, StringComparison.OrdinalIgnoreCase)) return e;
            foreach (var e in _rs) if ((e.name ?? "").IndexOf(which, StringComparison.OrdinalIgnoreCase) >= 0) return e;
            int i = FindProp(which);
            return i >= 0 ? RoomEntry(_props[i].itemId) : null;
        }

        /// <summary>Everything the scripts are doing with the partner ends (the user said stop, or called it over).</summary>
        void RoomStopAll(string why)
        {
            foreach (var e in _rs) if (e.rs != null && e.rs.Ok && e.rs.Active) { Debug.Log($"[MR] room script '{e.name}': stopped ({why})"); e.rs.Stop(); }
        }

        /// <summary>The strip of buttons over the thing that was pressed. Called once a frame with the boards (MrUi.UpdateUi).</summary>
        void UpdateRoomStrip(float dt)
        {
            var p = _rsStrip;
            var e = _rsStripKey.Length > 0 ? RoomEntry(_rsStripKey) : null;
            var prop = e != null ? PropOf(e.itemId) : null; var d = e != null ? Item(e.itemId) : null;
            if (e == null || e.dead || prop == null || d == null || _calibrating || Time.time > _rsStripUntil) { _rsStripKey = ""; p.Hide(); return; }
            bool wearable = e.wearSlot.Length > 0;
            int n = (wearable ? 1 : 0) + Mathf.Min(e.offers.Count, _rsButtons.Length - 1);
            if (n == 0) { _rsStripKey = ""; p.Hide(); return; }
            string key = e.key + "/" + e.worn + "/" + e.offers.Count + "/" + n;
            if (key != _rsStripShown)
            {
                _rsStripShown = key;
                const float bw = 0.070f, gap = 0.008f, bh = 0.030f;
                float width = n * bw + (n - 1) * gap + 0.024f, height = bh + 0.020f;
                p.width = width; p.height = height;
                Ui.SizePanel(p.bg, width, height, height * 0.5f, 0.0016f);
                if (p.glow != null) p.glow.transform.localScale = new Vector3(width * 1.22f, height * 1.34f, 1f);
                int slot = 0;
                for (int i = 0; i < _rsButtons.Length; i++)
                {
                    var b = _rsButtons[i];
                    bool on = i == 0 ? wearable : i - 1 < e.offers.Count;
                    if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
                    if (!on) continue;
                    b.transform.localPosition = new Vector3((slot - (n - 1) * 0.5f) * (bw + gap), 0f, 0f); b.homeSet = false;
                    slot++;
                    FitInto(b.label, i == 0 ? (e.worn.Length > 0 ? "Take off" : "Wear") : e.offers[i - 1].label, bw - 0.010f);
                    bool lit = i == 0 && e.worn.Length > 0;
                    b.color = lit ? HoneyPaper : Paper; b.color2 = lit ? new Color(1f, 0.84f, 0.50f, 0.98f) : PaperDeep;
                }
            }
            // over the thing - over the partner's head while it wears the thing - turned to the user
            Vector3 anchor = e.worn.Length > 0 && _rig.Visible ? _rig.HeadTopWorld + Vector3.up * (0.085f + p.height * 0.5f)
                : prop.shown + Vector3.up * (PokeTall(prop, d) + 0.048f + p.height * 0.5f);
            if (!_rsStripArmed)
            {
                bool clear = Time.time - _rsStripSince > 0.45f;
                for (int h = 0; h < 2 && clear; h++) { var hs = h == 0 ? Input.Right : Input.Left; if (hs.tracked && (hs.indexTip - anchor).magnitude < 0.10f) clear = false; }
                if (!clear) { if (p.Shown) p.Hide(); _rsStripUntil = Mathf.Max(_rsStripUntil, Time.time + 8f); return; }
                _rsStripArmed = true;
            }
            p.Show();
            FaceUser(p.transform, anchor);
            UpdateButtons(p, dt);
        }

        // ------------------------------------------------------------------ trying things out from the PC
        /// <summary>
        /// lpctl debug roomScript 0 0 0 "item id or name|path to a .lua file"  attaches a script to a thing here (or to a
        ///     skill by its id); instead of a path: the id of one of RoomScriptExamples (bat, wand, drum, football, lamp,
        ///     chair ...), "kind" for the script of the thing's kind, "none" to take it off again
        /// lpctl debug roomAct 0 0 0 "item|use / wear / unwear / stop / an act"
        /// lpctl debug roomKinds 1   every thing without a script gets the script of its kind (0: not any more)
        /// lpctl debug roomButton k  presses button k of the strip (0 = Wear / Take off)
        /// lpctl debug roomSlow f    the scripts' time runs at f (0.02 .. 1; 0 = as it is) - for pictures of something quick
        /// </summary>
        bool OnDebugRoomScript(DebugCommand c)
        {
            switch (c.cmd)
            {
                case "roomAct": RoomAct(c.text, "the PC (debug)"); return true;
                case "roomKinds": _rsKinds = c.value > 0.5f; Debug.Log("[MR] room script: things without a script " + (_rsKinds ? "get the script of their kind" : "have none")); return true;
                case "roomButton": RoomStripPress((int)c.value); return true;
                case "roomSlow": _rsSlow = Mathf.Clamp(c.value <= 0f ? 1f : c.value, 0.02f, 1f); Debug.Log("[MR] room script: time runs at " + _rsSlow.ToString("0.00")); return true;
                case "roomScript":
                {
                    string text = c.text ?? ""; int bar = text.IndexOf('|');
                    string which = (bar >= 0 ? text.Substring(0, bar) : text).Trim(), from = bar >= 0 ? text.Substring(bar + 1).Trim() : "kind";
                    string key = "", kind = "";
                    int i = FindProp(which);
                    if (i >= 0) { key = _props[i].itemId; kind = Item(key)?.kind ?? ""; }
                    else foreach (var s in World.skills) if (s != null && (string.Equals(s.id, which, StringComparison.OrdinalIgnoreCase) || string.Equals(s.name, which, StringComparison.OrdinalIgnoreCase))) key = "skill:" + s.id;
                    if (key.Length == 0) { Debug.Log($"[MR] room script: nothing on the desk (and no skill) is called '{which}'"); return true; }
                    if (from == "none" || from.Length == 0) { _rsLocal.Remove(key); Debug.Log($"[MR] room script: '{which}' has no script of this headset's any more"); return true; }
                    string lua = "";
                    try { if (System.IO.File.Exists(from)) lua = System.IO.File.ReadAllText(from); } catch (Exception ex) { Debug.Log("[MR] room script: " + from + " could not be read: " + ex.Message); }
                    if (lua.Length == 0 && from == "kind") lua = RoomScriptExamples.ForKind(kind);
                    if (lua.Length == 0) foreach (var set in new[] { RoomScriptExamples.Made, RoomScriptExamples.Shipped, RoomScriptExamples.Skills }) foreach (var ex in set) if (lua.Length == 0 && ex.id == from) lua = ex.script;
                    if (lua.Length == 0) { Debug.Log($"[MR] room script: '{from}' is neither a file nor one of the examples"); return true; }
                    _rsLocal[key] = lua.Replace("\r\n", "\n");
                    Debug.Log($"[MR] room script: '{which}' ({key}) gets the script {from} ({lua.Length} characters)");
                    return true;
                }
            }
            return false;
        }
    }
}
