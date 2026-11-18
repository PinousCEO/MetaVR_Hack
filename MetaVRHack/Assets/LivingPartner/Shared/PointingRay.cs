using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>
    /// The pointing ray of a tracked hand: where the user means when they point at a screen.
    /// The ray runs from an estimated shoulder through the index knuckle - a long lever, so it is steady and does not
    /// swing when the finger bends - and leans towards the finger while the index is held out straight. While the
    /// fingers close for a pinch the ray is held still, so the act of pinching does not drag the pointer off the spot.
    /// Pure functions (no engine types), shared by the Quest client and the unit tests.
    /// </summary>
    public static class PointingRay
    {
        /// <summary>Thumb-to-index distance (m) under which the fingers count as closing for a pinch: the ray stops following.</summary>
        public const float HoldGap = 0.045f;
        /// <summary>How much the ray leans from the shoulder line towards an extended index finger (0..1).</summary>
        public const float FingerWeight = 0.35f;

        /// <summary>Estimated shoulder for a hand. side: -1 left, +1 right. headYawDeg: heading of the head about the vertical.</summary>
        public static Vector3 Shoulder(Vector3 headPos, float headYawDeg, float side) =>
            headPos + M.Rot(M.Euler(0, headYawDeg, 0), new Vector3(side * 0.17f, -0.22f, -0.02f));

        /// <summary>The direction the hand is pointing in right now (unit vector), before smoothing.</summary>
        public static Vector3 Aim(Vector3 shoulder, Vector3 knuckle, Vector3 indexTip, Vector3 palmForward)
        {
            Vector3 want = M.SafeNormalize(knuckle - shoulder, Vector3.UnitZ);
            Vector3 finger = M.SafeNormalize(indexTip - knuckle, want);
            // an index finger that is held out along the hand is itself pointing: let it steer a little
            if (Vector3.Dot(finger, M.SafeNormalize(palmForward, want)) > 0.8f) want = M.SafeNormalize(Vector3.Lerp(want, finger, FingerWeight), want);
            return want;
        }

        /// <summary>
        /// The smoothed ray for this frame. current is last frame's direction (zero on the first frame), pinchGap the
        /// thumb-to-index distance: below HoldGap the ray keeps its direction.
        /// </summary>
        public static Vector3 Step(Vector3 current, Vector3 want, float pinchGap, float dt)
        {
            if (current.LengthSquared() < 0.25f) return want;
            if (pinchGap <= HoldGap) return current;
            float k = 1f - (float)Math.Exp(-dt * 18f);
            return M.SafeNormalize(Vector3.Lerp(current, want, k), want);
        }
    }
}

namespace LivingPartner.Shared
{
    /// <summary>
    /// What the user pointed at while saying something. The pointing hand is sampled all the time; an utterance has a
    /// start and an end; the answer is the spot the hand dwelt on during that interval - not where the hand happens to
    /// be when the words have been understood.
    /// </summary>
    public static class PointingDwell
    {
        /// <summary>How close samples must be (in u and in v, fractions of the display) to count as dwelling on one spot.</summary>
        public const float DwellBox = 0.04f;

        /// <summary>
        /// The pointed spot for the interval t0..t1 (host time; a margin of 0.4 s is allowed on both sides): the middle
        /// of a pinch when the user pinched to confirm, otherwise the centre of the densest dwell. Null when the hand
        /// was not on a screen in that interval.
        /// </summary>
        public static PointerSample Resolve(System.Collections.Generic.IReadOnlyList<PointerSample> history, double t0, double t1)
        {
            if (history == null) return null;
            var win = new System.Collections.Generic.List<PointerSample>();
            foreach (var s in history) if (s.valid && s.t >= t0 - 0.4 && s.t <= t1 + 0.4) win.Add(s);
            if (win.Count == 0) return null;
            var pinched = win.FindAll(s => s.pinch);
            if (pinched.Count > 0) return pinched[pinched.Count / 2];
            PointerSample best = null; int bestN = -1;
            foreach (var s in win)
            {
                int n = 0;
                foreach (var o in win) if (o.monitorId == s.monitorId && Math.Abs(o.u - s.u) < DwellBox && Math.Abs(o.v - s.v) < DwellBox) n++;
                if (n >= bestN) { bestN = n; best = s; }          // on a tie the later dwell wins
            }
            // the centre of that dwell, not the one sample that happened to have the most neighbours; then once more
            // with a tighter box, which drops the samples of the hand still moving in
            float cu = best.u, cv = best.v, box = DwellBox;
            for (int pass = 0; pass < 2; pass++)
            {
                double su = 0, sv = 0; int n = 0;
                foreach (var o in win) if (o.monitorId == best.monitorId && Math.Abs(o.u - cu) < box && Math.Abs(o.v - cv) < box) { su += o.u; sv += o.v; n++; }
                if (n < 3) break;
                cu = (float)(su / n); cv = (float)(sv / n); box = DwellBox * 0.3f;
            }
            return new PointerSample { t = best.t, monitorId = best.monitorId, u = cu, v = cv, pinch = false, valid = true };
        }
    }
}
