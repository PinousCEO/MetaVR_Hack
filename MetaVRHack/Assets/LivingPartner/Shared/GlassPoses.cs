using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>
    /// The poses the glass games and the shows inside the PC ask for that PoseLib does not have (GlassGame.petPose:
    /// idle, walk, hurt, cheer, down, pick, cower, wipe - the others are PoseLib.Idle / Walk / Happy / Cower).
    /// Pure functions of their arguments, like PoseLib; simple, but each one reads at a glance at 7 cm on a monitor.
    /// </summary>
    public static class GlassPoses
    {
        /// <summary>Wiping the glass in front of it: one arm up, the hand going from side to side, the body swaying along.</summary>
        public static void Wipe(Pose p, float t)
        {
            PoseLib.Idle(p, t);
            float s = M.Sin(t * 7.5f);
            p[Bone.ArmUR] = new Vector3(-118f + 8f * M.Sin(t * 15f), 0, 22f + 34f * s);      // up in front of the face, sweeping left and right
            p[Bone.ArmLR] = new Vector3(-14f, 0, 10f * s);
            p[Bone.ArmUL] = new Vector3(-30f, 0, -14f); p[Bone.ArmLL].X = -30f;
            p[Bone.Spine] = new Vector3(-4f, 0, 5f * s);
            p[Bone.Head] = new Vector3(-12f, 6f * s, -3f * s);
            p.EyeX = 0.5f * s; p.EyeY = 0.5f;
        }

        /// <summary>Bending down for something on the floor (a = 0 standing .. 1 all the way down).</summary>
        public static void Pick(Pose p, float t, float a)
        {
            a = M.Clamp01(a);
            PoseLib.Crouch(p, 0.75f * a);
            p[Bone.Spine].X = 46f * a; p[Bone.Chest].X = 12f * a; p[Bone.Head].X = 8f * a;
            p[Bone.ArmUL] = new Vector3(-66f * a, 0, 14f * a - 10f); p[Bone.ArmUR] = new Vector3(-66f * a, 0, 10f - 14f * a);     // both hands reach down in front
            p[Bone.ArmLL].X = -10f * a; p[Bone.ArmLR].X = -10f * a;
            p.EyeY = -0.8f * a; p.Blink = PoseLib.BlinkAt(t);
        }

        /// <summary>The game is lost: sitting on the floor, slumped, the head hanging, the eyes half shut.</summary>
        public static void Down(Pose p, float t)
        {
            PoseLib.Sit(p, t);
            float sigh = M.Sin(t * 1.4f);
            p[Bone.Spine] = new Vector3(24f + 3f * sigh, 0, 0); p[Bone.Chest].X = 8f;
            p[Bone.Head] = new Vector3(22f + 2f * sigh, 0, 5f);
            p[Bone.ArmUL] = new Vector3(8f, 0, -6f); p[Bone.ArmUR] = new Vector3(8f, 0, 6f);
            p[Bone.ArmLL].X = -6f; p[Bone.ArmLR].X = -6f;
            p[Bone.LegLL].X = 70f; p[Bone.LegLR].X = 70f;
            p.Blink = 0.6f; p.EyeY = -0.6f; p.Mouth = 0f;
        }

        /// <summary>Just hit: thrown back for a moment, arms out, eyes wide, shaking.</summary>
        public static void Hurt(Pose p, float t)
        {
            PoseLib.Startle(p, t);
            float shake = M.Sin(t * 42f);
            p[Bone.Spine] = new Vector3(-12f, 0, 4f * shake);
            p[Bone.Head] = new Vector3(-16f, 0, -5f * shake);
            p[Bone.ArmUL] = new Vector3(-24f, 0, -62f); p[Bone.ArmUR] = new Vector3(-24f, 0, 62f);
            p.Blink = 0.9f; p.Mouth = 0.7f;
        }

        /// <summary>Looking closely at something: leaning towards it, one hand shading the eyes, eyes wide. Add PoseLib.AddLook for where.</summary>
        public static void Peer(Pose p, float t)
        {
            PoseLib.Idle(p, t);
            p[Bone.Spine].X = 7f + 1.5f * M.Sin(t * 1.7f);
            p[Bone.Head] = new Vector3(-8f, 0, 3f * M.Sin(t * 0.9f));
            p[Bone.ArmUR] = new Vector3(-112f, 0, -20f); p[Bone.ArmLR].X = -58f;      // the right hand over the eyes
            p[Bone.ArmUL] = new Vector3(-12f, 0, -16f); p[Bone.ArmLL].X = -24f;
            p.Blink = PoseLib.BlinkAt(t) > 0f ? PoseLib.BlinkAt(t) : -0.3f;
        }

        /// <summary>Holding a thing out in front with both hands (lift = 0 at the chest .. 1 held up high).</summary>
        public static void Offer(Pose p, float t, float lift)
        {
            PoseLib.Idle(p, t);
            lift = M.Clamp01(lift);
            float up = -70f - 40f * lift;
            p[Bone.ArmUL] = new Vector3(up, 0, 20f); p[Bone.ArmUR] = new Vector3(up, 0, -20f);
            p[Bone.ArmLL].X = -34f; p[Bone.ArmLR].X = -34f;
            p[Bone.Head].X = -6f - 8f * lift; p[Bone.Spine].X = -3f * lift;
            p.Mouth = 0.4f;
        }

        /// <summary>The pose for a GlassGame.petPose. phase is the gait phase for "walk" (cycles), amount the 0..1 of "pick".</summary>
        public static void For(string petPose, Pose p, float t, float phase, float amount)
        {
            switch (petPose)
            {
                case "walk": case "run": PoseLib.Walk(p, phase); break;
                case "hurt": Hurt(p, t); break;
                case "cheer": PoseLib.Happy(p, t); break;
                case "down": Down(p, t); break;
                case "pick": Pick(p, t, amount); break;
                case "cower": PoseLib.Cower(p, t); break;
                case "wipe": Wipe(p, t); break;
                default: PoseLib.Idle(p, t); break;
            }
        }
    }
}
