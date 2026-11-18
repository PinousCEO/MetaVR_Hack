using LivingPartner.Host;
using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// H7 (2026-11-18): what was measured with the real model and put right - the critic knows the pictures a module
/// has painted, a tool's keys are measured (overlaps, keys on a display, keys outside the field), the partner's own
/// name names no game - and what the host SAYS and SHOWS for the contest: no brand in the lines the host writes
/// itself, the workbench's stages in English whatever the user speaks, the default browser by itself.
/// </summary>
public class H7Tests
{
    static bool Hangul(string s) => (s ?? "").Any(ch => ch >= 0xAC00 && ch <= 0xD7A3);

    // ------------------------------------------------------------------ A. the writer
    [Fact]
    public void The_critic_is_told_which_pictures_are_painted_for_the_module()
    {
        var r = new ScriptRequest { kind = ScriptRequest.Game, words = "슈팅 게임 만들어 줘", language = "Korean" };
        string script = "function update(dt)\n  pic('mini', 0.5, 0.5, 0.12)\n  for _, s in ipairs(shots()) do add(1) end\nend\n";
        string with = ScriptWriter.JudgePrompt(r, "the design", script, new List<ScriptArt> { new ScriptArt { name = "mini", drawing = "a tiny copy of the deer, mint green" } });
        Assert.Contains("- mini: a tiny copy of the deer, mint green", with);
        Assert.Contains("they exist, exactly like the built-in stickers", with);
        Assert.Contains("has been RUN by a tester already", with);
        // (none asked for: said too, so that the critic does not wonder)
        string without = ScriptWriter.JudgePrompt(r, "the design", script);
        Assert.Contains("(none: it draws with shapes and the built-in stickers only)", without);
        Assert.DoesNotContain("- mini:", without);
    }

    [Theory]
    [InlineData("A tiny Bambi clone, cream and brown, smiling", "a tiny friendly storybook companion figure clone, cream and brown, smiling")]
    [InlineData("a mini-Bambi with a toy blaster", "a mini friendly storybook companion figure with a toy blaster")]
    [InlineData("Bambi's little shadow copy", "a small friendly storybook companion figure's little shadow copy")]
    [InlineData("밤비의 작은 분신", "a small friendly storybook companion figure의 작은 분신")]
    [InlineData("a round orange ball with one cream stripe", "a round orange ball with one cream stripe")]
    public void No_drawing_for_the_painter_carries_the_partners_name(string drawing, string given)
    {
        Assert.Equal(given, ScriptWriter.Nameless(drawing));
        var art = ScriptWriter.Art(Newtonsoft.Json.Linq.JArray.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(new[] { new { name = "clone", drawing } })));
        Assert.Equal(given, Assert.Single(art).drawing);
        Assert.DoesNotContain("bambi", art[0].drawing.ToLowerInvariant()); Assert.DoesNotContain("밤비", art[0].drawing);
        // ... and the writer is told so, and that what the request names must be recognisable
        string p = ScriptWriter.Prompt(new ScriptRequest { kind = ScriptRequest.Game, words = "x" }, new List<ScriptExample>());
        Assert.Contains("The painter does not know Bambi and is never given a name", p);
        Assert.Contains("not the built-in monsters", p);
    }

    [Fact]
    public void The_critic_reports_only_what_the_API_can_put_right()
    {
        string q = ScriptWriter.JudgePrompt(new ScriptRequest { kind = ScriptRequest.Game, words = "x" }, "", "function update(dt) end");
        Assert.Contains("what the API has no means for is not a defect", q);
        Assert.Contains("EITHER open hand blocks", q);
    }

    [Fact]
    public void The_rules_of_a_game_name_the_poses_and_the_sounds_that_exist()
    {
        string p = ScriptWriter.Prompt(new ScriptRequest { kind = ScriptRequest.Game, words = "a shooting game" }, ScriptWriter.Pick(ScriptWriter.GameExamples(), "a shooting game"));
        Assert.Contains("a name that is not here does NOTHING", p);
        foreach (string pose in GlassScript.Poses) Assert.Contains(" " + pose, p);
        foreach (string sound in SfxSynth.Names) Assert.Contains(sound, p);
        Assert.Contains("no button lies on a display()", p);
        Assert.Contains("it is called FIRST in update", p);
    }

    const string Head = "function start() pet_goto(0.14) end\nfunction update(dt)\n  panel(W / 2, 0.5, W - 0.06, 0.94, 'Todo List')\n";
    const string Tail = "  for _, t in ipairs(taps()) do sound('tap') end\nend\n";

    [Fact]
    public void Two_keys_that_overlap_and_a_key_on_a_display_are_found_with_their_numbers()
    {
        // the bottom of the to-do list the writer made at low effort on 2026-11-18: 'Delete' and 'Clear All' share 0.02 of height, and 'Clear All' lies on the line of hints
        string lua = Head +
            "  display('Pick a row first.', W / 2, 0.11, W - 0.16, 0.10, 'center')\n" +
            "  if button('Done', 1.37, 0.36, 0.30, 0.12, 'mint') then sound('tap') end\n" +
            "  if button('Delete', 1.37, 0.20, 0.30, 0.12, 'pink') then sound('tap') end\n" +
            "  if button('Clear All', 1.37, 0.10, 0.30, 0.12, 'lilac') then sound('tap') end\n" + Tail;
        var problems = ScriptRules.Layout(lua);
        Assert.Contains(problems, x => x.StartsWith("two buttons overlap") && x.Contains("'Delete'") && x.Contains("'Clear All'"));
        Assert.Contains(problems, x => x.StartsWith("a button lies on top of a display") && x.Contains("'Clear All'"));
        Assert.DoesNotContain(problems, x => x.Contains("'Done'"));
        // ... and that is "does not work" for the writer: one repair goes into it
        var v = ScriptRules.CheckGame(lua);
        Assert.True(v.ok); Assert.Contains(v.serious, x => x.StartsWith("two buttons overlap")); Assert.Equal(1, v.Rank);
        Assert.True(ScriptWriter.WantsRepair(v, 0, ScriptWriter.MaxGameRepairs));
        // put right, nothing is said
        string right = Head +
            "  display('Pick a row first.', 0.60, 0.11, 0.90, 0.10, 'center')\n" +
            "  if button('Done', 1.37, 0.38, 0.30, 0.12, 'mint') then sound('tap') end\n" +
            "  if button('Delete', 1.37, 0.24, 0.30, 0.12, 'pink') then sound('tap') end\n" +
            "  if button('Clear All', 1.37, 0.10, 0.30, 0.12, 'lilac') then sound('tap') end\n" + Tail;
        Assert.Empty(ScriptRules.Layout(right));
    }

    [Fact]
    public void A_key_that_reaches_out_of_the_field_is_found()
    {
        string lua = Head + "  if button('0', 0.5, 0.04, 0.2, 0.12) then sound('tap') end\n  if button('=', W - 0.05, 0.5, 0.2, 0.12) then sound('tap') end\n  if button('7', 0.5, 0.5, 0.2, 0.12) then sound('tap') end\n" + Tail;
        var problems = ScriptRules.Layout(lua);
        Assert.Contains(problems, x => x.Contains("the button '0' lies partly outside the field") && x.Contains("its lower edge is at y = -0.02"));
        Assert.Contains(problems, x => x.Contains("the button '=' lies partly outside the field") && x.Contains("its right edge"));
        Assert.DoesNotContain(problems, x => x.Contains("'7'"));
        // the label may stand last, as the kit allows
        Assert.Contains(ScriptRules.Layout(Head + "  button(0.5, 1.0, 0.2, 0.12, 'Top')\n" + Tail), x => x.Contains("the button 'Top'") && x.Contains("its upper edge"));
    }

    [Fact]
    public void Keys_that_only_touch_are_left_alone_and_no_more_than_four_things_are_said()
    {
        var touching = new[] { new ScriptRules.KitRect("button", "a", 0.5f, 0.5f, 0.2f, 0.12f), new ScriptRules.KitRect("button", "b", 0.7f, 0.5f, 0.2f, 0.12f), new ScriptRules.KitRect("button", "c", 0.5f, 0.63f, 0.2f, 0.12f) };
        Assert.Empty(ScriptRules.LayoutProblems(touching, 1.78f));
        // two displays on each other are not the hand's business
        Assert.Empty(ScriptRules.LayoutProblems(new[] { new ScriptRules.KitRect("display", "", 0.5f, 0.5f, 0.4f, 0.2f), new ScriptRules.KitRect("display", "", 0.5f, 0.5f, 0.3f, 0.1f) }, 1.78f));
        string many = Head;
        for (int i = 0; i < 8; i++) many += $"  button('k{i}', 0.5, 0.5, 0.3, 0.2)\n";
        Assert.Equal(ScriptRules.MaxLayoutProblems, ScriptRules.Layout(many + Tail).Count);
        // a script that breaks, or one without keys, says nothing here
        Assert.Empty(ScriptRules.Layout("function update(dt) nothing_like_this() end"));
        Assert.Empty(ScriptRules.Layout("function update(dt) disc(0.5, 0.5, 0.1, 'pink') end"));
        Assert.Empty(ScriptRules.Layout(null));
    }

    [Fact]
    public void Bambi_must_not_stand_in_front_of_the_middle_of_a_key()
    {
        // nobody sent Bambi anywhere: it stands in the middle of the floor, where this tool has its bottom key
        string lua = "function update(dt)\n  panel(W / 2, 0.5, W - 0.06, 0.94, 'Keys')\n  if button('0', W / 2, 0.10, 0.2, 0.12) then sound('tap') end\n  if button('1', 0.4, 0.5, 0.2, 0.12) then sound('tap') end\n" + Tail;
        Assert.Contains(ScriptRules.Layout(lua), x => x.StartsWith("Bambi stands in front of the button '0'"));
        // sent to the side, nothing is said; a key whose edge it only brushes is left alone; a flat board is Bambi's to walk on
        Assert.Empty(ScriptRules.Layout("function start() pet_goto(0.14) end\n" + lua));
        Assert.Null(ScriptRules.Hidden(new[] { new ScriptRules.KitRect("button", "row", 0.36f, 0.18f, 0.54f, 0.12f) }, 0.14f, 0f));
        Assert.NotNull(ScriptRules.Hidden(new[] { new ScriptRules.KitRect("button", "k", 0.15f, 0.10f, 0.2f, 0.12f) }, 0.14f, 0f));
        Assert.Null(ScriptRules.Hidden(new[] { new ScriptRules.KitRect("display", "", 0.15f, 0.10f, 0.2f, 0.12f) }, 0.14f, 0f));
        Assert.Empty(ScriptRules.Layout("flat(true)\n" + lua));
    }

    [Fact]
    public void Every_example_the_writer_is_shown_has_its_keys_in_order()
    {
        foreach (var e in GlassScriptExamples.All)
            Assert.True(ScriptRules.Layout(e.lua).Count == 0, e.id + ": " + string.Join(" | ", ScriptRules.Layout(e.lua)));
    }

    [Fact]
    public void The_partners_own_name_names_no_game()
    {
        var own = new[] { new GamePackDef { id = "game-1", title = "밤비 슈팅", template = "script" }, new GamePackDef { id = "game-2", title = "틱택토", template = "script" }, new GamePackDef { id = "game-3", title = "Bambi", template = "script" } };
        // (the sentence that was turned away on 2026-11-18 with "밤비 슈팅은 이미 있어. 새로 만들까, 그걸 틀까?")
        Assert.Null(PlayRules.OwnGameNamed(own, "밤비랑 캐치볼 하는 게임 만들어 줘. 화면 밖으로 공을 던져 줘"));
        Assert.Null(PlayRules.OwnGameNamed(own, "밤비야 낚시 게임 만들어 줘")); Assert.Null(PlayRules.OwnGameNamed(own, "make a racing game with Bambi"));
        // what IS named is still found - by the rest of its title
        Assert.Equal("game-1", PlayRules.OwnGameNamed(own, "슈팅 게임 만들어 줘")?.id);
        Assert.Equal("game-1", PlayRules.OwnGameNamed(own, "밤비 슈팅 만들어 줘")?.id);
        Assert.Equal("game-2", PlayRules.OwnGameNamed(own, "밤비야 틱택토 만들어 줘")?.id);
    }

    // ------------------------------------------------------------------ B. what the host says and shows
    static readonly string[] Brands = { "youtube", "유튜브", "유투브", "google", "구글", "chrome", "크롬", "edge", "엣지", "naver", "네이버", "microsoft", "windows", "윈도우", "file explorer", "mspaint" };       // ("the notepad", "the calculator" are plain words)

    [Fact]
    public void The_lines_the_host_says_about_a_PC_action_name_no_brand()
    {
        foreach (string d in PcRules.Dos)
            foreach (bool korean in new[] { true, false })
            {
                var apps = d == "openApp" ? PcRules.Apps.Select(a => a.id).ToArray() : new[] { d == "openUrl" ? "https://example.com" : "lofi" };
                foreach (string arg in apps)
                {
                    var p = PcRules.Plan(d, arg, korean);
                    Assert.True(p.Ok, d);
                    foreach (string line in new[] { PcRules.DoneLine(p, korean, "name", "title"), PcRules.DoneLine(PcRules.Plan(d, d == "openApp" ? arg : d == "openUrl" ? arg : "", korean), korean, "name") })
                        foreach (string brand in Brands) Assert.False(line.ToLowerInvariant().Contains(brand), $"{d} {arg}: '{line}' names {brand}");
                }
            }
        foreach (bool korean in new[] { true, false })
            foreach (string problem in new[] { "app", "url", "empty", "unknown" })
                foreach (string brand in Brands) Assert.DoesNotContain(brand, PcRules.ProblemLine(new PcPlan { problem = problem }, korean).ToLowerInvariant());
        Assert.Equal("영상 사이트 열었어.", PcRules.DoneLine(PcRules.Plan("youtube", ""), true));
        Assert.Equal("I opened the video site.", PcRules.DoneLine(PcRules.Plan("youtube", ""), false));
        Assert.Contains("영상 사이트에서 '로파이' 검색 결과를 열었어", PcRules.DoneLine(PcRules.Plan("youtube", "로파이"), true));
        Assert.Contains("the web", PcRules.DoneLine(PcRules.Plan("webSearch", "deer"), false));
        Assert.Equal("I opened the browser.", PcRules.DoneLine(PcRules.Plan("openApp", "크롬"), false));
    }

    [Fact]
    public void The_browser_is_the_default_browser_by_itself_and_no_words_can_become_that_target()
    {
        var p = PcRules.Plan("openApp", "browser");
        Assert.True(p.Ok); Assert.Equal(PcRules.DefaultBrowser, p.target); Assert.Equal("", p.args);
        Assert.DoesNotContain("http", p.target);        // (no site is chosen for the user)
        // what a sentence carries never comes out as that target
        Assert.False(PcRules.Plan("openApp", PcRules.DefaultBrowser).Ok);
        Assert.Equal("", PcRules.Url(PcRules.DefaultBrowser));
        Assert.Equal("url", PcRules.Plan("openUrl", PcRules.DefaultBrowser).problem);
        foreach (var a in PcRules.Apps) Assert.False(a.target.Contains("google", StringComparison.OrdinalIgnoreCase) || a.target.Contains("youtube", StringComparison.OrdinalIgnoreCase), a.id);
        // which program that is, is read out of the "open" command Windows keeps for the user's choice: a full path of an .exe or nothing
        Assert.Equal(@"C:\Program Files\Some Browser\browser.exe", PcRules.ExeOfCommand("\"C:\\Program Files\\Some Browser\\browser.exe\" --single-argument %1"));
        Assert.Equal(@"C:\Browser\b.exe", PcRules.ExeOfCommand(@"C:\Browser\b.exe -url %1"));
        Assert.Equal(@"C:\Browser\b.exe", PcRules.ExeOfCommand(@"C:\Browser\b.exe"));
        foreach (string no in new[] { "", null, "browser.exe %1", "\"browser.exe\" %1", "\"C:\\x\\run.bat\" %1", "rundll32 url.dll,FileProtocolHandler %1", "\"..\\b.exe\"", "cmd /c start %1" })
            Assert.Equal("", PcRules.ExeOfCommand(no));
    }

    [Fact]
    public void The_stages_of_the_workbench_are_English_whatever_the_user_speaks()
    {
        foreach (string stage in new[] { ScriptWriter.Write, ScriptWriter.Try, ScriptWriter.Fix, ScriptWriter.Paint, ScriptWriter.Review, "anything" })
        {
            string text = ScriptWriter.StageText(stage, 2);
            Assert.False(Hangul(text), text); Assert.True(text.Length >= 6);
        }
        Assert.Equal("Fixing a bug (2)", ScriptWriter.StageText(ScriptWriter.Fix, 2));
    }

    [Fact]
    public void What_the_shop_shows_of_the_makers_modules_is_English()
    {
        foreach (var e in MarketSeed.All())
        {
            var m = e.module;
            foreach (string shown in new[] { m.name, m.summary, m.gives, m.author, m.game?.title, m.game?.howTo, m.item?.name, m.item?.note, m.skill?.name, m.ambience?.name, m.ability?.name })
                Assert.False(Hangul(shown), $"{m.id}: '{shown}'");
        }
        foreach (var e in GlassScriptExamples.All) { Assert.False(Hangul(e.title), e.id); Assert.False(Hangul(e.howTo), e.id); }
    }

    [Fact]
    public void A_game_that_shows_Korean_text_goes_back_to_the_writer()
    {
        string body = "function update(dt)\n  rect(W / 2, 0.5, W, 1, 'cream')\n  for _, t in ipairs(taps()) do add(1) sound('tap') end\n  text(LINE, W / 2, 0.5 + 0.01 * score(), 0.06, 'brown')\nend\n";
        Assert.Equal("", ScriptRules.ShownKorean(body.Replace("LINE", "'Tap anywhere'")));
        Assert.Equal("'아무 데나 눌러'", ScriptRules.ShownKorean(body.Replace("LINE", "'아무 데나 눌러'")));
        var v = ScriptRules.CheckGame(body.Replace("LINE", "\"점수\""));
        Assert.True(v.ok); Assert.Contains(v.serious, x => x.StartsWith("the program shows text that is not English (\"점수\")"));
        Assert.DoesNotContain(ScriptRules.CheckGame(body.Replace("LINE", "'Score'")).serious, x => x.Contains("not English"));
        // what Bambi SAYS may be the user's language, and a comment is nobody's business
        Assert.Equal("", ScriptRules.ShownKorean("function start() pet_say('같이 하자!', 2) end\n-- '점수'를 올린다\nlocal a = 1 -- \"한글\"\n" + body.Replace("LINE", "'Go'")));
        Assert.Equal("'끝'", ScriptRules.ShownKorean("finish('끝') -- over"));
        // everything that ships is English already
        foreach (var e in GlassScriptExamples.All) Assert.True(ScriptRules.ShownKorean(e.lua).Length == 0, e.id);
    }

    // ------------------------------------------------------------------ C. the safety read
    [Fact]
    public void A_path_to_import_and_the_shop_to_trust_are_named_from_this_PC_only()
    {
        Assert.True(MarketRules.LocalOnly("import")); Assert.True(MarketRules.LocalOnly("hub"));
        // what the headset's shop board sends stays open to it
        foreach (string cmd in new[] { "buy", "install", "remove", "publish", "setPrice", "unpublish", "like", "offer", "open", "make", "screen", "", null }) Assert.False(MarketRules.LocalOnly(cmd), cmd);
    }

    [Theory]
    [InlineData("rep-0123abcd", "page-1.png", true)] [InlineData("des-0123abcd", "option-3-s.png", true)]
    [InlineData("rep-0123abcd\n", "page-1.png", false)] [InlineData("rep-0123abcd", "page-1.png\n", false)]
    [InlineData("rep-0123abcd", "..\\state.json", false)] [InlineData("rep-0123abcd", "../page-1.png", false)] [InlineData("..", "page-1.png", false)]
    [InlineData("rep-0123abcd", "page-1.png/..", false)] [InlineData("rep-0123ABCD", "page-1.png", false)] [InlineData("rep-0123abcd", "page-1.pdf", false)]
    [InlineData("rep-0123abcd", "C:page-1.png", false)] [InlineData("rep-0123abcd", "page-1.png:stream", false)] [InlineData("rep-0123abcd", "", false)]
    public void Only_a_documents_id_and_a_plain_picture_name_reach_the_store(string id, string file, bool served)
    {
        Assert.Equal(served, DocsRules.SafeServed(id, file));
        string store = Path.Combine(Path.GetTempPath(), "lp-docs");       // (the host joins its store, the id and the file exactly like this)
        if (served) Assert.StartsWith(Path.GetFullPath(store) + Path.DirectorySeparatorChar + id + Path.DirectorySeparatorChar, Path.GetFullPath(Path.Combine(store, id, file)));
    }
}
