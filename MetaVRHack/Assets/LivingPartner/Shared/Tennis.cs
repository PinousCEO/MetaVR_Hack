using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>
    /// The place the game is played in, in the display frame of one monitor (metres, origin at the centre of the lit
    /// area, x right, y up the screen, z into the screen). The glass is the net: the partner's half is a box behind the
    /// glass as wide and as high as the screen, the user's half is the room in front of it, over the desk.
    /// </summary>
    public sealed class TennisCourt
    {
        public string monitorId = "";
        public float width = 0.60f, height = 0.34f;     // the opening = the lit area of the display
        public float depth = 0.50f;                      // how far the partner's half goes back behind the glass
        public V3 up = new V3(0, 1, 0);                  // the room's "up" in this frame (the display may lean back)
        public float deskHeight = -0.25f;                // the desk, measured along up from the centre of the display
        // A court out in the room, on the desk: no screen and no glass. z = 0 is a low net, both halves are on the desk
        // (FloorBack == deskHeight), the partner's half has lines instead of walls: beyond them the ball is out.
        public bool room;
        public float baseline = 0.30f;                   // where the partner stands behind the glass / the net
        public float radius = Tennis.Radius;
        public float FloorBack => -height * 0.5f;
    }

    /// <summary>One stroke: where the ball was and how it was moving at host time t0. Everything after follows from it.</summary>
    public sealed class TennisShot
    {
        public int seq;
        public string by = "";          // partner | user
        public V3 p0, v0;
        public double t0;
    }

    /// <summary>The whole game, owned by the host and sent to both clients.</summary>
    public sealed class TennisState
    {
        public bool active;
        public string mode = "";        // "" = tennis | "stars" = the partner throws, the user catches | "keeper" = the partner kicks, the user keeps goal
        public int round, rounds;       // stars / keeper: balls played, balls in a game
        public string phase = "off";    // off | ready | rally | point | over
        public TennisCourt court = new TennisCourt();
        public TennisShot shot;         // null while no ball is in play
        public int scoreUser, scorePartner, rally, bestRally;
        public double phaseAt;          // host time the phase began
        public double serveAt;          // host time of the next serve (phase ready)
        public float partnerX, partnerZ = 0.30f;   // where the partner is heading
        public double partnerHitAt;     // host time of its next stroke (0 = none planned)
        public string lastPoint = "";   // who won the last point: user | partner | ""
        public string note = "";        // short line for the scoreboard
        public int revision;
    }

    public static partial class Tennis
    {
        /// <summary>What the scoreboard says (two lines at most), for whichever ball game is being played.</summary>
        public static string ScoreLine(TennisState s)
        {
            string l;
            if (s.mode == "stars")
            {
                l = $"Caught  {s.scoreUser} / {s.rounds}";
                switch (s.phase)
                {
                    case "ready": l += s.round == 0 ? "\nCatch the ball with your hand" : "\nHere comes another ..."; break;
                    case "point": l += s.lastPoint == "user" ? "\nGot it!" : "\nDropped!"; break;
                    case "over": l += "\nThat's all of them!"; break;
                }
                return l;
            }
            if (s.mode == "keeper")
            {
                l = $"Saves  {s.scoreUser}   Goals  {s.scorePartner}   ({Math.Min(s.round + (s.phase == "over" || s.phase == "point" ? 0 : 1), s.rounds)}/{s.rounds})";
                switch (s.phase)
                {
                    case "ready": l += s.round == 0 ? "\nStop the ball with your palm" : "\nNext shot ..."; break;
                    case "point": l += s.lastPoint == "user" ? "\nSaved!" : "\nGoal for Bambi"; break;
                    case "over": l += s.scoreUser * 2 > s.rounds ? "\nYou win the shoot-out!" : "\nBambi wins the shoot-out!"; break;
                }
                return l;
            }
            l = $"You  {s.scoreUser} : {s.scorePartner}  Bambi";
            switch (s.phase)
            {
                case "ready": l += s.scoreUser + s.scorePartner == 0 ? "\nHit the ball back with an open hand" : "\nGet ready ..."; break;
                case "rally": if (s.rally >= 2) l += "\nRally " + s.rally; break;
                case "point": l += s.lastPoint == "user" ? "\nPoint to you!" : "\nPoint to Bambi!"; break;
                case "over": l += s.scoreUser > s.scorePartner ? "\nYou win!" : "\nBambi wins!"; break;
            }
            return l;
        }
    }

    /// <summary>From a client: start | stop | hit (the user's hand met the ball).</summary>
    public sealed class GameCommand
    {
        public string cmd = "";
        public string mode = "";        // start: which game ("" = tennis, "stars", "keeper")
        public string monitorId = "";
        public int seq;                 // hit: the shot that was struck
        public V3 pos;                  // hit: where the ball was (display frame)
        public V3 swing;                // hit: how the hand was moving (display frame, m/s)
        public V3 normal;               // hit: the way the palm faced
        public double t;                // hit: host time
        public V3 up = new V3(0, 1, 0); // start: the room's up in the display frame
        public float deskHeight = -0.25f;
        public V3 eye = new V3(0, 0.05f, -0.6f);   // start / hit: where the user's head is
        public bool room;               // start: a court on the desk (the partner is out in the room), not through a screen
        public float width, height, depth;   // start, room: the size of that court
    }

    public struct BallSample
    {
        public Vector3 pos, vel;
        public int bouncesFront, bouncesBack;   // floor bounces on the room side / inside the screen since the stroke
        public bool crossed;                    // has passed through the glass since the stroke
        public bool dead;                       // out of play
        public string why;                      // "" | double bounce | hit the frame | flew out | stopped
        public bool Front => pos.Z < 0;
    }

    /// <summary>
    /// The ball. Its path is a pure function of the stroke and the time, so the PC and the headset - which each draw
    /// only their side of the glass - show one ball without sending its position back and forth, and the host can tell
    /// where it will be. A soft, slow ball: low gravity, modest speeds.
    /// </summary>
    public static partial class Tennis
    {
        public const float Gravity = 1.0f;          // m/s^2: a floaty ball that fits through the screen in an arc
        public const float Radius = 0.016f;
        public static float BallRadius(string mode) => mode == "keeper" ? 0.047f : Radius;
        public const float FloorBounce = 0.74f, FloorDrag = 0.90f, WallBounce = 0.78f;
        public const float PartnerDepth = 0.30f;    // where the partner stands behind the glass
        public const float NetHeight = 0.045f;      // the net of a court on the desk
        /// <summary>How far behind the glass (or the net) the partner stands on this court.</summary>
        public static float Baseline(TennisCourt c) => c.baseline > 0.01f ? c.baseline : PartnerDepth;
        public const float PartnerReach = 0.135f;   // how high above its floor it can strike
        public const float PartnerSpeed = 0.42f;    // m/s along its baseline while playing
        public const float PaddleRadius = 0.085f;   // the user's palm as a racket
        public const int PointsToWin = 5;

        static float TimeToPlane(Vector3 p, Vector3 v, Vector3 g, Vector3 n, float d)
        {
            // n.p + n.v t + 0.5 n.g t^2 = d, smallest t > 0 with the ball moving into the plane
            float a = 0.5f * Vector3.Dot(n, g), b = Vector3.Dot(n, v), c = Vector3.Dot(n, p) - d;
            if (Math.Abs(a) < 1e-9f) return b < -1e-9f && c > 0 ? -c / b : float.PositiveInfinity;
            float disc = b * b - 4f * a * c;
            if (disc < 0) return float.PositiveInfinity;
            float s = (float)Math.Sqrt(disc);
            float t1 = (-b - s) / (2f * a), t2 = (-b + s) / (2f * a);
            float lo = Math.Min(t1, t2), hi = Math.Max(t1, t2);
            // the root that counts is one in the future at which the ball is coming DOWN onto the plane (just after a
            // bounce the ball is on the plane and leaving it: that moment is not a hit, the next time down is)
            if (lo > 1e-4f && b + 2f * a * lo < 0) return lo;
            if (hi > 1e-4f && b + 2f * a * hi < 0) return hi;
            return float.PositiveInfinity;
        }

        /// <summary>
        /// When one coordinate of the ball reaches a value while moving in the given direction (+1 / -1). Gravity may have
        /// a part along any axis (the display can lean), so this is a quadratic, not a straight line.
        /// </summary>
        static float TimeToCoord(float p, float v, float g, float target, int dir)
        {
            float a = 0.5f * g, c = p - target;
            if (Math.Abs(a) < 1e-9f)
            {
                if (Math.Abs(v) < 1e-9f || Math.Sign(v) != dir) return float.PositiveInfinity;
                float t = -c / v;
                return t > 1e-5f ? t : float.PositiveInfinity;
            }
            float disc = v * v - 4f * a * c;
            if (disc < 0) return float.PositiveInfinity;
            float s = (float)Math.Sqrt(disc);
            float t1 = (-v - s) / (2f * a), t2 = (-v + s) / (2f * a);
            float lo = Math.Min(t1, t2), hi = Math.Max(t1, t2);
            if (lo > 1e-5f && Math.Sign(v + g * lo) == dir) return lo;
            if (hi > 1e-5f && Math.Sign(v + g * hi) == dir) return hi;
            return float.PositiveInfinity;
        }

        /// <summary>Where the ball is at host time t.</summary>
        public static BallSample Evaluate(TennisCourt c, TennisShot s, double t)
        {
            var o = new BallSample { pos = s.p0, vel = s.v0, why = "" };
            float left = (float)(t - s.t0);
            if (left <= 0) return o;
            Vector3 up = M.SafeNormalize(c.up, Vector3.UnitY), g = -Gravity * up;
            float hw = c.width * 0.5f - c.radius, hh = c.height * 0.5f - c.radius;
            bool rest = false;
            for (int ev = 0; ev < 24 && left > 0 && !rest; ev++)
            {
                Vector3 p = o.pos, v = o.vel;
                bool front = p.Z < 0 || (Math.Abs(p.Z) < 1e-6f && v.Z < 0);
                float tGlass = TimeToCoord(p.Z, v.Z, g.Z, 0f, front ? 1 : -1);
                float tFloor = front ? TimeToPlane(p, v, g, up, c.deskHeight + c.radius) : TimeToPlane(p, v, g, Vector3.UnitY, c.FloorBack + c.radius);
                float tWall = float.PositiveInfinity; int wall = 0;
                if (!front && !c.room)
                {
                    float tw = TimeToCoord(p.X, v.X, g.X, hw, 1); if (tw < tWall) { tWall = tw; wall = 1; }
                    tw = TimeToCoord(p.X, v.X, g.X, -hw, -1); if (tw < tWall) { tWall = tw; wall = 1; }
                    tw = TimeToCoord(p.Z, v.Z, g.Z, c.depth - c.radius, 1); if (tw < tWall) { tWall = tw; wall = 2; }
                }
                float step = Math.Min(left, Math.Min(tGlass, Math.Min(tFloor, tWall)));
                o.pos = p + v * step + 0.5f * g * step * step;
                o.vel = v + g * step;
                left -= step;
                if (left <= 0) break;
                if (step == tGlass)
                {
                    o.pos.Z = 0;
                    if (c.room)
                    {
                        // a court on the desk: over the net is through, into it is the end of the stroke
                        if (o.pos.Y < c.FloorBack + NetHeight)
                        {
                            if (!o.dead) { o.dead = true; o.why = "into the net"; }
                            o.vel = new Vector3(o.vel.X * 0.3f, o.vel.Y * 0.3f, -o.vel.Z * 0.2f);
                            o.pos.Z = front ? -1e-4f : 1e-4f;
                        }
                        else { o.crossed = true; o.pos.Z = front ? 1e-4f : -1e-4f; }
                    }
                    else if (Math.Abs(o.pos.X) > hw || Math.Abs(o.pos.Y) > hh)
                    {
                        // outside the opening: from the room it strikes the bezel and drops; from inside it has left over the top
                        if (!o.dead) { o.dead = true; o.why = front ? "hit the frame" : "flew out"; }
                        if (front) { o.vel = new Vector3(o.vel.X * 0.3f, o.vel.Y * 0.3f, -Math.Abs(o.vel.Z) * 0.25f); o.pos.Z = -1e-4f; }
                        else o.pos.Z = -1e-4f;
                    }
                    else { o.crossed = true; o.pos.Z = front ? 1e-4f : -1e-4f; }
                }
                else if (step == tFloor)
                {
                    Vector3 n = front ? up : Vector3.UnitY;
                    float vn = Vector3.Dot(o.vel, n);
                    Vector3 tangent = (o.vel - n * vn) * FloorDrag;
                    if (front) o.bouncesFront++; else o.bouncesBack++;
                    // on the desk the partner's half has lines, not walls: a ball that lands beyond them is out
                    if (c.room && !front && !o.dead && (Math.Abs(o.pos.X) > hw + 2f * c.radius || o.pos.Z > c.depth)) { o.dead = true; o.why = "out"; }
                    if ((front ? o.bouncesFront : o.bouncesBack) >= 2 && !o.dead) { o.dead = true; o.why = "double bounce"; }
                    if (-vn * FloorBounce < 0.12f)
                    {
                        // too little left to leave the ground: it comes to rest
                        o.vel = Vector3.Zero; rest = true;
                        if (!o.dead) { o.dead = true; o.why = "stopped"; }
                    }
                    else o.vel = tangent - n * vn * FloorBounce;
                }
                else if (wall == 1) o.vel.X = -o.vel.X * WallBounce;
                else if (wall == 2) o.vel.Z = -o.vel.Z * WallBounce;
            }
            // far behind the user, or far to the side in the room: nobody is going to reach it
            if (!o.dead && o.pos.Z < 0 && (o.pos.Z < -1.3f || Math.Abs(o.pos.X) > 1.2f)) { o.dead = true; o.why = "flew out"; }
            return o;
        }

        /// <summary>Where the partner strikes its serve from, standing at partnerX on its baseline.</summary>
        public static Vector3 ServePoint(TennisCourt c, float partnerX) => new Vector3(partnerX, c.FloorBack + (c.radius > Radius ? c.radius + 0.008f : 0.105f), Baseline(c) - 0.03f);

        /// <summary>The velocity that takes a ball from one point to another in T seconds under the game's gravity.</summary>
        public static Vector3 VelocityFor(TennisCourt c, Vector3 from, Vector3 to, float T)
        {
            Vector3 up = M.SafeNormalize(c.up, Vector3.UnitY);
            return (to - from) / T + 0.5f * Gravity * T * up;
        }

        /// <summary>Does this stroke go through the opening of the screen (and not into its frame or over it)?</summary>
        public static bool PassesGlass(TennisCourt c, TennisShot s, out double tCross)
        {
            tCross = 0;
            bool startFront = s.p0.z < 0;
            for (double t = s.t0; t < s.t0 + 5.0; t += 0.01)
            {
                var b = Evaluate(c, s, t);
                if (b.dead && !b.crossed) return false;
                if (b.crossed && b.Front != startFront) { tCross = t; return true; }
            }
            return false;
        }

        /// <summary>
        /// The partner's stroke towards the user: from where it strikes the ball to a spot in front of the user's chest,
        /// in a time short enough for the arc to stay inside the opening. aim is -1..1, left to right of the user.
        /// </summary>
        public static TennisShot PartnerShot(TennisCourt c, int seq, Vector3 from, Vector3 eye, float aim, double t)
        {
            Vector3 up = M.SafeNormalize(c.up, Vector3.UnitY);
            float tz = M.Clamp(eye.Z * 0.62f, -0.50f, -0.30f);
            float tx = M.Clamp(eye.X * 0.6f + aim * 0.11f, -0.26f, 0.26f);
            // chest height: a forearm's length below the eyes, but never down at the desk
            float chest = Vector3.Dot(eye, up) - 0.27f;
            float h = M.Clamp(chest, c.deskHeight + 0.14f, c.deskHeight + 0.42f);
            Vector3 flat = new Vector3(tx, 0, tz);
            Vector3 target = flat + up * (h - Vector3.Dot(flat, up));
            var s = new TennisShot { seq = seq, by = "partner", p0 = from, t0 = t };
            for (float T = 1.35f; T >= 0.6f; T -= 0.05f)
            {
                s.v0 = VelocityFor(c, from, target, T);
                if (PassesGlass(c, s, out _)) return s;
            }
            s.v0 = VelocityFor(c, from, target, 0.6f);
            return s;
        }

        /// <summary>
        /// The ball after the user's hand has met it. The hand is small and the screen is a narrow target, so the stroke is
        /// helped: it always heads for the partner's half, lands in front of the partner, and the user steers it - left
        /// or right by the way the hand moves and faces, harder by swinging faster.
        /// </summary>
        public static TennisShot UserShot(TennisCourt c, int seq, Vector3 hit, Vector3 swing, Vector3 normal, double t)
        {
            float steer = M.Clamp(swing.X * 0.22f + normal.X * 0.10f, -0.5f, 0.5f);
            float tx = M.Clamp(hit.X * 0.25f + steer, -(c.width * 0.5f - 0.07f), c.width * 0.5f - 0.07f);
            float power = M.Clamp01((swing.Length() - 0.15f) / 1.4f);
            Vector3 target = new Vector3(tx, c.FloorBack + c.radius, M.Lerp(0.15f, 0.24f, power));      // first bounce, in front of the partner
            var s = new TennisShot { seq = seq, by = "user", p0 = hit, t0 = t };
            for (float T = M.Lerp(1.25f, 0.80f, power); T >= 0.45f; T -= 0.05f)
            {
                s.v0 = VelocityFor(c, hit, target, T);
                if (PassesGlass(c, s, out _)) return s;
            }
            return s;       // from here no arc fits through the opening: the stroke is played as it is and strikes the frame
        }

        /// <summary>
        /// Can the partner get to this ball? Looks along its path inside the screen for the first moment it is low enough
        /// to strike and the partner (now at partnerX, moving at PartnerSpeed) can be under it. False = it will miss.
        /// </summary>
        public static bool PlanReturn(TennisCourt c, TennisShot s, float partnerX, double now, out double hitAt, out Vector3 hitPos)
        {
            hitAt = 0; hitPos = default;
            float floor = c.FloorBack;
            for (double t = Math.Max(now + 0.22, s.t0); t < s.t0 + 6.0; t += 0.02)
            {
                var b = Evaluate(c, s, t);
                if (b.dead) return false;
                if (b.Front || !b.crossed) continue;
                if (b.pos.Z < 0.10f || b.pos.Z > c.depth - 0.05f) continue;
                float h = b.pos.Y - floor;
                if (h < 0.025f || h > PartnerReach) continue;
                if (b.bouncesBack == 0 && b.vel.Y > 0) continue;                      // still rising off the user's stroke: wait for it to come down
                if (Math.Abs(b.pos.X - partnerX) > PartnerSpeed * (t - now) + 0.045f) continue;   // cannot run that far in time
                hitAt = t; hitPos = b.pos; return true;
            }
            return false;
        }

        /// <summary>Does a palm (centre, facing, as a disc of PaddleRadius) meet the ball?</summary>
        public static bool PaddleHits(Vector3 ball, Vector3 palm, Vector3 normal, float radius = Radius)
        {
            Vector3 n = M.SafeNormalize(normal, Vector3.UnitZ), d = ball - palm;
            float off = Vector3.Dot(d, n);
            if (Math.Abs(off) > radius + 0.035f) return false;
            return (d - n * off).Length() <= PaddleRadius + radius;
        }
    }
}
