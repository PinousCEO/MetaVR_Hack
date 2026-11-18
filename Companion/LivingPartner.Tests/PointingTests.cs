using System.Numerics;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// The hand ray the Quest client builds from tracked joints (PointingRay), checked against hands whose joints are
/// constructed for a known target. These tests cover the geometry of the ray; they cannot say how accurately a real
/// person's hand is tracked or how people really hold a pointing hand - that needs the headset.
/// </summary>
public class PointingTests
{
    static readonly Vector3 Head = new(0, 1.20f, 0);

    /// <summary>Where a ray from origin along dir meets the screen plane z = depth.</summary>
    static Vector3 Hit(Vector3 origin, Vector3 dir, float depth) => origin + dir * ((depth - origin.Z) / dir.Z);

    /// <summary>A hand held out so that shoulder, knuckle and target are in one line, the index finger pointing on along it.</summary>
    static (Vector3 shoulder, Vector3 knuckle, Vector3 tip, Vector3 palmForward) StraightArm(Vector3 target, float side, float reach = 0.55f)
    {
        Vector3 shoulder = PointingRay.Shoulder(Head, 0, side);
        Vector3 dir = Vector3.Normalize(target - shoulder);
        Vector3 knuckle = shoulder + dir * reach;
        return (shoulder, knuckle, knuckle + dir * 0.08f, dir);
    }

    [Theory]
    [InlineData(0.0f, 1.10f, 1f)]      // straight ahead
    [InlineData(0.45f, 1.35f, 1f)]     // up and to the right
    [InlineData(-0.50f, 0.95f, 1f)]    // across the body, low
    [InlineData(-0.30f, 1.30f, -1f)]   // left hand
    public void A_straight_arm_points_exactly_at_its_target(float x, float y, float side)
    {
        var target = new Vector3(x, y, 0.65f);
        var h = StraightArm(target, side);
        Vector3 aim = PointingRay.Aim(h.shoulder, h.knuckle, h.tip, h.palmForward);
        Assert.True(Vector3.Distance(Hit(h.knuckle, aim, target.Z), target) < 0.001f);
    }

    [Fact]
    public void A_bent_index_finger_does_not_move_the_ray()
    {
        var target = new Vector3(0.2f, 1.25f, 0.65f);
        var h = StraightArm(target, 1f);
        // the finger curls down by 60 degrees (as it does on the way to a pinch): it is no longer "held out", so it is ignored
        Vector3 curled = h.knuckle + Vector3.Normalize(Vector3.Lerp(h.palmForward, -Vector3.UnitY, 0.6f)) * 0.08f;
        Vector3 aim = PointingRay.Aim(h.shoulder, h.knuckle, curled, h.palmForward);
        Assert.True(Vector3.Distance(Hit(h.knuckle, aim, target.Z), target) < 0.001f);
    }

    [Fact]
    public void With_a_bent_elbow_the_ray_lies_between_the_shoulder_line_and_the_finger()
    {
        // the forearm and finger point 15 degrees to the left of the shoulder-to-knuckle line: the two cues disagree
        Vector3 shoulder = PointingRay.Shoulder(Head, 0, 1f);
        Vector3 knuckle = shoulder + Vector3.Normalize(new Vector3(0.05f, -0.05f, 1f)) * 0.40f;
        Vector3 line = Vector3.Normalize(knuckle - shoulder);
        Vector3 finger = Vector3.Normalize(Vector3.Transform(line, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -15f * M.Deg2Rad)));
        Vector3 aim = PointingRay.Aim(shoulder, knuckle, knuckle + finger * 0.08f, finger);
        float fromLine = (float)Math.Acos(Math.Clamp(Vector3.Dot(aim, line), -1f, 1f)) * M.Rad2Deg;
        float fromFinger = (float)Math.Acos(Math.Clamp(Vector3.Dot(aim, finger), -1f, 1f)) * M.Rad2Deg;
        Assert.InRange(fromLine, 4.5f, 6.0f);              // 35 % of the way towards the finger
        Assert.InRange(fromFinger, 9.0f, 10.5f);
        // what that disagreement costs on a screen 60 cm away: the two cues are 16 cm apart there, the ray is 5-6 cm from the shoulder line
        float spread = Vector3.Distance(Hit(knuckle, line, 0.6f), Hit(knuckle, finger, 0.6f));
        float off = Vector3.Distance(Hit(knuckle, line, 0.6f), Hit(knuckle, aim, 0.6f));
        Assert.InRange(off / spread, 0.30f, 0.40f);
    }

    [Fact]
    public void The_shoulder_follows_the_heading_of_the_head()
    {
        Vector3 ahead = PointingRay.Shoulder(Head, 0, 1f), turned = PointingRay.Shoulder(Head, 90, 1f);
        Assert.True(ahead.X > Head.X + 0.15f && Math.Abs(ahead.Z - Head.Z) < 0.05f);      // right shoulder is to the right
        Assert.True(turned.Z < Head.Z - 0.15f && Math.Abs(turned.X - Head.X) < 0.05f);    // facing +X, the right shoulder is towards -Z
        Assert.True(PointingRay.Shoulder(Head, 0, -1f).X < Head.X - 0.15f);
    }

    [Fact]
    public void Closing_the_fingers_for_a_pinch_holds_the_ray_still()
    {
        Vector3 held = Vector3.Normalize(new Vector3(0.1f, 0.05f, 1f));
        Vector3 dragged = Vector3.Normalize(new Vector3(0.1f, -0.15f, 1f));      // the hand dips as the fingers close
        Vector3 ray = held;
        for (int i = 0; i < 30; i++) ray = PointingRay.Step(ray, dragged, 0.02f, 1f / 72f);
        Assert.Equal(held, ray);
        // open hand again: the ray follows, and is there within a quarter of a second
        for (int i = 0; i < 18; i++) ray = PointingRay.Step(ray, dragged, 0.09f, 1f / 72f);
        Assert.True(Vector3.Dot(ray, dragged) > 0.9999f);
    }

    [Fact]
    public void The_first_frame_takes_the_aim_as_it_is()
    {
        Vector3 want = Vector3.Normalize(new Vector3(-0.2f, 0.1f, 1f));
        Assert.Equal(want, PointingRay.Step(Vector3.Zero, want, 0.02f, 1f / 72f));
    }
}

/// <summary>Which spot an utterance refers to, from the stream of pointing samples (PointingDwell).</summary>
public class PointingDwellTests
{
    static PointerSample S(double t, float u, float v, bool pinch = false, bool valid = true, string mon = "A") => new() { t = t, u = u, v = v, pinch = pinch, valid = valid, monitorId = mon };

    [Fact]
    public void The_centre_of_the_dwell_is_returned_not_a_sample_on_its_edge()
    {
        var h = new List<PointerSample>();
        // the hand comes in from the left for half a second, then rests at (0.72, 0.46) with a little tremor
        for (int i = 0; i < 8; i++) h.Add(S(10 + i / 15.0, 0.50f + i * 0.027f, 0.40f + i * 0.0075f));
        var rnd = new Random(7);
        for (int i = 0; i < 45; i++) h.Add(S(10.6 + i / 15.0, 0.72f + (float)(rnd.NextDouble() - 0.5) * 0.004f, 0.46f + (float)(rnd.NextDouble() - 0.5) * 0.004f));
        var p = PointingDwell.Resolve(h, 10.0, 13.5);
        Assert.NotNull(p);
        Assert.InRange(p.u, 0.72f - 0.0015f, 0.72f + 0.0015f);     // within 3 px on a 1920-wide display
        Assert.InRange(p.v, 0.46f - 0.0015f, 0.46f + 0.0015f);
    }

    [Fact]
    public void A_pinch_during_the_utterance_decides()
    {
        var h = new List<PointerSample>();
        for (int i = 0; i < 30; i++) h.Add(S(5 + i / 15.0, 0.2f, 0.2f));
        for (int i = 0; i < 5; i++) h.Add(S(7.1 + i / 15.0, 0.8f, 0.6f, pinch: true));
        var p = PointingDwell.Resolve(h, 5.0, 7.5);
        Assert.Equal(0.8f, p.u, 3); Assert.Equal(0.6f, p.v, 3);
    }

    [Fact]
    public void Only_the_time_of_the_utterance_counts()
    {
        var h = new List<PointerSample>();
        for (int i = 0; i < 60; i++) h.Add(S(1 + i / 15.0, 0.1f, 0.1f));          // long before
        for (int i = 0; i < 20; i++) h.Add(S(20 + i / 15.0, 0.6f, 0.7f));         // while speaking
        for (int i = 0; i < 60; i++) h.Add(S(30 + i / 15.0, 0.9f, 0.9f));         // long after
        var p = PointingDwell.Resolve(h, 20.0, 21.3);
        Assert.Equal(0.6f, p.u, 3); Assert.Equal(0.7f, p.v, 3);
    }

    [Fact]
    public void Not_pointing_at_a_screen_gives_nothing()
    {
        var h = new List<PointerSample>();
        for (int i = 0; i < 30; i++) h.Add(S(5 + i / 15.0, 0, 0, valid: false));
        Assert.Null(PointingDwell.Resolve(h, 5.0, 7.0));
        Assert.Null(PointingDwell.Resolve(new List<PointerSample>(), 5.0, 7.0));
        Assert.Null(PointingDwell.Resolve(null, 5.0, 7.0));
    }

    [Fact]
    public void The_dwell_is_found_on_the_display_it_happened_on()
    {
        var h = new List<PointerSample>();
        for (int i = 0; i < 10; i++) h.Add(S(5 + i / 15.0, 0.3f, 0.3f, mon: "A"));
        for (int i = 0; i < 25; i++) h.Add(S(5.7 + i / 15.0, 0.3f, 0.3f, mon: "B"));
        Assert.Equal("B", PointingDwell.Resolve(h, 5.0, 7.5).monitorId);
    }
}
