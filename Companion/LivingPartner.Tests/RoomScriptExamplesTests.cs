using System.Numerics;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests
{
    /// <summary>
    /// The worked scripts (RoomScriptExamples) run for real: each one passes the checker without a warning and does what
    /// it says; the checker says what is wrong with scripts that are wrong; the text for the writer (RoomScriptApi.Doc)
    /// and the functions that exist are one and the same.
    /// </summary>
    public class RoomScriptExamplesTests
    {
        const float Dt = RoomScriptTests.Dt;

        public static IEnumerable<object[]> All()
        {
            foreach (var e in RoomScriptExamples.Made) yield return new object[] { "made", e.id };
            foreach (var e in RoomScriptExamples.Shipped) yield return new object[] { "shipped", e.id };
            foreach (var e in RoomScriptExamples.Skills) yield return new object[] { "skill", e.id };
        }

        static RoomScriptExamples.Example Find(string set, string id)
        {
            var list = set == "made" ? RoomScriptExamples.Made : set == "shipped" ? RoomScriptExamples.Shipped : RoomScriptExamples.Skills;
            foreach (var e in list) if (e.id == id) return e;
            throw new Exception("no example " + set + " " + id);
        }

        static RoomCheckResult Check(string set, string id) { var e = Find(set, id); return RoomScriptCheck.Run(e.script, e.skill ? null : RoomScriptExamples.Item(e)); }

        [Theory]
        [MemberData(nameof(All))]
        public void EveryExamplePassesTheCheckerWithoutAWarning(string set, string id)
        {
            var r = Check(set, id);
            Assert.True(r.ok, r.Report());
            Assert.True(r.warnings.Count == 0, r.Report());
            Assert.Contains(r.phases, p => p.ran && p.visible > 0);
            Assert.True(r.peakInstructions < LuaSandbox.DefaultBudget / 10, "a call is cheap: " + r.peakInstructions);
        }

        [Theory]
        [MemberData(nameof(All))]
        public void EveryExampleIsShort(string set, string id)
        {
            var e = Find(set, id);
            int lines = e.script.Split('\n').Length;
            Assert.InRange(lines, 5, set == "made" ? 75 : 45);
            Assert.StartsWith("-- ", e.script);             // it says what it is in its first line
            Assert.DoesNotContain("\r", e.script);
        }

        [Theory]
        [MemberData(nameof(All))]
        public void FiveMinutesWithWildHandsNeverBreakAnExample(string set, string id)
        {
            var e = Find(set, id);
            var room = new FakeRoom(11) { wild = true };
            room.things.Add(new RoomThingInfo { id = "t", name = "Desk", kind = "desk", uses = new[] { "table" }, x = 0.12f, z = 0.06f, size = 0.11f, top = 0.07f });
            room.things.Add(new RoomThingInfo { id = "c", name = "Chair", kind = "chair", uses = new[] { "sit" }, x = -0.1f, z = 0.06f, size = 0.075f, seat = 0.035f });
            var rs = new RoomScript(e.script, e.skill ? null : RoomScriptExamples.Item(e), room, 5);
            var rng = new GlassRng(); rng.Seed(3);
            var pose = new Pose();
            rs.Make();
            float dt = 1f / 30f; int steps = (int)(300f / dt);
            for (int i = 0; i < steps; i++)
            {
                // a user who presses everything all the time, also in the middle of what is going on
                float dice = rng.Next01();
                if (dice < 0.010f) rs.Use();
                else if (dice < 0.016f && rs.Offers.Count > 0) rs.Act(rs.Offers[rng.Int(rs.Offers.Count)].name);
                else if (dice < 0.019f) rs.Wear();
                else if (dice < 0.022f) rs.Unwear();
                else if (dice < 0.024f) rs.Stop();
                else if (dice < 0.025f) rs.SetPlace(-0.3f + 0.6f * rng.Next01(), 0f, -0.2f + 0.4f * rng.Next01(), 0f);
                room.Step(dt, rs); rs.Tick(dt);
                if (rs.Pet.motion.Length > 0) rs.MotionPose(pose);
                Assert.True(rs.Ok, "after " + (i * dt).ToString("0.0") + " s: " + rs.Error + " (line " + rs.ErrorLine + ")");
            }
            int on = 0; foreach (var o in rs.Objects) if (o.on) on++;
            Assert.True(on <= 3, "objects do not pile up: " + on);
            rs.Stop();
            Assert.False(rs.Pet.active);
        }

        // ------------------------------------------------------------------ the bat: the example the user named
        [Fact]
        public void TheBat_IsWornInTheRightHand_AndASwingHitsThePitchedBallAway()
        {
            var r = Check("made", "bat");
            Assert.True(r.ok, r.Report());
            Assert.True(r.wearable); Assert.Equal("handR", r.slot);
            Assert.Single(r.offers); Assert.Equal("swing", r.offers[0].name); Assert.Equal("Swing!", r.offers[0].label);
            Assert.Equal(new[] { "swing" }, r.motions);
            var wear = r.Phase("wear");
            Assert.True(wear.ran); Assert.True(wear.Did("pet_say", "Batter up!")); Assert.True(wear.Did("sound", "ta-da"));
            var act = r.Phase("worn: act swing");
            Assert.True(act.ran);
            Assert.True(act.Did("make", "ball"));
            Assert.True(act.Did("put")); Assert.True(act.Did("throw"));
            Assert.True(act.Did("pet_face_user"));
            Assert.True(act.Did("pet_play", "swing"));
            Assert.True(act.Did("sound", "hit")); Assert.True(act.Did("sparks")); Assert.True(act.Did("pet_say", "Home run!"));
            Assert.True(act.Did("pet_pose", "cheer"));
            Assert.True(act.Did("remove", "#1"));
            Assert.True(act.Did("done"));
            // in order: the pitch, the swing, the hit, the cheer, the ball gone
            float pitch = act.When("throw"), swing = act.When("pet_play"), hit = act.When("sound", "hit"), cheer = act.When("pet_pose", "cheer"), gone = act.When("remove");
            Assert.True(pitch < swing && swing < hit && hit < cheer && cheer < gone, r.Report());
            // the bat meets the ball in the middle of the swing: between "wound up" and "followed through"
            float u = (hit - swing) / 0.7f;
            Assert.InRange(u, 0.38f, 0.62f);
            Assert.InRange(act.seconds, 3.0f, 4.5f);
            // not worn yet: the act takes the bat in the hand by itself
            var first = r.Phase("act swing");
            Assert.True(first.Did("pet_hold", "handR"));
            Assert.Equal("", r.Phase("unwear").ran ? "" : "unwear did not run");
        }

        [Fact]
        public void TheBat_TheHitBallFliesAwayTowardsTheUserAndIsGoneAfterwards()
        {
            var e = Find("made", "bat");
            var room = new FakeRoom { reach = false };
            var rs = new RoomScript(e.script, RoomScriptExamples.Item(e), room, 1);
            Assert.True(rs.Wear());
            Assert.Equal("handR", rs.WornOn); Assert.Equal("handR", room.worn);
            RoomScriptTests.Run(rs, room, 3f, untilIdle: true);
            Assert.True(rs.Act("swing"));
            var ball = rs.Objects[0];
            Assert.True(ball.on); Assert.True(ball.flying);
            Assert.True(ball.z < room.petZ - 0.4f, "the pitch starts on the user's side");
            float minZ = 9f, maxY = 0f; bool sawSwing = false; int hitAt = -1;
            for (int i = 0; i < 400 && rs.Active; i++)
            {
                room.Step(Dt, rs); rs.Tick(Dt);
                if (rs.Pet.motion == "swing") sawSwing = true;
                if (hitAt < 0 && room.sounds.Contains("hit")) hitAt = i;
                if (hitAt >= 0 && ball.on) { minZ = Math.Min(minZ, ball.z); maxY = Math.Max(maxY, ball.y); }
            }
            Assert.True(sawSwing);
            Assert.True(hitAt > 0, "the ball was hit");
            Assert.True(minZ < room.petZ - 0.35f, "it flew away towards the user: " + minZ);
            Assert.True(maxY > 0.10f, "in an arc: " + maxY);
            Assert.False(ball.on);                          // tidied up
            Assert.False(rs.Active); Assert.False(rs.Pet.active);
            Assert.Equal("handR", rs.WornOn);               // the bat stays in the hand
            Assert.Equal(2, rs.Pet.face == 0 ? 2 : rs.Pet.face);
        }

        [Fact]
        public void TheBat_TheSwingTakesTheRightHandFromBehindAcrossTheFront()
        {
            var e = Find("made", "bat");
            var rs = new RoomScript(e.script, RoomScriptExamples.Item(e), new FakeRoom(), 1);
            Assert.True(rs.Ok, rs.Error);
            var pose = new Pose(); var pos = new Vector3[Skeleton.N]; var rot = new Quaternion[Skeleton.N];
            Vector3 Hand(float u)
            {
                Assert.True(rs.MotionPose("swing", u, pose));
                Skeleton.FK(pose, pos, rot);
                int f = (int)Bone.ArmLR;
                return pos[f] + M.Rot(rot[f], new Vector3(0, -Skeleton.ArmLower, 0));      // the fingertips of the right hand
            }
            // the character faces +z, its right is +x (character units: its height is 1)
            Vector3 rest = Hand(0f), back = Hand(0.35f), contact = Hand(0.55f), through = Hand(0.8f), end = Hand(1f);
            float shoulderZ = 0.04f;
            Assert.True(back.Z < shoulderZ - 0.08f, "wound up: the hand is behind the body (" + back.Z + ")");
            Assert.True(back.X > 0.15f, "on its right side (" + back.X + ")");
            Assert.True(contact.Z > shoulderZ + 0.08f, "at the ball: the hand is out in front (" + contact.Z + ")");
            Assert.True(through.X < back.X - 0.15f, "followed through: across to the left (" + back.X + " -> " + through.X + ")");
            Assert.True(through.Z > back.Z + 0.15f);
            Assert.True((end - rest).Length() < 0.03f, "it ends where it began");
            if (Environment.GetEnvironmentVariable("S3_DUMP") == "1") { var sb = new System.Text.StringBuilder(); for (int i = 0; i <= 20; i++) { var h = Hand(i / 20f); sb.AppendLine((i / 20f).ToString("0.00") + "  x " + h.X.ToString("0.000") + "  y " + h.Y.ToString("0.000") + "  z " + h.Z.ToString("0.000")); } File.WriteAllText("C:/Users/dlfgu/Documents/GitHub/MetaVR_Hack/artifacts/tmp/s3/swing.txt", sb.ToString()); }
            // one sweep: the hand goes forward all the way from the wind-up to the contact, and its path is long - a swing, not a twitch
            float path = 0f; Vector3 last = Hand(0f); float lastZ = back.Z;
            for (int i = 1; i <= 100; i++)
            {
                float u = i / 100f; Vector3 h = Hand(u);
                Assert.True((h - last).Length() < 0.12f, "no jump at u = " + u);
                if (u > 0.40f && u <= 0.50f) { Assert.True(h.Z >= lastZ - 0.004f, "forward at u = " + u); lastZ = h.Z; }
                path += (h - last).Length(); last = h;
                Assert.True(LuaMotion.HandClearance(pose, true) >= 0.99f, "the hand stays out of the bonnet at u = " + u);
            }
            Assert.True(path > 0.7f, "the hand travels " + path + " body heights");
        }

        // ------------------------------------------------------------------ the other made examples
        [Fact]
        public void TheWand_SwishesStarsComeOutABigStarFloatsUpAndPops()
        {
            var r = Check("made", "wand");
            Assert.True(r.ok, r.Report());
            Assert.True(r.wearable); Assert.Equal("handR", r.slot);
            Assert.Equal("wave", r.offers[0].name);
            var act = r.Phase("worn: act wave");
            Assert.True(act.Did("pet_play", "swish"));
            Assert.True(act.Did("make", "star"));
            Assert.True(act.Did("sparks")); Assert.True(act.Did("sound", "sparkle")); Assert.True(act.Did("sound", "pop"));
            Assert.True(act.Did("remove", "#1"));
            Assert.True(act.Count("sparks") >= 5, "stars while it swishes: " + act.Count("sparks"));

            var e = Find("made", "wand");
            var room = new FakeRoom { reach = false };
            var rs = new RoomScript(e.script, RoomScriptExamples.Item(e), room, 1);
            rs.Wear(); RoomScriptTests.Run(rs, room, 3f, untilIdle: true);
            rs.Act("wave");
            float y0 = rs.Objects[0].y;
            RoomScriptTests.Run(rs, room, 2f);
            Assert.True(rs.Objects[0].on);
            Assert.True(rs.Objects[0].y > y0 + 0.06f, "the star floats up");
            RoomScriptTests.Run(rs, room, 5f, untilIdle: true);
            Assert.False(rs.Objects[0].on);
            Assert.False(rs.Active); Assert.False(rs.TimersRepeat);
        }

        [Fact]
        public void TheDrum_ThePartnerGoesToItAndDrumsInRhythm()
        {
            var r = Check("made", "drum");
            Assert.True(r.ok, r.Report());
            Assert.False(r.wearable); Assert.Empty(r.offers);
            var use = r.Phase("use");
            Assert.True(use.Did("pet_goto")); Assert.True(use.Did("pet_play", "drumming"));
            Assert.Equal(16, use.Count("sound hammer") + use.Count("sound land"));
            Assert.Equal(16, use.Count("me_bounce"));
            Assert.True(use.Did("sound", "ta-da")); Assert.True(use.Did("pet_pose", "cheer")); Assert.True(use.Did("done"));
            // the blows come 0.3 s apart
            var times = new List<float>();
            foreach (var c in use.calls) if (c.Contains(" sound hammer") || c.Contains(" sound land")) times.Add(float.Parse(c.Substring(0, c.IndexOf(' ')), System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 1; i < times.Count; i++) Assert.InRange(times[i] - times[i - 1], 0.25f, 0.36f);
            Assert.True(use.When("pet_goto") < use.When("pet_play"));
        }

        [Fact]
        public void TheFootball_GoesToTheUsersHandAndComesBackWhenAFingerTouchesIt()
        {
            var e = Find("made", "football");
            var room = new FakeRoom();                      // (its fingertip goes for whatever comes near)
            var rs = new RoomScript(e.script, RoomScriptExamples.Item(e), room, 1);
            var log = new List<string>(); rs.Trace = (fn, what) => log.Add(fn + " " + what);
            float homeX = rs.Me.body.x, homeZ = rs.Me.body.z;
            Assert.True(rs.Use());
            float nearest = 9f; int touches = 0, kicks = 0; bool was = false;
            for (int i = 0; i < 60 * 60 && rs.Active; i++)
            {
                room.Step(Dt, rs); rs.Tick(Dt);
                nearest = Math.Min(nearest, rs.Me.body.z);
            }
            foreach (var l in log) { if (l.StartsWith("sound bounce")) touches++; if (l.StartsWith("pet_pose kick")) kicks++; }
            Assert.True(rs.Ok, rs.Error);
            Assert.True(nearest < homeZ - 0.06f, "the ball went over towards the user: " + nearest);
            Assert.Equal(5, touches);
            Assert.Equal(5, kicks);
            Assert.Contains("pet_say Good game!", log);
            Assert.False(rs.Active);
            Assert.Equal(homeX, rs.Me.body.x, 2); Assert.Equal(homeZ, rs.Me.body.z, 2);      // and it is back where it lay
            Assert.False(was);

            // nobody touches it: after ten seconds the partner is let go
            var room2 = new FakeRoom { reach = false, handSeen = false };
            var rs2 = new RoomScript(e.script, RoomScriptExamples.Item(e), room2, 1);
            rs2.Use();
            float t = RoomScriptTests.Run(rs2, room2, 60f, untilIdle: true);
            Assert.InRange(t, 10f, 16f);
            Assert.True(rs2.IdledOut); Assert.False(rs2.Pet.active);
        }

        [Fact]
        public void TheLamp_GoesOnAndOff()
        {
            var e = Find("made", "lamp");
            var room = new FakeRoom();
            var rs = new RoomScript(e.script, RoomScriptExamples.Item(e), room, 1);
            Assert.Equal("", rs.Me.glow);
            rs.Use(); Assert.Equal("#FFE08A", rs.Me.glow); Assert.Equal("cheer", rs.Pet.pose);
            RoomScriptTests.Run(rs, room, 5f, untilIdle: true);
            Assert.Equal("#FFE08A", rs.Me.glow);            // it stays on when the script is idle
            rs.Use(); Assert.Equal("", rs.Me.glow);
            rs.Use(); Assert.Equal("#FFE08A", rs.Me.glow);
            Assert.True(e.script.Split('\n').Length < 20, "a small script is a fine script");
        }

        // ------------------------------------------------------------------ the things that come with the partner
        [Fact]
        public void EveryBuiltInKindHasAScript()
        {
            foreach (var k in ItemKinds.All) Assert.True(RoomScriptExamples.ForKind(k.id).Length > 0, k.id);
            Assert.Equal(ItemKinds.All.Length, RoomScriptExamples.Shipped.Length);
            Assert.True(RoomScriptExamples.ForSkill("skill-dance").Length > 0);
            Assert.Equal("", RoomScriptExamples.ForKind("nothing"));
        }

        [Fact]
        public void TheChair_IsSatOn_AndWithATableBesideItThePartnerStudies()
        {
            var e = Find("shipped", "chair");
            var item = RoomScriptExamples.Item(e);
            var room = new FakeRoom();
            var rs = new RoomScript(e.script, item, room, 1);
            rs.Use();
            RoomScriptTests.Run(rs, room, 3f);
            Assert.Equal("sit", rs.Pet.on); Assert.Equal("", rs.Pet.onId); Assert.Equal("sit", rs.Pet.pose); Assert.Equal(2, rs.Pet.face);
            Assert.Equal(item.x, room.petX, 2); Assert.Equal(item.z, room.petZ, 2);
            RoomScriptTests.Run(rs, room, 60f);
            Assert.True(rs.Active); Assert.Equal("sit", rs.Pet.on);     // it stays seated
            rs.Use();                                                   // pressed again: it gets up
            Assert.False(rs.Pet.active); Assert.Equal("", rs.Pet.on);
            RoomScriptTests.Run(rs, room, 0.2f);
            Assert.False(rs.Active);

            room.things.Add(new RoomThingInfo { id = "desk", name = "Desk", kind = "desk", uses = new[] { "table" }, x = item.x, z = item.z - 0.08f, size = 0.11f, top = 0.07f });
            rs.Use();
            RoomScriptTests.Run(rs, room, 3f);
            Assert.Equal("study", rs.Pet.pose); Assert.Equal("sit", rs.Pet.on);
            Assert.Equal(1, rs.Pet.face); Assert.Equal(item.z - 0.08f, rs.Pet.fz, 3);     // it faces the table
            rs.Stop();
            Assert.False(rs.Pet.active); Assert.False(rs.Active);
        }

        [Fact]
        public void TheDesk_SendsThePartnerToTheChairBesideIt_TheBedIsSleptOn_TheBookIsReadAndPutBack()
        {
            var desk = Find("shipped", "desk"); var item = RoomScriptExamples.Item(desk);
            var room = new FakeRoom();
            room.things.Add(new RoomThingInfo { id = "chair-9", name = "Chair", kind = "chair", uses = new[] { "sit" }, x = item.x, z = item.z + 0.08f, size = 0.075f, seat = 0.035f });
            var rs = new RoomScript(desk.script, item, room, 1);
            rs.Use(); RoomScriptTests.Run(rs, room, 4f);
            Assert.Equal("sit", rs.Pet.on); Assert.Equal("chair-9", rs.Pet.onId); Assert.Equal("study", rs.Pet.pose);
            Assert.Equal(item.z + 0.08f, room.petZ, 2);
            rs.Stop();

            var bed = Find("shipped", "bed");
            room = new FakeRoom(); rs = new RoomScript(bed.script, RoomScriptExamples.Item(bed), room, 1);
            rs.Use(); RoomScriptTests.Run(rs, room, 4f);
            Assert.Equal("lie", rs.Pet.on); Assert.Equal("sleep", rs.Pet.pose);
            rs.Use();
            Assert.Equal("", rs.Pet.on); Assert.Contains("Good morning!", room.said);

            var book = Find("shipped", "book"); item = RoomScriptExamples.Item(book);
            room = new FakeRoom(); rs = new RoomScript(book.script, item, room, 1);
            rs.Use(); RoomScriptTests.Run(rs, room, 4f);
            Assert.Equal("handL", rs.WornOn); Assert.Equal("read", rs.Pet.pose);
            rs.Use();
            Assert.Equal("", rs.WornOn);
            Assert.Equal(item.x, rs.Me.body.x, 3); Assert.Equal(item.z, rs.Me.body.z, 3);      // back where it lay
        }

        [Fact]
        public void TheBallIsKickedAbout_TheRecordPlayerPutsARecordOn_TheDanceSkillTurnsTheRoomIntoAClub()
        {
            var ball = Find("shipped", "ball"); var item = RoomScriptExamples.Item(ball);
            var room = new FakeRoom { reach = false };
            var rs = new RoomScript(ball.script, item, room, 1);
            var log = new List<string>(); rs.Trace = (fn, what) => log.Add(fn + " " + what);
            rs.Use();
            float maxOff = 0f;
            for (int i = 0; i < 60 * 40 && rs.Active; i++) { room.Step(Dt, rs); rs.Tick(Dt); maxOff = Math.Max(maxOff, Math.Abs(rs.Me.body.x - item.x)); }
            Assert.False(rs.Active);
            Assert.Equal(4, log.FindAll(l => l.StartsWith("pet_pose kick")).Count);
            Assert.True(maxOff > 0.15f, "it rolled: " + maxOff);
            Assert.Equal(item.x, rs.Me.body.x, 1);          // right, left, right, left: it ends where it began

            var rec = Find("shipped", "record");
            room = new FakeRoom(); rs = new RoomScript(rec.script, RoomScriptExamples.Item(rec), room, 1);
            Assert.Equal(4, rs.Offers.Count);
            rs.Use(); Assert.Equal("song-lofi", room.music);
            rs.Act("club"); Assert.Equal("song-club", room.music);
            rs.Use(); Assert.Equal("", room.music);
            rs.Act("lullaby"); Assert.Equal("song-lullaby", room.music);
            rs.Act("stop"); Assert.Equal("", room.music);

            var dance = Find("skill", "skill-dance");
            room = new FakeRoom { petX = 0.2f }; rs = new RoomScript(dance.script, null, room, 1);
            Assert.True(rs.Use());
            Assert.Equal("amb-club", room.ambience); Assert.Equal("song-club", room.music);
            RoomScriptTests.Run(rs, room, 30f);
            Assert.Equal("dance", rs.Pet.pose); Assert.True(rs.Active);
            Assert.Equal(0f, room.petX, 2);                 // it dances on its home spot
            rs.Stop();
            Assert.Equal("", room.ambience); Assert.Equal("", room.music);
            Assert.False(rs.Active); Assert.False(rs.Pet.active);
        }

        // ------------------------------------------------------------------ the checker says what is wrong
        static RoomCheckResult Run(string lua, float seconds = 30f) => RoomScriptCheck.Run(lua, RoomScriptTests.Thing(), seconds);

        [Fact]
        public void TheCheckerReportsAnErrorWithItsLineAndWhereItHappened()
        {
            var r = Run("offer('a', 'A')\nfunction on_use() sound('tap') end\nfunction on_act(n)\n  local x = nil\n  x.y = 1\nend\n");
            Assert.False(r.ok);
            Assert.Equal(5, r.errorLine); Assert.Equal("act a", r.errorIn);
            Assert.Contains("nil", r.error);
            Assert.Contains("ERROR in act a, line 5", r.Report());
            r = Run("function on_use(");
            Assert.False(r.ok); Assert.Equal("load", r.errorIn); Assert.True(r.errorLine >= 1);
            r = Run("function on_use() while true do end end");
            Assert.False(r.ok); Assert.Contains("too long", r.error); Assert.Equal("use", r.errorIn);
            r = RoomScriptCheck.Run(null, null);
            Assert.False(r.ok);
        }

        [Theory]
        [InlineData("function on_use() local a = 1 end", "use: the script does nothing that is seen or heard")]
        [InlineData("x = 1", "defines none of the hooks")]
        [InlineData("function on_make() sound('pop') end", "pressing the thing does nothing")]
        [InlineData("motion('swing', 1, function(u, p) end) function on_use() pet_pose('cheer') end", "the motion 'swing' is defined but never played")]
        [InlineData("function on_use() local b = make('ball', 0.02, 'red'); throw(b, 0.2, 0.3, 0) end", "an object that was thrown (the ball #1) is still lying there")]
        [InlineData("function on_use() pet_goto(3, 0) end", "off its desk area")]
        [InlineData("function on_use() sound('tap') end function update(dt) me_turn(time() * 90) end", "update() never calls done()")]
        [InlineData("function on_use() every(0.5, function() sound('tick') end) end", "every() is never cancelled")]
        [InlineData("offer('fly', 'Fly') function on_use() sound('tap') end", "there is no on_act(name)")]
        [InlineData("function on_use() sound('tap') end function on_act(n) sound('pop') end", "on_act is defined but nothing is offered")]
        [InlineData("function on_use() sound('tap') end function on_wear() sound('pop') end", "the thing is not wearable")]
        [InlineData("function on_use() sound('kaboom') end", "there is no sound 'kaboom'")]
        [InlineData("wearable('handR') function on_wear() pet_drop(); sound('tap') end", "the thing is not on handR after it was put on")]
        [InlineData("function on_use() sound('tap'); for i = 1, 30 do make('ball', 0.01, 'red') end end", "24 little objects at most")]
        public void TheCheckerWarns(string lua, string warning)
        {
            var r = Run(lua, 12f);
            Assert.True(r.ok, r.Report());
            Assert.Contains(r.warnings, w => w.Contains(warning));
            Assert.Contains("WARNING: ", r.Report());
        }

        [Fact]
        public void SomethingMeantToGoOnUntilItIsStoppedIsNotCalledEndless_WhenItSaysHowItStops()
        {
            var r = Run("function on_use() pet_pose('dance', 600); every(1, function() sparks(0, 0.1, 0) end) end function on_stop() pet_free() end", 8f);
            Assert.True(r.ok); Assert.Empty(r.warnings);
            Assert.True(r.Phase("use").endless);
            Assert.NotNull(r.Phase("stop use"));
            r = Run("function on_use() pet_pose('sit', 600) end", 8f);      // a long pose ends by itself
            Assert.True(r.ok); Assert.Empty(r.warnings);
        }

        [Fact]
        public void TheCheckIsTheSameEveryTime()
        {
            foreach (var e in RoomScriptExamples.Made)
            {
                string a = RoomScriptCheck.Run(e.script, RoomScriptExamples.Item(e), 30f, 4).Report(), b = RoomScriptCheck.Run(e.script, RoomScriptExamples.Item(e), 30f, 4).Report();
                Assert.Equal(a, b);
                Assert.True(a.Length > 80);
            }
        }

        // ------------------------------------------------------------------ the text for the writer
        [Fact]
        public void TheDocAndTheFunctionsThatExistAreTheSame()
        {
            string doc = RoomScriptApi.Doc;
            // every function a script finds is described ...
            var rs = RoomScriptTests.New("x = 1", new FakeRoom());
            var real = new List<string>();
            foreach (var name in rs.FunctionNames())
            {
                if (name.Contains('.') || Array.IndexOf(LuaSandbox.AllowedGlobals, name) >= 0) continue;      // the standard library
                real.Add(name);
                Assert.True(doc.Contains(name + "("), "the Doc does not describe " + name + "()");
            }
            var listed = new List<string>(RoomScriptApi.Functions); listed.AddRange(LuaSandbox.CommonNames); listed.AddRange(LuaMotion.Functions);
            listed.Sort(string.CompareOrdinal); real.Sort(string.CompareOrdinal);
            Assert.Equal(listed, real);
            // ... and every function the Doc describes exists: a line of the FUNCTIONS part that begins with "  name("
            bool inFunctions = false; int described = 0;
            foreach (var raw in doc.Split('\n'))
            {
                if (raw.StartsWith("FUNCTIONS")) inFunctions = true;
                if (raw.StartsWith("RULES")) inFunctions = false;
                if (!inFunctions || !raw.StartsWith("  ") || raw.StartsWith("   ")) continue;
                int paren = raw.IndexOf('(');
                if (paren < 3) continue;
                string name = raw.Substring(2, paren - 2);
                bool ident = name.Length > 0; foreach (char c in name) if (!(char.IsLetter(c) || c == '_')) ident = false;
                if (!ident) continue;
                described++;
                Assert.True(real.Contains(name), "the Doc describes " + name + "(), which does not exist");
            }
            Assert.True(described >= RoomScriptApi.Functions.Length, "described: " + described);
            // the names a script may use
            foreach (var h in RoomScript.Hooks) Assert.Contains(" " + h + "(", doc);
            foreach (var p in RoomScriptApi.Poses) Assert.Contains(" " + p, doc);
            foreach (var s in SfxSynth.Names) Assert.Contains(" " + s, doc);
            foreach (var s in RoomScript.Shapes) Assert.Contains("\"" + s + "\"", doc);
            foreach (var s in RoomScript.Slots) Assert.Contains("\"" + s + "\"", doc);
            foreach (var c in LuaSandbox.ColorNames) Assert.Contains(" " + c, doc);
            foreach (var u in ItemUses.All) Assert.Contains(" " + u, doc);
            foreach (var a in WorldDefaults.Ambiences()) Assert.Equal(a.id, RoomScriptApi.AmbienceId(a.id.Substring(4)));
            foreach (var s in WorldDefaults.Songs()) Assert.Equal(s.id, RoomScriptApi.SongId(s.id.Substring(5)));
            foreach (var a in WorldDefaults.Ambiences()) Assert.Contains(" " + a.id.Substring(4), doc);
            foreach (var s in WorldDefaults.Songs()) Assert.Contains(" " + s.id.Substring(5), doc);
            // the numbers it gives are the runtime's
            Assert.Contains("gravity 1.5", doc); Assert.Equal(1.5f, RoomScript.Gravity);
            Assert.Contains("x -0.40 .. 0.40, z -0.22 .. 0.25", doc);
            Assert.Equal(-0.40f, RoomScript.PetMinX); Assert.Equal(0.40f, RoomScript.PetMaxX); Assert.Equal(-0.22f, RoomScript.PetMinZ); Assert.Equal(0.25f, RoomScript.PetMaxZ);
            Assert.Contains("1.2 m of the home spot", doc); Assert.Equal(1.2f, RoomScript.Box);
            Assert.Contains("24 at most", doc); Assert.Equal(24, RoomScript.MaxObjects);
            Assert.Contains("8 timers", doc); Assert.Equal(8, RoomScript.MaxTimers);
            Assert.Contains("4 at a time", doc); Assert.Equal(4, RoomScript.MaxLabels);
            Assert.Contains("for 10 s", doc); Assert.Equal(10f, RoomScript.IdleSeconds);
            Assert.Contains("0.12 m in a second", doc); Assert.Equal(0.12f, RoomScript.WalkSpeed);
            Assert.Contains(LuaMotion.Doc.Replace("\r", ""), doc); Assert.DoesNotContain("\r", doc);
            Assert.True(doc.Length < 14000, "the Doc fits a prompt: " + doc.Length);
        }

        [Fact]
        public void TheExamplesUseOnlyWhatTheDocDescribes_AndTheFewShotTextHasTheBat()
        {
            string few = RoomScriptExamples.FewShot();
            Assert.Contains("야구배트", few); Assert.Contains("motion(\"swing\"", few); Assert.Contains("wearable(\"handR\")", few);
            var rs = RoomScriptTests.New("x = 1", new FakeRoom());
            var known = new HashSet<string>(rs.FunctionNames());
            foreach (var set in new[] { RoomScriptExamples.Made, RoomScriptExamples.Shipped, RoomScriptExamples.Skills })
                foreach (var e in set)
                {
                    var r = RoomScriptCheck.Run(e.script, e.skill ? null : RoomScriptExamples.Item(e), 5f);
                    Assert.True(r.ok, e.id + ": " + r.Report());
                    if (!e.skill) Assert.Equal(e.script, RoomScriptExamples.Item(e).script);
                }
        }
    }
}
