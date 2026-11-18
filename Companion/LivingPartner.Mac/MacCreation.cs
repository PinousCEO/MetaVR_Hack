using System.Net;
using System.Text.RegularExpressions;
using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Mac;

sealed partial class Stage
{
    sealed class Creation
    {
        public GamePackDef game;
        public ItemDef item;
    }

    string CreationsDir => Path.Combine(dataDir, "creations");
    ClaudeGameClient gameModel;

    static (string id, string name) GamePattern(string words)
    {
        if (Matches(words, "뱀파이어|서바이벌|뱀서|vampire|surviv(al|ors)")) return ("survival", "Vampire Night");
        if (Matches(words, "골키퍼|축구|승부차기|goalie|goalkeeper|football|soccer|penalty")) return ("keeper", "Penalty Kick");
        if (Matches(words, "슈팅|사격|총싸움|손가락총|shooter|shooting|finger gun")) return ("shooter", "Mirror Shot");
        if (Matches(words, "오목|omok|five in a row")) return ("omok", "Gomoku");
        if (Matches(words, "기억력|카드 짝|memory|matching pairs")) return ("memory", "Matching Pairs");
        if (Matches(words, "가위바위보|rock paper scissors")) return ("rps", "Rock Paper Scissors");
        return ("", "");
    }

    static bool Matches(string words, string pattern) => Regex.IsMatch(words, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    bool TryCreateSpoken(string said, out string response)
    {
        response = "";
        string words = WorldRules.TypedText(NameRules.Strip(said ?? ""));
        if (StockChart.CreateRequest(words))
        {
            response = QueueWork("tool", "Stock chart", words, (job,ct) => MakeGame(job,words,ct));
            return true;
        }
        if (StockChart.OpenRequest(words))
        {
            var chart = StockChart.Find(bag.World.games, words);
            response = chart == null ? "Make a stock chart first." : GameButton(chart.id);
            if (response.Length == 0) response = "Opening your chart.";
            return true;
        }
        bool identify = SightRules.WantsIdentification(words);
        if (identify || SightRules.Plain(words) == SightRules.Make)
        {
            response = headset == null ? "Connect your headset to show me an object." :
                QueueWork(identify ? "look" : "model", identify ? "Identify room object" : "Copy room object", words, (job, ct) => CopyShownObject(job, words, identify, ct));
            return true;
        }
        if (Matches(words, @"(?:방금|지금|아까)\s*만든.*(?:게임.*(?:하자|할래|시작|켜)|플레이|해\s*보자)|play (?:my|the) (?:new|last|latest) game"))
        {
            var made = bag.World.games.LastOrDefault(g => IsCreationId(g.id, "game"));
            if (made == null) response = "You haven’t made a game yet.";
            else { string error = GameButton(made.id); response = error.Length == 0 ? made.title + " is starting." : error; }
            return true;
        }
        if (Matches(words, @"하자|켜\s*줘|플레이|시작|\bplay\b") && !Matches(words, "만들어|제작해|\\bmake\\b"))
        {
            var made = bag.World.games.LastOrDefault(g => IsCreationId(g.id, "game") && words.Contains(g.title, StringComparison.OrdinalIgnoreCase));
            if (made != null) { string error = GameButton(made.id); response = error.Length == 0 ? made.title + " is starting." : error; return true; }
        }
        if (!Matches(words, @"만들어|만들자|만들기|제작해|제작하자|빚어|조각해|\b(make|create|build|sculpt|model)\b") ||
            Matches(words, @"만들지\s*마|만들어?\s*(놓)?지\s*마|don't\s+(make|create|build)|문서|보고서|로고|아이콘|이미지|그림|사진으로\s*저장|\b(pdf|png|document|report|logo|icon|illustration)\b")) return false;
        bool game = Matches(words, @"게임|슈팅|승부차기|뱀파이어|오목|가위바위보|\b(game|shooter|goalkeeper|survivors)\b");
        if (!game && Matches(words, @"타이머|알람|노래|음악|스킬|기술|페이지|\b(timer|alarm|song|skill|website|page)\b")) return false;
        string name = game ? GamePattern(words).name : WorldRules.RoughName(words, ItemKinds.Guess(words));
        if (name.Length == 0) name = game ? "New game" : "3D object";
        response = QueueWork(game ? "game" : "item", name, words, (job, ct) => game ? MakeGame(job, words, ct) : MakeSculpture(job, words, ct));
        return true;
    }

    static JObject SchemaType(string type) => new() { ["type"] = type };
    static JObject CreationSchema(params (string key, JObject value)[] fields) => new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["required"] = new JArray(fields.Select(f => f.key)),
        ["properties"] = new JObject(fields.Select(f => new JProperty(f.key, f.value)))
    };

    async Task<Action> MakeGame(WorkJob job, string words, CancellationToken ct)
    {
        if (StockChart.IsChart(words))
        {
            WorkStep(job, "Loading closing prices", .30f);
            var chart = await StockChart.MakeAsync(words, ct);
            chart.id = CreationId("game");
            return () => SaveCreation(new Creation { game = chart }, null, job, ct);
        }
        var (genre, koreanTitle) = GamePattern(words);
        var example = GlassScriptExamples.Find(genre);
        bool useRecipe = example != null && Matches(words, @"기본|내장|레시피|템플릿|\b(default|built.in|recipe|template)\b");
        var model = useRecipe ? null : gameModel ??= ClaudeGameClient.Open();
        bool korean = Matches(words, "[가-힣]");
        GamePackDef pack = null;
        string repair = "";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (model == null)
            {
                if (!useRecipe)
                    throw new InvalidOperationException("To design a new game, set an Anthropic API key in the Mac keychain (LivingPartner.Anthropic) or ANTHROPIC_API_KEY. The built-in games are ready to play.");
                WorkStep(job, "Preparing game rules", .2f);
                pack = GlassScriptExamples.Pack(example);
                if (korean) pack.title = koreanTitle;
            }
            else
            {
                WorkStep(job, attempt == 0 ? "Writing actions and rules" : "Applying playtest results", attempt == 0 ? .15f : .65f);
                string brief = "Design a playable MR game for the user's request in the supplied Glass Lua API. Give it a distinct title and concise controls in the user's language. " +
                    "Support desk play and a calibrated TV. For a shooter, make aim and shots hit moving enemies and send enemy shots physically across the glass with shoot_out and out_events. " +
                    "For a goalkeeper game use a football mesh inside, shoot_out football into the room and visibly carry the blocked return to the kicker inside. " +
                    "For horde survival use 2D movement, autoattacks, experience gems and meaningful selectable upgrades. Implement the user's particular rules. " +
                    "Use only built-in art. Keep source under 24000 characters.\n" + GlassScriptApi.Doc;
                if (example != null) brief += "\nWorking reference you may adapt:\n" + example.lua;
                string ask = repair.Length == 0 ? words : words + "\nFix this previous play-test failure:\n" + repair + "\nPrevious Lua:\n" + pack.script;
                var result = await model.AskJsonAsync(brief, ask,
                    CreationSchema(("title", SchemaType("string")), ("howTo", SchemaType("string")), ("lua", SchemaType("string"))), 12000, ct);
                pack = new GamePackDef { template = GlassScript.TemplateName, title = WorldRules.Text(result["title"], 40, "New game"),
                    howTo = WorldRules.Text(result["howTo"], 160), script = (string)result["lua"] ?? "" };
            }
            ct.ThrowIfCancellationRequested();
            WorkStep(job, "Testing hand input and TV interaction", attempt == 0 ? .46f : .82f);
            repair = "";
            foreach (int seed in new[] { 1, 19 })
            {
                ct.ThrowIfCancellationRequested();
                var report = GlassScriptCheck.Run(pack.script, pack, 40f, seed);
                if (!report.Clean) { repair = report.Summary(); break; }
                if (genre is "keeper" or "shooter" && (!report.usedOuts || !report.readOutEvents || report.outs < 1))
                    repair = "No returnable shot crossed the glass during play.";
                if (genre == "shooter" && (!report.readShots || !report.readAim))
                    repair = "Pointing and firing must interact with moving targets.";
                if (genre == "keeper" && !Regex.IsMatch(pack.script, "[\x22'](?:pack:)?(?:football|soccerball)[\x22']", RegexOptions.IgnoreCase))
                    repair = "Use the 3D football for the visible kick, incoming ball and returned ball.";
                if (repair.Length > 0) break;
            }
            if (repair.Length == 0) break;
            if (model == null || attempt > 0) throw new InvalidOperationException("The game did not pass its playtest. " + WorldRules.Short(repair, 170));
        }
        pack.id = CreationId("game");
        WorkStep(job, "Saving the tested game to your bag", .95f);
        return () => SaveCreation(new Creation { game = pack }, null, job, ct);
    }

    static JObject ShapeSchema()
    {
        var fields = new List<(string, JObject)> { ("name", SchemaType("string")), ("shape", new JObject { ["type"] = "string", ["enum"] = new JArray(ShapePart.Shapes) }) };
        foreach (string value in "x y z sx sy sz rx ry rz round".Split(' ')) fields.Add((value, SchemaType("number")));
        fields.Add(("color", SchemaType("string")));
        return CreationSchema(("title", SchemaType("string")), ("plan", SchemaType("string")), ("parts", new JObject { ["type"] = "array", ["items"] = CreationSchema(fields.ToArray()) }),
            ("seat", SchemaType("number")), ("top", SchemaType("number")));
    }

    async Task<Action> MakeSculpture(WorkJob job, string words, CancellationToken ct)
    {
        var kind = ItemKinds.Guess(words);
        string name = WorldRules.RoughName(words, kind);
        var model = Model;
        JObject answer;
        if (WorldRules.IsFirewood(name) || WorldRules.IsFirewood(words))
        {
            name = "Firewood";
            answer = WorldRules.FirewoodShape();
        }
        else if (model == null)
        {
            int pattern = kind.id switch { "chair" => 0, "lamp" => 1, "plant" => 2, _ => -1 };
            if (pattern < 0) throw new InvalidOperationException("Set an OpenAI key on your Mac to sculpt this. Chair, lamp and planter recipes are available offline.");
            WorkStep(job, "Assembling the 3D model", .25f);
            answer = JObject.Parse(WorldRules.SculptExamples[pattern].answer);
        }
        else
        {
            WorkStep(job, "Shaping the materials", .15f);
            answer = await model.AskJsonAsync(SculptureDesign.Brief + "\nAlso include a short English object title.",
                words, Array.Empty<(string, byte[], string)>(), ShapeSchema(), 12000, ct);
            name = WorldRules.Text(answer["title"], 36, name);
        }
        ct.ThrowIfCancellationRequested();
        WorkStep(job, "Joining the parts", .55f);
        var sculpture = WorldRules.Sculpt(answer, out var why) ?? throw new InvalidDataException("Unable to read the model. " + why);
        WorldRules.Join(sculpture);
        if (sculpture.loose.Count != 0) throw new InvalidDataException("Some parts are disconnected.");
        string id = CreationId("item");
        var item = new ItemDef { id = id, name = WorldRules.EnglishItemName(name, kind.id), asked = words, kind = kind.id, uses = kind.uses, sizeM = Math.Max(WorldRules.MadeSizeMin, kind.sizeM),
            color = kind.color, note = "", status = "ready", progress = 1, model = id + ".glb", parts = sculpture.parts, seat = sculpture.seat, top = sculpture.top,
            madeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), script = RoomScriptExamples.ForKind(kind.id) ?? "" };
        WorkStep(job, "Checking mesh and materials", .81f);
        var glb = GlbWriter.Write(PartMesh.Build(item.parts), item.name, item.sizeM);
        if (GlbWriter.Check(glb, out why) == null) throw new InvalidDataException(why);
        return () => SaveCreation(new Creation { item = item }, glb, job, ct);
    }

    static string CreationId(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..12];
    static bool IsCreationId(string id, string prefix) => id != null && Regex.IsMatch(id, "^" + prefix + "-[0-9a-f]{12}\\z");

    void SaveCreation(Creation asset, byte[] glb, WorkJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (asset.item != null && bag.World.items.Count >= WorldRules.MaxItems) throw new InvalidOperationException("Your bag is full.");
        string id = asset.item?.id ?? asset.game.id;
        Directory.CreateDirectory(CreationsDir);
        string tmp = Path.Combine(CreationsDir, ".new-" + id);
        Directory.CreateDirectory(tmp);
        try
        {
            if (glb != null) File.WriteAllBytes(Path.Combine(tmp, asset.item.model), glb);
            File.WriteAllText(Path.Combine(tmp, "bag-entry.json"), Proto.ToJson(asset));
            ct.ThrowIfCancellationRequested();
            Directory.Move(tmp, Path.Combine(CreationsDir, id));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        AddCreation(asset);
        job.resultKind = asset.item == null ? "game" : "item";
        job.resultId = id;
    }

    void AddCreation(Creation asset)
    {
        if (asset.game != null) bag.World.games.Add(asset.game);
        else { WorldRules.EnglishLabel(asset.item); asset.item.inRoom = false; asset.item.wornOn = asset.item.onId = ""; bag.World.items.Add(asset.item); }
        bag.NotifyInventoryChanged();
    }

    void RestoreCreations()
    {
        if (!Directory.Exists(CreationsDir)) return;
        foreach (string dir in Directory.EnumerateDirectories(CreationsDir).OrderBy(Directory.GetCreationTimeUtc).ThenBy(d => d, StringComparer.Ordinal))
        {
            string id = Path.GetFileName(dir);
            if (!Matches(id, @"^(item|game)-[0-9a-f]{12}\z")) continue;
            try
            {
                string entry = Path.Combine(dir, "bag-entry.json");
                if (!File.Exists(entry) || new FileInfo(entry).Length > 512000) continue;
                var a = Proto.FromJson<Creation>(File.ReadAllText(entry));
                if (a?.item != null && a.game == null && a.item.id == id && id.StartsWith("item-", StringComparison.Ordinal) &&
                    a.item.model == id + ".glb" && File.Exists(Path.Combine(dir, a.item.model))) AddCreation(a);
                else if (a?.game != null && a.item == null && a.game.id == id && id.StartsWith("game-", StringComparison.Ordinal) &&
                    a.game.template == GlassScript.TemplateName && a.game.script?.Length is > 0 and <= LuaSandbox.MaxSource) AddCreation(a);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public IResult WorldFile(IPAddress remote, string filename)
    {
        if (!MayFetch(remote)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!WorldRules.SafeFile(filename)) return Results.NotFound();
        var model = GeneratedModelFile(Path.GetFileNameWithoutExtension(filename));
        return model != null && Path.GetFileName(model.path) == filename ? Results.File(model.path, "model/gltf-binary", enableRangeProcessing: true) : Results.NotFound();
    }

    DocFile GeneratedModelFile(string id)
    {
        if (!IsCreationId(id, "item")) return null;
        var item = bag.World.items.FirstOrDefault(i => i.id == id && i.model == id + ".glb");
        if (item == null) return null;
        string path = Path.Combine(CreationsDir, id, item.model);
        return File.Exists(path) ? new DocFile { name = DocsRules.SafeTitle(item.name, id) + ".glb", path = path, kind = "glb" } : null;
    }
}
