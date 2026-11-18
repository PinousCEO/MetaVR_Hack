using System.Text.RegularExpressions;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

// The marketplace's books and its shelf, as plain data and plain rules. Nothing in this file touches the host, the
// network or the disk (the tests and the hub compile it on its own, next to WorldRules.cs): what the makers of the
// partner sell (MarketSeed), and what it means to install, remove, wrap and list a module on this PC (MarketRules).
// Market.cs is the part that lives in the host: it says the lines, calls the models, keeps the files and talks to the
// hub. The stars are not kept here any more: buying and selling happen on the hub (LivingPartner.MarketHub), between
// people; this PC only remembers what it has, and the last balance it heard.

/// <summary>What the host keeps of the marketplace between runs (in state.json, next to the world).</summary>
public sealed class MarketState
{
    public int stars = MarketRules.StartStars;                  // the balance on the hub as last heard (shown while the hub cannot be reached)
    /// <summary>The modules this PC holds a copy of: what came with the partner, what was bought or got from the hub, and what the user made that exists only as a module (a game, an ability, surroundings; a thing or skill of theirs that is put away).</summary>
    public List<MarketModule> catalogue = new();
    public List<string> installed = new();                      // module ids
    public List<string> seeded = new();                         // the built-in modules whose "installed from the start" has been applied once (so that removing one of them lasts)
    public List<string> own;                                    // the modules in the catalogue that the user made (null in a state file from before the hub: then everything not built in is theirs)
    public List<string> owned;                                  // what the user has bought or got on the hub, as last heard - and what was installed before there was a hub (null: not worked out yet)
    public string hub = "";                                     // where the hub is, when the user chose one ("" = this PC's own; the environment variable LP_MARKET_HUB goes before both)
    public int iconFailures;                                    // how many pictures for the shelf failed in a row (three: no more are asked for)
    public bool gamesUnbundled;  // read and written by older releases
    public bool threeGameLibrary;
    public List<ArchivedGame> archivedGames = new();
    /// <summary>The pictures in the world folder that have been looked at for a round plate behind the object (Market.cs, the repair of old icons): each is looked at once (null in an older state file).</summary>
    public List<string> iconsChecked;
    public List<string> fetchLater;                             // paid for (or got) on the hub, but the download failed: fetched again by itself when the hub is next heard from (null in an older state file)
    /// <summary>
    /// The records of the record player that are the user's OWN and come from nowhere else: tunes they had composed
    /// ("신나는 노래 하나 만들어줘") and what used to be skills that did nothing but play a tune. The other records are
    /// worked out (MarketRules.Records): the three that come with the partner, the tunes of the skills it knows and of
    /// what is installed. (null in an older state file.)
    /// </summary>
    public List<SongDef> records;
    /// <summary>The record player that comes with the partner has been put into the bag of a state from before, once - unless the user had a thing for music already (MarketRules.Tidy). Throwing it away afterwards lasts.</summary>
    public bool recordPlayerGiven;
    /// <summary>Things, skills and abilities that were plainly the same thing twice have been brought down to one, once (MarketRules.Tidy). What the user makes alike afterwards is theirs to keep.</summary>
    public bool duplicatesTidied;
    /// <summary>What ships is a script too: the things and skills of a state from before there were scripts have been given the scripts their modules carry, once (ScriptRules.SeedScripts; Market.cs says so in the log).</summary>
    public bool scriptsSeeded;
}

public sealed class ArchivedGame
{
    public MarketModule module;
    public bool installed, owned, mine;
}

/// <summary>One module that comes with the partner, and what the host needs to know about it beyond its listing.</summary>
public sealed class MarketSeedEntry
{
    public MarketModule module;
    public bool preinstalled;           // the partner has it from the start
    public string koName = "";          // what a Korean speaker calls it (a thing is put in the bag under this name)
    public string ko = "", en = "";     // what the partner says when it gets it, as its own discovery
    public string koSummary = "", koGives = "";      // the listing's two lines in Korean (the page on the PC shows them to a Korean speaker)
    public string draw = "";            // what to draw on its sticker: the object that stands for it (a thing itself; a game, a skill, surroundings, an ability: see MarketSeed.Drawings)
    public string drawEnemy = "", drawPickup = "";   // games with creatures and pick-ups of their own: what to draw for each
}

/// <summary>What happened when a module was installed or removed on this PC (paying for one is the hub's business).</summary>
public enum Deal { Done, Unknown, Already, NotInstalled }

/// <summary>The shelf as it is on the first day: everything "Nilo Studio" made.</summary>
public static class MarketSeed
{
    public const string Author = "Nilo Studio";

    static MarketSeedEntry Entry(MarketModule m, bool pre, string koName, string ko, string en, string draw = "")
    {
        m.author = Author; m.builtin = true; m.version = "1.0";
        return new MarketSeedEntry { module = m, preinstalled = pre, koName = koName, ko = ko, en = en, draw = draw };
    }

    // ------------------------------------------------------------------ games
    static MarketSeedEntry Game(string id, string template, string title, int price, bool pre, string koName, string summary, string gives, string howTo, string[] tags, GamePackDef dress = null, string drawEnemy = "", string drawPickup = "")
    {
        var g = dress ?? new GamePackDef();
        g.id = id; g.template = template; g.title = title; g.howTo = howTo;
        var e = Entry(new MarketModule { id = id, kind = ModuleKinds.Game, name = title, summary = summary, gives = gives, price = price, tags = tags, game = g }, pre, koName,
            $"{koName} 받아 왔어! 이제 가방에 있어. 놀이 메뉴에서 같이 하자.", $"{title} is in my bag now! You'll find it on the Play page.");
        e.drawEnemy = drawEnemy; e.drawPickup = drawPickup;
        return e;
    }

    /// <summary>
    /// A game of the makers' that IS a script - the same kind of module the partner writes when it is asked for a game
    /// (GlassScriptExamples: the ones marked for the shelf). null when that example does not exist (any more).
    /// </summary>
    static MarketSeedEntry ScriptGame(string id, string example, int price, string koName, string summary, string gives, string[] tags)
    {
        var e = GlassScriptExamples.Find(example);
        if (e == null || string.IsNullOrWhiteSpace(e.lua)) return null;
        return Game(id, ScriptRules.Template, e.title, price, false, koName, summary, gives, e.howTo, tags, new GamePackDef { script = e.lua });
    }

    /// <summary>
    /// The Lua port of one of the makers' own glass games (GlassScriptExamples: "survival" = Vampire Night, "umbrella" =
    /// Shadow Umbrella): what ships is a script too. "" when there is no port - the game is then the way of playing
    /// that is built into the apps, as it was (Ported says which template the pack has).
    /// </summary>
    static string Port(string example) => GlassScriptExamples.Find(example)?.lua ?? "";
    static string Ported(string example, string native) => string.IsNullOrWhiteSpace(Port(example)) ? native : ScriptRules.Template;

    /// <summary>One bar of a bass line that walks on its root: the same figure under every chord.</summary>
    static int[] Walk(params int[] roots)
    {
        var line = new List<int>();
        foreach (int r in roots) line.AddRange(new[] { r, 0, 0, r, 0, 0, r, 0, r, 0, 0, r, 0, r + 12, 0, 0 });
        return line.ToArray();
    }

    /// <summary>A minor: a creeping figure that climbs in the third bar and falls home in the fourth.</summary>
    public static SongDef VampireTune() => new SongDef
    {
        id = "song-vampire", name = "Vampire Night", bpm = 128, bars = 4, lead = "square", minor = true, volume = 0.8f,
        melody = new[] { 76, -1, 0, 76, 77, -1, 76, 0, 72, -1, 0, 69, -1, 0, 0, 0,   74, -1, 0, 74, 76, -1, 74, 0, 69, -1, 0, 65, -1, 0, 0, 0,
                         76, -1, 0, 76, 77, -1, 76, 0, 81, -1, 0, 79, -1, 0, 81, 0,   79, -1, 76, 0, 74, -1, 71, 0, 69, -1, -1, -1, 0, 0, 0, 0 },
        chords = new[] { 57, 50, 57, 52 }, bass = Walk(45, 38, 45, 40),
        kick = "x---x---x---x---", snare = "----x-------x---", hat = "x-x-x-x-x-x-x-xx",
    };

    /// <summary>D minor, plucked: drops that patter down the chord, bar after bar - the tune of the umbrella game.</summary>
    public static SongDef UmbrellaTune() => new SongDef
    {
        id = "song-umbrella", name = "Shadow Umbrella", bpm = 104, bars = 4, lead = "pluck", minor = true, volume = 0.75f,
        melody = new[] { 74, 0, 77, 0, 81, 0, 77, 0, 74, 0, 77, 0, 81, -1, 0, 0,   69, 0, 72, 0, 76, 0, 72, 0, 69, 0, 72, 0, 76, -1, 0, 0,
                         67, 0, 70, 0, 74, 0, 70, 0, 67, 0, 70, 0, 74, -1, 76, 0,   77, 0, 76, 0, 74, 0, 72, 0, 74, -1, -1, -1, 0, 0, 0, 0 },
        chords = new[] { 50, 57, 55, 50 }, bass = Walk(38, 45, 43, 38),
        kick = "x-------x-------", snare = "", hat = "--x---x---x---x-",
    };

    /// <summary>D minor: the second club tune, for the DJ booth - a stabbing figure that moves up through Gm and Am.</summary>
    public static SongDef NeonTune() => new SongDef
    {
        id = "song-neon", name = "Neon", bpm = 126, bars = 4, lead = "pluck", minor = true, volume = 0.8f,
        melody = new[] { 74, 0, 74, 77, 0, 74, 0, 81, 0, 79, 77, 0, 74, 0, 72, 0,   79, 0, 79, 82, 0, 79, 0, 74, 0, 77, 79, 0, 82, 0, 79, 0,
                         81, 0, 81, 84, 0, 81, 0, 76, 0, 79, 81, 0, 84, 0, 81, 0,   74, 0, 77, 0, 81, 0, 77, 0, 74, -1, 0, 72, 74, -1, -1, 0 },
        chords = new[] { 50, 55, 57, 50 }, bass = Walk(38, 43, 45, 38),
        kick = "x---x---x---x---", snare = "----x-------x---", hat = "--x-x-x---x-x-xx",
    };

    // ------------------------------------------------------------------ things
    static MarketSeedEntry Thing(string kindId, string name, string koName, int price, string summary, string gives, string ko, string en, string draw, params string[] moreTags)
    {
        var k = ItemKinds.Find(kindId);
        string id = "item-" + kindId;
        // (what a thing DOES is a script, also for the things that ship: RoomScriptExamples has one for every built-in kind)
        var item = new ItemDef { id = id, name = name, kind = k.id, uses = (string[])k.uses.Clone(), sizeM = k.sizeM, status = "ready", progress = 1, script = RoomScriptExamples.ForKind(k.id) ?? "" };
        var tags = new List<string> { koName }; tags.AddRange(moreTags);
        return Entry(new MarketModule { id = id, kind = ModuleKinds.Item, name = name, summary = summary, gives = gives, price = price, tags = tags.ToArray(), item = item }, false, koName, ko, en, draw);
    }

    // ------------------------------------------------------------------ skills, surroundings, abilities
    static MarketSeedEntry Skill(SkillDef s, int price, bool pre, string koName, string summary, string gives, params string[] tags)
    {
        s.status = "ready";
        string phrase = s.phrases.Length > 0 ? s.phrases[0] : s.name;
        string koPhrase = s.phrases.FirstOrDefault(p => p.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) ?? phrase;
        return Entry(new MarketModule { id = s.id, kind = ModuleKinds.Skill, name = s.name, summary = summary, gives = gives, price = price, tags = tags, skill = s }, pre, koName,
            $"{koName} 배웠어! \"{koPhrase}\"라고 하면 돼.", $"I learned {s.name}! Just say \"{phrase}\".");
    }

    static MarketSeedEntry Mood(AmbienceDef a, int price, bool pre, string koName, string summary, string gives, params string[] tags) =>
        Entry(new MarketModule { id = a.id, kind = ModuleKinds.Ambience, name = a.name, summary = summary, gives = gives, price = price, tags = tags, ambience = a }, pre, koName,
            $"짠, {koName}! 가방에 넣어 놨으니까 거기서 언제든 다시 켤 수 있어.", $"Ta-da, {a.name}! It's in your bag now - switch it on there any time.");

    static MarketSeedEntry Ability(string id, string name, string koName, int price, bool pre, string summary, string gives, string prompt, string[] examples, params string[] tags)
    {
        string koExample = examples.FirstOrDefault(p => p.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) ?? examples[0];
        string enExample = examples.FirstOrDefault(p => !p.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) ?? examples[0];
        return Entry(new MarketModule { id = id, kind = ModuleKinds.Ability, name = name, summary = summary, gives = gives, price = price, tags = tags, ability = new AbilityDef { id = id, name = name, prompt = prompt, examples = examples } }, pre, koName,
            $"{koName} 능력이 생겼어! \"{koExample}\"처럼 물어봐.", $"I can do that now: {name}! Try \"{enExample}\".");
    }

    /// <summary>
    /// What the makers once sold and no longer do, and what must not linger anywhere - on the shelf, in the bag, among
    /// what is owned, in the shop's list: the "Music" skill (music is the record player and its records), "Dust
    /// Bunnies" (the same survival game as Vampire Night in other clothes) and "Foggy Window" (the wipe game: taken
    /// out on 2026-11-17, with its way of playing).
    /// </summary>
    public static readonly string[] Retired = new[] { "skill-music" }.Concat(StarterGames.RetiredIds).ToArray();

    /// <summary>The games that used to be installed from the start (until 2026-11-17). They are free in the shop now; a state from before has them taken out once (MarketRules.Merge).</summary>
    public static readonly string[] OnceBundledGames = { "game-tennis", "game-rps", "game-pop", "game-redlight" };

    /// <summary>Dancing, as it comes with the partner: the same skill as ever, and what it does is written as a script too (RoomScriptExamples.ForSkill).</summary>
    public static SkillDef Dance()
    {
        var dance = WorldDefaults.Dance();
        dance.script = RoomScriptExamples.ForSkill(dance.id) ?? "";
        return dance;
    }

    public static List<MarketSeedEntry> All()
    {
        var list = new List<MarketSeedEntry>();

        var tennis = StarterGames.Tennis(); var keeper = StarterGames.Keeper(); var defense = StarterGames.Defense();
        list.Add(Game(keeper.id, keeper.template, keeper.title, 0, true, "골키퍼", "Save Nilo’s five penalty kicks. Each save sends the ball back across the glass.", "Includes the 3D penalty shoot-out.", keeper.howTo, new[] { "골키퍼", "승부차기", "축구", "penalty", "goalkeeper" }, keeper));
        list.Add(Game(tennis.id, tennis.template, tennis.title, 0, true, "테니스", "A rally across the screen. Return Nilo’s serve with either hand.", "Play tennis with Nilo.", tennis.howTo, new[] { "테니스", "tennis", "sports" }, tennis));
        list.Add(Game(defense.id, defense.template, defense.title, 0, true, "악마의 문", "Defend the gate from incoming demons in your room. Block with the left palm and cast with the right hand.", "Includes the 3D defense game.", defense.howTo, new[] { "악마의 문", "디펜스", "demon gate", "defense" }, defense));
        var table=StarterGames.Gomoku(); table.title="Gomoku"; table.howTo="Black points and pinches. White points to an empty intersection until the stone is placed.";
        list.Add(Game(table.id,table.template,table.title,0,true,"오목","Play five in a row together on your TV. The headset wearer pinches to place black stones, the other player points to place white stones.","Includes the shared TV board.",table.howTo,new[]{"오목","gomoku","five in a row"},table));
        // ---- skills: dancing comes with the partner; the others are bought. There is no "Music" skill any more: music is
        // ONE thing, the record player and its records (see MarketRules.Records). The three below stay skills - each also
        // changes the surroundings and dances, studies or naps - and the tune of an installed one is a record as well.
        list.Add(Skill(Dance(), 0, true, "춤", "A club tune, club lights round your work area, and Nilo dancing in the middle of it.", "Nilo can dance - lights, tune and all.", "춤", "댄스", "dance", "club"));
        list.Add(Skill(new SkillDef
        {
            id = "skill-dj", name = "DJ Booth", note = "Turns the room round the desk into a club, puts the Neon set on and dances.",
            phrases = new[] { "DJ booth", "디제이 부스", "디제이 해 줘", "be the DJ", "클럽 열어 줘", "party time" },
            steps = new List<SkillStep> { new SkillStep { act = "ambience" }, new SkillStep { act = "music" }, new SkillStep { act = "emote", arg = "happy", seconds = 1.5f }, new SkillStep { act = "dance" } },
            song = NeonTune(),
            ambience = new AmbienceDef { id = "amb-dj", name = "DJ Booth", pattern = "spots", c0 = "#0E0A2A", c1 = "#FFD23F", c2 = "#B66DFF", dim = 0.85f, speed = 1.4f, density = 1.4f, beat = true },
        }, 40, false, "디제이 부스", "Nilo takes the decks: a second club tune of its own (\"Neon\"), gold and violet lights that pulse with it, and a dance.", "Nilo can turn your desk into a club night and dance to its own set.", "디제이 부스", "디제이", "클럽", "dj", "club", "party"));
        list.Add(Skill(new SkillDef
        {
            id = "skill-rainyfocus", name = "Rainy Focus", note = "Lets rain fall round the work area, puts lo-fi on and sits down to study beside you.",
            phrases = new[] { "rainy focus", "집중 모드", "비 오는 집중", "focus mode", "공부 모드", "let's focus" },
            steps = new List<SkillStep> { new SkillStep { act = "ambience" }, new SkillStep { act = "music" }, new SkillStep { act = "activity", arg = "study" } },
            song = WorldDefaults.Lofi(), ambience = WorldDefaults.RainAll(),
        }, 25, false, "비 오는 집중", "Rain on every side, a lo-fi loop, and Nilo studying next to you (when it has a desk and a chair).", "Nilo can set up a rainy afternoon to concentrate in - and study along.", "집중 모드", "집중", "비", "focus", "rain", "study"));
        list.Add(Skill(new SkillDef
        {
            id = "skill-lullaby", name = "Lullaby", note = "Brings the night sky out, plays a lullaby and goes to sleep.",
            phrases = new[] { "lullaby", "자장가", "자장가 불러 줘", "잘 시간이야", "bedtime", "sing me to sleep" },
            steps = new List<SkillStep> { new SkillStep { act = "ambience" }, new SkillStep { act = "music" }, new SkillStep { act = "activity", arg = "nap" } },
            song = WorldDefaults.Lullaby(), ambience = WorldDefaults.NightSky(),
        }, 20, false, "자장가", "Stars come out round the desk, a slow lullaby plays, and Nilo curls up for a nap (when it has a bed).", "Nilo can wind the day down: night sky, a lullaby, and a nap.", "자장가", "잠", "밤하늘", "lullaby", "sleep", "night"));

        // ---- surroundings: rain and the night sky come with the partner
        list.Add(Mood(WorldDefaults.RainAll(), 0, true, "비", "Rain all round the work area, soft and steady.", "Nilo can make it rain round your desk.", "비", "rain"));
        list.Add(Mood(WorldDefaults.NightSky(), 0, true, "밤하늘", "The room goes dark and fills with slow stars.", "Nilo can bring the night sky out.", "밤하늘", "별", "night", "stars"));
        list.Add(Mood(WorldDefaults.ClubLights(), 12, false, "클럽", "Pink and blue lights sweeping the room, pulsing with whatever tune is on.", "Nilo can turn the room into a club.", "클럽", "club", "party"));
        list.Add(Mood(WorldDefaults.Aurora(), 15, false, "오로라", "Green and violet curtains of light drifting overhead.", "Nilo can hang an aurora over your desk.", "오로라", "aurora"));
        list.Add(Mood(WorldDefaults.Petals(), 10, false, "봄", "A bright spring day with petals floating by.", "Nilo can bring spring in, petals and all.", "봄", "벚꽃", "꽃잎", "spring", "petals"));
        list.Add(Mood(WorldDefaults.Snow(), 10, false, "눈", "Quiet snowfall on a pale winter morning.", "Nilo can let it snow.", "눈", "겨울", "snow", "winter"));
        // the grand ones: whole views that take the place of the room outside the work area
        list.Add(Mood(WorldDefaults.Peak(), 30, false, "산 정상", "A mountain top at first light: ranges all round, one great peak, a sea of cloud far below.", "Nilo can move your desk to the top of a mountain.", "산 정상", "산", "산꼭대기", "mountain", "peak", "view"));
        list.Add(Mood(WorldDefaults.Coast(), 30, false, "바닷가", "The seaside on a clear day: the horizon, sun glittering on the water, islands, foam on the sand.", "Nilo can move your desk to the seaside.", "바닷가", "바다", "해변", "sea", "beach", "coast", "view"));
        list.Add(Mood(WorldDefaults.SunsetCoast(), 25, false, "노을 해변", "The same beach at dusk: a violet sky, an orange sun low over the water.", "Nilo can take your desk to a beach at sunset.", "노을 해변", "노을", "석양", "sunset", "beach", "view"));

        // ---- things: one of each built-in shape. Each `gives` promises only what that kind of thing allows (Activities).
        list.Add(Thing("desk", "Desk", "책상", 0, "A little wooden desk, just Nilo's size.", "Nilo gets a place to work - and can study, once there is a chair too.",
            "책상이다! 의자만 있으면 앉아서 공부할 수 있겠다.", "A desk! With a chair I could sit down and study.", "a small wooden desk with a drawer, seen from the front at a slight angle, warm wood colour", "table", "탁자"));
        list.Add(Thing("chair", "Chair", "의자", 0, "A round-backed chair in soft rose.", "Nilo can sit down - and study, when there is a desk.",
            "의자다! 이제 앉을 수 있어. 책상이 있으면 공부도 할 수 있겠다.", "A chair! Now I can sit down - and study, when there's a desk.", "a small rounded chair with a curved back, soft rose colour, seen at a three-quarter angle", "stool", "seat"));
        list.Add(Thing("bed", "Bed", "침대", 30, "A snug bed with a sky-blue blanket.", "Nilo can take a nap, and sit on the edge of it.",
            "침대다! 이제 낮잠 잘 수 있겠다.", "A bed! Now I can take a nap.", "a small cosy bed with a plump pillow and a sky-blue blanket, seen at a three-quarter angle", "nap", "잠"));
        list.Add(Thing("sofa", "Sofa", "소파", 30, "A plump lavender sofa for two very small friends.", "Nilo can sit back and rest on it, or doze off.",
            "소파다! 푹 앉아서 쉬거나 한숨 잘 수 있겠다.", "A sofa! I can sink into it, or doze off.", "a small plump two-seat sofa with round arms, lavender colour, seen from the front", "couch", "쇼파"));
        list.Add(Thing("book", "Book", "책", 10, "A storybook with a honey-yellow cover.", "Nilo can read - sitting down, if there is a chair.",
            "책이다! 이제 책 읽을 수 있어.", "A book! Now I can read.", "a closed storybook with a honey-yellow cover and a ribbon bookmark, slightly tilted", "read", "독서"));
        list.Add(Thing("ball", "Ball", "공", 0, "A bouncy orange ball.", "Nilo can play with it.",
            "공이다! 이걸로 놀 수 있겠다.", "A ball! I can play with this.", "a bouncy orange ball with one cream stripe", "play", "놀이"));
        list.Add(Thing("lamp", "Lamp", "램프", 15, "A desk lamp with a warm yellow shade.", "A warm little light beside Nilo's things.",
            "램프다! 책상이 따뜻해 보이겠다.", "A lamp! The desk will look so warm.", "a small desk lamp with a warm yellow shade, glowing softly", "light", "조명", "스탠드"));
        list.Add(Thing("plant", "Plant", "화분", 10, "A leafy plant in a round pot.", "Something green for the desk.",
            "화분이다! 책상이 초록초록해지겠다.", "A plant! Something green for my desk.", "a leafy green houseplant in a round cream pot", "식물", "나무", "green"));
        list.Add(Thing("cup", "Mug", "머그잔", 10, "A white mug of something warm.", "A warm mug to keep Nilo company.",
            "머그잔이다! 옆에 두면 아늑하겠다.", "A mug! That will be cosy to have nearby.", "a white mug of hot cocoa with a little steam, seen from the side", "컵", "커피", "cup", "cocoa"));
        // (the record player comes with the partner, in the bag: every tune is one of its records)
        var player = Thing("record", "Record player", "레코드 플레이어", 0, "A turntable with a wooden base and a tiny record. Press it to choose a record.", "Nilo can put a record on with it.",
            "레코드 플레이어다! 이걸로 노래 틀 수 있겠다.", "A record player! I can put a tune on with it.", "a small record player with a wooden base, a black vinyl record and a tone arm, seen from above at an angle", "턴테이블", "엘피", "turntable", "vinyl", "music");
        player.preinstalled = true;
        list.Add(player);
        list.Add(Thing("speaker", "Speaker", "스피커", 20, "A chunky little speaker in slate blue.", "Nilo can put a tune on with it.",
            "스피커다! 이걸로 노래 틀 수 있겠다.", "A speaker! I can put a tune on with it.", "a chunky little speaker with one round cone, slate blue, seen from the front", "라디오", "radio", "music"));
        list.Add(Thing("food", "Cake", "케이크", 15, "A slice of strawberry cake.", "A treat on the desk, just for the mood.",
            "케이크다! 보기만 해도 기분 좋다.", "Cake! Just looking at it makes me happy.", "a slice of strawberry shortcake on a small plate, pink and cream", "간식", "snack", "dessert"));
        list.Add(Thing("toy", "Toy robot", "장난감 로봇", 20, "A wind-up robot with a round head.", "Nilo can play with it.",
            "장난감 로봇이다! 같이 놀 수 있겠다.", "A toy robot! We can play together.", "a small wind-up toy robot with a round head and a key on its back, honey yellow", "로봇", "장난감", "robot", "play"));

        // ---- abilities: instructions that join the partner's own (no code). Each relaxes the "one short sentence" rule only as far as it needs.
        list.Add(Ability("ability-calc", "Pocket calculator", "계산기", 0, true, "Sums, percentages, tips and splits - with the working shown in one line, so you can check it.", "Nilo can work numbers out and show how it got there.",
            "When the user asks you to work something out - arithmetic, a percentage, a tip, splitting a bill, a unit conversion, how many days until a date - calculate it carefully and answer with the working AND the result in ONE short line, " +
            "for example \"128 × 37 = 128 × 30 + 128 × 7 = 3,840 + 896 = 4,736\". Break a multiplication into parts like that before you add them up, and check the last digit before you answer. Use digits and the signs × ÷ + −. " +
            "If the question can be read two ways (which order, which unit), say in a few words how you read it. If the numbers are too long for you to be sure of, say that they should check it with a real calculator rather than guess. " +
            "For this, your reply may be one line of working plus the answer, even when that is longer than usual.",
            new[] { "128 곱하기 37은?", "What is 15% of 240?", "3만 8천 원을 네 명이 나누면?", "How many days until Christmas?" }, "계산기", "계산", "calculator", "math"));
        list.Add(Ability("ability-translate", "Translator", "번역기", 15, false, "Korean to English and back, keeping the tone: casual stays casual, polite stays polite.", "Nilo can translate between Korean and English without flattening your tone.",
            "When the user asks you to translate (\"번역해 줘\", \"영어로 뭐야?\", \"how do you say this in Korean?\"), translate between Korean and English: Korean in, English out; English in, Korean out - unless they name the direction. " +
            "Keep the tone of the original: casual stays casual (반말 stays 반말), polite stays polite, a joke stays a joke; translate the meaning, not word by word. " +
            "Give the translation itself first, in quotation marks, in the target language, with nothing before it. Add a very short note in the user's language only when a word has no good equivalent or the sentence could mean two things. " +
            "For this, the translation may be as long as the original needs; only your own words round it stay short.",
            new[] { "\"오늘 회의 좀 미뤄도 될까요?\" 영어로 번역해 줘", "How do you say \"I'm running late\" in Korean?", "이 문장 영어로 뭐야?" }, "번역기", "번역", "translator", "english", "korean"));
        list.Add(Ability("ability-ideas", "Idea buddy", "아이디어 친구", 20, false, "Three quick ideas, different in kind - then it asks which one to grow.", "Nilo can brainstorm with you: three ideas, then deeper on the one you pick.",
            "When the user asks for ideas, wants to brainstorm or says they are stuck (\"아이디어 좀 줘\", \"뭐 하면 좋을까?\", \"help me think of a name\"), give exactly three quick ideas that differ in kind - one safe, one playful, one bold - each in a handful of words, numbered 1, 2, 3 on one line. " +
            "Then ask which one to grow. When they pick one, grow it: two or three concrete next steps, again short. Build on what they said earlier in this conversation, and never bring back an idea they turned down. " +
            "For this, the no-lists rule is relaxed to that one numbered line.",
            new[] { "발표 제목 아이디어 좀 줘", "Help me think of a name for my app", "주말에 뭐 하면 좋을까?" }, "아이디어 친구", "아이디어", "브레인스토밍", "ideas", "brainstorm"));
        list.Add(Ability("ability-proofread", "Proofreader", "맞춤법 검사", 10, false, "Spelling, spacing and grammar fixed with as few changes as possible, in your own tone.", "Nilo can check a sentence for you and say what it changed.",
            "When the user asks you to check, fix or polish a sentence or a short text they say or type (\"이 문장 좀 봐 줘\", \"맞춤법 맞아?\", \"proofread this\"), give the corrected text first, in quotation marks, in the same language and the same tone as the original, changing as little as possible. " +
            "Then name the one or two changes that matter most in a few words (spelling, spacing, grammar, a clearer word). If it was already right, say so and change nothing. " +
            "Text on the screen that you have not looked at cannot be proofread from memory: for that, choose reviewScreen instead. " +
            "For this, the corrected text may be as long as the original; only your own words round it stay short.",
            new[] { "\"않되\"가 맞아 \"안 돼\"가 맞아?", "Proofread this: \"I has went to the store yesterday\"", "이 문장 맞춤법 좀 봐 줘" }, "맞춤법 검사", "맞춤법", "교정", "proofread", "grammar", "spelling"));

        foreach (var e in list)
        {
            if (Korean.TryGetValue(e.module.id, out var ko)) { e.koSummary = ko.summary; e.koGives = ko.gives; }
            if (e.draw.Length == 0 && Drawings.TryGetValue(e.module.id, out string draw)) e.draw = draw;
        }
        return list;
    }

    /// <summary>
    /// The sticker of everything the makers sell that is not a thing: ONE object that stands for the game, the skill,
    /// the surroundings or the ability, described for the drawing (the style is Maker.IconStyle, the same as every
    /// thing's: a die-cut sticker). Tennis is a module like the chair and the desk, and its picture belongs with theirs.
    /// </summary>
    static readonly Dictionary<string, string> Drawings = new()
    {
        ["game-keeper"] = "A white and black soccer ball in front of a white soccer goal net, two orange keeper gloves below it",
        [SwarmRules.PackId] = "A horned demon emerging from a stone portal, amber eyes and a falling comet in a dark dungeon arch",
        [StarterGames.GomokuId] = "Black and ivory stones resting on a small maple go board",
        ["game-tennis"] = "a small tennis racket leaning at an angle, coral-red frame, cream strings and a wood-brown grip, with a bright yellow-green tennis ball resting against its strings",
        ["game-rps"] = "three chubby cartoon hands bunched together as one group: a fist (rock) in rose, an open palm (paper) in cream and a hand holding out two fingers (scissors) in sky blue",
        ["game-pop"] = "a plump five-pointed star in honey yellow bursting open with a pop: a little crack on one side and a few tiny sparkles and star crumbs flying off it",
        ["game-redlight"] = "a pink mugunghwa flower (rose of Sharon: five round petals, a deep red centre, a cream stamen) on a short green stem with two leaves, and beside it a small cream cartoon hand held up flat in a 'stop' gesture",
        ["game-vampire"] = "a small round plum-purple bat with two tiny fangs holding a little cream game controller in its feet, a pale yellow crescent moon tucked behind one wing",
        ["game-catch"] = "a small woven basket in warm wood with three honey-yellow stars falling into it, one of them already inside",
        ["game-pairs"] = "two playing cards leaning on each other, cream with a rose back, each showing the same sky-blue star",
        ["game-focus"] = "a round kitchen timer shaped like a tomato, coral red with a small green leaf on top and a cream dial",
        ["game-tag"] = "a pair of small honey-yellow running shoes with little motion puffs behind them and one cream sparkle",
        ["skill-dance"] = "two beamed music notes in rose and sky blue with a four-pointed cream sparkle beside them, and underneath a tiny pair of honey-yellow dancing shoes caught mid-step",
        ["skill-dj"] = "a pair of chunky headphones in violet with gold ear cups resting on a small DJ turntable",
        ["skill-rainyfocus"] = "a small open book under a sky-blue umbrella, three raindrops falling beside it",
        ["skill-lullaby"] = "a sleepy crescent moon in butter yellow wearing a little rose nightcap, one small star and a soft music note beside it",
        ["amb-rain"] = "a plump sky-blue rain cloud with five raindrops falling from it",
        ["amb-night"] = "a little glass jar with a cork, filled with a night sky: deep blue inside, tiny cream stars and a crescent moon",
        ["amb-club"] = "a mirror disco ball hanging on a short chain, its tiles pink and sky blue, two little light sparkles on it",
        ["amb-aurora"] = "a wavy ribbon of aurora light in mint green and violet curling above three tiny snowy pine trees",
        ["amb-petals"] = "a short cherry-blossom branch with five pink flowers, two petals drifting off it",
        ["amb-snow"] = "a six-pointed snowflake in pale blue with a tiny snowman leaning against it",
        ["amb-peak"] = "a snowy mountain peak rising above a ring of soft white clouds, a small butter-yellow sun beside its top",
        ["amb-coast"] = "a little beach: a curl of sky-blue wave with white foam running onto pale sand, a small yellow sun above it",
        ["amb-sunset"] = "the same little beach at sunset: an orange half-sun sinking behind a violet wave with foam on rose-gold sand",
        ["ability-calc"] = "a pocket calculator in honey yellow with round cream buttons and a small plus sign on its display",
        ["ability-translate"] = "two overlapping speech bubbles, one rose and one sky blue, with a small two-way arrow between them",
        ["ability-ideas"] = "a glowing light bulb in butter yellow with a sage-green base and three little sparkle rays",
        ["ability-proofread"] = "a coral-red pencil with a cream eraser drawing a small check mark on a slip of paper",
    };

    /// <summary>The same two lines of every listing in Korean, as a shop would say them (summary, what the partner gains).</summary>
    static readonly Dictionary<string, (string summary, string gives)> Korean = new()
    {
        ["game-keeper"] = ("밤비가 차는 입체 축구공을 두 손으로 막아 화면 안으로 되돌려요.", "다섯 번의 페널티킥을 막는 골키퍼 게임이에요."),
        [SwarmRules.PackId] = ("문에서 나오는 악마를 막아 주세요. 왼손을 펴서 방어하고 오른손 검지로 주문을 쏘세요.", "레벨마다 세 가지 강화 중 하나를 골라 수비를 이어가요."),
        [StarterGames.GomokuId] = ("같은 TV에서 번갈아 돌을 놓아요. 착용자는 흑, 맞은편 사람은 백입니다.","가로·세로·대각선으로 다섯 개를 먼저 이으면 승리해요."),
        ["game-tennis"] = ("화면을 사이에 두고 주고받는 랠리. 밤비가 모니터 안에서 서브를 넣으면 손으로 받아쳐요.", "밤비랑 테니스를 칠 수 있어요."),
        ["game-rps"] = ("진짜 손으로 하는 가위바위보. 셋에 맞춰 손 모양을 내요.", "밤비랑 가위바위보를 할 수 있어요."),
        ["game-pop"] = ("책상에서 별이 둥실 떠올라요. 시간이 다 되기 전에 최대한 많이 터뜨려요.", "밤비가 별 터뜨리기 한 판을 열어 줘요."),
        ["game-redlight"] = ("무궁화 꽃이 피었습니다. 밤비가 안 볼 때만 다가가고, 돌아보면 멈춰요.", "밤비랑 무궁화 꽃이 피었습니다를 할 수 있어요."),
        ["game-vampire"] = ("책상에 밤이 내리면 박쥐들이 몰려와요. 해가 뜰 때까지 밤비를 지켜 주세요. 마늘이 도움이 돼요.", "새 게임: 밤새 박쥐들로부터 밤비를 지켜요."),
        ["game-catch"] = ("위에서 별이 떨어져요. 손을 그 아래에 두면 바구니가 따라오고, 밤비도 같이 뛰어요.", "새 게임: 세 개를 놓치기 전에 떨어지는 별을 받아요."),
        ["game-pairs"] = ("책상 위에 뒤집힌 카드 열두 장. 톡 눌러 두 장을 뒤집고, 짝이 맞으면 열린 채로 남아요.", "새 게임: 여섯 쌍을 모두 찾아요."),
        ["game-focus"] = ("25분 집중, 5분 휴식을 되풀이해요. 고리가 차오르는 동안 밤비도 옆에서 공부해요.", "작은 도구: 책상 위의 집중 타이머."),
        ["game-tag"] = ("밤비가 책상 위를 뛰어다니며 손을 피해요. 30초 동안 최대한 많이 터치해요.", "새 게임: 밤비랑 술래잡기."),
        ["skill-dance"] = ("클럽 음악에 클럽 조명, 그 한가운데서 춤추는 밤비.", "밤비가 조명이랑 음악까지 갖추고 춤을 춰요."),
        ["skill-dj"] = ("밤비가 디제이가 돼요. 직접 만든 두 번째 클럽 곡 'Neon', 음악에 맞춰 반짝이는 금빛과 보랏빛 조명, 그리고 춤.", "밤비가 책상을 클럽으로 바꾸고 자기 곡에 맞춰 춤을 춰요."),
        ["skill-rainyfocus"] = ("사방에 내리는 비, 로파이 음악, 그리고 옆에서 같이 공부하는 밤비(책상과 의자가 있을 때).", "밤비가 집중하기 좋은 비 오는 오후를 만들고 같이 공부해요."),
        ["skill-lullaby"] = ("책상 둘레에 별이 뜨고, 느린 자장가가 흐르고, 밤비는 웅크리고 잠들어요(침대가 있을 때).", "밤비가 밤하늘과 자장가로 하루를 마무리하고 잠들어요."),
        ["amb-rain"] = ("작업 공간 둘레로 조용히 내리는 비.", "밤비가 책상 둘레에 비를 내려 줘요."),
        ["amb-night"] = ("방이 어두워지고 별이 천천히 흘러요.", "밤비가 밤하늘을 펼쳐 줘요."),
        ["amb-club"] = ("방을 휩쓰는 분홍빛과 파란빛 조명. 틀어 놓은 음악에 맞춰 반짝여요.", "밤비가 방을 클럽으로 바꿔 줘요."),
        ["amb-aurora"] = ("머리 위로 흐르는 초록빛과 보랏빛 빛의 커튼.", "밤비가 책상 위에 오로라를 걸어 줘요."),
        ["amb-petals"] = ("꽃잎이 흩날리는 화창한 봄날.", "밤비가 꽃잎 날리는 봄을 데려와요."),
        ["amb-snow"] = ("하얀 겨울 아침에 조용히 내리는 눈.", "밤비가 눈을 내려 줘요."),
        ["amb-peak"] = ("새벽빛이 드는 산 정상. 사방으로 이어지는 산줄기와 우뚝 솟은 봉우리, 발아래로 펼쳐진 구름바다.", "밤비가 책상을 산 꼭대기로 옮겨 줘요."),
        ["amb-coast"] = ("맑은 날의 바닷가. 수평선과 반짝이는 윤슬, 멀리 섬들, 모래 위로 밀려오는 하얀 파도.", "밤비가 책상을 바닷가로 옮겨 줘요."),
        ["amb-sunset"] = ("해 질 녘의 같은 해변. 보랏빛 하늘 아래 주황빛 해가 바다 위로 낮게 걸려 있어요.", "밤비가 책상을 노을 지는 해변으로 데려가 줘요."),
        ["item-desk"] = ("밤비 몸에 딱 맞는 작은 나무 책상.", "밤비에게 일할 자리가 생겨요. 의자까지 있으면 공부도 할 수 있어요."),
        ["item-chair"] = ("등받이가 둥근 연분홍 의자.", "밤비가 앉을 수 있어요. 책상이 있으면 공부도 해요."),
        ["item-bed"] = ("하늘색 이불이 덮인 포근한 침대.", "밤비가 낮잠을 자거나 걸터앉을 수 있어요."),
        ["item-sofa"] = ("아주 작은 친구 둘이 앉을 만한 통통한 라벤더색 소파.", "밤비가 기대앉아 쉬거나 꾸벅꾸벅 졸 수 있어요."),
        ["item-book"] = ("꿀빛 표지의 이야기책.", "밤비가 책을 읽을 수 있어요. 의자가 있으면 앉아서 읽어요."),
        ["item-ball"] = ("통통 튀는 주황색 공.", "밤비가 가지고 놀 수 있어요."),
        ["item-lamp"] = ("따뜻한 노란 갓을 쓴 책상 램프.", "밤비 물건 곁을 밝히는 작고 따뜻한 불빛."),
        ["item-plant"] = ("둥근 화분에 담긴 잎이 무성한 식물.", "책상에 초록빛을 더해 줘요."),
        ["item-cup"] = ("따뜻한 음료가 담긴 하얀 머그잔.", "밤비 곁을 지키는 따뜻한 머그잔."),
        ["item-record"] = ("나무 받침에 작은 레코드판이 올라간 턴테이블. 누르면 레코드를 고를 수 있어요.", "밤비가 이걸로 레코드를 틀 수 있어요."),
        ["item-speaker"] = ("청회색의 통통한 작은 스피커.", "밤비가 이걸로 음악을 틀 수 있어요."),
        ["item-food"] = ("딸기 케이크 한 조각.", "책상 위의 달콤한 기분 전환."),
        ["item-toy"] = ("머리가 둥근 태엽 로봇.", "밤비가 가지고 놀 수 있어요."),
        ["ability-calc"] = ("덧셈, 퍼센트, 팁, 더치페이까지. 풀이 과정을 한 줄로 보여 줘서 바로 확인할 수 있어요.", "밤비가 계산을 하고 어떻게 풀었는지도 보여 줘요."),
        ["ability-translate"] = ("한국어와 영어를 오가며 말투는 그대로. 반말은 반말로, 존댓말은 존댓말로.", "밤비가 말투를 살려서 한국어와 영어를 번역해 줘요."),
        ["ability-ideas"] = ("결이 다른 아이디어 세 개를 빠르게. 그러고는 어느 걸 키울지 물어봐요.", "밤비랑 브레인스토밍: 아이디어 셋, 그리고 고른 하나를 더 깊이."),
        ["ability-proofread"] = ("맞춤법, 띄어쓰기, 문법을 최소한만 고쳐요. 말투는 그대로.", "밤비가 문장을 봐 주고 무엇을 고쳤는지 알려 줘요."),
    };
}

/// <summary>What installing, removing, publishing and listing mean - as arithmetic on the books (MarketState) and on the world's lists.</summary>
public static class MarketRules
{
    public const int StartStars = 300, MaxPrice = 999, MaxAbilities = 8, MaxAbilityPrompt = 1200;
    public const string You = "You";
    /// <summary>
    /// The ways of playing. "script" is the one every game MADE WITH WORDS has: the game is a small Lua program the
    /// partner wrote (GamePackDef.script, ScriptWriter.cs). The others are the makers' own, built into the apps.
    /// </summary>
    public static readonly string[] Templates = { "tennis", "keeper", SwarmRules.Template, "gomoku", "rps", "pop", "redlight", "survival", "umbrella", ScriptRules.Template };
    /// <summary>The glass games: played inside the PC screen when the partner is in the PC, on the desk otherwise (a scripted game is one of them).</summary>
    public static readonly string[] GlassTemplates = { "survival", "umbrella", ScriptRules.Template };

    static readonly Regex IdRx = new(@"^[A-Za-z0-9][A-Za-z0-9_-]{1,59}\z");

    /// <summary>A copy that shares nothing with the original (a module's payload must not change when the world's copy does, nor the other way round).</summary>
    public static T Clone<T>(T o) where T : class => o == null ? null : Proto.FromJson<T>(Proto.ToJson(o));

    static string Flat(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>An id that can be a name (of a module, of its tune, of its surroundings): letters, digits, '_' and '-', 2 to 60 of them.</summary>
    public static bool SafeId(string id) => IdRx.IsMatch(id ?? "");

    // How long the words of a module may be at the very most, and how many of them there may be. Every module's words
    // end up in snapshots, on the shop's page and before the language model, so none of them is without an end. A
    // module that comes from somewhere else is cut to less than this before it gets here (MarketPack.Sanitise), and
    // nothing made on this PC comes near: these are the backstop for whatever reaches Problem by another way.
    // (a tag may be as long as a phrase: a skill of the user's own is listed under its first phrases)
    const int HardName = 60, HardLine = 320, HardAsked = 600, HardTags = 12, HardTag = 80, HardPhrase = 80, HardArg = 200, HardStepSeconds = 600;
    public const int GameTitleMax = 40, GameHowToMax = 160, GameNameMax = 24, AbilityNameMax = 40, AbilityExamplesMax = 6, AbilityExampleMax = 80;

    static bool Longer(string s, int max) => s != null && s.Length > max;
    /// <summary>A number that is one, inside a range. (NaN passes every test of the form x &lt; a || x &gt; b: that form is used nowhere here.)</summary>
    static bool Within(float v, float min, float max) => float.IsFinite(v) && v >= min && v <= max;

    // ------------------------------------------------------------------ is this module sound?
    /// <summary>What is wrong with a module ("" = nothing): every module on the shelf has an id that can be a file name, one payload that fits its kind, and a payload the headset can carry out.</summary>
    /// <summary>
    /// The commands of the marketplace that only a caller on this PC may give (lpctl, the page), never a device on
    /// the network: "import" names a PATH on this PC that the host then opens (any file; one on a network share makes
    /// the PC knock on that server), "hub" names the shop this PC trusts with the user's stars and modules.
    /// </summary>
    public static bool LocalOnly(string cmd) => cmd is "import" or "hub";

    /// <summary>
    /// What a `publish` COMMAND from a client comes to: "publish" | "install" | "have". The user (2026-11-18): "I pressed
    /// Fish Fishing in the marketplace - it was already there - and it said it PUT IT UP." The headset's shop board had
    /// Sell as the only button of every card of the user's own, and the host did as it was told. A press on the board
    /// sells only through its explicit Sell control, which marks the command (ShopPress.SellMark in text). A publish from
    /// a headset WITHOUT the mark is a plain press (a build from before the mark): what is the user's own and not in the
    /// bag goes back into the bag ("install"), anything else stays as it is ("have") - nothing is put up for sale.
    /// The page on the PC, lpctl and a sentence ("이거 20별에 팔아 줘") say "sell" in so many words: always "publish".
    /// </summary>
    public static string PublishMeans(bool fromHeadset, string mark, bool mine, bool installed)
    {
        if (!fromHeadset || string.Equals((mark ?? "").Trim(), ShopPress.SellMark, StringComparison.OrdinalIgnoreCase)) return "publish";
        return mine && !installed ? "install" : "have";
    }

    public static string Problem(MarketModule m)
    {
        if (m == null) return "no module";
        if (!IdRx.IsMatch(m.id ?? "")) return "the id cannot be used as a name";
        if (Array.IndexOf(ModuleKinds.All, m.kind) < 0) return "unknown kind '" + m.kind + "'";
        if (string.IsNullOrWhiteSpace(m.name)) return "no name";
        if (m.price < 0 || m.price > MaxPrice) return "the price is out of range";
        if (Longer(m.name, HardName) || Longer(m.summary, HardLine) || Longer(m.gives, HardLine) || Longer(m.author, HardName) || Longer(m.version, 20)) return "a line of the listing is too long";
        if (m.tags != null && (m.tags.Length > HardTags || m.tags.Any(t => Longer(t, HardTag)))) return "too many tags, or one that is too long";
        int payloads = (m.item != null ? 1 : 0) + (m.skill != null ? 1 : 0) + (m.ambience != null ? 1 : 0) + (m.game != null ? 1 : 0) + (m.ability != null ? 1 : 0);
        if (payloads != 1) return payloads == 0 ? "no payload" : "more than one payload";
        if (m.song != null && m.kind != ModuleKinds.Ambience) return "only surroundings carry a tune beside their payload";
        switch (m.kind)
        {
            case ModuleKinds.Item:
                if (m.item == null) return "a thing without its ItemDef";
                if (m.item.id != m.id) return "the thing's id is not the module's";
                if (m.item.uses == null || m.item.uses.Length == 0 || m.item.uses.Any(u => Array.IndexOf(ItemUses.All, u) < 0)) return "the thing is for nothing the partner knows";
                if (!ItemKinds.All.Any(k => k.id == m.item.kind)) return "the thing has no built-in shape";
                if (!Within(m.item.sizeM, WorldRules.SizeMin, WorldRules.SizeMax)) return "the thing's size is out of range";
                if (Longer(m.item.name, HardName) || Longer(m.item.note, HardLine) || Longer(m.item.asked, HardAsked)) return "a line of the thing is too long";
                if (m.item.uses.Length > ItemUses.All.Length) return "the thing is for too many things";
                if (!float.IsFinite(m.item.x) || !float.IsFinite(m.item.z) || !float.IsFinite(m.item.yaw) || !float.IsFinite(m.item.seat) || !float.IsFinite(m.item.top) || !float.IsFinite(m.item.progress)) return "the thing carries a number that is not one";
                if (ScriptRules.Bytes(m.item.script) > ScriptRules.MaxScriptBytes) return "the thing's script is too long";
                if (!string.IsNullOrEmpty(m.item.wornOn) && Array.IndexOf(ScriptRules.Slots, m.item.wornOn) < 0) return "the thing is worn where nothing can be worn";
                if (m.item.parts != null)
                {
                    if (m.item.parts.Count > ShapePart.Max) return "the thing is made of too many parts";
                    foreach (var p in m.item.parts)
                        if (p == null || !(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) && float.IsFinite(p.sx) && float.IsFinite(p.sy) && float.IsFinite(p.sz) && float.IsFinite(p.rx) && float.IsFinite(p.ry) && float.IsFinite(p.rz) && float.IsFinite(p.round)))
                            return "a part of the thing carries a number that is not one";
                }
                return "";
            case ModuleKinds.Skill:
                if (m.skill == null) return "a skill without its SkillDef";
                if (m.skill.id != m.id) return "the skill's id is not the module's";
                return SkillProblem(m.skill);
            case ModuleKinds.Ambience:
                if (m.ambience == null) return "surroundings without their AmbienceDef";
                if (m.ambience.id != m.id) return "the surroundings' id is not the module's";
                string a = AmbienceProblem(m.ambience);
                return a.Length > 0 ? a : m.song != null ? SongProblem(m.song) : "";
            case ModuleKinds.Game:
                if (m.game == null) return "a game without its GamePackDef";
                if (m.game.id != m.id) return "the game's id is not the module's";
                return GameProblem(m.game);
            default:
                if (m.ability == null) return "an ability without its AbilityDef";
                if (m.ability.id != m.id) return "the ability's id is not the module's";
                if (string.IsNullOrWhiteSpace(m.ability.name)) return "the ability has no name";
                if (Longer(m.ability.name, AbilityNameMax)) return "the ability's name is too long";
                if ((m.ability.prompt ?? "").Trim().Length < 20) return "the ability has no instructions";
                if (m.ability.prompt.Length > MaxAbilityPrompt) return "the ability's instructions are too long";
                if (m.ability.examples != null && (m.ability.examples.Length > AbilityExamplesMax || m.ability.examples.Any(e => Longer(e, AbilityExampleMax)))) return "the ability has too many examples, or one that is too long";
                return "";
        }
    }

    /// <summary>A tune as the headset plays it: every line whole bars long and no longer than the tune (a shorter one repeats), every note in range, drums of one bar or of the whole tune.</summary>
    public static string SongProblem(SongDef s)
    {
        if (s == null) return "no tune";
        if (s.bars != 2 && s.bars != 4) return "a tune has 2 or 4 bars";
        if (s.bpm < WorldRules.BpmMin || s.bpm > WorldRules.BpmMax) return "the tempo is out of range";
        if (Array.IndexOf(WorldRules.Leads, s.lead) < 0) return "unknown lead '" + s.lead + "'";
        if (!Within(s.swing, 0f, 0.5f)) return "the swing is out of range";
        if (!Within(s.volume, 0f, 1f)) return "the volume is out of range";
        if (Longer(s.name, HardName)) return "the tune's name is too long";
        int steps = s.bars * 16;
        foreach (var (name, line) in new[] { ("melody", s.melody), ("bass", s.bass) })
        {
            if (line == null) return name + " is missing";
            if (line.Length % 16 != 0 || line.Length > steps || (line.Length > 0 && steps % line.Length != 0)) return $"{name} has {line.Length} steps, not {steps}";
            if (line.Any(n => n > 0 && (n < WorldRules.NoteMin || n > WorldRules.NoteMax)) || line.Any(n => n < -1)) return name + " has a note out of range";
            if (line.Length > 0 && line[0] < 0) return name + " starts by holding a note that was never struck";
        }
        if (s.chords == null || (s.chords.Length != 0 && s.chords.Length != s.bars)) return "one chord per bar";
        if (s.chords.Any(n => n < 0 || (n > 0 && (n < WorldRules.NoteMin || n > WorldRules.NoteMax)))) return "a chord is out of range";
        foreach (var (name, d) in new[] { ("kick", s.kick), ("snare", s.snare), ("hat", s.hat) })
        {
            if (d == null) return name + " is missing";
            if (d.Length != 0 && d.Length != 16 && d.Length != steps) return $"{name} has {d.Length} steps";
            if (d.Any(ch => ch != 'x' && ch != '-')) return name + " has a character that is neither a hit nor a rest";
        }
        bool heard = s.melody.Any(n => n > 0) || s.bass.Any(n => n > 0) || s.chords.Any(n => n > 0) || (s.kick + s.snare + s.hat).Contains('x');
        return heard ? "" : "there is nothing to hear in it";
    }

    public static string AmbienceProblem(AmbienceDef a)
    {
        if (a == null) return "no surroundings";
        if (Array.IndexOf(WorldRules.Patterns, a.pattern) < 0) return "unknown pattern '" + a.pattern + "'";
        foreach (string c in new[] { a.c0, a.c1, a.c2 }) if (WorldRules.Colour(c, "") != c) return "'" + WorldRules.Short(c, 16) + "' is not a colour written as #RRGGBB";
        if (!Within(a.dim, 0f, 1f)) return "the dimming is out of range";
        if (!Within(a.speed, 0.1f, 3f) || !Within(a.density, 0.1f, 3f)) return "speed or density is out of range";
        if (Longer(a.name, HardName)) return "the surroundings' name is too long";
        return "";
    }

    public static string SkillProblem(SkillDef s)
    {
        if (string.IsNullOrWhiteSpace(s.name)) return "the skill has no name";
        if (Longer(s.name, HardName) || Longer(s.note, HardLine) || Longer(s.asked, HardAsked)) return "a line of the skill is too long";
        if (s.phrases != null && (s.phrases.Length > WorldRules.MaxPhrases || s.phrases.Any(p => Longer(p, HardPhrase)))) return "the skill has too many phrases, or one that is too long";
        // (a skill is a short list of acts - or a script, which then is what it does: SkillDef.script)
        bool scripted = !string.IsNullOrWhiteSpace(s.script);
        if (ScriptRules.Bytes(s.script) > ScriptRules.MaxScriptBytes) return "the skill's script is too long";
        if (s.steps == null || (s.steps.Count == 0 && !scripted) || s.steps.Count > WorldRules.MaxSteps) return "a skill has 1 to " + WorldRules.MaxSteps + " steps";
        foreach (var st in s.steps)
        {
            if (st == null || Array.IndexOf(WorldRules.Acts, st.act) < 0) return "a step with an act the headset does not know";
            if (Longer(st.arg, HardArg)) return "a step says too much";
            if (!Within(st.seconds, 0f, HardStepSeconds)) return "a step lasts longer than a step may";
            if (st.act == "music" && st.arg.Length == 0 && s.song == null) return "a step plays the skill's own tune, and there is none";
            if (st.act == "ambience" && st.arg.Length == 0 && s.ambience == null) return "a step sets the skill's own surroundings, and there are none";
            if (st.act == "activity" && Array.IndexOf(WorldRules.ActivityNames, st.arg) < 0) return "unknown activity '" + st.arg + "'";
            if (st.act == "emote" && Array.IndexOf(WorldRules.Emotes, st.arg) < 0) return "unknown emote '" + st.arg + "'";
        }
        string song = s.song == null ? "" : SongProblem(s.song), amb = s.ambience == null ? "" : AmbienceProblem(s.ambience);
        return song.Length > 0 ? song : amb;
    }

    public static string GameProblem(GamePackDef g)
    {
        if (Array.IndexOf(Templates, g.template) < 0) return "unknown way of playing '" + g.template + "'";
        if (string.IsNullOrWhiteSpace(g.title)) return "the game has no title";
        if (Longer(g.title, GameTitleMax) || Longer(g.howTo, GameHowToMax) || Longer(g.enemyName, GameNameMax) || Longer(g.pickupName, GameNameMax)) return "a line of the game is too long";
        if (!Within(g.seconds, 20f, 120f)) return "a round lasts 20 to 120 seconds";
        if (!Within(g.speed, 0.5f, 2f) || !Within(g.spawn, 0.5f, 2f)) return "speed or spawn is out of range";
        if (g.lives < 1 || g.lives > 5) return "1 to 5 lives";
        foreach (string f in new[] { g.enemyIcon, g.pickupIcon, g.icon }) if (!string.IsNullOrEmpty(f) && !WorldRules.SafeFile(f)) return "an icon is not a plain file name";
        // a game that IS a script: the program is there and not longer than a module may carry; its pictures are few and plainly named
        // (whether the program RUNS is asked of the checker: ScriptRules.Problem for what comes from outside, the writer for what is made here)
        if (g.template == ScriptRules.Template && string.IsNullOrWhiteSpace(g.script)) return "the game is a script, and there is none in it";
        if (ScriptRules.Bytes(g.script) > ScriptRules.MaxScriptBytes) return "the game's script is too long";
        if (g.art != null)
        {
            if (g.art.Count > ScriptRules.MaxArt) return "a game has at most " + ScriptRules.MaxArt + " pictures of its own";
            foreach (var a in g.art)
            {
                if (a == null || string.IsNullOrEmpty(a.name) || ScriptRules.ArtName(a.name) != a.name) return "a picture of the game has a name a script cannot use";
                if (!string.IsNullOrEmpty(a.file) && !WorldRules.SafeFile(a.file)) return "a picture of the game is not a plain file name";
            }
            if (g.art.Select(a => a.name).Distinct().Count() != g.art.Count) return "two pictures of the game have one name";
        }
        string song = g.song == null ? "" : SongProblem(g.song), amb = g.ambience == null ? "" : AmbienceProblem(g.ambience);
        return song.Length > 0 ? song : amb;
    }

    // ------------------------------------------------------------------ what a language model wrote, made safe
    /// <summary>What the partner says when the program it wrote for a request never got to run: it does NOT hand over something broken, or something else than what was asked for.</summary>
    public static string CouldNotWriteLine(bool korean) => korean
        ? "이건 아무리 해도 제대로 안 돌아가네. 미안, 다르게 말해 줄래?"
        : "I couldn't get this one to work, however I tried. Sorry - could you say it another way?";

    /// <summary>
    /// A game the partner has just WRITTEN (ScriptWriter), as a pack: template "script", the program, the names of the
    /// pictures it draws (their files follow when they are painted). null when there is no title or no program.
    /// The numbers a script does not use (seconds, speed, spawn, lives) stay at what every pack has.
    /// </summary>
    public static GamePackDef ScriptGame(string id, string title, string howTo, string script, IEnumerable<string> art = null)
    {
        title = WorldRules.Short(title, 28); script = script ?? "";
        if (title.Length == 0 || string.IsNullOrWhiteSpace(script)) return null;
        var g = new GamePackDef { id = id ?? "", template = ScriptRules.Template, title = title, howTo = WorldRules.Short(howTo, 140), script = script };
        foreach (string name in art ?? Enumerable.Empty<string>())
        {
            string n = ScriptRules.ArtName(name);
            if (n.Length > 0 && g.art.Count < ScriptRules.MaxArt && !g.art.Any(x => x.name == n)) g.art.Add(new PackArt { name = n });
        }
        return g;
    }

    /// <summary>An ability written by a language model, checked. null when it has no name or no instructions worth the name.</summary>
    public static AbilityDef Ability(JToken j, string id)
    {
        if (j is not JObject o) return null;
        var examples = new List<string>();
        foreach (var e in o["examples"] as JArray ?? new JArray())
        {
            string ex = WorldRules.Text(e, 80);
            if (ex.Length > 0 && examples.Count < 5 && !examples.Contains(ex)) examples.Add(ex);
        }
        var a = new AbilityDef { id = id ?? "", name = WorldRules.Text(o["name"], 24), prompt = WorldRules.Text(o["prompt"], MaxAbilityPrompt), examples = examples.ToArray() };
        return a.name.Length == 0 || a.prompt.Length < 20 ? null : a;
    }

    static void UpgradeBoundaryGame(MarketModule m)
    {
        if (m.game?.script == null || (m.id != "game-cd155593" && m.id != "game-09743082")) return;
        var bytes = System.Text.Encoding.UTF8.GetBytes(m.game.script.Replace("\r\n", "\n"));
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        if (m.id == "game-cd155593" && hash is "85C366B69B7F6D23E4B0B9A3F35A37C7DE8B9B4D57222ED0ABC6D77F03C267D9" or "0D0ACDE3D5BC2C1A508CACF0A6D0540D9D5C8B13715A55F8D83F3E9A09FE8B91")
            m.game.script = GlassScriptExamples.Keeper;
        if (m.id == "game-09743082" && hash is "22CB63CC0E4EABC9A66D8496A085C036ADCD2301F0F7D2E67B0318538307151A" or "B83ED3F2EFB0DAD252A8D286E911F9CE2888353C9B57ECC4D22FB11B38B117DE")
            m.game.script = GlassScriptExamples.Shooter;
    }

    // ------------------------------------------------------------------ the shelf
    /// <summary>
    /// Brings the stored shelf up to date with what comes with the partner: new built-in modules are added, the old ones
    /// refreshed (their text and payload are code, not saved data - only a picture drawn for them and their install count
    /// are kept), built-in ones that no longer exist are taken off, and what the user made is left exactly as it is.
    /// What is "installed from the start" is installed once, the first time that module is seen: removing it lasts.
    /// </summary>
    public static void Merge(MarketState s, List<MarketSeedEntry> seeds)
    {
        s.catalogue ??= new(); s.installed ??= new(); s.seeded ??= new(); s.hub ??= ""; s.fetchLater ??= new();
        s.catalogue.RemoveAll(m => m == null || string.IsNullOrEmpty(m.id));
        // a state file from before the hub: everything on the shelf that is not built in was made by the user, and
        // what was installed then was paid for then - it is theirs for good
        s.own ??= s.catalogue.Where(m => !m.builtin).Select(m => m.id).ToList();
        s.owned ??= s.installed.Where(id => !s.own.Contains(id)).ToList();
        s.archivedGames ??= new();
        if (!s.threeGameLibrary)
        {
            // Keep original scripts, art references and ownership intact while removing old games from the active library.
            foreach (var m in s.catalogue.Where(m => m.kind == ModuleKinds.Game && !StarterGames.IsStarter(m.id)))
                if (!s.archivedGames.Any(a => a.module?.id == m.id))
                    s.archivedGames.Add(new ArchivedGame { module = Clone(m), installed = s.installed.Contains(m.id), owned = s.owned.Contains(m.id), mine = s.own.Contains(m.id) });
            s.catalogue.RemoveAll(m => m.kind == ModuleKinds.Game && !StarterGames.IsStarter(m.id));
            s.seeded.RemoveAll(id => StarterGames.IsStarter(id));
            s.threeGameLibrary = true;
        }
        var shelf = new List<MarketModule>();
        foreach (var seed in seeds)
        {
            var fresh = Clone(seed.module);
            var old = s.catalogue.FirstOrDefault(m => m.id == fresh.id);
            if (old != null) { fresh.icon = old.icon ?? ""; fresh.installs = old.installs; fresh.publishedUnixMs = old.publishedUnixMs; }
            // (the same for the pictures drawn for a game's creatures and pick-ups)
            if (old?.game != null && fresh.game != null) { fresh.game.enemyIcon = old.game.enemyIcon ?? ""; fresh.game.pickupIcon = old.game.pickupIcon ?? ""; fresh.game.icon = old.game.icon ?? ""; }
            // (and the sticker drawn for a skill that comes with the partner: dancing, music)
            if (old?.skill != null && fresh.skill != null) fresh.skill.icon = old.skill.icon ?? "";
            shelf.Add(fresh);
            if (!s.seeded.Contains(fresh.id))
            {
                s.seeded.Add(fresh.id);
                if (seed.preinstalled && !s.installed.Contains(fresh.id)) s.installed.Add(fresh.id);
            }
        }
        // the user's own and what they bought, in the order they came; anything that is no longer sound is dropped rather than shown
        foreach (var m in s.catalogue)
        {
            if (m.builtin || shelf.Any(x => x.id == m.id) || Array.IndexOf(MarketSeed.Retired, m.id) >= 0) continue;
            UpgradeBoundaryGame(m);
            Normalise(m);
            if (Problem(m).Length == 0) shelf.Add(m);
        }
        s.catalogue = shelf;
        s.gamesUnbundled = true;
        s.iconsChecked ??= new();
        s.installed = s.installed.Where(id => shelf.Any(m => m.id == id)).Distinct().ToList();
        s.own = s.own.Where(id => shelf.Any(m => m.id == id && !m.builtin)).Distinct().ToList();
        // (what the makers no longer sell is nobody's any more: the "Music" skill, "Dust Bunnies")
        s.owned = s.owned.Where(id => !string.IsNullOrEmpty(id) && Array.IndexOf(MarketSeed.Retired, id) < 0).Distinct().ToList();
        s.fetchLater.RemoveAll(id => Array.IndexOf(MarketSeed.Retired, id) >= 0 || s.archivedGames.Any(a => a.module?.id == id));
        s.seeded.RemoveAll(id => Array.IndexOf(MarketSeed.Retired, id) >= 0);
        s.stars = Math.Max(0, s.stars);
        // the user's own records: sound ones only, each once, no more than the player holds
        s.records ??= new();
        var records = new List<SongDef>();
        foreach (var r in s.records)
            if (r != null && SafeId(r.id) && SongProblem(r).Length == 0 && !records.Any(x => x.id == r.id)) records.Add(r);
        if (records.Count > WorldRules.MaxOwnRecords) records.RemoveRange(0, records.Count - WorldRules.MaxOwnRecords);
        s.records = records;
    }

    // ------------------------------------------------------------------ music is one thing: the record player and its records
    /// <summary>A module that is a RECORD: a skill module that does nothing but play a tune (WorldRules.TuneOnly). Installing it adds the record; it never joins the skills.</summary>
    public static bool IsRecord(MarketModule m) => m != null && m.kind == ModuleKinds.Skill && m.skill != null && WorldRules.TuneOnly(m.skill);

    /// <summary>The same for what a listing on the hub shows of a skill before it is bought (its steps).</summary>
    public static bool IsRecord(string kind, IEnumerable<HubStep> steps) =>
        kind == ModuleKinds.Skill && steps != null && WorldRules.TuneOnly(new SkillDef { steps = steps.Where(st => st != null).Select(st => new SkillStep { act = st.act ?? "", arg = st.arg ?? "" }).ToList() });

    /// <summary>How a record is called and what it gives, wherever a shop shows it ("Record: Lullaby").</summary>
    public const string RecordGives = "A new record for the record player.";
    public static string RecordName(string name) => WorldRules.Short("Record: " + (name ?? "").Trim(), HardName);

    /// <summary>
    /// The records of the record player, in the order the headset shows them: the three that come with the partner,
    /// the user's own (tunes they had made, what used to be tune-only skills), the tunes of the skills the partner
    /// knows, and what the installed modules carry - surroundings with a tune of their own, and skill modules that are
    /// records. Each tune once (by its id), WorldRules.MaxRecords at most: every snapshot carries them.
    /// </summary>
    public static List<SongDef> Records(WorldState w, MarketState s)
    {
        var list = new List<SongDef>();
        void Add(SongDef song) { if (song != null && !string.IsNullOrEmpty(song.id) && list.Count < WorldRules.MaxRecords && !list.Any(x => x.id == song.id)) list.Add(song); }
        foreach (var d in WorldDefaults.Songs()) Add(d);
        foreach (var r in s?.records ?? new List<SongDef>()) Add(r);
        foreach (var k in w?.skills ?? new List<SkillDef>()) if (k != null && k.status == "ready" && k.id != WorldRules.UseSkillId) Add(k.song);
        foreach (var m in s?.catalogue ?? new List<MarketModule>())
        {
            if (m == null || !s.installed.Contains(m.id)) continue;
            if (m.kind == ModuleKinds.Ambience) Add(m.song);
            if (IsRecord(m)) Add(WorldRules.RecordOf(m.skill, list));
        }
        return list;
    }

    /// <summary>The surroundings the user owns: the ones that are installed (what came with the partner, what was bought or got, what they had made), in the order of the shelf.</summary>
    public static List<AmbienceDef> Surroundings(MarketState s)
    {
        var list = new List<AmbienceDef>();
        foreach (var m in s?.catalogue ?? new List<MarketModule>())
            if (m != null && m.kind == ModuleKinds.Ambience && m.ambience != null && s.installed.Contains(m.id) && !list.Any(a => a.id == m.ambience.id)) list.Add(m.ambience);
        return list;
    }

    /// <summary>
    /// Brings world.ambiences and world.songs - what the user OWNS and can switch on at any time, whether or not it is
    /// on now - up to date with the books. Called after everything that changes them (the start, an install, a
    /// removal, a tune that was made). Returns true when either list changed.
    /// </summary>
    public static bool Owned(WorldState w, MarketState s)
    {
        if (w == null || s == null) return false;
        var ambiences = Surroundings(s); var songs = Records(w, s);
        bool changed = Proto.ToJson(ambiences) != Proto.ToJson(w.ambiences ?? new List<AmbienceDef>()) || Proto.ToJson(songs) != Proto.ToJson(w.songs ?? new List<SongDef>());
        w.ambiences = ambiences; w.songs = songs;
        return changed;
    }

    /// <summary>
    /// The surroundings that are ON right now follow what the user owns under the same id (world.ambience is a copy
    /// from the moment it was switched on: when the makers' own version of a place changes - it was a photograph for
    /// one evening and is the painted view again - what is on changes with it). Surroundings that are nobody's
    /// module - designed with words - are left alone. Call after Owned. Returns true when world.ambience changed.
    /// </summary>
    public static bool CurrentPlace(WorldState w)
    {
        if (w?.ambience == null || w.ambiences == null) return false;
        var owned = w.ambiences.FirstOrDefault(a => a != null && a.id == w.ambience.id);
        if (owned == null || Proto.ToJson(owned) == Proto.ToJson(w.ambience)) return false;
        w.ambience = owned;
        return true;
    }

    /// <summary>
    /// A tune becomes one of the user's own records (a copy is kept). false when it is one already, or cannot be one.
    /// With more than WorldRules.MaxOwnRecords the oldest of their own gives way.
    /// </summary>
    public static bool AddRecord(MarketState s, SongDef song)
    {
        if (s == null || song == null || !SafeId(song.id) || SongProblem(song).Length > 0) return false;
        s.records ??= new();
        if (s.records.Any(r => r.id == song.id) || WorldDefaults.Songs().Any(d => d.id == song.id)) return false;
        s.records.Add(Clone(song));
        if (s.records.Count > WorldRules.MaxOwnRecords) s.records.RemoveRange(0, s.records.Count - WorldRules.MaxOwnRecords);
        return true;
    }

    // ------------------------------------------------------------------ putting an older state in order
    /// <summary>What Tidy did, for the log - and the files in the world folder that belonged to what it took away (the caller deletes those nothing shows any more).</summary>
    public sealed class TidyReport
    {
        public List<string> notes = new();
        public List<string> files = new();
        public bool Changed => notes.Count > 0;
    }

    /// <summary>
    /// A module is forgotten for good: it is gone from the shelf, from what is installed, the user's own and what they
    /// own, from what is still to be fetched, and from the world (the bag, the skills, the games on the Play page, the
    /// abilities; surroundings or a tune of it that are on go off). Not "removed" - that only puts a module away.
    /// Returns true when anything was there to forget.
    /// </summary>
    public static bool Forget(WorldState w, MarketState s, string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        int n = 0;
        if (s != null)
        {
            var m = s.catalogue?.FirstOrDefault(x => x != null && x.id == id);
            if (m != null && w != null)
            {
                if (w.ambience != null && (w.ambience.id == m.ambience?.id || w.ambience.id == m.game?.ambience?.id || w.ambience.id == m.skill?.ambience?.id)) w.ambience = null;
                var song = SongOf(m);
                if (w.song != null && song != null && w.song.id == song.id) w.song = null;
            }
            n += s.catalogue?.RemoveAll(x => x == null || x.id == id) ?? 0;
            n += (s.installed?.RemoveAll(x => x == id) ?? 0) + (s.own?.RemoveAll(x => x == id) ?? 0) + (s.owned?.RemoveAll(x => x == id) ?? 0) + (s.fetchLater?.RemoveAll(x => x == id) ?? 0);
        }
        if (w != null)
        {
            n += (w.items?.RemoveAll(i => i != null && i.id == id) ?? 0) + (w.skills?.RemoveAll(k => k != null && k.id == id) ?? 0)
               + (w.games?.RemoveAll(g => g != null && g.id == id) ?? 0) + (w.abilities?.RemoveAll(a => a != null && a.id == id) ?? 0);
            if (w.doingSkill == id) { w.doingSkill = ""; w.doing = ""; }
        }
        return n > 0;
    }

    /// <summary>Two names are plainly one thing's: the same, or one inside the other ("선인장 화분", "작은 선인장 화분"), letters and digits only.</summary>
    public static bool Alike(string a, string b)
    {
        string x = Flat(a), y = Flat(b);
        return x.Length >= 2 && y.Length >= 2 && (x.Contains(y) || y.Contains(x));
    }

    /// <summary>
    /// Puts a saved state in order, at every start (after Merge, before Sync). Returns what it did.
    ///  - What the makers no longer sell (MarketSeed.Retired: the "Music" skill, "Dust Bunnies") is taken out of the world.
    ///  - Music is one thing: a skill that does nothing but play a tune (WorldRules.TuneOnly) becomes a RECORD under the
    ///    skill's name and is no skill any more. (Every start: such a skill cannot come into being any other way now.)
    ///  - The record player: a state from before gets the one that comes with the partner into its bag, once - unless
    ///    the user already has a thing for music.
    ///  - ONCE (duplicatesTidied): where two things of the same kind have names of which one contains the other, one is
    ///    kept - the one that is ready, has a picture and stands on the desk, in that order of weight - and the other
    ///    goes; the same for skills and for abilities of one name. What came from the shop is only uninstalled (it
    ///    stays the user's); what the user had made is gone with its files.
    /// </summary>
    public static TidyReport Tidy(WorldState w, MarketState s)
    {
        var report = new TidyReport();
        if (w == null || s == null) return report;
        w.items ??= new(); w.skills ??= new(); w.games ??= new(); w.abilities ??= new();
        s.catalogue ??= new(); s.installed ??= new(); s.records ??= new();
        bool Held(string id) => s.catalogue.Any(m => m != null && m.id == id);
        void File(string f) { if (!string.IsNullOrEmpty(f) && !f.StartsWith("market-", StringComparison.Ordinal) && !report.files.Contains(f)) report.files.Add(f); }

        // ---- what no longer exists
        foreach (string id in MarketSeed.Retired)
            if (Forget(w, s, id)) report.notes.Add($"{id} is no longer part of the partner: taken out");

        // ---- a skill that only plays a tune is a record
        foreach (var k in w.skills.Where(k => k != null && !k.builtin && k.status == "ready" && WorldRules.TuneOnly(k)).ToList())
        {
            var record = WorldRules.RecordOf(k, Records(w, s));
            if (record == null) continue;                       // (its tune cannot be found: it stays what it is)
            w.skills.Remove(k);
            if (w.doingSkill == k.id) { w.doingSkill = ""; w.doing = ""; }
            // (a copy of it this PC holds as a module stays, and is a record by itself for as long as it is installed)
            bool module = Held(k.id) && s.installed.Contains(k.id);
            bool added = !module && AddRecord(s, record);
            if (!Held(k.id)) File(k.icon);
            report.notes.Add($"the skill {k.id} '{k.name}' only played a tune: it is {(added ? $"the record '{record.name}' ({record.id}) now" : module ? "a record of its module now" : $"the record '{record.name}' ({record.id}), which was there already")}");
        }

        // ---- the record player
        if (!s.recordPlayerGiven)
        {
            s.recordPlayerGiven = true;
            bool has = w.items.Any(i => i != null && i.uses != null && Array.IndexOf(i.uses, ItemUses.Music) >= 0);
            if (!has && Held("item-record") && !s.installed.Contains("item-record"))
            {
                s.installed.Add("item-record");
                report.notes.Add("no thing for music in the bag: the record player that comes with the partner is put into it");
            }
        }

        // ---- the same thing twice
        if (!s.duplicatesTidied)
        {
            s.duplicatesTidied = true;
            var gone = new List<ItemDef>();
            int Weight(ItemDef i) => (i.status == "ready" ? 8 : 0) + (!string.IsNullOrEmpty(i.icon) ? 4 : 0) + (i.inRoom ? 2 : 0) + (!string.IsNullOrEmpty(i.model) || (i.parts != null && i.parts.Count > 0) ? 1 : 0);
            var kept = new List<ItemDef>();
            foreach (var i in w.items.Where(i => i != null).Select((i, at) => (i, at)).OrderByDescending(x => Weight(x.i)).ThenBy(x => x.at).Select(x => x.i))
            {
                var twin = kept.FirstOrDefault(k => k.kind == i.kind && Alike(k.name, i.name));
                if (twin == null) { kept.Add(i); continue; }
                gone.Add(i);
                report.notes.Add($"'{i.name}' ({i.id}) and '{twin.name}' ({twin.id}) are the same kind of thing ({i.kind}) under one name: '{twin.name}' is kept, '{i.name}' goes");
            }
            foreach (var i in gone)
            {
                w.items.Remove(i);
                if (Held(i.id)) s.installed.RemoveAll(x => x == i.id); else { File(i.icon); File(i.model); }
                foreach (var k in w.skills)
                {
                    if (k == null || k.builtin) continue;
                    if (k.itemId == i.id) k.itemId = "";
                    k.steps?.RemoveAll(st => st != null && st.act == "goto" && st.arg == i.id);
                }
            }
            var skills = new List<SkillDef>();
            foreach (var k in w.skills.Where(k => k != null && !k.builtin).Select((k, at) => (k, at)).OrderByDescending(x => (x.k.status == "ready" ? 2 : 0) + (!string.IsNullOrEmpty(x.k.icon) ? 1 : 0)).ThenBy(x => x.at).Select(x => x.k).ToList())
            {
                var twin = skills.FirstOrDefault(x => Flat(x.name).Length > 0 && Flat(x.name) == Flat(k.name));
                if (twin == null) { skills.Add(k); continue; }
                w.skills.Remove(k);
                if (w.doingSkill == k.id) { w.doingSkill = ""; w.doing = ""; }
                if (Held(k.id)) s.installed.RemoveAll(x => x == k.id); else File(k.icon);
                report.notes.Add($"two skills are called '{twin.name}': {twin.id} is kept, {k.id} goes");
            }
            var abilities = new List<AbilityDef>();
            foreach (var a in w.abilities.Where(a => a != null).ToList())
            {
                var twin = abilities.FirstOrDefault(x => Flat(x.name).Length > 0 && Flat(x.name) == Flat(a.name));
                if (twin == null) { abilities.Add(a); continue; }
                w.abilities.Remove(a);
                s.installed.RemoveAll(x => x == a.id);
                report.notes.Add($"two abilities are called '{twin.name}': {twin.id} is kept, {a.id} goes");
            }
        }
        return report;
    }

    /// <summary>Fills in what a module read from a file or from the network may have left out, so that the rest of the code never meets a null.</summary>
    public static void Normalise(MarketModule m)
    {
        m.name ??= ""; m.summary ??= ""; m.gives ??= ""; m.author ??= ""; m.icon ??= ""; m.version ??= "1.0"; m.tags ??= new string[0]; m.kind ??= "";
        if (m.ability != null) { m.ability.name ??= ""; m.ability.prompt ??= ""; m.ability.examples ??= new string[0]; }
        if (m.game != null) { var g = m.game; g.template ??= ""; g.title ??= ""; g.howTo ??= ""; g.enemyName ??= ""; g.pickupName ??= ""; g.enemyIcon ??= ""; g.pickupIcon ??= ""; g.icon ??= ""; g.rule ??= ""; g.script ??= ""; g.art ??= new(); g.art.RemoveAll(a => a == null); foreach (var a in g.art) { a.name ??= ""; a.file ??= ""; } }
        // (no surroundings carry a picture: a place that was a photograph for one evening - 2026-11-17 - is the painted view again)
        foreach (var a in new[] { m.ambience, m.skill?.ambience, m.game?.ambience })
            if (a != null) { a.image = ""; a.yaw = 0f; }
        // (nothing is smaller than WorldRules.SizeMin any more: a thing from before that was is made that large; and a module's thing stands on nothing)
        if (m.item != null) { if (float.IsFinite(m.item.sizeM) && m.item.sizeM < WorldRules.SizeMin) m.item.sizeM = WorldRules.SizeMin; m.item.y = 0f; m.item.onId = ""; }
        if (m.skill != null) { var k = m.skill; k.name ??= ""; k.asked ??= ""; k.note ??= ""; k.phrases ??= new string[0]; k.itemId ??= ""; k.icon ??= ""; k.steps ??= new(); k.steps.RemoveAll(st => st == null); foreach (var st in k.steps) { st.act ??= ""; st.arg ??= ""; } k.script ??= ""; }
        // (a module's thing is worn by nobody: it lies on a shelf)
        if (m.item != null) { var i = m.item; i.name ??= ""; i.asked ??= ""; i.kind ??= "box"; i.uses ??= new string[0]; i.color ??= ""; i.note ??= ""; i.model ??= ""; i.icon ??= ""; i.script ??= ""; i.wornOn = ""; }
    }

    public static MarketModule Find(MarketState s, string id) => s.catalogue.FirstOrDefault(m => m.id == id);
    public static bool Installed(MarketState s, string id) => s.installed.Contains(id);

    /// <summary>
    /// The module meant by an id, a name, or a sentence with its name or one of its tags in it ("의자 사줘", "the vampire
    /// game"). prefer: among several that fit, the ones this is true for come first (what is not installed yet when
    /// buying, what is installed when removing).
    /// </summary>
    public static MarketModule Find(MarketState s, string text, Func<MarketModule, bool> prefer) =>
        Match(s.catalogue, text, m => m.id, m => m.name, m => m.tags, prefer);

    /// <summary>
    /// The same search over any list of things that have an id, a name and other words they go by (the host searches
    /// the hub's listings and the user's own things with it, not only the modules it holds a copy of).
    /// </summary>
    public static T Match<T>(IEnumerable<T> list, string text, Func<T, string> id, Func<T, string> name, Func<T, IEnumerable<string>> tags, Func<T, bool> prefer = null) where T : class =>
        Matches(list, text, id, name, tags, prefer).FirstOrDefault();

    /// <summary>
    /// Everything that fits the words EQUALLY well, the preferred ones first: all that have this id, else all of this
    /// very name, else all with this tag, else all whose name (or tag) is the longest one found in the sentence, else
    /// all whose name contains the words. Match takes the first; whoever is about to spend the user's stars looks at
    /// the whole list - two listings of one name are two different things to buy, and a name is not unique in a shop.
    /// </summary>
    public static List<T> Matches<T>(IEnumerable<T> list, string text, Func<T, string> id, Func<T, string> name, Func<T, IEnumerable<string>> tags, Func<T, bool> prefer = null) where T : class
    {
        var none = new List<T>();
        string raw = (text ?? "").Trim(), flat = Flat(raw);
        if (raw.Length == 0) return none;
        var all = list.ToList();
        var order = prefer == null ? all : all.Where(prefer).Concat(all.Where(m => !prefer(m))).ToList();
        IEnumerable<string> Tags(T m) => tags(m) ?? Enumerable.Empty<string>();
        var hits = order.Where(m => id(m) == raw).ToList();
        if (hits.Count > 0) return hits;
        hits = order.Where(m => string.Equals((name(m) ?? "").Trim(), raw, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count > 0) return hits;
        if (flat.Length == 0) return none;
        hits = order.Where(m => Flat(name(m)) == flat).ToList();
        if (hits.Count > 0) return hits;
        hits = order.Where(m => Tags(m).Any(t => Flat(t) == flat)).ToList();
        if (hits.Count > 0) return hits;
        // a sentence with the name in it - the longest name wins ("record player" before "record")
        int Longest(T m)
        {
            int best = 0;
            string n = Flat(name(m));
            if (n.Length >= 2 && flat.Contains(n)) best = n.Length;
            foreach (string t in Tags(m)) { string f = Flat(t); if (f.Length >= 2 && f.Length > best && flat.Contains(f)) best = f.Length; }
            return best;
        }
        var found = order.Select(m => (m, len: Longest(m))).ToList();
        int bestLen = found.Count == 0 ? 0 : found.Max(x => x.len);
        if (bestLen > 0) return found.Where(x => x.len == bestLen).Select(x => x.m).ToList();
        // part of a name ("vampire" for "Vampire Night")
        if (flat.Length >= 3) return order.Where(m => Flat(name(m)).Contains(flat)).ToList();
        return none;
    }

    // ------------------------------------------------------------------ which pictures are still to be drawn
    static string Q(string s) => (s ?? "").Replace("\"", "'");

    /// <summary>What to draw for a thing, from what is known of it.</summary>
    public static string Words(ItemDef i)
    {
        string kind = ItemKinds.Find(i.kind) is { words.Length: > 0 } k ? k.words[0] : "";
        string said = !string.IsNullOrEmpty(i.asked) ? i.asked : i.note ?? "";
        return WorldRules.Short($"{i.name}{(kind.Length > 0 ? $" (a {kind})" : "")}{(!string.IsNullOrEmpty(i.color) ? $", main colour {i.color}" : "")}{(said.Length > 0 ? $"; described as: {Q(said)}" : "")}", 300);
    }
    /// <summary>What to draw for a skill: one object that stands for it.</summary>
    public static string Words(SkillDef k) => WorldRules.Short($"one simple object or symbol that stands for the skill \"{Q(k.name)}\"{(!string.IsNullOrEmpty(k.note) ? $" ({Q(k.note)})" : "")}", 300);
    /// <summary>What to draw for a creature or a pick-up of a game.</summary>
    public static string Words(string creature) => WorldRules.Short($"a cute {Q(creature)}, a single one, seen from the front", 200);
    /// <summary>What to draw for a game pack: its creature (or what its title names) made to read as "the game".</summary>
    public static string Words(GamePackDef g) =>
        g.template == ScriptRules.Template
            ? WorldRules.Short($"one object that stands for the game or little tool \"{Q(g.title)}\"{(!string.IsNullOrEmpty(g.howTo) ? $" ({Q(g.howTo)})" : "")}: its most recognisable piece (the board with a few stones, the dice, the bricks and the ball, the clock face), as a toy", 300)
            : WorldRules.Short($"a cute {Q(!string.IsNullOrEmpty(g.enemyName) ? g.enemyName : g.title)} holding a tiny cream game controller, one small honey-yellow star beside it: the mascot of the game \"{Q(g.title)}\"", 300);
    /// <summary>What to draw for a module that is none of these: an ability, surroundings.</summary>
    public static string Words(MarketModule m) => m.item != null ? Words(m.item) : m.skill != null ? Words(m.skill) : m.game != null ? Words(m.game)
        : m.ambience != null ? WorldRules.Short($"one simple object that stands for the mood \"{Q(m.name)}\" ({m.ambience.pattern}), in the colours {m.ambience.c1} and {m.ambience.c2}", 300)
        : WorldRules.Short($"one simple object that stands for the ability \"{Q(m.name)}\"{(!string.IsNullOrEmpty(m.summary) ? $" ({Q(m.summary)})" : "")}", 300);

    /// <summary>One picture that is still to be drawn.</summary>
    public sealed class PictureWish
    {
        public string id = "";              // the thing, skill or module it is for
        public string part = "icon";        // icon (its own sticker) | enemy | pickup (the creature and the pick-up of a game)
        public string file = "";            // the name it gets, without ".png": market-<id>... for what this PC holds as a module (the shelf keeps it), <id> for a thing or skill that lives in the world only
        public string what = "";            // what to draw
        public bool owned;                  // the user has it (it is in the bag, among the skills, or installed)
    }

    /// <summary>
    /// Every picture that is missing, in the order they are drawn - one image call at a time, so the order matters:
    /// first what the user HAS (the things in the bag, the skills - also dancing and music, which come with the
    /// partner - the games in the bag with their creatures and pick-ups, then whatever else is installed), then the
    /// rest of the shelf. A game pack wants a sticker of its own (GamePackDef.icon; the creature's picture is not
    /// it), a skill its SkillDef.icon, every other module its listing's icon. What has its picture wants none.
    /// seeds: what the makers wrote to be drawn for their own modules. underWay: an icon the Maker is drawing just now.
    /// </summary>
    public static List<PictureWish> PicturesWanted(MarketState s, WorldState w, IReadOnlyDictionary<string, MarketSeedEntry> seeds = null, Func<string, bool> underWay = null)
    {
        var list = new List<PictureWish>();
        var seen = new HashSet<string>();
        var held = new Dictionary<string, MarketModule>();
        foreach (var m in s.catalogue) if (m != null) held[m.id] = m;
        MarketSeedEntry SeedOf(string id) => seeds != null && seeds.TryGetValue(id, out var e) ? e : null;
        void Add(string id, string part, string file, string what, bool owned)
        {
            if (string.IsNullOrWhiteSpace(what) || (underWay != null && underWay(id)) || !seen.Add(id + "/" + part)) return;
            list.Add(new PictureWish { id = id, part = part, file = file, what = what, owned = owned });
        }
        // a module this PC holds: its own sticker, and for a game its creature and pick-up
        void Module(MarketModule m, bool owned)
        {
            var seed = m.builtin ? SeedOf(m.id) : null;
            bool has = m.game != null ? !string.IsNullOrEmpty(m.game.icon) : m.skill != null ? !string.IsNullOrEmpty(m.skill.icon) || !string.IsNullOrEmpty(m.icon) : !string.IsNullOrEmpty(m.icon);
            if (!has) Add(m.id, "icon", "market-" + m.id, seed is { draw.Length: > 0 } ? seed.draw : Words(m), owned);
            if (m.game == null) return;
            // (the creatures and pick-ups of the ways of playing that show them)
            bool shows = m.builtin ? seed != null : m.game.template is "survival" or "pop";       // (a scripted game has none: its own pictures are GamePackDef.art, painted when it is made)
            if (!shows) return;
            if (string.IsNullOrEmpty(m.game.enemyIcon) && (seed is { drawEnemy.Length: > 0 } || (!m.builtin && !string.IsNullOrEmpty(m.game.enemyName))))
                Add(m.id, "enemy", $"market-{m.id}-enemy", seed is { drawEnemy.Length: > 0 } ? seed.drawEnemy : Words(m.game.enemyName), owned);
            if (string.IsNullOrEmpty(m.game.pickupIcon) && (seed is { drawPickup.Length: > 0 } || (!m.builtin && !string.IsNullOrEmpty(m.game.pickupName))))
                Add(m.id, "pickup", $"market-{m.id}-pickup", seed is { drawPickup.Length: > 0 } ? seed.drawPickup : Words(m.game.pickupName), owned);
        }

        // 1. what the user has: the bag, the skills, the games - then everything else that is installed
        foreach (var i in w.items ?? new List<ItemDef>())
        {
            if (i == null || i.status != "ready" || !string.IsNullOrEmpty(i.icon)) continue;
            // (a thing this PC holds as a module shows the module's picture: when that is there already it is only copied over, by Market.cs)
            if (held.TryGetValue(i.id, out var m)) { if (string.IsNullOrEmpty(m.icon)) Module(m, true); }
            else Add(i.id, "icon", i.id, Words(i), true);
        }
        foreach (var k in w.skills ?? new List<SkillDef>())
        {
            if (k == null || k.status != "ready" || !string.IsNullOrEmpty(k.icon)) continue;
            if (held.TryGetValue(k.id, out var m)) Module(m, true);
            else if (!k.builtin) Add(k.id, "icon", k.id, Words(k), true);
        }
        foreach (var g in w.games ?? new List<GamePackDef>())
            if (g != null && held.TryGetValue(g.id, out var m)) Module(m, true);
        foreach (var m in s.catalogue)
            if (m != null && (s.installed.Contains(m.id) || (s.own != null && s.own.Contains(m.id)))) Module(m, true);
        // 2. the rest of the shelf
        foreach (var m in s.catalogue) if (m != null) Module(m, false);
        return list;
    }

    // ------------------------------------------------------------------ is that game in the bag?
    /// <summary>
    /// The module a game belongs to, by the name the conversation, the headset and lpctl start it with: "tennis" and
    /// its two variations ("keeper", "stars") are the Tennis module, "rps" / "pop" / "redlight" their own, and the id
    /// of a game pack is itself. "" when the name is none of these (then nothing is known to be missing).
    /// </summary>
    public static string GameModule(string game)
    {
        string g = (game ?? "").Trim().ToLowerInvariant();
        return g switch
        {
            "tennis" => StarterGames.TennisId, "keeper" => StarterGames.KeeperId, "swarm" => SwarmRules.PackId, "gomoku" => StarterGames.GomokuId,
            _ => g.StartsWith("game-", StringComparison.Ordinal) && SafeId(g) ? (game ?? "").Trim() : "",
        };
    }

    /// <summary>Is the game pack in the bag (world.games)? That list is the only truth: a game that is not in it is not played, whoever asks.</summary>
    public static bool HasGame(WorldState w, string moduleId) => !string.IsNullOrEmpty(moduleId) && w?.games != null && w.games.Any(g => g != null && g.id == moduleId);

    /// <summary>May this game be started? Yes when its module is in the bag - and when the name is of no module at all (nothing is known to be missing).</summary>
    public static bool CanPlay(WorldState w, string game) => GameModule(game) is var id && (id.Length == 0 || HasGame(w, id));

    // ------------------------------------------------------------------ is there room for it?
    /// <summary>
    /// Why what a module gives cannot join the world just now: "" (there is room, or it is there already), "items"
    /// (the bag is full) or "skills" (as many skills as can be taught are known). The same limits the Maker keeps to
    /// when the user has something made: a thing bought, got or imported counts like one that was made.
    /// </summary>
    public static string BagProblem(WorldState w, MarketModule m)
    {
        if (w == null || m == null) return "";
        if (m.kind == ModuleKinds.Item && m.item != null && w.items != null && !w.items.Any(i => i.id == m.item.id) && w.items.Count >= WorldRules.MaxItems) return "items";
        if (m.kind == ModuleKinds.Skill && m.skill != null && !m.skill.builtin && !IsRecord(m) && w.skills != null && !w.skills.Any(k => k.id == m.skill.id) && w.skills.Count(k => k != null && !k.builtin) >= WorldRules.MaxSkills) return "skills";
        return "";
    }

    // ------------------------------------------------------------------ what is installed on this PC
    /// <summary>Marks a module this PC holds as installed. (Whether the user may have it - bought, free, their own - was settled before, with the hub.)</summary>
    public static Deal Install(MarketState s, string id, out MarketModule m)
    {
        m = Find(s, id);
        if (m == null) return Deal.Unknown;
        if (s.installed.Contains(m.id)) return Deal.Already;
        s.installed.Add(m.id);
        return Deal.Done;
    }

    /// <summary>Takes a module off the installed list. Nothing is paid back: what was bought stays the user's and can be installed again.</summary>
    public static Deal Remove(MarketState s, string id, out MarketModule m)
    {
        m = Find(s, id);
        if (m == null) return Deal.Unknown;
        return s.installed.Remove(m.id) ? Deal.Done : Deal.NotInstalled;
    }

    // ------------------------------------------------------------------ the world
    /// <summary>
    /// Puts what a module gives into the world: a thing into the bag (one copy, under the module's id), a skill among
    /// the skills, a game among the games, an ability among the abilities. Surroundings are not kept in the world: they
    /// become known through the list of installed ones. Returns false when it was there already (nothing is ever doubled).
    /// name / note: what the thing is called and what the partner says about it, in the user's language ("" = the module's own).
    /// </summary>
    public static bool Give(WorldState w, MarketModule m, long nowUnixMs = 0, string name = "", string note = "")
    {
        w.items ??= new(); w.skills ??= new(); w.games ??= new(); w.abilities ??= new();
        switch (m.kind)
        {
            case ModuleKinds.Item:
            {
                if (m.item == null || w.items.Any(i => i.id == m.item.id)) return false;
                var item = Clone(m.item);
                item.inRoom = false; item.status = "ready"; item.progress = 1; item.madeUnixMs = nowUnixMs; item.y = 0f; item.onId = ""; item.wornOn = "";
                if (item.icon.Length == 0) item.icon = m.icon ?? "";
                if (name.Length > 0) item.name = name;
                if (note.Length > 0) item.note = note;
                w.items.Add(item);
                return true;
            }
            case ModuleKinds.Skill:
            {
                if (m.skill == null || w.skills.Any(k => k.id == m.skill.id)) return false;
                // a skill module that only plays a tune is a RECORD: it never joins the skills (Records lists it while it is installed)
                if (IsRecord(m)) return false;
                var skill = Clone(m.skill);
                skill.status = "ready";
                if (skill.icon.Length == 0) skill.icon = m.icon ?? "";
                // what came with the partner stands in front, as it always has
                int at = skill.builtin ? w.skills.TakeWhile(k => k.builtin).Count() : w.skills.Count;
                w.skills.Insert(at, skill);
                return true;
            }
            case ModuleKinds.Game:
                if (m.game == null || w.games.Any(g => g.id == m.game.id)) return false;
                w.games.Add(Clone(m.game));
                return true;
            case ModuleKinds.Ability:
                if (m.ability == null || w.abilities.Any(a => a.id == m.ability.id)) return false;
                w.abilities.Add(Clone(m.ability));
                return true;
            default: return false;
        }
    }

    /// <summary>The reverse of Give, on the world's lists alone. Returns false when there was nothing to take.</summary>
    public static bool Take(WorldState w, MarketModule m)
    {
        w.items ??= new(); w.skills ??= new(); w.games ??= new(); w.abilities ??= new();
        switch (m.kind)
        {
            case ModuleKinds.Item: return w.items.RemoveAll(i => i.id == m.id) > 0;
            case ModuleKinds.Skill:
                if (w.doingSkill == m.id) { w.doingSkill = ""; w.doing = ""; }
                return w.skills.RemoveAll(k => k.id == m.id) > 0;
            case ModuleKinds.Game: return w.games.RemoveAll(g => g.id == m.id) > 0;
            case ModuleKinds.Ability: return w.abilities.RemoveAll(a => a.id == m.id) > 0;
            case ModuleKinds.Ambience:
                if (w.ambience != null && w.ambience.id == m.id) { w.ambience = null; return true; }
                return false;
            default: return false;
        }
    }

    /// <summary>
    /// At start-up: everything that is installed is in the world, and nothing that is not installed lingers there -
    /// a built-in skill that was removed stays removed, the games and abilities are exactly the installed ones.
    /// Things and skills the user made directly are never touched. Returns true when the world changed.
    /// </summary>
    public static bool Sync(WorldState w, MarketState s)
    {
        w.items ??= new(); w.skills ??= new(); w.games ??= new(); w.abilities ??= new();
        bool changed = false;
        foreach (var m in s.catalogue)
        {
            bool on = s.installed.Contains(m.id);
            if (on) changed |= Give(w, m);
            else if (m.builtin && (m.kind == ModuleKinds.Skill || m.kind == ModuleKinds.Item)) changed |= Take(w, m);
        }
        changed |= w.games.RemoveAll(g => g == null || !s.catalogue.Any(m => m.kind == ModuleKinds.Game && m.id == g.id && s.installed.Contains(m.id))) > 0;
        changed |= w.abilities.RemoveAll(a => a == null || !s.catalogue.Any(m => m.kind == ModuleKinds.Ability && m.id == a.id && s.installed.Contains(m.id))) > 0;
        // a game's or an ability's payload is the module's: what the world holds is brought up to date with it (a new version of a built-in game)
        foreach (var m in s.catalogue.Where(m => s.installed.Contains(m.id)))
        {
            if (m.kind == ModuleKinds.Game) { int i = w.games.FindIndex(g => g.id == m.id); if (i >= 0 && Proto.ToJson(w.games[i]) != Proto.ToJson(m.game)) { w.games[i] = Clone(m.game); changed = true; } }
            if (m.kind == ModuleKinds.Ability) { int i = w.abilities.FindIndex(a => a.id == m.id); if (i >= 0 && Proto.ToJson(w.abilities[i]) != Proto.ToJson(m.ability)) { w.abilities[i] = Clone(m.ability); changed = true; } }
        }
        return changed;
    }

    // ------------------------------------------------------------------ publishing what the user made
    /// <summary>One of the user's own things as a module of theirs: listed under the thing's id, with a copy of the thing as its payload.</summary>
    public static MarketModule Wrap(ItemDef item, int price, long nowUnixMs)
    {
        var copy = Clone(item);
        copy.status = "ready"; copy.progress = 1; copy.inRoom = false; copy.x = 0; copy.z = 0; copy.yaw = 0; copy.y = 0; copy.onId = ""; copy.wornOn = "";
        return new MarketModule
        {
            id = item.id, kind = ModuleKinds.Item, name = item.name, summary = WorldRules.Short(item.note.Length > 0 ? item.note : item.asked, 160), gives = GivesFor(item),
            author = You, icon = item.icon ?? "", price = Math.Clamp(price, 0, MaxPrice), tags = new[] { item.kind }, builtin = false, publishedUnixMs = nowUnixMs, item = copy,
        };
    }

    /// <summary>A skill the user taught, as a module of theirs.</summary>
    public static MarketModule Wrap(SkillDef skill, int price, long nowUnixMs)
    {
        var copy = Clone(skill);
        copy.status = "ready"; copy.builtin = false;
        return new MarketModule
        {
            id = skill.id, kind = ModuleKinds.Skill, name = skill.name, summary = WorldRules.Short(skill.note.Length > 0 ? skill.note : skill.asked, 160), gives = GivesFor(skill),
            author = You, icon = skill.icon ?? "", price = Math.Clamp(price, 0, MaxPrice), tags = skill.phrases.Take(4).ToArray(), builtin = false, publishedUnixMs = nowUnixMs, skill = copy,
        };
    }

    static string Join(List<string> parts) => parts.Count <= 1 ? string.Concat(parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

    /// <summary>What the partner gains from a thing, said from what the thing is for.</summary>
    public static string GivesFor(ItemDef item)
    {
        var can = new List<string>();
        foreach (string use in item.uses ?? new string[0])
        {
            string what = use switch
            {
                ItemUses.Sit => "sit on it", ItemUses.Table => "work at it", ItemUses.Sleep => "nap on it", ItemUses.Read => "read it", ItemUses.Play => "play with it",
                ItemUses.Music => "put a tune on with it", ItemUses.Light => "light the desk with it", ItemUses.Eat => "enjoy it as a treat", _ => "",
            };
            if (what.Length > 0 && !can.Contains(what)) can.Add(what);
        }
        return can.Count > 0 ? "Nilo can " + Join(can) + "." : "Something of your own making for Nilo's desk.";
    }

    /// <summary>What the partner gains from a skill, said from the acts it is made of.</summary>
    public static string GivesFor(SkillDef skill)
    {
        var does = new List<string>();
        foreach (var st in skill.steps ?? new List<SkillStep>())
        {
            string what = st.act switch
            {
                "music" => st.arg == "stop" ? "turns the tune off" : "puts a tune on", "dance" => "dances", "ambience" => st.arg == "clear" ? "puts the room back" : "changes the room round the desk",
                "activity" => st.arg switch { "study" => "studies", "sit" => "sits down", "nap" => "takes a nap", "read" => "reads", _ => "plays" },
                "rain" => st.arg == "off" ? "stops the rain" : "lets it rain on the desk", "say" => "says a line", "goto" => "walks over", "emote" => "", _ => "",
            };
            if (what.Length > 0 && !does.Contains(what)) does.Add(what);
        }
        return does.Count > 0 ? "When asked, Nilo " + Join(does) + "." : "Something you taught Nilo to do.";
    }

    // ------------------------------------------------------------------ what the clients see
    /// <summary>A module this PC holds, as one line for the clients. (Who sells it, whether it is on sale and whether the user owns it are the hub's to say: Market.cs fills those in.)</summary>
    public static MarketListing Listing(MarketModule m, bool installed) => new MarketListing
    {
        // (a skill module that only plays a tune is shown as what it is: a record for the record player)
        id = m.id, kind = m.kind, name = IsRecord(m) ? RecordName(m.name) : m.name, summary = m.summary, gives = IsRecord(m) ? RecordGives : m.gives, author = m.author, seller = m.author, icon = m.icon ?? "", price = m.price,
        installed = installed, mine = !m.builtin, builtin = m.builtin, template = m.game?.template ?? "",
    };

    /// <summary>The catalogue as a list for the clients: what is installed first, then by kind (in the order of ModuleKinds.All), otherwise as it stands on the shelf.</summary>
    public static List<MarketListing> List(MarketState s)
    {
        return s.catalogue
            .Select((m, i) => (m, i, on: s.installed.Contains(m.id)))
            .OrderBy(x => x.on ? 0 : 1).ThenBy(x => Math.Max(0, Array.IndexOf(ModuleKinds.All, x.m.kind))).ThenBy(x => x.i)
            .Select(x => Listing(x.m, x.on)).ToList();
    }

    /// <summary>The tune a module carries: its own, its skill's, or its game's (null when it has none).</summary>
    public static SongDef SongOf(MarketModule m) => m == null ? null : m.song ?? m.skill?.song ?? m.game?.song;

    // ------------------------------------------------------------------ a tune as a sound file
    /// <summary>The tune as a 16-bit mono WAV file: `loops` passes back to back (one pass is made to loop, so there is no seam).</summary>
    public static byte[] Wav(SongDef song, int loops, out float seconds)
    {
        float[] pass = SongSynth.Render(song, out float one);
        loops = Math.Clamp(loops, 1, 8);
        seconds = one * loops;
        int n = pass.Length * loops, bytes = n * 2;
        var file = new byte[44 + bytes];
        using (var w = new BinaryWriter(new MemoryStream(file)))
        {
            w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVEfmt "u8.ToArray());
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SongSynth.SampleRate); w.Write(SongSynth.SampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(bytes);
            for (int k = 0; k < loops; k++)
                foreach (float v in pass) w.Write((short)Math.Round(Math.Clamp(v, -1f, 1f) * 32767f));
        }
        return file;
    }

    /// <summary>A name that can be a file's: letters and digits of any script kept, everything else a dash.</summary>
    public static string FileName(string name, string fallback)
    {
        string slug = Regex.Replace(name ?? "", @"[^\p{L}\p{Nd}]+", "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        return slug.Length > 0 ? slug : fallback;
    }
}

/// <summary>How a thing came to be the user's. Only what the partner MADE is offered for sale afterwards.</summary>
public enum Came { Made, Bought, Installed, Imported }

/// <summary>
/// "I made it! Shall I put it up in the marketplace?" - the partner's offer after it has finished making something,
/// as plain rules (Market.cs asks the question, publishes and says the lines; the clients see it as market.offerId /
/// offerPrice / offerSeq). One offer at a time: a newer one replaces the one before it. It ends when it is answered,
/// when three minutes have passed (the thing simply stays the user's) or when the thing is gone or on sale already.
/// </summary>
public sealed class MakeOffer
{
    /// <summary>How long the question stands unanswered.</summary>
    public const double Seconds = 180;
    /// <summary>How long after the question an answer needs no name ("응" is enough): see Conversation.OnText.</summary>
    public const double ListeningSeconds = 25;
    public const int MaxSuggested = 995;

    public string id = "";              // what is on offer ("" = nothing)
    public string name = "";
    public int price;                   // the price the partner suggests
    public int seq;                     // goes up with every new offer
    public double askedAt = -1000, until;

    public bool Open => id.Length > 0;

    /// <summary>The price the partner suggests for something it made, by its kind.</summary>
    public static int Suggest(string kind) => Math.Clamp(kind switch
    {
        ModuleKinds.Item => 10, ModuleKinds.Skill => 15, ModuleKinds.Ambience => 10, ModuleKinds.Ability => 15, ModuleKinds.Game => 25, _ => 10,
    }, 0, MaxSuggested);

    /// <summary>
    /// Something has just become the user's. When the partner made it, it is offered: true comes back, and the offer
    /// before it (if one was still open) is over. What was bought, installed or imported is never offered - it is
    /// somebody else's work, or was on sale a moment ago.
    /// </summary>
    public bool Begin(string id, string name, string kind, Came how, double now)
    {
        if (how != Came.Made || string.IsNullOrEmpty(id)) return false;
        this.id = id; this.name = name ?? ""; price = Suggest(kind); seq++;
        askedAt = now; until = now + Seconds;
        return true;
    }

    /// <summary>Is an answer without the partner's name still to be taken as an answer (the question was asked a moment ago)?</summary>
    public bool Listening(double now) => Open && now - askedAt <= ListeningSeconds;

    /// <summary>Three minutes without an answer: the offer ends by itself (true, once). Nothing is published.</summary>
    public bool Expire(double now)
    {
        if (!Open || now < until) return false;
        Close();
        return true;
    }

    /// <summary>The thing is gone, or is on sale by another way: there is nothing to ask about any more (true when that ended the offer).</summary>
    public bool Drop(string gone)
    {
        if (!Open || gone != id) return false;
        Close();
        return true;
    }

    /// <summary>
    /// The user's answer. forId: the offer it answers ("" = whichever is open); an answer to an offer that is over
    /// answers nothing. yes: publish = true and the price to publish at - the one they named, the suggested one when
    /// they named none (0), and 0 only when they said "free". Either way the offer is over.
    /// </summary>
    public (bool answered, bool publish, string id, int price) Answer(string forId, string text, int named)
    {
        if (!Open || (!string.IsNullOrEmpty(forId) && forId != id)) return (false, false, "", 0);
        string t = (text ?? "").Trim().ToLowerInvariant();
        bool yes = t.StartsWith("yes") || t == "y" || t == "true" || t == "publish", free = t.Contains("free");
        string was = id;
        int at = free ? 0 : named > 0 ? Math.Clamp(named, 0, MarketRules.MaxPrice) : price;
        Close();
        return (true, yes, was, yes ? at : 0);
    }

    void Close() { id = ""; name = ""; price = 0; }

    // ------------------------------------------------------------------ the answer in plain words
    // Matched against the whole of what was said, with spaces and punctuation taken out: an answer is nothing but an
    // answer. ("아니, 그거 말고 의자 꺼내 줘" begins like a no and is not one: it goes to the language model.)
    const string KoPrice = @"(?<price>(?<n>\d{1,3})(별|개|스타)(에|로|으로)?|별(?<n2>\d{1,3})개(에|로)?|(?<free>무료|공짜)(로)?)";
    // ("어" is left out on purpose: it is also what people say while they think, and that must not put anything up for sale)
    static readonly Regex YesKo = new(@"^(?<word>(응|웅|엉|네|넵|예|그래|그럼|좋아|좋지|오케이|ㅇㅇ|당연하지|물론|물론이지|콜){1,2})?(이거|그거|그럼|바로|한번)?((마켓|마켓플레이스|상점)에)?" + KoPrice + @"?(?<act>올려|올리자|공개해|공개하자|팔아|팔자)?(줘|봐|줄래|보자)?$");
    static readonly Regex YesEn = new(@"^(?<word>yes|yeah|yep|yup|sure|ok|okay)?(please)?(?<act>doit|goahead|putitup|publish(it)?|sellit)?(for)?(?<price>(?<n>\d{1,3})stars?|(?<free>free))?(please)?$");
    static readonly Regex NoKo = new(@"^(?<word>(아니|아니야|아니요|아냐|아뇨|됐어|됐어요|괜찮아|괜찮아요|싫어|안돼|안해|하지마|나중에|다음에){1,2})?(이거|그거|그건)?(그냥)?(?<act>둬|둘래|둘게|놔둬|갖고있을게|올리지마|공개하지마|팔지마|안올려|안올릴래|안팔아|안팔래)?(줘)?$");
    static readonly Regex NoEn = new(@"^(?<word>(no|nope|nah|notnow|dont|nothanks|nevermind){1,2})?(thanks)?(?<act>keepit|leaveit|dontpublish(it)?|dontsellit)?$");

    /// <summary>
    /// A short answer to the question, understood without a language model: "응", "그래 올려줘", "공개해", "20별에 올려줘",
    /// "무료로 올려" -> yes (with the stars named, or free); "아니", "됐어", "그냥 둬", "올리지 마" -> no. null for
    /// anything that is not plainly one of the two and nothing else: that goes to the model, or is not for the partner.
    /// </summary>
    public static (bool yes, int price, bool free)? Understand(string said)
    {
        string flat = new string((said ?? "").ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());
        if (flat.Length == 0 || flat.Length > 24) return null;
        foreach (var rx in new[] { NoKo, NoEn })
        {
            var m = rx.Match(flat);
            if (m.Success && (m.Groups["word"].Success || m.Groups["act"].Success)) return (false, 0, false);
        }
        foreach (var rx in new[] { YesKo, YesEn })
        {
            var m = rx.Match(flat);
            if (!m.Success || !(m.Groups["word"].Success || m.Groups["act"].Success || m.Groups["price"].Success)) continue;
            string n = m.Groups["n"].Success ? m.Groups["n"].Value : m.Groups["n2"].Success ? m.Groups["n2"].Value : "";
            return (true, n.Length > 0 ? int.Parse(n) : 0, m.Groups["free"].Success);
        }
        return null;
    }
}


/// <summary>
/// "테니스는 아직 없어. 마켓플레이스에 무료로 있으니까 받아올까?" - the partner's question when it is asked to play a game
/// that is not in the bag. Like the offer (MakeOffer) it is one question at a time, and for 25 seconds an answer
/// needs no name: "응" / "받아줘" gets the game (it is free), "아니" leaves it. Nothing happens by itself.
/// </summary>
public sealed class GameWish
{
    public string id = "", name = "";       // the module that was asked about ("" = nothing is asked)
    public double askedAt = -1000;

    public bool Open => id.Length > 0;
    public void Ask(string moduleId, string moduleName, double now) { id = moduleId ?? ""; name = moduleName ?? ""; askedAt = now; }
    /// <summary>The question was asked a moment ago: an answer needs no name.</summary>
    public bool Listening(double now) => Open && now - askedAt <= MakeOffer.ListeningSeconds;
    /// <summary>Still worth telling the language model about (two minutes).</summary>
    public bool Fresh(double now) => Open && now - askedAt <= 120;
    /// <summary>The question is answered (or over): what was asked about comes back, and nothing is asked any more.</summary>
    public string Take() { string was = id; id = ""; name = ""; return was; }

    static readonly System.Text.RegularExpressions.Regex YesKo = new(@"^(?<word>(응|웅|엉|네|넵|예|그래|그럼|좋아|좋지|오케이|ㅇㅇ|당연하지|물론|물론이지|콜){1,2})?(이거|그거|그럼|바로|한번|지금)?(?<act>받아|받아와|받아줘|받아올래|받자|깔아|설치해|가져와|다운받아|다운로드해)?(줘|와|봐|줄래|오자|와줘)?$");
    static readonly System.Text.RegularExpressions.Regex YesEn = new(@"^(?<word>yes|yeah|yep|yup|sure|ok|okay)?(please)?(?<act>getit|installit|goahead|doit|downloadit)?(please)?$");
    static readonly System.Text.RegularExpressions.Regex NoKo = new(@"^(?<word>(아니|아니야|아니요|아냐|아뇨|됐어|됐어요|괜찮아|괜찮아요|싫어|안돼|안해|하지마|나중에|다음에){1,2})?(이거|그거|그건)?(그냥)?(?<act>둬|놔둬|받지마|안받아|안받을래|필요없어)?$");
    static readonly System.Text.RegularExpressions.Regex NoEn = new(@"^(?<word>(no|nope|nah|notnow|dont|nothanks|nevermind){1,2})?(thanks)?(?<act>leaveit|dontgetit)?$");

    /// <summary>A short answer to the question in plain words: true = get it, false = leave it, null = not plainly an answer (and nothing else).</summary>
    public static bool? Understand(string said)
    {
        string flat = new string((said ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (flat.Length == 0 || flat.Length > 24) return null;
        foreach (var rx in new[] { NoKo, NoEn }) { var m = rx.Match(flat); if (m.Success && (m.Groups["word"].Success || m.Groups["act"].Success)) return false; }
        foreach (var rx in new[] { YesKo, YesEn }) { var m = rx.Match(flat); if (m.Success && (m.Groups["word"].Success || m.Groups["act"].Success)) return true; }
        return null;
    }
}
