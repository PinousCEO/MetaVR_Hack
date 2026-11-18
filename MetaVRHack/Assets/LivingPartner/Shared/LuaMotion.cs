using System;
using System.Collections.Generic;
using System.Numerics;
using MoonSharp.Interpreter;

namespace LivingPartner.Shared
{
    /// <summary>
    /// MOTIONS that a script defines for the partner: "wearing the baseball bat adds a swing". The script calls
    ///   motion(name, seconds, function(u, p) ... end [, loop])
    /// and the function fills a pose table `p` for the moment u (0..1) - see Doc, which is the text for the language
    /// model. Evaluate turns that into a Pose (Skeleton.cs): the bones the script did not set come from a built-in
    /// base pose, every angle is clamped to what this character's body can do, and the hands are kept out of the
    /// bonnet. Pure: the same script, name, u and t always give the same Pose; no engine types.
    /// One LuaMotion belongs to one LuaSandbox (it registers the functions there). Used by GlassScript (pet_play /
    /// pet_pose) and by RoomScript.
    /// </summary>
    public sealed class LuaMotion
    {
        public const int MaxMotions = 24;
        public const int Budget = 20000;                // instructions one evaluation of a motion may use
        public const float MinSeconds = 0.1f, MaxSeconds = 30f;

        /// <summary>The functions this adds to a sandbox.</summary>
        public static readonly string[] Functions = { "motion", "ease", "swing", "pulse", "wave", "keys" };

        /// <summary>The names a script uses for the bones, and the bone each one is.</summary>
        public static readonly string[] BoneNames =
        {
            "hips", "spine", "chest", "head", "armL", "foreL", "armR", "foreR", "legL", "shinL", "footL", "legR", "shinR", "footR", "antenna",
        };
        static readonly Bone[] BoneOfName =
        {
            Bone.Hips, Bone.Spine, Bone.Chest, Bone.Head, Bone.ArmUL, Bone.ArmLL, Bone.ArmUR, Bone.ArmLR,
            Bone.LegUL, Bone.LegLL, Bone.FootL, Bone.LegUR, Bone.LegLR, Bone.FootR, Bone.Antenna,
        };
        /// <summary>The built-in poses a motion can stand on (p.base).</summary>
        public static readonly string[] BaseNames = { "idle", "stand", "walk", "run", "sit", "crouch", "cheer", "think", "wave", "dance", "ready" };

        /// <summary>The bone a script's name means (handL / handR are the fore bones). Bone.Count when it is no bone.</summary>
        public static Bone BoneOf(string name)
        {
            if (name == "handL") return Bone.ArmLL;
            if (name == "handR") return Bone.ArmLR;
            for (int i = 0; i < BoneNames.Length; i++) if (BoneNames[i] == name) return BoneOfName[i];
            return Bone.Count;
        }

        /// <summary>The bones of the left side take mirrored numbers: the same values on armL and armR give a symmetric pose.</summary>
        public static bool Mirrored(Bone b)
        {
            return b == Bone.ArmUL || b == Bone.ArmLL || b == Bone.LegUL || b == Bone.LegLL || b == Bone.FootL;
        }

        // ---- what this body can do: degrees, in the script's (mirrored) sense. { xMin, xMax, yMin, yMax, zMin, zMax }
        static readonly float[] LimHips = { -90, 90, -360, 360, -90, 90 };
        static readonly float[] LimSpine = { -25, 50, -45, 45, -25, 25 };
        static readonly float[] LimChest = { -20, 30, -40, 40, -20, 20 };
        static readonly float[] LimHead = { -35, 35, -70, 70, -25, 25 };
        static readonly float[] LimArm = { -125, 60, -60, 60, -45, 125 };
        static readonly float[] LimFore = { -110, 20, -45, 45, -45, 45 };
        static readonly float[] LimLeg = { -115, 45, -30, 30, -15, 45 };
        static readonly float[] LimShin = { 0, 125, -10, 10, -10, 10 };
        static readonly float[] LimFoot = { -45, 60, -30, 30, -20, 20 };
        static readonly float[] LimAntenna = { -60, 60, -60, 60, -60, 60 };
        public const float LiftMin = -0.15f, LiftMax = 0.6f, ShiftMax = 0.2f;
        public const float SquashMin = -0.4f, SquashMax = 0.6f;

        /// <summary>The range of a bone: { xMin, xMax, yMin, yMax, zMin, zMax } in the script's sense (left side mirrored).</summary>
        public static float[] LimitsOf(Bone b)
        {
            switch (b)
            {
                case Bone.Hips: return LimHips;
                case Bone.Spine: return LimSpine;
                case Bone.Chest: return LimChest;
                case Bone.Head: return LimHead;
                case Bone.ArmUL: case Bone.ArmUR: return LimArm;
                case Bone.ArmLL: case Bone.ArmLR: return LimFore;
                case Bone.LegUL: case Bone.LegUR: return LimLeg;
                case Bone.LegLL: case Bone.LegLR: return LimShin;
                case Bone.FootL: case Bone.FootR: return LimFoot;
                default: return LimAntenna;
            }
        }

        sealed class Def
        {
            public string name;
            public float seconds;
            public bool loop;
            public DynValue fn;
        }

        readonly LuaSandbox box;
        readonly List<Def> defs = new List<Def>();
        readonly Pose basePose = new Pose();
        readonly DynValue[] two = new DynValue[2];

        public LuaMotion(LuaSandbox box)
        {
            this.box = box;
            box.Def("motion", a =>
            {
                string name = Clean(a.Str(0, "", 24));
                if (name.Length == 0) throw LuaSandbox.Bad("motion: it needs a name: motion(\"swing\", 1.0, function(u, p) ... end)");
                DynValue fn = a.Fn(2);
                bool loop = a.Bool(3, false);
                float seconds = a.Num(1, 1f);
                if (fn == null && a.Fn(1) != null) { fn = a.Fn(1); seconds = 1f; loop = a.Bool(2, false); }      // motion(name, fn)
                if (fn == null) throw LuaSandbox.Bad("motion: the third argument must be a function(u, p)");
                seconds = seconds < MinSeconds ? MinSeconds : seconds > MaxSeconds ? MaxSeconds : seconds;
                Def d = Find(name);
                if (d == null)
                {
                    if (defs.Count >= MaxMotions) { box.Note("more than " + MaxMotions + " motions: '" + name + "' was left out"); return DynValue.Nil; }
                    d = new Def();
                    d.name = name;
                    defs.Add(d);
                }
                d.seconds = seconds; d.loop = loop; d.fn = fn;
                return DynValue.Nil;
            });
            box.Def("ease", a => DynValue.NewNumber(Ease(a.Num(0, 0f))));
            box.Def("swing", a =>
            {
                float from = a.Num(1, 0f), to = a.Num(2, 1f);
                return DynValue.NewNumber(from + (to - from) * Ease(a.Num(0, 0f)));
            });
            box.Def("pulse", a =>
            {
                float u = a.Num(0, 0f), n = a.Num(1, 1f, 0.01f, 100f);
                return DynValue.NewNumber(Math.Abs(Math.Sin(Math.PI * u * n)));
            });
            box.Def("wave", a =>
            {
                float u = a.Num(0, 0f), n = a.Num(1, 1f, 0.01f, 100f);
                return DynValue.NewNumber(Math.Sin(2.0 * Math.PI * u * n));
            });
            box.Def("keys", a => Keys(a));
        }

        static string Clean(string s) { return (s ?? "").Trim().ToLowerInvariant(); }

        /// <summary>0 -> 1 with a soft start and a soft stop (clamped outside 0..1).</summary>
        public static float Ease(float u)
        {
            if (!(u > 0f)) return 0f;
            if (u >= 1f) return 1f;
            return u * u * (3f - 2f * u);
        }

        // keys(u, {t0, v0, t1, v1, ...}): the value at u, going smoothly from each key to the next. A value is a number or {x, y, z}.
        DynValue Keys(LuaArgs a)
        {
            float u = a.Num(0, 0f);
            Table list = a.Tab(1);
            if (list == null) return DynValue.NewNumber(0);
            int n = list.Length / 2;
            if (n < 1) return DynValue.NewNumber(0);
            if (n > 64) n = 64;
            DynValue first = null, v0 = null, v1 = null;
            float t0 = 0f, t1 = 0f;
            for (int i = 0; i < n; i++)
            {
                DynValue tk = list.Get(2 * i + 1), vk = list.Get(2 * i + 2);
                if (tk.Type != DataType.Number || (vk.Type != DataType.Number && vk.Type != DataType.Table)) continue;
                float t = (float)tk.Number;
                if (float.IsNaN(t)) continue;
                if (first == null) first = vk;
                if (t <= u) { v0 = vk; t0 = t; }
                else { v1 = vk; t1 = t; break; }
            }
            if (first == null) return DynValue.NewNumber(0);
            if (v0 == null) return CopyValue(first);            // before the first key
            if (v1 == null || !(t1 > t0)) return CopyValue(v0); // after the last
            float k = Ease((u - t0) / (t1 - t0));
            if (v0.Type == DataType.Number && v1.Type == DataType.Number) return DynValue.NewNumber(v0.Number + (v1.Number - v0.Number) * k);
            Table r = box.NewTable();
            SetAxis(r, "x", 1, v0, v1, k); SetAxis(r, "y", 2, v0, v1, k); SetAxis(r, "z", 3, v0, v1, k);
            return DynValue.NewTable(r);
        }

        DynValue CopyValue(DynValue v)
        {
            if (v.Type == DataType.Number) return v;
            Table r = box.NewTable();
            SetAxis(r, "x", 1, v, v, 0f); SetAxis(r, "y", 2, v, v, 0f); SetAxis(r, "z", 3, v, v, 0f);
            return DynValue.NewTable(r);
        }

        static void SetAxis(Table r, string name, int index, DynValue a, DynValue b, float k)
        {
            float x = Axis(a, name, index), y = Axis(b, name, index);
            if (float.IsNaN(x) && float.IsNaN(y)) return;               // neither key sets this axis: it stays the base pose's
            if (float.IsNaN(x)) x = 0f;
            if (float.IsNaN(y)) y = 0f;
            r.Set(name, DynValue.NewNumber(x + (y - x) * k));
        }

        // an axis of a key's value: a number is its x; NaN = not set
        static float Axis(DynValue v, string name, int index)
        {
            if (v.Type == DataType.Number) return index == 1 ? (float)v.Number : float.NaN;
            if (v.Type != DataType.Table) return float.NaN;
            return LuaArgs.Field(v.Table, name, index, float.NaN);
        }

        // ------------------------------------------------------------------ what was defined

        Def Find(string name)
        {
            for (int i = 0; i < defs.Count; i++) if (defs[i].name == name) return defs[i];
            return null;
        }

        public int Count { get { return defs.Count; } }
        public string NameAt(int i) { return i >= 0 && i < defs.Count ? defs[i].name : ""; }
        public bool Has(string name) { return name != null && Find(Clean(name)) != null; }
        /// <summary>How long the motion lasts (0 when there is none of this name).</summary>
        public float Seconds(string name) { Def d = name != null ? Find(Clean(name)) : null; return d != null ? d.seconds : 0f; }
        public bool Loops(string name) { Def d = name != null ? Find(Clean(name)) : null; return d != null && d.loop; }

        /// <summary>The pose of the motion at u (0..1; a loop wraps). The base pose's own time is u * seconds.</summary>
        public bool Evaluate(string name, float u, Pose into)
        {
            return Evaluate(name, u, Wrap(u, Loops(name)) * Seconds(name), into);
        }

        /// <summary>
        /// The pose of the motion at u, on a base pose taken at the time t (seconds, free running: the base breathes
        /// on while a short motion repeats). False when there is no such motion or the script is broken (`into` is
        /// then the idle pose); a motion whose function breaks stops the script like any other error.
        /// </summary>
        public bool Evaluate(string name, float u, float t, Pose into)
        {
            if (into == null) return false;
            if (!(t > -1e6f && t < 1e6f)) t = 0f;
            Def d = name != null ? Find(Clean(name)) : null;
            if (d == null || box.Broken) { PoseLib.Idle(into, t); return false; }
            u = Wrap(u, d.loop);
            Table p = box.NewTable();
            two[0] = DynValue.NewNumber(u); two[1] = DynValue.NewTable(p);
            if (!box.CallWithin(Budget, d.fn, two)) { PoseLib.Idle(into, t); return false; }
            if (box.Result != null && box.Result.Type == DataType.Table) p = box.Result.Table;       // (a function that returns the pose instead of filling p)
            Fill(p, t, into);
            return true;
        }

        static float Wrap(float u, bool loop)
        {
            if (!(u > 0f)) return 0f;                   // also NaN
            if (loop) { u -= (float)Math.Floor(u); return u; }
            return u > 1f ? 1f : u;
        }

        /// <summary>A built-in pose by its name in p.base (anything unknown is idle).</summary>
        public static void Base(string name, Pose p, float t)
        {
            switch (name)
            {
                case "stand": case "none": p.Clear(); break;
                case "walk": PoseLib.Walk(p, t * 1.7f); break;
                case "run": PoseLib.Run(p, t * 2.4f); break;
                case "sit": PoseLib.Sit(p, t); break;
                case "crouch": PoseLib.Crouch(p, 0.6f); break;
                case "cheer": case "happy": PoseLib.Happy(p, t); break;
                case "think": PoseLib.Think(p, t); break;
                case "wave": PoseLib.Wave(p, t); break;
                case "dance": PoseLib.Dance(p, t, t * 2f); break;
                case "ready": PoseLib.TennisReady(p, t); break;
                default: PoseLib.Idle(p, t); break;
            }
        }

        void Fill(Table p, float t, Pose into)
        {
            DynValue b = p.Get("base");
            string baseName = b.Type == DataType.String ? Clean(b.String) : "idle";
            if (b.Type == DataType.String && Array.IndexOf(BaseNames, baseName) < 0 && baseName != "none" && baseName != "happy")
                box.Note("motion: unknown base '" + (baseName.Length > 20 ? baseName.Substring(0, 20) : baseName) + "' (there are: " + string.Join(" ", BaseNames) + ")");
            Base(baseName, into, t);
            basePose.CopyFrom(into);

            foreach (TablePair kv in p.Pairs)
            {
                if (kv.Key.Type != DataType.String) continue;
                string key = kv.Key.String;
                Bone bone = BoneOf(key);
                if (bone == Bone.Count) continue;
                float sign = Mirrored(bone) ? -1f : 1f;
                Vector3 e = into.Euler[(int)bone];
                DynValue v = kv.Value;
                if (v.Type == DataType.Number) { float x = Finite((float)v.Number); if (!float.IsNaN(x)) e.X = x; }
                else if (v.Type == DataType.Table)
                {
                    float x = LuaArgs.Field(v.Table, "x", 1, float.NaN), y = LuaArgs.Field(v.Table, "y", 2, float.NaN), z = LuaArgs.Field(v.Table, "z", 3, float.NaN);
                    if (!float.IsNaN(x)) e.X = x;
                    if (!float.IsNaN(y)) e.Y = y * sign;
                    if (!float.IsNaN(z)) e.Z = z * sign;
                }
                into.Euler[(int)bone] = e;
            }

            float lean = Number(p, "lean", 0f);
            if (lean != 0f) into.Euler[(int)Bone.Spine].X += lean;
            float lift = Number(p, "lift", float.NaN);
            if (!float.IsNaN(lift)) into.HipsOffset.Y = lift;
            float shift = Number(p, "shift", float.NaN);
            if (!float.IsNaN(shift)) into.HipsOffset.X = shift;
            float squash = Number(p, "squash", float.NaN);
            if (!float.IsNaN(squash)) into.Squash = squash;
            float mouth = Number(p, "mouth", float.NaN);
            if (!float.IsNaN(mouth)) into.Mouth = mouth;
            float blink = Number(p, "blink", float.NaN);
            if (!float.IsNaN(blink)) into.Blink = blink;
            DynValue look = p.Get("look");
            if (look.Type == DataType.Table)
            {
                float yaw = LuaArgs.Field(look.Table, "yaw", 1, 0f), pitch = LuaArgs.Field(look.Table, "pitch", 2, 0f);
                PoseLib.AddLook(into, yaw, pitch);
            }
            Limit(into, basePose);
        }

        static float Finite(float f) { return f > -1e6f && f < 1e6f ? f : float.NaN; }

        static float Number(Table p, string name, float otherwise)
        {
            DynValue v = p.Get(name);
            if (v.Type != DataType.Number) return otherwise;
            float f = Finite((float)v.Number);
            return float.IsNaN(f) ? otherwise : f;
        }

        // ------------------------------------------------------------------ the limits

        static float Clamp(float v, float a, float b) { return !(v >= a) ? a : v > b ? b : v; }       // NaN -> a

        /// <summary>
        /// Clamp a pose to what the body can do: every bone to its range, the offsets and the face channels to theirs,
        /// and the arms are taken back towards `safe` (the base pose) until no hand is inside the head and its bonnet.
        /// </summary>
        public static void Limit(Pose p, Pose safe)
        {
            for (int i = 1; i < Skeleton.N; i++)
            {
                Bone b = (Bone)i;
                float[] lim = LimitsOf(b);
                Vector3 e = p.Euler[i];
                if (float.IsNaN(e.X)) e.X = 0f;
                if (float.IsNaN(e.Y)) e.Y = 0f;
                if (float.IsNaN(e.Z)) e.Z = 0f;
                e.X = Clamp(e.X, lim[0], lim[1]);
                if (Mirrored(b)) { e.Y = -Clamp(-e.Y, lim[2], lim[3]); e.Z = -Clamp(-e.Z, lim[4], lim[5]); }
                else { e.Y = Clamp(e.Y, lim[2], lim[3]); e.Z = Clamp(e.Z, lim[4], lim[5]); }
                p.Euler[i] = e;
            }
            p.Euler[0] = Vector3.Zero;
            p.HipsOffset = new Vector3(Clamp(p.HipsOffset.X, -ShiftMax, ShiftMax), Clamp(p.HipsOffset.Y, LiftMin, LiftMax), Clamp(p.HipsOffset.Z, -ShiftMax, ShiftMax));
            p.Squash = Clamp(p.Squash, SquashMin, SquashMax);
            p.Mouth = Clamp(p.Mouth, 0f, 1f);
            p.Blink = Clamp(p.Blink, -0.5f, 1f);
            p.EyeX = Clamp(p.EyeX, -1f, 1f); p.EyeY = Clamp(p.EyeY, -1f, 1f);
            if (safe != null)
            {
                KeepHandOut(p, safe, Bone.ArmUL, Bone.ArmLL);
                KeepHandOut(p, safe, Bone.ArmUR, Bone.ArmLR);
            }
        }

        [ThreadStatic] static Vector3[] fkPos;
        [ThreadStatic] static Quaternion[] fkRot;

        /// <summary>
        /// How deep a hand is inside the head and its bonnet: under 1 = inside (the wrist or the fingertips, whichever
        /// is deeper), 1 = on the surface. The bonnet is taken a little larger than it is.
        /// </summary>
        public static float HandClearance(Pose p, bool right)
        {
            if (fkPos == null) { fkPos = new Vector3[Skeleton.N]; fkRot = new Quaternion[Skeleton.N]; }
            Skeleton.FK(p, fkPos, fkRot);
            int fore = (int)(right ? Bone.ArmLR : Bone.ArmLL), head = (int)Bone.Head;
            Vector3 centre = fkPos[head] + M.Rot(fkRot[head], new Vector3(0, Skeleton.HeadCenterUp, 0));
            Vector3 wrist = fkPos[fore], tip = fkPos[fore] + M.Rot(fkRot[fore], new Vector3(0, -Skeleton.ArmLower, 0));
            return Math.Min(Inside(wrist, centre), Inside(tip, centre));
        }

        static float Inside(Vector3 point, Vector3 centre)
        {
            float rx = Skeleton.HeadHalfWidth + 0.02f, ry = Skeleton.HeadRadius + 0.02f, rz = Skeleton.HeadRadius + 0.02f;
            Vector3 d = point - centre;
            return (float)Math.Sqrt(d.X * d.X / (rx * rx) + d.Y * d.Y / (ry * ry) + d.Z * d.Z / (rz * rz));
        }

        static void KeepHandOut(Pose p, Pose safe, Bone upper, Bone lower)
        {
            bool right = upper == Bone.ArmUR;
            if (HandClearance(p, right) >= 1f) return;
            Vector3 wantU = p.Euler[(int)upper], wantL = p.Euler[(int)lower];
            Vector3 safeU = safe.Euler[(int)upper], safeL = safe.Euler[(int)lower];
            float lo = 0f, hi = 1f;                     // how much of what was asked for is kept
            for (int i = 0; i < 8; i++)
            {
                float k = (lo + hi) * 0.5f;
                p.Euler[(int)upper] = Vector3.Lerp(safeU, wantU, k);
                p.Euler[(int)lower] = Vector3.Lerp(safeL, wantL, k);
                if (HandClearance(p, right) >= 1f) lo = k; else hi = k;
            }
            p.Euler[(int)upper] = Vector3.Lerp(safeU, wantU, lo);
            p.Euler[(int)lower] = Vector3.Lerp(safeL, wantL, lo);
        }

        // ------------------------------------------------------------------ the text for the model

        /// <summary>The two motions of the Doc, as a script (the tests evaluate exactly this).</summary>
        public const string Worked =
@"-- a baseball swing with the right hand: wind up, swing through, follow through
motion(""swing"", 1.0, function(u, p)
  local back = keys(u, {0, 0, 0.35, 1, 0.45, 1, 0.6, 0})   -- wound up
  local thru = keys(u, {0.4, 0, 0.6, 1, 0.8, 1, 1, 0})     -- through the ball and across
  local twist = 36 * back - 44 * thru                      -- + turns the body to its own right
  p.spine = {x = 10, y = twist * 0.4}
  p.chest = {y = twist * 0.6}
  p.head  = {y = -twist * 0.8}                             -- the eyes stay on the ball
  p.armR  = {x = 40 * back - 90 * thru, z = 10 + 40 * back - 20 * thru}
  p.foreR = {x = -12 - 10 * thru}
  p.armL  = {x = -30 + 15 * back - 50 * thru, z = -15 * back - 20 * thru}   -- the other hand comes along
  p.foreL = {x = -30}
  p.legL  = {x = -14 * thru}                               -- a small step into it
  p.squash = 0.10 * back - 0.18 * thru
end)

-- a jump for joy: gather, jump with the arms flung out, land
motion(""jump"", 0.9, function(u, p)
  local down = keys(u, {0, 0, 0.2, 1, 0.3, 0, 0.78, 0, 0.86, 1, 1, 0})   -- gathering, and landing
  local air  = keys(u, {0.22, 0, 0.5, 1, 0.8, 0})                        -- 0 on the ground .. 1 at the top
  p.lift   = 0.45 * air - 0.012 * down
  p.squash = 0.40 * down - 0.25 * air
  p.spine  = {x = 14 * down - 6 * air}
  p.head   = {x = 8 * down - 14 * air}
  p.armL   = {x = 25 * down, z = 10 + 55 * air}             -- out to the sides, never over the head
  p.armR   = {x = 25 * down, z = 10 + 55 * air}
  p.legL   = {x = -30 * down - 20 * air}
  p.legR   = {x = -30 * down - 20 * air}
  p.shinL  = {x = 55 * down + 45 * air}
  p.shinR  = {x = 55 * down + 45 * air}
  p.mouth  = air
  p.blink  = 0.7 * air
end)
";

        /// <summary>The motion API for the language model that writes scripts (games and things alike).</summary>
        public const string Doc =
@"MOTIONS - teach the partner (Bambi) a movement of its own.
motion(name, seconds, function(u, p) ... end [, loop])
  Defines a motion. While it plays, the function is called every frame with u = 0..1 (how far through; a loop
  wraps round) and p = an empty pose table to fill. Whatever you do not set comes from a built-in base pose.
  Define motions at the top of the script, not inside update.
THE BODY: a round little character, one body height tall, a very big head with a wide bonnet, short arms, very
short legs on big feet. It faces forward (+z); its own right is +x. Angles are DEGREES. Each bone is p.<bone> = {x=, y=, z=}
(leave out what you do not need). The LEFT side is mirrored: the same numbers on armL and armR give a symmetric pose.
  spine, chest   x: + bows forward, - leans back.  y: + turns to its own right, - to its left.  z: + leans to its left.
  head           x: + nods down, - looks up.  y: + turns right.  z: tilts.
  armL, armR     x: - lifts the arm forward (-90 = straight out in front), + swings it back.
                 z: + lifts it out to the side (55 = level with the shoulder), - brings it across the body.
  foreL, foreR   the hand end of the arm.  x: - bends it up towards the face.
  legL, legR     x: - lifts the thigh forward (-88 = sitting), + swings it back.  z: + out to the side.
  shinL, shinR   x: + bends the knee (0..125).
  footL, footR   x: - toes up, + toes down.
  hips           y: turns the whole body (360 = one full turn).  x, z: tips the whole body.
  antenna        the feeler on top of the head (not every character has one).
  p.lift   the whole body up (+) or down (-), in body heights: 0.4 = a good jump, -0.1 = squatting.
  p.shift  the body sideways, in body heights (-0.2 .. 0.2).      p.lean  degrees added to the bow of the spine.
  p.squash 0..0.6 pressed down and wide (gathering, landing), negative = stretched tall (-0.4).
  p.mouth 0..1 open,  p.blink 0..1 eyes shut (-0.5 = wide open),  p.look = {yaw=, pitch=} where it looks.
  p.base   the pose everything else comes from: idle (default) stand walk run sit crouch cheer think wave dance ready.
The runtime keeps every bone inside what this body can do and keeps the hands out of the bonnet: a hand cannot go
above the head - for joy the arms go OUT to the sides (z around 60), not up.
HELPERS for the timing:
  keys(u, {t0, v0, t1, v1, ...}) -> the value at u, moving smoothly from key to key (a value is a number or {x, y, z}).
  ease(u) -> 0..1 with a soft start and stop.   swing(u, a, b) -> from a to b, eased.
  pulse(u [, n]) -> 0 up to 1 and back to 0 (n times).   wave(u [, n]) -> a sine, -1..1, n whole swings.
TWO WORKED MOTIONS (imitate the style: a few named phases made with keys, then the bones):
" + Worked;
    }
}
