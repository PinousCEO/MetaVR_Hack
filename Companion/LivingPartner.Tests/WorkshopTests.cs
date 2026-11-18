using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// THE WORKSHOP (Workshop.cs, WorkshopPlay.cs, WorkshopLook.cs): the pure parts. The simulated players with the REAL
/// interpreter on the shipped examples (a player who goes for it scores; nobody at the glass is treated fairly), the
/// rasteriser (a PNG of every kind of sprite, and it never throws), the harness's acceptance rules (what stands in the
/// way of a flat "shooter", a game that is over at once, words nobody can read, a frozen motion), and the agent loop
/// itself with a model that answers from a list: the repair after the harness objects, the critic's objection going
/// back in, the caps, the honest failure, cancelling.
/// </summary>
public partial class WorkshopTests
{
    [Theory]
    [InlineData("thing", "mr", "working", true, false)]
    [InlineData("model", "mr", "working", true, false)]
    [InlineData("item", "mr", "queued", false, false)]
    [InlineData("game", "pc", "working", false, true)]
    [InlineData("game", "pc", "queued", false, false)]
    [InlineData("designs", "pc", "working", false, false)]
    [InlineData("report", "mr", "working", false, false)]
    [InlineData("skill", "mr", "working", false, false)]
    [InlineData("tool", "pc", "working", false, false)]
    [InlineData("music", "pc", "working", false, false)]
    public void Only_work_on_a_3D_object_or_game_moves_the_partner(string kind, string where, string status, bool bench, bool pc)
    {
        var job = new WorkJob { kind = kind, where = where, status = status };
        Assert.Equal(bench, WorkJob.AtBench(job));
        Assert.Equal(pc, WorkJob.InsidePc(job));
    }

    const string ShooterWords = "슈팅 게임 만들어 줘. 네가 PC 안에서 나한테 총을 쏘고 나는 왼손으로 막으면서 네 작은 분신들을 쏴서 맞추는 게임";

    // a small game of its own (no example is handed back): dots come, a hand pops them
    const string Dots = @"local dots = {}
local t = 0
function start() pet_say('Touch the dots!', 2) end
function update(dt)
  t = t + dt
  if #dots < 3 and t > 1 then dots[#dots + 1] = {x = rnd(0.2, W - 0.2), y = rnd(0.3, 0.8)}  t = 0 end
  for _, h in ipairs(hands()) do
    for i = #dots, 1, -1 do
      if dist(h.x, h.y, dots[i].x, dots[i].y) < h.r + 0.06 then table.remove(dots, i)  add(1)  sound('pop') end
    end
  end
  for _, d in ipairs(dots) do disc(d.x, d.y, 0.06, 'pink') end
  text('Touch the dots', W / 2, 0.92, 0.06, 'ink')
end
";

    static PlayReport Play(string lua, string player, float seconds = 40f) => WorkshopPlay.Run(lua, null, player, seconds, 1, true, new[] { 3f, seconds * 0.5f });

    [Fact]
    public void A_player_who_goes_for_it_scores_in_the_shooter_and_nobody_is_treated_fairly()
    {
        var seeker = Play(GlassScriptExamples.Shooter, WorkshopPlay.Seeker, 60f);
        Assert.True(seeker.Ok, seeker.error);
        Assert.True(seeker.scoreChanges >= 5, seeker.Digest());          // it aims at the clones and hits them
        Assert.True(seeker.shots >= 20 && seeker.readShots && seeker.readAim);
        Assert.True(seeker.outsSent >= 3 && seeker.blocked >= 1 && seeker.usedOuts && seeker.readOutEvents, seeker.Digest());
        Assert.True(seeker.PetActed && seeker.petPlays >= 1 && seeker.petLines >= 1);
        Assert.Contains(seeker.timeline, e => e.what.StartsWith("OUT #1 leaves the field"));
        Assert.Contains(seeker.timeline, e => e.what.Contains("BLOCKED"));
        Assert.True(seeker.frames.Count >= 3 && seeker.frames[0].sprites.Count > 0);
        var nobody = Play(GlassScriptExamples.Shooter, WorkshopPlay.Nobody, 60f);
        Assert.True(nobody.Ok && nobody.scoreChanges == 0 && nobody.taps == 0 && nobody.shots == 0);
        Assert.True(nobody.hit >= 1 && nobody.blocked == 0);              // with nobody there everything reaches the player
        Assert.True(!nobody.finished || nobody.finishedAt >= ScriptRules.MinSecondsAlone, nobody.Digest());
        Assert.True(WorkshopRules.Reacts(seeker, nobody));
        // the same play twice is the same play
        Assert.Equal(seeker.Digest(), Play(GlassScriptExamples.Shooter, WorkshopPlay.Seeker, 60f).Digest());
        string text = seeker.Text();
        Assert.Contains("PLAYTEST", text); Assert.Contains("timeline", text); Assert.Contains("DRAWN at t=", text); Assert.Contains("Bambi", text);
    }

    [Fact]
    public void The_players_play_every_shipped_example_without_breaking_it()
    {
        foreach (var e in GlassScriptExamples.All)
            foreach (string player in WorkshopPlay.Players)
            {
                var r = WorkshopPlay.Run(e.lua, null, player, 25f, 1, true, new[] { 2f });
                Assert.True(r.Ok, $"{e.id} with {player}: {r.error}");
                Assert.True(r.drew, e.id);
                Assert.NotEmpty(r.frames);
            }
        // a tool: the seeker finds the keys (the calculator's display changes), a board: its taps place stones
        Assert.True(Play(GlassScriptExamples.Calculator, WorkshopPlay.Seeker).usedKit);
        Assert.True(WorkshopRules.Reacts(Play(GlassScriptExamples.Calculator, WorkshopPlay.Seeker), Play(GlassScriptExamples.Calculator, WorkshopPlay.Nobody)));
        Assert.True(WorkshopRules.Reacts(Play(GlassScriptExamples.Omok, WorkshopPlay.Seeker), Play(GlassScriptExamples.Omok, WorkshopPlay.Nobody)));
        // a script that breaks says where
        var broken = Play("function update(dt) nothing_here() end", WorkshopPlay.Seeker);
        Assert.False(broken.Ok); Assert.True(broken.errorLine >= 1); Assert.Contains("ERROR", broken.Digest());
    }

    [Fact]
    public void The_picture_of_a_frame_is_a_png_and_never_throws()
    {
        var play = Play(GlassScriptExamples.Shooter, WorkshopPlay.Seeker);
        foreach (var f in play.frames)
        {
            byte[] png = WorkshopLook.Png(f, "test");
            Assert.NotNull(png);
            Assert.True(png.Length > 2000 && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47);
        }
        // every kind of sprite, also the ones no script should send: nothing throws, a picture comes
        var all = new PlayFrame { t = 1, player = "test", banner = "A banner", petLine = "Hello there!", petMotion = "swing", hand = true, handX = 0.4f, handY = 0.5f, aim = true, aimX = 1f, aimY = 0.6f, lives = 3 };
        all.flying.Add((0.8f, 0.5f, "orb"));
        int id = GlassScript.FirstId;
        foreach (string art in GlassArt.All.Concat(new[] { GlassArt.Paper, GlassArt.PackPrefix + "clone", "no-such-art", "", null }))
            all.sprites.Add(new GlassSprite { id = id++, art = art, x = 0.1f + 0.05f * (id % 30), y = 0.2f + 0.02f * (id % 30), size = 0.12f, w = art == GlassArt.Rect ? 0.3f : 0f, alpha = 1f, color = id % 2 == 0 ? 0xF4A9A0 : -1, text = "Words 12", text2 = "more", fill = 0.3f, rot = art == GlassArt.Rect ? 30f : 0f });
        all.sprites.Add(new GlassSprite { id = id++, art = GlassArt.Label, x = float.NaN, y = 5f, size = 900f, alpha = 1f, text = new string('x', 300) });
        Assert.NotNull(WorkshopLook.Png(all));
        Assert.Null(WorkshopLook.Png(null));
        Assert.NotNull(WorkshopLook.Png(new PlayFrame { W = 0.01f }));
        Assert.Contains("thing(s)", WorkshopPlay.Dump(all)); Assert.Contains("in the ROOM", WorkshopPlay.Dump(all));
    }

    [Fact]
    public void The_harness_accepts_what_is_right_and_says_what_is_not()
    {
        static void None(List<string> objections) => Assert.True(objections.Count == 0, string.Join(" | ", objections));
        ScriptVerdict Check(string lua) => ScriptRules.CheckGame(lua, null, ScriptRules.MadeGameSeconds, 1);
        List<string> Accept(string words, string lua) => WorkshopRules.AcceptGame(words, lua, Check(lua), Play(lua, WorkshopPlay.Nobody, 60f), Play(lua, WorkshopPlay.Random), Play(lua, WorkshopPlay.Seeker, 60f));
        // the shipped shooter IS the user's shooter: nothing stands in its way (and the checker no longer calls shots() "hands never read")
        Assert.Empty(Check(GlassScriptExamples.Shooter).serious);
        None(Accept(ShooterWords, GlassScriptExamples.Shooter));
        None(Accept("축구 게임: 네가 화면 안에서 공을 차고 내가 손으로 막는 거", GlassScriptExamples.Keeper));
        None(Accept("계산기 만들어 줘", GlassScriptExamples.Calculator));
        None(Accept("오목 게임 만들어 줘", GlassScriptExamples.Omok));
        None(Accept("a game where I touch dots", Dots));
        // ... but handed back for the same request with the examples in view, it is a copy
        Assert.Contains(WorkshopRules.AcceptGame(ShooterWords, GlassScriptExamples.Shooter, Check(GlassScriptExamples.Shooter), Play(GlassScriptExamples.Shooter, WorkshopPlay.Nobody), null, Play(GlassScriptExamples.Shooter, WorkshopPlay.Seeker), ScriptWriter.GameExamples()), o => o.Contains("handed back"));
        Assert.Null(WorkshopRules.CopyOf(Dots, ScriptWriter.GameExamples()));
        // the user's "분신 사격": a flat picture of a shooting game - nothing leaves the field, nothing is shot, Bambi does nothing
        var flat = Accept(ShooterWords, Dots.Replace("pet_say('Touch the dots!', 2)", ""));
        Assert.Contains(flat, o => o.Contains("NOTHING left the field"));
        Assert.Contains(flat, o => o.Contains("shots() is never read"));
        Assert.Contains(flat, o => o.Contains("Bambi does NOTHING"));
        // a tool without the kit
        Assert.Contains(Accept("계산기 만들어 줘", Dots), o => o.Contains("UI kit"));
        // it does not run
        var dead = Accept("a game", "function update(dt) nothing_here() end");
        Assert.Single(dead); Assert.Contains("does not run", dead[0]);
        // lost at once with nobody playing; won at once by a player
        string unfair = "local t = 0\nfunction start() lives(1) end\nfunction update(dt)\n  t = t + dt\n  disc(W / 2, 0.5, 0.1 + 0.01 * math.sin(t * 5), 'pink')\n  for _, h in ipairs(hands()) do add(1) end\n  if t > 3 and lives() > 0 then lives(0)  finish('Lost') end\nend\n";
        Assert.Contains(Accept("a game", unfair), o => o.Contains("NOBODY playing"));
        string tooShort = Dots.Replace("add(1)  sound('pop')", "add(1)  sound('pop')  if score() >= 2 then finish('You win!') end");
        Assert.Contains(Accept("a game where I touch dots", tooShort), o => o.Contains("THROUGH THE WHOLE GAME"));
        // what the request needs, read from its words
        Assert.True(WorkshopRules.GivesPartnerAPart(ShooterWords)); Assert.False(WorkshopRules.GivesPartnerAPart("계산기 만들어 줘"));
        Assert.True(WorkshopRules.NamesMovement("야구 배트 만들어 줘. 그걸 들면 야구 스윙을 해")); Assert.True(WorkshopRules.NamesWearing("그걸 들면 야구 스윙을 해")); Assert.False(WorkshopRules.NamesMovement("작은 램프"));
    }

    [Fact]
    public void Words_nobody_can_read_are_found_in_the_frames()
    {
        static GlassSprite S(string art, float x, float y, float w, float h, string text = "") => new GlassSprite { id = GlassScript.FirstId, art = art, x = x, y = y, w = w, size = h, alpha = 1f, color = 0xFFFFFF, text = text };
        PlayFrame Frame(params GlassSprite[] sprites) { var f = new PlayFrame(); f.sprites.AddRange(sprites); return f; }
        // a title, and a display drawn over it afterwards
        var covered = Frame(S(GlassArt.Label, 0.9f, 0.9f, 0, 0.06f, "DESK CALCULATOR"), S(GlassArt.Paper, 0.9f, 0.88f, 1.0f, 0.12f));
        Assert.Contains(WorkshopRules.Unreadable(new[] { covered, covered }), o => o.Contains("DESK CALCULATOR") && o.Contains("UNDER"));
        // the box first and the words on it: that is a button
        var button = Frame(S(GlassArt.Paper, 0.9f, 0.88f, 0.3f, 0.12f), S(GlassArt.Label, 0.9f, 0.88f, 0, 0.06f, "7"));
        Assert.Empty(WorkshopRules.Unreadable(new[] { button, button }));
        // two lines on each other
        var twice = Frame(S(GlassArt.Label, 0.9f, 0.5f, 0, 0.06f, "Shoot the clones"), S(GlassArt.Label, 0.92f, 0.51f, 0, 0.06f, "Left hand shields"));
        Assert.Contains(WorkshopRules.Unreadable(new[] { twice, twice }), o => o.Contains("on top of each other"));
        // something that flies past a word in ONE frame is nothing
        Assert.Empty(WorkshopRules.Unreadable(new[] { covered, button, button }));
        Assert.Empty(WorkshopRules.Unreadable(null));
    }

    [Fact]
    public void A_things_script_is_used_in_a_room_and_its_motions_are_sampled()
    {
        var bat = new ItemDef { id = "item-bat", name = "bat", kind = "box", sizeM = 0.07f };
        string report = WorkshopPlay.Room(RoomScriptExamples.Bat, bat, out var motions);
        Assert.Contains("wearable", report); Assert.Contains("pet_play", report); Assert.Contains("motion '", report);
        Assert.NotEmpty(motions);
        Assert.All(motions, m => Assert.False(m.Frozen, m.text));
        Assert.All(motions, m => Assert.True(m.played, m.text));
        var verdict = ScriptRules.CheckRoom(RoomScriptExamples.Bat, bat);
        Assert.Empty(WorkshopRules.AcceptRoom(ScriptRequest.Thing, "야구 배트 만들어 줘. 그걸 들면 야구 스윙을 해", verdict, motions));
        // a "motion" that is one pose from beginning to end is none
        string frozen = "wearable('handR')\noffer('swing', 'Swing')\nmotion('swing', 1, function(u, p) p.armR = {x = -60} end)\nfunction on_use() pet_play('swing') sound('whoosh') end\nfunction on_act(name) pet_play('swing') sound('whoosh') end\nfunction on_wear() pet_say('Ready!') end\nfunction on_unwear() end\n";
        WorkshopPlay.Room(frozen, bat, out var still);
        Assert.Single(still); Assert.True(still[0].Frozen, still[0].text);
        Assert.Contains(WorkshopRules.AcceptRoom(ScriptRequest.Thing, "배트. 들면 스윙해", ScriptRules.CheckRoom(frozen, bat), still), o => o.Contains("FROZEN POSE"));
        // the words say "when it holds it": a thing that cannot be held is not it; a movement that is named needs a motion
        string plain = "function on_use() pet_say('A bat!') sound('pop') end\n";
        var objections = WorkshopRules.AcceptRoom(ScriptRequest.Thing, "야구 배트 만들어 줘. 그걸 들면 야구 스윙을 해", ScriptRules.CheckRoom(plain, bat), new List<MotionSample>());
        Assert.Contains(objections, o => o.Contains("wearable(slot)")); Assert.Contains(objections, o => o.Contains("motion(...)"));
        Assert.Empty(WorkshopRules.AcceptRoom(ScriptRequest.Thing, "작은 램프", ScriptRules.CheckRoom(plain, bat), new List<MotionSample>()));
    }

    // ------------------------------------------------------------------ the loop, with a model that answers from a list
    sealed class FakeModel : IWorkshopModel
    {
        readonly Queue<Func<JObject, JObject>> _turns = new();
        public readonly List<JObject> bodies = new();
        public int critics;
        public Func<JObject> critic = () => Review();
        public string Name => "fake";
        public FakeModel Then(params (string name, JObject args)[] calls) { _turns.Enqueue(_ => Reply(calls)); return this; }
        public Func<JObject, JObject> forever;
        public Task<JObject> RespondAsync(JObject body, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            bodies.Add((JObject)body.DeepClone());
            if (body["text"] != null) { critics++; return Task.FromResult(Message(critic().ToString())); }
            if (_turns.Count > 0) return Task.FromResult(_turns.Dequeue()(body));
            return Task.FromResult(forever != null ? forever(body) : Message("I am done."));
        }
        static int _id;
        public static JObject Reply(params (string name, JObject args)[] calls)
        {
            var output = new JArray { new JObject { ["type"] = "reasoning", ["id"] = "rs_" + ++_id, ["encrypted_content"] = "x" } };
            foreach (var (name, args) in calls) output.Add(new JObject { ["type"] = "function_call", ["call_id"] = "call_" + ++_id, ["name"] = name, ["arguments"] = args.ToString() });
            return new JObject { ["id"] = "resp_" + ++_id, ["status"] = "completed", ["output"] = output, ["usage"] = new JObject { ["input_tokens"] = 1000, ["output_tokens"] = 100 } };
        }
        public static JObject Message(string text) => new JObject
        {
            ["id"] = "resp_" + ++_id, ["status"] = "completed", ["usage"] = new JObject { ["input_tokens"] = 500, ["output_tokens"] = 50 },
            ["output"] = new JArray { new JObject { ["type"] = "message", ["content"] = new JArray { new JObject { ["type"] = "output_text", ["text"] = text } } } },
        };
        public static JObject Review(params string[] defects) => new JObject { ["clauses"] = new JArray(), ["pictures"] = new JArray(), ["defects"] = new JArray(defects), ["recognised"] = defects.Length == 0 };
    }

    static (string, JObject) Write(string script, string note = "a note") => ("write", new JObject { ["script"] = script, ["title"] = "Dots", ["how_to_play"] = "Touch the dots.", ["art"] = new JArray(), ["note"] = note });
    static (string, JObject) Done(string missing = "") => ("done", new JObject { ["summary"] = "Dots to touch.", ["phrases"] = new JArray(), ["missing_api"] = missing });
    static ScriptRequest Request(string words = "a game where I touch dots") => new ScriptRequest { kind = ScriptRequest.Game, words = words, id = "game-test" };
    static WorkshopOptions Options() => new WorkshopOptions { model = "fake", seconds = 20 };

    [Fact]
    public void The_loop_repairs_what_the_harness_objects_to_and_hands_over_what_passed()
    {
        var model = new FakeModel()
            .Then(Write("function update(dt) nothing_here() end", "first try"))
            .Then(("done", new JObject { ["summary"] = "x", ["phrases"] = new JArray(), ["missing_api"] = "" }))          // too early: the harness says no
            .Then(Write(Dots, "dots that can be touched"), ("look", new JObject { ["player"] = "seeker", ["t"] = 5 }))
            .Then(Done());
        var steps = new List<string>(); var log = new List<string>();
        var res = Workshop.MakeAsync(model, Request(), Options(), (text, p, round) => { if (steps.Count == 0 || steps[^1] != text) steps.Add(text); }, log.Add, CancellationToken.None).Result;
        Assert.True(res.ok, res.why + " | " + string.Join(" / ", log));
        Assert.Equal(2, res.rounds); Assert.Equal(4, res.turns); Assert.Equal(1, res.criticCalls); Assert.Equal(1, model.critics);
        Assert.Equal(Dots.Trim(), res.script.Trim()); Assert.Equal("Dots", res.title); Assert.Equal("Touch the dots.", res.howTo);
        Assert.False(res.versions[0].Accepted); Assert.True(res.versions[1].Accepted); Assert.True(res.versions[1].criticOk);
        Assert.NotEmpty(res.frames); Assert.Equal("done", res.endedBy);
        Assert.True(res.tokensIn >= 4000);
        // what the work queue was told, in plain English, in order
        Assert.Equal("Reading the game API", steps[0]); Assert.Equal("Writing the first version", steps[1]);
        Assert.Contains("Play-testing: nobody at the glass", steps); Assert.Contains("Play-testing: a player who goes for it", steps); Assert.Contains("Looking at the picture", steps);
        Assert.Contains(steps, s => s.StartsWith("Fixing: it does not run")); Assert.Contains("Critic: asking for a second opinion", steps);
        Assert.StartsWith("Done", steps[^1]);
        // the model was told why: the error with its line, and that done was refused
        string second = model.bodies[1].ToString(), third = model.bodies[2].ToString();
        Assert.Contains("THE HARNESS OBJECTS", second); Assert.Contains("nothing_here", second);
        Assert.Contains("NOT ACCEPTED", third);
        // the pictures were attached to the conversation after the version that ran (two of the harness's own, one asked for with look)
        var lastInput = (JArray)model.bodies[3]["input"];
        Assert.Equal(3, lastInput.SelectTokens("$..[?(@.type == 'input_image')]").Count());
        // nothing is kept at the service: the whole conversation travels, with what the model thought (encrypted)
        Assert.False((bool)model.bodies[0]["store"]); Assert.Contains("reasoning.encrypted_content", model.bodies[0]["include"].ToString());
        Assert.Contains(lastInput, i => (string)i["type"] == "reasoning"); Assert.Equal("required", (string)model.bodies[0]["tool_choice"]);
        Assert.Contains("BOUNDARY", (string)model.bodies[0]["instructions"]);
        // the critic was GIVEN pictures and what happened
        var critic = model.bodies.Single(b => b["text"] != null);
        Assert.True(critic.SelectTokens("$..[?(@.type == 'input_image')]").Count() >= 2); Assert.Contains("WHAT HAPPENED WHEN IT WAS PLAYED", critic.ToString());
    }

    [Fact]
    public void The_critics_objection_goes_back_into_the_loop()
    {
        int asked = 0;
        var model = new FakeModel { critic = () => ++asked == 1 ? FakeModel.Review("The dots are pink: the request says blue dots.") : FakeModel.Review() }
            .Then(Write(Dots)).Then(Done())
            .Then(("patch", new JObject { ["find"] = "'pink'", ["replace"] = "'sky'", ["note"] = "blue dots" })).Then(Done());
        var steps = new List<string>();
        var res = Workshop.MakeAsync(model, Request("a game where I touch blue dots"), Options(), (text, p, round) => steps.Add(text), null, CancellationToken.None).Result;
        Assert.True(res.ok, res.why);
        Assert.Equal(2, res.rounds); Assert.Equal(2, res.criticCalls); Assert.Contains("'sky'", res.script); Assert.DoesNotContain("'pink'", res.script);
        Assert.Contains(steps, s => s.StartsWith("Critic: The dots are pink")); Assert.Contains(steps, s => s.StartsWith("Fixing: The dots are pink"));
        Assert.Contains("REVIEWER", model.bodies.Where(b => b["text"] == null).ElementAt(2).ToString());
        Assert.True(res.versions[1].criticOk); Assert.Empty(res.defects);
        // a patch whose fragment is not there changes nothing and says so
        var miss = new FakeModel().Then(Write(Dots)).Then(("patch", new JObject { ["find"] = "no such text", ["replace"] = "x", ["note"] = "n" })).Then(Done());
        var res2 = Workshop.MakeAsync(miss, Request(), Options(), null, null, CancellationToken.None).Result;
        Assert.True(res2.ok); Assert.Equal(1, res2.rounds); Assert.Contains("NOT FOUND", miss.bodies[2].ToString());
    }

    [Fact]
    public void Nothing_that_did_not_pass_is_ever_handed_over()
    {
        // a model that only ever writes a program that breaks: the cap ends it, and the job FAILS with a plain reason
        var stubborn = new FakeModel { forever = _ => FakeModel.Reply(Write("function update(dt) nothing_here() end")) };
        var o = Options(); o.maxVersions = 3; o.maxTurns = 5;
        var res = Workshop.MakeAsync(stubborn, Request(), o, null, null, CancellationToken.None).Result;
        Assert.False(res.ok); Assert.Equal("", res.script); Assert.Equal(3, res.rounds);
        Assert.StartsWith("no version passed: it does not run", res.why); Assert.Contains("cap", res.endedBy);
        // the cap with a version that passed (the model never calls done): that version is taken
        var slow = new FakeModel { forever = _ => FakeModel.Reply(("check", new JObject())) }.Then(Write(Dots));
        var res2 = Workshop.MakeAsync(slow, Request(), o, null, null, CancellationToken.None).Result;
        Assert.True(res2.ok); Assert.Equal(1, res2.rounds); Assert.Contains("cap of 5 turns", res2.endedBy); Assert.Equal("Dots", res2.title);
        // a model that stops using its tools
        var silent = Workshop.MakeAsync(new FakeModel(), Request(), o, null, null, CancellationToken.None).Result;
        Assert.False(silent.ok); Assert.Contains("stopped using its tools", silent.why);
        // cancelled: the calls stop and nothing comes of it
        using var cts = new CancellationTokenSource();
        var cancelling = new FakeModel { forever = _ => { cts.Cancel(); return FakeModel.Reply(Write(Dots)); } };
        var res3 = Workshop.MakeAsync(cancelling, Request(), o, null, null, cts.Token).Result;
        Assert.False(res3.ok); Assert.True(res3.cancelled); Assert.Equal("cancelled", res3.why);
        // no key at all
        Assert.False(Workshop.MakeAsync(null, Request(), o, null, null, CancellationToken.None).Result.ok);
    }

    [Fact]
    public void A_thing_is_written_quickly_and_its_motion_must_move()
    {
        var bat = new ItemDef { id = "item-bat", name = "bat", kind = "box", sizeM = 0.07f };
        var request = new ScriptRequest { kind = ScriptRequest.Thing, words = "야구 배트 만들어 줘. 그걸 들면 야구 스윙을 해", id = "item-bat", item = bat, language = "Korean" };
        string frozen = "wearable('handR')\noffer('swing', 'Swing')\nmotion('swing', 1, function(u, p) p.armR = {x = -60} end)\nfunction on_use() pet_play('swing') sound('whoosh') end\nfunction on_act(name) pet_play('swing') sound('whoosh') end\nfunction on_wear() pet_say('Ready!') end\nfunction on_unwear() end\n";
        string moving = frozen.Replace("p.armR = {x = -60}", "p.armR = {x = -60 + 120 * u}  p.spine = {y = 40 * math.sin(u * 3.14)}");
        (string, JObject) W(string s) => ("write", new JObject { ["script"] = s, ["title"] = "", ["how_to_play"] = "들면 스윙할 수 있어.", ["art"] = new JArray(), ["note"] = "the bat swings" });
        var model = new FakeModel().Then(W(frozen)).Then(W(moving)).Then(Done());
        var o = WorkshopOptions.For(ScriptRequest.Thing);
        Assert.True(o.quick); Assert.Equal("low", o.effort); Assert.True(o.maxSeconds <= 120);
        var res = Workshop.MakeAsync(model, request, o, null, null, CancellationToken.None).Result;
        Assert.True(res.ok, res.why);
        Assert.Equal(2, res.rounds); Assert.Contains("120 * u", res.script); Assert.Equal("들면 스윙할 수 있어.", res.howTo);
        Assert.Contains("FROZEN POSE", model.bodies[1].ToString());
        Assert.True(res.verdict.wearable); Assert.Empty(res.frames);
        // the first message of a quick job carries two worked things, the tools have no `look`
        Assert.Contains("WORKED PROGRAMS", model.bodies[0]["input"].ToString());
        Assert.DoesNotContain(Workshop.Tools(ScriptRequest.Thing), t => (string)t["name"] == "look");
        Assert.Contains(Workshop.Tools(ScriptRequest.Game), t => (string)t["name"] == "look");
        foreach (var t in Workshop.Tools(ScriptRequest.Game)) { Assert.Equal("function", (string)t["type"]); Assert.True((bool)t["strict"]); Assert.Equal(((JObject)t["parameters"]["properties"]).Count, ((JArray)t["parameters"]["required"]).Count); }
    }
}
