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
        if (Matches(words, "뱀파이어|서바이벌|뱀서|vampire|surviv(al|ors)")) return ("survival", "뱀파이어 나이트");
        if (Matches(words, "골키퍼|축구|승부차기|goalie|goalkeeper|football|soccer|penalty")) return ("keeper", "유리 너머 승부차기");
        if (Matches(words, "슈팅|사격|총싸움|손가락총|shooter|shooting|finger gun")) return ("shooter", "분신 슈팅");
        if (Matches(words, "오목|omok|five in a row")) return ("omok", "오목");
        if (Matches(words, "기억력|카드 짝|memory|matching pairs")) return ("memory", "짝 맞추기");
        if (Matches(words, "가위바위보|rock paper scissors")) return ("rps", "가위바위보");
        return ("", "");
    }

    static bool Matches(string words, string pattern) => Regex.IsMatch(words, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    bool TryCreateSpoken(string said, out string response)
    {
        response = "";
        string words = WorldRules.TypedText(NameRules.Strip(said ?? ""));
        if (SightRules.Plain(words) == SightRules.Make)
        {
            response = headset == null ? "보여 주는 물건을 보려면 먼저 헤드셋을 연결해 줘." :
                QueueWork("model", "보여 준 물건의 3D 모델", words, (job, ct) => MakeSculpture(job, words, ct, true));
            return true;
        }
        if (Matches(words, @"(?:방금|지금|아까)\s*만든.*(?:게임.*(?:하자|할래|시작|켜)|플레이|해\s*보자)|play (?:my|the) (?:new|last|latest) game"))
        {
            var made = bag.World.games.LastOrDefault(g => IsCreationId(g.id, "game"));
            if (made == null) response = "아직 만든 게임이 없어.";
            else { string error = GameButton(made.id); response = error.Length == 0 ? made.title + " 시작할게." : error; }
            return true;
        }
        if (Matches(words, @"하자|켜\s*줘|플레이|시작|\bplay\b") && !Matches(words, "만들어|제작해|\\bmake\\b"))
        {
            var made = bag.World.games.LastOrDefault(g => IsCreationId(g.id, "game") && words.Contains(g.title, StringComparison.OrdinalIgnoreCase));
            if (made != null) { string error = GameButton(made.id); response = error.Length == 0 ? made.title + " 시작할게." : error; return true; }
        }
        if (!Matches(words, @"만들어|만들자|만들기|제작해|제작하자|빚어|조각해|\b(make|create|build|sculpt|model)\b") ||
            Matches(words, @"만들지\s*마|만들어?\s*(놓)?지\s*마|don't\s+(make|create|build)|문서|보고서|로고|아이콘|이미지|그림|사진으로\s*저장|\b(pdf|png|document|report|logo|icon|illustration)\b")) return false;
        bool game = Matches(words, @"게임|슈팅|승부차기|뱀파이어|오목|가위바위보|\b(game|shooter|goalkeeper|survivors)\b");
        if (!game && Matches(words, @"타이머|알람|노래|음악|스킬|기술|페이지|\b(timer|alarm|song|skill|website|page)\b")) return false;
        string name = game ? GamePattern(words).name : WorldRules.RoughName(words, ItemKinds.Guess(words));
        if (name.Length == 0) name = game ? "새 게임" : "3D 오브젝트";
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
                    throw new InvalidOperationException("새 게임을 설계하려면 Mac 키체인 LivingPartner.Anthropic 또는 ANTHROPIC_API_KEY에 Anthropic API 키를 설정해 줘. 기본 게임은 바로 플레이할 수 있어.");
                WorkStep(job, "기본 게임 레시피 준비", .2f);
                pack = GlassScriptExamples.Pack(example);
                if (korean) pack.title = koreanTitle;
            }
            else
            {
                WorkStep(job, attempt == 0 ? "게임 규칙과 동작 작성" : "플레이 테스트 결과 반영", attempt == 0 ? .15f : .65f);
                string brief = "Design a playable MR game for the user's request in the supplied Glass Lua API. Give it a distinct title and concise controls in the user's language. " +
                    "Support desk play and a calibrated TV. For a shooter, make aim and shots hit moving enemies and send enemy shots physically across the glass with shoot_out and out_events. " +
                    "For a goalkeeper game use a football mesh inside, shoot_out football into the room and visibly carry the blocked return to the kicker inside. " +
                    "For horde survival use 2D movement, autoattacks, experience gems and meaningful selectable upgrades. Implement the user's particular rules. " +
                    "Use only built-in art. Keep source under 24000 characters.\n" + GlassScriptApi.Doc;
                if (example != null) brief += "\nWorking reference you may adapt:\n" + example.lua;
                string ask = repair.Length == 0 ? words : words + "\nFix this previous play-test failure:\n" + repair + "\nPrevious Lua:\n" + pack.script;
                var result = await model.AskJsonAsync(brief, ask,
                    CreationSchema(("title", SchemaType("string")), ("howTo", SchemaType("string")), ("lua", SchemaType("string"))), 12000, ct);
                pack = new GamePackDef { template = GlassScript.TemplateName, title = WorldRules.Text(result["title"], 40, "새 게임"),
                    howTo = WorldRules.Text(result["howTo"], 160), script = (string)result["lua"] ?? "" };
            }
            ct.ThrowIfCancellationRequested();
            WorkStep(job, "손 입력과 TV 화면 왕복 플레이 테스트", attempt == 0 ? .46f : .82f);
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
            if (model == null || attempt > 0) throw new InvalidOperationException("게임의 플레이 테스트를 통과하지 못했어. " + WorldRules.Short(repair, 170));
        }
        pack.id = CreationId("game");
        WorkStep(job, "검증한 게임을 가방에 저장", .95f);
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

    async Task<Action> MakeSculpture(WorkJob job, string words, CancellationToken ct, bool fromSight = false)
    {
        var kind = ItemKinds.Guess(words);
        string name = WorldRules.RoughName(words, kind);
        var model = Model;
        ItemIdea seen = null;
        JObject answer;
        if (fromSight)
        {
            if (model == null) throw new InvalidOperationException("보여 준 물건을 알아보려면 Mac에 OpenAI API 키를 설정해 줘.");
            WorkStep(job, "헤드셋으로 보여 준 물건 한 장 촬영", .08f);
            var photo = await TakeObjectPhoto(job.id, ct);
            var attachments = new[] { ("image/jpeg", photo, "held-object.jpg") };
            WorkStep(job, "물건의 형태와 크기 확인", .24f);
            string[] kinds = ItemKinds.All.Select(k => k.id).ToArray();
            var found = await model.AskJsonAsync(SightRules.SeeSystem,
                SightRules.SeeAsk(words, Matches(words, "[가-힣]") ? "Korean" : "English", kinds, ItemUses.All),
                attachments, SightRules.SeeShape(kinds, ItemUses.All), 1600, ct);
            string what = WorldRules.Text(found["what"], 80);
            if ((bool?)found["found"] != true || what.Length == 0) throw new InvalidOperationException("손에 든 물건을 알아보지 못했어. 물건을 가운데 보여 주고 다시 부탁해 줘.");
            seen = WorldRules.Item(found, ItemKinds.Guess(what));
            kind = ItemKinds.Find(seen.kind);
            name = SightRules.Title(seen.name, what);
            WorkStep(job, "사진에 보이는 부분을 3D로 조각", .39f);
            answer = await model.AskJsonAsync(SculptureDesign.Brief + "\nInclude a short object title in the user's language.",
                SightRules.SculptNote(what, seen.modelPrompt), attachments, ShapeSchema(), 12000, ct);
        }
        else if (WorldRules.IsFirewood(name) || WorldRules.IsFirewood(words))
        {
            name = "장작";
            answer = WorldRules.FirewoodShape();
        }
        else if (model == null)
        {
            int pattern = kind.id switch { "chair" => 0, "lamp" => 1, "plant" => 2, _ => -1 };
            if (pattern < 0) throw new InvalidOperationException("이 물건을 새로 조각하려면 Mac에 OpenAI API 키를 설정해 주세요. 기본 레시피는 의자, 램프, 화분이 있어요.");
            WorkStep(job, "기본 3D 레시피 조립", .25f);
            answer = JObject.Parse(WorldRules.SculptExamples[pattern].answer);
        }
        else
        {
            WorkStep(job, "형태와 재질 설계", .15f);
            answer = await model.AskJsonAsync(SculptureDesign.Brief + "\nAlso include a short object title in the user's language.",
                words, Array.Empty<(string, byte[], string)>(), ShapeSchema(), 12000, ct);
            name = WorldRules.Text(answer["title"], 36, name);
        }
        ct.ThrowIfCancellationRequested();
        WorkStep(job, "3D 부품 결합", .55f);
        var sculpture = WorldRules.Sculpt(answer, out var why) ?? throw new InvalidDataException("3D 형상을 읽을 수 없어요. " + why);
        WorldRules.Join(sculpture);
        if (sculpture.loose.Count != 0) throw new InvalidDataException("연결되지 않은 부품이 남아 있어요.");
        string id = CreationId("item");
        var item = new ItemDef { id = id, name = name, asked = words, kind = kind.id, uses = seen?.uses ?? kind.uses, sizeM = seen?.sizeM ?? Math.Max(WorldRules.MadeSizeMin, kind.sizeM),
            color = seen?.color ?? kind.color, note = seen?.note ?? "", status = "ready", progress = 1, model = id + ".glb", parts = sculpture.parts, seat = sculpture.seat, top = sculpture.top,
            madeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), script = RoomScriptExamples.ForKind(kind.id) ?? "" };
        WorkStep(job, "GLB 메시와 재질 검사", .81f);
        var glb = GlbWriter.Write(PartMesh.Build(item.parts), item.name, seen == null ? item.sizeM : SightRules.Metres(seen.realCm));
        if (GlbWriter.Check(glb, out why) == null) throw new InvalidDataException(why);
        return () =>
        {
            SaveCreation(new Creation { item = item }, glb, job, ct);
            if (fromSight)
            {
                try { job.note = Path.GetRelativePath(DocumentRoot, ExportToDocuments(item, GeneratedModelFile(item.id))); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { job.note = "가방에 저장했어. 문서 폴더에 쓰지 못해서 대기줄에 GLB 다운로드를 뒀어."; }
            }
        };
    }

    static string CreationId(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..12];
    static bool IsCreationId(string id, string prefix) => id != null && Regex.IsMatch(id, "^" + prefix + "-[0-9a-f]{12}\\z");

    void SaveCreation(Creation asset, byte[] glb, WorkJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (asset.item != null && bag.World.items.Count >= WorldRules.MaxItems) throw new InvalidOperationException("가방의 물건 칸이 가득 찼어요.");
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
        else { asset.item.inRoom = false; asset.item.wornOn = asset.item.onId = ""; bag.World.items.Add(asset.item); }
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
