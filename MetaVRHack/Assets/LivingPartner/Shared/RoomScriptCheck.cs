using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LivingPartner.Shared
{
    // CHECKING A ROOM SCRIPT BY RUNNING IT. The host's writer has a language model write the script of a thing; before
    // the thing gets it, the script is run here in a room that is not there (FakeRoom): it is made, used, every act it
    // offers is asked for, it is worn, the acts are asked for again, it is taken off. What the script did each time is
    // written down, and what is wrong with it is said in words the model can repair it from.

    /// <summary>
    /// A room that is not there: the partner walks where it is sent, a fingertip of the "user" reaches for whatever
    /// comes near, and everything that happens once is written down. For the checker and for tests.
    /// </summary>
    public sealed class FakeRoom : IRoomHost
    {
        public float petX, petZ;
        public bool petBusy;
        public float headX = 0f, headY = 0.35f, headZ = -0.50f;
        /// <summary>The fingertip: where it rests, where it is, whether it is seen at all.</summary>
        public bool handSeen = true; public float handX = 0.12f, handY = 0.02f, handZ = -0.30f;
        public float restX = 0.12f, restY = 0.02f, restZ = -0.30f;
        /// <summary>reach: the fingertip goes for the nearest object that is within 0.8 m. wild: it wanders about at random (and pinches now and then).</summary>
        public bool reach = true, wild;
        public string worn = "";
        public float time;
        public readonly List<string> said = new List<string>(), sounds = new List<string>(), wears = new List<string>();
        public int sparks, sparkBursts;
        readonly GlassRng _rng = new GlassRng();
        float _wx, _wy, _wz, _wildT; bool _pinch;
        bool _going; float _goX, _goZ, _goSpeed;

        public FakeRoom(int seed = 1) { _rng.Seed(seed); }

        public void PetAt(out float x, out float z) { x = petX; z = petZ; }
        public int Hands(RoomHand[] into)
        {
            if (!handSeen || into == null || into.Length == 0) return 0;
            into[0] = new RoomHand { x = handX, y = handY, z = handZ, pinch = _pinch };
            return 1;
        }
        public void UserHead(out float x, out float y, out float z) { x = headX; y = headY; z = headZ; }
        public void Say(string text, float seconds) { if (said.Count < 200) said.Add(text); }
        public void Sound(string name, float x, float y, float z) { if (sounds.Count < 2000) sounds.Add(name); }
        public void Sparks(float x, float y, float z, int n, string color) { sparkBursts++; sparks += n; }
        public void Wear(string slot) { worn = slot ?? ""; if (wears.Count < 200) wears.Add(worn); }
        /// <summary>The things that stand in this room (put them in before the script runs).</summary>
        public readonly List<RoomThingInfo> things = new List<RoomThingInfo>();
        public void Things(List<RoomThingInfo> into) { into.AddRange(things); }
        /// <summary>The surroundings and the tune as the scripts asked for them ("" = none), and every request in order.</summary>
        public string ambience = "", music = "";
        public readonly List<string> asked = new List<string>();
        public void Ambience(string what) { ambience = what ?? ""; if (asked.Count < 200) asked.Add("ambience " + ambience); }
        public void Music(string what) { music = what ?? ""; if (asked.Count < 200) asked.Add("music " + music); }

        /// <summary>The room moves on by dt: the partner walks to where the script wants it, the fingertip goes where it is drawn to.</summary>
        public void Step(float dt, RoomScript rs)
        {
            time += dt;
            if (rs != null && rs.Pet.active && rs.Pet.walk) { _going = true; _goX = rs.Pet.gx; _goZ = rs.Pet.gz; _goSpeed = rs.Pet.speed; }
            if (_going && !petBusy)
            {
                // (like the real one, it walks on to the spot it was sent to when the script has stopped asking)
                float dx = _goX - petX, dz = _goZ - petZ, d = (float)Math.Sqrt(dx * dx + dz * dz), step = _goSpeed * dt;
                if (d <= step || d < 1e-5f) { petX = _goX; petZ = _goZ; _going = false; } else { petX += dx / d * step; petZ += dz / d * step; }
            }
            float tx = restX, ty = restY, tz = restZ, speed = 0.6f;
            if (wild)
            {
                _wildT -= dt;
                if (_wildT <= 0f)
                {
                    _wildT = 0.2f + _rng.Next01() * 1.2f;
                    _wx = -0.6f + 1.2f * _rng.Next01(); _wy = 0.3f * _rng.Next01() * _rng.Next01(); _wz = -0.6f + 1.0f * _rng.Next01();
                    _pinch = _rng.Next01() < 0.3f; handSeen = _rng.Next01() > 0.15f;
                    // (now and then straight at something of the script's)
                    if (rs != null && _rng.Next01() < 0.5f) { var o = Nearest(rs, 9f); if (o != null) { _wx = o.x; _wy = o.y; _wz = o.z; } }
                }
                tx = _wx; ty = _wy; tz = _wz; speed = 1.2f;
            }
            else if (reach && rs != null)
            {
                var o = Nearest(rs, 0.8f);
                if (o != null) { tx = o.x; ty = o.y; tz = o.z; }
            }
            float hx = tx - handX, hy = ty - handY, hz = tz - handZ, hd = (float)Math.Sqrt(hx * hx + hy * hy + hz * hz), hs = speed * dt;
            if (hd <= hs || hd < 1e-5f) { handX = tx; handY = ty; handZ = tz; } else { handX += hx / hd * hs; handY += hy / hd * hs; handZ += hz / hd * hs; }
        }

        RoomObject Nearest(RoomScript rs, float within)
        {
            RoomObject best = null; float bd = within * within;
            for (int i = -1; i < rs.Objects.Length; i++)
            {
                RoomObject o = i < 0 ? rs.Me.body : rs.Objects[i];
                if (i < 0 ? !(rs.Me.moved || rs.Me.body.flying) || rs.WornOn.Length > 0 : !o.on) continue;
                float dx = o.x - handX, dy = o.y - handY, dz = o.z - handZ, d = dx * dx + dy * dy + dz * dz;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }
    }

    /// <summary>One thing the checker did to the script, and what the script did then.</summary>
    public sealed class RoomCheckPhase
    {
        public string name = "";            // make | use | act swing | wear | worn: act swing | unwear
        public bool ran;                    // the script has something for it
        public float seconds;               // how long the script was busy with it
        public bool endless;                // it was still going when the checker gave up
        public int visible;                 // calls that are seen or heard
        public readonly List<string> calls = new List<string>();     // "0.48 pet_play swing"

        public bool Did(string function, string whatContains = null)
        {
            foreach (var c in calls)
            {
                int sp = c.IndexOf(' '); string rest = sp < 0 ? "" : c.Substring(sp + 1);
                if (rest == function || rest.StartsWith(function + " ", StringComparison.Ordinal))
                    if (whatContains == null || rest.IndexOf(whatContains, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>How many of the calls begin like that ("sparks", "sound hammer").</summary>
        public int Count(string begins)
        {
            int n = 0;
            foreach (var c in calls)
            {
                int sp = c.IndexOf(' '); string rest = sp < 0 ? "" : c.Substring(sp + 1);
                if (rest == begins || rest.StartsWith(begins + " ", StringComparison.Ordinal)) n++;
            }
            return n;
        }

        /// <summary>When (seconds into the phase) the script first made that call; -1 = never.</summary>
        public float When(string function, string whatContains = null)
        {
            foreach (var c in calls)
            {
                int sp = c.IndexOf(' '); string rest = sp < 0 ? "" : c.Substring(sp + 1);
                if ((rest == function || rest.StartsWith(function + " ", StringComparison.Ordinal)) && (whatContains == null || rest.IndexOf(whatContains, StringComparison.Ordinal) >= 0))
                    return float.Parse(c.Substring(0, sp), CultureInfo.InvariantCulture);
            }
            return -1f;
        }
    }

    public sealed class RoomCheckResult
    {
        /// <summary>The script loads and runs through everything without an error (warnings do not make it false).</summary>
        public bool ok;
        public string error = "";           // "" or the message
        public int errorLine;               // 1-based, 0 = unknown
        public string errorIn = "";         // where it broke: load | make | use | act swing ...
        public bool wearable; public string slot = "";
        public readonly List<RoomOffer> offers = new List<RoomOffer>();
        public readonly List<string> hooks = new List<string>();        // the hooks it defines
        public readonly List<string> motions = new List<string>();      // the motions it defines
        public readonly List<RoomCheckPhase> phases = new List<RoomCheckPhase>();
        public readonly List<string> facts = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public int peakInstructions;        // the most one call into the script used
        /// <summary>What the room saw in all: the lines said, the sounds, how many sparks.</summary>
        public readonly List<string> said = new List<string>(), sounds = new List<string>();
        public int sparks;

        public RoomCheckPhase Phase(string name) { foreach (var p in phases) if (p.name == name) return p; return null; }

        /// <summary>The whole result as text: for the writer's repair prompt, and for a log.</summary>
        public string Report()
        {
            var sb = new StringBuilder();
            if (!ok) sb.Append("ERROR").Append(errorIn.Length > 0 ? " in " + errorIn : "").Append(errorLine > 0 ? ", line " + errorLine : "").Append(": ").Append(error).Append('\n');
            foreach (var f in facts) sb.Append("- ").Append(f).Append('\n');
            foreach (var p in phases)
            {
                if (!p.ran) continue;
                sb.Append("* ").Append(p.name).Append(" (").Append(p.seconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(p.endless ? " s, STILL GOING" : " s").Append("): ");
                int n = 0;
                foreach (var c in p.calls) { if (n++ >= 16) { sb.Append(" ..."); break; } if (n > 1) sb.Append("; "); sb.Append(c); }
                if (p.calls.Count == 0) sb.Append("nothing");
                sb.Append('\n');
            }
            foreach (var w in warnings) sb.Append("WARNING: ").Append(w).Append('\n');
            return sb.ToString();
        }
    }

    public static class RoomScriptCheck
    {
        public const float Dt = 1f / 30f;
        static readonly string[] Unseen = { "offer", "wearable", "done", "error" };

        /// <summary>
        /// Runs the script of a thing through everything a user can do with it, in a room that is not there.
        /// seconds: how long each step may go on before it is called endless. Never throws.
        /// </summary>
        public static RoomCheckResult Run(string lua, ItemDef item, float seconds = 30f, int seed = 1)
        {
            var res = new RoomCheckResult();
            try { Check(res, lua, item ?? new ItemDef { id = "thing", name = "thing", sizeM = 0.08f }, Math.Max(2f, Math.Min(seconds, 600f)), seed); }
            catch (Exception e) { res.ok = false; if (res.error.Length == 0) res.error = "the checker broke: " + e.GetType().Name + " " + e.Message; }
            return res;
        }

        static void Check(RoomCheckResult res, string lua, ItemDef item, float seconds, int seed)
        {
            var room = new FakeRoom(seed);
            RoomCheckPhase now = null; float phaseStart = 0f;
            var rs = new RoomScript(lua, item, room, seed);
            var played = new HashSet<string>();
            rs.Trace = (fn, what) =>
            {
                if (fn == "pet_play") played.Add(what);
                if (now == null || now.calls.Count >= 400) return;
                if (Array.IndexOf(Unseen, fn) < 0) now.visible++;
                now.calls.Add((rs.Time - phaseStart).ToString("0.00", CultureInfo.InvariantCulture) + " " + fn + (what.Length > 0 ? " " + what : ""));
            };
            bool Broke(string where)
            {
                if (rs.Ok) return false;
                res.ok = false; res.error = rs.Error; res.errorLine = rs.ErrorLine; res.errorIn = where;
                return true;
            }
            if (Broke("load")) { Finish(res, rs, room, played); return; }
            res.wearable = rs.Wearable; res.slot = rs.WearSlot;
            foreach (var o in rs.Offers) res.offers.Add(new RoomOffer { name = o.name, label = o.label });
            foreach (var h in RoomScript.Hooks) if (rs.Has(h)) res.hooks.Add(h);
            foreach (var m in rs.Motions) res.motions.Add(m);

            var littered = new HashSet<int>();
            // one thing the user does, and the script until it has finished with it
            bool Do(string name, Func<bool> start)
            {
                now = new RoomCheckPhase { name = name }; phaseStart = rs.Time; res.phases.Add(now);
                now.ran = start();
                float t = 0f;
                while (rs.Ok && rs.Active && t < seconds) { room.Step(Dt, rs); rs.Tick(Dt); t += Dt; }
                now.seconds = t;
                if (Broke(name)) return false;
                if (rs.Active)
                {
                    // Still going. That is as it should be for something the user stops (sitting on the chair, dancing):
                    // a long pose, or a script that says what happens when it is stopped (on_stop). Anything else would run for ever.
                    now.endless = true;
                    bool meant = rs.Has("on_stop") || (!rs.Updating && !rs.TimersRepeat);
                    if (!meant)
                        res.warnings.Add(name + ": still going after " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s - " +
                            (rs.Updating ? "update() never calls done(): it would run for ever" : "a timer started with every() is never cancelled (cancel it, or call done()): it would run for ever") +
                            " (if it is meant to go on until the user stops it, define on_stop())");
                    var before = now; float t0 = rs.Time;
                    now = new RoomCheckPhase { name = "stop " + name, ran = rs.Has("on_stop") }; phaseStart = rs.Time; res.phases.Add(now);
                    rs.Stop();
                    for (float u = 0f; rs.Ok && rs.Active && u < seconds; u += Dt) { room.Step(Dt, rs); rs.Tick(Dt); }
                    now.seconds = rs.Time - t0;
                    if (Broke("on_stop")) return false;
                    now = before;
                }
                if (now.ran && now.visible == 0) res.warnings.Add(name + ": the script does nothing that is seen or heard");
                for (int i = 0; i < rs.Objects.Length; i++)
                    if (rs.Objects[i].on && rs.Objects[i].thrown && littered.Add(i))
                        res.warnings.Add(name + ": an object that was thrown (the " + rs.Objects[i].shape + " #" + (i + 1) + ") is still lying there when everything is over - remove it");
                var done = now; now = null;
                return done.ran;
            }

            Do("make", () => { bool has = rs.Has("on_make"); rs.Make(); return has; });
            if (!rs.Ok) { Finish(res, rs, room, played); return; }
            Do("use", () => rs.Use());
            if (!rs.Ok) { Finish(res, rs, room, played); return; }
            foreach (var o in res.offers)
            {
                if (!Do("act " + o.name, () => rs.Act(o.name)) && rs.Ok) res.warnings.Add("offer(\"" + o.name + "\", ...) but there is no on_act(name) to do it");
                if (!rs.Ok) { Finish(res, rs, room, played); return; }
            }
            if (rs.Wearable)
            {
                Do("wear", () => rs.Wear());
                if (!rs.Ok) { Finish(res, rs, room, played); return; }
                if (rs.WornOn != rs.WearSlot) res.warnings.Add("wear: the thing is not on " + rs.WearSlot + " after it was put on (it is " + (rs.WornOn.Length > 0 ? "on " + rs.WornOn : "not worn") + ")");
                foreach (var o in res.offers)
                {
                    Do("worn: act " + o.name, () => rs.Act(o.name));
                    if (!rs.Ok) { Finish(res, rs, room, played); return; }
                }
                if (rs.WornOn.Length > 0)
                {
                    Do("unwear", () => rs.Unwear());
                    if (!rs.Ok) { Finish(res, rs, room, played); return; }
                    if (rs.WornOn.Length > 0) res.warnings.Add("unwear: the thing is still worn (on " + rs.WornOn + ") after it was taken off");
                }
            }
            res.ok = true;
            Finish(res, rs, room, played);
        }

        static void Finish(RoomCheckResult res, RoomScript rs, FakeRoom room, HashSet<string> played)
        {
            res.peakInstructions = rs.PeakInstructions;
            res.said.AddRange(room.said); res.sounds.AddRange(room.sounds); res.sparks = room.sparks;
            if (res.wearable) res.facts.Add("wearable: " + res.slot);
            foreach (var o in res.offers) res.facts.Add("offers: " + o.name + " \"" + o.label + "\"");
            foreach (var m in res.motions) res.facts.Add("motion: " + m + " (" + rs.MotionSeconds(m).ToString("0.##", CultureInfo.InvariantCulture) + " s)");
            if (res.hooks.Count > 0) res.facts.Add("hooks: " + string.Join(" ", res.hooks));
            if (!res.ok) return;
            foreach (var n in rs.Notes) res.warnings.Add(n);
            foreach (var m in res.motions) if (!played.Contains(m)) res.warnings.Add("the motion '" + m + "' is defined but never played (pet_play(\"" + m + "\"))");
            if (res.hooks.Count == 0) res.warnings.Add("the script defines none of the hooks (" + string.Join(", ", RoomScript.Hooks) + "): the thing does nothing");
            bool hasUse = res.hooks.Contains("on_use");
            if (!hasUse && res.offers.Count == 0 && !res.wearable && res.hooks.Count > 0) res.warnings.Add("pressing the thing does nothing: there is no on_use, no offer(...) and no wearable(...)");
            if (res.hooks.Contains("on_act") && res.offers.Count == 0) res.warnings.Add("on_act is defined but nothing is offered: call offer(name, label) at the top of the script");
            if ((res.hooks.Contains("on_wear") || res.hooks.Contains("on_unwear")) && !res.wearable) res.warnings.Add("on_wear is defined but the thing is not wearable: call wearable(slot) at the top of the script");
            if (res.peakInstructions > LuaSandbox.DefaultBudget / 2) res.warnings.Add("one call used " + res.peakInstructions + " instructions - more than half of what a call may use: do less work in one call");
        }
    }
}
