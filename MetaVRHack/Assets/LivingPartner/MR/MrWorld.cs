using System;
using System.Collections;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Networking;

namespace LivingPartner.MR
{
    /// <summary>
    /// The partner's world as the headset shows it. What exists and what is going on is DATA from the PC (WorldState:
    /// items, skills, the tune, the surroundings, what it was asked to do) - built there by a language model from what
    /// the user says. Here that data is carried out:
    ///  - THINGS stand on the desk round the partner's spot (a pool of PropView objects; a built-in toy shape at once,
    ///    the generated model when its file has arrived). A pinch picks one up and puts it somewhere else.
    ///  - WHAT IT DOES WITH THEM: it walks to the chair and studies at the desk, naps on the bed, reads, kicks the ball,
    ///    dances - poses fitted to where the things are and how tall they are. Left alone, it does these by itself.
    ///  - SKILLS are lists of such acts; the tune of one is turned into sound here (SongSynth), and the surroundings of
    ///    one are drawn round the work area (AmbienceView) - the screens and the desk stay as they are.
    ///  - The first seconds: a word mark and a line that says what is happening, until the PC and the screens are found.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("World")]
        [SerializeField] PropView[] _props;
        [SerializeField] AmbienceView _ambience;
        [SerializeField] AudioSource _music;
        [Header("Intro")]
        [SerializeField] UiSprite _introLogo;
        [SerializeField] FxQuad _introRing, _introBg;
        [SerializeField] StoryLabel _introLine;

        static readonly WorldState NoWorld = new WorldState();
        WorldState World => _snap != null && _snap.world != null ? _snap.world : NoWorld;

        // the desk as the things know it: the partner's home spot, "away from the user" and "to the user's right" there
        Vector3 _worldHome, _worldFwd = Vector3.forward, _worldRight = Vector3.right; bool _worldFrame;

        Vector3 _worldShift;      // the things' anchor minus the partner's spot (zero until the user moves the spot)

        void WorldFrame()
        {
            // The things are laid out round the spot the app CHOSE for the partner on the desk. When the user moves the
            // partner's spot - carries it somewhere by a pinch, or calls it over - the things stay where they are:
            // _worldShift holds how far the spot has been moved since it was chosen. (Without it every piece of furniture
            // slid across the desk after the partner.)
            if (!_homeSet) return;
            Vector3 anchor = _home + _worldShift;
            if (_worldFrame && (anchor - _worldHome).sqrMagnitude < 1e-6f) return;
            Vector3 f = anchor - Input.HeadPos; f.y = 0;
            if (f.sqrMagnitude < 1e-4f) { f = Input.HeadRot * Vector3.forward; f.y = 0; }
            if (f.sqrMagnitude < 1e-4f) return;
            _worldHome = anchor; _worldFwd = f.normalized; _worldRight = Vector3.Cross(Vector3.up, _worldFwd); _worldFrame = true;
        }

        Vector3 ItemPos(ItemDef d) => new Vector3(_worldHome.x, _deskY, _worldHome.z) + _worldRight * d.x + _worldFwd * d.z;
        Quaternion ItemRot(ItemDef d) => Quaternion.LookRotation(_worldFwd, Vector3.up) * Quaternion.Euler(0, d.yaw, 0);
        ItemDef Item(string id) { foreach (var i in World.items) if (i.id == id) return i; return null; }
        PropView PropOf(string id) { foreach (var p in _props) if (p.itemId == id) return p; return null; }
        void SendWorld(string cmd, string id = "", string text = "", float x = 0, float z = 0, float yaw = 0) => _link.Send(Msg.World, new WorldCommand { cmd = cmd, id = id, text = text, x = x, z = z, yaw = yaw });

        // ------------------------------------------------------------------ files from the PC (models, icons)
        sealed class Shape { public Mesh mesh; public Texture2D texture; public float height = 1f; public bool loading; public bool failed; }
        readonly Dictionary<string, Shape> _shapes = new Dictionary<string, Shape>();
        readonly Dictionary<string, Texture2D> _icons = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _iconAsked = new HashSet<string>();
        readonly Dictionary<string, float> _iconFailedAt = new Dictionary<string, float>();      // when the fetch of a picture last failed

        string WorldUrl(string file)
        {
            string ws = _link.CurrentUrl ?? "";
            if (ws.Length == 0) return "";
            string http = ws.Replace("wss://", "https://").Replace("ws://", "http://");
            int cut = http.LastIndexOf("/ws", StringComparison.Ordinal);
            if (cut > 0) http = http.Substring(0, cut);
            return http + "/world/" + file;
        }

        IEnumerator Fetch(string file, Action<byte[]> done)
        {
            string url = WorldUrl(file);
            if (url.Length == 0) { done(null); yield break; }
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 30;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Debug.Log($"[MR] world: could not fetch {file}: {req.error}"); done(null); }
                else done(req.downloadHandler.data);
            }
        }

        /// <summary>The icon of an item or a skill (a picture made on the PC), or null until it has arrived.</summary>
        Texture2D Icon(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_icons.TryGetValue(file, out var have)) return have;
            // a picture that could not be fetched is asked for again, but not before ten seconds have passed (it used to
            // be asked for again in the very next frame, for ever, with a line in the log each time)
            if (_iconFailedAt.TryGetValue(file, out float failedAt) && Time.time - failedAt < 10f) return null;
            if (_iconAsked.Add(file))
                StartCoroutine(Fetch(file, bytes =>
                {
                    if (bytes == null) { _iconAsked.Remove(file); _iconFailedAt[file] = Time.time; return; }
                    _iconFailedAt.Remove(file);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Icon " + file, wrapMode = TextureWrapMode.Clamp };
                    if (tex.LoadImage(bytes, true)) { _icons[file] = tex; Debug.Log($"[MR] world: icon {file} arrived ({tex.width}x{tex.height})"); } else Destroy(tex);
                }));
            return null;
        }

        Shape ShapeFor(ItemDef d)
        {
            // the generated model when there is one (and it could be read), otherwise the built-in toy shape
            if (!string.IsNullOrEmpty(d.model))
            {
                string key = "glb:" + d.model;
                if (!_shapes.TryGetValue(key, out var s))
                {
                    s = new Shape { loading = true }; _shapes[key] = s;
                    StartCoroutine(Fetch(d.model, bytes =>
                    {
                        s.loading = false;
                        if (bytes == null) { s.failed = true; return; }
                        var mesh = PropMeshes.FromGlb(bytes, out Texture2D tex, out float h, out string err);
                        if (mesh == null) { s.failed = true; Debug.Log($"[MR] world: model {d.model} could not be read: {err}"); return; }
                        s.mesh = mesh; s.texture = tex; s.height = h;
                        Debug.Log($"[MR] world: model {d.model} arrived: {mesh.vertexCount} vertices, {bytes.Length / 1024} kB, texture {(tex != null ? tex.width + "x" + tex.height : "none")}");
                    }));
                }
                if (s.mesh != null) return s;
            }
            // No generated model: its own shape, sculpted on the PC out of simple parts, when it has one. Kept under the
            // item's id and a fingerprint of the parts, so that a thing sculpted anew is built anew.
            if (d.parts != null && d.parts.Count > 0)
            {
                string pk = PropMeshes.PartsKey(d.id, d.parts);
                if (!_shapes.TryGetValue(pk, out var sc))
                {
                    var mesh = PropMeshes.BuildParts(d.parts, out float ph);
                    sc = new Shape { mesh = mesh, height = ph, failed = mesh == null }; _shapes[pk] = sc;
                    Debug.Log(mesh != null ? $"[MR] world: {d.name} sculpted out of {d.parts.Count} parts: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles, height {ph:0.00}" : $"[MR] world: the {d.parts.Count} parts of {d.name} could not be built; it keeps the built-in shape");
                }
                if (sc.mesh != null)
                {
                    // UpdateProps names what a prop shows by the thing's kind and colour whenever that is not a generated
                    // model, and neither changes when the sculpted shape arrives for a thing that is already out as its
                    // built-in shape. So such a prop is handed its new shape here (with the same little pop).
                    var p = PropOf(d.id);
                    if (p != null && p.shape.Length > 0 && !p.shape.StartsWith("glb:") && !ReferenceEquals(p.Mesh, sc.mesh))
                    {
                        p.Mesh = sc.mesh; p.texture = null; p.height = sc.height;
                        if (p.appear > 0.5f) { p.appear = 0.55f; _sparks.Burst(p.shown + Vector3.up * (d.sizeM * 0.5f), _worldRight, Vector3.up, 0.07f, 16); }
                    }
                    return sc;
                }
            }
            string bk = "kind:" + d.kind + ":" + d.color;
            if (!_shapes.TryGetValue(bk, out var b))
            {
                PropMeshes.Measures(d.kind, out float h, out _);
                b = new Shape { mesh = PropMeshes.Build(d.kind, d.color), height = h }; _shapes[bk] = b;
            }
            return b;
        }

        /// <summary>How high above the desk the seat (or the top) of a thing is, in metres.</summary>
        float TopOf(ItemDef d, bool seat)
        {
            var p = PropOf(d.id);
            bool model = p != null && p.texture != null || (p != null && p.shape.StartsWith("glb:"));
            if (model) return p.height * d.sizeM * (seat ? 0.5f : 1f);
            // sculpted out of parts: whoever sculpted it said how high its seat and its top are, in the measure of the
            // shape (0 = not said: a thing with only a top is sat on there, and anything else at half its height)
            if (d.parts != null && d.parts.Count > 0 && _shapes.TryGetValue(PropMeshes.PartsKey(d.id, d.parts), out var sc) && sc.mesh != null)
            {
                float at = seat ? (d.seat > 0f ? d.seat : d.top > 0f ? d.top : sc.height * 0.5f) : (d.top > 0f ? d.top : sc.height);
                return Mathf.Min(at, sc.height) * d.sizeM;
            }
            PropMeshes.Measures(d.kind, out float h, out float s);
            return (seat ? s : h) * d.sizeM;
        }

        // ------------------------------------------------------------------ every frame
        int _grab = -1; int _grabHand; Vector3 _grabOffset; float _nextWorldLog;
        float _grabLostT;      // for how long the carrying hand has not been seen
        // the thing that was just let go, where, when, and the revision of the world it was let go in
        string _dropItem = ""; Vector3 _dropPos; float _dropAt; int _dropRev;

        void UpdateWorld(float dt, bool hostOk)
        {
            UpdateIntro(dt, hostOk);
            WorldFrame();
            var w = hostOk ? World : NoWorld;
            MakingNow(w);              // is something being made? (the workbench, MrCraft)
            UpdateProps(dt, w, hostOk);
            UpdateCarryFx(dt);         // the ring under a thing that is turned, the ring where it would land (MrCarry)
            UpdateArrive(dt, hostOk);  // a new thing grows where it stands, with a ring round it and an arrow to it (MrArrive)
            UpdateDoing(dt, w);
            UpdateMusic(dt, w);
            UpdateAmbience(dt, w);
            UpdateWorkbench(dt);
        }

        // ------------------------------------------------------------------ the things on the desk
        void UpdateProps(float dt, WorldState w, bool hostOk)
        {
            bool show = hostOk && _worldFrame && _ch.owner == Env.Mr || (hostOk && _worldFrame && _ch.owner == Env.Pc);      // its things stay out on the desk while it is in the screen
            Vector3 head = Input.HeadPos;
            // which prop shows which item: keep what is already shown, give free props to new things
            foreach (var p in _props)
            {
                if (p.itemId.Length == 0) continue;
                var d = show ? Item(p.itemId) : null;
                if (d == null || !d.inRoom) { p.itemId = ""; p.shape = ""; p.Visible = false; p.shadow.enabled = false; p.progress.enabled = false; }
            }
            if (!show) return;
            StackFallback(w);      // (a PC app that does not know of things standing on things: MrCarry)
            for (int k = 0; k < w.items.Count; k++)
            {
                var d = w.items[k];
                if (!d.inRoom || PropOf(d.id) != null) continue;
                foreach (var p in _props)
                    if (p.itemId.Length == 0)
                    {
                        p.itemId = d.id; p.appear = 0f; p.shown = ItemPosY(d);
                        _sparks.Burst(p.shown + Vector3.up * (d.sizeM * 0.5f), _worldRight, Vector3.up, 0.06f, 14);
                        Sfx("pop", p.shown, 0.6f);
                        Debug.Log($"[MR] world: {d.name} ({d.kind}, {d.sizeM * 100:0} cm) is on the desk");
                        break;
                    }
            }
            // picking a thing up: thumb and finger of either hand closed on it, then it follows the hand until they open
            if (_grab >= 0)
            {
                var gp = _props[_grab]; var gd = Item(gp.itemId);
                var hs = _grabHand == 0 ? Input.Right : Input.Left;
                // The carrying hand is not seen for a frame now and then: the thing waits where it was for up to 0.3 s
                // before that counts as letting go. (One such frame used to drop it there and then - and the place it
                // was dropped at is sent to the PC and kept.)
                _grabLostT = hs.tracked ? 0f : _grabLostT + dt;
                if (gd == null || (hs.tracked ? !hs.pinch : _grabLostT > 0.3f))
                {
                    if (gd != null)
                    {
                        var other = _grabHand == 0 ? Input.Left : Input.Right;
                        bool intoBag = CarryOverPalm(gp, gd, other, hs);      // let go at the open other palm: back into the bag (MrCarry)
                        if (intoBag)
                        {
                            // Until the PC's answer is here the thing stays where it was let go. (It slid back to its old
                            // place first, and jumped to the new one with the next snapshot.)
                            _dropItem = gd.id; _dropPos = gp.shown; _dropAt = Time.time; _dropRev = w.revision; _dropYaw = _dropYawShown = _grabYaw; _dropY = 0f;
                            SendWorld("store", gd.id); _sparks.Burst(other.palmPos, _worldRight, Vector3.up, 0.05f, 10); Sfx("pop", other.palmPos, 0.6f); CarryStored(gd);
                        }
                        else CarryRelease(gp, gd, w);      // on the desk, or on top of another thing; out of the way of the others (MrCarry)
                    }
                    _grab = -1;
                }
                else if (hs.tracked) CarryStep(dt, gp, gd, hs);      // it follows the pinch and turns with the hand (MrCarry)
            }
            else if ((!Input.Simulated || SimCarry) && _mode != Mode.Held && (_doingNow.Length == 0 || _doingNow == "craft"))      // (while it is at its workbench the user goes on moving things about; the piece in the making cannot be picked up)
            {
                for (int h = 0; h < 2 && _grab < 0; h++)
                {
                    var hs = h == 0 ? Input.Right : Input.Left;
                    if (!hs.tracked || !hs.pinchDown) continue;
                    Vector3 tip = (hs.indexTip + hs.thumbTip) * 0.5f;
                    for (int i = 0; i < _props.Length; i++)
                    {
                        var p = _props[i]; var d = p.itemId.Length > 0 ? Item(p.itemId) : null;
                        if (d == null || d.status == "making") continue;
                        Vector3 mid = p.shown + Vector3.up * (d.sizeM * 0.35f);
                        if (Vector3.Distance(tip, mid) > Mathf.Max(0.045f, d.sizeM * 0.6f)) continue;
                        // (the same pinch closed nearer to the partner than to this thing: then it is the partner that is picked up - MrHold)
                        if (_holdWait[h] >= 0f && _ch.owner == Env.Mr && HoldDistance(tip) < Mathf.Max(0f, Vector3.Distance(tip, mid) - d.sizeM * 0.3f)) continue;
                        CarryBegin(i, h, tip);
                        break;
                    }
                }
            }
            // (a thing that was just let go: its new place has arrived with the PC's answer, or the answer is overdue)
            if (_dropItem.Length > 0 && (w.revision != _dropRev || Time.time - _dropAt > 1.5f)) _dropItem = "";
            _dropYawShown = Mathf.Lerp(_dropYawShown, _dropYaw, 1f - Mathf.Exp(-dt * 10f));
            Arrivals(w);      // a thing that was not there before: it arrives where it is seen (MrArrive)
            for (int i = 0; i < _props.Length; i++)
            {
                var p = _props[i];
                if (p.itemId.Length == 0) continue;
                var d = Item(p.itemId);
                var s = ShapeFor(d);
                string key = s.texture != null || (!string.IsNullOrEmpty(d.model) && _shapes.TryGetValue("glb:" + d.model, out var g) && g == s) ? "glb:" + d.model : "kind:" + d.kind + ":" + d.color;
                if (p.shape != key) { p.shape = key; p.Mesh = s.mesh; p.texture = s.texture; p.height = s.height; if (p.appear > 0.5f) { p.appear = 0.55f; _sparks.Burst(p.shown + Vector3.up * (d.sizeM * 0.5f), _worldRight, Vector3.up, 0.07f, 16); } }
                p.appear = Mathf.Min(1f, p.appear + dt / 0.45f);
                // where it is and how it is turned: in the hand, where it was just let go (it glides to its spot and
                // settles on its angle), on a thing that is being carried (it goes along at once), or where the PC has it
                Vector3 want; Quaternion turned; float follow = 12f, under = d.y;
                if (_grab == i) { want = p.shown; turned = DeskRot(_grabYaw); under = _grabOnY; }
                else if (p.itemId == _dropItem) { want = _dropPos; turned = DeskRot(_dropYawShown); follow = 9f; under = _dropY; }
                else if (Rides(d, out want, out turned)) { follow = 45f; under = want.y - _deskY; }
                else { want = ItemPosNow(d); turned = ItemRotNow(d); }
                p.shown = Vector3.Lerp(p.shown, want, 1f - Mathf.Exp(-dt * follow));
                bool making = d.status == "making";
                float bob = making ? Mathf.Sin(Time.time * 3f) * 0.004f + 0.008f : 0f;
                p.transform.SetPositionAndRotation(p.shown + Vector3.up * bob, turned * (making ? Quaternion.Euler(0, Time.time * 40f, 0) : Quaternion.identity));
                float give = d.id == _mkPieceId ? CraftSquash : 0f;      // the piece the partner is hammering at gives a little under each blow
                p.transform.localScale = new Vector3(1f + 0.07f * give, 1f - 0.12f * give, 1f + 0.07f * give) * (d.sizeM * Mathf.Max(0.001f, Ui.Pop(p.appear)));
                p.Visible = true;
                p.SetLook(Color.white, making ? 0.8f : 0f, making ? Mathf.Lerp(0.05f, p.height, Mathf.Max(d.progress, Mathf.Repeat(Time.time * 0.35f, 1f) * 0.15f)) : 2f);
                // its shadow on the desk, and the ring that fills while it is being made
                p.shadow.enabled = true;
                p.shadow.transform.SetPositionAndRotation(new Vector3(p.shown.x, _deskY + Mathf.Max(0f, under) + 0.0012f, p.shown.z), Quaternion.Euler(90, 0, 0));      // on the desk, or on the top it stands on
                p.shadow.transform.localScale = Vector3.one * d.sizeM * 1.15f;
                if (p.progress.enabled != making) p.progress.enabled = making;
                if (making)
                {
                    Vector3 at = p.shown + Vector3.up * (d.sizeM * p.height + 0.03f);
                    p.progress.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - head, Vector3.up));
                    p.progress.transform.localScale = Vector3.one * 0.03f;
                    p.progress.SetParam(new Vector4(0.78f, 0.20f, Mathf.Clamp01(Mathf.Max(0.04f, d.progress)), 0));
                    p.progress.SetPhase(Time.time * 0.4f);
                }
            }
        }

        // a thing the partner is using may be somewhere else for the moment (the book in its hands, the ball in flight)
        string _heldItem = ""; Vector3 _heldPos; Quaternion _heldRot = Quaternion.identity;
        Vector3 ItemPosNow(ItemDef d) => d.id == _heldItem ? _heldPos : ItemPosY(d);      // (at its own height: on the desk, or on the top it stands on)
        Quaternion ItemRotNow(ItemDef d) => d.id == _heldItem ? _heldRot : ItemRot(d);

        // ------------------------------------------------------------------ what it is doing
        string _doingNow = "", _doingSeen = ""; double _doingAt = -1; bool _selfDoing; float _idleSince, _selfUntil;
        int _actPhase; float _actT, _actYaw; string _actFor = "";
        readonly List<SkillStep> _steps = new List<SkillStep>(); int _stepAt; float _stepT;

        void UpdateDoing(float dt, WorldState w)
        {
            // what the PC says it was asked to do: a skill (a list of acts) or one activity
            string asked = w.doingSkill.Length > 0 ? "skill:" + w.doingSkill : w.doing;
            if (asked != _doingSeen || (asked.Length > 0 && w.doingAt != _doingAt))
            {
                _doingSeen = asked; _doingAt = w.doingAt; _selfDoing = false; _steps.Clear(); _stepAt = 0; _stepT = 0f;
                SkillDef skill = null;
                if (w.doingSkill.Length > 0) foreach (var s in w.skills) if (s.id == w.doingSkill) skill = s;
                if (skill != null && RoomSkillScripted(skill)) SetDoing("");      // a skill that is a script: MrRoomScript carries it out
                else if (skill != null)
                {
                    if (skill.itemId.Length > 0) _steps.Add(new SkillStep { act = "goto", arg = skill.itemId });
                    foreach (var st in skill.steps) if (st.act == "dance" || st.act == "activity" || st.act == "emote" || st.act == "goto" || st.act == "wait") _steps.Add(st);
                    Debug.Log($"[MR] world: skill '{skill.name}': {_steps.Count} act(s) of its {skill.steps.Count} are the headset's");
                    SetDoing(_steps.Count == 0 ? "" : "steps");
                }
                else SetDoing(asked);
                if (asked.Length > 0) _idleSince = Time.time;
            }
            // a skill's acts, one after another
            if (_doingNow == "steps" || (_steps.Count > 0 && _stepAt < _steps.Count))
            {
                var st = _stepAt < _steps.Count ? _steps[_stepAt] : null;
                if (st == null) { if (_doingNow == "steps") SetDoing(""); }
                else
                {
                    _stepT += dt;
                    bool next = false;
                    switch (st.act)
                    {
                        case "emote":
                            if (_stepT <= dt * 1.5f) PlayEmote(st.arg == "wave" ? Emote.Wave : st.arg == "think" ? Emote.Think : st.arg == "startle" ? Emote.Startle : st.arg == "thankful" ? Emote.Thankful : Emote.Happy, st.seconds > 0 ? st.seconds : 2f);
                            next = _stepT > (st.seconds > 0 ? st.seconds : 2f); break;
                        case "wait": next = _stepT > Mathf.Max(0.2f, st.seconds); break;
                        case "goto":
                        {
                            var d = Item(st.arg);
                            if (d == null || !d.inRoom || _ch.owner != Env.Mr) { next = true; break; }
                            if (_stepT <= dt * 1.5f) { Vector3 at = UseSpot(d, -_worldFwd, 0.035f); _motor.MoveTo(new Vector2(at.x, at.z), false, YawTo(_worldFwd)); }
                            next = _stepT > 0.3f && _motor.Arrived || _stepT > 6f; break;
                        }
                        default:      // dance / activity: goes on (for its seconds, or until told to stop)
                            if (_stepT <= dt * 1.5f) { string what = st.act == "dance" ? "dance" : st.arg; if (_doingNow != what) SetDoing(what); }
                            next = st.seconds > 0 && _stepT > st.seconds; break;
                    }
                    if (next) { _stepAt++; _stepT = 0f; if (_stepAt >= _steps.Count && (st.act == "emote" || st.act == "wait" || st.act == "goto")) SetDoing(""); }
                }
            }
            // Something is being made: it is at its workbench, hammering (MrCraft) - by itself, like what it does when left
            // alone, and only while nothing is asked of it. What the user asks for has already taken its place above;
            // when that is over it goes back to the bench.
            bool bench = BenchFree;
            if (bench && _doingNow != "craft") { _selfDoing = false; SetDoing("craft"); }
            else if (!bench && _doingNow == "craft") SetDoing("");
            if (_doingNow == "craft") _idleSince = Time.time;
            // left alone with its things, it does something with them by itself for a while
            bool free =_doingSeen.Length == 0 && _game.Length == 0 && !_tn.active && _ch.owner == Env.Mr && _mode == Mode.Free && TransitionIdle && _ch.talk.Length == 0 && _ch.activity == "idle";
            if (!free) _idleSince = Time.time;
            if (_selfDoing && (!free || Time.time > _selfUntil)) { _selfDoing = false; SetDoing(""); _idleSince = Time.time; }
            else if (!_selfDoing && free && _doingNow.Length == 0 && Time.time - _idleSince > 22f)
            {
                var can = Activities.Possible(w.items);
                can.RemoveAll(a => a.name == "music");
                if (can.Count > 0)
                {
                    var pick = can[UnityEngine.Random.Range(0, Mathf.Min(can.Count, 3))];
                    _selfDoing = true; _selfUntil = Time.time + UnityEngine.Random.Range(16f, 26f);
                    Debug.Log("[MR] world: left alone, it goes to " + pick.line);
                    SetDoing(pick.name);
                }
                else _idleSince = Time.time;
            }
        }

        void SetDoing(string what)
        {
            if (what == _doingNow) return;
            bool was = _doingNow.Length > 0 && _doingNow != "steps";
            _doingNow = what; _actPhase = 0; _actT = 0f; _actFor = ""; _heldItem = "";
            if (was && _ch.owner == Env.Mr)
            {
                // getting up: back on its feet where it is, then home
                _pos.y = _deskY; _rot = Quaternion.Euler(0, _rot.eulerAngles.y, 0);
                _motor.Teleport(new Vector2(_pos.x, _pos.z), _rot.eulerAngles.y);
                if (what.Length == 0 || what == "steps") _motor.MoveTo(new Vector2(_home.x, _home.z), false);
            }
        }

        /// <summary>The partner's pose while it is doing something with its things, or dancing. False: it walks / idles as usual.</summary>
        bool WorldPose(float dt)
        {
            if (StallPose(dt)) return true;      // it is putting a thing up for sale at its stall (MrShow)
            string what = _doingNow;
            if (what.Length == 0 || what == "steps" || !_worldFrame) return false;
            float time = Time.time;
            _actT += dt;
            if (what == "dance")
            {
                if (_actPhase == 0)
                {
                    // it dances on its own spot, in front of its things - not behind the desk it was sitting at
                    Vector3 home = new Vector3(_home.x, _deskY, _home.z);
                    if (_actFor != "dance") { _actFor = "dance"; _actT = 0f; if (Vector3.Distance(_pos, home) > 0.04f) _motor.MoveTo(new Vector2(home.x, home.z), true, YawTo(Input.HeadPos - home)); }
                    if (!((_motor.Arrived && _actT > 0.15f) || _actT > 5f)) return false;
                    _actPhase = 1; _actYaw = YawTo(Input.HeadPos - _pos); _motor.Stop();
                }
                float beats = _music.isPlaying && _songNow != null ? _music.time * _songNow.bpm / 60f : time * 2f;
                PoseLib.Dance(_pose, time, beats);
                _rot = Quaternion.Euler(0, _actYaw + 22f * Mathf.Sin(beats * Mathf.PI * 0.25f), 0);
                _pos.y = _deskY;
                _motor.Teleport(new Vector2(_pos.x, _pos.z), _actYaw);
                if (Mathf.Repeat(beats, 8f) < dt * (_songNow != null ? _songNow.bpm / 60f : 2f)) _sparks.Burst(_pos + Vector3.up * (_H * 1.1f), Input.HeadRot * Vector3.right, Vector3.up, 0.05f, 8);
                return true;
            }
            if (what == "craft") return CraftPose(dt);      // at its workbench, making something (MrCraft)
            // the rest needs things: where it goes, which way it faces there, how high it sits
            var act = Activities.Find(World.items, what == "music" ? "music" : what);
            if (act == null) { SetDoing(""); return false; }
            ItemDef a = Item(act.itemIds[0]), b = act.itemIds.Length > 1 ? Item(act.itemIds[1]) : null;
            if (a == null) { SetDoing(""); return false; }
            ItemDef seat = what == "study" ? b : what == "sit" ? a : what == "read" ? b : null;
            Vector3 spot; float yaw; float rise = 0f;
            Vector3 toUser = Input.HeadPos - ItemPos(a); toUser.y = 0;
            switch (what)
            {
                case "study": spot = ItemPos(seat); yaw = YawTo(ItemPos(a) - ItemPos(seat)); rise = TopOf(seat, true); break;
                case "sit": spot = ItemPos(a); yaw = YawTo(Input.HeadPos - ItemPos(a)); rise = TopOf(a, true); break;
                case "read":
                    if (seat != null) { spot = ItemPos(seat); yaw = YawTo(Input.HeadPos - ItemPos(seat)); rise = TopOf(seat, true); }
                    else { spot = UseSpot(a, toUser, 0.04f); yaw = YawTo(toUser); }      // (a book that lies on the table: it goes to the edge of the table)
                    break;
                case "nap": spot = ItemPos(a); yaw = YawTo(ItemRot(a) * Vector3.right); rise = TopOf(a, true); break;      // along the bed, whichever way it stands
                case "play": spot = string.IsNullOrEmpty(a.onId) ? ItemPos(a) + toUser.normalized * 0.01f - _worldRight * (a.sizeM * 0.5f + 0.03f) : UseSpot(a, -_worldRight, 0.03f); yaw = YawTo(_worldRight); break;
                default: spot = UseSpot(a, toUser, 0.035f); yaw = YawTo(-toUser); break;      // at its music box
            }
            // (what it sits or lies on may itself stand on another thing: then it is that much higher up, and it walks to
            // the edge of the thing at the bottom, not through it)
            ItemDef onWhat = seat ?? (what == "nap" ? a : null);
            float lift = onWhat != null ? onWhat.y : 0f;
            if (_actPhase == 0)
            {
                // on the way there (the usual walk)
                Vector3 walkTo = onWhat != null && !string.IsNullOrEmpty(onWhat.onId) ? UseSpot(onWhat, Input.HeadPos - ItemPos(onWhat), 0.03f) : spot;
                if (_actFor != what) { _actFor = what; _motor.MoveTo(new Vector2(walkTo.x, walkTo.z), Vector3.Distance(_pos, walkTo) > 0.25f, yaw); _actT = 0f; }
                if ((_motor.Arrived && _actT > 0.2f) || _actT > 7f) { _actPhase = 1; _actT = 0f; _sparks.Burst(spot + Vector3.up * (lift + rise + 0.02f), _worldRight, Vector3.up, 0.04f, 7); }
                return false;
            }
            float sitHips = rise / Mathf.Max(0.01f, _H) - Skeleton.HipsHeight + 0.02f;      // hips at seat height (the pose bends the legs)
            _pos = new Vector3(spot.x, _deskY + lift, spot.z); _rot = Quaternion.Euler(0, yaw, 0);
            switch (what)
            {
                case "study": PoseLib.Study(_pose, time); _pose.HipsOffset.Y = sitHips; break;
                case "sit": PoseLib.SitOn(_pose, time); _pose.HipsOffset.Y = sitHips; LookAtUser(_pose); break;
                case "read":
                    PoseLib.ReadBook(_pose, time, seat != null);
                    if (seat != null) _pose.HipsOffset.Y = sitHips;
                    // the book is in its hands
                    _heldItem = a.id; _heldRot = Quaternion.Euler(-62f, yaw + 180f, 0);
                    _heldPos = _pos + _rot * new Vector3(0, (seat != null ? rise : 0.012f) + _H * 0.20f, _H * 0.36f);
                    break;
                case "nap":
                    PoseLib.Sleep(_pose, time);
                    _rot = Quaternion.Euler(-90f, yaw, 0);                                  // on its back, head towards the pillow
                    _pos = new Vector3(spot.x, _deskY + lift + rise + _H * 0.16f, spot.z) + ItemRot(a) * Vector3.right * (_H * 0.48f);      // its feet: the body lies from here towards the pillow, in the middle of the bed
                    if (Mathf.Repeat(_actT, 2.6f) < dt) _sparks.Burst(_pos + Vector3.up * 0.05f - ItemRot(a) * Vector3.right * (_H * 0.8f), _worldRight, Vector3.up, 0.02f, 2);
                    break;
                case "play":
                {
                    // a kick every two and a half seconds: the ball rolls off along the desk, slows, and rolls back
                    float u = Mathf.Repeat(_actT, 2.5f);
                    if (u < 0.6f) PoseLib.Kick(_pose, u / 0.6f, time); else PoseLib.Idle(_pose, time);
                    float roll = u < 0.30f ? 0f : Mathf.Sin(Mathf.Clamp01((u - 0.30f) / 2.0f) * Mathf.PI) * 0.16f;
                    _heldItem = a.id; _heldPos = ItemPosY(a) + _worldRight * roll; _heldRot = ItemRot(a) * Quaternion.Euler(0, 0, -roll * 900f);
                    if (u >= 0.30f && u - dt < 0.30f) _sparks.Burst(ItemPosY(a), _worldRight, Vector3.up, 0.03f, 6);
                    break;
                }
                default:
                    if (_actT < 1.6f) PoseLib.Work(_pose, time); else { PoseLib.Happy(_pose, time * 0.5f); }
                    break;
            }
            _motor.Teleport(new Vector2(spot.x, spot.z), yaw);
            return true;
        }

        // ------------------------------------------------------------------ the tune
        SongDef _songNow; string _songKey = ""; float[] _songPcm; float[] _songBeat; volatile bool _songReady; float _songSeconds, _musicVol;
        AudioClip _songClip; float _songPeak;

        void UpdateMusic(float dt, WorldState w)
        {
            // a game pack brings its own tune for as long as it is played
            bool packTune = _game.Length > 0 && _pack != null && _pack.song != null && _pack.song.melody != null && _pack.song.melody.Length > 0;
            var s = packTune ? _pack.song : w.song;
            string key = s == null ? "" : packTune ? "pack:" + _pack.id + "@" + _gameStartedAt.ToString("0.00") : s.id + "@" + w.songAt.ToString("0.00");
            if (key != _songKey)
            {
                _songKey = key; _songReady = false; _songNow = s;
                if (s != null)
                {
                    // the notes are turned into sound off the main thread (a few hundred thousand samples)
                    var song = s;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        // (brought up to nearly full scale: as rendered - right for a PC's speakers - it could not be heard on the headset)
                        try { var pcm = SongSynth.Render(song, out float secs); _songPeak = SongSynth.Normalize(pcm, 0.95f); var beat = SongSynth.Beat(song); _songPcm = pcm; _songBeat = beat; _songSeconds = secs; _songReady = _songNow == song; }
                        catch (Exception e) { Debug.Log("[MR] world: the tune could not be made: " + e.Message); }
                    });
                }
            }
            if (_songReady)
            {
                _songReady = false;
                if (_songClip != null) Destroy(_songClip);
                _songClip = AudioClip.Create("Tune", _songPcm.Length, 1, SongSynth.SampleRate, false);      // sound data, not a scene object
                _songClip.SetData(_songPcm, 0);
                _music.Stop(); _music.clip = _songClip; _music.loop = true; _music.volume = 0f; _music.Play();
                Debug.Log($"[MR] world: tune '{_songNow.name}' {_songNow.bpm} bpm, {_songSeconds:0.0} s loop, peak {_songPeak:0.00} as rendered, played at 0.95");
            }
            bool on = _songNow != null && _music.clip != null;
            _musicVol = Mathf.MoveTowards(_musicVol, on ? (_settings.quietMode ? 0.27f : 0.9f) : 0f, dt * 0.8f);      // (Quiet: the tune at 30 %)
            _music.volume = _musicVol;
            if (!on && _musicVol <= 0f && _music.isPlaying) _music.Stop();
        }

        /// <summary>How strong the tune is at this moment (0..1), for lights that pulse with it.</summary>
        float BeatNow()
        {
            if (_songNow == null || _songBeat == null || _songBeat.Length == 0 || !_music.isPlaying) return 0f;
            float step = _music.time / Mathf.Max(0.01f, SongSynth.StepSeconds(_songNow));
            int i = (int)step % _songBeat.Length;
            return _songBeat[i] * (1f - 0.6f * (step - Mathf.Floor(step)));
        }

        // ------------------------------------------------------------------ the surroundings
        AmbienceDef _ambNow; float _ambAmount; Vector3 _workDir = Vector3.forward; Vector4 _workSize = new Vector4(55, 30, 75, 9);
        // Where the place that is on (the mountain top, the seaside) is ANCHORED IN THE WORLD: the direction the work area
        // lay in, and its half width, at the moment the place came on. Fixed from then on - the work area's window is
        // worked out from the head's position every frame, and a place hung on it swung round the user whenever the
        // head turned ("the sea suddenly moves very fast when I turn my head"). And where its GROUND lies (the beach and
        // the sea, the summit underfoot): a sheet in the room at about the height of the floor, fixed at the same moment
        // (xyz: the point of it under the user then, w: how far below the eyes) - before, it hung under the head and
        // slid along with every movement of it.
        Vector4 _placeDir = new Vector4(0, 0, 1, 55), _placeAt = new Vector4(0, 0, 0, 0); string _placeId = "", _placePattern = "";

        static Color Hex(string hex, Color fallback) => ColorUtility.TryParseHtmlString(hex ?? "", out Color c) ? c : fallback;
        static int PatternOf(string p) => p == "spots" ? 1 : p == "stars" ? 2 : p == "rain" ? 3 : p == "aurora" ? 4 : p == "snow" ? 5 : p == "petals" ? 6 : p == "bubbles" ? 7 : p == "peak" ? 8 : p == "coast" ? 9 : 0;
        /// <summary>
        /// What is drawn for these surroundings. (For a few hours of 2026-11-17 the three places were to be photographs -
        /// pattern "photo" with the name of a picture; the user did not want that. A PC app that still says so gets the
        /// painted place: the mountain top, or the seaside in the colours it sends.)
        /// </summary>
        static int PatternOf(AmbienceDef a) => a == null ? 0 : a.pattern != "photo" ? PatternOf(a.pattern) : (a.id ?? "").Contains("peak") ? 8 : 9;

        void UpdateAmbience(float dt, WorldState w)
        {
            // (a game pack brings its own surroundings for as long as it is played)
            var amb = _game.Length > 0 && _pack != null && _pack.ambience != null && PatternOf(_pack.ambience) != 0 ? _pack.ambience : w.ambience;
            if (amb != null) _ambNow = amb;
            _ambAmount = Mathf.MoveTowards(_ambAmount, amb != null ? 1f : 0f, dt / 1.4f);
            bool on = _ambAmount > 0.002f && _ambNow != null;
            // where the screens are from the user, at this moment
            Vector3 head = Input.HeadPos, sum = Vector3.zero;
            foreach (var mv in _mons.Values) { Vector3 d = mv.Origin - head; d.y = 0; if (d.sqrMagnitude > 1e-4f) sum += d.normalized; }
            bool coming = on && !_ambience.Visible;
            if (coming && sum.sqrMagnitude > 1e-4f) _workDir = sum.normalized;      // (coming on: the window is over the screens at once, it does not swing there)
            _ambience.Visible = on;
            if (!on) return;
            float placeWide = _workSize.x;
            // the work area: the screens and the desk in front of them - that window is left as it is
            if (_mons.Count > 0)
            {
                if (sum.sqrMagnitude > 1e-4f) _workDir = Vector3.Slerp(_workDir, sum.normalized, 1f - Mathf.Exp(-dt * 2f));
                float wide = 30f, top = 12f; Vector3 right = Vector3.Cross(Vector3.up, _workDir);
                foreach (var mv in _mons.Values)
                    for (int c = 0; c < 4; c++)
                    {
                        Vector3 corner = mv.ToWorld(c % 2, c / 2) - head;
                        wide = Mathf.Max(wide, Mathf.Abs(Mathf.Atan2(Vector3.Dot(corner, right), Vector3.Dot(corner, _workDir)) * Mathf.Rad2Deg));
                        top = Mathf.Max(top, Mathf.Asin(Mathf.Clamp(corner.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
                    }
                placeWide = Mathf.Clamp(wide + 9f, 35f, 85f);
                _workSize = Vector4.Lerp(_workSize, new Vector4(placeWide, Mathf.Clamp(top + 7f, 12f, 55f), 78f, 9f), 1f - Mathf.Exp(-dt * 2f));
            }
            else if (_worldFrame) _workDir = _worldFwd;
            // the place is anchored when it comes on (or when another place takes over), never again while it is on
            string placeId = _ambNow.id ?? "", placePattern = _ambNow.pattern ?? "";
            if (coming || placeId != _placeId || placePattern != _placePattern)
            {
                _placeId = placeId; _placePattern = placePattern;
                _placeDir = new Vector4(_workDir.x, 0f, _workDir.z, placeWide);
                float below = Mathf.Clamp(head.y - (_deskY - 0.72f), 0.9f, 1.7f);      // the eyes above the floor (the desk's top is about 72 cm above it)
                _placeAt = new Vector4(head.x, head.y - below, head.z, below);
            }
            _ambience.transform.position = head;
            _ambience.Set(Hex(_ambNow.c0, new Color(0.08f, 0.04f, 0.18f)), Hex(_ambNow.c1, Color.magenta), Hex(_ambNow.c2, Color.cyan), PatternOf(_ambNow),
                Mathf.Clamp01(_ambNow.dim), Mathf.Clamp(_ambNow.speed, 0.1f, 4f), Mathf.Clamp(_ambNow.density, 0.2f, 3f), _ambNow.beat ? BeatNow() : 0f, _ambAmount, _workDir, _workSize, _placeDir, _placeAt);
        }

        // ------------------------------------------------------------------ the first seconds
        float _introT, _introGone; bool _introDone;

        void UpdateIntro(float dt, bool hostOk)
        {
            if (_introDone) return;
            _introT += dt;
            if ((_introT >= 2.35f && hostOk) || _introT >= 4.8f || _introGone > 0f) _introGone += dt;
            float show = Mathf.SmoothStep(0f, 1f, _introT / 0.8f) * (1f - Mathf.Clamp01(_introGone / 0.7f));
            if (_introGone > 0.75f || _link.NeedsPairing || Input.Simulated && _introT > 0.1f && !_introSim)
            {
                _introDone = true;
                _introLogo.enabled = false; _introRing.enabled = false; _introBg.enabled = false; _introLine.text = "";
                IntroFxHide();
                return;
            }
            Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward;
            if (_introT < 0.3f || _introAt == Vector3.zero) _introAt = head + fwd * 0.62f;
            _introAt = Vector3.Lerp(_introAt, head + fwd * 0.62f, 1f - Mathf.Exp(-dt * 1.5f));          // it drifts after the gaze, lazily
            Quaternion face = Input.HeadRot;
            float pop = Ui.Pop(Mathf.Clamp01((_introT - 0.25f) / 0.7f)) * (1f - Mathf.Clamp01(_introGone / 0.7f));
            IntroFx(_introAt, face, show);      // the glow, the stars and the circling badges (MrIntro)
            _introLogo.enabled = true; _introRing.enabled = true;
            _introLogo.transform.SetPositionAndRotation(_introAt + Vector3.up * (0.012f * Mathf.Sin(_introT * 1.8f)), face);
            _introLogo.transform.localScale = new Vector3(0.43f, 0.43f * _introLogo.texture.height / _introLogo.texture.width, 1f) * Mathf.Max(0.001f, pop);      // preserve the illustrated wordmark’s proportions
            _introLogo.SetColor(new Color(1, 1, 1, show)); _introLogo.SetLook(0f, Mathf.Repeat(_introT * 0.5f, 1.6f) < 1f ? Mathf.Repeat(_introT * 0.5f, 1.6f) : -1f);
            _introRing.transform.SetPositionAndRotation(_introAt + face * new Vector3(0, 0, 0.01f), face * Quaternion.Euler(0, 0, _introT * 50f));
            _introRing.transform.localScale = Vector3.one * (0.34f * pop);
            _introRing.SetColors(new Color(1f, 0.84f, 0.42f, 0.9f * show), new Color(1f, 0.96f, 0.86f, 0.9f * show));
            string line = hostOk && _mons.Count == 0 && !LocalMode ? "Finding your screens…" : "";
            Place(_introLine, _introAt - Vector3.up * 0.127f, show > 0.4f ? line : "");
            _introBg.enabled = _introLine.text.Length > 0;
            _introBg.transform.SetPositionAndRotation(_introLine.transform.position + _introLine.transform.forward * 0.002f, _introLine.transform.rotation);
            Ui.SizePanel(_introBg, _introLine.Width + 0.05f, _introLine.Height + 0.026f, 0.019f, 0.0018f);
            if (Mathf.Repeat(_introT, 0.9f) < dt) _sparks.Burst(_introAt + face * new Vector3(Mathf.Sin(_introT * 3f) * 0.14f, Mathf.Cos(_introT * 2.3f) * 0.05f, 0), face * Vector3.right, Vector3.up, 0.03f, 4);
        }
        Vector3 _introAt; bool _introSim;
    }
}
