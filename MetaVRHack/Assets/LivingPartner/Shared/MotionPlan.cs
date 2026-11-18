using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    public enum CrossVariant { DiveFlip = 0, BezelClimb = 1, RunLeap = 2 }
    public enum CrossPhase { Prepare = 0, FlightIn = 1, Thread = 2, FlightOut = 3, Land = 4, Recover = 5, Done = 6 }

    /// <summary>
    /// Deterministic description of one portal crossing, expressed in the PORTAL FRAME:
    /// origin = the chosen portal point on the lit display surface, +X = screen right, +Y = screen up,
    /// +Z = INTO the screen. The display surface is exactly the plane z = 0, whatever the monitor's tilt.
    /// MR space is z &lt; 0 and obeys real gravity (mrUp = world up seen from this frame); the PC "inside" is z &gt; 0 and
    /// is screen-aligned (its up is +Y). Both renderers evaluate the same plan against the same shared clock, so the
    /// body lines up without streaming bones.
    /// </summary>
    [Serializable]
    public sealed class CrossingPlan
    {
        public string transitionId = "";
        public string characterId = "";
        public int direction;            // 0 = MR -> PC, 1 = PC -> MR
        public int variant;              // CrossVariant
        public float H = 0.10f;          // character height in meters
        public V3 mrFoot;                // real standing point on the MR side (portal frame, z < 0)
        public V3 mrUp = new V3(0, 1, 0);// world up expressed in the portal frame (monitor tilt)
        public V3 pcFoot;                // standing point on the PC side (portal frame, z > 0)
        public float slope;              // rise per unit depth of the travel direction through the portal
        public float tPrepare = 0.42f, tFlightIn = 0.6f, tThread = 0.55f, tFlightOut = 0.7f, tLand = 0.22f, tRecover = 0.6f;
        public string monitorId = "";
        public float portalPxX, portalPxY;   // Windows physical pixel of the portal point (virtual desktop)

        public float Total => tPrepare + tFlightIn + tThread + tFlightOut + tLand + tRecover;
        public float ThreadStart => tPrepare + tFlightIn;
        public float ThreadEnd => ThreadStart + tThread;
        public float DirSign => direction == 0 ? 1f : -1f;
        public Vector3 From => direction == 0 ? (Vector3)mrFoot : (Vector3)pcFoot;
        public Vector3 To => direction == 0 ? (Vector3)pcFoot : (Vector3)mrFoot;
        public Vector3 MrUp => M.SafeNormalize(mrUp, Vector3.UnitY);
    }

    public sealed class CharState
    {
        public Vector3 RootPos;          // portal frame, meters
        public Quaternion RootRot = Quaternion.Identity;
        public readonly Pose Pose = new Pose();
        public CrossPhase Phase;
        public float Tau;
    }

    /// <summary>Result of body-vs-plane analysis used by tests, heartbeats and the debug overlay.</summary>
    public struct ThroughInfo
    {
        public float fraction;   // 0..1 of body samples on the destination side
        public float head, torso, legs, feet; // per-part fraction on the destination side
        public float minSigned, maxSigned;    // signed distance range of the body samples (m), + = destination side
    }

    public static class PlanBuilder
    {
        /// <summary>Clearance (m) the character needs around the portal point to pass without touching an edge.</summary>
        public static float ClearanceRadius(float H) => 0.56f * H;   // the head is the widest part (0.33 H to each side) and passes obliquely

        /// <summary>PC-resident depth behind the glass (m).</summary>
        public static float ResidentDepth(float H) => 0.45f * H;

        /// <summary>Preferred MR launch/landing distance in front of the screen (m), from how far the portal is above the desk.</summary>
        public static float LaunchDistance(float H, float rise) => M.Clamp(1.4f * H + 0.50f * Math.Abs(rise), 1.6f * H, 5.5f * H);

        /// <summary>
        /// Builds a plan from the REAL geometry both sides reported. mrFoot is where the partner stands in MR (portal frame),
        /// mrUp the world-up direction in that frame, pcFootY the height of the PC floor relative to the portal (negative),
        /// bottomEdgeBelowPortal the distance from the portal down to the bottom of the lit area. The variant is chosen from it.
        /// </summary>
        public static CrossingPlan Build(string transitionId, string characterId, int direction, float H,
            Vector3 mrFoot, Vector3 mrUp, float pcFootY, float bottomEdgeBelowPortal, float headroomAbovePortal = 10f)
        {
            mrUp = M.SafeNormalize(mrUp, Vector3.UnitY);
            if (mrFoot.Z > -0.6f * H) mrFoot.Z = -0.6f * H;      // the MR standing point is always in front of the glass
            var p = new CrossingPlan
            {
                transitionId = transitionId, characterId = characterId, direction = direction, H = H,
                mrFoot = mrFoot, mrUp = mrUp, pcFoot = new V3(0, pcFootY, ResidentDepth(H)),
            };
            float mrRise = -Vector3.Dot(mrFoot, mrUp);                         // portal height above the MR standing surface
            float mrDist = (mrFoot + mrUp * mrRise).Length();                  // horizontal distance of the standing point
            float riseSrc = direction == 0 ? mrRise : -pcFootY;
            float dropDst = direction == 0 ? -pcFootY : mrRise;
            float centreRise = riseSrc - 0.5f * H;
            // Variant from geometry: a short hop over the lower bezel when the portal hugs the bottom edge,
            // a long running leap for far/high targets, otherwise a standing dive.
            if (direction == 0 && bottomEdgeBelowPortal >= 0 && bottomEdgeBelowPortal < 0.75f * H && centreRise < 2.2f * H && centreRise > 0.2f * H)
                p.variant = (int)CrossVariant.BezelClimb;
            else if (centreRise > 2.6f * H || (direction == 0 && mrDist > 3.2f * H))
                p.variant = (int)CrossVariant.RunLeap;
            else
                p.variant = (int)CrossVariant.DiveFlip;

            // The body enters obliquely (rising 37-49 degrees), like a dive into a pool: seen from in front, the part still
            // outside and the part already inside sit along one line through the chosen point instead of hiding each other.
            p.slope = M.Lerp(1.15f, 0.75f, M.Clamp01((dropDst - 0.5f * H) / (3.0f * H)));
            // little room between the portal and the top of the lit area: enter flatter so the body does not leave the screen
            if (direction == 0) p.slope = Math.Min(p.slope, M.Lerp(0.2f, 1.15f, M.Clamp01((headroomAbovePortal - 0.9f * H) / (1.3f * H))));
            float lenIn = (float)Math.Sqrt(centreRise * centreRise + (direction == 0 ? mrDist * mrDist : 0.01f));
            p.tPrepare = p.variant == (int)CrossVariant.RunLeap ? 0.26f : 0.42f;
            p.tFlightIn = M.Clamp(0.34f + 1.5f * lenIn, 0.42f, 1.05f);
            if (p.variant == (int)CrossVariant.BezelClimb) p.tFlightIn = 1.55f; // hop, hang on the bezel, pull up
            p.tThread = 0.55f;
            p.tFlightOut = M.Clamp(0.48f + 1.3f * (float)Math.Sqrt(Math.Max(0.0, dropDst) / 4.9f), 0.55f, 1.15f);
            p.tLand = 0.22f; p.tRecover = 0.62f;
            return p;
        }
    }

    public static class PlanEvaluator
    {
        [ThreadStatic] static Pose _a, _b;
        [ThreadStatic] static Vector3[] _fkPos;
        [ThreadStatic] static Quaternion[] _fkRot;

        public static Vector3 TravelDir(CrossingPlan p) => Vector3.Normalize(new Vector3(0, p.slope, p.DirSign));

        /// <summary>Shortest rotation taking +Y to the given up direction.</summary>
        static Quaternion FromUp(Vector3 up)
        {
            Vector3 axis = Vector3.Cross(Vector3.UnitY, up);
            float s = axis.Length(), c = Vector3.Dot(Vector3.UnitY, up);
            if (s < 1e-6f) return Quaternion.Identity;
            return Quaternion.CreateFromAxisAngle(axis / s, (float)Math.Atan2(s, c));
        }

        /// <summary>Evaluates root transform (portal frame) and pose at plan time tau (seconds).</summary>
        public static void Evaluate(CrossingPlan p, float tau, CharState s)
        {
            _a ??= new Pose(); _b ??= new Pose();
            float H = p.H;
            tau = M.Clamp(tau, 0f, p.Total);
            s.Tau = tau;
            Vector3 from = p.From, to = p.To;
            Vector3 upMr = p.MrUp, upPc = Vector3.UnitY;
            Vector3 upFrom = p.direction == 0 ? upMr : upPc, upTo = p.direction == 0 ? upPc : upMr;
            Vector3 d = TravelDir(p);
            Vector3 zin = new Vector3(0, 0, p.DirSign);
            Vector3 pin = -d * (0.62f * H), pout = d * (0.62f * H);
            float phiP = 90f - (float)Math.Atan(p.slope) * M.Rad2Deg;   // pitch that aligns the body's up axis with the travel direction
            float yaw = p.direction == 0 ? 0f : 180f;

            // --- path control points
            Vector3 a0 = from + upFrom * (0.5f * H), a3 = pin;
            float la = Vector3.Distance(a0, a3);
            Vector3 a1 = a0 + Vector3.Normalize(upFrom + zin * 0.45f) * (0.45f * la);
            Vector3 a2 = a3 - d * (0.40f * la);
            Vector3 b0 = pout, b3 = to + upTo * (0.5f * H);
            float lb = Vector3.Distance(b0, b3);
            // after the portal the body keeps going along d, moving clear of the glass before it tucks and turns
            Vector3 b1 = b0 + d * (0.75f * H) + zin * (0.5f * H);
            Vector3 b2 = b3 + upTo * Math.Min(0.45f * lb + 0.35f * H, 1.3f * H) + zin * (0.35f * H);   // a modest arc: the body stays near the chosen point

            Vector3 centre; float phi; float yawNow = yaw; Vector3 up = upFrom;
            float t0 = p.tPrepare, t1 = t0 + p.tFlightIn, t2 = t1 + p.tThread, t3 = t2 + p.tFlightOut, t4 = t3 + p.tLand;

            if (tau < t0)
            {
                s.Phase = CrossPhase.Prepare;
                float u = tau / t0;
                centre = a0; phi = 0f;
                PoseLib.Crouch(s.Pose, M.Smooth(u));
                s.Pose[Bone.Head].X -= 12f * M.Smooth(u); // looks up at the portal
            }
            else if (tau < t1)
            {
                s.Phase = CrossPhase.FlightIn;
                float u = (tau - t0) / p.tFlightIn;
                up = Vector3.Normalize(Vector3.Lerp(upFrom, upPc, M.Smooth(u)));   // leave the source's own "up" on the way to the portal
                if (p.variant == (int)CrossVariant.BezelClimb)
                {
                    // hop up to the lower bezel, hang, pull up and tip over the edge into the thread line
                    Vector3 hang = new Vector3(0, pin.Y - 0.72f * H, -p.DirSign * 0.16f * H);
                    if (u < 0.34f)
                    {
                        float k = M.EaseOut(u / 0.34f);
                        centre = Vector3.Lerp(a0, hang, k) + upFrom * (0.25f * H * M.Sin(k * (float)Math.PI));
                        phi = 0f;
                        PoseLib.Crouch(_a, 1f); PoseLib.Hang(_b); Pose.Lerp(_a, _b, M.Smooth(u / 0.2f), s.Pose);
                    }
                    else if (u < 0.56f)
                    {
                        float k = (u - 0.34f) / 0.22f;
                        centre = hang + new Vector3(0, 0.012f * H * M.Sin(k * 2f * (float)Math.PI), 0); phi = 4f * M.Sin(k * 2f * (float)Math.PI);
                        PoseLib.Hang(s.Pose);
                    }
                    else
                    {
                        float k = M.Smooth((u - 0.56f) / 0.44f);
                        centre = M.Bezier(hang, hang + upPc * (0.55f * H), pin - d * (0.3f * H), pin, k);
                        phi = phiP * M.Smooth(k);
                        PoseLib.PullUp(_a, M.Clamp01(k * 2.2f)); PoseLib.Dive(_b); Pose.Lerp(_a, _b, M.Smooth((k - 0.55f) / 0.45f), s.Pose);
                    }
                }
                else
                {
                    float k = M.Lerp(M.EaseOut(u), u, 0.55f);       // fast launch that settles into the thread speed
                    centre = M.Bezier(a0, a1, a2, a3, k);
                    phi = phiP * M.Smooth((float)Math.Pow(u, 0.9));
                    PoseLib.Crouch(_a, 1f); PoseLib.Dive(_b); Pose.Lerp(_a, _b, M.Smooth(u / 0.3f), s.Pose);
                }
            }
            else if (tau < t2)
            {
                s.Phase = CrossPhase.Thread;
                float u = (tau - t1) / p.tThread;
                centre = Vector3.Lerp(pin, pout, u); phi = phiP; up = upPc;
                PoseLib.Dive(s.Pose);
            }
            else if (tau < t3)
            {
                s.Phase = CrossPhase.FlightOut;
                float u = (tau - t2) / p.tFlightOut;
                float k = M.Lerp(u, M.EaseIn(u), 0.45f);
                centre = M.Bezier(b0, b1, b2, b3, k);
                up = Vector3.Normalize(Vector3.Lerp(upPc, upTo, M.Smooth(u)));
                // hold the dive line until clear of the glass, then swing the feet back under the body and come down upright.
                // (An earlier version turned a full somersault here; with this character's big head it read as tumbling.)
                phi = M.Lerp(phiP, 0f, M.Smooth((u - 0.12f) / 0.50f));
                PoseLib.Dive(_a); PoseLib.Tuck(_b);
                float tuck = M.Smooth((u - 0.10f) / 0.2f) * (1f - M.Smooth((u - 0.52f) / 0.23f));
                Pose.Lerp(_a, _b, tuck, s.Pose);
                if (u > 0.52f) { PoseLib.Land(_a, 0.3f); Pose.Lerp(s.Pose, _a, M.Smooth((u - 0.52f) / 0.3f), s.Pose); }
            }
            else if (tau < t4)
            {
                s.Phase = CrossPhase.Land;
                float u = (tau - t3) / p.tLand;
                centre = b3; phi = 0f; up = upTo;
                PoseLib.Land(s.Pose, M.Lerp(0.3f, 1f, M.EaseOut(u)));
            }
            else
            {
                s.Phase = tau >= p.Total ? CrossPhase.Done : CrossPhase.Recover;
                float u = p.tRecover <= 0 ? 1f : (tau - t4) / p.tRecover;
                centre = b3; phi = 0f; up = upTo;
                PoseLib.Land(_a, 1f); PoseLib.Idle(_b, 0f); Pose.Lerp(_a, _b, M.Smooth(u / 0.7f), s.Pose);
                if (p.direction == 0)
                {
                    // inside the PC the partner turns round to face the viewer with a small hop
                    float k = M.Smooth((u - 0.25f) / 0.75f);
                    yawNow = yaw + 180f * k;
                    centre += up * (0.12f * H * M.Sin(k * (float)Math.PI));
                }
            }

            var rot = M.Mul(FromUp(up), M.Mul(M.Euler(0, yawNow, 0), M.Euler(phi, 0, 0)));
            s.RootRot = rot;
            s.RootPos = centre - M.Rot(rot, new Vector3(0, 0.5f * H, 0));
        }

        /// <summary>Portal-frame positions of the body sample points for a state.</summary>
        public static void SamplePoints(CrossingPlan p, CharState s, Vector3[] outPts)
        {
            _fkPos ??= new Vector3[Skeleton.N]; _fkRot ??= new Quaternion[Skeleton.N];
            Skeleton.FK(s.Pose, _fkPos, _fkRot);
            for (int i = 0; i < Skeleton.Samples.Length; i++)
            {
                var sm = Skeleton.Samples[i];
                Vector3 local = _fkPos[(int)sm.bone] + M.Rot(_fkRot[(int)sm.bone], sm.local);
                outPts[i] = s.RootPos + M.Rot(s.RootRot, local * p.H);
            }
        }

        /// <summary>How much of the body is on the destination side of the display plane (z = 0).</summary>
        public static ThroughInfo Through(CrossingPlan p, CharState s)
        {
            var pts = new Vector3[Skeleton.Samples.Length];
            SamplePoints(p, s, pts);
            var r = new ThroughInfo { minSigned = float.MaxValue, maxSigned = float.MinValue };
            float[] cnt = new float[4], tot = new float[4];
            int through = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                float sd = pts[i].Z * p.DirSign;
                r.minSigned = Math.Min(r.minSigned, sd); r.maxSigned = Math.Max(r.maxSigned, sd);
                int part = Skeleton.Samples[i].part == "head" ? 0 : Skeleton.Samples[i].part == "torso" ? 1 : Skeleton.Samples[i].part == "legs" ? 2 : 3;
                tot[part]++;
                if (sd > 0) { through++; cnt[part]++; }
            }
            r.fraction = through / (float)pts.Length;
            r.head = cnt[0] / tot[0]; r.torso = cnt[1] / tot[1]; r.legs = cnt[2] / tot[2]; r.feet = cnt[3] / tot[3];
            return r;
        }

        /// <summary>Continuous crossing progress 0..1: 0 = leading point touches the plane, 1 = trailing point has passed.</summary>
        public static float CrossingProgress(CrossingPlan p, CharState s)
        {
            var ti = Through(p, s);
            float span = ti.maxSigned - ti.minSigned;
            if (span < 1e-6f) return ti.maxSigned > 0 ? 1f : 0f;
            return M.Clamp01(ti.maxSigned / span);
        }

        /// <summary>Plan time at which the crossing progress reaches the given value (used by the debug freeze points).</summary>
        public static float TauForProgress(CrossingPlan p, float progress)
        {
            var s = new CharState();
            float lo = p.tPrepare, hi = p.ThreadEnd + p.tFlightOut * 0.5f;
            const int N = 240; float prev = lo;
            for (int i = 0; i <= N; i++)
            {
                float t = M.Lerp(lo, hi, i / (float)N);
                Evaluate(p, t, s);
                if (CrossingProgress(p, s) >= progress)
                {
                    float a = prev, b = t;
                    for (int k = 0; k < 20; k++)
                    {
                        float m = 0.5f * (a + b);
                        Evaluate(p, m, s);
                        if (CrossingProgress(p, s) >= progress) b = m; else a = m;
                    }
                    return b;
                }
                prev = t;
            }
            return hi;
        }
    }

    /// <summary>
    /// Shared crossing clock. tau(now) = anchorTau + (hostNow - anchorHost) * rate, optionally stopping at stopTau.
    /// rate 1 = play, 0 = hold, -1 = rewind (recover to the source side), 0.25 = slow motion for inspection.
    /// </summary>
    [Serializable]
    public sealed class CrossClock
    {
        public string transitionId = "";
        public double anchorHost;
        public float anchorTau;
        public float rate = 1f;
        public bool hasStop;
        public float stopTau;
        public int revision;

        public float TauAt(double hostNow, float total)
        {
            float tau = anchorTau + (float)(hostNow - anchorHost) * rate;
            if (hasStop)
            {
                if (rate >= 0 && tau > stopTau) tau = stopTau;
                if (rate < 0 && tau < stopTau) tau = stopTau;
            }
            return M.Clamp(tau, 0f, total);
        }
    }
}
