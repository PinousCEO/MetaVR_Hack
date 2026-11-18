using System.Numerics;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>The ball of the tennis game: one path for both renderers, through the glass, off floors and walls.</summary>
public class TennisTests
{
    static TennisCourt Court(float tiltDeg = 0)
    {
        float a = tiltDeg * M.Deg2Rad;
        // a display leaning back by tiltDeg: the room's up, seen from the display, tips towards the viewer
        return new TennisCourt { width = 0.598f, height = 0.336f, depth = 0.50f, up = new V3(0, (float)Math.Cos(a), -(float)Math.Sin(a)), deskHeight = -0.24f };
    }
    static readonly Vector3 Eye = new(0.03f, 0.12f, -0.62f);

    [Theory]
    [InlineData(0f, -1f)]
    [InlineData(12f, 1f)]
    public void A_kick_reaches_the_keeper_within_half_a_second_and_stops_at_the_ground(float tilt, float aim)
    {
        var court = Court(tilt);
        court.radius = Tennis.BallRadius("keeper");
        var from = Tennis.ServePoint(court, 0);
        var kick = Tennis.PartnerShot(court, 1, from, Eye, aim, 10, keeper: true);
        Assert.True(Tennis.PassesGlass(court, kick, out double crossing));
        var ball = Tennis.Evaluate(court, kick, 10.52);
        Assert.True(crossing < 10.36 && ball.Front && !ball.dead && ball.pos.Z < -.30f);
        for (double t = 10.52; t < 13; t += .01)
        {
            var later = Tennis.Evaluate(court, kick, t);
            Assert.Equal(0, later.bouncesFront);
            if (later.dead) return;
        }
        Assert.Fail("The shot remained in play after passing the keeper.");
    }

    [Fact]
    public void Saving_a_keeper_shot_ends_its_flight_and_scores_exactly_once()
    {
        var match = new TennisReferee();
        match.Start(Court(), "keeper", Eye, 0);
        match.Tick(2.5);
        var s = match.State;
        var kick = s.shot;
        Assert.NotNull(kick);
        for (double t = 2.5; t < 5; t += 1.0 / 90)
        {
            var ball = Tennis.Evaluate(s.court, kick, t);
            if (!ball.Front || ball.dead) continue;
            var hand = new GameCommand { seq = kick.seq, t = t, eye = Eye, normal = Vector3.UnitZ };
            Assert.True(match.Hit(hand, t));
            Assert.Equal("saved", s.note);
            Assert.Equal("point", s.phase);
            Assert.Null(s.shot);
            Assert.False(match.Hit(hand, t));
            Assert.Equal(1, s.scoreUser);
            return;
        }
        Assert.Fail("The incoming shot never reached the goalkeeper.");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(12f)]
    public void The_partners_stroke_comes_out_through_the_opening_towards_the_user(float tilt)
    {
        var c = Court(tilt);
        foreach (float px in new[] { -0.22f, 0f, 0.2f })
            foreach (float aim in new[] { -1f, 0f, 1f })
            {
                var s = Tennis.PartnerShot(c, 1, new Vector3(px, c.FloorBack + 0.09f, 0.28f), Eye, aim, 100.0);
                Assert.True(Tennis.PassesGlass(c, s, out double tc), $"px={px} aim={aim}");
                var atGlass = Tennis.Evaluate(c, s, tc);
                Assert.True(Math.Abs(atGlass.pos.X) < c.width * 0.5f && Math.Abs(atGlass.pos.Y) < c.height * 0.5f);
                // somewhere on its way it is within reach of a hand in front of the chest, before it has touched the desk
                bool reachable = false;
                for (double t = tc; t < tc + 2.5 && !reachable; t += 0.01)
                {
                    var b = Tennis.Evaluate(c, s, t);
                    if (b.bouncesFront > 0) break;
                    if (b.pos.Z < -0.25f && b.pos.Z > -0.55f && Math.Abs(b.pos.X) < 0.35f && Vector3.Dot(b.pos, c.up) > c.deskHeight + 0.08f) reachable = true;
                }
                Assert.True(reachable, $"px={px} aim={aim}");
            }
    }

    [Fact]
    public void The_users_stroke_goes_in_bounces_once_and_can_be_returned()
    {
        var c = Court(8f);
        int returned = 0, total = 0;
        foreach (float hx in new[] { -0.2f, 0f, 0.22f })
            foreach (float hy in new[] { -0.12f, 0.0f, 0.10f })
                foreach (float swingX in new[] { -0.8f, 0f, 0.8f })
                {
                    total++;
                    var s = Tennis.UserShot(c, 2, new Vector3(hx, hy, -0.38f), new Vector3(swingX, 0.1f, 0.9f), new Vector3(0, 0, 1), 50.0);
                    Assert.True(Tennis.PassesGlass(c, s, out double tc), $"hit=({hx},{hy}) swing={swingX}");
                    // it lands in the partner's half
                    BallSample landed = default;
                    for (double t = tc; t < tc + 3; t += 0.005) { landed = Tennis.Evaluate(c, s, t); if (landed.bouncesBack >= 1) break; }
                    Assert.Equal(1, landed.bouncesBack);
                    Assert.InRange(landed.pos.Z, 0.05f, c.depth);
                    if (Tennis.PlanReturn(c, s, 0f, 50.0, out double hitAt, out Vector3 hitPos))
                    {
                        returned++;
                        Assert.True(hitAt > tc && hitPos.Z > 0.09f);
                        Assert.InRange(hitPos.Y - c.FloorBack, 0.02f, Tennis.PartnerReach + 0.001f);
                    }
                }
        // from the middle of its baseline the partner gets to most balls, and not to all of them
        Assert.InRange(returned, total * 2 / 3, total);
    }

    [Fact]
    public void Swinging_sideways_steers_the_ball()
    {
        var c = Court();
        var hit = new Vector3(0, 0, -0.38f);
        float X(float swingX)
        {
            var s = Tennis.UserShot(c, 1, hit, new Vector3(swingX, 0, 0.8f), new Vector3(0, 0, 1), 0);
            for (double t = 0; t < 4; t += 0.005) { var b = Tennis.Evaluate(c, s, t); if (b.bouncesBack >= 1) return b.pos.X; }
            return float.NaN;
        }
        Assert.True(X(-1f) < X(0f) - 0.05f && X(0f) < X(1f) - 0.05f);
    }

    [Fact]
    public void A_ball_nobody_touches_bounces_twice_on_the_desk_and_is_dead()
    {
        var c = Court();
        var s = Tennis.PartnerShot(c, 1, new Vector3(0, c.FloorBack + 0.08f, 0.28f), Eye, 0, 10.0);
        BallSample b = default;
        for (double t = 10; t < 20; t += 0.01) { b = Tennis.Evaluate(c, s, t); if (b.dead) break; }
        Assert.True(b.dead);
        Assert.True(b.why == "double bounce" || b.why == "flew out");
        Assert.True(b.crossed);
        // the desk is a real floor: the ball never goes below it
        for (double t = 10; t < 20; t += 0.01) Assert.True(Vector3.Dot(Tennis.Evaluate(c, s, t).pos, c.up) >= c.deskHeight + Tennis.Radius - 0.002f);
    }

    [Fact]
    public void A_ball_aimed_past_the_screen_strikes_the_frame()
    {
        var c = Court();
        var s = new TennisShot { seq = 1, by = "user", p0 = new V3(0.45f, 0.0f, -0.4f), v0 = new V3(0, 0.35f, 0.9f), t0 = 0 };
        Assert.False(Tennis.PassesGlass(c, s, out _));
        BallSample b = default;
        for (double t = 0; t < 3; t += 0.01) { b = Tennis.Evaluate(c, s, t); if (b.dead) break; }
        Assert.Equal("hit the frame", b.why);
        Assert.True(b.pos.Z <= 0);
    }

    [Fact]
    public void Inside_the_screen_the_ball_stays_in_the_box()
    {
        var c = Court();
        var s = new TennisShot { seq = 1, by = "user", p0 = new V3(0, 0, 0.05f), v0 = new V3(1.1f, 0.5f, 1.4f), t0 = 0 };
        for (double t = 0; t < 1.2; t += 0.004)
        {
            var b = Tennis.Evaluate(c, s, t);
            if (b.Front) break;
            Assert.InRange(b.pos.X, -c.width * 0.5f, c.width * 0.5f);
            Assert.InRange(b.pos.Z, -0.001f, c.depth);
            Assert.True(b.pos.Y >= c.FloorBack + Tennis.Radius - 0.002f);
        }
    }

    [Fact]
    public void The_path_is_continuous_and_the_same_whenever_it_is_asked_for()
    {
        var c = Court(10f);
        var s = Tennis.PartnerShot(c, 3, new Vector3(0.1f, c.FloorBack + 0.1f, 0.3f), Eye, 0.4f, 7.0);
        Vector3 prev = Tennis.Evaluate(c, s, 7.0).pos;
        for (double t = 7.004; t < 11; t += 0.004)
        {
            var b = Tennis.Evaluate(c, s, t);
            Assert.True(Vector3.Distance(b.pos, prev) < 0.02f, $"jump of {Vector3.Distance(b.pos, prev) * 1000:0} mm at t={t - 7:0.000}");
            prev = b.pos;
            Assert.Equal(b.pos, Tennis.Evaluate(c, s, t).pos);      // no hidden state
        }
    }

    [Fact]
    public void The_palm_is_a_racket_of_a_known_size()
    {
        var palm = new Vector3(0.1f, 0, -0.4f); var n = new Vector3(0, 0, 1);
        Assert.True(Tennis.PaddleHits(palm + new Vector3(0.05f, 0.03f, 0.01f), palm, n));
        Assert.False(Tennis.PaddleHits(palm + new Vector3(0.14f, 0, 0), palm, n));           // beside the hand
        Assert.False(Tennis.PaddleHits(palm + new Vector3(0, 0, 0.09f), palm, n));           // still on its way
    }
}
