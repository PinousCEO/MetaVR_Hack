using System.Numerics;
using LivingPartner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace LivingPartner.Tests;

public class CoordinateTests
{
    // The developer machine's real layout: a portrait display at negative coordinates, a primary, and one above it.
    static MonitorDesc Portrait => new() { id = @"\\.\DISPLAY1", bounds = new RectI(-1080, -832, 1080, 1920), work = new RectI(-1080, -832, 1080, 1872), dpiScale = 1.0f, rotation = 90, physWidthM = 0.336f, physHeightM = 0.597f };
    static MonitorDesc Primary => new() { id = @"\\.\DISPLAY2", bounds = new RectI(0, 0, 1920, 1080), work = new RectI(0, 0, 1920, 1032), dpiScale = 1.25f, primary = true, physWidthM = 0.597f, physHeightM = 0.336f };
    static MonitorDesc Upper => new() { id = @"\\.\DISPLAY3", bounds = new RectI(0, -1080, 1920, 1080), work = new RectI(0, -1080, 1920, 1080), dpiScale = 1.5f, physWidthM = 0.527f, physHeightM = 0.296f };

    [Fact]
    public void UvPixelRoundTrip_NegativeCoordinatesAndRotation()
    {
        foreach (var m in new[] { Portrait, Primary, Upper })
            foreach (var (u, v) in new[] { (0f, 0f), (1f, 1f), (0.5f, 0.5f), (0.13f, 0.87f) })
            {
                ScreenMath.UVToPixel(m, u, v, out float px, out float py);
                Assert.InRange(px, m.bounds.x, m.bounds.Right);
                Assert.InRange(py, m.bounds.y, m.bounds.Bottom);
                ScreenMath.PixelToUV(m, px, py, out float u2, out float v2);
                Assert.Equal(u, u2, 4); Assert.Equal(v, v2, 4);
            }
        ScreenMath.UVToPixel(Portrait, 0, 0, out float x0, out float y0);
        Assert.Equal(-1080f, x0); Assert.Equal(-832f, y0);
    }

    [Fact]
    public void PixelMetresRoundTrip_DifferentPixelPitchPerMonitor()
    {
        foreach (var m in new[] { Portrait, Primary, Upper })
        {
            ScreenMath.PhysicalSize(m, null, out float w, out float h, out string src);
            Assert.Equal("edid", src);
            var local = ScreenMath.PixelToLocal(m, w, h, m.bounds.x + 100, m.bounds.y + 50);
            ScreenMath.LocalToPixel(m, w, h, local, out float px, out float py);
            Assert.Equal(m.bounds.x + 100, px, 2); Assert.Equal(m.bounds.y + 50, py, 2);
            // centre of the display is the frame origin; +y is up while pixel y grows downwards
            var c = ScreenMath.PixelToLocal(m, w, h, m.bounds.x + m.bounds.w * 0.5f, m.bounds.y + m.bounds.h * 0.5f);
            Assert.True(c.Length() < 1e-5f);
            Assert.True(ScreenMath.PixelToLocal(m, w, h, m.bounds.x, m.bounds.y).Y > 0);
        }
        // the same physical length is a different number of pixels on each display (no shared scale is assumed)
        float mppA = Primary.physWidthM / Primary.bounds.w, mppB = Upper.physWidthM / Upper.bounds.w;
        Assert.NotEqual(mppA, mppB, 6);
    }

    [Fact]
    public void CalibrationOverridesEdid_AndDefaultIsLabelled()
    {
        var m = Primary;
        var c = new MonitorCalibration { monitorId = m.id, widthM = 0.60f, heightM = 0.3375f };
        ScreenMath.PhysicalSize(m, c, out float w, out _, out string src);
        Assert.Equal("calibration", src); Assert.Equal(0.60f, w);
        m.physWidthM = 0; m.physHeightM = 0;
        ScreenMath.PhysicalSize(m, null, out _, out _, out src);
        Assert.Equal("default", src);
    }

    static MonitorFrame TiltedFrame(float yawDeg, float pitchDeg, Vector3 centre, float w, float h, out Vector3 tl, out Vector3 tr, out Vector3 bl, out Vector3 br)
    {
        var rot = M.Euler(pitchDeg, yawDeg, 0);
        Vector3 rx = M.Rot(rot, Vector3.UnitX) * (w * 0.5f), ry = M.Rot(rot, Vector3.UnitY) * (h * 0.5f);
        tl = centre - rx + ry; tr = centre + rx + ry; bl = centre - rx - ry; br = centre + rx - ry;
        return MonitorFrame.FromCorners(tl, tr, bl, br);
    }

    [Fact]
    public void MonitorFrame_FromCorners_RecoversSizeAxesAndIsOrthonormal()
    {
        var f = TiltedFrame(24, -8, new Vector3(-0.5f, 1.1f, 0.55f), 0.336f, 0.597f, out var tl, out var tr, out var bl, out var br);
        Assert.Equal(0.336f, f.widthM, 4); Assert.Equal(0.597f, f.heightM, 4);
        Assert.Equal(0, Vector3.Dot(f.x, f.y), 5); Assert.Equal(0, Vector3.Dot(f.x, f.z), 5); Assert.Equal(1, f.z.Length(), 5);
        Assert.True(f.Residual(tl, tr, bl, br) < 1e-5f);
        // +z points INTO the screen: a viewer standing in front (negative local z) sees x to the right and y up
        var viewer = f.ToWorld(new Vector3(0, 0, -0.6f));
        Assert.True(Vector3.Dot(f.origin - viewer, f.z) > 0);
        var p = new Vector3(0.3f, 1.4f, -0.2f);
        Assert.True(Vector3.Distance(p, f.ToWorld(f.ToLocal(p))) < 1e-5f);
    }

    [Fact]
    public void MrRay_To_Pixel_To_WindowClient_Chain()
    {
        var m = Upper;
        var f = TiltedFrame(0, 9, new Vector3(0, 1.36f, 0.6f), m.physWidthM, m.physHeightM, out _, out _, out _, out _);
        // aim a ray from the head at a known point of the display and follow the whole chain
        float u = 0.25f, v = 0.7f;
        var world = f.ToWorld(ScreenMath.UVToLocal(f.widthM, f.heightM, u, v));
        var head = new Vector3(0.05f, 1.25f, -0.1f);
        Assert.True(f.Raycast(head, Vector3.Normalize(world - head), out var local, out float dist));
        Assert.True(dist > 0.3f);
        ScreenMath.LocalToUV(f.widthM, f.heightM, local, out float u2, out float v2);
        Assert.Equal(u, u2, 4); Assert.Equal(v, v2, 4);
        ScreenMath.UVToPixel(m, u2, v2, out float px, out float py);
        Assert.Equal(480f, px, 1); Assert.Equal(-1080f + 756f, py, 1);
        var client = new RectI(200, -1000, 800, 600);
        ScreenMath.PixelToClient(client, px, py, out float cx, out float cy, out float nu, out float nv);
        Assert.Equal(280f, cx, 1); Assert.Equal(676f, cy, 1);
        ScreenMath.ClientToPixel(client, nu, nv, out float px2, out float py2);
        Assert.Equal(px, px2, 2); Assert.Equal(py, py2, 2);
        // a ray pointing away from the display never hits it
        Assert.False(f.Raycast(head, Vector3.Normalize(head - world), out _, out _));
    }

    [Fact]
    public void PhysicalArrangementIsNotAssumedToMatchWindowsArrangement()
    {
        // Windows says Upper is directly above Primary; physically we place it to the RIGHT. The mapping must follow calibration only.
        var fPrimary = TiltedFrame(0, 0, new Vector3(0, 1.0f, 0.6f), 0.597f, 0.336f, out _, out _, out _, out _);
        var fUpper = TiltedFrame(-30, 0, new Vector3(0.62f, 1.0f, 0.45f), 0.527f, 0.296f, out _, out _, out _, out _);
        var head = new Vector3(0, 1.2f, 0);
        var aim = fUpper.ToWorld(new Vector3(0, 0, 0));
        Assert.True(fUpper.Raycast(head, Vector3.Normalize(aim - head), out var local, out _));
        ScreenMath.LocalToUV(fUpper.widthM, fUpper.heightM, local, out float u, out float v);
        ScreenMath.UVToPixel(Upper, u, v, out float px, out float py);
        Assert.Equal(960f, px, 0); Assert.Equal(-540f, py, 0);   // lands in Upper's pixel rectangle, wherever it physically is
        Assert.True(Upper.bounds.Contains(px, py)); Assert.False(Primary.bounds.Contains(px, py));
    }

    [Fact]
    public void ClampPortal_ReportsAdjustment()
    {
        var allowed = new RectI(-1080, -832, 1080, 1872);
        Assert.False(ScreenMath.ClampPortal(allowed, 130, -500, 300, out float x, out float y));
        Assert.Equal(-500f, x); Assert.Equal(300f, y);
        Assert.True(ScreenMath.ClampPortal(allowed, 130, -1075, -830, out x, out y));
        Assert.Equal(-950f, x); Assert.Equal(-702f, y);
    }

    [Fact]
    public void OffAxisProjection_IsSymmetricOnlyWhenEyeIsCentred()
    {
        ScreenMath.OffAxis(new Vector3(0, 0, -0.6f), -0.1f, 0.1f, -0.1f, 0.1f, 0.02f, out float l, out float r, out float b, out float t);
        Assert.Equal(-l, r, 6); Assert.Equal(-b, t, 6);
        ScreenMath.OffAxis(new Vector3(0.2f, 0.1f, -0.6f), -0.1f, 0.1f, -0.1f, 0.1f, 0.02f, out l, out r, out b, out t);
        Assert.True(-l > r && -b > t);
    }
}

public class CrossingPlanTests
{
    readonly ITestOutputHelper _out;
    public CrossingPlanTests(ITestOutputHelper o) { _out = o; }

    public static IEnumerable<object[]> Geometries()
    {
        foreach (int dir in new[] { 0, 1 })
            foreach (float mrFootY in new[] { -0.09f, -0.2f, -0.35f, -0.55f })
                foreach (float pcFootY in new[] { -0.09f, -0.2f, -0.45f })
                    foreach (float tilt in new[] { 0f, 10f })
                        yield return new object[] { dir, mrFootY, pcFootY, tilt };
    }

    static CrossingPlan Make(int dir, float mrFootY, float pcFootY, float tilt, float H = 0.10f)
    {
        // world up as seen from a display tilted about its horizontal axis; the MR standing point lies on the (gravity-level) desk
        Vector3 upLocal = M.Rot(M.Inverse(M.Euler(tilt, 0, 0)), Vector3.UnitY);
        Vector3 horiz = Vector3.Normalize(Vector3.UnitZ - upLocal * Vector3.Dot(Vector3.UnitZ, upLocal));
        Vector3 mrFoot = upLocal * mrFootY - horiz * PlanBuilder.LaunchDistance(H, -mrFootY);
        return PlanBuilder.Build("t", "c", dir, H, mrFoot, upLocal, pcFootY, 0.5f);
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void StartsAndEndsExactlyOnTheStandingPoints(int dir, float mrFootY, float pcFootY, float tilt)
    {
        var p = Make(dir, mrFootY, pcFootY, tilt);
        var s = new CharState();
        PlanEvaluator.Evaluate(p, 0, s);
        Assert.True(Vector3.Distance(s.RootPos, p.From) < 1e-4f, "start root");
        PlanEvaluator.Evaluate(p, p.Total, s);
        Assert.True(Vector3.Distance(s.RootPos, p.To) < 1e-4f, "end root");
        // ends upright for the side it is on (real gravity in MR, screen-up in the PC) and facing the viewer
        Vector3 upEnd = dir == 0 ? Vector3.UnitY : p.MrUp;
        Assert.True(Vector3.Distance(M.Rot(s.RootRot, Vector3.UnitY), upEnd) < 1e-3f);
        Assert.True(Vector3.Dot(M.Rot(s.RootRot, Vector3.UnitZ), -Vector3.UnitZ) > 0.95f);
        // and starts upright for the side it leaves
        PlanEvaluator.Evaluate(p, 0, s);
        Assert.True(Vector3.Distance(M.Rot(s.RootRot, Vector3.UnitY), dir == 0 ? p.MrUp : Vector3.UnitY) < 1e-3f);
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void NoTeleport_RootAndBodyMoveContinuously(int dir, float mrFootY, float pcFootY, float tilt)
    {
        var p = Make(dir, mrFootY, pcFootY, tilt);
        var s = new CharState();
        var pts = new Vector3[Skeleton.Samples.Length]; var prev = new Vector3[pts.Length];
        const float dt = 1f / 120f;
        float maxStep = 0, maxAngle = 0; Quaternion prevRot = Quaternion.Identity;
        for (float t = 0; t <= p.Total; t += dt)
        {
            PlanEvaluator.Evaluate(p, t, s);
            PlanEvaluator.SamplePoints(p, s, pts);
            if (t > 0)
            {
                for (int i = 0; i < pts.Length; i++) maxStep = Math.Max(maxStep, Vector3.Distance(pts[i], prev[i]));
                float dot = Math.Abs(Quaternion.Dot(prevRot, s.RootRot));
                maxAngle = Math.Max(maxAngle, 2f * (float)Math.Acos(Math.Min(1f, dot)) * M.Rad2Deg);
            }
            Array.Copy(pts, prev, pts.Length); prevRot = s.RootRot;
        }
        // 3.2 cm per 1/120 s = 3.8 m/s is far above anything the motion needs; a teleport would show up as a much larger step
        Assert.True(maxStep < 0.032f, $"largest body-point step {maxStep * 1000:0.0} mm in 1/120 s");
        Assert.True(maxAngle < 14f, $"largest root rotation step {maxAngle:0.0} deg in 1/120 s");
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void BodyPassesSequentially_HeadThenTorsoThenLegsThenFeet(int dir, float mrFootY, float pcFootY, float tilt)
    {
        var p = Make(dir, mrFootY, pcFootY, tilt);
        var s = new CharState();
        float[] first = { -1, -1, -1, -1 }, full = { -1, -1, -1, -1 };
        float prevFrac = 0; float allThrough = -1;
        for (float t = 0; t <= p.Total; t += 1f / 240f)
        {
            PlanEvaluator.Evaluate(p, t, s);
            var ti = PlanEvaluator.Through(p, s);
            float[] part = { ti.head, ti.torso, ti.legs, ti.feet };
            for (int i = 0; i < 4; i++)
            {
                if (first[i] < 0 && part[i] > 0) first[i] = t;
                if (full[i] < 0 && part[i] >= 1f) full[i] = t;
            }
            // while playing forward, a part that has gone through never comes back out
            Assert.True(ti.fraction >= prevFrac - 1e-6f, $"body re-emerged at tau={t:0.000} ({prevFrac:0.00} -> {ti.fraction:0.00})");
            prevFrac = ti.fraction;
            if (allThrough < 0 && ti.fraction >= 1f) allThrough = t;
        }
        Assert.True(first[0] > p.tPrepare, "nothing crosses during the crouch");
        Assert.True(first[0] <= first[1] && first[1] <= first[2] && first[2] <= first[3], $"entry order head/torso/legs/feet = {string.Join(", ", first.Select(x => x.ToString("0.000")))}");
        Assert.True(full[0] <= full[1] && full[1] <= full[2] && full[2] <= full[3], $"completion order = {string.Join(", ", full.Select(x => x.ToString("0.000")))}");
        Assert.True(first[3] - first[0] > 0.15f, $"the passage is visibly sequential (head to feet spans {first[3] - first[0]:0.000}s)");
        Assert.True(allThrough > 0 && allThrough < p.Total - p.tLand, "the whole body is through before landing");
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void BodyThreadsThroughTheChosenPoint(int dir, float mrFootY, float pcFootY, float tilt)
    {
        var p = Make(dir, mrFootY, pcFootY, tilt);
        var s = new CharState();
        var pts = new Vector3[Skeleton.Samples.Length]; var prev = new Vector3[pts.Length];
        Vector3 n = Vector3.UnitZ * p.DirSign;
        float worst = 0; bool have = false;
        for (float t = 0; t <= p.Total; t += 1f / 480f)
        {
            PlanEvaluator.Evaluate(p, t, s);
            PlanEvaluator.SamplePoints(p, s, pts);
            if (have)
                for (int i = 0; i < pts.Length; i++)
                {
                    float a = Vector3.Dot(prev[i], n), b = Vector3.Dot(pts[i], n);
                    if (a <= 0 && b > 0)
                    {
                        var hit = Vector3.Lerp(prev[i], pts[i], a / (a - b));
                        worst = Math.Max(worst, hit.Length());       // distance from the portal point, measured in the display plane
                    }
                }
            Array.Copy(pts, prev, pts.Length); have = true;
        }
        Assert.True(worst <= PlanBuilder.ClearanceRadius(p.H) * 1.05f, $"a body point crossed {worst * 1000:0.0} mm from the chosen point (allowed {PlanBuilder.ClearanceRadius(p.H) * 1000:0.0} mm)");
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void FreezePointsAreOrdered_AndRewindRetracesTheSamePath(int dir, float mrFootY, float pcFootY, float tilt)
    {
        var p = Make(dir, mrFootY, pcFootY, tilt);
        float last = 0;
        foreach (float prog in new[] { 0.001f, 0.25f, 0.5f, 0.75f, 0.999f })
        {
            float tau = PlanEvaluator.TauForProgress(p, prog);
            Assert.True(tau > last, $"tau for {prog:P0} = {tau:0.000}");
            last = tau;
            var s = new CharState(); PlanEvaluator.Evaluate(p, tau, s);
            Assert.True(Math.Abs(prog - PlanEvaluator.CrossingProgress(p, s)) < 0.02f, $"progress at freeze point {prog:P0} was {PlanEvaluator.CrossingProgress(p, s):P1}");
        }
        // the plan is a pure function of tau, so a rewound clock shows exactly the frames it showed on the way in
        var a = new CharState(); var b = new CharState();
        var clock = new CrossClock { anchorHost = 100, anchorTau = 0, rate = 1 };
        float tauFwd = clock.TauAt(100 + 1.0, p.Total);
        var rew = new CrossClock { anchorHost = 101.5, anchorTau = clock.TauAt(101.5, p.Total), rate = -1 };
        float tauBack = rew.TauAt(102.0, p.Total);
        Assert.Equal(tauFwd, tauBack, 3);
        PlanEvaluator.Evaluate(p, tauFwd, a); PlanEvaluator.Evaluate(p, tauBack, b);
        Assert.True(Vector3.Distance(a.RootPos, b.RootPos) < 1e-3f);
        Assert.Equal(0f, rew.TauAt(200, p.Total));            // rewind ends whole on the source side
    }

    [Fact]
    public void VariantIsChosenFromGeometry_NotOneFixedJump()
    {
        float H = 0.10f; var up = Vector3.UnitY;
        var mid = PlanBuilder.Build("a", "c", 0, H, new Vector3(0, -0.20f, -0.22f), up, -0.3f, 0.30f);
        var high = PlanBuilder.Build("b", "c", 0, H, new Vector3(0, -0.55f, -0.40f), up, -0.4f, 0.40f);
        var nearBottom = PlanBuilder.Build("c", "c", 0, H, new Vector3(0, -0.15f, -0.20f), up, -0.09f, 0.05f);
        Assert.Equal((int)CrossVariant.DiveFlip, mid.variant);
        Assert.Equal((int)CrossVariant.RunLeap, high.variant);
        Assert.Equal((int)CrossVariant.BezelClimb, nearBottom.variant);
        Assert.True(high.tFlightIn > mid.tFlightIn, "a higher target takes a longer flight");
        Assert.True(nearBottom.slope > mid.slope, "little room to fall => steeper rising entry");
        _out.WriteLine($"mid total={mid.Total:0.00}s high total={high.Total:0.00}s bezel total={nearBottom.Total:0.00}s");
    }

    [Fact]
    public void CrossClock_HoldSlowAndStop()
    {
        var c = new CrossClock { anchorHost = 10, anchorTau = 0.5f, rate = 0 };
        Assert.Equal(0.5f, c.TauAt(99, 3));
        c = new CrossClock { anchorHost = 10, anchorTau = 0, rate = 0.25f };
        Assert.Equal(0.5f, c.TauAt(12, 3), 4);
        c = new CrossClock { anchorHost = 10, anchorTau = 0, rate = 1, hasStop = true, stopTau = 1.2f };
        Assert.Equal(1.2f, c.TauAt(50, 3), 4);
        Assert.Equal(0f, c.TauAt(9.7, 3));     // scheduled slightly in the future: both sides wait at tau 0
    }
}

public class GaitTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StanceFootDoesNotSlide(bool run)
    {
        float H = 0.10f, amp = run ? PoseLib.RunAmp : PoseLib.WalkAmp;
        float stride = PoseLib.StrideLength(amp) * H;
        var pose = new Pose(); var pos = new Vector3[Skeleton.N]; var rot = new Quaternion[Skeleton.N];
        float worst = 0;
        for (int cycle = 0; cycle < 2; cycle++)
            foreach (bool left in new[] { true, false })
            {
                float start = cycle + (left ? 0f : 0.5f); float? z0 = null;
                for (float ph = start + 0.02f; ph < start + 0.48f; ph += 0.01f)
                {
                    if (run) PoseLib.Run(pose, ph); else PoseLib.Walk(pose, ph);
                    Skeleton.FK(pose, pos, rot);
                    float rootZ = ph * stride;                                   // root advances exactly one stride per cycle
                    float footZ = rootZ + pos[(int)(left ? Bone.FootL : Bone.FootR)].Z * H;
                    z0 ??= footZ;
                    worst = Math.Max(worst, Math.Abs(footZ - z0.Value));
                }
            }
        Assert.True(worst < 0.0012f, $"stance foot slid {worst * 1000:0.00} mm over a step of {stride * 500:0.0} mm");
    }

    [Fact]
    public void StanceFootStaysOnTheGround()
    {
        var pose = new Pose(); var pos = new Vector3[Skeleton.N]; var rot = new Quaternion[Skeleton.N];
        for (float ph = 0.02f; ph < 0.48f; ph += 0.02f)
        {
            PoseLib.Walk(pose, ph);
            Skeleton.FK(pose, pos, rot);
            Assert.Equal(Skeleton.AnkleHeight, pos[(int)Bone.FootL].Y, 2);
        }
    }
}

public class TransitionMachineTests
{
    static TargetInfo Target => new() { valid = true, monitorId = "m" };

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void HappyPath_OwnershipMovesOnlyAfterTheLastFoot(int dir)
    {
        var m = new TransitionMachine();
        string src = dir == 0 ? Env.Mr : Env.Pc, dst = dir == 0 ? Env.Pc : Env.Mr;
        Assert.True(m.Begin("t1", "c", dir, src, Target, 0));
        Assert.Equal(TState.TargetSelected, m.State);
        Assert.True(m.Apply(TEvent.BeginApproach, "t1", 1)); Assert.Equal(TState.Approaching, m.State);
        Assert.True(m.Apply(TEvent.ApproachDone, "t1", 2)); Assert.Equal(TState.Approaching, m.State);   // receiver not ready yet
        Assert.False(m.Apply(TEvent.StartCrossing, "t1", 2));                                             // cannot start without the receiver
        Assert.True(m.Apply(TEvent.ReceiverReady, "t1", 3)); Assert.Equal(TState.ReceiverReady, m.State);
        Assert.True(m.Apply(TEvent.StartCrossing, "t1", 4)); Assert.Equal(TState.Crossing, m.State);
        Assert.Equal(src, m.Info.owner);                                                                   // still owned by the source while crossing
        Assert.True(m.Apply(TEvent.CrossingFinished, "t1", 5)); Assert.Equal(TState.Settling, m.State);
        Assert.Equal(dst, m.Info.owner);
        Assert.True(m.Apply(TEvent.Settled, "t1", 6)); Assert.Equal(TState.Complete, m.State);
        Assert.False(m.IsActive);
    }

    [Fact]
    public void DuplicateAndStaleMessagesNeverChangeState()
    {
        var m = new TransitionMachine();
        m.Begin("t1", "c", 0, Env.Mr, Target, 0);
        foreach (var e in new[] { TEvent.BeginApproach, TEvent.ApproachDone, TEvent.ReceiverReady, TEvent.StartCrossing, TEvent.CrossingFinished, TEvent.Settled }) m.Apply(e, "t1", 1);
        Assert.Equal(TState.Complete, m.State); int rev = m.Info.revision;
        foreach (var e in Enum.GetValues<TEvent>()) Assert.False(m.Apply(e, "t1", 2), e + " after Complete");
        Assert.Equal(TState.Complete, m.State); Assert.Equal(Env.Pc, m.Info.owner); Assert.Equal(rev, m.Info.revision);
        Assert.False(m.Apply(TEvent.Cancel, "old-transition", 3));
        Assert.False(m.Apply(TEvent.Cancel, "", 3));
        // a second identical "ready" during the approach is ignored too
        var n = new TransitionMachine(); n.Begin("t2", "c", 0, Env.Mr, Target, 0); n.Apply(TEvent.BeginApproach, "t2", 0);
        Assert.True(n.Apply(TEvent.ReceiverReady, "t2", 1)); Assert.False(n.Apply(TEvent.ReceiverReady, "t2", 1));
    }

    [Fact]
    public void CancelBeforeCrossing_KeepsTheCharacterOnTheSourceSide()
    {
        var m = new TransitionMachine(); m.Begin("t", "c", 0, Env.Mr, Target, 0); m.Apply(TEvent.BeginApproach, "t", 0);
        Assert.True(m.Apply(TEvent.Cancel, "t", 1)); Assert.Equal(TState.Cancelled, m.State); Assert.Equal(Env.Mr, m.Info.owner);
        Assert.False(m.Apply(TEvent.Cancel, "t", 2));   // duplicate cancel
    }

    [Fact]
    public void CancelDuringCrossing_RecoversToSource_NeverDuplicatesOrLoses()
    {
        var m = new TransitionMachine(); m.Begin("t", "c", 1, Env.Pc, Target, 0);
        foreach (var e in new[] { TEvent.BeginApproach, TEvent.ApproachDone, TEvent.ReceiverReady, TEvent.StartCrossing }) m.Apply(e, "t", 1);
        Assert.True(m.Apply(TEvent.Cancel, "t", 2)); Assert.Equal(TState.Recovering, m.State);
        TransitionMachine.Visibility(m.Info, out bool mr, out bool pc, out bool clipped);
        Assert.True(mr && pc && clipped);                // both draw complementary halves while rewinding
        Assert.True(m.Apply(TEvent.RecoverDone, "t", 3)); Assert.Equal(TState.Cancelled, m.State); Assert.Equal(Env.Pc, m.Info.owner);
        Assert.False(m.Apply(TEvent.RecoverDone, "t", 4));
    }

    [Fact]
    public void HoldAndResume_ReturnToTheHeldState_AndTooLateCancelIsIgnored()
    {
        var m = new TransitionMachine(); m.Begin("t", "c", 0, Env.Mr, Target, 0); m.Apply(TEvent.BeginApproach, "t", 0);
        Assert.True(m.Apply(TEvent.Hold, "t", 1, "window minimised")); Assert.Equal(TState.Hold, m.State);
        Assert.True(m.Apply(TEvent.Resume, "t", 2)); Assert.Equal(TState.Approaching, m.State);
        foreach (var e in new[] { TEvent.ApproachDone, TEvent.ReceiverReady, TEvent.StartCrossing, TEvent.CrossingFinished }) m.Apply(e, "t", 3);
        Assert.Equal(TState.Settling, m.State);
        Assert.False(m.Apply(TEvent.Cancel, "t", 4));    // the body is already through: it stays in the destination
        Assert.Equal(Env.Pc, m.Info.owner);
    }

    [Fact]
    public void AtNoPointDoBothSidesShowAWholeCharacter()
    {
        var m = new TransitionMachine(); m.Begin("t", "c", 0, Env.Mr, Target, 0);
        var seq = new[] { TEvent.BeginApproach, TEvent.ApproachDone, TEvent.Hold, TEvent.Resume, TEvent.ReceiverReady, TEvent.StartCrossing, TEvent.Hold, TEvent.Resume, TEvent.CrossingFinished, TEvent.Settled };
        Check(m.Info);
        foreach (var e in seq) { m.Apply(e, "t", 1); Check(m.Info); }
        static void Check(TransitionInfo t)
        {
            TransitionMachine.Visibility(t, out bool mr, out bool pc, out bool clipped);
            Assert.True(mr || pc, "the character is never drawn by nobody: " + t.state);
            if (mr && pc) Assert.True(clipped, "both sides draw only while clipped to complementary halves: " + t.state);
        }
    }

    [Fact]
    public void CannotStartFromTheWrongSideOrWhileBusy()
    {
        var m = new TransitionMachine();
        Assert.False(m.Begin("t", "c", 0, Env.Pc, Target, 0));   // asked to leave MR but the character is in the PC
        Assert.True(m.Begin("t", "c", 1, Env.Pc, Target, 0));
        Assert.False(m.Begin("u", "c", 1, Env.Pc, Target, 0));   // already transitioning
    }
}

public class ModuleAndIntentTests
{
    [Fact]
    public void Timer_FullLifecycle_OnAbsoluteTime()
    {
        long t0 = 1_800_000_000_000;
        var m = TimerLogic.Create("mod-1", "Timer 1", 25 * 60000, t0);
        Assert.Equal("idle", m.status); Assert.Equal(25 * 60000, TimerLogic.Remaining(m, t0));
        Assert.True(TimerLogic.Start(m, t0));
        Assert.Equal(24 * 60000, TimerLogic.Remaining(m, t0 + 60000));
        Assert.True(TimerLogic.Pause(m, t0 + 60000)); Assert.False(TimerLogic.Pause(m, t0 + 61000));
        Assert.Equal(24 * 60000, TimerLogic.Remaining(m, t0 + 600000));         // paused time does not count
        Assert.True(TimerLogic.Resume(m, t0 + 600000));
        Assert.Equal(23 * 60000, TimerLogic.Remaining(m, t0 + 660000));
        Assert.True(TimerLogic.SetDuration(m, 10 * 60000, t0 + 660000));          // 2 minutes already elapsed
        Assert.Equal(8 * 60000, TimerLogic.Remaining(m, t0 + 660000));
        Assert.False(TimerLogic.Tick(m, t0 + 660000 + 8 * 60000 - 1));
        Assert.True(TimerLogic.Tick(m, t0 + 660000 + 8 * 60000)); Assert.Equal("done", m.status);
        Assert.False(TimerLogic.Tick(m, t0 + 99999999));                          // completion fires exactly once
        Assert.True(TimerLogic.Reset(m, t0)); Assert.Equal("idle", m.status); Assert.Equal(10 * 60000, m.remainingMs);
    }

    [Fact]
    public void Timer_SurvivesRestartAndFrameStalls()
    {
        long t0 = 1_800_000_000_000;
        var m = TimerLogic.Create("mod-1", "Timer 1", 25 * 60000, t0);
        TimerLogic.Start(m, t0);
        // "restart": serialise, drop, restore much later with no ticks in between
        var restored = Proto.FromJson<ModuleDef>(Proto.ToJson(m));
        Assert.Equal(m.endUnixMs, restored.endUnixMs);
        Assert.Equal(5 * 60000, TimerLogic.Remaining(restored, t0 + 20 * 60000));
        Assert.True(TimerLogic.Tick(restored, t0 + 40 * 60000));                  // finished while the app was not running: reported once on return
        Assert.Equal(0, TimerLogic.Remaining(restored, t0 + 40 * 60000));
    }

    [Theory]
    [InlineData("타이머 하나 만들어줘", "createTimer", 0, false)]
    [InlineData("타이머 하나 만들어서 25분 시작해 줘", "createTimer", 25 * 60000, true)]
    [InlineData("아까 만든 타이머 25분 시작해 줘", "timerStart", 25 * 60000, false)]
    [InlineData("타이머 일시정지", "timerPause", 0, false)]
    [InlineData("타이머 다시 시작해", "timerResume", 0, false)]
    [InlineData("타이머 초기화해 줘", "timerReset", 0, false)]
    [InlineData("타이머 10분으로 바꿔줘", "timerSetDuration", 10 * 60000, false)]
    [InlineData("여기로 들어가 봐", "enterPortal", 0, false)]
    [InlineData("이쪽으로 들어가 봐. 내가 만들고 있는 사이트인데 디자인을 좀 봐줘", "enterPortal", 0, true)]
    [InlineData("여기 디자인 어때?", "analyze", 0, false)]
    [InlineData("이제 나와", "returnToMr", 0, false)]
    [InlineData("make a timer and start it for 1 hour 30 minutes", "createTimer", 90 * 60000, true)]
    [InlineData("오늘 날씨 좋다", "unknown", 0, false)]
    public void Intents(string text, string kind, long durationMs, bool alsoStart)
    {
        var it = IntentParser.Parse(text);
        Assert.Equal(kind, it.kind);
        Assert.Equal(durationMs, it.durationMs);
        Assert.Equal(alsoStart, it.alsoStart);
    }

    [Fact]
    public void Deixis_IsFlaggedSoThePointingHistoryIsUsed()
    {
        Assert.True(IntentParser.Parse("여기로 들어가 봐").deictic);
        Assert.True(IntentParser.Parse("이 창 안에 들어가서 확인해 줘").deictic);
        Assert.False(IntentParser.Parse("타이머 시작").deictic);
    }

    [Fact]
    public void RainShelter_OnlyWhenTheHandBlocksTheRainPath()
    {
        var head = new Vector3(0, 1.0f, 0); var rain = Vector3.Normalize(new Vector3(0.12f, -1f, 0.05f));
        var above = head - rain * 0.07f;
        Assert.True(RainShelter.IsSheltered(head, rain, above, Vector3.UnitY, 0.06f, 0.30f, out float gap, out float lat));
        Assert.Equal(0.07f, gap, 2); Assert.True(lat < 0.01f);
        Assert.True(RainShelter.IsSheltered(head, rain, above, -Vector3.UnitY, 0.06f, 0.30f, out _, out _));        // palm up or down both block
        // close to the character but beside it: not an umbrella
        Assert.False(RainShelter.IsSheltered(head, rain, head + new Vector3(0.11f, -0.02f, 0), Vector3.UnitY, 0.06f, 0.30f, out _, out _));
        // above but too far to the side
        Assert.False(RainShelter.IsSheltered(head, rain, above + new Vector3(0.12f, 0, 0), Vector3.UnitY, 0.06f, 0.30f, out _, out _));
        // edge-on hand lets the rain through
        Assert.False(RainShelter.IsSheltered(head, rain, above, Vector3.UnitX, 0.06f, 0.30f, out _, out _));
        // hand below the head never shelters
        Assert.False(RainShelter.IsSheltered(head, rain, head + rain * 0.07f, Vector3.UnitY, 0.06f, 0.30f, out _, out _));
        // too high above to matter
        Assert.False(RainShelter.IsSheltered(head, rain, head - rain * 0.8f, Vector3.UnitY, 0.06f, 0.30f, out _, out _));
        // straight-down rain needs the hand straight above; slanted rain needs it shifted up-wind
        Assert.True(RainShelter.IsSheltered(head, -Vector3.UnitY, head + new Vector3(0, 0.1f, 0), Vector3.UnitY, 0.06f, 0.30f, out _, out _));
        var slant = Vector3.Normalize(new Vector3(0.7f, -1f, 0));
        Assert.False(RainShelter.IsSheltered(head, slant, head + new Vector3(0, 0.15f, 0), Vector3.UnitY, 0.06f, 0.30f, out _, out _));
        Assert.True(RainShelter.IsSheltered(head, slant, head - slant * 0.15f, Vector3.UnitY, 0.06f, 0.30f, out _, out _));
    }

    [Fact]
    public void ClockSync_ConvergesDespiteJitter()
    {
        var c = new ClockSync(); var rnd = new Random(7); double trueOffset = 1234.567;
        for (int i = 0; i < 20; i++)
        {
            double send = i * 0.2, up = 0.002 + rnd.NextDouble() * 0.015, down = 0.002 + rnd.NextDouble() * 0.015;
            c.AddSample(send, send + up + trueOffset, send + up + down);
        }
        Assert.True(c.Synced);
        Assert.True(Math.Abs(c.Offset - trueOffset) < 0.006, $"offset error {(c.Offset - trueOffset) * 1000:0.00} ms");
    }

    [Fact]
    public void Protocol_RoundTripsAndRejectsGarbage()
    {
        var plan = PlanBuilder.Build("tr-1", "partner-001", 0, 0.10f, new Vector3(0, -0.2f, -0.22f), Vector3.UnitY, -0.3f, 0.3f);
        string json = Proto.Pack(Msg.TransitionUpdate, new TransitionInfo { transitionId = "tr-1", plan = plan, clock = new CrossClock { anchorHost = 5, rate = 1 } }, "id-1", 7, 12.5);
        var env = Proto.Parse(json);
        Assert.Equal(Proto.Version, env.v); Assert.Equal("id-1", env.id); Assert.Equal(7, env.seq);
        var info = env.Body<TransitionInfo>();
        Assert.Equal(plan.Total, info.plan.Total, 5); Assert.Equal(plan.slope, info.plan.slope, 5);
        Assert.Null(Proto.Parse("not json"));
        Assert.Null(Proto.Parse("{\"type\":\"x\",\"body\":5}")?.Body<TransitionInfo>());
    }
}

public class DesignTests
{
    [Fact]
    public void Next_cycles_through_every_design_and_wraps()
    {
        string d = Designs.Ids[0];
        var seen = new System.Collections.Generic.HashSet<string> { d };
        for (int i = 0; i < Designs.Ids.Length - 1; i++) { d = Designs.Next(d); Assert.True(seen.Add(d)); }
        Assert.Equal(Designs.Ids.Length, seen.Count);
        Assert.Equal(Designs.Ids[0], Designs.Next(d));
    }

    [Fact]
    public void Next_of_an_unknown_design_is_the_first_one()
    {
        Assert.Equal(Designs.Ids[0], Designs.Next("no-such-look"));
        Assert.Equal(Designs.Ids[0], Designs.Next(null));
    }

    [Fact]
    public void The_default_character_design_is_a_known_one()
    {
        Assert.Contains(new CharacterInfo().design, Designs.Ids);
    }
}
