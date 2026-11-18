using LivingPartner.Host;
using LivingPartner.MarketHub;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// MODULES ARE SCRIPTS. The writer (ScriptWriter.cs) with a model that answers from a list - first a broken program,
/// then a mended one: the repair loop, which version is kept, the honest give-up, what the workbench says, the
/// pictures a script asks for. And the host's plain rules about scripts (ScriptRules.cs) with the REAL checkers:
/// what a scripted thing offers, wearing, a module from outside whose script breaks, the makers' own modules as
/// scripts, the migration of a saved state.
/// </summary>
public class ScriptWriterTests
{
    static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4 };

    /// <summary>A model that answers from a list (an Exception in the list is a call that fails).</summary>
    sealed class FakeModel : IScriptModel
    {
        readonly Queue<object> _answers;
        public readonly List<(string system, string user)> asked = new();
        public FakeModel(params object[] answers) { _answers = new Queue<object>(answers); }
        public string Name => "fake";
        public readonly List<bool> repairs = new();
        public Task<JObject> AskAsync(string system, string user, JObject schema, int maxTokens, CancellationToken ct, bool repair = false)
        {
            asked.Add((system, user)); repairs.Add(repair);
            object a = _answers.Count > 0 ? _answers.Dequeue() : new Exception("no more answers");
            if (a is Exception e) throw e;
            return Task.FromResult((JObject)a);
        }
    }

    static JObject Answer(string script, string title = "Test", string howTo = "Tap it.", params (string name, string drawing)[] art) => new JObject
    {
        ["title"] = title, ["howTo"] = howTo, ["script"] = script, ["phrases"] = new JArray("테스트 해 줘", "do the test"),
        ["art"] = new JArray(art.Select(a => (object)new JObject { ["name"] = a.name, ["drawing"] = a.drawing }).ToArray()),
    };

    // ---- programs of every quality (the real checkers say which is which)
    const string Broken = "local t = 0\nfunction update(dt)\n  rect(W / 2, 0.5, 0.2, 0.2, 'pink')\n  local nothing = nil\n  nothing.x = 1\nend\n";
    const string NoHands = "function update(dt)\n  disc(W / 2 + 0.3 * math.sin(time()), 0.5, 0.05, 'pink')\nend\n";
    const string SmallText = "local x, y = 0.5, 0.5\nfunction update(dt)\n  for _, t in ipairs(taps()) do x, y = t.x, t.y; add(1) end\n  disc(x, y, 0.06, 'pink')\n  text('tap anywhere', W / 2, 0.9, 0.02, 'ink')\nend\n";
    const string Draws = "function update(dt)\n  local n = #taps()\nend\n";
    static string Good => GlassScriptExamples.Find("catch").lua;
    const string ThingBroken = "function on_use()\n  local a = nil\n  a.b = 1\nend\n";
    const string ThingNeverPlays = "motion(\"spin\", 1.0, function(u, p) p.hips = {y = 360 * u} end)\nfunction on_use()\n  pet_say(\"Hi!\")\n  sound(\"pop\")\nend\n";

    static ScriptRequest Game(string words = "풍선 터뜨리기 게임 만들어 줘") => new ScriptRequest { kind = ScriptRequest.Game, words = words, language = "Korean", id = "game-test0001" };

    static (ScriptResult res, List<string> stages, List<string> log) Write(FakeModel model, ScriptRequest r, IList<ScriptExample> examples = null)
    {
        var stages = new List<string>(); var log = new List<string>();
        var res = ScriptWriter.WriteAsync(model, r, examples ?? ScriptWriter.GameExamples(), (stage, n, p) => { string s = ScriptWriter.StageText(stage, n); if (stages.Count == 0 || stages[^1] != s) stages.Add(s); }, log.Add, CancellationToken.None).Result;
        return (res, stages, log);
    }

    // ------------------------------------------------------------------ the repair loop
    [Fact]
    public void A_broken_program_goes_back_to_the_model_with_its_error_and_the_mended_one_is_kept()
    {
        var model = new FakeModel(Answer(Broken, "풍선 팡팡", "풍선을 톡 쳐요."), Answer(Good, "풍선 팡팡", "풍선을 톡 쳐요."));
        var (res, stages, log) = Write(model, Game());
        Assert.True(res.ok); Assert.Equal(Good.Trim(), res.script.Trim()); Assert.Equal("풍선 팡팡", res.title); Assert.Equal("풍선을 톡 쳐요.", res.howTo);
        Assert.Equal(2, res.calls); Assert.Equal(1, res.repairs); Assert.Equal(2, res.attempts.Count);
        Assert.Equal(new[] { false, true }, model.repairs);       // (the model is told which call is a repair: it thinks longer over that one)
        Assert.False(res.attempts[0].verdict.ran); Assert.Equal(5, res.attempts[0].verdict.errorLine);
        Assert.True(res.verdict.Clean);
        // what the workbench said, in order
        // (the last: the critic's turn - this model has none, so nothing comes of it)
        Assert.Equal(new[] { "Writing the code", "Trying it out", "Fixing a bug (1)", "Trying it out", "Checking it is what you asked" }, stages);
        Assert.Equal("Fixing a bug (2)", ScriptWriter.StageText(ScriptWriter.Fix, 2)); Assert.Equal("Painting the pictures", ScriptWriter.StageText(ScriptWriter.Paint, 0));      // (on the screen of the headset: English whatever the user speaks)
        // the first question: the API text VERBATIM (with the motion API), examples, the request, the hard rules
        string first = model.asked[0].user;
        Assert.Contains(GlassScriptApi.Doc.TrimEnd(), first); Assert.Contains("motion(name, seconds", first);
        Assert.Contains("풍선 터뜨리기 게임 만들어 줘", first); Assert.Contains("=== HARD RULES ===", first); Assert.Contains("=== EXAMPLES ===", first);
        // the repair: the program with its line numbers, the error with its line and the text of that line, and the order to write ALL of it again
        string second = model.asked[1].user;
        Assert.Contains("  5|   nothing.x = 1", second); Assert.Contains("line 5: attempt to index a nil value", second); Assert.Contains("nothing.x = 1", second);
        Assert.Contains("WHOLE corrected program", second); Assert.Contains(GlassScriptApi.Doc.TrimEnd(), second);
        // the log: sizes, times, attempt numbers and the verdict - never the program
        Assert.Contains(log, l => l.Contains("attempt 1") && l.Contains("ERROR line 5"));
        Assert.Contains(log, l => l.Contains("attempt 2 (repair 1)") && l.Contains("check: ok"));
        Assert.DoesNotContain(log, l => l.Contains("rect(W / 2") || l.Contains("function update"));
    }

    [Fact]
    public void The_best_version_is_kept_and_a_program_that_never_ran_is_not_handed_over()
    {
        // ok-but-the-hands-are-never-read, then two that break (a game has two repairs): the first is the best there is (ok with warnings > not ok)
        var (res, _, _) = Write(new FakeModel(Answer(NoHands), Answer(Broken), Answer(Broken), Answer(Broken)), Game());
        Assert.True(res.ok); Assert.Equal(NoHands, res.script); Assert.Equal(3, res.calls); Assert.Equal(2, res.repairs); Assert.Equal(1, res.verdict.Rank);
        // only small things to say (letters too small): good enough as it is - no repair, the user is waiting
        var small = new FakeModel(Answer(SmallText), Answer(Good));
        (res, _, _) = Write(small, Game());
        Assert.True(res.ok); Assert.Equal(1, res.calls); Assert.Equal(0, res.repairs); Assert.Equal(2, res.verdict.Rank); Assert.Single(small.asked);
        // ... but when a repair was needed anyway, the cleaner version wins
        (res, _, _) = Write(new FakeModel(Answer(NoHands), Answer(SmallText), Answer(Good)), Game());
        Assert.True(res.ok); Assert.Equal(2, res.calls); Assert.Equal(SmallText, res.script);
        // never anything that runs: the writer gives up after the first writing and two repairs, and says why
        var (bad, stages, log) = Write(new FakeModel(Answer(Broken), Answer(Broken), Answer(""), Answer(Draws)), Game());
        Assert.False(bad.ok); Assert.Equal("", bad.script); Assert.Equal(3, bad.calls); Assert.Equal(2, bad.repairs); Assert.NotEqual("", bad.why);
        Assert.Equal("Fixing a bug (2)", stages[^2]);
        Assert.Contains(log, l => l.Contains("could NOT be made to work"));
        Assert.Contains("안 돌아가", MarketRules.CouldNotWriteLine(true));
        // a call that fails is put once more; two failures end it
        (res, _, _) = Write(new FakeModel(new Exception("HTTP 500 oops"), Answer(Good)), Game());
        Assert.True(res.ok); Assert.Equal(2, res.calls); Assert.Equal(0, res.repairs);
        (res, _, _) = Write(new FakeModel(new Exception("x"), new Exception("y"), Answer(Good)), Game());
        Assert.False(res.ok); Assert.Equal(2, res.calls);
        // a code fence round the program is taken off
        Assert.Equal("function update(dt) end\n", ScriptWriter.Clean("```lua\r\nfunction update(dt) end\r\n```"));
    }

    [Fact]
    public void The_examples_never_show_the_very_thing_that_is_asked_for()
    {
        var games = ScriptWriter.GameExamples();
        Assert.Contains(games, e => e.id == "omok");
        foreach (string words in new[] { "오목 게임 만들어 줘", "omok", "Gomoku please", "오목놀이" })
        {
            var shown = ScriptWriter.Pick(games, words);
            Assert.Equal(ScriptWriter.ExamplesShown, shown.Count); Assert.DoesNotContain(shown, e => e.id == "omok");
            Assert.All(shown, e => Assert.True(ScriptRules.Lines(e.lua) <= ScriptWriter.LongExample, e.id));      // two short ones: the prompt is read for every writing
            Assert.DoesNotContain(GlassScriptExamples.Omok.Trim(), ScriptWriter.Prompt(Game(words), shown));
        }
        // (for another board game the omok example may be shown: it is not the thing asked for)
        Assert.DoesNotContain(ScriptWriter.Pick(games, "뽀모도로 타이머"), e => e.id == "pomodoro");
        var things = ScriptWriter.RoomExamples(false);
        Assert.DoesNotContain(ScriptWriter.Pick(things, "야구배트 만들어 줘. 착용하면 야구 스윙 모션이 추가돼"), e => e.id == "bat");
        Assert.DoesNotContain(ScriptWriter.Pick(things, "작은 북. 누르면 밤비가 쳐"), e => e.id == "drum");
        Assert.Equal(2, ScriptWriter.Pick(things, "요요").Count);
        Assert.DoesNotContain(ScriptWriter.Pick(ScriptWriter.RoomExamples(true), "춤 춰 줘"), e => e.id == "skill-dance");
        // a thing's question carries the room API and what is known of the thing
        var bat = new ScriptRequest { kind = ScriptRequest.Thing, words = "야구배트. 착용하면 스윙", language = "Korean", item = new ItemDef { id = "item-1", name = "야구배트", kind = "toy", uses = new[] { "play" }, sizeM = 0.08f } };
        string prompt = ScriptWriter.Prompt(bat, ScriptWriter.Pick(things, bat.words));
        Assert.Contains(RoomScriptApi.Doc.TrimEnd(), prompt); Assert.Contains("THE THING: \"야구배트\", about 8 cm", prompt); Assert.Contains("wearable(slot)", prompt);
    }

    [Fact]
    public void The_pictures_a_script_asks_for_are_three_at_most_and_named_so_that_it_can_draw_them()
    {
        string script = Good.Replace("pic('star'", "pic('my balloon'").Replace("pic(\"star\"", "pic(\"my balloon\"");
        var model = new FakeModel(Answer(Good, "T", "h", ("My Balloon", "a round red balloon on a string"), ("my-balloon", "again"), ("", "x"), ("b", ""), ("C c", "a cloud"), ("d", "a kite"), ("e", "one too many")));
        var (res, _, _) = Write(model, Game());
        Assert.True(res.ok);
        Assert.Equal(new[] { "my-balloon", "c-c", "d" }, res.art.Select(a => a.name));
        Assert.Equal("a round red balloon on a string", res.art[0].drawing);
        var pack = MarketRules.ScriptGame("game-test0001", res.title, res.howTo, res.script, res.art.Select(a => a.name));
        Assert.Equal(3, pack.art.Count); Assert.All(pack.art, a => Assert.Equal("", a.file));
        // every picture is a file of the module (it travels with it); one that failed is left out and the game still runs
        pack.art[0].file = "market-game-test0001-art-my-balloon.png"; pack.art.RemoveAll(a => a.file.Length == 0);
        var m = new MarketModule { id = pack.id, kind = ModuleKinds.Game, name = pack.title, game = pack };
        Assert.Equal("", MarketRules.Problem(m));
        var art = Assert.Single(MarketPack.FileRefs(m), r => r.role == "art");
        Assert.Equal("my-balloon", art.part); Assert.Equal("-art-my-balloon", art.Suffix); Assert.Equal(".png", art.Ext);
        Assert.True(ScriptRules.CheckGame(script, new[] { "my-balloon" }).ok);
        // a thing's script has no pictures of its own
        var thing = new ScriptRequest { kind = ScriptRequest.Thing, words = "램프", item = new ItemDef { id = "item-1", name = "램프", sizeM = 0.1f } };
        var (lamp, _, _) = Write(new FakeModel(Answer(RoomScriptExamples.Lamp, "", "누르면 켜져.", ("x", "y"))), thing, ScriptWriter.RoomExamples(false));
        Assert.True(lamp.ok); Assert.Empty(lamp.art); Assert.Equal("누르면 켜져.", lamp.howTo);
    }

    // ------------------------------------------------------------------ things: what their scripts offer, wearing
    [Fact]
    public void A_thing_gets_the_script_that_passed_and_what_it_offers_is_read_from_it()
    {
        var item = new ItemDef { id = "item-bat00001", name = "야구배트", kind = "toy", uses = new[] { "play" }, sizeM = 0.08f, status = "ready", inRoom = true };
        var request = new ScriptRequest { kind = ScriptRequest.Thing, words = "야구배트 만들어 줘. 착용하면 야구 스윙 모션이 추가돼", language = "Korean", id = item.id, item = item };
        var model = new FakeModel(Answer(ThingBroken), Answer(ThingNeverPlays), Answer(RoomScriptExamples.Bat));
        var (res, stages, _) = Write(model, request, ScriptWriter.RoomExamples(false));
        Assert.True(res.ok); Assert.Equal(3, res.calls); Assert.True(res.verdict.Clean);
        Assert.Contains("Fixing a bug (2)", stages);
        Assert.Contains("DOES NOT WORK: the motion 'spin' is defined but never played", model.asked[2].user);
        Assert.True(res.verdict.wearable); Assert.Equal("handR", res.verdict.slot); Assert.Equal("swing", Assert.Single(res.verdict.offers).name); Assert.Contains("swing", res.verdict.motions);

        item.script = res.script;
        var tricks = ScriptRules.Tricks(item);
        Assert.True(tricks.Any); Assert.True(tricks.wearable); Assert.Equal("handR", tricks.slot);
        Assert.Equal("wear (handR), acts: swing 'Swing!'", ScriptRules.TricksLine(tricks));
        Assert.Equal("swing", ScriptRules.FindAct(tricks, "swing").name); Assert.Equal("swing", ScriptRules.FindAct(tricks, "Swing!").name); Assert.Equal("swing", ScriptRules.FindAct(tricks, "배트로 swing 해 봐").name);
        Assert.Null(ScriptRules.FindAct(tricks, "점프")); Assert.Null(ScriptRules.FindAct(tricks, ""));
        Assert.False(ScriptRules.Tricks(new ItemDef { id = "x", name = "x" }).Any);
        Assert.False(ScriptRules.Tricks(new ItemDef { id = "x", name = "x", script = "this is not lua" }).ok);
        Assert.Equal("", ScriptRules.TricksLine(ScriptRules.Tricks(new ItemDef { id = "l", name = "lamp", script = RoomScriptExamples.Lamp })));

        // wearing: the host keeps WHERE; one thing per place - wearing another takes the first off; a worn thing is out, not in the bag
        var wand = new ItemDef { id = "item-wand0001", name = "지팡이", sizeM = 0.07f, status = "ready", script = RoomScriptExamples.Wand };
        var lamp = new ItemDef { id = "item-lamp0001", name = "램프", sizeM = 0.1f, status = "ready", inRoom = true, script = RoomScriptExamples.Lamp };
        var items = new List<ItemDef> { item, wand, lamp };
        Assert.Equal("", ScriptRules.Wear(items, item, "", tricks, out var off)); Assert.Null(off); Assert.Equal("handR", item.wornOn); Assert.True(item.inRoom);
        Assert.Equal("", ScriptRules.Wear(items, wand, "", ScriptRules.Tricks(wand), out off));
        Assert.Same(item, off); Assert.Equal("", item.wornOn); Assert.Equal("handR", wand.wornOn); Assert.True(wand.inRoom);
        Assert.Equal("", ScriptRules.Wear(items, item, "head", tricks, out off)); Assert.Null(off); Assert.Equal("head", item.wornOn); Assert.Equal("handR", wand.wornOn);
        Assert.Equal("", ScriptRules.Wear(items, item, "elbow", tricks, out _)); Assert.Equal("handR", item.wornOn); Assert.Equal("", wand.wornOn);      // (no such place: where its script says)
        Assert.Equal("not-wearable", ScriptRules.Wear(items, lamp, "", ScriptRules.Tricks(lamp), out _)); Assert.Equal("", lamp.wornOn);
        Assert.Equal("no-script", ScriptRules.Wear(items, new ItemDef { id = "p", name = "plain", status = "ready" }, "", new ThingTricks(), out _));
        Assert.True(ScriptRules.Unwear(item)); Assert.Equal("", item.wornOn); Assert.False(ScriptRules.Unwear(item));
        // a module's thing is worn by nobody
        item.wornOn = "handR";
        Assert.Equal("", MarketRules.Wrap(item, 0, 0).item.wornOn); Assert.Equal(res.script, MarketRules.Wrap(item, 0, 0).item.script);
    }

    // ------------------------------------------------------------------ a script from somewhere else is run before it is installed
    [Fact]
    public void A_module_whose_script_breaks_is_refused_with_the_line_and_one_with_warnings_is_taken()
    {
        MarketModule Pack(string script) { var g = MarketRules.ScriptGame("game-abcd1234", "Balloons", "Tap.", script, new[] { "balloon" }); g.art[0].file = "balloon.png"; g.icon = "icon.png"; return new MarketModule { id = g.id, kind = ModuleKinds.Game, name = g.title, summary = "s", gives = "A new game.", icon = "icon.png", game = g }; }
        var files = new Dictionary<string, byte[]> { ["balloon.png"] = Png, ["icon.png"] = Png };

        // a .bambi file: the script and its pictures survive export -> import
        string problem = MarketPack.Unpack(MarketPack.Pack(Pack(Good), files), out var back, out var got);
        Assert.Equal("", problem); Assert.Equal(Good, back.game.script); Assert.Equal("script", back.game.template);
        Assert.Equal("balloon", Assert.Single(back.game.art).name); Assert.Equal("balloon.png", back.game.art[0].file); Assert.Equal(2, got.Count);
        // warnings only (the hands are never read): installed all the same
        Assert.Equal("", MarketPack.Unpack(MarketPack.Pack(Pack(NoHands), files), out back, out _)); Assert.Equal(NoHands, back.game.script);
        // a script that breaks: refused, with the line
        problem = MarketPack.Unpack(MarketPack.Pack(Pack(Broken), files), out back, out _);
        Assert.Null(back); Assert.True(ScriptRules.IsScriptProblem(problem)); Assert.Contains("line 5", problem); Assert.Contains("attempt to index a nil value", problem);
        Assert.False(ScriptRules.IsScriptProblem("no name"));
        // a thing and a skill likewise
        var thing = MarketRules.Wrap(new ItemDef { id = "item-abcd1234", name = "Bat", kind = "toy", uses = new[] { "play" }, sizeM = 0.08f, status = "ready", script = ThingBroken }, 0, 0);
        Assert.Contains("the thing's script does not run: line 3", MarketPack.Sanitise(thing));
        thing.item.script = RoomScriptExamples.Bat;
        Assert.Equal("", MarketPack.Sanitise(thing)); Assert.Equal(RoomScriptExamples.Bat, thing.item.script);
        var skill = new MarketModule { id = "skill-abcd1234", kind = ModuleKinds.Skill, name = "Spin", summary = "s", gives = "g", skill = new SkillDef { id = "skill-abcd1234", name = "Spin", phrases = new[] { "spin" }, script = RoomScriptExamples.SkillDance } };
        Assert.Equal("", MarketPack.Sanitise(skill)); Assert.Equal(RoomScriptExamples.SkillDance, skill.skill.script); Assert.Empty(skill.skill.steps);
        skill.skill.script = ThingBroken;
        Assert.Contains("the skill's script does not run", MarketPack.Sanitise(skill));

        // the hub: publish -> buy on another account; the buyer gets the program and the names of its pictures; a broken one is not put up
        long clock = 1_000_000;
        var hub = new Hub(new MemoryHubStore(), () => clock += 1000);
        var seller = hub.Register("만든이").value; var buyer = hub.Register("사는이").value;
        var offer = new HubPublish { module = Pack(Good), price = 25, files = files.ToDictionary(f => f.Key, f => Convert.ToBase64String(f.Value)) };
        var listing = hub.Publish(seller.token, offer);
        Assert.True(listing.Ok, listing.error); Assert.Equal("script", listing.value.template);
        Assert.Equal(ScriptRules.Lines(Good), listing.value.detail.scriptLines); Assert.Equal(1, listing.value.detail.art);
        Assert.True(hub.Buy(buyer.token, "game-abcd1234").Ok);
        var bought = Proto.FromJson<MarketModule>(hub.Package(buyer.token, "game-abcd1234").value);
        Assert.Equal("", MarketPack.Sanitise(bought));
        Assert.Equal(Good, bought.game.script); Assert.Equal("balloon", bought.game.art.Single().name);
        Assert.True(hub.FileOf(buyer.token, bought.game.art[0].file).Ok);
        var refused = hub.Publish(seller.token, new HubPublish { module = Pack(Broken), price = 5 });
        Assert.False(refused.Ok); Assert.Equal("module-invalid", refused.error);
        // a script may be 64 KB, and no more
        var big = Pack(Good); big.game.script = Good + "\n--" + new string('x', ScriptRules.MaxScriptBytes);
        Assert.Contains("too long", MarketRules.Problem(big));
        // what the shop says a module is made of
        Assert.Equal("Tap. - made of: a script (61 lines)", ScriptRules.Summary("Tap.", 61)); Assert.Equal("Tap.", ScriptRules.Summary("Tap.", 0));
        Assert.EndsWith(" - made of: a script (7 lines)", ScriptRules.Summary(new string('가', 300), 7)); Assert.Equal(140, ScriptRules.Summary(new string('가', 300), 7).Length);
    }

    // ------------------------------------------------------------------ what ships is a script too
    [Fact]
    public void Everything_of_the_makers_that_can_be_a_script_is_one_and_passes_the_checker()
    {
        var seeds = MarketSeed.All();
        // every thing: the script of its kind, which runs, does something when it is pressed and earns no warning that matters
        foreach (var e in seeds.Where(e => e.module.kind == ModuleKinds.Item))
        {
            var item = e.module.item;
            Assert.Equal(RoomScriptExamples.ForKind(item.kind), item.script);
            Assert.False(string.IsNullOrWhiteSpace(item.script), item.id + " has no script");
            var v = ScriptRules.CheckRoom(item.script, item);
            Assert.True(v.ok, item.id + ": " + v.Line); Assert.Empty(v.serious);
            Assert.Equal("", MarketPack.Sanitise(MarketRules.Clone(e.module)));
        }
        // dancing
        var dance = seeds.Single(e => e.module.id == "skill-dance").module.skill;
        Assert.Equal(RoomScriptExamples.ForSkill("skill-dance"), dance.script); Assert.NotEqual("", dance.script);
        var dv = ScriptRules.CheckRoom(dance.script, new ItemDef { id = dance.id, name = dance.name });
        Assert.True(dv.ok, dv.Line); Assert.True(dv.useEndless);       // (it dances until it is told to stop)
        Assert.NotEmpty(dance.steps);                                 // (the list of acts is still there for an app from before)
        var builtins = seeds.Where(e => e.module.game != null).ToList();
        Assert.All(builtins, e => { Assert.True(e.preinstalled); Assert.True(StarterGames.IsStarter(e.module.id)); Assert.Empty(e.module.game.script); });
        Assert.Equal(4,builtins.Count);
        Assert.True(ScriptRules.CheckGame(GlassScriptExamples.Shooter).Clean);
    }

    [Fact]
    public void A_saved_state_from_before_gets_the_scripts_once_and_what_the_user_made_is_left_alone()
    {
        var s = new MarketState(); var w = new WorldState();
        MarketRules.Merge(s, MarketSeed.All());
        Assert.False(s.scriptsSeeded);
        // a bag from before there were scripts: a chair and a bed from the shop, a thing the user had made, Dance as it always was
        s.installed.AddRange(new[] { "item-chair", "item-bed" });
        MarketRules.Sync(w, s);
        foreach (var i in w.items) i.script = "";
        var own = new ItemDef { id = "item-a52d813d", name = "책상", kind = "desk", uses = new[] { "table" }, sizeM = 0.12f, status = "ready" };
        w.items.Add(own);
        w.skills.RemoveAll(k => k.id == "skill-dance"); w.skills.Insert(0, WorldDefaults.Dance());
        Assert.Equal("", w.skills[0].script);

        var notes = ScriptRules.SeedScripts(w, s.catalogue);
        Assert.Equal(4, notes.Count);                                   // the chair, the bed, the record player that comes with the partner - and Dance
        Assert.Equal(RoomScriptExamples.ForKind("record"), w.items.Single(i => i.id == "item-record").script);
        Assert.Equal(RoomScriptExamples.ForKind("chair"), w.items.Single(i => i.id == "item-chair").script);
        Assert.Equal(RoomScriptExamples.ForKind("bed"), w.items.Single(i => i.id == "item-bed").script);
        Assert.Equal(RoomScriptExamples.ForSkill("skill-dance"), w.skills.Single(k => k.id == "skill-dance").script);
        Assert.Equal("", own.script);                                   // (the user's own desk is theirs: nothing is put into it)
        Assert.Empty(ScriptRules.SeedScripts(w, s.catalogue));          // once: the second time there is nothing left to do
        // a game needs no migration: an installed one is its module's pack at every start, whatever that is now
        var created = MarketRules.ScriptGame("game-testcatch", "Catch", "Catch the falling stars", GlassScriptExamples.Find("catch").lua);
        s.catalogue.Add(new MarketModule { id = created.id, name = created.title, kind = ModuleKinds.Game, game = created });
        s.installed.Add(created.id);
        MarketRules.Sync(w, s);
        var restored = w.games.Single(g => g.id == created.id); Assert.Equal("script", restored.template); Assert.Equal(created.script, restored.script);
        // the flag travels in the state file
        s.scriptsSeeded = true;
        Assert.True(Proto.FromJson<MarketState>(Proto.ToJson(s)).scriptsSeeded);
    }
}
