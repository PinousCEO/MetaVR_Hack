using System.Numerics;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests
{
    /// <summary>
    /// The runtime of the scripts of THINGS (RoomScript): when each hook is called, timers, the little objects and how
    /// they fall, the limits, errors that must not get out, and the interface the room gives a script.
    /// (The sandbox itself and the motion API are S2's and tested with them.)
    /// </summary>
    public class RoomScriptTests
    {
        internal const float Dt = 1f / 60f;

        internal static ItemDef Thing(string id = "thing-1", float x = 0.12f, float z = -0.06f, float size = 0.08f)
        {
            return new ItemDef { id = id, name = "Thing", kind = "toy", uses = new[] { "play" }, sizeM = size, inRoom = true, status = "ready", x = x, z = z };
        }

        internal static RoomScript New(string lua, FakeRoom room, ItemDef item = null, int seed = 1)
        {
            return new RoomScript(lua, item ?? Thing(), room, seed);
        }

        /// <summary>Lets the room and the script run for that long (or until the script has finished, when untilIdle).</summary>
        internal static float Run(RoomScript rs, FakeRoom room, float seconds, bool untilIdle = false, float dt = Dt)
        {
            float t = 0f;
            while (t < seconds && !(untilIdle && !rs.Active)) { room.Step(dt, rs); rs.Tick(dt); t += dt; }
            return t;
        }

        static List<string> Traced(RoomScript rs)
        {
            var list = new List<string>();
            rs.Trace = (fn, what) => list.Add(what.Length > 0 ? fn + " " + what : fn);
            return list;
        }

        // ------------------------------------------------------------------ hooks
        [Fact]
        public void EachHookIsCalledWhenItShould()
        {
            var room = new FakeRoom();
            var rs = New(@"
wearable('head')
offer('hop', 'Hop!')
function on_make() pet_say('made') end
function on_use() pet_say('used') end
function on_wear() pet_say('worn on ' .. worn()) end
function on_unwear() pet_say('off ' .. tostring(worn())) end
function on_act(name) pet_say('act ' .. name) end
function on_stop() pet_say('stopped') end
", room);
            Assert.True(rs.Ok, rs.Error);
            Assert.Empty(room.said);                        // loading calls nothing
            Assert.True(rs.Wearable); Assert.Equal("head", rs.WearSlot);
            Assert.Single(rs.Offers); Assert.Equal("hop", rs.Offers[0].name); Assert.Equal("Hop!", rs.Offers[0].label);
            foreach (var h in new[] { "on_make", "on_use", "on_wear", "on_unwear", "on_act", "on_stop" }) Assert.True(rs.Has(h), h);
            Assert.False(rs.Has("update"));

            rs.Make(); Assert.Equal(new[] { "made" }, room.said);
            Assert.True(rs.Use()); Assert.Equal("used", room.said[^1]);
            Assert.True(rs.Wear()); Assert.Equal("worn on head", room.said[^1]); Assert.Equal("head", rs.WornOn); Assert.Equal("head", room.worn);
            Assert.True(rs.Act("hop")); Assert.Equal("act hop", room.said[^1]);
            Assert.False(rs.Act("fly"));                    // not offered
            Assert.Equal("act hop", room.said[^1]);
            Assert.True(rs.Unwear()); Assert.Equal("off nil", room.said[^1]); Assert.Equal("", rs.WornOn); Assert.Equal("", room.worn);
            Assert.False(rs.Unwear());                      // it is not worn
            rs.Stop(); Assert.Equal("stopped", room.said[^1]);
            Assert.Equal(6, room.said.Count);
            Assert.True(rs.Ok);
        }

        [Fact]
        public void AThingThatIsNotWearableCannotBeWorn_AndAMissingHookIsNoError()
        {
            var room = new FakeRoom();
            var rs = New("function on_use() sound('tap') end", room);
            Assert.False(rs.Wear());
            Assert.Equal("", rs.WornOn);
            rs.Make();                                      // no on_make
            Assert.False(rs.Act("anything"));
            Assert.True(rs.Use());
            Assert.True(rs.Ok);
            Assert.Equal(new[] { "tap" }, room.sounds);
        }

        [Fact]
        public void OfferMayCarryItsOwnFunction()
        {
            var room = new FakeRoom();
            var rs = New("offer('ring', 'Ring', function() sound('sparkle') end)", room);
            Assert.True(rs.Act("ring"));
            Assert.Equal(new[] { "sparkle" }, room.sounds);
        }

        [Fact]
        public void UpdateRunsFromAHookUntilDone()
        {
            var room = new FakeRoom();
            var rs = New(@"
n = 0
function on_use() n = 0 end
function update(dt) n = n + 1; if n == 30 then done() end; label(tostring(n), 0, 0.1, 0) end
", room);
            Run(rs, room, 1f);                              // not started: update is not called
            Assert.False(rs.Active);
            Assert.False(rs.Labels[0].on);
            rs.Use();
            Assert.True(rs.Active);
            Run(rs, room, 2f);
            Assert.False(rs.Active);                        // done() ended it
            Assert.Equal("30", LastLabel(rs));
            rs.Use();                                       // and a hook starts it again
            Run(rs, room, 0.1f);
            Assert.True(rs.Active);
        }

        static string LastLabel(RoomScript rs) { string s = ""; float at = -1; foreach (var l in rs.Labels) if (l.since > at) { at = l.since; s = l.text; } return s; }

        [Fact]
        public void AnUpdateThatOnlyWaitsIsLetGoAfterTenSeconds()
        {
            var room = new FakeRoom();
            var rs = New(@"
calls = 0
function on_use() pet_pose('think', 1) end
function update(dt) calls = calls + 1; local h = hands() end
", room);
            rs.Use();
            Assert.True(rs.Pet.active);
            float t = Run(rs, room, 1f + RoomScript.PetGrace - 0.2f);
            Assert.True(rs.Pet.active);                     // the pose is over, but it stays put for a moment
            t += Run(rs, room, 0.5f);
            Assert.True(rs.Active); Assert.False(rs.Pet.active);      // the script still waits; the partner is not kept waiting with it
            t += Run(rs, room, 30f, untilIdle: true);
            Assert.InRange(t, 1f + RoomScript.IdleSeconds - 0.2f, 1f + RoomScript.IdleSeconds + 0.3f);     // the pose, then ten idle seconds
            Assert.True(rs.IdledOut);
            Assert.False(rs.Pet.active);                    // the partner is its own again
            Assert.True(rs.Ok);
        }

        [Fact]
        public void WithoutUpdateAThingIsFinishedWhenWhatItStartedIsOver()
        {
            var room = new FakeRoom();
            var rs = New("function on_use() pet_pose('cheer', 1.5); sound('pop') end", room);
            rs.Use();
            Assert.True(rs.Active); Assert.Equal("cheer", rs.Pet.pose);
            float t = Run(rs, room, 10f, untilIdle: true);
            Assert.InRange(t, 1.45f, 1.6f);
            Assert.False(rs.Pet.active); Assert.Equal("", rs.Pet.pose);
        }

        [Fact]
        public void StopCallsOnStopEndsTimersAndFreesThePartner()
        {
            var room = new FakeRoom();
            var rs = New(@"
function on_use() pet_pose('dance', 600); every(0.5, function() sound('tick') end) end
function on_stop() music_stop() end
", room);
            rs.Use();
            Run(rs, room, 2.2f);
            Assert.True(rs.Active); Assert.True(rs.TimersRepeat);
            Assert.Equal(4, room.sounds.Count);
            rs.Stop();
            Assert.False(rs.Active); Assert.False(rs.Pet.active); Assert.False(rs.TimersRepeat);
            Assert.Contains("music ", room.asked);
            Run(rs, room, 2f);
            Assert.Equal(4, room.sounds.Count);             // the timer is gone
        }

        // ------------------------------------------------------------------ timers
        [Fact]
        public void AfterFiresOnceEveryRepeatsCancelStops()
        {
            var room = new FakeRoom();
            var rs = New(@"
function on_use()
  after(0.5, function() sound('pop') end)
  local n = 0
  local t
  t = every(0.25, function() n = n + 1; sound('tick'); if n == 4 then cancel(t) end end)
end
", room);
            rs.Use();
            Run(rs, room, 0.45f);
            Assert.Equal(new[] { "tick" }, room.sounds);
            Run(rs, room, 0.1f);
            room.sounds.Sort(string.CompareOrdinal);
            Assert.Equal(new[] { "pop", "tick", "tick" }, room.sounds);
            float t = Run(rs, room, 5f, untilIdle: true);
            Assert.Equal(5, room.sounds.Count);             // four ticks and the pop
            Assert.False(rs.Active);                        // nothing is left: the script is idle again
            Assert.InRange(0.55f + t, 0.95f, 1.1f);
        }

        [Fact]
        public void ANinthTimerIsRefused_AndDoneEndsTheRepeatingOnes()
        {
            var room = new FakeRoom();
            var rs = New(@"
ids = {}
function on_use()
  for i = 1, 9 do ids[i] = every(1, function() end) end
  pet_say(tostring(ids[8]) .. ' ' .. tostring(ids[9]))
end
function on_act() done() end
", room);
            rs.Use();
            Assert.Equal("8 nil", room.said[0]);
            Assert.Contains(rs.Notes, n => n.Contains("timers at most"));
            Assert.True(rs.TimersRepeat);
            rs.Act("x");
            Assert.False(rs.TimersRepeat);
            Run(rs, room, 0.1f);
            Assert.False(rs.Active);
        }

        // ------------------------------------------------------------------ the little objects
        [Fact]
        public void AThrownBallLandsBouncesLowerEachTimeAndStops()
        {
            var room = new FakeRoom { reach = false };
            var rs = New(@"
function on_use() b = make('ball', 0.02, 'red'); put(b, 0, 0.20, 0); throw(b, 0.15, 0, 0) end
", room);
            rs.Use();
            var o = rs.Objects[0];
            Assert.True(o.on); Assert.Equal("ball", o.shape); Assert.Equal("#E2574C", o.color); Assert.Equal(0.02f, o.size, 4);
            var tops = new List<float>(); float last = o.y, lastV = 0f; bool rising = false;
            float t = 0f;
            while (rs.Active && t < 30f)
            {
                room.Step(Dt, rs); rs.Tick(Dt); t += Dt;
                Assert.True(o.y >= 0.01f - 1e-4f, "it never goes through the desk");
                if (o.y < last && rising && last > 0.015f) tops.Add(last);
                rising = o.y > last; last = o.y; lastV = o.vy;
            }
            Assert.False(rs.Active);                        // it came to rest, and with that the script is idle
            Assert.False(o.flying);
            Assert.Equal(0.01f, o.y, 3);                    // lying on the desk (half its size up)
            Assert.True(tops.Count >= 2, "bounces: " + tops.Count);
            for (int i = 1; i < tops.Count; i++) Assert.True(tops[i] < tops[i - 1] * 0.75f, "each bounce is lower");
            Assert.True(tops[0] < 0.20f * 0.5f);
            Assert.True(o.x > 0.1f && o.x < 1.0f, "it went along x and rolled out: " + o.x);
            Assert.True(o.bounces >= 2);
            Assert.Contains("bounce", room.sounds);         // the desk is heard
            Assert.True(t < 12f, "it stops in good time: " + t);
        }

        [Fact]
        public void AnObjectThatWasOnlyPutStaysThereAlsoInTheAir()
        {
            var room = new FakeRoom();
            var rs = New("function on_use() s = make('star', 0.03, 'yellow'); put(s, 0.1, 0.25, -0.1) end", room);
            rs.Use();
            Run(rs, room, 3f);
            var o = rs.Objects[0];
            Assert.True(o.on); Assert.False(o.flying);
            Assert.Equal(0.25f, o.y, 4); Assert.Equal(0.1f, o.x, 4); Assert.Equal(-0.1f, o.z, 4);
            Assert.False(rs.Active);
        }

        [Fact]
        public void ThrowToArrivesAtThePlaceAtTheTime()
        {
            var room = new FakeRoom { reach = false };
            var rs = New(@"
function on_use() b = make('ball', 0.02, 'white'); put(b, 0.3, 0.07, -0.45); throw_to(b, 0.02, 0.05, -0.05, 0.9) end
", room);
            rs.Use();
            Run(rs, room, 0.9f - Dt * 0.5f);
            var o = rs.Objects[0];
            Assert.InRange(o.x, 0.012f, 0.028f); Assert.InRange(o.y, 0.040f, 0.060f); Assert.InRange(o.z, -0.06f, -0.04f);
        }

        [Fact]
        public void RollToStopsWhereItWasSent()
        {
            var room = new FakeRoom { reach = false };
            var rs = New(@"
function on_use() b = make('ball', 0.03, 'white'); put(b, -0.2, 0, 0.1); roll_to(b, 0.15, -0.2); roll_to(0, -0.3, 0.2) end
", room, Thing(x: 0.1f, z: 0.0f));
            rs.Use();
            Run(rs, room, 20f, untilIdle: true);
            var o = rs.Objects[0];
            Assert.False(o.flying);
            Assert.Equal(0.15f, o.x, 2); Assert.Equal(-0.2f, o.z, 2); Assert.Equal(0.015f, o.y, 3);
            Assert.True(o.roll > 0.3f);
            // the thing itself (id 0) rolls as well
            Assert.Equal(-0.3f, rs.Me.body.x, 2); Assert.Equal(0.2f, rs.Me.body.z, 2); Assert.Equal(0f, rs.Me.body.y, 3);
            Assert.True(rs.Me.moved);
        }

        [Fact]
        public void HitSeesAnObjectThatPassesThePointBetweenTwoFrames()
        {
            var room = new FakeRoom { reach = false };
            var rs = New(@"
hits = 0
function on_use() b = make('ball', 0.01, 'white'); put(b, -0.5, 0.1, 0); throw(b, 2.5, 0.3, 0) end
function update(dt) if b and hit(b, 0, 0.13, 0, 0.03) then hits = hits + 1; remove(b); b = nil; pet_say('hit'); done() end end
", room);
            rs.Use();
            Run(rs, room, 2f, dt: 1f / 20f);                // 12 cm a frame: the ball is never within 3 cm of the point AT a frame
            Assert.Equal(new[] { "hit" }, room.said);
            Assert.False(rs.Objects[0].on);
        }

        [Fact]
        public void WhereAndHitOfAnObjectThatIsGoneAreNilAndFalse_AndNilIsNotTheThingItself()
        {
            var room = new FakeRoom();
            var rs = New(@"
function on_use()
  local b = make('box', 0.02, 'blue'); remove(b)
  local x = where(b)
  pet_say(tostring(x) .. ' ' .. tostring(hit(b, 0, 0, 0, 9)) .. ' ' .. tostring(hit(nil, 0.12, 0, -0.06, 0.01)) .. ' ' .. tostring(hit(0, 0.12, 0, -0.06, 0.01)))
  put(nil, 1, 1, 1); throw(nil, 1, 1, 1); remove(nil)
end
", room);
            rs.Use();
            Assert.True(rs.Ok, rs.Error);
            Assert.Equal("nil false false true", room.said[0]);
            Assert.False(rs.Me.moved);                      // put(nil ...) did not move the thing
        }

        // ------------------------------------------------------------------ limits
        [Fact]
        public void TheTwentyFifthObjectIsRefused()
        {
            var room = new FakeRoom();
            var rs = New(@"
function on_use()
  local last
  for i = 1, 25 do last = make('ball', 0.01, 'white') end
  pet_say(tostring(last))
  remove(3)
  pet_say(tostring(make('box', 0.01, 'white')))
end
", room);
            rs.Use();
            Assert.Equal(new[] { "nil", "3" }, room.said);  // refused; and a freed place is used again
            int on = 0; foreach (var o in rs.Objects) if (o.on) on++;
            Assert.Equal(RoomScript.MaxObjects, on);
            Assert.Contains(rs.Notes, n => n.Contains("24 little objects"));
        }

        [Fact]
        public void FarPlacesAreCutDownToTheBox_AndThePartnerStaysOnItsDesk()
        {
            var room = new FakeRoom { reach = false };
            var rs = New(@"
function on_use()
  b = make('ball', 0.02, 'white'); put(b, 50, 9, -50)
  me_move(-3, 7)
  pet_goto(3, -2)
  label('far', 99, 99, 99)
  c = make('ball', 5, 'white')
  me_scale(10)
  throw(c, 100, 0, 0)
end
", room);
            rs.Use();
            var o = rs.Objects[0];
            Assert.Equal(RoomScript.Box, o.x, 4); Assert.Equal(RoomScript.Box, o.y, 4); Assert.Equal(-RoomScript.Box, o.z, 4);
            Assert.Equal(-RoomScript.Box, rs.Me.body.x, 4); Assert.Equal(RoomScript.Box, rs.Me.body.z, 4);
            Assert.Equal(RoomScript.PetMaxX, rs.Pet.gx, 4); Assert.Equal(RoomScript.PetMinZ, rs.Pet.gz, 4);
            Assert.Equal(RoomScript.Box, rs.Labels[0].x, 4);
            Assert.Equal(0.20f, rs.Objects[1].size, 4);
            Assert.Equal(2f, rs.Me.scale, 4);
            Assert.True(rs.Objects[1].vx <= RoomScript.MaxSpeed + 1e-4f);
            Assert.Contains(rs.Notes, n => n.StartsWith("pet_goto") && n.Contains("off its desk area"));
            Assert.Contains(rs.Notes, n => n.StartsWith("put") && n.Contains("further than"));
            Run(rs, room, 20f, untilIdle: true);
            foreach (var q in rs.Objects) if (q.on) { Assert.InRange(q.x, -RoomScript.Box, RoomScript.Box); Assert.InRange(q.z, -RoomScript.Box, RoomScript.Box); Assert.InRange(q.y, 0f, RoomScript.Box); }
            // the partner walked to the edge of its area, not off it
            Run(rs, room, 1f);
            Assert.Equal(RoomScript.PetMaxX, room.petX, 2); Assert.Equal(RoomScript.PetMinZ, room.petZ, 2);
        }

        [Fact]
        public void AFifthLabelTakesThePlaceOfTheOldest_AndLabelsGoAwayByThemselves()
        {
            var room = new FakeRoom();
            var rs = New(@"
function on_use() for i = 1, 5 do label('L' .. i, 0, 0.1 * i, 0, i) end end
", room);
            rs.Use();
            var texts = new List<string>(); foreach (var l in rs.Labels) if (l.on) texts.Add(l.text);
            texts.Sort();
            Assert.Equal(new[] { "L2", "L3", "L4", "L5" }, texts);
            Run(rs, room, 3.5f);
            texts.Clear(); foreach (var l in rs.Labels) if (l.on) texts.Add(l.text);
            texts.Sort();
            Assert.Equal(new[] { "L4", "L5" }, texts);
            Run(rs, room, 2f);
            foreach (var l in rs.Labels) Assert.False(l.on);
        }

        [Fact]
        public void WrongNamesAreForgivenAndNoted()
        {
            var room = new FakeRoom();
            var rs = New(@"
wearable('hat')
function on_use()
  sound('kaboom'); sound('tada')
  pet_pose('levitate'); pet_play('nothing')
  make('pyramid', 0.02, 'chartreuse')
  me_glow('gold', 1)
end
", room);
            Assert.Equal("head", rs.WearSlot);
            rs.Use();
            Assert.True(rs.Ok, rs.Error);
            Assert.Equal(new[] { "ta-da" }, room.sounds);
            Assert.Equal("idle", rs.Pet.pose);
            Assert.True(rs.Objects[0].on); Assert.Equal("ball", rs.Objects[0].shape); Assert.Equal("#FFFFFF", rs.Objects[0].color);
            Assert.Equal("#F2B84B", rs.Me.glow);
            foreach (var word in new[] { "kaboom", "levitate", "nothing", "pyramid", "chartreuse" })
                Assert.Contains(rs.Notes, n => n.Contains(word));
            Run(rs, room, 1.2f);
            Assert.Equal("", rs.Me.glow);                   // a glow with seconds goes out by itself
        }

        [Fact]
        public void SoundsAndSparksOfOneFrameAreCapped()
        {
            var room = new FakeRoom();
            var rs = New("function on_use() for i = 1, 50 do sound('tap'); sparks(0, 0.1, 0, 500) end end", room);
            rs.Use();
            Assert.True(room.sounds.Count <= RoomScript.MaxSoundsPerStep);
            Assert.True(room.sparkBursts <= RoomScript.MaxSparksPerStep);
            Assert.True(room.sparks <= RoomScript.MaxSparksPerStep * 24);
        }

        // ------------------------------------------------------------------ errors never get out
        [Theory]
        [InlineData("function on_use( pet_say('x') end", "load")]
        [InlineData("local t = nil; t.x = 1", "load")]
        [InlineData("function on_make() local t = nil; t.x = 1 end", "make")]
        [InlineData("function on_use() nothing_like_this() end", "use")]
        [InlineData("wearable('handR') function on_wear() error('no') end", "wear")]
        [InlineData("wearable('handR') function on_unwear() local a = 1 + {} end", "unwear")]
        [InlineData("offer('a', 'A') function on_act(n) return n.x.y end", "act")]
        [InlineData("function on_use() end function update(dt) local s = 'a' .. nil end", "update")]
        [InlineData("function on_use() after(0.1, function() boom() end) end", "timer")]
        [InlineData("function on_use() pet_goto(0.1, 0.1, function() boom() end) end", "arrive")]
        [InlineData("function on_use() while true do end end", "use")]
        [InlineData("function on_use() pet_pose('sit', 600) end function on_stop() boom() end", "stop")]
        [InlineData("motion('m', 1, function(u, p) p.armR = nil + 1 end) function on_use() pet_play('m') end", "motion")]
        public void AnErrorAnywhereSwitchesTheScriptOffWithoutThrowing(string lua, string where)
        {
            var room = new FakeRoom();
            var rs = New(lua, room);
            var pose = new Pose();
            if (where != "load")
            {
                Assert.True(rs.Ok, rs.Error);
                rs.Make(); rs.Use(); rs.Wear(); rs.Act("a"); rs.Unwear();
                for (int i = 0; i < 120; i++) { room.Step(Dt, rs); rs.Tick(Dt); rs.MotionPose(pose); }
                rs.Stop();
            }
            Assert.False(rs.Ok, "it must have failed in " + where);
            Assert.True(rs.Error.Length > 0);
            if (lua.Contains("while true")) Assert.Contains("too long", rs.Error);
            // an ordinary thing from here on: nothing runs, nothing is asked of the partner, nothing is left in the room
            Assert.False(rs.Active); Assert.False(rs.Pet.active);
            foreach (var o in rs.Objects) Assert.False(o.on);
            int said = room.said.Count;
            rs.Make(); rs.Use(); rs.Wear(); rs.Act("a"); rs.Unwear(); rs.Stop(); rs.Tick(Dt); rs.MotionPose(pose);
            Assert.False(rs.Use());
            Assert.Equal(said, room.said.Count);
        }

        [Fact]
        public void TheErrorSaysTheLine()
        {
            var rs = New("local a = 1\nfunction on_use()\n  local b = 2\n  broken_call(a, b)\nend\n", new FakeRoom());
            rs.Use();
            Assert.False(rs.Ok);
            Assert.Equal(4, rs.ErrorLine);
            Assert.Contains("nil", rs.Error);
        }

        [Fact]
        public void NothingThrowsForNullsAndNonsense()
        {
            var rs = new RoomScript(null, null, null);
            Assert.False(rs.Ok);
            rs.Tick(1f); rs.Use(); rs.Stop(); rs.Dispose();
            var room = new FakeRoom();
            rs = New(@"
function on_use()
  pet_goto(0/0, 1/0); pet_face(nil, {}); pet_pose(nil); pet_pose(5); pet_play(); pet_say(); pet_say({}); pet_hold(7); pet_drop()
  me_move('a', 'b'); me_turn(1/0); me_spin(-1/0); me_glow({}); me_glow(); me_scale(0/0)
  put(0/0, 1, 2, 3); throw(1/0, 0, 0, 0); throw_to({}, 1, 1, 1, -5); where('x'); hit(); remove(-3); roll_to(99, 1, 1)
  local m = make(); put(m, 0/0, 0/0, 0/0); throw(m, 0/0, 1/0, -1/0)
  sparks(); sparks(0/0, 0/0, 0/0, -5, 5); sound(); sound(3); label(); label(5, 'a', 'b', 'c', 1/0)
  after(); after(1); after('x', 2); every(0, function() end); cancel(); cancel('q'); time(1, 2, 3)
  near(); near(5); near({}); things(1); hands(1); user(2); music(); ambience(); offer(); wearable(); worn(1)
  pet_sit(); pet_lie(); pet_sit(5, 'x'); pet_free(1)
end
", room);
            rs.Use();
            Assert.True(rs.Ok, rs.Error);
            for (int i = 0; i < 200; i++) { room.Step(Dt, rs); rs.Tick(i % 7 == 0 ? float.NaN : i % 5 == 0 ? -1f : Dt); }
            Assert.True(rs.Ok, rs.Error);
            foreach (var o in rs.Objects) if (o.on) { Assert.False(float.IsNaN(o.x + o.y + o.z)); Assert.False(float.IsInfinity(o.x + o.y + o.z)); }
            Assert.False(float.IsNaN(rs.Me.body.x + rs.Me.body.y + rs.Me.body.z + rs.Me.yaw + rs.Me.scale + rs.Me.spin));
            Assert.False(float.IsNaN(rs.Pet.gx + rs.Pet.gz + rs.Pet.fx + rs.Pet.fz));
        }

        // ------------------------------------------------------------------ the partner
        [Fact]
        public void ThePartnerWalksWhereItIsSentAndTheCallbackComesWhenItIsThere()
        {
            var room = new FakeRoom();
            var rs = New(@"
function on_use() pet_goto(0.12, 0, function() pet_say('here'); pet_pose('wave', 1) end) end
", room);
            rs.Use();
            Assert.True(rs.Pet.active); Assert.True(rs.Pet.walk);
            float t = Run(rs, room, 0.7f);
            Assert.Empty(room.said);                        // 0.12 m at 0.12 m/s: not yet
            Run(rs, room, 0.4f);
            Assert.Equal(new[] { "here" }, room.said);
            Assert.False(rs.Pet.walk); Assert.Equal("wave", rs.Pet.pose);
            Assert.Equal(0.12f, room.petX, 2);
            Run(rs, room, 3f, untilIdle: true);
            Assert.False(rs.Pet.active);
        }

        [Fact]
        public void PlayingAMotionRunsItsTimeAndCallsBack_AndWhatWasAskedLastWins()
        {
            var room = new FakeRoom();
            var rs = New(@"
motion('hop', 0.5, function(u, p) p.lift = 0.3 * pulse(u) end)
motion('sway', 1.0, function(u, p) p.spine = {z = 10 * wave(u)} end, true)
function on_use() pet_pose('think', 5); pet_play('hop', function() pet_say('over'); pet_play('sway') end) end
function on_act(n) pet_pose('cheer', 1) end
", room);
            Assert.Equal(new[] { "hop", "sway" }, rs.Motions);
            Assert.Equal(0.5f, rs.MotionSeconds("hop"), 3);
            rs.Use();
            Assert.Equal("hop", rs.Pet.motion); Assert.Equal("", rs.Pet.pose);       // the motion took the pose's place
            var pose = new Pose();
            Run(rs, room, 0.25f);
            Assert.InRange(rs.Pet.MotionU, 0.45f, 0.56f);
            Assert.True(rs.MotionPose(pose));
            Assert.InRange(pose.HipsOffset.Y, 0.28f, 0.31f);
            Run(rs, room, 0.3f);
            Assert.Equal(new[] { "over" }, room.said);
            Assert.Equal("sway", rs.Pet.motion); Assert.True(rs.Pet.motionLoop);
            Run(rs, room, 5f);
            Assert.True(rs.Active); Assert.True(rs.MotionPose(pose));
            Run(rs, room, 6f);
            Assert.False(rs.Active);                        // a motion that goes round and round is let go after ten seconds
            Assert.False(rs.MotionPose(pose));
            // a pose asked after a motion ends the motion
            rs.Use(); rs.Act("x");
            Assert.Equal("", rs.Pet.motion); Assert.Equal("cheer", rs.Pet.pose);
        }

        [Fact]
        public void WearingKeepsTheThingAtThePartner_AndThrowingItTakesItOutOfTheHand()
        {
            var room = new FakeRoom { petX = -0.2f, petZ = 0.1f };
            var rs = New(@"
wearable('handR')
function on_act(n)
  local x, y, z = me()
  pet_say(string.format('%.2f %.2f %.2f %s', x, y, z, tostring(worn())))
  if n == 'toss' then throw(0, 0.3, 0.4, 0) end
end
", room);
            rs.Wear();
            Assert.Equal("handR", rs.WornOn);
            rs.Act("where");
            Assert.Equal("-0.20 0.04 0.10 handR", room.said[0]);
            rs.Act("toss");
            Assert.Equal("", rs.WornOn); Assert.Equal("", room.worn);      // out of the hand
            Assert.True(rs.Me.body.flying);
            Run(rs, room, 10f, untilIdle: true);
            Assert.False(rs.Me.body.flying);
            Assert.True(rs.Me.body.x > -0.1f);
            Assert.Equal(0f, rs.Me.body.y, 3);
            Assert.True(rs.Me.moved);
            // what the host says about where it is worn is taken over without a hook
            rs.SetWorn("back"); Assert.Equal("back", rs.WornOn);
            rs.SetWorn("nonsense"); Assert.Equal("", rs.WornOn);
        }

        [Fact]
        public void TheScriptSeesTheHandsTheUserAndTheOtherThings()
        {
            var room = new FakeRoom { reach = false };
            room.things.Add(new RoomThingInfo { id = "thing-1", name = "me", kind = "chair", uses = new[] { "sit" }, x = 0.12f, z = -0.06f });
            room.things.Add(new RoomThingInfo { id = "t", name = "Desk", kind = "desk", uses = new[] { "table" }, x = 0.12f, z = 0.06f, size = 0.11f, top = 0.07f });
            room.things.Add(new RoomThingInfo { id = "b", name = "Bed", kind = "bed", uses = new[] { "sleep", "sit" }, x = -0.3f, z = 0.1f, size = 0.17f, seat = 0.03f });
            var rs = New(@"
function on_use()
  local h = hands()
  local ux, uy, uz = user()
  pet_say(#h .. ' ' .. string.format('%.2f %.2f %.2f %s', h[1].x, h[1].y, h[1].z, tostring(h[1].pinch)) .. string.format(' %.2f %.2f %.2f', ux, uy, uz))
  local all = things()
  local d = near('table', 0.2)
  local far = near('sleep', 0.2)
  local any = near('sleep', 1)
  pet_say(#all .. ' ' .. d.name .. ' ' .. d.uses[1] .. ' ' .. string.format('%.2f', d.top) .. ' ' .. tostring(far) .. ' ' .. any.id)
  pet_sit('study', 30, d)
end
", room);
            rs.Use();
            Assert.True(rs.Ok, rs.Error);
            Assert.Equal("1 0.12 0.02 -0.30 false 0.00 0.35 -0.50", room.said[0]);
            Assert.Equal("2 Desk table 0.07 nil b", room.said[1]);      // the thing itself is not among the others
            Assert.Equal("sit", rs.Pet.on); Assert.Equal("t", rs.Pet.onId); Assert.Equal("study", rs.Pet.pose);
            room.handSeen = false;
            var rs2 = New("function on_use() pet_say(tostring(#hands())) end", room);
            rs2.Use();
            Assert.Equal("0", room.said[^1]);
        }

        [Fact]
        public void MusicAndSurroundingsAreAskedOfTheRoomByTheirIds()
        {
            var room = new FakeRoom();
            var rs = New(@"
function on_use() ambience('club'); music('lofi'); music('song-club'); ambience('My Place') end
function on_stop() ambience_clear(); music_stop() end
", room);
            rs.Use(); rs.Stop();
            Assert.Equal(new[] { "ambience amb-club", "music song-lofi", "music song-club", "ambience My Place", "ambience ", "music " }, room.asked);
        }

        [Fact]
        public void TheSameSeedGivesTheSameRun_AndAnotherSeedAnother()
        {
            string lua = @"
function on_use()
  for i = 1, 6 do local b = make('ball', 0.02, 'white'); put(b, 0, 0.1, 0); throw(b, rnd(-1, 1), rnd(0, 1), rnd(-1, 1)) end
  every(0.3, function() sparks(rnd(-0.2, 0.2), 0.1, rnd(-0.2, 0.2), rndi(3, 9)) end)
  after(4, done)
end";
            string Go(int seed)
            {
                var room = new FakeRoom(seed);
                var rs = New(lua, room, null, seed);
                var log = Traced(rs);
                rs.Use();
                Run(rs, room, 6f);
                var sb = new System.Text.StringBuilder();
                foreach (var l in log) sb.Append(l).Append('\n');
                foreach (var o in rs.Objects) if (o.on) sb.Append(o.x.ToString("R")).Append(' ').Append(o.y.ToString("R")).Append(' ').Append(o.z.ToString("R")).Append('\n');
                return sb.ToString();
            }
            string a = Go(7), b = Go(7), c = Go(8);
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
            Assert.Contains("throw #6", a);
        }

        [Fact]
        public void TheSeedComesFromTheThingsIdWhenNoneIsGiven()
        {
            string lua = "function on_use() pet_say(tostring(rnd())) end";
            var r1 = new FakeRoom(); var r2 = new FakeRoom(); var r3 = new FakeRoom();
            new RoomScript(lua, Thing("item-a"), r1).Use(); new RoomScript(lua, Thing("item-a"), r2).Use(); new RoomScript(lua, Thing("item-b"), r3).Use();
            Assert.Equal(r1.said[0], r2.said[0]);
            Assert.NotEqual(r1.said[0], r3.said[0]);
        }

        [Fact]
        public void TheThingTurnsGlowsAndHops()
        {
            var room = new FakeRoom();
            var rs = New("function on_use() me_spin(90); me_bounce(); me_glow('sky'); me_scale(1.5); me_turn(10) end function on_act() me_spin(0); me_glow('off') end", room);
            rs.Use();
            Assert.Equal("#9CCBEB", rs.Me.glow); Assert.Equal(1.5f, rs.Me.scale, 3);
            Run(rs, room, 2f);
            Assert.InRange(rs.Me.yaw, 10f + 180f - 3f, 10f + 180f + 3f);         // it keeps turning also when the script is idle
            Assert.False(rs.Active);
            Assert.InRange(rs.Time - rs.Me.bounceAt, 1.9f, 2.1f);
            Assert.Equal("#9CCBEB", rs.Me.glow);            // a glow without seconds stays
            rs.Act("x");
            Assert.Equal("", rs.Me.glow); Assert.Equal(0f, rs.Me.spin);
        }

        [Fact]
        public void WhereTheHostHasTheThingIsTakenOverUnlessTheScriptMovedIt()
        {
            var room = new FakeRoom();
            var rs = New("function on_use() me_move(0.3, 0.1) end", room);
            rs.SetPlace(-0.1f, 0f, 0.05f, 30f);
            Assert.Equal(-0.1f, rs.Me.body.x, 4); Assert.Equal(30f, rs.Me.yaw, 3);
            rs.Use();
            rs.SetPlace(-0.1f, 0f, 0.05f, 30f);            // the host does not know yet
            Assert.Equal(0.3f, rs.Me.body.x, 4);
            rs.PlaceTaken();                                // now it does
            rs.SetPlace(0.2f, 0f, 0.0f, 0f);
            Assert.Equal(0.2f, rs.Me.body.x, 4);
        }

        [Fact]
        public void BuiltInPosesAllGiveAPose()
        {
            var p = new Pose(); var idle = new Pose();
            foreach (var name in RoomScriptApi.Poses)
            {
                Assert.True(RoomScriptApi.IsPose(name));
                for (float since = 0f; since < 2f; since += 0.31f)
                {
                    RoomScriptApi.Pose(name, p, 3.3f + since, since);
                    foreach (var e in p.Euler) Assert.False(float.IsNaN(e.X + e.Y + e.Z), name);
                }
                PoseLib.Idle(idle, 3.3f + 0.31f); RoomScriptApi.Pose(name, p, 3.3f + 0.31f, 0.31f);
                bool differs = false;
                for (int i = 0; i < Skeleton.N; i++) if ((p.Euler[i] - idle.Euler[i]).Length() > 1f) differs = true;
                if (name != "idle") Assert.True(differs || Math.Abs(p.HipsOffset.Y - idle.HipsOffset.Y) > 0.01f, name + " is not just standing");
            }
            Assert.True(RoomScriptApi.IsPose("happy")); Assert.True(RoomScriptApi.IsPose("Cheer")); Assert.False(RoomScriptApi.IsPose("levitate"));
        }
    }
}
