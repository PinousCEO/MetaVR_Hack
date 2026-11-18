using System;
using System.Collections.Generic;
using System.Globalization;
using MoonSharp.Interpreter;

namespace LivingPartner.Shared
{
    // ROOM SCRIPTS: what a THING on the desk DOES, as a small Lua program (ItemDef.script) written by the host's language
    // model when the thing was made - "a baseball bat: when Bambi holds it, a swing is added".
    //   RoomScript          one thing's script, for as long as the thing is in the room. Pure C#, no engine types: the
    //                       host runs it to CHECK a script it has just written (RoomScriptCheck), the headset runs it to
    //                       make it real (MR/MrRoomScript.cs).
    //   IRoomHost           the little the script needs from the room it is in: where the partner and the hands are, and
    //                       the things that happen ONCE (a line said, a sound, sparks, the thing put on).
    //   everything else is STATE that whoever draws the room reads each frame: Pet (what the partner is asked to do),
    //   Me (the thing itself), Objects (its little objects), Labels.
    // The room as a script sees it: metres; x = to the user's right, z = away from the user, y = above the desk; the
    // origin is the partner's home spot (the frame of ItemDef.x / z / y). The partner is about 0.10 m tall.
    // The Lua side (sandbox, budgets, errors) is LuaSandbox, and the motions a script defines are LuaMotion - shared with
    // the scripted games (GlassScript).

    /// <summary>A fingertip of the user, in the room's frame.</summary>
    public struct RoomHand { public float x, y, z; public bool pinch; }

    /// <summary>The room a script lives in: what it may ask, and what happens once. (What goes on is read from RoomScript's state.)</summary>
    public interface IRoomHost
    {
        /// <summary>Where the partner stands now (room frame, on the desk).</summary>
        void PetAt(out float x, out float z);
        /// <summary>The fingertips that are seen (the index tips of both hands at most): fills the array, returns how many.</summary>
        int Hands(RoomHand[] into);
        /// <summary>The user's head.</summary>
        void UserHead(out float x, out float y, out float z);
        /// <summary>The partner says a line (a caption over its head) for that long.</summary>
        void Say(string text, float seconds);
        /// <summary>One of SfxSynth's sounds, heard at that place.</summary>
        void Sound(string name, float x, float y, float z);
        /// <summary>A burst of n sparks of that colour ("#RRGGBB") at that place.</summary>
        void Sparks(float x, float y, float z, int n, string color);
        /// <summary>The partner has put the thing on there (handR | handL | head | back), or taken it off ("").</summary>
        void Wear(string slot);
        /// <summary>The things that are out in the room (the one the script belongs to as well): adds them to the list.</summary>
        void Things(List<RoomThingInfo> into);
        /// <summary>The surroundings: the id or the name of a place the user owns or one that comes with the partner ("" = the room as it is). A request to whoever keeps the world (world command ambience).</summary>
        void Ambience(string what);
        /// <summary>The tune: the id or the name of a record ("" = silence). A request as well (world command music).</summary>
        void Music(string what);
    }

    /// <summary>A thing in the room, as a script is told about it.</summary>
    public sealed class RoomThingInfo
    {
        public string id = "", name = "", kind = "";
        public string[] uses = new string[0];
        public float x, y, z;               // where it stands (room frame)
        public float size;                  // its longest side, metres
        public float seat, top;             // how high its seat / its top is above its foot, metres
    }

    /// <summary>One of a thing's little objects (and the thing itself, as far as it can be put and thrown).</summary>
    public sealed class RoomObject
    {
        public bool on;
        public string shape = "ball";       // ball | box | disc | star | ring
        public float size = 0.02f;          // metres across
        public string color = "#FFFFFF";
        public float x, y, z;               // its middle (the thing itself: the point it stands on)
        public float vx, vy, vz;
        public bool flying;                 // thrown and not yet at rest (an object that was only put somewhere stays there, also in the air)
        public bool thrown;                 // it has been thrown at least once
        public int bounces;                 // times it has met the desk since it was last thrown
        public float roll;                  // metres it has travelled along the desk (whoever draws it turns it by that)
        public float px, py, pz;            // where it was a step ago
        public float madeAt;
    }

    public sealed class RoomLabel { public bool on; public string text = ""; public float x, y, z, until, since; }

    public sealed class RoomOffer { public string name = "", label = ""; }

    /// <summary>What the script asks of the partner at this moment. Not Active: the partner is its own.</summary>
    public sealed class RoomPet
    {
        public bool active;                 // the script is steering the partner
        public bool walk; public float gx, gz, speed = RoomScript.WalkSpeed;       // go there (metres a second)
        public int face;                    // 0 = as it likes, 1 = towards (fx, fz), 2 = towards the user
        public float fx, fz;
        public string pose = "";            // a built-in pose (RoomScriptApi.Poses), "" = none
        public float poseSince, poseUntil;  // script time
        public string on = "";              // "" = where it stands | sit = seated on a thing (at its seat) | lie = lying on top of it
        public string onId = "";            // sit / lie: the id of that thing ("" = the thing the script belongs to)
        public string motion = "";          // a motion of the script's own (LuaMotion), "" = none; it wins over pose
        public float motionT, motionSeconds = 1f; public bool motionLoop;
        /// <summary>How far the motion that is playing has got, 0..1.</summary>
        public float MotionU => motion.Length == 0 ? 0f : motionLoop ? M.Frac(motionT / Math.Max(0.05f, motionSeconds)) : M.Clamp01(motionT / Math.Max(0.05f, motionSeconds));
    }

    /// <summary>The thing itself, as the script has it.</summary>
    public sealed class RoomThing
    {
        public readonly RoomObject body = new RoomObject { on = true, shape = "thing" };   // x, y, z: where it stands (y = its foot above the desk)
        public float yaw;                   // degrees
        public float spin;                  // degrees a second (0 = still)
        public float scale = 1f;            // 0.5 .. 2, times its own size
        public string glow = "";            // "#RRGGBB", "" = none
        public float glowUntil;             // script time, 0 = until it is turned off
        public float bounceAt = -99f;       // script time of the last little hop
        public bool moved;                  // the script has put it somewhere else than where the host has it
    }

    public sealed class RoomScript
    {
        // ---- the rules ----
        public const int MaxObjects = 24, MaxTimers = 8, MaxLabels = 4, MaxOffers = 4;
        public const float Box = 1.2f;                                   // everything stays within this of the home spot
        public const float PetMinX = -0.40f, PetMaxX = 0.40f, PetMinZ = -0.22f, PetMaxZ = 0.25f;   // the desk area the partner may be sent to
        public const float PetHeight = 0.10f, HandHeight = 0.04f;
        public const float WalkSpeed = 0.12f;                            // metres a second: what its short legs manage (the headset's motor: 0.08 walking, 0.15 running)
        public const float Gravity = 1.5f;                               // m/s^2: a toy world - things fall slowly enough to be seen
        public const float Bounce = 0.55f, BounceStop = 0.10f, RollFriction = 0.25f, SlideFriction = 1.4f, MaxSpeed = 3f;
        public const float IdleSeconds = 10f, WalkTimeout = 10f, PetGrace = 3f;
        public const int MaxSoundsPerStep = 4, MaxSparksPerStep = 4;
        public static readonly string[] Slots = { "handR", "handL", "head", "back" };
        public static readonly string[] Shapes = { "ball", "box", "disc", "star", "ring" };
        public static readonly string[] Hooks = { "on_make", "on_use", "on_stop", "on_wear", "on_unwear", "on_act", "update" };

        // ---- what is on show ----
        public readonly RoomPet Pet = new RoomPet();
        public readonly RoomThing Me = new RoomThing();
        public readonly RoomObject[] Objects = new RoomObject[MaxObjects];
        public readonly RoomLabel[] Labels = new RoomLabel[MaxLabels];
        public readonly List<RoomOffer> Offers = new List<RoomOffer>();
        /// <summary>Where it can be worn ("" = it cannot).</summary>
        public string WearSlot { get; private set; } = "";
        public bool Wearable => WearSlot.Length > 0;
        /// <summary>Where the partner wears it now ("" = it does not).</summary>
        public string WornOn { get; private set; } = "";
        /// <summary>The script is doing something: Tick has work to do (false: the thing just stands there, and costs nothing).</summary>
        public bool Active { get; private set; }
        /// <summary>Seconds this script has lived (advanced by Tick).</summary>
        public float Time { get; private set; }
        /// <summary>Why the script was switched off ("" = it is fine): the message, with the line.</summary>
        public string Error { get; private set; } = "";
        public int ErrorLine { get; private set; }
        public bool Ok => Error.Length == 0;
        /// <summary>What the runtime had to refuse or cut down (a place too far away, a 25th object ...), each said once. For the checker.</summary>
        public List<string> Notes => _lua.Notes;
        /// <summary>Every call the script makes that changes something, as (function, what) - for the checker and for logs. May be null.</summary>
        public Action<string, string> Trace;
        public readonly string ItemId, ItemName;
        public readonly float ItemSize;

        readonly IRoomHost _host;
        readonly LuaSandbox _lua;
        readonly LuaMotion _motions;
        readonly RoomHand[] _hands = new RoomHand[4];
        readonly Dictionary<string, DynValue> _offerFn = new Dictionary<string, DynValue>();

        sealed class Timer { public bool on; public int id; public float at, every; public DynValue fn; }
        readonly Timer[] _timers = new Timer[MaxTimers];
        int _timerIds;
        DynValue _onArrive, _onMotionEnd;
        float _walkSince, _faceUntil, _idle;
        bool _updateOn, _wrote, _loaded;
        float _petHold;
        int _soundsNow, _sparksNow;

        /// <summary>
        /// Loads the script of a thing. Nothing here throws: a script that cannot be read or fails leaves Error set, and
        /// the thing is an ordinary thing. seed: what rnd() starts from (0 = from the thing's id, the same every time).
        /// </summary>
        public RoomScript(string lua, ItemDef item, IRoomHost host, int seed = 0)
        {
            item = item ?? new ItemDef();
            _host = host ?? new NoRoom();
            ItemId = item.id ?? ""; ItemName = item.name ?? ""; ItemSize = M.Clamp(item.sizeM, 0.02f, 0.4f);
            for (int i = 0; i < Objects.Length; i++) Objects[i] = new RoomObject();
            for (int i = 0; i < Labels.Length; i++) Labels[i] = new RoomLabel();
            for (int i = 0; i < _timers.Length; i++) _timers[i] = new Timer();
            Me.body.size = ItemSize; Me.body.x = Fin(item.x); Me.body.y = Math.Max(0f, Fin(item.y)); Me.body.z = Fin(item.z); Me.yaw = Fin(item.yaw);
            WornOn = Array.IndexOf(Slots, item.wornOn ?? "") >= 0 ? item.wornOn : "";
            if (seed == 0) { unchecked { int h = 17; foreach (char c in ItemId) h = h * 31 + c; seed = h == 0 ? 1 : h; } }
            _lua = new LuaSandbox(seed);
            _motions = new LuaMotion(_lua);
            try
            {
                RoomScriptApi.Register(this, _lua);
                if (!_lua.Load(lua ?? "")) { Fail(); return; }
                _loaded = true;
            }
            catch (Exception e) { Fail("the script could not be loaded: " + e.Message, 0); }
        }

        /// <summary>The motions the script defines (LuaMotion): their names.</summary>
        public List<string> Motions { get { var l = new List<string>(); for (int i = 0; i < _motions.Count; i++) l.Add(_motions.NameAt(i)); return l; } }
        public float MotionSeconds(string name) => _motions.Seconds(name);
        public bool Has(string hook) => Ok && _loaded && _lua.Has(hook);

        // ------------------------------------------------------------------ what the room tells the script
        /// <summary>The thing has appeared in the room.</summary>
        public void Make() { Hook("on_make"); }

        /// <summary>The user pressed the thing, or asked to use it. False: the script has nothing for that.</summary>
        public bool Use() => Hook("on_use");

        /// <summary>The partner puts the thing on (slot "" = where the script says). False: it is not a thing to wear.</summary>
        public bool Wear(string slot = "")
        {
            if (!Ok || !Wearable) return false;
            slot = Array.IndexOf(Slots, slot ?? "") >= 0 ? slot : WearSlot;
            SetWornBy(slot, "wear");
            Hook("on_wear");
            return true;
        }

        public bool Unwear()
        {
            if (!Ok || WornOn.Length == 0) return false;
            SetWornBy("", "unwear");
            Hook("on_unwear");
            return true;
        }

        /// <summary>The user asked for one of the acts the thing offers. False: it has no such act.</summary>
        public bool Act(string name)
        {
            name = (name ?? "").Trim();
            if (!Ok || name.Length == 0) return false;
            if (_offerFn.TryGetValue(name, out var fn) && fn != null) { Begin(); Call(fn); return true; }
            bool offered = false; foreach (var o in Offers) if (o.name == name) offered = true;
            if (!_lua.Has("on_act")) return false;
            if (!offered && Offers.Count > 0) return false;
            Begin(); Call(_lua.Get("on_act"), DynValue.NewString(name));
            return true;
        }

        /// <summary>What the host says about where the thing is worn (a snapshot): taken over without a word to the script.</summary>
        public void SetWorn(string slot) { WornOn = Array.IndexOf(Slots, slot ?? "") >= 0 ? slot : ""; }

        /// <summary>Where the host has the thing (it was carried somewhere): taken over unless the script itself is moving it.</summary>
        public void SetPlace(float x, float y, float z, float yaw)
        {
            if (Me.body.flying || Me.moved) return;
            Me.body.x = Fin(x); Me.body.y = Math.Max(0f, Fin(y)); Me.body.z = Fin(z); Me.yaw = Fin(yaw);
        }

        /// <summary>The place the script put the thing has been taken over by whoever keeps the things (ItemDef.x / z): from here on SetPlace counts again.</summary>
        public void PlaceTaken() { Me.moved = false; }

        bool Hook(string name)
        {
            if (!Ok || !_loaded || !_lua.Has(name)) return false;
            Begin();
            Call(_lua.Get(name));
            return true;
        }

        void Begin()
        {
            Active = true; _idle = 0f;
            _updateOn = _lua.Has("update");
        }

        // ------------------------------------------------------------------ every frame
        /// <summary>Advances the script by dt seconds: its timers, update(dt), its objects, what the partner was asked. Never throws.</summary>
        public void Tick(float dt)
        {
            if (!Ok) return;
            if (!(dt > 0f)) dt = 0f; if (dt > 0.1f) dt = 0.1f;
            Time += dt;
            _soundsNow = 0; _sparksNow = 0;
            if (Me.glow.Length > 0 && Me.glowUntil > 0f && Time >= Me.glowUntil) { Me.glow = ""; Me.glowUntil = 0f; }
            if (Math.Abs(Me.spin) > 0.01f) Me.yaw = Wrap(Me.yaw + Me.spin * dt);
            foreach (var l in Labels) if (l.on && Time >= l.until) l.on = false;
            if (!Active) return;
            _wrote = false;

            // timers (an "every" fires once a step at most)
            for (int i = 0; i < _timers.Length && Ok; i++)
            {
                var t = _timers[i];
                if (!t.on || Time < t.at) continue;
                var fn = t.fn;
                if (t.every > 0f) { t.at += t.every; if (t.at < Time) t.at = Time + t.every; } else { t.on = false; t.fn = null; }
                Call(fn);
            }
            if (Ok && _updateOn && _lua.Has("update")) Call(_lua.Get("update"), DynValue.NewNumber(dt));
            if (!Ok) return;

            // the objects: thrown ones fall, bounce on the desk, roll out
            for (float left = dt; left > 1e-5f; left -= 1f / 90f)
            {
                float h = Math.Min(left, 1f / 90f);
                bool first = left == dt;
                if (Me.body.flying) Step(Me.body, h, 0f, true, first);
                else if (first) Was(Me.body);
                foreach (var o in Objects) if (o.on) { if (o.flying) Step(o, h, Rest(o), o.shape == "ball" || o.shape == "ring" || o.shape == "disc", first); else if (first) Was(o); }
            }
            if (WornOn.Length > 0) { PetAt(out float wx, out float wz); Me.body.x = wx; Me.body.z = wz; Me.body.y = HandHeight; Me.body.flying = false; }

            // the partner: has it arrived, is the motion over, is the pose over
            if (Pet.walk)
            {
                PetAt(out float px, out float pz);
                bool there = (px - Pet.gx) * (px - Pet.gx) + (pz - Pet.gz) * (pz - Pet.gz) < 0.02f * 0.02f;
                if (there || Time - _walkSince > WalkTimeout)
                {
                    if (!there) Note("the partner did not get to where pet_goto sent it within " + WalkTimeout.ToString("0", CultureInfo.InvariantCulture) + " s");
                    Pet.walk = false;
                    var fn = _onArrive; _onArrive = null;
                    if (fn != null) Call(fn);
                }
            }
            if (Ok && Pet.motion.Length > 0)
            {
                Pet.motionT += dt;
                if (!Pet.motionLoop && Pet.motionT >= Pet.motionSeconds)
                {
                    Pet.motion = "";
                    var fn = _onMotionEnd; _onMotionEnd = null;
                    if (fn != null) Call(fn);
                }
            }
            if (Pet.pose.Length > 0 && Time >= Pet.poseUntil) { Pet.pose = ""; Pet.on = ""; Pet.onId = ""; }
            if (!Ok) return;

            // is anything still going on? A script whose update() only waits is let go after ten seconds
            bool busy = Me.body.flying || Pet.walk || Pet.pose.Length > 0 || (Pet.motion.Length > 0 && !Pet.motionLoop) || Time < _faceUntil;
            if (!busy) foreach (var t in _timers) if (t.on) { busy = true; break; }
            if (!busy) foreach (var o in Objects) if (o.on && o.flying) { busy = true; break; }
            if (busy || _wrote) _idle = 0f; else _idle += dt;
            if (_updateOn && _idle >= IdleSeconds) { _updateOn = false; IdledOut = true; }
            // The partner is the script's only while the script asks something of it, and for a moment after (so that it
            // stays put between one kick and the next). A script that goes on waiting in update() does not keep it.
            if (Pet.walk || Pet.pose.Length > 0 || Pet.motion.Length > 0) _petHold = Time + PetGrace;
            else if (Pet.active && Time > _petHold) { Pet.active = false; Pet.face = 0; }
            // (a motion that goes round and round is let go like an update that only waits: after ten seconds of nothing else)
            bool looping = Pet.motion.Length > 0 && Pet.motionLoop && _idle < IdleSeconds;
            if (!_updateOn && !busy && !looping) Release();
        }

        /// <summary>The script was let go because its update() had done nothing for IdleSeconds (it never said done()).</summary>
        public bool IdledOut { get; private set; }

        /// <summary>The script is waiting in update() (true), or only letting what it started run out (false).</summary>
        public bool Updating => Active && _updateOn;

        void Release()
        {
            Active = false; _updateOn = false;
            Pet.active = false; Pet.walk = false; Pet.face = 0; Pet.pose = ""; Pet.motion = ""; Pet.on = ""; Pet.onId = "";
            _onArrive = null; _onMotionEnd = null;
        }

        /// <summary>
        /// The room takes the partner back, or the user says stop: on_stop() is called (if the script has one), then
        /// every timer ends and the partner is its own. What the thing shows of itself (its glow, its objects) stays.
        /// </summary>
        public void Stop()
        {
            if (!Ok) return;
            if (Active && _loaded && _lua.Has("on_stop")) { Say("stop", ""); Call(_lua.Get("on_stop")); }      // (a thing that is doing nothing is not told to stop)
            if (!Ok) return;
            foreach (var t in _timers) { t.on = false; t.fn = null; }
            Release();
        }

        /// <summary>Every global function the script finds (for the test that holds the Doc and the runtime together).</summary>
        public List<string> FunctionNames() => _lua.FunctionNames();

        /// <summary>A timer started with every() is running.</summary>
        public bool TimersRepeat { get { foreach (var t in _timers) if (t.on && t.every > 0f) return true; return false; } }

        /// <summary>The most instructions one call into the script has used.</summary>
        public int PeakInstructions => _lua.PeakInstructions;

        /// <summary>The pose of the motion that is playing, if one is: true when p was filled in.</summary>
        public bool MotionPose(Pose p)
        {
            if (!Ok || Pet.motion.Length == 0) return false;
            if (_motions.Evaluate(Pet.motion, Pet.MotionU, Time, p)) return true;
            if (_lua.Broken) Fail();
            return false;
        }

        /// <summary>The pose of a motion of this script at u (0..1), whether it is playing or not. For the checker and for tests.</summary>
        public bool MotionPose(string name, float u, Pose p) => Ok && _motions.Evaluate(name, u, p);

        /// <summary>Stops everything and forgets the script (the thing has left the room).</summary>
        public void Dispose()
        {
            Release();
            foreach (var o in Objects) o.on = false;
            foreach (var l in Labels) l.on = false;
            foreach (var t in _timers) { t.on = false; t.fn = null; }
            Me.glow = ""; Me.spin = 0f;
            if (Error.Length == 0) Error = "disposed";
        }

        // ------------------------------------------------------------------ the objects
        static float Rest(RoomObject o) => o.shape == "disc" || o.shape == "ring" ? o.size * 0.12f : o.size * 0.5f;
        static void Was(RoomObject o) { o.px = o.x; o.py = o.y; o.pz = o.z; }

        void Step(RoomObject o, float h, float rest, bool round, bool first)
        {
            if (first) Was(o);
            o.vy -= Gravity * h;
            o.x += o.vx * h; o.y += o.vy * h; o.z += o.vz * h;
            if (o.y <= rest)
            {
                o.y = rest;
                if (o.vy < -BounceStop)
                {
                    float speed = -o.vy;
                    o.vy = speed * Bounce; o.vx *= 0.85f; o.vz *= 0.85f; o.bounces++;
                    if (o.bounces <= 3 && speed > 0.15f) Emit("bounce", o.x, o.y, o.z);
                }
                else
                {
                    o.vy = 0f;
                    float sp = (float)Math.Sqrt(o.vx * o.vx + o.vz * o.vz), dec = (round ? RollFriction : SlideFriction) * h;
                    if (sp <= dec || sp < 0.01f) { o.vx = o.vz = 0f; o.flying = false; }
                    else { float k = (sp - dec) / sp; o.vx *= k; o.vz *= k; o.roll += sp * h; }
                }
            }
            if (o.x < -Box) { o.x = -Box; o.vx = Math.Abs(o.vx) * 0.3f; } else if (o.x > Box) { o.x = Box; o.vx = -Math.Abs(o.vx) * 0.3f; }
            if (o.z < -Box) { o.z = -Box; o.vz = Math.Abs(o.vz) * 0.3f; } else if (o.z > Box) { o.z = Box; o.vz = -Math.Abs(o.vz) * 0.3f; }
            if (o.y > Box) { o.y = Box; if (o.vy > 0f) o.vy = 0f; }
        }

        /// <summary>The object a script means by an id: 0 = the thing itself, 1.. = one it made. Null when there is none.</summary>
        internal RoomObject Body(int id)
        {
            if (id == 0) return Me.body;
            return id >= 1 && id <= MaxObjects && Objects[id - 1].on ? Objects[id - 1] : null;
        }

        /// <summary>The middle of an object, for a touch test (the thing itself is measured from its foot).</summary>
        static float MidY(RoomObject o, bool me) => me ? o.y + o.size * 0.5f : o.y;

        // ------------------------------------------------------------------ the calls of the script (RoomScriptApi binds them to Lua)
        internal void Offer(string name, string label, DynValue fn)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            if (name.Length > 24) name = name.Substring(0, 24);
            label = Short(string.IsNullOrWhiteSpace(label) ? name : label.Trim(), 18);
            foreach (var o in Offers) if (o.name == name) { o.label = label; if (fn != null) _offerFn[name] = fn; return; }
            if (Offers.Count >= MaxOffers) { Note("offer: a thing offers " + MaxOffers + " acts at most ('" + name + "' is left out)"); return; }
            Offers.Add(new RoomOffer { name = name, label = label });
            if (fn != null) _offerFn[name] = fn;
            Say("offer", name + " '" + label + "'");
        }

        internal void SetWearable(string slot)
        {
            slot = FixSlot(slot, "wearable");
            WearSlot = slot; Say("wearable", slot);
        }

        string FixSlot(string slot, string fn)
        {
            slot = (slot ?? "").Trim();
            foreach (var s in Slots) if (string.Equals(s, slot, StringComparison.OrdinalIgnoreCase)) return s;
            string low = slot.ToLowerInvariant();
            if (low == "hand" || low == "right" || low == "righthand" || low == "hand_r" || low == "r") return "handR";
            if (low == "left" || low == "lefthand" || low == "hand_l" || low == "l") return "handL";
            if (low == "hat" || low == "top") return "head";
            if (slot.Length > 0) Note(fn + ": there is no slot '" + slot + "' (handR | handL | head | back): handR is taken");
            return WearSlot.Length > 0 ? WearSlot : "handR";
        }

        void SetWornBy(string slot, string why)
        {
            if (WornOn == slot) return;
            if (slot.Length == 0 && WornOn.Length > 0)
            {
                // taken off: it is put down beside the partner
                PetAt(out float px, out float pz);
                Me.body.x = M.Clamp(px + 0.07f, -Box, Box); Me.body.z = pz; Me.body.y = 0f; Me.body.flying = false; Me.body.vx = Me.body.vy = Me.body.vz = 0f; Me.moved = true;
            }
            WornOn = slot;
            Wrote(); Say(why, slot);
            try { _host.Wear(slot); } catch (Exception) { }
        }

        internal void PetGoto(float x, float z, float speed, DynValue then)
        {
            float cx = M.Clamp(x, PetMinX, PetMaxX), cz = M.Clamp(z, PetMinZ, PetMaxZ);
            if (Math.Abs(cx - x) > 0.005f || Math.Abs(cz - z) > 0.005f) Note("pet_goto: the partner is sent to (" + N(x) + ", " + N(z) + "), which is off its desk area - it goes to (" + N(cx) + ", " + N(cz) + ") instead");
            Pet.active = true; Pet.walk = true; Pet.gx = cx; Pet.gz = cz; Pet.speed = M.Clamp(speed > 0f ? speed : WalkSpeed, 0.05f, 0.3f);
            _walkSince = Time; _onArrive = then;
            Wrote(); Say("pet_goto", N(cx) + " " + N(cz));
        }

        internal void PetFace(bool user, float x, float z)
        {
            Pet.active = true; Pet.face = user ? 2 : 1; Pet.fx = Lim(x); Pet.fz = Lim(z); _faceUntil = Time + 0.6f; _petHold = Time + PetGrace;
            Wrote(); Say(user ? "pet_face_user" : "pet_face", user ? "" : N(x) + " " + N(z));
        }

        internal void PetPose(string name, float seconds, string on = "", string onId = "")
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            if (on.Length == 0 && (name.Length == 0 || name == "none" || name == "stop")) { Pet.pose = ""; Pet.on = ""; Pet.onId = ""; Wrote(); Say("pet_pose", "none"); return; }
            if (name.Length == 0) name = on == "lie" ? "sleep" : "sit";
            if (!RoomScriptApi.IsPose(name))
            {
                if (on.Length == 0 && _motions.Has(name)) { PetPlay(name, null); return; }      // (it meant a motion of its own)
                Note("pet_pose: there is no pose '" + name + "' (" + string.Join(" ", RoomScriptApi.Poses) + ") - idle is taken");
                name = "idle";
            }
            Pet.active = true; Pet.pose = name; Pet.on = on; Pet.onId = on.Length > 0 && onId != ItemId ? onId ?? "" : ""; Pet.poseSince = Time; Pet.poseUntil = Time + M.Clamp(seconds > 0f ? seconds : on.Length > 0 ? 20f : 2f, 0.2f, 3600f);
            Pet.motion = ""; _onMotionEnd = null;      // (what was asked last is what it does)
            Wrote(); Say(on == "sit" ? "pet_sit" : on == "lie" ? "pet_lie" : "pet_pose", name);
        }

        internal void PetPlay(string name, DynValue then)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            if (!_motions.Has(name))
            {
                if (RoomScriptApi.IsPose(name)) { PetPose(name, 0f); return; }      // (it meant a built-in pose)
                Note("pet_play: no motion '" + name + "' has been defined with motion(...)");
                return;
            }
            Pet.active = true; Pet.motion = name; Pet.motionT = 0f; Pet.motionSeconds = Math.Max(0.05f, _motions.Seconds(name)); Pet.motionLoop = _motions.Loops(name);
            Pet.pose = ""; Pet.on = ""; Pet.onId = "";
            _onMotionEnd = then;
            Wrote(); Say("pet_play", name);
        }

        internal void PetSay(string text, float seconds)
        {
            text = Short((text ?? "").Trim(), 80);
            if (text.Length == 0) return;
            Wrote(); Say("pet_say", text);
            try { _host.Say(text, M.Clamp(seconds > 0f ? seconds : 2.5f, 0.5f, 10f)); } catch (Exception) { }
        }

        internal void PetHold(string slot)
        {
            slot = string.IsNullOrWhiteSpace(slot) ? (WearSlot.Length > 0 ? WearSlot : "handR") : FixSlot(slot, "pet_hold");
            SetWornBy(slot, "pet_hold");
        }

        internal void PetDrop() { if (WornOn.Length > 0) SetWornBy("", "pet_drop"); }

        internal void PetAt(out float x, out float z) { x = 0f; z = 0f; try { _host.PetAt(out x, out z); } catch (Exception) { } x = Fin(x); z = Fin(z); }

        internal bool PetIsBusy()
        {
            return Pet.walk || Pet.pose.Length > 0 || (Pet.motion.Length > 0 && !Pet.motionLoop) || Time < _faceUntil;
        }

        /// <summary>The partner is its own again at once (it stops walking, posing, sitting for this script).</summary>
        internal void PetFree()
        {
            bool was = Pet.active;
            Pet.active = false; Pet.walk = false; Pet.face = 0; Pet.pose = ""; Pet.motion = ""; Pet.on = ""; Pet.onId = ""; _onArrive = null; _onMotionEnd = null; _faceUntil = 0f;
            if (was) Wrote();
            Say("pet_free", "");
        }

        readonly List<RoomThingInfo> _things = new List<RoomThingInfo>();
        /// <summary>The OTHER things in the room.</summary>
        internal List<RoomThingInfo> Things()
        {
            _things.Clear();
            try { _host.Things(_things); } catch (Exception) { }
            _things.RemoveAll(t => t == null || (ItemId.Length > 0 && t.id == ItemId));
            if (_things.Count > 40) _things.RemoveRange(40, _things.Count - 40);
            return _things;
        }

        /// <summary>The nearest other thing that is for that ("sit", "table" ...; "" = any) within r metres of this thing, or null.</summary>
        internal RoomThingInfo Near(string use, float r)
        {
            use = (use ?? "").Trim().ToLowerInvariant();
            MePlace(out float mx, out _, out float mz);
            RoomThingInfo best = null; float bd = r > 0f ? r * r : 0.3f * 0.3f;
            foreach (var t in Things())
            {
                if (use.Length > 0 && Array.IndexOf(t.uses ?? new string[0], use) < 0 && t.kind != use) continue;
                float d = (t.x - mx) * (t.x - mx) + (t.z - mz) * (t.z - mz);
                if (d <= bd) { bd = d; best = t; }
            }
            return best;
        }

        internal void Ambience(string what)
        {
            what = RoomScriptApi.AmbienceId(Short((what ?? "").Trim(), 60));
            Wrote(); Say(what.Length > 0 ? "ambience" : "ambience_clear", what);
            try { _host.Ambience(what); } catch (Exception) { }
        }

        internal void Music(string what)
        {
            what = RoomScriptApi.SongId(Short((what ?? "").Trim(), 60));
            Wrote(); Say(what.Length > 0 ? "music" : "music_stop", what);
            try { _host.Music(what); } catch (Exception) { }
        }

        /// <summary>Rolls an object along the desk so that it comes to rest at that place.</summary>
        internal void RollTo(int id, float x, float z)
        {
            var o = Body(id); if (o == null) { Missing("roll_to", id); return; }
            if (id == 0) Unworn("roll_to");
            x = LimNote(x, "roll_to"); z = LimNote(z, "roll_to");
            bool round = id == 0 || o.shape == "ball" || o.shape == "ring" || o.shape == "disc";
            float dx = x - o.x, dz = z - o.z, d = (float)Math.Sqrt(dx * dx + dz * dz);
            o.y = id == 0 ? 0f : Rest(o);
            if (d < 0.003f) { o.vx = o.vy = o.vz = 0f; o.flying = false; return; }      // it is there already
            float v = Math.Min(MaxSpeed, (float)Math.Sqrt(2f * (round ? RollFriction : SlideFriction) * d));
            Throw(id, dx / d * v, 0f, dz / d * v);
        }

        internal void MeMove(float x, float z, float y, bool hasY)
        {
            Unworn("me_move");
            var b = Me.body;
            b.x = LimNote(x, "me_move"); b.z = LimNote(z, "me_move"); if (hasY) b.y = M.Clamp(y, 0f, Box);
            b.flying = false; b.vx = b.vy = b.vz = 0f; Me.moved = true;
            Wrote(); Say("me_move", N(b.x) + " " + N(b.y) + " " + N(b.z));
        }
        /// <summary>Where the thing is now: where it stands, or - worn - at the partner's hand.</summary>
        internal void MePlace(out float x, out float y, out float z)
        {
            if (WornOn.Length > 0) { PetAt(out x, out z); y = WornOn == "head" ? PetHeight : HandHeight; return; }
            x = Me.body.x; y = Me.body.y; z = Me.body.z;
        }
        internal void MeTurn(float yaw) { Me.yaw = Wrap(yaw); Me.moved = true; Wrote(); Say("me_turn", N(Me.yaw)); }
        internal void MeSpin(float degPerSec) { Me.spin = M.Clamp(degPerSec, -1080f, 1080f); Me.moved = true; Wrote(); Say("me_spin", N(Me.spin)); }
        internal void MeBounce() { Me.bounceAt = Time; Wrote(); Say("me_bounce", ""); }
        internal void MeGlow(string color, float seconds)
        {
            if (string.IsNullOrEmpty(color)) { Me.glow = ""; Me.glowUntil = 0f; Wrote(); Say("me_glow", "off"); return; }
            Me.glow = color; Me.glowUntil = seconds > 0f ? Time + Math.Min(seconds, 3600f) : 0f;
            Wrote(); Say("me_glow", Me.glow);
        }
        internal void MeScale(float s)
        {
            float c = M.Clamp(s, 0.5f, 2f);
            if (Math.Abs(c - s) > 0.001f) Note("me_scale: " + N(s) + " is outside 0.5 .. 2 (" + N(c) + " is taken)");
            Me.scale = c; Wrote(); Say("me_scale", N(c));
        }

        void Unworn(string why)
        {
            if (WornOn.Length == 0) return;
            // (a thing that is worn and is put or thrown somewhere leaves the hand first)
            PetAt(out float px, out float pz);
            WornOn = "";
            Me.body.x = px; Me.body.z = pz; Me.body.y = HandHeight;
            Say("pet_drop", "(" + why + ")");
            try { _host.Wear(""); } catch (Exception) { }
        }

        internal int MakeObject(string shape, float size, string color)
        {
            shape = (shape ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(Shapes, shape) < 0)
            {
                string was = shape;
                shape = shape == "sphere" || shape == "circle" ? "ball" : shape == "cube" || shape == "square" ? "box" : shape == "coin" || shape == "plate" ? "disc" : shape == "torus" || shape == "hoop" ? "ring" : "ball";
                Note("make: there is no shape '" + was + "' (" + string.Join(" | ", Shapes) + "): " + shape + " is taken");
            }
            for (int i = 0; i < Objects.Length; i++)
            {
                var o = Objects[i];
                if (o.on) continue;
                o.on = true; o.shape = shape; o.color = string.IsNullOrEmpty(color) ? "#FFFFFF" : color;
                float s = size > 0f ? size : 0.02f, c = M.Clamp(s, 0.005f, 0.20f);
                if (Math.Abs(c - s) > 0.0005f) Note("make: a size of " + N(s) + " m is outside 0.005 .. 0.2 (" + N(c) + " is taken)");
                o.size = c;
                // it appears just over the thing until it is put somewhere
                o.x = Me.body.x; o.z = Me.body.z; o.y = Me.body.y + ItemSize * Me.scale + c;
                o.vx = o.vy = o.vz = 0f; o.flying = false; o.thrown = false; o.bounces = 0; o.roll = 0f; o.madeAt = Time; Was(o);
                Wrote(); Say("make", shape + " #" + (i + 1));
                return i + 1;
            }
            Note("make: a thing has " + MaxObjects + " little objects at most (remove the ones that are no longer needed)");
            return 0;
        }

        internal void Put(int id, float x, float y, float z)
        {
            var o = Body(id); if (o == null) { Missing("put", id); return; }
            if (id == 0) { Unworn("put"); Me.moved = true; }
            o.x = LimNote(x, "put"); o.z = LimNote(z, "put"); o.y = M.Clamp(y, id == 0 ? 0f : Rest(o), Box);
            o.vx = o.vy = o.vz = 0f; o.flying = false; Was(o);
            Wrote(); Say("put", "#" + id + " " + N(o.x) + " " + N(o.y) + " " + N(o.z));
        }

        internal void Throw(int id, float vx, float vy, float vz)
        {
            var o = Body(id); if (o == null) { Missing("throw", id); return; }
            if (id == 0) { Unworn("throw"); Me.moved = true; }
            float m = Math.Max(Math.Abs(vx), Math.Max(Math.Abs(vy), Math.Abs(vz)));
            if (m > MaxSpeed) { Note("throw: " + N(m) + " m/s is faster than things fly here (" + N(MaxSpeed) + " m/s at most)"); float k = MaxSpeed / m; vx *= k; vy *= k; vz *= k; }
            o.vx = vx; o.vy = vy; o.vz = vz; o.flying = true; o.thrown = true; o.bounces = 0;
            Wrote(); Say("throw", "#" + id + " " + N(vx) + " " + N(vy) + " " + N(vz));
        }

        internal void ThrowTo(int id, float x, float y, float z, float seconds)
        {
            var o = Body(id); if (o == null) { Missing("throw_to", id); return; }
            if (id == 0) Unworn("throw_to");
            x = LimNote(x, "throw_to"); z = LimNote(z, "throw_to"); y = M.Clamp(y, 0f, Box);
            float dx = x - o.x, dy = y - o.y, dz = z - o.z;
            float t = seconds > 0f ? seconds : (float)Math.Sqrt(dx * dx + dz * dz) / 0.5f;
            t = M.Clamp(t, 0.15f, 4f);
            Throw(id, dx / t, dy / t + 0.5f * Gravity * t, dz / t);
        }

        internal bool Where(int id, out float x, out float y, out float z)
        {
            var o = Body(id); x = y = z = 0f;
            if (o == null) return false;
            x = o.x; y = o.y; z = o.z; return true;
        }

        internal bool Hit(int id, float x, float y, float z, float r)
        {
            var o = Body(id); if (o == null) return false;
            float lift = id == 0 ? o.size * 0.5f : 0f;
            float reach = Math.Max(0f, r) + o.size * 0.5f;
            // the nearest point to (x, y, z) on the way the object went in this step
            float ax = o.px, ay = o.py + lift, az = o.pz, bx = o.x - ax, by = o.y + lift - ay, bz = o.z - az;
            float len = bx * bx + by * by + bz * bz;
            float u = len < 1e-10f ? 0f : M.Clamp01(((x - ax) * bx + (y - ay) * by + (z - az) * bz) / len);
            float qx = ax + bx * u - x, qy = ay + by * u - y, qz = az + bz * u - z;
            return qx * qx + qy * qy + qz * qz <= reach * reach;
        }

        internal void Remove(int id)
        {
            if (id == 0) { Note("remove: the thing itself (0) cannot be removed"); return; }
            var o = Body(id); if (o == null) return;
            o.on = false; o.flying = false;
            Wrote(); Say("remove", "#" + id);
        }

        internal int Hands(out RoomHand[] hands)
        {
            hands = _hands; int n = 0;
            try { n = _host.Hands(_hands); } catch (Exception) { }
            return n < 0 ? 0 : n > _hands.Length ? _hands.Length : n;
        }

        internal void User(out float x, out float y, out float z) { x = 0f; y = 0.35f; z = -0.5f; try { _host.UserHead(out x, out y, out z); } catch (Exception) { } x = Fin(x); y = Fin(y); z = Fin(z); }

        internal void Sparks(float x, float y, float z, int n, string color)
        {
            Wrote();
            if (++_sparksNow > MaxSparksPerStep) return;
            n = n <= 0 ? 8 : n > 24 ? 24 : n;
            x = Lim(x); y = M.Clamp(y, 0f, Box); z = Lim(z);
            string c = string.IsNullOrEmpty(color) ? "#FFE08A" : color;
            Say("sparks", n + " at " + N(x) + " " + N(y) + " " + N(z));
            try { _host.Sparks(x, y, z, n, c); } catch (Exception) { }
        }

        internal void Sound(string name)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(SfxSynth.Names, name) < 0)
            {
                string was = name;
                name = name == "tada" || name == "fanfare" ? "ta-da" : name == "boing" ? "bounce" : name == "bell" || name == "ding" || name == "chime" ? "sparkle" : name == "drum" || name == "knock" || name == "thud" ? "hammer" : name == "click" ? "tap" : "";
                Note("sound: there is no sound '" + was + "' (" + string.Join(" ", SfxSynth.Names) + ")" + (name.Length > 0 ? ": " + name + " is taken" : ""));
                if (name.Length == 0) return;
            }
            Wrote(); Say("sound", name);
            if (WornOn.Length > 0) { PetAt(out float px, out float pz); Emit(name, px, HandHeight, pz); }
            else Emit(name, Me.body.x, Me.body.y + ItemSize * 0.5f, Me.body.z);
        }

        void Emit(string name, float x, float y, float z)
        {
            if (++_soundsNow > MaxSoundsPerStep) return;
            try { _host.Sound(name, x, y, z); } catch (Exception) { }
        }

        internal void Label(string text, float x, float y, float z, float seconds)
        {
            text = Short((text ?? "").Trim(), 40);
            if (text.Length == 0) return;
            RoomLabel into = null;
            foreach (var l in Labels) if (!l.on) { into = l; break; }
            if (into == null) { into = Labels[0]; foreach (var l in Labels) if (l.since < into.since) into = l; }      // (a fifth one takes the place of the oldest)
            into.on = true; into.text = text; into.x = Lim(x); into.y = M.Clamp(y, 0f, Box); into.z = Lim(z);
            into.since = Time; into.until = Time + M.Clamp(seconds > 0f ? seconds : 2f, 0.3f, 10f);
            Wrote(); Say("label", text);
        }

        internal int After(float seconds, DynValue fn, bool every)
        {
            if (fn == null) { Note((every ? "every" : "after") + ": the second argument must be a function"); return 0; }
            foreach (var t in _timers)
            {
                if (t.on) continue;
                float s = every ? M.Clamp(seconds, 0.05f, 60f) : M.Clamp(seconds, 0f, 120f);
                t.on = true; t.id = ++_timerIds; t.at = Time + s; t.every = every ? s : 0f; t.fn = fn;
                _wrote = true;
                return t.id;
            }
            Note((every ? "every" : "after") + ": a thing has " + MaxTimers + " timers at most (this one is left out)");
            return 0;
        }

        internal void Cancel(int id) { foreach (var t in _timers) if (t.on && t.id == id) { t.on = false; t.fn = null; } }

        /// <summary>The script says it has finished: update() is not called any more, and when what it started has run out the partner is its own again.</summary>
        internal void Done()
        {
            _updateOn = false;
            foreach (var t in _timers) if (t.on && t.every > 0f) { t.on = false; t.fn = null; }
            Say("done", "");
        }

        // ------------------------------------------------------------------ small things
        void Call(DynValue fn, params DynValue[] args)
        {
            if (fn == null || !Ok) return;
            if (!_lua.Call(fn, args)) Fail();
        }

        void Fail() => Fail(_lua.Error, _lua.ErrorLine);

        void Fail(string message, int line)
        {
            if (Error.Length > 0) return;
            Error = string.IsNullOrEmpty(message) ? "the script failed" : message; ErrorLine = line;
            // the thing stays an ordinary thing
            Release();
            foreach (var o in Objects) o.on = false;
            foreach (var l in Labels) l.on = false;
            foreach (var t in _timers) { t.on = false; t.fn = null; }
            Me.spin = 0f; Me.glow = ""; Me.body.flying = false;
            Trace?.Invoke("error", Error);
        }

        void Wrote() { _wrote = true; }
        void Say(string fn, string what) { Trace?.Invoke(fn, what); }
        void Missing(string fn, int id) { Note(fn + ": there is no object " + id + " (it was removed, or never made)"); }

        internal void Note(string text)
        {
            if (Notes.Contains(text)) return;
            // (the same complaint with other numbers is the same complaint)
            int cut = text.IndexOf(':');
            if (cut > 0) { string head = text.Substring(0, cut + 1); int same = 0; foreach (var n in Notes) if (n.StartsWith(head, StringComparison.Ordinal)) same++; if (same >= 2) return; }
            _lua.Note(text);
        }

        float LimNote(float v, string fn)
        {
            v = Fin(v);
            if (v < -Box || v > Box) { Note(fn + ": " + N(v) + " m is further than " + N(Box) + " m from the home spot (it is cut down to that)"); return v < 0 ? -Box : Box; }
            return v;
        }

        static float Lim(float v) => M.Clamp(Fin(v), -Box, Box);
        static float Fin(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        static float Wrap(float deg) { deg = Fin(deg) % 360f; return deg < 0f ? deg + 360f : deg; }
        static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string Short(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        /// <summary>"#RRGGBB" for what the script gave as a colour (white, and a note, when it is none).</summary>
        internal string ColorOf(DynValue v, string fn)
        {
            string c = RoomScriptApi.Color(v);
            if (c.Length > 0) return c;
            if (v != null && v.Type != DataType.Nil && v.Type != DataType.Void) Note(fn + ": " + (v.Type == DataType.String ? "'" + Short(v.String, 20) + "'" : LuaSandbox.Text(v)) + " is not a colour (\"#RRGGBB\" or " + string.Join(" ", LuaSandbox.ColorNames) + "): white is taken");
            return "#FFFFFF";
        }

        /// <summary>A room with nobody in it (a script that is only loaded, to see what it offers).</summary>
        sealed class NoRoom : IRoomHost
        {
            public void PetAt(out float x, out float z) { x = 0f; z = 0f; }
            public int Hands(RoomHand[] into) => 0;
            public void UserHead(out float x, out float y, out float z) { x = 0f; y = 0.35f; z = -0.5f; }
            public void Say(string text, float seconds) { }
            public void Sound(string name, float x, float y, float z) { }
            public void Sparks(float x, float y, float z, int n, string color) { }
            public void Wear(string slot) { }
            public void Things(List<RoomThingInfo> into) { }
            public void Ambience(string what) { }
            public void Music(string what) { }
        }
    }
}
