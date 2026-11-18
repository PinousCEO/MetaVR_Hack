using System;
using System.Collections.Generic;
using System.IO;
using LivingPartner.Shared;

namespace LivingPartner.MR
{
    /// <summary>
    /// PLAYING WITHOUT THE PC. Everything in this app goes through the Companion Host on a PC - but somebody who only
    /// has the headset (a judge with nothing but the APK) must still get a whole, working moment. This is a small
    /// stand-in for the host that lives inside the app, behind the same link (HostLink.GoLocal): what the app sends
    /// arrives in Handle as the objects they are, and what a host would answer - state snapshots, glass.state, a line the
    /// partner says, intent.result - is handed back through `deliver` and reaches the app through the very same handlers.
    /// The rest of the app does not know the difference. Pure C#: no scene objects, no engine types.
    ///
    /// WHAT IS IN THE LOCAL WORLD (all of it ships inside the app, in Shared):
    ///  - the partner, out in the room;
    ///  - the thirteen things that ship, each with the script of its kind (RoomScriptExamples.ForKind) - all of them in the bag - and two things the partner MADE, to show what
    ///    it can make: the baseball bat (with a swing of its own) and the magic wand;
    ///  - the skill of dancing (a script);
    ///  - the games and little tools that are scripts: the six on the shelf, and Glass Keeper, Clone Shooter, Omok and
    ///    the Calculator - what the partner writes when it is asked for a game;
    ///  - every place and every record that comes with the partner.
    /// WHAT IT CARRIES OUT (the small part of the host's rules that the headset's own buttons need - WorldRules,
    /// Maker and GlassHost are where the full rules are):
    ///   world.command   place | store | use | wear | unwear | act | ambience | music | useSkill | do | stop
    ///   glass.command   start (always out on the desk: side "mr") | stop | choose
    ///   glass.state     the reports of the game that runs: taken into the snapshot; "over" -> the best score is kept,
    ///                   the result is said, the game is cleared six seconds later
    ///   settings.update quiet, rain
    ///   market.command  open -> one line for the menu: the marketplace needs the PC app
    /// Everything else (voice, tasks, the screens, documents, tennis ...) is ignored quietly: there is no PC.
    /// A snapshot goes out at once after a change and otherwise once a second; world.revision goes up with changes.
    /// What the user did is kept in one small file (where the things are, which are out, the place that is on, Quiet,
    /// the best scores), with all owned things in the bag at the next start.
    /// </summary>
    public sealed class LocalWorld
    {
        /// <summary>What the menu says when the marketplace is asked for.</summary>
        public const string NeedsPc = "The marketplace needs the PC app";
        /// <summary>The pictures of the local things are not fetched from a PC: a file name that starts with this is one of the pictures inside the app.</summary>
        public const string IconPrefix = "local-";
        const double OverSeconds = 6, SilentSeconds = 20, SnapshotEvery = 1.0, SaveAfter = 1.2;
        const float StackMaxY = 0.4f;

        readonly string _path;
        readonly Func<double> _clock;
        readonly Action<string, object> _deliver;
        /// <summary>A line for the menu's strip of words (the marketplace was asked for).</summary>
        public Action<string> Status;

        readonly WorldState _w = new WorldState();
        readonly GlassState _g = new GlassState();
        readonly SettingsMsg _settings = new SettingsMsg { micEnabled = false, voiceReplies = false, cameraEnabled = false };
        readonly Dictionary<string, int> _best = new Dictionary<string, int>();
        readonly string _session = "local-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        WorldState _sent; int _sentRev = -1;
        long _seq;
        bool _dirty = true;
        double _nextSnapshot, _saveAt = -1, _overAt = -1, _lastReport;
        string _lastRecord = "";

        /// <summary>The saved file was found and read at the start.</summary>
        public bool Restored { get; private set; }
        /// <summary>One line about the start, for the log.</summary>
        public string StartNote { get; private set; } = "";
        public int Saves { get; private set; }
        public string SaveError { get; private set; } = "";
        public int Revision => _w.revision;
        public int Commands { get; private set; }
        public WorldState World => _w;

        /// <summary>path: where the state is kept ("" = nowhere). clock: the link's time. deliver: a message for the app (HostLink.DeliverLocal).</summary>
        public LocalWorld(string path, Func<double> clock, Action<string, object> deliver)
        {
            _path = path ?? ""; _clock = clock; _deliver = deliver;
            _court.Changed += () => {
                _courtView = Proto.FromJson<TennisState>(Proto.ToJson(_court.State));
                _deliver(Msg.GameState, _courtView);
                _dirty = true;
            };
            Build();
            // (run numbers never repeat those of a run before: the app remembers which runs it has ended)
            _g.runId = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 1000000) * 100;
            Load();
        }

        // ------------------------------------------------------------------ what there is
        static string Pic(string name) => IconPrefix + name + ".png";

        void Build()
        {
            // ---- the things that ship: the list ends with what the bag shows first (the newest is the last one)
            string[] kinds = { "toy", "food", "speaker", "record", "cup", "plant", "lamp", "book", "sofa", "bed", "ball", "chair", "desk" };
            foreach (string kind in kinds)
            {
                var k = ItemKinds.Find(kind);
                string name = kind;
                foreach (var e in RoomScriptExamples.Shipped) if (e.id == kind) name = e.name;
                _w.items.Add(new ItemDef
                {
                    id = "item-" + kind, name = name, asked = name, kind = kind, uses = k.uses, sizeM = k.sizeM, color = k.color, status = "ready", progress = 1f,
                    icon = Pic("item-" + kind), script = RoomScriptExamples.ForKind(kind) ?? "", note = "Came with Nilo.",
                });
            }
            // ---- two things the partner made when it was asked (their scripts are the ones it wrote: RoomScriptExamples.Made)
            foreach (string made in new[] { "wand", "bat" })
                foreach (var e in RoomScriptExamples.Made)
                {
                    if (e.id != made) continue;
                    var k = ItemKinds.Find(e.kind);
                    _w.items.Add(new ItemDef
                    {
                        id = "item-" + made, name = e.name, asked = e.requestEn, kind = e.kind, uses = e.uses, sizeM = made == "bat" ? 0.09f : e.sizeM, color = made == "bat" ? "#E9C99A" : "#FFD66B",
                        status = "ready", progress = 1f, icon = made == "bat" ? Pic("item-bat") : "", script = e.script, note = "Nilo made this.",
                        parts = made == "bat" ? BatParts() : WandParts(),
                    });
                }
            // ---- dancing: the script that comes with the partner
            var dance = WorldDefaults.Dance();
            dance.script = RoomScriptExamples.ForSkill(dance.id) ?? ""; dance.icon = Pic("skill-dance");
            _w.skills.Add(dance);

            _w.games.Add(StarterGames.Keeper());
            _w.games.Add(StarterGames.Tennis());
            _w.games.Add(StarterGames.Defense());
            _w.games.Add(StarterGames.Gomoku());

            _w.ambiences.AddRange(WorldDefaults.Ambiences());
            _w.songs.AddRange(WorldDefaults.Songs());
            _w.revision = 1;
        }

        // The bat as the partner sculpted it when it was asked for one (seven simple parts; longest side 1, standing on y = 0).
        static List<ShapePart> BatParts() => new List<ShapePart>
        {
            new ShapePart { shape = "cylinder", y = 0.72f, sx = 0.16f, sy = 0.46f, sz = 0.16f, color = "#E9C99A", round = 0.9f },
            new ShapePart { shape = "cone", y = 0.49f, sx = 0.14f, sy = 0.22f, sz = 0.14f, rx = 180f, color = "#B98B63", round = 0.82f },
            new ShapePart { shape = "cylinder", y = 0.24f, sx = 0.09f, sy = 0.32f, sz = 0.09f, color = "#E9C99A", round = 0.8f },
            new ShapePart { shape = "cylinder", y = 0.09f, sx = 0.11f, sy = 0.10f, sz = 0.11f, color = "#FFF3D6", round = 0.7f },
            new ShapePart { shape = "cylinder", y = 0.04f, sx = 0.14f, sy = 0.08f, sz = 0.14f, color = "#B98B63", round = 0.9f },
            new ShapePart { shape = "sphere", y = 0.025f, sx = 0.12f, sy = 0.05f, sz = 0.12f, color = "#FFF3D6" },
            new ShapePart { shape = "sphere", y = 0.955f, sx = 0.14f, sy = 0.09f, sz = 0.14f, color = "#FFF3D6" },
        };

        // The wand: a rod with a soft grip, and a round star of light on its tip.
        static List<ShapePart> WandParts() => new List<ShapePart>
        {
            new ShapePart { shape = "cylinder", y = 0.42f, sx = 0.07f, sy = 0.80f, sz = 0.07f, color = "#8C6A5D", round = 0.7f },
            new ShapePart { shape = "cylinder", y = 0.11f, sx = 0.10f, sy = 0.20f, sz = 0.10f, color = "#F4A9A0", round = 0.9f },
            new ShapePart { shape = "torus", y = 0.80f, sx = 0.14f, sy = 0.04f, sz = 0.14f, color = "#FFF3D6" },
            new ShapePart { shape = "sphere", y = 0.90f, sx = 0.20f, sy = 0.20f, sz = 0.14f, color = "#FFD66B" },
        };

        // ------------------------------------------------------------------ kept from one sitting to the next
        sealed class Saved
        {
            public int version = 1;
            public int revision;
            public List<SavedItem> items = new List<SavedItem>();
            public string ambience = "", lastRecord = "";
            public bool quiet;
            public Dictionary<string, int> best = new Dictionary<string, int>();
            public string savedAt = "";
        }
        sealed class SavedItem { public string id = ""; public bool inRoom; public float x, z, yaw, y; public string onId = "", wornOn = ""; }

        void Load()
        {
            StartNote = "a new world (nothing was saved yet)";
            if (_path.Length == 0) { StartNote = "a new world (it is not kept)"; return; }
            try
            {
                if (!File.Exists(_path)) return;
                var s = Proto.FromJson<Saved>(File.ReadAllText(_path));
                if (s == null || s.items == null) { StartNote = "a new world (the saved file could not be read)"; return; }
                foreach (var k in s.items)
                {
                    var it = k != null ? FindItemById(k.id) : null;
                    if (it == null) continue;
                    it.x = Fin(k.x, 1.5f); it.z = Fin(k.z, 1.5f); it.yaw = Fin(k.yaw, 360f);
                }
                Settle();
                _w.ambience = FindAmbience(s.ambience ?? "");
                _lastRecord = s.lastRecord ?? "";
                _settings.quietMode = s.quiet;
                if (s.best != null) foreach (var kv in s.best) if (kv.Key != null && kv.Value > 0) _best[kv.Key] = kv.Value;
                _w.revision = Math.Max(1, s.revision) + 1;
                Restored = true;
                StartNote = $"saved inventory ({s.savedAt}): all things start in the bag, {(_w.ambience != null ? "the place " + _w.ambience.name : "no place on")}, {_best.Count} best score(s)";
            }
            catch (Exception e) { StartNote = "a new world (the saved file could not be read: " + e.Message + ")"; }
        }

        static float Fin(float v, float max) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : Math.Max(-max, Math.Min(max, v));

        /// <summary>A thing whose onId names nothing that is out on the desk stands on the desk; a thing in the bag stands on nothing.</summary>
        void Settle()
        {
            foreach (var i in _w.items)
            {
                if (!i.inRoom) { i.y = 0f; i.onId = ""; i.wornOn = ""; continue; }
                var under = i.onId.Length > 0 ? FindItemById(i.onId) : null;
                if (i.onId.Length > 0 && (under == null || !under.inRoom || under == i)) { i.onId = ""; i.y = 0f; }
                if (i.onId.Length == 0) i.y = 0f;
            }
        }

        /// <summary>Writes the file now when something has changed since the last time (the app is paused or closed).</summary>
        public void Flush() { if (_saveAt >= 0) Save(); }

        void Save()
        {
            _saveAt = -1;
            if (_path.Length == 0) return;
            try
            {
                var s = new Saved { revision = _w.revision, ambience = _w.ambience != null ? _w.ambience.id : "", lastRecord = _lastRecord, quiet = _settings.quietMode, savedAt = DateTime.Now.ToString("s") };
                foreach (var i in _w.items) s.items.Add(new SavedItem { id = i.id, inRoom = i.inRoom, x = i.x, z = i.z, yaw = i.yaw, y = i.y, onId = i.onId ?? "", wornOn = i.wornOn ?? "" });
                foreach (var kv in _best) s.best[kv.Key] = kv.Value;
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, Proto.ToJson(s, true));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
                Saves++; SaveError = "";
            }
            catch (Exception e) { SaveError = e.Message; }
        }

        // ------------------------------------------------------------------ every frame
        public void Tick()
        {
            double now = _clock();
            _court.Tick(now);
            if (_g.game.Length > 0)
            {
                if (_overAt >= 0) { if (now - _overAt > OverSeconds) { ClearGlass(); TellGlass(); } }
                else if (now - _lastReport > SilentSeconds) { ClearGlass(); TellGlass(); }      // nobody is playing it
            }
            if (_saveAt >= 0 && now >= _saveAt) Save();
            if (_dirty || now >= _nextSnapshot)
            {
                _dirty = false; _nextSnapshot = now + SnapshotEvery;
                _deliver(Msg.Snapshot, Snapshot(now));
            }
        }

        readonly TennisReferee _court = new TennisReferee();
        TennisState _courtView;

        void OnBallGame(GameCommand command)
        {
            double now = _clock();
            if (command.cmd == "hit") { _court.Hit(command, now); return; }
            if (command.cmd == "stop") { _court.Stop(); return; }
            if (command.cmd != "start" || !command.room || _court.State.active ||
                (!string.IsNullOrEmpty(command.mode) && command.mode != "keeper")) return;
            if (_g.game.Length > 0) { ClearGlass(); TellGlass(); }
            float h = Math.Max(.2f, Math.Min(.8f, command.height));
            _court.Start(new TennisCourt { room = true, width = Math.Max(.25f, Math.Min(.7f, command.width)),
                height = h, depth = Math.Max(.2f, Math.Min(.6f, command.depth)), up = new V3(0, 1, 0),
                deskHeight = -h * .5f, baseline = .20f }, command.mode, command.eye, now);
        }

        public void NotifyInventoryChanged() => Changed();

        void Changed() { _w.revision++; _dirty = true; _saveAt = _clock() + SaveAfter; }

        StateSnapshot Snapshot(double now)
        {
            // The app keeps what it is given and compares it with what comes next: the things are handed over as copies,
            // made anew whenever the world has changed. (Scripts, parts and the lists that never change are shared.)
            if (_sent == null || _sentRev != _w.revision)
            {
                _sentRev = _w.revision;
                var c = new WorldState { revision = _w.revision, doing = _w.doing, doingSkill = _w.doingSkill, doingAt = _w.doingAt, ambience = _w.ambience, song = _w.song, songAt = _w.songAt };
                foreach (var i in _w.items) c.items.Add(Copy(i));
                c.skills.AddRange(_w.skills); c.games.AddRange(_w.games); c.ambiences.AddRange(_w.ambiences); c.songs.AddRange(_w.songs);
                _sent = c;
            }
            var s = new StateSnapshot { sessionId = _session, hostTime = now, seq = ++_seq, world = _sent, glass = CopyGlass(), pc = null, mr = null, tennis = _courtView };
            s.character.quietMode = _settings.quietMode;      // (owner mr, name Nilo, idle: as a CharacterInfo begins)
            s.settings = new SettingsMsg { quietMode = _settings.quietMode, rain = _settings.rain, micEnabled = false, voiceReplies = false, cameraEnabled = false, debugOverlay = _settings.debugOverlay };
            s.connectedRoles.Add(Roles.Quest);
            return s;
        }

        static ItemDef Copy(ItemDef i) => new ItemDef
        {
            id = i.id, name = i.name, asked = i.asked, kind = i.kind, uses = i.uses, sizeM = i.sizeM, color = i.color, note = i.note, status = i.status, progress = i.progress,
            model = i.model, icon = i.icon, inRoom = i.inRoom, x = i.x, z = i.z, yaw = i.yaw, y = i.y, script = i.script, wornOn = i.wornOn, onId = i.onId, madeUnixMs = i.madeUnixMs,
            parts = i.parts, seat = i.seat, top = i.top,
        };

        GlassState CopyGlass() => new GlassState
        {
            game = _g.game, packId = _g.packId, title = _g.title, side = _g.side, monitorId = "", runId = _g.runId, phase = _g.phase, score = _g.score, lives = _g.lives, level = _g.level,
            best = _g.best, seconds = _g.seconds, choices = _g.choices, choiceNotes = _g.choiceNotes, banner = _g.banner, result = _g.result,
        };

        // ------------------------------------------------------------------ what the app sends
        public void Handle(string type, object body)
        {
            switch (type)
            {
                case Msg.World: if (body is WorldCommand w) { Commands++; OnWorld(w); } break;
                case Msg.GameCommand: if (body is GameCommand ball) { Commands++; OnBallGame(ball); } break;
                case Msg.GlassCommand: if (body is GlassCommand g) { Commands++; OnGlass(g); } break;
                case Msg.GlassState: if (body is GlassState r) OnGlassReport(r); break;
                case Msg.Settings:
                    if (body is SettingsMsg m && (m.quietMode != _settings.quietMode || m.rain != _settings.rain || m.debugOverlay != _settings.debugOverlay))
                    {
                        _settings.quietMode = m.quietMode; _settings.rain = m.rain; _settings.debugOverlay = m.debugOverlay;
                        _dirty = true; _saveAt = _clock() + SaveAfter;
                    }
                    break;
                case Msg.Market: if (body is MarketCommand mc && mc.cmd == "open" && !string.IsNullOrEmpty(mc.text)) Status?.Invoke(NeedsPc); break;
                // (everything else - the hands, the head, voice, tasks, screens, documents, tennis - has nobody to go to)
            }
        }

        void Say(string text, float seconds = 3.5f) => _deliver(Msg.Say, new SayMsg { text = text, mood = "happy", seconds = seconds, basis = "local" });

        // ------------------------------------------------------------------ the world
        static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        static bool IsOff(string t) { t = (t ?? "").Trim().ToLowerInvariant(); return t.Length == 0 || t == "clear" || t == "off" || t == "stop" || t == "none"; }

        ItemDef FindItemById(string id) { foreach (var i in _w.items) if (i.id == id) return i; return null; }

        /// <summary>A thing by its id, its name or its kind, or a name that has these words in it.</summary>
        ItemDef FindItem(string what)
        {
            what = (what ?? "").Trim();
            if (what.Length == 0) return null;
            foreach (var i in _w.items) if (Same(i.id, what)) return i;
            foreach (var i in _w.items) if (Same(i.name, what)) return i;
            foreach (var i in _w.items) if (Same(i.kind, what)) return i;
            foreach (var i in _w.items) if ((i.name ?? "").IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            return null;
        }

        AmbienceDef FindAmbience(string what)
        {
            what = (what ?? "").Trim();
            if (what.Length == 0) return null;
            foreach (var a in _w.ambiences) if (Same(a.id, what) || Same(a.id, "amb-" + what) || Same(a.name, what)) return a;
            return null;
        }

        SongDef FindSong(string what)
        {
            what = (what ?? "").Trim();
            foreach (var s in _w.songs) if (Same(s.id, what) || Same(s.id, "song-" + what) || Same(s.name, what)) return s;
            return null;
        }

        void OnWorld(WorldCommand c)
        {
            string id = (c.id ?? "").Trim(), text = (c.text ?? "").Trim();
            string target = id.Length > 0 ? id : text, arg = text.Length > 0 ? text : id;
            switch (c.cmd)
            {
                case "place": Place(FindItem(target), c); break;
                case "store": Store(FindItem(target)); break;
                case "use": Use(FindItem(target)); break;
                case "wear": Wear(FindItem(id.Length > 0 ? id : text), id.Length > 0 ? text : ""); break;
                case "unwear": { var it = FindItem(target); if (it != null && !string.IsNullOrEmpty(it.wornOn)) { it.wornOn = ""; Changed(); } break; }
                case "act": Act(id, text); break;
                case "ambience": SetAmbience(arg); break;
                case "music": SetMusic(arg); break;
                case "useSkill": UseSkill(target); break;
                case "do": Do(arg); break;
                case "stop": Stop(); break;
                // (makeItem, teach, delete ...: making needs the PC, and what ships cannot be thrown away)
            }
        }

        List<ItemDef> Riders(ItemDef under)
        {
            var list = new List<ItemDef>();
            var ids = new HashSet<string> { under.id };
            for (bool more = true; more;)
            {
                more = false;
                foreach (var i in _w.items)
                    if (i != under && i.inRoom && !string.IsNullOrEmpty(i.onId) && ids.Contains(i.onId) && ids.Add(i.id)) { list.Add(i); more = true; }
            }
            return list;
        }

        static void Carry(List<ItemDef> riders, ItemDef moved, float ox, float oz, float oyaw, float oy)
        {
            if (riders.Count == 0) return;
            float turn = moved.yaw - oyaw;
            double rad = turn * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
            foreach (var r in riders)
            {
                double dx = r.x - ox, dz = r.z - oz;
                r.x = Fin((float)(moved.x + dx * cos + dz * sin), 1.6f);
                r.z = Fin((float)(moved.z - dx * sin + dz * cos), 1.6f);
                r.yaw = (r.yaw + turn) % 360f;
                r.y = Math.Max(0f, Math.Min(2f * StackMaxY, r.y + (moved.y - oy)));
            }
        }

        /// <summary>place: where the headset put it (x, z, yaw, y metres above the desk on the thing onId) - or, with nothing given, where it belongs. What stands on it goes along.</summary>
        void Place(ItemDef item, WorldCommand c)
        {
            if (item == null) return;
            var riders = item.inRoom ? Riders(item) : new List<ItemDef>();
            float ox = item.x, oz = item.z, oyaw = item.yaw, oy = item.y;
            bool sane = !float.IsNaN(c.x) && !float.IsInfinity(c.x) && !float.IsNaN(c.z) && !float.IsInfinity(c.z) && !float.IsNaN(c.yaw) && !float.IsInfinity(c.yaw);
            bool given = sane && !(c.x == 0 && c.z == 0 && c.yaw == 0 && !(c.y > 0) && string.IsNullOrEmpty(c.onId));
            item.inRoom = true;
            if (given)
            {
                // (it may stand on another thing that is out on the desk and does not itself stand on it)
                ItemDef under = string.IsNullOrEmpty(c.onId) || c.onId == item.id ? null : FindItemById(c.onId);
                if (under != null && (!under.inRoom || riders.Contains(under))) under = null;
                item.x = Fin(c.x, 1.5f); item.z = Fin(c.z, 1.5f); item.yaw = c.yaw % 360f;
                item.onId = under != null ? under.id : "";
                item.y = under != null && !float.IsNaN(c.y) && !float.IsInfinity(c.y) ? Math.Max(0f, Math.Min(StackMaxY, c.y)) : 0f;
            }
            else Activities.Arrange(_w.items, item);      // the chair at the desk, the rest in plain sight in front of the partner's spot
            Carry(riders, item, ox, oz, oyaw, oy);
            Changed();
        }

        void Store(ItemDef item)
        {
            if (item == null || !item.inRoom) return;
            // what stood on it comes down to the desk where it is
            foreach (var r in _w.items)
            {
                if (r == item || r.onId != item.id) continue;
                float drop = r.y;
                foreach (var up in Riders(r)) up.y = Math.Max(0f, up.y - drop);
                r.y = 0f; r.onId = "";
            }
            item.inRoom = false; item.y = 0f; item.onId = ""; item.wornOn = "";
            CheckDoing();
            Changed();
        }

        /// <summary>
        /// use: a thing on the desk was pressed. A thing with a script: the headset runs its on_use itself - nothing to do
        /// here. Without one: what the thing is for decides (Activities) - it sits, studies, naps, reads, plays; pressed
        /// again while it does that, it stops.
        /// </summary>
        void Use(ItemDef item)
        {
            if (item == null) return;
            if (!item.inRoom) { Say($"The {item.name} is in my bag. Take it out and I'll use it!", 4f); return; }
            if (!string.IsNullOrWhiteSpace(item.script)) return;
            string[] uses = item.uses ?? new string[0];
            if (Array.IndexOf(uses, ItemUses.Music) >= 0) { SetMusic(_w.song != null ? "stop" : ""); return; }
            string act = "";
            foreach (var a in Activities.Possible(_w.items)) if (a.name != "music" && Array.IndexOf(a.itemIds, item.id) >= 0) { act = a.name; break; }
            if (act.Length == 0) { Say($"I do like looking at the {item.name}.", 3f); return; }
            if (_w.doing == act && _w.doingSkill.Length == 0) { _w.doing = ""; Changed(); Say("Okay, I'll stop.", 2.5f); return; }
            Begin(act);
        }

        void Begin(string act)
        {
            _w.doingSkill = ""; _w.doing = act; _w.doingAt = _clock();
            Changed();
            Say(act == "study" ? "Okay, study time!" : act == "sit" ? "I'll sit down." : act == "nap" ? "I'll take a little nap." : act == "read" ? "I'll read for a bit." : "Yay, playtime!", 3f);
        }

        /// <summary>An activity needs its things on the desk: when one of them is put away, the activity is over.</summary>
        void CheckDoing()
        {
            if (_w.doing.Length > 0 && _w.doingSkill.Length == 0 && Activities.Find(_w.items, _w.doing) == null) _w.doing = "";
        }

        static readonly string[] Slots = { "handR", "handL", "head", "back" };

        /// <summary>wear: the headset's script put the thing on (it says where). Only one thing is worn in one place: what was there comes off.</summary>
        void Wear(ItemDef item, string slot)
        {
            if (item == null) return;
            string where = Array.IndexOf(Slots, slot ?? "") >= 0 ? slot : "handR";
            foreach (var other in _w.items) if (other != item && other.wornOn == where) other.wornOn = "";
            item.inRoom = true; item.wornOn = where; item.y = 0f; item.onId = "";
            Changed();
        }

        sealed class Intent { public string kind = "", text = ""; }

        /// <summary>act: a thing is asked for one of the acts its script offers. The headset runs the scripts: it is passed back to it (intent.result, kind act, "id|act").</summary>
        void Act(string what, string act)
        {
            if (what.Length == 0 && act.IndexOf('|') >= 0) { int bar = act.IndexOf('|'); what = act.Substring(0, bar).Trim(); act = act.Substring(bar + 1).Trim(); }
            var item = FindItem(what);
            if (item == null) foreach (var i in _w.items) if (!string.IsNullOrEmpty(i.wornOn)) { item = i; break; }
            if (item == null || act.Length == 0 || string.IsNullOrWhiteSpace(item.script)) return;
            if (!item.inRoom) { item.inRoom = true; Activities.Arrange(_w.items, item); Changed(); }
            _deliver(Msg.IntentResult, new Intent { kind = "act", text = item.id + "|" + act });
        }

        void SetAmbience(string what)
        {
            if (IsOff(what)) { if (_w.ambience != null) { _w.ambience = null; Changed(); } return; }
            var a = FindAmbience(what);
            if (a == null || (_w.ambience != null && _w.ambience.id == a.id)) return;
            _w.ambience = a;
            Changed();
            if (_w.doingSkill.Length == 0) Say("There, changed!", 2.5f);
        }

        void SetMusic(string what)
        {
            what = (what ?? "").Trim();
            if (what.Length > 0 && IsOff(what))
            {
                if (_w.song == null) return;
                _w.song = null;
                if (_w.doing == "music" && _w.doingSkill.Length == 0) _w.doing = "";
                Changed();
                return;
            }
            // no wish at all: the record that was on last, otherwise the quiet one to work to
            var s = what.Length == 0 ? (FindSong(_lastRecord) ?? FindSong("song-lofi") ?? (_w.songs.Count > 0 ? _w.songs[0] : null)) : FindSong(what);
            if (s == null || (_w.song != null && _w.song.id == s.id && _w.doingSkill.Length > 0)) return;
            _w.song = s; _w.songAt = _clock(); _lastRecord = s.id;
            Changed();
            if (_w.doingSkill.Length == 0) Say($"Here's {s.name}.", 2.5f);
        }

        void UseSkill(string what)
        {
            SkillDef k = null;
            foreach (var s in _w.skills) if (Same(s.id, what) || Same(s.name, what)) k = s;
            if (k == null) return;
            // (its script runs in the headset: on_use when world.doingSkill names it, on_stop when that ends)
            _w.doingSkill = k.id; _w.doing = "skill:" + k.id; _w.doingAt = _clock();
            Changed();
            Say(k.id == "skill-dance" ? "Let's dance!" : $"Okay, {k.name}!", 3f);
        }

        void Do(string what)
        {
            string act = (what ?? "").Trim().ToLowerInvariant();
            if (act == "stop") { Stop(); return; }
            if (act == "dance") { UseSkill("skill-dance"); return; }
            if (act != "study" && act != "sit" && act != "nap" && act != "read" && act != "play") return;
            if (Activities.Find(_w.items, act) == null) { Say("I need my things out on the desk for that.", 3.5f); return; }
            Begin(act);
        }

        /// <summary>Everything back to how it is by itself: nothing being done, no tune, the room as it is.</summary>
        void Stop()
        {
            bool was = _w.doing.Length > 0 || _w.song != null || _w.ambience != null;
            if (!was) return;
            _w.doing = ""; _w.doingSkill = ""; _w.song = null; _w.ambience = null;
            Changed();
            Say("Okay, everything's back to normal.", 3f);
        }

        // ------------------------------------------------------------------ games on the desk
        GamePackDef FindPack(string what)
        {
            what = (what ?? "").Trim();
            if (what.Length == 0) return null;
            foreach (var p in _w.games) if (Same(p.id, what) || Same(p.id, "game-" + what)) return p;
            foreach (var p in _w.games) if (Same(p.title, what)) return p;
            foreach (var p in _w.games) if ((p.title ?? "").IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0) return p;
            return null;
        }

        void ClearGlass()
        {
            // (everything but the run number: a late report of the old run then fits nothing)
            _g.game = ""; _g.packId = ""; _g.title = ""; _g.side = ""; _g.phase = "";
            _g.score = 0; _g.lives = 0; _g.level = 0; _g.best = 0; _g.seconds = 0;
            _g.choices = new string[0]; _g.choiceNotes = new string[0]; _g.banner = ""; _g.result = "";
            _overAt = -1;
        }

        /// <summary>The app hears at once which game is on (glass.state), and the next snapshot says the same.</summary>
        void TellGlass() { _deliver(Msg.GlassState, CopyGlass()); _dirty = true; }

        void OnGlass(GlassCommand c)
        {
            switch (c.cmd)
            {
                case "start":
                {
                    var pack = FindPack(c.game);
                    if (pack == null) { Say("I don't have a game like that yet.", 3f); return; }
                    if (!GlassGames.Is(pack.template)) return;
                    _court.Stop();
                    if (_g.game.Length > 0 && _g.packId == pack.id && _g.phase != "over") { Say("We are already playing!", 2.5f); return; }
                    // a game that is running gives way to the new one; it is always played out here, on the desk
                    _g.game = pack.template; _g.packId = pack.id; _g.title = pack.title ?? ""; _g.side = Env.Mr;
                    _g.runId++;
                    _g.phase = "intro"; _g.score = 0; _g.lives = Math.Max(1, pack.lives); _g.level = 1; _g.seconds = 0;
                    _g.best = _best.TryGetValue(pack.id, out int b) ? b : 0;
                    _g.choices = new string[0]; _g.choiceNotes = new string[0]; _g.banner = ""; _g.result = "";
                    _overAt = -1; _lastReport = _clock();
                    TellGlass();
                    Say($"Okay, let's play {pack.title}!", 3f);
                    break;
                }
                case "stop": if (_g.game.Length > 0) { ClearGlass(); TellGlass(); } break;
                case "choose":
                    // (a card picked by something other than the hands: handed to the game, which runs in the app)
                    if (_g.game.Length > 0 && _overAt < 0) _deliver(Msg.GlassCommand, new GlassCommand { cmd = "choose", game = _g.packId, pick = Math.Max(0, Math.Min(2, c.pick)), how = c.how ?? "" });
                    break;
            }
        }

        /// <summary>glass.state from the app, which runs the game: taken over for the snapshot. Only about the run that is on, and only until it is over.</summary>
        void OnGlassReport(GlassState r)
        {
            if (_g.game.Length == 0 || r.runId != _g.runId || _g.phase == "over") return;
            string phase = r.phase ?? "";
            if (phase != "intro" && phase != "play" && phase != "choose" && phase != "over") return;
            bool changed = _g.phase != phase;
            _g.phase = phase; _g.score = Math.Max(0, r.score); _g.lives = Math.Max(0, r.lives); _g.level = Math.Max(0, r.level);
            if (!float.IsNaN(r.seconds) && !float.IsInfinity(r.seconds)) _g.seconds = Math.Max(0f, r.seconds);
            _g.choices = r.choices ?? new string[0]; _g.choiceNotes = r.choiceNotes ?? new string[0]; _g.banner = r.banner ?? ""; _g.result = r.result ?? "";
            _lastReport = _clock();
            if (!changed) return;
            if (phase == "over")
            {
                _overAt = _lastReport;
                bool best = _g.score > 0 && (!_best.TryGetValue(_g.packId, out int had) || had < _g.score);
                if (best) { _best[_g.packId] = _g.score; _saveAt = _lastReport + SaveAfter; }
                _g.best = _best.TryGetValue(_g.packId, out int now) ? now : 0;
                Say(!string.IsNullOrWhiteSpace(_g.result) ? _g.result.Trim() : $"That's it! {_g.score} points{(_g.level > 1 ? $", up to level {_g.level}" : "")}.{(best ? " A new best!" : "")}", 5f);
            }
            _dirty = true;      // the snapshot says the new phase at once
        }

        /// <summary>The best score kept for a pack (0 = none).</summary>
        public int Best(string packId) => _best.TryGetValue(packId ?? "", out int b) ? b : 0;
        /// <summary>The state in one line, for the log.</summary>
        public string Line()
        {
            int out_ = 0; string worn = "";
            foreach (var i in _w.items) { if (i.inRoom) out_++; if (!string.IsNullOrEmpty(i.wornOn)) worn += (worn.Length > 0 ? ", " : "") + i.name + " on " + i.wornOn; }
            return $"revision {_w.revision}, {_w.items.Count} things ({out_} out on the desk{(worn.Length > 0 ? "; worn: " + worn : "")}), {_w.games.Count} games, {_w.skills.Count} skill(s), {_w.ambiences.Count} places, {_w.songs.Count} records; "
                 + $"place {(_w.ambience != null ? _w.ambience.name : "-")}, tune {(_w.song != null ? _w.song.name : "-")}, doing '{_w.doing}'; game {(_g.game.Length > 0 ? _g.title + " run " + _g.runId + " " + _g.phase + " score " + _g.score : "-")}; "
                 + $"{Commands} command(s), {_seq} snapshot(s), {Saves} save(s){(SaveError.Length > 0 ? ", SAVE ERROR: " + SaveError : "")}";
        }
    }
}
