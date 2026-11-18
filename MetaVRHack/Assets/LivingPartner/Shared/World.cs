using System;
using System.Collections.Generic;

namespace LivingPartner.Shared
{
    // What the user gives the partner, and what the partner learns to do with it - as DATA. Nothing here is code that
    // has to be written in advance for each new thing: the PC (Maker) has a language model fill these records in when
    // the user says "here is a record player" or "from now on, when I ask for music, play it on that", and the headset
    // only knows how to carry out the small set of acts a skill is made of.
    //   Item      a thing on the desk or in the bag (a generated 3D model, or one of the built-in toy shapes)
    //   Skill     a named thing the partner can do: a short list of acts (music, dance, ambience, activity, say ...)
    //   Song      a tune written as notes; the headset turns it into sound (SongSynth)
    //   Ambience  what the surroundings look like while the work area stays as it is (club lights, rain everywhere ...)

    /// <summary>A thing the partner owns. Lives in the bag until it is put on the desk.</summary>
    public sealed class ItemDef
    {
        public string id = "";
        public string name = "";            // short label, in the user's words ("Desk", "레코드판")
        public string asked = "";           // what the user said when giving it
        public string kind = "box";         // the nearest built-in shape, see ItemKinds.All
        public string[] uses = new string[0];   // what it is for, see ItemUses
        public float sizeM = 0.10f;         // its longest side in the room, at the partner's scale (the partner is about 0.10 m tall)
        public string color = "";           // "#RRGGBB" for the built-in shape ("" = that shape's own colour)
        public string note = "";            // one line: what the partner worked out it can do with it
        public string status = "making";    // making | ready | failed
        public float progress;              // 0..1 while a 3D model is being generated
        public string model = "";           // file name of the generated GLB under /world/ ("" = draw the built-in shape)
        public string icon = "";            // file name of its icon PNG under /world/ ("" = none)
        public bool inRoom;                 // on the desk (true) or in the bag (false)
        public float x, z, yaw;             // on the desk, relative to the partner's home spot: metres to the user's right, metres away from the user, degrees
        public float y;                     // metres above the desk: 0 = it stands on the desk; more = it stands on another thing (on a table top)
        // What the thing DOES, as a small Lua program written by the partner when the thing was made (RoomScript): what
        // happens when it is used or put on - the partner picks it up, a new motion of its own ("a baseball swing"), things
        // flying, sounds. "" = it only has the built-in uses above.
        public string script = "";
        public string wornOn = "";          // "" = not worn; handR | handL | head | back: the partner is wearing / holding it there (world command wear / unwear)
        public string onId = "";            // the thing it stands on ("" = the desk). When that thing is moved it goes along; when that thing is put away it comes down to the desk.
        public long madeUnixMs;
        // Its own shape, sculpted by the language model out of simple parts (Maker, "sculpt"): shown when there is no
        // generated model. Empty = the built-in shape of its kind. At most ShapePart.Max parts, already made to the
        // common measure by the host (WorldRules.ValidateParts): longest side 1, standing on y = 0, middle over the origin.
        public List<ShapePart> parts = new List<ShapePart>();
        public float seat, top;             // how high its seat / its table top is, in that same measure (0 = not given)
    }

    /// <summary>
    /// One simple solid of a sculpted thing. Axes: x = to the user's right, y = up, z = away from the user (so the
    /// front of a thing faces -z). x, y, z is the MIDDLE of the part; sx, sy, sz is its size along its own axes before
    /// it is turned; rx, ry, rz turn it about its middle, in degrees, applied in the order z, x, y (Unity's Euler).
    ///   box       sx, sy, sz = its three sides. round = 0..1: how much its edges are rounded (1 = as far as its thinnest side allows).
    ///   cylinder  stands along y: sx (= sz) = diameter, sy = height. round = 0..1: how much its two rims are rounded.
    ///   cone      stands along y, wide end down: sx (= sz) = diameter of the base, sy = height.
    ///             round = 0..1: how blunt it is - the diameter of its top as a part of the base (0 = a point, 0.5 = a lamp shade).
    ///   sphere    sx, sy, sz = its three diameters (a ball when equal, otherwise an egg or a cushion).
    ///   capsule   a rod with round ends along y: sx (= sz) = thickness, sy = whole length.
    ///   torus     a ring lying flat (its hole looks up, along y): sx (= sz) = outer diameter, sy = thickness of the ring itself.
    /// Where sx and sz differ the round shapes are oval.
    /// </summary>
    public sealed class ShapePart
    {
        public const int Max = 40;          // parts of one thing: every snapshot carries them, and the headset draws them
        public static readonly string[] Shapes = { "box", "cylinder", "cone", "sphere", "capsule", "torus" };

        // (a number that is 0 is left out of the JSON and read back as 0: most parts are not turned and many sit on the
        // middle line, and every snapshot carries every part of every thing)
        public string shape = "box";
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)] public float x, y, z;
        public float sx = 0.2f, sy = 0.2f, sz = 0.2f;
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)] public float rx, ry, rz;
        public string color = "";           // "#RRGGBB"
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)] public float round;
    }

    /// <summary>One act of a skill.</summary>
    public sealed class SkillStep
    {
        // music (arg: "" = this skill's song, or a song id; "stop") | dance | ambience (arg: "" = this skill's ambience, an ambience id, or "clear")
        // activity (arg: study | sit | nap | read | play) | emote (arg: wave | happy | think | startle | thankful) | say (arg: the line)
        // rain (arg: on | off) | goto (arg: an item id, or "home") | wait
        public string act = "";
        public string arg = "";
        public float seconds;               // how long this act lasts before the next one starts (0 = goes on until the skill is stopped)
    }

    /// <summary>Something the partner can do when asked: built in (Dance) or taught by the user in their own words.</summary>
    public sealed class SkillDef
    {
        public string id = "";
        public string name = "";            // short label for its badge ("Dance", "노래 틀기")
        public string asked = "";           // what the user said when teaching it
        public string note = "";            // one line: what happens
        public string[] phrases = new string[0];   // ways the user may ask for it (for the language model's eyes)
        public string itemId = "";          // the item it uses, if any (the partner goes to it first)
        public List<SkillStep> steps = new List<SkillStep>();
        public SongDef song;                // its own tune, if it plays one
        public AmbienceDef ambience;        // its own surroundings, if it changes them
        public string icon = "";            // file name of its icon PNG under /world/ ("" = a built-in badge)
        public bool builtin;
        public string status = "ready";     // making | ready | failed
        public string script = "";          // what the skill DOES, as a small Lua program (RoomScript: on_use() when it is asked for, on_stop() when it is stopped); "" = its steps above
    }

    /// <summary>
    /// A tune as notes, short enough to loop. Steps are sixteenth notes; every pattern has bars * 16 entries (shorter
    /// ones repeat). Notes are MIDI numbers (60 = middle C), 0 = silence, a negative number holds the previous note.
    /// </summary>
    public sealed class SongDef
    {
        public string id = "";
        public string name = "";
        public int bpm = 100;
        public int bars = 4;
        public float swing;                 // 0..0.5: how late the off-beat sixteenths come
        public string lead = "bell";        // bell | pluck | pad | square | soft
        public int[] melody = new int[0];
        public int[] chords = new int[0];   // one root note per bar (a triad is built on it); 0 = none
        public bool minor;
        public int[] bass = new int[0];
        public string kick = "", snare = "", hat = "";   // 'x' = hit, '-' = rest, one character per sixteenth
        public float volume = 0.8f;
    }

    /// <summary>
    /// The surroundings. The work area - the user's screens and desk - is left exactly as it is; everything round it is
    /// dimmed and drawn over with a moving pattern.
    /// </summary>
    public sealed class AmbienceDef
    {
        public string id = "";
        public string name = "";
        public string pattern = "none";     // none | spots (club lights) | stars | rain | aurora | snow | petals | bubbles | peak / coast (painted views, kept for old data) | photo (a 360-degree photograph: image)
        public string c0 = "#1A1030", c1 = "#FF5FA2", c2 = "#5FD0FF";   // background, and two colours of the pattern
        public float dim = 0.6f;            // 0..1: how much of the real room is covered round the work area
        public float speed = 1f;            // how fast the pattern moves
        public float density = 1f;          // how much of it there is
        public bool beat;                   // pulses with the tune that is playing
        // A PLACE as a photograph: the name of a 360-degree picture that ships with the app (Resources/Places/<image>), drawn all
        // round the user instead of a pattern (pattern = "photo"). yaw turns it (degrees) so that its best side lies beyond the screens.
        public string image = "";
        public float yaw;
    }

    /// <summary>Everything of this kind the host knows, sent to the clients with each snapshot.</summary>
    public sealed class WorldState
    {
        public int PackRoom()
        {
            int count = 0;
            foreach (var item in items)
            {
                if (item == null || (!item.inRoom && string.IsNullOrEmpty(item.wornOn) && string.IsNullOrEmpty(item.onId))) continue;
                item.inRoom = false;
                item.wornOn = item.onId = "";
                item.y = 0;
                count++;
            }
            return count;
        }
        public int revision;
        public List<ItemDef> items = new List<ItemDef>();
        public List<SkillDef> skills = new List<SkillDef>();
        public AmbienceDef ambience;        // null = the room as it is
        public SongDef song;                // null = silence
        public double songAt;               // host time the tune started
        public string doing = "";           // what the partner was asked to do just now: "" | dance | study | sit | nap | read | play | skill:<id>
        public string doingSkill = "";      // the skill being carried out ("" = none)
        public double doingAt;              // host time it started
        public string making = "";          // one line while something is being built ("Drawing the desk ..."), "" otherwise
        // The partner at its workbench: while ANYTHING is being made (a thing, a skill, a game pack, an ability, surroundings)
        // makingKind says what (item | skill | game | ability | ambience, "" = nothing), makingName what it will be called
        // (may be "" at first), makingProgress how far it is (0..1), makingId the item's id when it is a thing that already
        // stands on the desk. The headset shows the partner hammering away at it for as long as makingKind is set.
        public string makingKind = "", makingName = "", makingId = "";
        public float makingProgress;
        public List<GamePackDef> games = new List<GamePackDef>();      // the game packs that are installed (Market.cs): what the headset offers on its Play page
        public List<AbilityDef> abilities = new List<AbilityDef>();
        // What the user OWNS and can switch on at any time, whether or not it is on now (ambience / song above say what is on):
        public List<AmbienceDef> ambiences = new List<AmbienceDef>();  // surroundings (the places bought in the marketplace, the ones that came with the partner): world command "ambience" with the id, "clear" to go back to the room
        public List<SongDef> songs = new List<SongDef>();              // the records of the record player: world command "music" with the id, "stop"    // the abilities that are installed (Market.cs): instructions the language model follows on the PC
    }

    /// <summary>From a client (or lpctl): what to do with the world.</summary>
    public sealed class WorldCommand
    {
        // makeItem (text) | teach (text) | useSkill (id or name) | ambience (text: an id, a name or a description; "clear") | music (text: an id, a description; "stop")
        // do (text: dance | study | sit | nap | read | play | stop) | place (id, x, z, yaw) | store (id) | delete (id) | deleteSkill (id) | stop
        // wear (id, text = handR | handL | head | back, "" = where its script says) | unwear (id): the partner puts the thing on / takes it off
        // act (id, text = the name of something its script offers, e.g. "swing"): the thing's script is asked to do that
        // use (id): the user pressed that thing on the desk - the partner goes and uses it (what it is for decides: sit, study, nap, read, play ...);
        //           pressed again while it is doing that, it stops. (A thing for music is not "used": the headset shows the records to pick from.)
        public string cmd = "";
        public string id = "";
        public string text = "";
        public float x, z, yaw;
        public float y;                     // place: metres above the desk (see ItemDef.y)
        public string onId = "";            // place: the thing it was put on ("" = the desk)
    }

    public static class ItemUses
    {
        public const string Sit = "sit", Table = "table", Sleep = "sleep", Read = "read", Play = "play", Music = "music", Light = "light", Decor = "decor", Eat = "eat";
        public static readonly string[] All = { Sit, Table, Sleep, Read, Play, Music, Light, Decor, Eat };
    }

    /// <summary>The built-in toy shapes: what a thing looks like before (or without) a generated model, and what it is for.</summary>
    // (sizeM: no thing is smaller than 0.05 m - a mug of 3 cm beside a partner of 10 cm could hardly be seen, let alone picked up)
    public static class ItemKinds
    {
        public sealed class Kind { public string id; public string[] uses; public float sizeM; public string color; public string[] words; }

        public static readonly Kind[] All =
        {
            new Kind { id = "desk", uses = new[] { ItemUses.Table }, sizeM = 0.11f, color = "#E9C99A", words = new[] { "desk", "table", "책상", "테이블", "탁자" } },
            new Kind { id = "chair", uses = new[] { ItemUses.Sit }, sizeM = 0.075f, color = "#F4A9A0", words = new[] { "chair", "stool", "seat", "의자", "스툴" } },
            new Kind { id = "bed", uses = new[] { ItemUses.Sleep, ItemUses.Sit }, sizeM = 0.17f, color = "#BFD9F2", words = new[] { "bed", "침대", "이불", "매트" } },
            new Kind { id = "sofa", uses = new[] { ItemUses.Sit, ItemUses.Sleep }, sizeM = 0.15f, color = "#C9B8F0", words = new[] { "sofa", "couch", "소파", "쇼파" } },
            new Kind { id = "book", uses = new[] { ItemUses.Read }, sizeM = 0.05f, color = "#F2C14E", words = new[] { "book", "책", "노트", "notebook", "만화" } },
            new Kind { id = "ball", uses = new[] { ItemUses.Play }, sizeM = 0.05f, color = "#FF9F6B", words = new[] { "ball", "공", "축구공", "농구공" } },
            new Kind { id = "lamp", uses = new[] { ItemUses.Light, ItemUses.Decor }, sizeM = 0.11f, color = "#FFE08A", words = new[] { "lamp", "light", "램프", "조명", "스탠드", "전등" } },
            new Kind { id = "plant", uses = new[] { ItemUses.Decor }, sizeM = 0.09f, color = "#9BD3A0", words = new[] { "plant", "tree", "flower", "화분", "나무", "꽃", "식물" } },
            new Kind { id = "cup", uses = new[] { ItemUses.Eat }, sizeM = 0.05f, color = "#FFFFFF", words = new[] { "cup", "mug", "coffee", "tea", "컵", "커피", "머그", "차" } },
            new Kind { id = "record", uses = new[] { ItemUses.Music }, sizeM = 0.08f, color = "#8C6A5D", words = new[] { "record", "turntable", "vinyl", "레코드", "턴테이블", "엘피", "lp" } },
            new Kind { id = "speaker", uses = new[] { ItemUses.Music }, sizeM = 0.07f, color = "#7D8CA3", words = new[] { "speaker", "radio", "boombox", "스피커", "라디오", "오디오" } },
            new Kind { id = "food", uses = new[] { ItemUses.Eat }, sizeM = 0.05f, color = "#F7B7C8", words = new[] { "cake", "food", "snack", "cookie", "케이크", "음식", "간식", "쿠키", "과자", "빵" } },
            new Kind { id = "toy", uses = new[] { ItemUses.Play, ItemUses.Decor }, sizeM = 0.05f, color = "#FFD66B", words = new[] { "toy", "doll", "robot", "장난감", "인형", "로봇" } },
            new Kind { id = "box", uses = new[] { ItemUses.Decor }, sizeM = 0.06f, color = "#E3C7A0", words = new string[0] },
        };

        public static Kind Find(string id)
        {
            foreach (var k in All) if (k.id == id) return k;
            return All[All.Length - 1];
        }

        /// <summary>The built-in shape a description sounds like (the last one, "box", when nothing matches). Used when no language model is at hand.</summary>
        public static Kind Guess(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            foreach (var k in All) foreach (var w in k.words) if (t.Contains(w)) return k;
            return All[All.Length - 1];
        }
    }

    /// <summary>What the partner can do with the things that are out on the desk: worked out from what they are for.</summary>
    public static class Activities
    {
        public sealed class Activity { public string name; public string[] itemIds; public string line; }

        static ItemDef With(IList<ItemDef> items, string use, ItemDef not = null)
        {
            foreach (var i in items) if (i != not && i.inRoom && i.status != "failed" && Array.IndexOf(i.uses, use) >= 0) return i;
            return null;
        }

        /// <summary>Every activity the things on the desk allow, the richest first (a desk AND a chair: study, not just sit).</summary>
        public static List<Activity> Possible(IList<ItemDef> items)
        {
            var list = new List<Activity>();
            if (items == null) return list;
            var table = With(items, ItemUses.Table); var seat = With(items, ItemUses.Sit); var bed = With(items, ItemUses.Sleep);
            var book = With(items, ItemUses.Read); var toy = With(items, ItemUses.Play); var music = With(items, ItemUses.Music);
            if (table != null && seat != null) list.Add(new Activity { name = "study", itemIds = new[] { table.id, seat.id }, line = "sit down at the " + table.name + " and study" });
            if (book != null) list.Add(new Activity { name = "read", itemIds = seat != null ? new[] { book.id, seat.id } : new[] { book.id }, line = "read the " + book.name });
            if (bed != null) list.Add(new Activity { name = "nap", itemIds = new[] { bed.id }, line = "take a nap on the " + bed.name });
            if (seat != null) list.Add(new Activity { name = "sit", itemIds = new[] { seat.id }, line = "sit on the " + seat.name });
            if (toy != null) list.Add(new Activity { name = "play", itemIds = new[] { toy.id }, line = "play with the " + toy.name });
            if (music != null) list.Add(new Activity { name = "music", itemIds = new[] { music.id }, line = "put a tune on with the " + music.name });
            return list;
        }

        public static Activity Find(IList<ItemDef> items, string name)
        {
            foreach (var a in Possible(items)) if (a.name == name) return a;
            return null;
        }

        /// <summary>Where a new thing goes on the desk so that things that belong together stand together (the chair at the desk ...).</summary>
        // Where a thing that is neither a table nor a chair goes: first the spots right IN FRONT of the home spot (towards
        // the user, z < 0), where a thing that was just made is seen at once; then the arc round the home spot.
        static readonly float[] SpotX = { 0.10f, -0.10f, 0.20f, -0.20f, 0.00f, 0.30f, -0.30f,   0.17f, 0.26f, -0.27f, 0.10f, -0.08f, 0.33f, -0.36f, 0.20f };
        static readonly float[] SpotZ = { -0.07f, -0.07f, -0.08f, -0.08f, -0.12f, -0.06f, -0.06f,   0.05f, -0.04f, -0.04f, 0.13f, 0.15f, 0.10f, 0.08f, -0.12f };
        /// <summary>How many of the spots lie in front of the home spot (the first ones of the list).</summary>
        public const int FrontSpots = 7;

        /// <summary>Is that spot free for the thing - nothing standing there, and nothing as large as it standing between it and the user?</summary>
        static bool SpotFree(IList<ItemDef> items, ItemDef fresh, float x, float z)
        {
            foreach (var i in items)
            {
                if (i == null || i == fresh || !i.inRoom) continue;
                float reach = (i.sizeM + fresh.sizeM) * 0.5f + 0.01f;
                if (Math.Abs(i.x - x) < reach && Math.Abs(i.z - z) < reach) return false;
                // (hidden: something at least as large stands nearer to the user, in the same line of sight)
                if (i.z < z - 0.02f && i.sizeM >= fresh.sizeM * 0.8f && Math.Abs(i.x - x) < (i.sizeM + fresh.sizeM) * 0.5f) return false;
            }
            return true;
        }

        public static void Arrange(IList<ItemDef> items, ItemDef fresh)
        {
            fresh.y = 0f; fresh.onId = "";      // where it belongs is on the desk itself, not on another thing
            var table = With(items, ItemUses.Table, fresh);
            bool isSeat = Array.IndexOf(fresh.uses, ItemUses.Sit) >= 0 && Array.IndexOf(fresh.uses, ItemUses.Sleep) < 0;
            bool isTable = Array.IndexOf(fresh.uses, ItemUses.Table) >= 0;
            // (the chair goes BEHIND the desk as the user sees it and faces the user: the partner is then seen from the front, over its desk)
            if (isSeat && table != null) { fresh.x = table.x; fresh.z = table.z + (table.sizeM * 0.30f + fresh.sizeM * 0.40f); fresh.yaw = 0; return; }
            if (isTable) { fresh.x = -0.17f; fresh.z = 0.02f; fresh.yaw = 0; var seat = With(items, ItemUses.Sit, fresh); if (seat != null && Array.IndexOf(seat.uses, ItemUses.Sleep) < 0) { seat.x = fresh.x; seat.z = fresh.z + (fresh.sizeM * 0.30f + seat.sizeM * 0.40f); seat.yaw = 0; } return; }
            // the others: a free spot in plain sight right in front of the home spot, otherwise along the arc round it
            for (int k = 0; k < SpotX.Length; k++)
                if (SpotFree(items, fresh, SpotX[k], SpotZ[k])) { fresh.x = SpotX[k]; fresh.z = SpotZ[k]; fresh.yaw = SpotX[k] > 0 ? -20f : SpotX[k] < 0 ? 20f : 0f; return; }
            fresh.x = 0.22f; fresh.z = 0.0f; fresh.yaw = 0;
        }
    }

    /// <summary>What comes with the partner: a few tunes and surroundings, and the skill of dancing.</summary>
    public static class WorldDefaults
    {
        public static SongDef Club() => new SongDef
        {
            id = "song-club", name = "Club", bpm = 122, bars = 4, lead = "square", minor = true, volume = 0.8f,
            melody = new[] { 69, 0, 72, 0, 76, 0, 72, 0, 69, 0, 72, 0, 77, 76, 0, 0,  67, 0, 71, 0, 74, 0, 71, 0, 67, 0, 71, 0, 76, 74, 0, 0,
                             65, 0, 69, 0, 72, 0, 69, 0, 65, 0, 69, 0, 74, 72, 0, 0,  64, 0, 68, 0, 71, 0, 68, 0, 76, 0, 74, 0, 71, 0, 68, 0 },
            chords = new[] { 57, 55, 53, 52 }, bass = new[] { 45, 0, 45, 57, 0, 45, 0, 57, 45, 0, 45, 57, 0, 45, 57, 0 },
            kick = "x---x---x---x---", snare = "----x-------x---", hat = "--x---x---x---xx",
        };

        public static SongDef Lofi() => new SongDef
        {
            id = "song-lofi", name = "Lo-fi", bpm = 78, bars = 4, swing = 0.18f, lead = "bell", volume = 0.75f,
            melody = new[] { 72, -1, 0, 76, -1, 0, 79, -1, 0, 0, 77, -1, 76, -1, 0, 0,  74, -1, 0, 77, -1, 0, 81, -1, 0, 0, 79, -1, 77, -1, 0, 0,
                             72, -1, 0, 76, -1, 0, 79, -1, 84, -1, 0, 0, 83, -1, 79, -1,  77, -1, 0, 0, 76, -1, 0, 0, 74, -1, -1, -1, 0, 0, 0, 0 },
            chords = new[] { 60, 57, 65, 67 }, bass = new[] { 36, 0, 0, 0, 0, 0, 43, 0, 36, 0, 0, 0, 0, 0, 0, 0 },
            kick = "x-----x---x-----", snare = "----x-------x---", hat = "x-x-x-x-x-x-x-x-",
        };

        public static SongDef Lullaby() => new SongDef
        {
            id = "song-lullaby", name = "Lullaby", bpm = 66, bars = 4, lead = "soft", volume = 0.7f,
            melody = new[] { 76, -1, -1, -1, 79, -1, -1, -1, 76, -1, -1, -1, 72, -1, -1, -1,  74, -1, -1, -1, 77, -1, -1, -1, 74, -1, -1, -1, 71, -1, -1, -1,
                             72, -1, -1, -1, 76, -1, -1, -1, 79, -1, -1, -1, 84, -1, -1, -1,  83, -1, -1, -1, 79, -1, -1, -1, 72, -1, -1, -1, -1, -1, 0, 0 },
            chords = new[] { 60, 55, 57, 53 }, bass = new[] { 48, 0, 0, 0, 0, 0, 0, 0, 55, 0, 0, 0, 0, 0, 0, 0 },
            kick = "", snare = "", hat = "",
        };

        public static AmbienceDef ClubLights() => new AmbienceDef { id = "amb-club", name = "Club", pattern = "spots", c0 = "#140A2E", c1 = "#FF4FA3", c2 = "#4FD6FF", dim = 0.82f, speed = 1.2f, density = 1.2f, beat = true };
        public static AmbienceDef RainAll() => new AmbienceDef { id = "amb-rain", name = "Rain", pattern = "rain", c0 = "#2A3550", c1 = "#CFE6FF", c2 = "#8FB8E8", dim = 0.45f, speed = 1f, density = 1f };
        public static AmbienceDef NightSky() => new AmbienceDef { id = "amb-night", name = "Night sky", pattern = "stars", c0 = "#0C1030", c1 = "#FFF2C0", c2 = "#9FB8FF", dim = 0.8f, speed = 0.6f, density = 1f };
        public static AmbienceDef Aurora() => new AmbienceDef { id = "amb-aurora", name = "Aurora", pattern = "aurora", c0 = "#081826", c1 = "#6CFFC0", c2 = "#B48CFF", dim = 0.75f, speed = 0.7f, density = 1f };
        public static AmbienceDef Petals() => new AmbienceDef { id = "amb-petals", name = "Spring", pattern = "petals", c0 = "#FFE9EE", c1 = "#FFB3C8", c2 = "#FFFFFF", dim = 0.35f, speed = 0.8f, density = 1f };
        public static AmbienceDef Snow() => new AmbienceDef { id = "amb-snow", name = "Snow", pattern = "snow", c0 = "#DCE8F5", c1 = "#FFFFFF", c2 = "#C8DCF0", dim = 0.4f, speed = 0.7f, density = 1f };

        // PLACES: whole views painted round the user (the work area stays as it is). c0 = the sky high up, c1 = the sun and
        // the warm band at the horizon, c2 = what is far away (haze on the ranges, shallow water). dim 1 = the room is gone.
        public static AmbienceDef Peak() => new AmbienceDef { id = "amb-peak", name = "Mountain top", pattern = "peak", c0 = "#4F86D6", c1 = "#FFC27A", c2 = "#9DB4D9", dim = 1f, speed = 1f, density = 1f };
        public static AmbienceDef Coast() => new AmbienceDef { id = "amb-coast", name = "Seaside", pattern = "coast", c0 = "#48A4EA", c1 = "#FFE0A8", c2 = "#45CFC6", dim = 1f, speed = 1f, density = 1f };
        public static AmbienceDef SunsetCoast() => new AmbienceDef { id = "amb-sunset", name = "Sunset beach", pattern = "coast", c0 = "#5B4B9E", c1 = "#FF9A5C", c2 = "#F2A07A", dim = 1f, speed = 0.8f, density = 1f };

        public static AmbienceDef[] Ambiences() => new[] { ClubLights(), RainAll(), NightSky(), Aurora(), Petals(), Snow(), Peak(), Coast(), SunsetCoast() };
        public static SongDef[] Songs() => new[] { Club(), Lofi(), Lullaby() };

        /// <summary>Dancing: comes with the partner. A club tune, the club lights round the work area, and the dance.</summary>
        public static SkillDef Dance() => new SkillDef
        {
            id = "skill-dance", name = "Dance", builtin = true, note = "Puts a club tune on, turns the room round the desk into a club and dances.",
            phrases = new[] { "dance", "let's dance", "춤 춰", "춤춰 줘", "댄스", "춤추자" },
            steps = new List<SkillStep> { new SkillStep { act = "ambience" }, new SkillStep { act = "music" }, new SkillStep { act = "dance" } },
            song = Club(), ambience = ClubLights(),
        };

        /// <summary>
        /// NOT a skill of the partner's any more (2026-11-17): music is ONE thing - the record player and its records
        /// (WorldState.songs; Lo-fi is one of the three records that come with it). Kept only as the record of what a
        /// saved state from before may still hold under the id "skill-music"; the host takes that out when it starts.
        /// </summary>
        public static SkillDef Music() => new SkillDef
        {
            id = "skill-music", name = "Music", builtin = true, note = "Puts a quiet lo-fi tune on to work to.",
            phrases = new[] { "play music", "put some music on", "노래 틀어 줘", "음악 틀어", "노래 틀어줄래" },
            steps = new List<SkillStep> { new SkillStep { act = "music" }, new SkillStep { act = "emote", arg = "happy", seconds = 2f } },
            song = Lofi(),
        };

        public static SkillDef[] Skills() => new[] { Dance() };
    }
}
