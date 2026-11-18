using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>Bones of the shared partner rig. The same indices are used by the Quest and Windows renderers.</summary>
    public enum Bone
    {
        Root = 0, Hips, Spine, Chest, Head,
        ArmUL, ArmLL, ArmUR, ArmLR,
        LegUL, LegLL, FootL, LegUR, LegLR, FootR,
        Antenna,
        Count
    }

    /// <summary>
    /// Rig definition in "character units": total standing height = 1, feet on y=0, facing +Z, right = +X.
    /// The model skinner (Unity) and the FK used by tests (.NET) both read this single definition.
    /// The joints are measured on the owner's character model (Art/Characters/Tripo): a head that is more than half
    /// of the height, a round body that sits a little forward of the head, short arms and very short legs on big feet.
    /// </summary>
    public static class Skeleton
    {
        public const int N = (int)Bone.Count;
        public const float LegUpper = 0.05f, LegLower = 0.05f, LegLen = LegUpper + LegLower;   // the thigh is inside the body: only a stub and the foot show
        public const float ArmUpper = 0.08f, ArmLower = 0.08f;                                 // shoulder -> wrist (sleeve and cuff), wrist -> fingertips (hand)
        public const float HeadCenterUp = 0.29f, HeadRadius = 0.29f;                           // from the neck joint; the bonnet is 0.33 wide to each side
        public const float HeadHalfWidth = 0.33f;
        public const float HipsHeight = 0.17f;
        public const float AnkleHeight = 0.05f;
        /// <summary>How far the arms stand out from the body when a pose leaves them alone (degrees from straight down).</summary>
        public const float ArmRestSplay = 35f;

        public static readonly int[] Parent = { -1, 0, 1, 2, 3, 3, 5, 3, 7, 1, 9, 10, 1, 12, 13, 4 };

        public static readonly Vector3[] RestLocal =
        {
            new Vector3(0, 0, 0),              // Root
            new Vector3(0, HipsHeight, 0.035f),// Hips (the body is in front of the head's axis)
            new Vector3(0, 0.05f, 0),          // Spine
            new Vector3(0, 0.08f, 0),          // Chest
            new Vector3(0, 0.12f, -0.03f),     // Head (neck joint)
            new Vector3(-0.195f, 0.075f, 0.005f), // ArmUL
            new Vector3(0, -ArmUpper, 0),      // ArmLL
            new Vector3(0.195f, 0.075f, 0.005f),  // ArmUR
            new Vector3(0, -ArmUpper, 0),      // ArmLR
            new Vector3(-0.104f, -0.02f, 0),   // LegUL
            new Vector3(0, -LegUpper, 0),      // LegLL
            new Vector3(0, -LegLower, 0),      // FootL
            new Vector3(0.104f, -0.02f, 0),    // LegUR
            new Vector3(0, -LegUpper, 0),      // LegLR
            new Vector3(0, -LegLower, 0),      // FootR
            new Vector3(0, HeadCenterUp + HeadRadius - 0.01f, 0), // Antenna base (no geometry on the current model)
        };

        /// <summary>
        /// Rest rotation of each bone (Euler degrees), applied before the pose's own rotation. Only the upper arms have
        /// one: they stand out from the round body, so a pose angle of zero does not push the hands into the belly.
        /// </summary>
        public static readonly Vector3[] RestEuler = MakeRestEuler();
        static readonly Quaternion[] RestRot = MakeRestRot();
        static Vector3[] MakeRestEuler()
        {
            var r = new Vector3[N];
            r[(int)Bone.ArmUL] = new Vector3(0, 0, -ArmRestSplay);
            r[(int)Bone.ArmUR] = new Vector3(0, 0, ArmRestSplay);
            return r;
        }
        static Quaternion[] MakeRestRot()
        {
            var r = new Quaternion[N];
            for (int i = 0; i < N; i++) r[i] = M.Euler(RestEuler[i]);
            return r;
        }

        /// <summary>Body sample points (bone, local offset in character units) used to measure how much of the body is through a plane.</summary>
        public static readonly (Bone bone, Vector3 local, string part)[] Samples =
        {
            (Bone.Head, new Vector3(0, HeadCenterUp + HeadRadius, 0), "head"),
            (Bone.Head, new Vector3(0, HeadCenterUp, 0), "head"),
            (Bone.Head, new Vector3(0, 0.02f, 0), "head"),
            (Bone.Chest, new Vector3(0, 0.05f, 0), "torso"),
            (Bone.Spine, new Vector3(0, 0.03f, 0), "torso"),
            (Bone.Hips, new Vector3(0, 0, 0), "torso"),
            (Bone.LegUL, new Vector3(0, -LegUpper * 0.5f, 0), "legs"),
            (Bone.LegUR, new Vector3(0, -LegUpper * 0.5f, 0), "legs"),
            (Bone.LegLL, new Vector3(0, -LegLower * 0.5f, 0), "legs"),
            (Bone.LegLR, new Vector3(0, -LegLower * 0.5f, 0), "legs"),
            (Bone.FootL, new Vector3(0, -AnkleHeight * 0.6f, 0.03f), "feet"),
            (Bone.FootR, new Vector3(0, -AnkleHeight * 0.6f, 0.03f), "feet"),
        };

        /// <summary>Forward kinematics in character units. outPos/outRot are in the character's root space (before root transform).</summary>
        public static void FK(Pose pose, Vector3[] outPos, Quaternion[] outRot)
        {
            for (int i = 0; i < N; i++)
            {
                var localRot = M.Mul(RestRot[i], M.Euler(pose.Euler[i]));
                var localPos = RestLocal[i];
                if (i == (int)Bone.Hips) localPos += pose.HipsOffset;
                int p = Parent[i];
                if (p < 0) { outPos[i] = localPos; outRot[i] = localRot; }
                else
                {
                    outPos[i] = outPos[p] + M.Rot(outRot[p], localPos);
                    outRot[i] = M.Mul(outRot[p], localRot);
                }
            }
        }
    }

    /// <summary>A full-body pose: per-bone local Euler angles (degrees, Unity order) plus face/expressive channels.</summary>
    public sealed class Pose
    {
        public readonly Vector3[] Euler = new Vector3[Skeleton.N];
        public Vector3 HipsOffset;   // character units
        public float Blink;          // 0 open .. 1 closed (negative = widened)
        public float Mouth;          // 0 closed .. 1 open
        public float EyeX, EyeY;     // -1..1 gaze offset
        public float Squash;         // 0..1 landing squash

        public void Clear()
        {
            for (int i = 0; i < Skeleton.N; i++) Euler[i] = Vector3.Zero;
            HipsOffset = Vector3.Zero; Blink = 0; Mouth = 0; EyeX = 0; EyeY = 0; Squash = 0;
        }

        public void CopyFrom(Pose o)
        {
            Array.Copy(o.Euler, Euler, Skeleton.N);
            HipsOffset = o.HipsOffset; Blink = o.Blink; Mouth = o.Mouth; EyeX = o.EyeX; EyeY = o.EyeY; Squash = o.Squash;
        }

        public static void Lerp(Pose a, Pose b, float t, Pose o)
        {
            t = M.Clamp01(t);
            for (int i = 0; i < Skeleton.N; i++) o.Euler[i] = Vector3.Lerp(a.Euler[i], b.Euler[i], t);
            o.HipsOffset = Vector3.Lerp(a.HipsOffset, b.HipsOffset, t);
            o.Blink = M.Lerp(a.Blink, b.Blink, t); o.Mouth = M.Lerp(a.Mouth, b.Mouth, t);
            o.EyeX = M.Lerp(a.EyeX, b.EyeX, t); o.EyeY = M.Lerp(a.EyeY, b.EyeY, t);
            o.Squash = M.Lerp(a.Squash, b.Squash, t);
        }

        public ref Vector3 this[Bone b] => ref Euler[(int)b];
    }

    /// <summary>
    /// Deterministic procedural pose library. Every function is a pure function of its arguments, so two
    /// processes that evaluate the same motion at the same shared time produce the same pose without streaming bones.
    /// The angles suit the character's build: its arms are short and its bonnet is wide, so the hands never go above
    /// the shoulders (they would disappear into the bonnet) and the wrist - the "lower arm" bone - bends only a little.
    /// </summary>
    public static class PoseLib
    {
        const float TwoPi = (float)(Math.PI * 2.0);

        /// <summary>
        /// Standing. A round little body shows that it is alive by breathing with its whole shape, shifting its weight,
        /// letting the arms drift, and now and then tapping a foot - not by standing like a statue.
        /// </summary>
        public static void Idle(Pose p, float t)
        {
            p.Clear();
            float breath = M.Sin(t * 2.2f);
            float sway = M.Sin(t * 0.62f);                                   // the weight drifts slowly from one side to the other
            p[Bone.Spine] = new Vector3(1.5f + 1.5f * breath, 0, 2.4f * sway);
            p[Bone.Chest].X = 1.0f * breath;
            p[Bone.Head] = new Vector3(-2f + 1.4f * M.Sin(t * 1.1f), 4f * M.Sin(t * 0.7f), -1.6f * sway + 2f * M.Sin(t * 0.53f));   // the head stays level over the swaying body
            p[Bone.ArmUL] = new Vector3(5f * M.Sin(t * 0.9f), 0, -10f - 2.5f * breath - 3f * sway);
            p[Bone.ArmUR] = new Vector3(5f * M.Sin(t * 0.9f + 1.7f), 0, 10f + 2.5f * breath - 3f * sway);
            p[Bone.ArmLL].X = -12f + 4f * M.Sin(t * 1.3f); p[Bone.ArmLR].X = -12f + 4f * M.Sin(t * 1.3f + 2.1f);
            p.Squash = 0.11f * (0.5f - 0.5f * breath);                        // breathing in rises, breathing out settles (the feet stay on the ground)
            // every few seconds the toes of one foot tap twice
            float tap = M.Frac(t / 6.4f);
            if (tap > 0.84f) p[Bone.FootR].X = -20f * Math.Max(0f, M.Sin((tap - 0.84f) / 0.16f * TwoPi * 2f));
            p[Bone.Antenna] = new Vector3(6f * M.Sin(t * 1.9f), 0, 5f * M.Sin(t * 1.3f));
            p.Blink = BlinkAt(t);
        }

        public static float BlinkAt(float t)
        {
            float tb = M.Frac(t / 3.7f);
            return tb > 0.96f ? M.Sin((tb - 0.96f) / 0.04f * (float)Math.PI) : 0f;
        }

        /// <summary>Distance (character units) the root must advance per gait cycle for zero stance-foot slip.</summary>
        public static float StrideLength(float ampDeg) => 4f * Skeleton.LegLen * M.Sin(ampDeg * M.Deg2Rad);

        static void Leg(float phase, float ampDeg, float kneeLift, out float thigh, out float knee, out float zf)
        {
            float ph = M.Frac(phase);
            float sinA = M.Sin(ampDeg * M.Deg2Rad);
            float lift = 0f;
            if (ph < 0.5f) zf = sinA * (1f - 4f * ph);                         // stance: linear so a constant root speed has no slip
            else { float u = (ph - 0.5f) / 0.5f; zf = sinA * (-M.Cos((float)Math.PI * u)); lift = M.Sin((float)Math.PI * u); }
            thigh = -(float)Math.Asin(M.Clamp(zf, -1f, 1f)) * M.Rad2Deg;
            knee = kneeLift * lift;
            thigh -= knee * 0.45f; // lift the knee forward while it bends
        }

        /// <summary>
        /// Walk/run gait. phase is in cycles (distance / StrideLength). The legs are very short, so the step is shown by
        /// the whole body: it rocks over the planted foot (waddle, degrees), dips as a foot lands and stretches as it
        /// pushes off (bounce, as squash - the feet stay on the ground), and the arms pump. Only the legs and the hips
        /// decide where the feet are, so the planted foot does not slide.
        /// </summary>
        public static void Gait(Pose p, float phase, float ampDeg, float kneeLift, float lean, float armSwing, float waddle, float bounce)
        {
            p.Clear();
            Leg(phase, ampDeg, kneeLift, out float thL, out float knL, out float zfL);
            Leg(phase + 0.5f, ampDeg, kneeLift, out float thR, out float knR, out float zfR);
            p[Bone.LegUL].X = thL; p[Bone.LegLL].X = knL; p[Bone.FootL].X = -(thL + knL) * 0.85f;
            p[Bone.LegUR].X = thR; p[Bone.LegLR].X = knR; p[Bone.FootR].X = -(thR + knR) * 0.85f;
            float zStance = M.Frac(phase) < 0.5f ? zfL : zfR;
            p.HipsOffset.Y = -Skeleton.LegLen * (1f - (float)Math.Sqrt(Math.Max(0.0, 1.0 - zStance * zStance)));
            float c = M.Cos(TwoPi * phase), rock = M.Sin(TwoPi * phase);     // rock > 0 while the left foot is planted: lean to the left
            // twist and rock above the pelvis only, so the planted foot does not swing
            p[Bone.Spine] = new Vector3(lean, 4f * c, waddle * rock);
            p[Bone.Chest].Y = -10f * c;
            p[Bone.Head] = new Vector3(-lean * 0.6f + 2.5f * M.Sin(TwoPi * phase * 2f), 0, -waddle * 0.8f * rock);     // the head stays almost level and nods with each step
            p[Bone.ArmUL] = new Vector3(armSwing * c, 0, -8f - 5f * Math.Max(0f, rock)); p[Bone.ArmUR] = new Vector3(-armSwing * c, 0, 8f + 5f * Math.Max(0f, -rock));
            p[Bone.ArmLL].X = -22f - 14f * Math.Max(0f, -c); p[Bone.ArmLR].X = -22f - 14f * Math.Max(0f, c);
            p.Squash = bounce * M.Sin(TwoPi * phase * 2f);                   // each step: down just after the foot lands, up as it pushes off
            p[Bone.Antenna].X = -6f * M.Sin(TwoPi * phase * 2f);
        }

        public const float WalkAmp = 26f, RunAmp = 40f;
        public static void Walk(Pose p, float phase) => Gait(p, phase, WalkAmp, 90f, 4f, 36f, 5.5f, 0.30f);
        public static void Run(Pose p, float phase) => Gait(p, phase, RunAmp, 110f, 16f, 58f, 7f, 0.45f);

        /// <summary>
        /// Added on top of a standing pose while the body turns on the spot: the feet come off the ground in turn, so a
        /// turn is made of small steps instead of the feet twisting on the floor. lift is 0..1.
        /// </summary>
        public static void StepInPlace(Pose p, float phase, float lift)
        {
            float w = M.Sin(TwoPi * phase), l = Math.Max(0f, w) * lift, r = Math.Max(0f, -w) * lift;
            p[Bone.LegUL].X += -55f * l; p[Bone.LegLL].X += 110f * l; p[Bone.FootL].X += -55f * l;
            p[Bone.LegUR].X += -55f * r; p[Bone.LegLR].X += 110f * r; p[Bone.FootR].X += -55f * r;
            p[Bone.Spine].Z += 4f * (r - l);                                 // lean over the foot that is standing
            p[Bone.Head].Z += -3f * (r - l);
            p.Squash += 0.16f * lift * Math.Abs(M.Cos(TwoPi * phase));       // settle as each foot comes down
        }

        public static void Crouch(Pose p, float a)
        {
            p.Clear();
            a = M.Clamp01(a);
            float th = 60f * a, sh = 45f * a;
            p[Bone.LegUL].X = -th; p[Bone.LegUR].X = -th;
            p[Bone.LegLL].X = th + sh; p[Bone.LegLR].X = th + sh;
            p[Bone.FootL].X = -sh; p[Bone.FootR].X = -sh;
            float drop = Skeleton.LegUpper * (1f - M.Cos(th * M.Deg2Rad)) + Skeleton.LegLower * (1f - M.Cos(sh * M.Deg2Rad));
            float fz = Skeleton.LegUpper * M.Sin(th * M.Deg2Rad) - Skeleton.LegLower * M.Sin(sh * M.Deg2Rad);
            p.HipsOffset = new Vector3(0, -drop, -fz);
            p[Bone.Spine].X = 28f * a; p[Bone.Head].X = -22f * a;
            p[Bone.ArmUL] = new Vector3(45f * a, 0, -10f); p[Bone.ArmUR] = new Vector3(45f * a, 0, 10f);
            p[Bone.ArmLL].X = -20f * a; p[Bone.ArmLR].X = -20f * a;
            p.Squash = 0.38f * a;                                            // gathering itself: the whole body presses down before a jump
        }

        public static void Dive(Pose p)
        {
            p.Clear();
            p[Bone.ArmUL] = new Vector3(32f, 0, -18f); p[Bone.ArmUR] = new Vector3(32f, 0, 18f);     // swept back like small wings
            p[Bone.ArmLL].X = 8f; p[Bone.ArmLR].X = 8f;
            p[Bone.Spine].X = -4f; p[Bone.Chest].X = -4f; p[Bone.Head].X = -10f;
            p[Bone.LegUL].X = 6f; p[Bone.LegUR].X = 6f; p[Bone.LegLL].X = 4f; p[Bone.LegLR].X = 4f;
            p[Bone.FootL].X = 55f; p[Bone.FootR].X = 55f;
            p[Bone.Antenna].X = 35f;
            p.Blink = -0.2f;
            p.Squash = -0.32f;                                               // stretched along the line of flight
        }

        public static void Tuck(Pose p)
        {
            p.Clear();
            p[Bone.LegUL].X = -115f; p[Bone.LegUR].X = -115f; p[Bone.LegLL].X = 125f; p[Bone.LegLR].X = 125f;
            p[Bone.FootL].X = -10f; p[Bone.FootR].X = -10f;
            p[Bone.Spine].X = 35f; p[Bone.Chest].X = 15f; p[Bone.Head].X = 20f;
            p[Bone.ArmUL] = new Vector3(-75f, 0, 0); p[Bone.ArmUR] = new Vector3(-75f, 0, 0);
            p[Bone.ArmLL].X = -40f; p[Bone.ArmLR].X = -40f;
            p.Squash = 0.22f;                                                // rolled up into a ball
        }

        public static void Land(Pose p, float a)
        {
            Crouch(p, a);
            p[Bone.ArmUL] = new Vector3(10f * a, 0, -5f - 45f * a); p[Bone.ArmUR] = new Vector3(10f * a, 0, 5f + 45f * a);
            p.Squash = 0.55f * a;
        }

        public static void Hang(Pose p)
        {
            p.Clear();
            p[Bone.ArmUL] = new Vector3(-115f, 0, -20f); p[Bone.ArmUR] = new Vector3(-115f, 0, 20f);   // holding on in front of the chin
            p[Bone.LegUL].X = -8f; p[Bone.LegUR].X = -4f; p[Bone.LegLL].X = 15f; p[Bone.LegLR].X = 10f;
            p[Bone.Head].X = -30f;
        }

        public static void PullUp(Pose p, float a)
        {
            p.Clear();
            a = M.Clamp01(a);
            p[Bone.ArmUL] = new Vector3(M.Lerp(-115f, -60f, a), 0, -20f); p[Bone.ArmUR] = new Vector3(M.Lerp(-115f, -60f, a), 0, 20f);
            p[Bone.ArmLL].X = M.Lerp(0f, -50f, a); p[Bone.ArmLR].X = M.Lerp(0f, -50f, a);
            p[Bone.LegUL].X = -70f * a; p[Bone.LegLL].X = 90f * a;
            p[Bone.LegUR].X = -10f * a; p[Bone.LegLR].X = 20f * a;
            p[Bone.Head].X = -30f + 20f * a;
        }

        public static void Wave(Pose p, float t)
        {
            Idle(p, t);
            p[Bone.ArmUR] = new Vector3(-10f, 0, 62f + 8f * M.Sin(t * 11f));
            p[Bone.ArmLR] = new Vector3(0, 0, 28f * M.Sin(t * 11f) + 12f);
            p[Bone.Head].Z = 7f; p.Mouth = 0.6f;
        }

        public static void Shiver(Pose p, float t)
        {
            Crouch(p, 0.35f);
            p[Bone.Spine].X = 22f; p[Bone.Head].X = 14f;
            p[Bone.ArmUL] = new Vector3(-45f, 0, 28f); p[Bone.ArmUR] = new Vector3(-45f, 0, -28f);
            p[Bone.ArmLL].X = -50f; p[Bone.ArmLR].X = -50f;
            p[Bone.Hips].Y = 2.5f * M.Sin(t * 38f);
            p[Bone.Chest].Z = 1.5f * M.Sin(t * 41f);
            p.Blink = 0.55f; p.Mouth = 0.15f;
        }

        public static void Cower(Pose p, float t)
        {
            Crouch(p, 0.6f);
            p[Bone.ArmUL] = new Vector3(-105f, 0, 16f); p[Bone.ArmUR] = new Vector3(-105f, 0, -16f);     // hands up in front of the face
            p[Bone.ArmLL].X = -40f; p[Bone.ArmLR].X = -40f;
            p[Bone.Head].X = 18f; p[Bone.Hips].Y = 2f * M.Sin(t * 30f);
            p.Blink = 0.8f;
        }

        public static void Happy(Pose p, float t)
        {
            Idle(p, t);
            p.HipsOffset.Y += 0.035f * Math.Abs(M.Sin(t * 6f));
            p[Bone.ArmUL] = new Vector3(0, 0, -55f - 14f * M.Sin(t * 12f)); p[Bone.ArmUR] = new Vector3(0, 0, 55f + 14f * M.Sin(t * 12f));
            p[Bone.Head].X = -10f; p.Mouth = 0.8f; p.Blink = 0.7f;
        }

        public static void Sit(Pose p, float t)
        {
            Idle(p, t);
            p[Bone.LegUL].X = -88f; p[Bone.LegUR].X = -88f; p[Bone.LegLL].X = 70f + 12f * M.Sin(t * 2.4f); p[Bone.LegLR].X = 70f - 12f * M.Sin(t * 2.4f);
            p.HipsOffset.Y = -(Skeleton.HipsHeight - 0.065f);
            p[Bone.ArmUL] = new Vector3(20f, 0, -18f); p[Bone.ArmUR] = new Vector3(20f, 0, 18f);
        }

        public static void Balance(Pose p, float t, float wobble)
        {
            Crouch(p, 0.22f);
            float s = M.Sin(t * 5.5f) * wobble;
            p[Bone.ArmUL] = new Vector3(0, 0, -52f + 16f * s); p[Bone.ArmUR] = new Vector3(0, 0, 52f + 16f * s);
            p[Bone.Spine].Z = 7f * s; p[Bone.Head].Z = -5f * s;
        }

        public static void Startle(Pose p, float t)
        {
            Idle(p, t);
            p[Bone.ArmUL] = new Vector3(-20f, 0, -50f); p[Bone.ArmUR] = new Vector3(-20f, 0, 50f);
            p[Bone.Head].X = -14f; p[Bone.Spine].X = -8f; p.Blink = -0.5f; p.Mouth = 0.5f;
            p.HipsOffset.Y += 0.02f;
        }

        public static void Think(Pose p, float t)
        {
            Idle(p, t);
            p[Bone.ArmUR] = new Vector3(-95f, 0, -22f); p[Bone.ArmLR].X = -45f;
            p[Bone.Head] = new Vector3(-6f, 10f * M.Sin(t * 0.8f), 8f);
            p.EyeX = 0.5f * M.Sin(t * 0.8f); p.EyeY = 0.4f;
        }

        public static void Work(Pose p, float t)
        {
            Idle(p, t);
            float h = M.Sin(t * 9f);
            p[Bone.ArmUR] = new Vector3(-95f + 38f * h, 0, 0); p[Bone.ArmLR].X = -45f + 20f * h;
            p[Bone.ArmUL] = new Vector3(-55f, 0, 12f); p[Bone.ArmLL].X = -35f;
            p[Bone.Spine].X = 12f; p[Bone.Head].X = 10f;
        }

        /// <summary>
        /// At its workbench, making something: bent over the piece, the left hand holding it, the right arm swinging a
        /// hammer - up slowly, down fast, a little bounce of the whole body on every blow. strike is the moment within
        /// one blow (0..1; the blow lands at 0.62) - the caller takes it from CraftBeat so that sparks fly on the blow.
        /// Every few blows it stops, leans back and looks the piece over.
        /// </summary>
        public static void Craft(Pose p, float t, float strike, bool pause)
        {
            Idle(p, t);
            if (pause)
            {
                // a step back in its mind: head tilted, hammer hand at the chin
                p[Bone.Spine].X = 2f; p[Bone.Head] = new Vector3(4f, 10f * M.Sin(t * 1.3f), 9f);
                p[Bone.ArmUR] = new Vector3(-92f, 0, -24f); p[Bone.ArmLR].X = -52f;
                p[Bone.ArmUL] = new Vector3(-48f, 0, 14f); p[Bone.ArmLL].X = -40f;
                return;
            }
            // the swing: raised until 0.45, down by 0.62, held on the piece for a moment, then up again
            float up = strike < 0.45f ? M.Smooth(strike / 0.45f) : strike < 0.62f ? 1f - M.Smooth((strike - 0.45f) / 0.17f) : 0f;
            float hit = strike >= 0.62f ? 1f - M.Clamp01((strike - 0.62f) / 0.22f) : 0f;      // the bounce right after the blow
            p[Bone.ArmUR] = new Vector3(-58f - 92f * up, 0, -8f); p[Bone.ArmLR].X = -28f - 46f * up;
            p[Bone.ArmUL] = new Vector3(-62f, 0, 16f); p[Bone.ArmLL].X = -30f;
            p[Bone.Spine].X = 16f - 9f * up + 5f * hit; p[Bone.Head].X = 14f - 6f * up;
            p.HipsOffset.Y -= 0.012f * hit;
        }

        /// <summary>Where in a hammer blow the moment t is (0..1), and whether it is one of the pauses to look the piece over.</summary>
        public static float CraftBeat(float t, out bool pause)
        {
            const float Blow = 0.46f;                    // a little more than two blows a second
            float cycle = M.Frac(t / 6.0f);              // five seconds of hammering, one of looking
            pause = cycle > 0.84f;
            return M.Frac(t / Blow);
        }

        /// <summary>
        /// Picked up by the back of its hood and hanging from the hand. The body is turned by the caller (it swings
        /// under the hand like a pendulum); here are the loose parts. swingX / swingZ say how far the body has swung
        /// out from under the hand, in degrees - to its own right, and forwards: arms and legs trail BEHIND the swing,
        /// the toes point down. struggle runs from 1 (it has just been grabbed: eyes wide, arms flung out, legs
        /// pedalling) to 0 (it has settled: it hangs patiently, looks up at whoever holds it, and kicks its legs
        /// twice every few seconds).
        /// </summary>
        public static void Dangle(Pose p, float t, float swingX, float swingZ, float struggle)
        {
            p.Clear();
            struggle = M.Clamp01(struggle);
            float sx = M.Clamp(swingX, -60f, 60f), sz = M.Clamp(swingZ, -60f, 60f);
            float breath = M.Sin(t * 2.2f);
            // the hood pulls at the back of the head: the chin drops a little; the spine gives with the swing
            p[Bone.Spine] = new Vector3(8f + 0.10f * sz, 0, -0.12f * sx);
            p[Bone.Chest].X = 3f + breath;
            p[Bone.Head] = new Vector3(9f - 0.15f * sz, 5f * M.Sin(t * 0.7f) * (1f - struggle), 0.15f * sx);
            // arms: loose, a little forward; flung out and flapping at first
            float flap = struggle * M.Sin(t * 17f);
            float armX = -14f + 0.55f * sz, armZ = -0.50f * sx;
            p[Bone.ArmUL] = new Vector3(armX + 10f * flap, 0, -6f - 42f * struggle + armZ);
            p[Bone.ArmUR] = new Vector3(armX - 10f * flap, 0, 6f + 42f * struggle + armZ);
            p[Bone.ArmLL].X = -10f - 14f * struggle; p[Bone.ArmLR].X = -10f - 14f * struggle;
            // legs: pedalling at first, later two quick kicks every few seconds
            float beat = M.Frac(t / 3.4f);
            float kick = beat > 0.82f ? M.Sin((beat - 0.82f) / 0.18f * TwoPi * 2f) : 0f;
            float pedal = struggle * M.Sin(t * 14f) + (1f - struggle) * kick;
            float legX = -6f + 0.60f * sz, legZ = -0.35f * sx;
            p[Bone.LegUL] = new Vector3(legX - 26f * pedal, 0, legZ);
            p[Bone.LegUR] = new Vector3(legX + 26f * pedal, 0, legZ);
            p[Bone.LegLL].X = 12f + 14f * Math.Max(0f, pedal); p[Bone.LegLR].X = 12f + 14f * Math.Max(0f, -pedal);
            p[Bone.FootL].X = 38f; p[Bone.FootR].X = 38f;                    // toes down
            p[Bone.Antenna] = new Vector3(0.3f * sz, 0, -0.3f * sx);
            // the face: wide eyes and an open mouth at first (as Startle has them), then half-closed, patient eyes that look up
            p.Blink = M.Lerp(Math.Max(BlinkAt(t), 0.18f), -0.5f, struggle);
            p.Mouth = 0.5f * struggle;
            p.EyeY = 0.35f * (1f - struggle);
            p.EyeX = 0.3f * M.Sin(t * 0.6f) * (1f - struggle);
        }

        public static void Point(Pose p, float t)
        {
            Idle(p, t);
            p[Bone.ArmUR] = new Vector3(-85f, 0, 0); p[Bone.ArmLR].X = -4f;
        }

        public static void Thankful(Pose p, float t)
        {
            Idle(p, t);
            float bow = 0.5f + 0.5f * M.Sin(t * 3.2f);
            p[Bone.Spine].X = 20f * bow; p[Bone.Head].X = 12f * bow;
            p[Bone.ArmUL] = new Vector3(-60f, 0, 24f); p[Bone.ArmUR] = new Vector3(-60f, 0, -24f);
            p[Bone.ArmLL].X = -45f; p[Bone.ArmLR].X = -45f;
            p.Blink = 0.75f; p.Mouth = 0.5f;
        }

        /// <summary>Listening to the user: leaning in a little, head cocked, eyes wide, hands together in front.</summary>
        public static void Listen(Pose p, float t)
        {
            Idle(p, t);
            p[Bone.Spine].X = 7f; p[Bone.Head] = new Vector3(-9f, 0, 9f + 2f * M.Sin(t * 1.3f));
            p[Bone.ArmUL] = new Vector3(-48f, 0, 22f); p[Bone.ArmUR] = new Vector3(-48f, 0, -22f);
            p[Bone.ArmLL].X = -30f; p[Bone.ArmLR].X = -30f;
            p.Blink = BlinkAt(t) > 0 ? BlinkAt(t) : -0.25f;
        }

        /// <summary>
        /// Speaking. voice is how loud the partner's own voice is right now (0..1): the head nods and the hands move with
        /// the syllables, since the face has no mouth to show it.
        /// </summary>
        public static void Talk(Pose p, float t, float voice)
        {
            Idle(p, t);
            voice = M.Clamp01(voice);
            float beat = M.Sin(t * 3.1f), beat2 = M.Sin(t * 2.3f + 1.1f);
            p[Bone.Head] = new Vector3(-5f - 9f * voice, 5f * beat2, 3f * beat);
            p[Bone.Spine].X = 3f + 3f * voice; p[Bone.Chest].Y = 4f * beat2;
            p[Bone.ArmUL] = new Vector3(-28f - 22f * voice * (0.5f + 0.5f * beat), 0, -6f - 14f * voice);
            p[Bone.ArmUR] = new Vector3(-28f - 22f * voice * (0.5f - 0.5f * beat), 0, 6f + 14f * voice);
            p[Bone.ArmLL].X = -18f - 14f * voice; p[Bone.ArmLR].X = -18f - 14f * voice;
            p.HipsOffset.Y += 0.010f * voice;
            p.Mouth = voice;
        }

        /// <summary>Tennis, waiting for the ball: knees bent, leaning in, the racket hand held out in front, light on the feet.</summary>
        public static void TennisReady(Pose p, float t)
        {
            Crouch(p, 0.30f);
            p.Squash = 0.11f + 0.07f * M.Sin(t * 7f);
            p[Bone.Spine] = new Vector3(12f, 0, 2.5f * M.Sin(t * 3.5f)); p[Bone.Head].X = -10f;
            p[Bone.ArmUR] = new Vector3(-42f, 0, 14f); p[Bone.ArmLR].X = -30f;      // the racket hand
            p[Bone.ArmUL] = new Vector3(-30f, 0, -16f); p[Bone.ArmLL].X = -25f;
            p.Blink = BlinkAt(t);
        }

        public static void FootballKick(Pose pose, float u, float time)
        {
            TennisReady(pose, time);
            float kick = M.Sin(TwoPi * u * 0.5f) * M.Sin(TwoPi * u);
            pose[Bone.LegUR].X += kick * 68f;
            pose[Bone.LegLR].X += Math.Max(0f, kick) * 42f;
            pose[Bone.Spine].Z -= kick * 12f;
        }

        /// <summary>Where in TennisSwing (0..1) the racket meets the ball.</summary>
        public const float SwingContact = 0.5f;

        /// <summary>
        /// Tennis, a forehand with the right hand. u runs 0..1: the body turns and the arm goes back, swings through the
        /// ball (contact at SwingContact), follows through across the body and comes back to the ready stance.
        /// </summary>
        public static void TennisSwing(Pose p, float u, float t)
        {
            TennisReady(p, t);
            u = M.Clamp01(u);
            float back = M.Smooth(u / 0.38f) * (1f - M.Smooth((u - 0.38f) / 0.14f));    // wound up
            float rec = M.Smooth((u - 0.70f) / 0.30f);                                   // coming back to ready
            float thru = M.Smooth((u - 0.38f) / 0.16f) * (1f - rec);                     // through the ball and across
            float twist = 36f * back - 42f * thru;
            p[Bone.Spine].Y += twist * 0.4f; p[Bone.Chest].Y += twist * 0.6f;
            p[Bone.Head].Y -= twist * 0.85f;                                             // the eyes stay on the ball
            Vector3 arm = Vector3.Lerp(p[Bone.ArmUR], new Vector3(36f, 0, 50f), back);
            p[Bone.ArmUR] = Vector3.Lerp(arm, new Vector3(-92f, 0, -6f), thru);
            p[Bone.ArmLR].X = M.Lerp(M.Lerp(-30f, -8f, back), -14f, thru);
            p[Bone.ArmUL] = Vector3.Lerp(p[Bone.ArmUL], new Vector3(-38f, 0, -34f), back);   // the free hand points at the ball
            p.Squash += 0.10f * back - 0.20f * thru;                                     // down into the stroke, up through it
        }

        // ---- playing and living with its things -------------------------------------------------------------------

        /// <summary>Both hands over the eyes: counting with its back turned ("red light, green light").</summary>
        public static void CoverEyes(Pose p, float t)
        {
            Idle(p, t);
            p[Bone.ArmUL] = new Vector3(-118f, 0, 22f); p[Bone.ArmUR] = new Vector3(-118f, 0, -22f);
            p[Bone.ArmLL].X = -95f; p[Bone.ArmLR].X = -95f;
            p[Bone.Head].X = 9f; p.Blink = 1f;
        }

        /// <summary>Turned round, leaning forward, eyes wide, looking from side to side: did anything move?</summary>
        public static void Peek(Pose p, float t)
        {
            Idle(p, t);
            float look = M.Sin(t * 4.2f);
            p[Bone.Spine].X = 16f; p[Bone.Head] = new Vector3(-10f, 16f * look, 0);
            p[Bone.ArmUL] = new Vector3(12f, 0, -40f); p[Bone.ArmUR] = new Vector3(12f, 0, 40f);
            p[Bone.ArmLL].X = -70f; p[Bone.ArmLR].X = -70f;
            p.Blink = -0.5f; p.EyeX = 0.7f * look;
        }

        /// <summary>
        /// Dancing. beat counts the beats of the tune (one unit per beat): the whole body bounces on every beat, and the
        /// move changes every eight beats - a side step, both hands in the air, a twist, and pointing to the sky.
        /// </summary>
        public static void Dance(Pose p, float t, float beat)
        {
            Idle(p, t);
            int move = (int)Math.Floor(beat / 8f) % 4; if (move < 0) move += 4;
            float ph = beat * TwoPi;
            float half = M.Sin(ph * 0.5f);                                   // one side to the other over two beats
            float bounce = Math.Abs(M.Sin(ph * 0.5f));                       // up on every beat
            p.HipsOffset.Y += 0.032f * bounce; p.Squash = 0.12f * (1f - bounce);
            p[Bone.Head].Z = -6f * half;
            switch (move)
            {
                case 0:      // side step, arms swinging across
                    p.HipsOffset.X = 0.035f * half; p[Bone.Spine].Z = 9f * half;
                    p[Bone.ArmUL] = new Vector3(-34f * half, 0, -42f); p[Bone.ArmUR] = new Vector3(34f * half, 0, 42f);
                    p[Bone.LegUL].X = -14f * Math.Max(0f, half); p[Bone.LegUR].X = -14f * Math.Max(0f, -half);
                    break;
                case 1:      // both hands in the air, pumping
                    p[Bone.ArmUL] = new Vector3(0, 0, -122f - 22f * bounce); p[Bone.ArmUR] = new Vector3(0, 0, 122f + 22f * bounce);
                    p[Bone.Head].X = -9f; p[Bone.Spine].Y = 10f * M.Sin(ph * 0.25f);
                    break;
                case 2:      // the twist
                    p[Bone.Spine].Y = 24f * half; p[Bone.Chest].Y = 14f * half; p[Bone.Head].Y = -30f * half;
                    p[Bone.ArmUL] = new Vector3(-58f, 0, -30f); p[Bone.ArmUR] = new Vector3(-58f, 0, 30f);
                    p[Bone.ArmLL].X = -72f; p[Bone.ArmLR].X = -72f;
                    p[Bone.LegLL].X = 18f * bounce; p[Bone.LegLR].X = 18f * bounce;
                    break;
                default:     // one hand to the sky, then the other
                    bool left = M.Sin(ph * 0.25f) > 0;
                    p[Bone.ArmUL] = left ? new Vector3(0, 0, -152f) : new Vector3(24f, 0, -24f);
                    p[Bone.ArmUR] = left ? new Vector3(24f, 0, 24f) : new Vector3(0, 0, 152f);
                    p[Bone.Spine].Z = left ? -9f : 9f;
                    p[Bone.LegUL].X = left ? -22f * bounce : 0f; p[Bone.LegUR].X = left ? 0f : -22f * bounce;
                    break;
            }
            p.Mouth = 0.6f; p.Blink = 0.4f * bounce;
        }

        /// <summary>Seated on something with a seat (a chair, the edge of a bed): thighs forward, shins hanging. The caller puts the hips at seat height.</summary>
        public static void SitOn(Pose p, float t)
        {
            Idle(p, t);
            p[Bone.LegUL].X = -86f; p[Bone.LegUR].X = -86f;
            p[Bone.LegLL].X = 80f + 7f * M.Sin(t * 2.0f); p[Bone.LegLR].X = 80f - 7f * M.Sin(t * 2.0f);      // the feet swing a little
            p[Bone.ArmUL] = new Vector3(16f, 0, -14f); p[Bone.ArmUR] = new Vector3(16f, 0, 14f);
            p.Squash = 0f;
        }

        /// <summary>Seated at a desk, writing: bent over the page, the right hand moving, now and then a pause to think.</summary>
        public static void Study(Pose p, float t)
        {
            SitOn(p, t);
            float pause = M.Frac(t / 7.5f);
            if (pause > 0.80f)
            {
                // pen to the chin, eyes up: thinking
                p[Bone.Spine].X = 6f; p[Bone.Head] = new Vector3(-8f, 8f * M.Sin(t * 0.9f), 6f);
                p[Bone.ArmUR] = new Vector3(-96f, 0, -22f); p[Bone.ArmLR].X = -48f;
                p[Bone.ArmUL] = new Vector3(-52f, 0, 14f); p[Bone.ArmLL].X = -52f;
                p.EyeY = 0.5f;
                return;
            }
            float h = M.Sin(t * 8f);
            p[Bone.Spine].X = 9f; p[Bone.Head] = new Vector3(9f, 5f * M.Sin(t * 0.8f), 0);      // bent over the page, but the face can still be seen
            p[Bone.ArmUR] = new Vector3(-62f + 4f * h, 0, -10f + 7f * M.Sin(t * 2.6f)); p[Bone.ArmLR].X = -50f + 7f * h;
            p[Bone.ArmUL] = new Vector3(-54f, 0, 15f); p[Bone.ArmLL].X = -56f;
            p.EyeY = -0.5f;
        }

        /// <summary>Holding a book up with both hands, head bowed over it; every few seconds a page is turned.</summary>
        public static void ReadBook(Pose p, float t, bool seated)
        {
            if (seated) SitOn(p, t); else Sit(p, t);
            p[Bone.ArmUL] = new Vector3(-72f, 0, 18f); p[Bone.ArmUR] = new Vector3(-72f, 0, -18f);
            p[Bone.ArmLL].X = -68f; p[Bone.ArmLR].X = -68f;
            p[Bone.Head] = new Vector3(15f + 2f * M.Sin(t * 0.7f), 7f * M.Sin(t * 0.45f), 0);
            float turn = M.Frac(t / 5.5f);
            if (turn > 0.88f) { float u = (turn - 0.88f) / 0.12f; p[Bone.ArmUR] = new Vector3(-72f, 0, -18f + 42f * M.Sin(u * (float)Math.PI)); }
            p.EyeY = -0.4f; p.EyeX = 0.4f * M.Sin(t * 1.6f);
        }

        /// <summary>Asleep (the caller lays the body down): slow breathing, eyes shut.</summary>
        public static void Sleep(Pose p, float t)
        {
            p.Clear();
            float breath = M.Sin(t * 1.3f);
            p[Bone.Spine].X = 2f * breath; p[Bone.Chest].X = 1.5f * breath;
            p[Bone.ArmUL] = new Vector3(0, 0, -16f); p[Bone.ArmUR] = new Vector3(0, 0, 16f);
            p[Bone.LegUL].X = -6f; p[Bone.LegUR].X = -10f; p[Bone.LegLR].X = 14f;
            p[Bone.Head].Z = 8f;
            p.Squash = 0.06f * (0.5f - 0.5f * breath);
            p.Blink = 1f; p.Mouth = 0.15f + 0.1f * Math.Max(0f, breath);
        }

        /// <summary>A kick with the right foot. u runs 0..1: the leg goes back, swings through, and comes down again.</summary>
        public static void Kick(Pose p, float u, float t)
        {
            Idle(p, t);
            u = M.Clamp01(u);
            float back = M.Smooth(u / 0.35f) * (1f - M.Smooth((u - 0.35f) / 0.15f));
            float thru = M.Smooth((u - 0.35f) / 0.18f) * (1f - M.Smooth((u - 0.65f) / 0.35f));
            p[Bone.LegUR].X = 34f * back - 74f * thru; p[Bone.LegLR].X = 40f * back + 6f * thru;
            p[Bone.Spine].X = 8f * back - 10f * thru;
            p[Bone.ArmUL] = new Vector3(-30f * thru, 0, -46f); p[Bone.ArmUR] = new Vector3(26f * thru, 0, 40f);
        }

        /// <summary>Rock - paper - scissors: the fist comes down on every beat of the count.</summary>
        public static void RpsShake(Pose p, float t, float beat)
        {
            Idle(p, t);
            float down = Math.Abs(M.Sin(beat * (float)Math.PI));
            p[Bone.ArmUR] = new Vector3(-88f + 30f * down, 0, 0); p[Bone.ArmLR].X = -52f;
            p[Bone.Head].X = 4f; p.HipsOffset.Y += 0.012f * (1f - down);
        }

        /// <summary>Additive look-at applied on top of any pose (degrees, already clamped by caller).</summary>
        public static void AddLook(Pose p, float yawDeg, float pitchDeg)
        {
            yawDeg = M.Clamp(yawDeg, -70f, 70f); pitchDeg = M.Clamp(pitchDeg, -40f, 40f);
            p[Bone.Head].Y += yawDeg * 0.7f; p[Bone.Head].X += pitchDeg * 0.7f;
            p[Bone.Chest].Y += yawDeg * 0.3f;
            p.EyeX = M.Clamp(yawDeg / 60f, -1f, 1f); p.EyeY = M.Clamp(-pitchDeg / 40f, -1f, 1f);
        }
    }
}
