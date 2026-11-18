using System.Numerics;
using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests
{
    /// <summary>Motions defined by scripts (LuaMotion): the bone names, the limits, the helpers, and the two worked motions of the Doc.</summary>
    public class GlassScriptMotionTests
    {
        readonly ITestOutputHelper o;
        public GlassScriptMotionTests(ITestOutputHelper o) { this.o = o; }

        static (LuaSandbox box, LuaMotion m) Make(string src)
        {
            var box = new LuaSandbox(3);
            var m = new LuaMotion(box);
            Assert.True(box.Load(src), box.ErrorText());
            return (box, m);
        }

        static void Fk(Pose p, out Vector3[] pos, out Quaternion[] rot)
        {
            pos = new Vector3[Skeleton.N]; rot = new Quaternion[Skeleton.N];
            Skeleton.FK(p, pos, rot);
        }

        static Vector3 Hand(Pose p, bool right)
        {
            Fk(p, out var pos, out var rot);
            int fore = (int)(right ? Bone.ArmLR : Bone.ArmLL);
            return pos[fore] + M.Rot(rot[fore], new Vector3(0, -Skeleton.ArmLower, 0));
        }

        [Fact]
        public void Bone_names_mean_the_right_bones_and_the_left_side_is_mirrored()
        {
            Assert.Equal(Bone.Hips, LuaMotion.BoneOf("hips"));
            Assert.Equal(Bone.Spine, LuaMotion.BoneOf("spine"));
            Assert.Equal(Bone.Chest, LuaMotion.BoneOf("chest"));
            Assert.Equal(Bone.Head, LuaMotion.BoneOf("head"));
            Assert.Equal(Bone.ArmUL, LuaMotion.BoneOf("armL"));
            Assert.Equal(Bone.ArmLL, LuaMotion.BoneOf("foreL"));
            Assert.Equal(Bone.ArmUR, LuaMotion.BoneOf("armR"));
            Assert.Equal(Bone.ArmLR, LuaMotion.BoneOf("foreR"));
            Assert.Equal(Bone.LegUL, LuaMotion.BoneOf("legL"));
            Assert.Equal(Bone.LegLL, LuaMotion.BoneOf("shinL"));
            Assert.Equal(Bone.FootL, LuaMotion.BoneOf("footL"));
            Assert.Equal(Bone.LegUR, LuaMotion.BoneOf("legR"));
            Assert.Equal(Bone.LegLR, LuaMotion.BoneOf("shinR"));
            Assert.Equal(Bone.FootR, LuaMotion.BoneOf("footR"));
            Assert.Equal(Bone.Antenna, LuaMotion.BoneOf("antenna"));
            Assert.Equal(Bone.ArmLR, LuaMotion.BoneOf("handR"));
            Assert.Equal(Bone.Count, LuaMotion.BoneOf("tail"));
            Assert.Equal(LuaMotion.BoneNames.Length, new HashSet<string>(LuaMotion.BoneNames).Count);

            var (_, m) = Make("motion('t', 1, function(u, p) p.base = 'stand'\n p.armL = {x = -40, y = 10, z = 30}\n p.armR = {x = -40, y = 10, z = 30}\n p.legL = {z = 20}\n p.legR = {z = 20}\n p.head = {5, 6, 7}\n p.spine = 12 end)");
            var p = new Pose();
            Assert.True(m.Evaluate("t", 0.5f, p));
            Assert.Equal(new Vector3(-40, -10, -30), p[Bone.ArmUL]);
            Assert.Equal(new Vector3(-40, 10, 30), p[Bone.ArmUR]);
            Assert.Equal(-20f, p[Bone.LegUL].Z);
            Assert.Equal(20f, p[Bone.LegUR].Z);
            Assert.Equal(new Vector3(5, 6, 7), p[Bone.Head]);         // by position too
            Assert.Equal(12f, p[Bone.Spine].X);                        // a bare number is x
            // the same numbers left and right = a symmetric pose: the hands are mirror images
            Vector3 l = Hand(p, false), r = Hand(p, true);
            Assert.Equal(-l.X, r.X, 4); Assert.Equal(l.Y, r.Y, 4); Assert.Equal(l.Z, r.Z, 4);
            Assert.True(r.X > 0.2f && r.Z > 0.05f, r.ToString());      // out to the right and in front
        }

        [Fact]
        public void What_is_not_set_comes_from_the_base_pose()
        {
            var (_, m) = Make("motion('a', 2, function(u, p) p.armR = {z = 40} end)\nmotion('b', 2, function(u, p) p.base = 'sit' end)\nmotion('c', 2, function(u, p) p.base = 'nonsense' end)");
            var p = new Pose(); var idle = new Pose(); var sit = new Pose();
            Assert.True(m.Evaluate("a", 0.25f, 3.3f, p));
            PoseLib.Idle(idle, 3.3f);
            LuaMotion.Limit(idle, null);
            Assert.Equal(idle[Bone.Spine], p[Bone.Spine]);
            Assert.Equal(idle[Bone.ArmUR].X, p[Bone.ArmUR].X);         // an axis that was left out stays the base's
            Assert.Equal(40f, p[Bone.ArmUR].Z);
            Assert.True(m.Evaluate("b", 0.25f, 3.3f, p));
            PoseLib.Sit(sit, 3.3f);
            Assert.Equal(sit[Bone.LegUL].X, p[Bone.LegUL].X);
            Assert.True(p.HipsOffset.Y < -0.05f);
            Assert.True(m.Evaluate("c", 0.25f, 3.3f, p));             // an unknown base is idle, and noted
            Assert.Equal(idle[Bone.Spine], p[Bone.Spine]);
        }

        [Fact]
        public void Limits_clamp_every_bone_and_channel()
        {
            var (_, m) = Make(@"motion('wild', 1, function(u, p)
  p.base = 'stand'
  for _, b in ipairs({'hips','spine','chest','head','armL','foreL','armR','foreR','legL','shinL','footL','legR','shinR','footR','antenna'}) do p[b] = {x = 9999, y = -9999, z = 9999} end
  p.lift = 50  p.shift = -9  p.squash = 7  p.mouth = -3  p.blink = 12  p.lean = 5000
end)
motion('neg', 1, function(u, p)
  p.base = 'stand'
  for _, b in ipairs({'hips','spine','chest','head','armL','foreL','armR','foreR','legL','shinL','footL','legR','shinR','footR','antenna'}) do p[b] = {x = -9999, y = 9999, z = -9999} end
  p.lift = -50  p.squash = -7
end)");
            foreach (string name in new[] { "wild", "neg" })
            {
                var p = new Pose();
                Assert.True(m.Evaluate(name, 0.5f, p));
                for (int i = 1; i < Skeleton.N; i++)
                {
                    float[] lim = LuaMotion.LimitsOf((Bone)i);
                    float s = LuaMotion.Mirrored((Bone)i) ? -1f : 1f;
                    Vector3 e = p.Euler[i];
                    // (the arms may have been taken back further, towards the base, to keep the hands out of the bonnet)
                    Assert.InRange(e.X, lim[0], lim[1]);
                    Assert.InRange(e.Y * s, lim[2], lim[3]);
                    Assert.InRange(e.Z * s, lim[4], lim[5]);
                }
                Assert.InRange(p.HipsOffset.Y, LuaMotion.LiftMin, LuaMotion.LiftMax);
                Assert.InRange(p.HipsOffset.X, -LuaMotion.ShiftMax, LuaMotion.ShiftMax);
                Assert.InRange(p.Squash, LuaMotion.SquashMin, LuaMotion.SquashMax);
                Assert.InRange(p.Mouth, 0f, 1f);
                Assert.InRange(p.Blink, -0.5f, 1f);
                Assert.True(LuaMotion.HandClearance(p, true) >= 0.999f && LuaMotion.HandClearance(p, false) >= 0.999f);
            }
            var w = new Pose();
            m.Evaluate("wild", 0.5f, w);
            Assert.Equal(LuaMotion.LiftMax, w.HipsOffset.Y);
            Assert.Equal(125f, w[Bone.LegLL].X);
            Assert.Equal(50f, w[Bone.Spine].X);
        }

        [Fact]
        public void A_hand_cannot_go_into_the_bonnet()
        {
            // both arms straight up: that is inside the wide bonnet on this character
            var (_, m) = Make("motion('up', 1, function(u, p) p.base = 'stand'\n p.armL = {z = 125}\n p.armR = {z = 125} end)\nmotion('side', 1, function(u, p) p.base = 'stand'\n p.armL = {z = 55}\n p.armR = {z = 55} end)");
            var raw = new Pose();
            raw[Bone.ArmUR].Z = 125f; raw[Bone.ArmUL].Z = -125f;
            Assert.True(LuaMotion.HandClearance(raw, true) < 1f);      // the pose as asked for IS inside
            var p = new Pose();
            Assert.True(m.Evaluate("up", 0f, p));
            Assert.True(LuaMotion.HandClearance(p, true) >= 0.999f);
            Assert.True(LuaMotion.HandClearance(p, false) >= 0.999f);
            Assert.True(p[Bone.ArmUR].Z < 125f && p[Bone.ArmUR].Z > 30f, p[Bone.ArmUR].ToString());     // taken back, but still raised as far as it can be
            Assert.Equal(-p[Bone.ArmUL].Z, p[Bone.ArmUR].Z, 3);
            // arms out to the sides are left exactly as asked
            Assert.True(m.Evaluate("side", 0f, p));
            Assert.Equal(55f, p[Bone.ArmUR].Z);
            Assert.Equal(-55f, p[Bone.ArmUL].Z);
        }

        [Fact]
        public void Keys_and_the_other_helpers()
        {
            var box = new LuaSandbox(1);
            new LuaMotion(box);
            Assert.True(box.Load(@"
k = {0, 10, 0.5, 20, 1, 0}
a0 = keys(-1, k)  a1 = keys(0, k)  a2 = keys(0.25, k)  a3 = keys(0.5, k)  a4 = keys(0.75, k)  a5 = keys(1, k)  a6 = keys(7, k)
q = keys(0.125, k)
v = keys(0.5, {0, {x = 0, z = 10}, 1, {x = 40, z = 30}})
w = keys(0.5, {0, {1, 2, 3}, 1, {3, 4, 5}})
bad1 = keys('x', k)  bad2 = keys(0.5, 'nope')  bad3 = keys(0.5, {})  bad4 = keys(0.5, {0, 'a', 1})
e0 = ease(-2)  e1 = ease(0.5)  e2 = ease(3)  e3 = ease(0.25)
s = swing(0.5, 10, 30)
p0 = pulse(0)  p1 = pulse(0.5)  p2 = pulse(1)  p3 = pulse(0.25, 2)
w1 = wave(0.25)  w2 = wave(0.75)  w3 = wave(0.125, 2)
"), box.ErrorText());
            double N(string n) => box.Get(n).Number;
            Assert.Equal(10, N("a0")); Assert.Equal(10, N("a1")); Assert.Equal(15, N("a2"), 5); Assert.Equal(20, N("a3")); Assert.Equal(10, N("a4"), 5); Assert.Equal(0, N("a5")); Assert.Equal(0, N("a6"));
            Assert.Equal(10 + 10 * (0.25 * 0.25 * (3 - 0.5)), N("q"), 4);          // smooth, not linear: slow out of the key
            Assert.Equal(20, box.Get("v").Table.Get("x").Number, 5);
            Assert.Equal(20, box.Get("v").Table.Get("z").Number, 5);
            Assert.True(box.Get("v").Table.Get("y").IsNil());                       // an axis no key sets stays unset (= the base pose's)
            Assert.Equal(2, box.Get("w").Table.Get("x").Number, 5);
            Assert.Equal(4, box.Get("w").Table.Get("z").Number, 5);
            Assert.Equal(10, N("bad1")); Assert.Equal(0, N("bad2")); Assert.Equal(0, N("bad3")); Assert.Equal(0, N("bad4"));
            Assert.Equal(0, N("e0")); Assert.Equal(0.5, N("e1"), 5); Assert.Equal(1, N("e2")); Assert.Equal(0.15625, N("e3"), 5);
            Assert.Equal(20, N("s"), 5);
            Assert.Equal(0, N("p0"), 5); Assert.Equal(1, N("p1"), 5); Assert.Equal(0, N("p2"), 5); Assert.Equal(1, N("p3"), 5);
            Assert.Equal(1, N("w1"), 5); Assert.Equal(-1, N("w2"), 5); Assert.Equal(1, N("w3"), 5);
        }

        [Fact]
        public void A_motion_is_deterministic_and_loops_wrap()
        {
            string src = LuaMotion.Worked + "\nmotion('flap', 0.5, function(u, p) p.armL = {z = 30 + 20 * wave(u)}\n p.armR = {z = 30 + 20 * wave(u)} end, true)";
            var (_, m1) = Make(src);
            var (_, m2) = Make(src);
            Assert.Equal(3, m1.Count);
            Assert.True(m1.Has("swing") && m1.Has("JUMP") && m1.Has("flap") && !m1.Has("nothing"));
            Assert.Equal(1.0f, m1.Seconds("swing")); Assert.Equal(0.9f, m1.Seconds("jump")); Assert.Equal(0f, m1.Seconds("nothing"));
            Assert.False(m1.Loops("swing")); Assert.True(m1.Loops("flap"));
            var a = new Pose(); var b = new Pose();
            for (int i = 0; i <= 40; i++)
            {
                float u = i / 40f;
                foreach (string name in new[] { "swing", "jump", "flap" })
                {
                    Assert.True(m1.Evaluate(name, u, 1.7f, a));
                    Assert.True(m2.Evaluate(name, u, 1.7f, b));
                    Assert.True(m1.Evaluate(name, u, 1.7f, a));           // and again: asking twice changes nothing
                    for (int k = 0; k < Skeleton.N; k++) Assert.Equal(a.Euler[k], b.Euler[k]);
                    Assert.Equal(a.HipsOffset, b.HipsOffset); Assert.Equal(a.Squash, b.Squash); Assert.Equal(a.Mouth, b.Mouth);
                }
            }
            // a loop wraps: 1.25 is 0.25; a one-shot holds its end
            m1.Evaluate("flap", 1.25f, 0f, a); m1.Evaluate("flap", 0.25f, 0f, b);
            Assert.Equal(a[Bone.ArmUR], b[Bone.ArmUR]);
            Assert.Equal(50f, a[Bone.ArmUR].Z, 3);
            m1.Evaluate("swing", 5f, 0f, a); m1.Evaluate("swing", 1f, 0f, b);
            Assert.Equal(a[Bone.ArmUR], b[Bone.ArmUR]);
            m1.Evaluate("swing", float.NaN, 0f, a); m1.Evaluate("swing", 0f, 0f, b);
            Assert.Equal(a[Bone.ArmUR], b[Bone.ArmUR]);
            // no such motion: false, and the pose is the idle pose (never garbage)
            Assert.False(m1.Evaluate("nothing", 0.5f, 2f, a));
            PoseLib.Idle(b, 2f);
            Assert.Equal(b[Bone.Spine], a[Bone.Spine]);
        }

        [Fact]
        public void The_worked_swing_takes_the_right_hand_from_behind_the_shoulder_across_the_front()
        {
            var (box, m) = Make(LuaMotion.Worked);
            var p = new Pose();
            Vector3 start = default, wound = default, contact = default, follow = default, end = default;
            float maxClear = 9f;
            for (int i = 0; i <= 100; i++)
            {
                float u = i / 100f;
                Assert.True(m.Evaluate("swing", u, 0f, p), box.ErrorText());
                Vector3 h = Hand(p, true);
                maxClear = Math.Min(maxClear, Math.Min(LuaMotion.HandClearance(p, true), LuaMotion.HandClearance(p, false)));
                if (i == 0) start = h; if (i == 40) wound = h; if (i == 52) contact = h; if (i == 70) follow = h; if (i == 100) end = h;
            }
            Fk(p, out var pos, out _);
            o.WriteLine($"swing: start {start}  wound {wound}  contact {contact}  follow {follow}  end {end}");
            Vector3 shoulder = pos[(int)Bone.ArmUR];
            Assert.True(wound.Z < shoulder.Z - 0.04f, "wound up: the hand is behind the shoulder " + wound);
            Assert.True(wound.X > 0.15f, "wound up: on its right side " + wound);
            Assert.True(follow.Z > 0.08f, "follow through: in front " + follow);
            Assert.True(follow.X < wound.X - 0.20f, "it travelled across the front " + wound + " -> " + follow);
            Assert.True(contact.Z > wound.Z + 0.08f, "through the ball: forwards " + contact);
            Assert.True(Vector3.Distance(start, end) < 0.03f, "it comes back to where it started");
            Assert.True(maxClear >= 0.999f, "no hand in the bonnet");
            // the body turns right to wind up and left to follow through; the head turns against it
            m.Evaluate("swing", 0.4f, 0f, p);
            Assert.True(p[Bone.Spine].Y + p[Bone.Chest].Y > 25f && p[Bone.Head].Y < -15f);
            m.Evaluate("swing", 0.7f, 0f, p);
            Assert.True(p[Bone.Spine].Y + p[Bone.Chest].Y < -30f && p[Bone.Head].Y > 15f);
        }

        [Fact]
        public void The_worked_jump_leaves_the_ground_and_lands_again()
        {
            var (box, m) = Make(LuaMotion.Worked);
            var p = new Pose();
            float FootY(float u, out float handAboveShoulder)
            {
                Assert.True(m.Evaluate("jump", u, 0f, p), box.ErrorText());
                Fk(p, out var pos, out _);
                handAboveShoulder = Hand(p, true).Y - pos[(int)Bone.ArmUR].Y;
                Assert.True(LuaMotion.HandClearance(p, true) >= 0.999f && LuaMotion.HandClearance(p, false) >= 0.999f);
                return Math.Min(pos[(int)Bone.FootL].Y, pos[(int)Bone.FootR].Y);
            }
            float ground = FootY(0f, out _), top = FootY(0.5f, out float handTop), landed = FootY(1f, out _), gather = FootY(0.2f, out _);
            o.WriteLine($"jump: foot y at 0 {ground:0.000}, gathering {gather:0.000}, top {top:0.000}, end {landed:0.000}; hand above shoulder at the top {handTop:0.000}");
            Assert.InRange(ground, Skeleton.AnkleHeight - 0.02f, Skeleton.AnkleHeight + 0.02f);
            Assert.InRange(landed, Skeleton.AnkleHeight - 0.02f, Skeleton.AnkleHeight + 0.02f);
            Assert.InRange(gather, Skeleton.AnkleHeight - 0.015f, Skeleton.AnkleHeight + 0.015f);      // gathering: the feet stay on the floor
            Assert.True(top > ground + 0.35f, "it jumps: " + top);
            Assert.True(handTop < 0.06f, "the hands stay about level with the shoulders: " + handTop);
            // the arms are flung out at the top, symmetric
            Assert.True(p[Bone.ArmUR].Z < 20f);
            m.Evaluate("jump", 0.5f, 0f, p);
            Assert.Equal(65f, p[Bone.ArmUR].Z, 2); Assert.Equal(-65f, p[Bone.ArmUL].Z, 2);
            Assert.Equal(1f, p.Mouth, 3);
        }

        [Fact]
        public void Fifty_motions_and_garbage_break_nothing()
        {
            var (box, m) = Make(@"
motion('garbage', 1, function(u, p)
  p.armR = 'up'  p.armL = {x = 0/0, y = 1/0, z = -1/0}  p.head = {x = 'a'}  p.spine = true  p.chest = function() end
  p.lift = 0/0  p.squash = 'big'  p.look = 5  p.base = 42  p[1] = 3  p.nothing = {x = 1}
  return 17
end)
motion('returns', 1, function(u) return {armR = {x = -50}, lift = 0.2} end)
motion('short', function(u, p) p.lift = 0.1 end)
motion('long', 99999, function(u, p) end)
for i = 1, 50 do motion('m' .. i, 1, function(u, p) p.armR = {x = -i} end) end
motion('m3', 2, function(u, p) p.armR = {x = -77} end)
");
            Assert.Equal(LuaMotion.MaxMotions, m.Count);
            Assert.True(m.Has("m20") && !m.Has("m21") && !m.Has("m50"));
            Assert.Contains(box.Notes, n => n.Contains("more than"));
            Assert.Equal(1f, m.Seconds("short")); Assert.Equal(LuaMotion.MaxSeconds, m.Seconds("long"));
            var p = new Pose(); var idle = new Pose();
            PoseLib.Idle(idle, 1f); LuaMotion.Limit(idle, null);
            Assert.True(m.Evaluate("garbage", 0.5f, 1f, p));
            for (int i = 0; i < Skeleton.N; i++) Assert.Equal(idle.Euler[i], p.Euler[i]);      // everything it said was nonsense: the base pose
            Assert.Equal(idle.HipsOffset, p.HipsOffset); Assert.Equal(idle.Squash, p.Squash);
            Assert.True(m.Evaluate("returns", 0.5f, 1f, p));
            Assert.Equal(-50f, p[Bone.ArmUR].X); Assert.Equal(0.2f, p.HipsOffset.Y);
            Assert.True(m.Evaluate("m3", 0.5f, 1f, p));
            Assert.Equal(-77f, p[Bone.ArmUR].X);                                               // defined again: replaced
            Assert.Equal(2f, m.Seconds("m3"));
            Assert.False(box.Broken);
            Assert.True(box.PeakInstructions <= LuaSandbox.Slice * 2, "a motion is cheap: " + box.PeakInstructions);

            // wrong arguments are an error with a line, like everywhere
            var b1 = new LuaSandbox(1); new LuaMotion(b1);
            Assert.False(b1.Load("\nmotion()"));
            Assert.Equal(2, b1.ErrorLine); Assert.Contains("name", b1.Error);
            var b2 = new LuaSandbox(1); new LuaMotion(b2);
            Assert.False(b2.Load("motion('a', 1)"));
            Assert.Contains("function", b2.Error);

            // a motion that throws, and one that never ends: the script stops, the pose is the idle pose, nothing hangs
            var (b3, m3) = Make("motion('boom', 1, function(u, p) p.armR = nil + 1 end)");
            Assert.False(m3.Evaluate("boom", 0.5f, 1f, p));
            Assert.True(b3.Broken); Assert.Equal(1, b3.ErrorLine);
            for (int i = 0; i < Skeleton.N; i++) Assert.Equal(idle.Euler[i].X, p.Euler[i].X, 3);
            Assert.False(m3.Evaluate("boom", 0.6f, 1f, p));
            var (b4, m4) = Make("motion('never', 1, function(u, p) while true do end end)");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(m4.Evaluate("never", 0.5f, 1f, p));
            Assert.Equal(LuaSandbox.TooLong, b4.Error);
            Assert.True(b4.LastInstructions <= LuaMotion.Budget, b4.LastInstructions.ToString());
            Assert.True(sw.ElapsedMilliseconds < 500);
        }
    }
}
