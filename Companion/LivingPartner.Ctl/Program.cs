using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

// lpctl: local diagnostic/automation tool for the Companion Host (loopback only, authenticated with the local token).
//   lpctl status
//   lpctl enter <px> <py> | return <px> <py> | loop <n> <px> <py> | stoploop
//   lpctl freeze <0..1> | play | slow <rate> | rewind | overlay <0|1>
//   lpctl utter "<text>" | owner <mr|pc> | debug <cmd> [value] [px] [py] [text]
//   lpctl listen                   (the partner starts listening on the microphone of the side it is on)
//   lpctl tennis [start|stop]      (the MR client starts or stops the tennis game)
//   lpctl voicefile <wav path>     (the MR client sends this recording as if it had been spoken; labelled test-file)
//   lpctl pointer                  (the newest pointing sample the host received from the MR client)
//   lpctl allow-project <path> <name> <titleContains> <previewFile> <allowPush:0|1> [test command...]
//   lpctl task <analyze|select|approvePublish|cancel|preview|analyzeOnArrival|readOnArrival|dismiss> [taskId] [moduleId] [prompt]
//   lpctl review [px py] ["words"]   (look at the window at that pixel - or the partner's own window - and save feedback + a revised version)
//   lpctl look [px py] ["words"]     (look at the display under that pixel, at that spot, and answer; 0 0 = where the hand / gaze last was, else the partner's display)
//   lpctl readfile <path> ["words"]  (read a PDF / picture / text file and save notes + a revised version)
//   lpctl module <create|start|pause|resume|reset|setDuration|setDisplay|setAlert|delete> [durationMs] [value]
//   lpctl world                    (the partner's things, skills, surroundings, tune and what it is doing)
//   lpctl voice <file.wav> [test-file|microphone-open]   (a recording sent as a voice clip; `lpctl status` shows what the host heard and made of it)
//   lpctl world make "<text>" | teach "<text>" | skill "<name>" | do <dance|study|sit|nap|read|play|stop>
//   lpctl world use <id|name>      (a thing on the desk is pressed: the partner goes and uses it, again = it stops; a name that is no thing's is a skill's)
//   lpctl world ambience <id|name|"text"|clear>   (surroundings the user owns - see `lpctl world` - are switched on at once; clear = the room as it is)
//   lpctl world music <id|name|"text"|stop>       (a record of the record player - see `lpctl world`; no words = any; stop = off)
//   lpctl world place <id|name> [x z yaw [y onId]] | store <id|name> | delete <id|name> | forget <skill> | stop
//                                  (y onId: the thing is put ON another thing, y metres above the desk)
//   lpctl world redraw <id|name>   (draw the picture of a thing, a skill or a game pack again - needs an OpenAI key on the host)
//                                  (add --watch <seconds> to see what the partner says and how the world changes meanwhile)
//   lpctl market                   (the marketplace: the user's nickname and stars on the hub, what is on sale and by whom, what is theirs)
//   lpctl buy <name|id> | install <name|id> | remove <name|id>      (buy = install: what is not the user's yet is paid for on the hub)
//   lpctl publish <name|id> <price> | price <name|id> <stars> | unpublish <name|id>      (selling the user's own things)
//   lpctl like <name|id> | nickname "<name>" | export <name|id> | import <path of a .yonder file>
//   lpctl hub [<address>|default]  (which hub the host talks to; without an argument: where it is and whether it answers)
//   lpctl hub-as <nickname> buy|like "<listing name>" | publish-sample [price] | me
//                                  (TEST HELPER: acts as ANOTHER account straight against the hub, so that selling to
//                                   "someone else" can be shown on one PC. Needs no host. The hub: LP_MARKET_HUB, else this PC's.)
//   lpctl make "<text>" [item|skill|game|ambience|ability]   (a new module from a description; the kind is worked out when not given)
//                                  (a game or app is WRITTEN by the partner as a script: add --watch 300 to see "Writing the code", "Trying it out", "Fixing a bug (2)", "Painting the pictures")
//   lpctl remake <game id|name> ["<the request>"]   (one of the user's own games is written again by the writer, under the id, name and listing it has)
//   lpctl script check <file.lua> [game|thing|skill]   (RUNS a script in the checker, as the host does before it accepts one; needs no host. Exit 0 = it runs and is not dead)
//   lpctl script show <id|name>    (the script of a game, a thing or a skill the partner has - what the module is made of)
//   lpctl world wear <id|name> [handR|handL|head|back] | unwear <id|name> | act <id|name> <act>   (a thing with a script: put on, taken off, asked for one of the acts it offers)
//   lpctl say "<text>"             (type something to the partner, as in the box on the shop's page; `utter` is the same)
//   lpctl offer yes [price|free] | no     (the answer to the partner's "I made it - shall I put it up in the marketplace?"; see market.offerId)
//   lpctl open [pc|mr|close]       (the shop: the page in the browser, or the board in the headset)
//   lpctl screen <1|2|3|this|close> [analyze]   (bring a display of the PC into the room as a board)
//   lpctl song export ["<description>"]         (save the tune that is playing - or one written to the description - as a WAV file)
//   lpctl glass                    (the glass game that is on; `lpctl status` shows it too)
//   lpctl glass start <game> | stop | choose <0-2>     (game: a pack's id, its title, a word of it or its way of playing - "Vampire Night", survival, umbrella)
//   lpctl glass hand <monitorId|-> <u> <v> <dist>      (TEST: one report of the right hand in front of that display, through the host to the PC client)
//   lpctl doc                      (the documents the partner made in this sitting: reports as PDF, design options as PNG + PDF)
//   lpctl doc report "<topic>"     (research the topic and make a PDF of it; add --watch 240 to see the stages)
//   lpctl doc designs <px> <py> ["words"]   (four new versions of the icon / design at that pixel of the desktop)
//   lpctl doc point <px> <py>      (TEST: that pixel stands for the pointing hand for the next sentence - `lpctl say "이 아이콘 새로 만들어 줘"`)
//   lpctl doc open [id] | close | page <n|next|prev> | pick <n> [id] | deliver [id] | reveal [id] | delete <id>
//   lpctl watch <seconds>          (print events)
//   lpctl wait <seconds>           (wait until no transition is active, print the final state)
internal static class Program
{
    static ClientWebSocket _ws;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0) { Console.WriteLine("usage: lpctl <command> ... (see source header)"); return 2; }
        // the test helper talks to the hub, not to the host: it works with no host running
        if (args[0] == "hub-as") return await HubAs(args);
        // ... and the checker of scripts runs here, in this process: no host is needed for it either
        if (args[0] == "script" && args.Length > 1 && args[1] == "check") return ScriptCheck(args);
        // (LP_HOME: the data folder of a trial host that runs next to the real one - the same variable the host reads)
        string root = Environment.GetEnvironmentVariable("LP_HOME") is { Length: > 0 } home ? Path.GetFullPath(home) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LivingAIPartner");
        string tokenFile = Path.Combine(root, "local.token");
        if (!File.Exists(tokenFile)) { Console.WriteLine("host token not found - is the host installed/running?"); return 3; }
        string token = File.ReadAllText(tokenFile).Trim();
        string port = Environment.GetEnvironmentVariable("LP_PORT") ?? Proto.DefaultPort.ToString();
        _ws = new ClientWebSocket();
        try { await _ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), new CancellationTokenSource(3000).Token); }
        catch (Exception e) { Console.WriteLine("cannot connect to host: " + e.Message); return 4; }
        await Send(Msg.Hello, new Hello { role = Roles.Tool, clientId = "lpctl-" + Guid.NewGuid().ToString("N")[..6], deviceName = "lpctl", token = token });
        StateSnapshot snap = null;
        var first = await ReadUntil(e => { if (e.type == Msg.Snapshot) { snap = e.Body<StateSnapshot>(); return true; } return e.type == Msg.Reject; }, 4);
        if (snap == null) { Console.WriteLine("rejected by host"); return 5; }
        var sinceSnapshot = System.Diagnostics.Stopwatch.StartNew();       // (host time now = snap.hostTime + this)

        float F(int i, float d = 0) => args.Length > i ? float.Parse(args[i], Inv) : d;
        float Num(int i) => args.Length > i && float.TryParse(args[i], System.Globalization.NumberStyles.Float, Inv, out float v) ? v : 0;      // (an option such as --watch in that place is simply not a number)
        string S(int i, string d = "") => args.Length > i ? args[i] : d;
        switch (args[0])
        {
            case "status": PrintStatus(snap); break;
            case "snapshot": Console.WriteLine(Proto.ToJson(snap, true)); break;
            case "enter": await Send(Msg.Debug, new DebugCommand { cmd = "enter", px = F(1), py = F(2), text = S(3) }); break;
            case "return": await Send(Msg.Debug, new DebugCommand { cmd = "return", px = F(1), py = F(2) }); break;
            case "loop": await Send(Msg.Debug, new DebugCommand { cmd = "loop", value = F(1, 2), px = F(2), py = F(3) }); break;
            case "stoploop": await Send(Msg.Debug, new DebugCommand { cmd = "stopLoop" }); break;
            case "freeze": await Send(Msg.Debug, new DebugCommand { cmd = "freeze", value = F(1, 0.5f) }); break;
            case "play": await Send(Msg.Debug, new DebugCommand { cmd = "play" }); break;
            case "slow": await Send(Msg.Debug, new DebugCommand { cmd = "slow", value = F(1, 0.25f) }); break;
            case "cancel": await Send(Msg.Debug, new DebugCommand { cmd = "cancel" }); break;
            case "rewind": await Send(Msg.Debug, new DebugCommand { cmd = "rewind" }); break;
            case "overlay": await Send(Msg.Debug, new DebugCommand { cmd = "overlay", value = F(1, 1) }); break;
            // say = utter: something typed to the partner, exactly as the box on the shop's page sends it (Core.Typed)
            case "utter": case "say": await Send(Msg.Debug, new DebugCommand { cmd = "utter", text = S(1) }); break;
            // voice <file.wav> [source]: a RECORDING is sent as a voice clip, the way a client sends what its microphone heard
            // (16-bit mono PCM; source "test-file" - said to the partner - or "microphone-open" - simply said in the room).
            // The host transcribes it (that needs its OpenAI key); `lpctl status` then shows what it heard and made of it.
            case "voice":
            {
                var clip = WavClip(S(1), S(2).StartsWith("--") ? "test-file" : S(2, "test-file"), snap.hostTime);
                if (clip == null) { Console.WriteLine("not a 16-bit mono PCM .wav file: " + S(1)); return 2; }
                await Send(Msg.VoiceClip, clip);
                break;
            }
            case "owner": await Send(Msg.Debug, new DebugCommand { cmd = "setOwner", text = S(1) }); break;
            case "listen": await Send(Msg.Debug, new DebugCommand { cmd = "listen", value = 1 }); break;
            case "tennis": await Send(Msg.Debug, new DebugCommand { cmd = S(1, "start") == "stop" ? "tennisStop" : "tennisStart" }); break;      // the MR client starts / stops the game
            case "listencancel": await Send(Msg.Debug, new DebugCommand { cmd = "listenCancel" }); break;
            case "voicefile": await Send(Msg.Debug, new DebugCommand { cmd = "voiceFile", text = Path.GetFullPath(S(1)) }); break;
            case "pointer":
            {
                await Send(Msg.Debug, new DebugCommand { cmd = "pointer" });
                var reply = await ReadUntil(e => e.type == Msg.Debug && e.Body<DebugCommand>()?.cmd == "pointer", 3);
                var d = reply?.Body<DebugCommand>();
                Console.WriteLine(d == null || d.text.Length == 0 ? "none" : $"{d.text} age={d.value:0.00}");
                break;
            }
            case "debug": await Send(Msg.Debug, new DebugCommand { cmd = S(1), value = F(2), px = F(3), py = F(4), text = S(5) }); break;
            case "allow-project":
                var link = new JObject { ["path"] = S(1), ["name"] = S(2), ["titleContains"] = S(3), ["previewFile"] = S(4), ["allowPush"] = S(5) == "1", ["testCommand"] = new JArray(args.Skip(6).ToArray()) };
                await Send(Msg.Debug, new DebugCommand { cmd = "allowProject", text = link.ToString() });
                break;
            case "task": await Send(Msg.TaskCommand, new TaskCommand { cmd = S(1), taskId = S(2), moduleId = S(3), prompt = S(4) }); break;
            case "readfile": await Send(Msg.TaskCommand, new TaskCommand { cmd = "read", projectPath = Path.GetFullPath(S(1)), prompt = S(2, "Please read this file.") }); break;
            case "readwindow": await Send(Msg.TaskCommand, new TaskCommand { cmd = "read", pxX = F(1), pxY = F(2), prompt = S(3, "Please read the file in this window.") }); break;      // the file shown in the window at that pixel
            case "review": await Send(Msg.TaskCommand, new TaskCommand { cmd = "analyze", pxX = F(1), pxY = F(2), prompt = S(3, "Please look at this and give me feedback.") }); break;      // the window at that pixel (0 0 = the partner's own window)
            // lpctl look [px py] ["words"]: the partner looks at the display under that pixel, at that spot, and answers (0 0 = where the hand / the gaze last was, else the partner's display as a whole)
            case "look": await Send(Msg.TaskCommand, new TaskCommand { cmd = "look", pxX = Num(1), pxY = Num(2), prompt = S(3, "이 화면이 어떤 화면이야?") }); break;
            case "module": await Send(Msg.ModuleCommand, new ModuleCommand { cmd = S(1), durationMs = (long)F(2), value = S(3) }); break;
            case "tasks": PrintTasks(snap); break;
            case "world":
            {
                // the same message the headset sends; what happens is seen in the next snapshots (lpctl world, or --watch)
                var w = S(1) switch
                {
                    "" => null,
                    "make" => new WorldCommand { cmd = "makeItem", text = S(2) },
                    "teach" => new WorldCommand { cmd = "teach", text = S(2) },
                    // use <id|name>: a THING of the partner's is pressed (world command "use": it goes and uses it; again = stop).
                    // What names no thing is a skill, as it always was (useSkill); `skill <name>` says so outright.
                    "use" => snap.world != null && snap.world.items.Any(i => i.id == S(2) || string.Equals(i.name, S(2), StringComparison.OrdinalIgnoreCase))
                        ? new WorldCommand { cmd = "use", id = S(2) } : new WorldCommand { cmd = "useSkill", id = S(2) },
                    "skill" => new WorldCommand { cmd = "useSkill", id = S(2) },
                    "ambience" => new WorldCommand { cmd = "ambience", text = S(2).StartsWith("--") ? "clear" : S(2, "clear") },
                    "music" => new WorldCommand { cmd = "music", text = S(2).StartsWith("--") ? "" : S(2) },      // no words: any tune
                    "do" => new WorldCommand { cmd = "do", text = S(2) },
                    "place" => new WorldCommand { cmd = "place", id = S(2), x = Num(3), z = Num(4), yaw = Num(5), y = Num(6), onId = S(7).StartsWith("--") ? "" : S(7) },
                    "store" => new WorldCommand { cmd = "store", id = S(2) },
                    "delete" => new WorldCommand { cmd = "delete", id = S(2) },
                    "forget" => new WorldCommand { cmd = "deleteSkill", id = S(2) },
                    "redraw" => new WorldCommand { cmd = "redraw", id = S(2) },      // the picture of one thing, skill or game pack is drawn again (under a new file name)
                    // a thing with a script (ItemDef.script): the partner puts it on / takes it off, or the thing is asked for one of the acts it offers
                    "wear" => new WorldCommand { cmd = "wear", id = S(2), text = S(3).StartsWith("--") ? "" : S(3) },
                    "unwear" => new WorldCommand { cmd = "unwear", id = S(2) },
                    "act" => new WorldCommand { cmd = "act", id = S(2), text = S(3).StartsWith("--") ? "" : S(3) },
                    "stop" => new WorldCommand { cmd = "stop" },
                    _ => new WorldCommand(),
                };
                if (w == null) PrintWorld(snap.world);
                else if (w.cmd.Length == 0) { Console.WriteLine("unknown world command (make | teach | use | skill | ambience | music | do | place | store | delete | forget | redraw | wear | unwear | act | stop)"); return 2; }
                else await Send(Msg.World, w);
                break;
            }
            // the marketplace: the same message the headset's shop board and the web page send
            case "market": PrintMarket(snap.market, snap.world); break;
            case "buy": await Send(Msg.Market, new MarketCommand { cmd = "buy", id = S(1) }); break;
            case "install": await Send(Msg.Market, new MarketCommand { cmd = "install", id = S(1) }); break;
            case "remove": await Send(Msg.Market, new MarketCommand { cmd = "remove", id = S(1) }); break;
            case "publish": await Send(Msg.Market, new MarketCommand { cmd = "publish", id = S(1), price = (int)Num(2) }); break;
            case "price": await Send(Msg.Market, new MarketCommand { cmd = "setPrice", id = S(1), price = (int)Num(2) }); break;
            case "unpublish": await Send(Msg.Market, new MarketCommand { cmd = "unpublish", id = S(1) }); break;
            case "like": await Send(Msg.Market, new MarketCommand { cmd = "like", id = S(1) }); break;
            case "nickname": await Send(Msg.Market, new MarketCommand { cmd = "nickname", text = S(1) }); break;
            case "export": await Send(Msg.Market, new MarketCommand { cmd = "export", id = S(1) }); break;
            case "import": await Send(Msg.Market, new MarketCommand { cmd = "import", text = Path.GetFullPath(S(1)) }); break;
            case "hub":
                if (S(1).Length == 0 || S(1).StartsWith("--")) { Console.WriteLine(snap.market == null ? "the host sent no marketplace" : $"hub: {(snap.market.hub.Length > 0 ? snap.market.hub : "?")} {(snap.market.online ? "online" : "OFFLINE")}  nickname='{snap.market.nickname}' stars={snap.market.stars}"); break; }
                await Send(Msg.Market, new MarketCommand { cmd = "hub", text = S(1) == "default" ? "" : S(1) });
                break;
            case "make": await Send(Msg.Market, new MarketCommand { cmd = "make", text = S(1), id = S(2).StartsWith("--") ? "" : S(2) }); break;
            case "remake": await Send(Msg.Market, new MarketCommand { cmd = "remake", id = S(1), text = S(2).StartsWith("--") ? "" : S(2) }); break;
            case "script":
            {
                if (S(1) != "show" || S(2).Length == 0) { Console.WriteLine("usage: lpctl script check <file.lua> [game|thing|skill] | script show <id|name>"); return 2; }
                return ScriptShow(snap.world, S(2));
            }
            case "offer":
            {
                // yes [price] | yes free | no - the offer that is open just now (its id goes with the answer, so that an answer never lands on a newer offer)
                bool yes = S(1) is "yes" or "y", free = S(2) == "free";
                if (!yes && S(1) != "no") { Console.WriteLine("usage: lpctl offer yes [price|free] | no"); return 2; }
                if (string.IsNullOrEmpty(snap.market?.offerId)) Console.WriteLine("(nothing is on offer just now - the answer is sent all the same)");
                await Send(Msg.Market, new MarketCommand { cmd = "offer", id = snap.market?.offerId ?? "", text = yes ? (free ? "yes free" : "yes") : "no", price = yes && !free && int.TryParse(S(2), out int stars) ? stars : 0 });
                break;
            }
            case "open": await Send(Msg.Market, new MarketCommand { cmd = "open", text = S(1) switch { "mr" => "mr", "close" => "", _ => "pc" } }); break;      // (close: the board in the headset is put away)
            case "screen":
                if (snap.screen != null && S(1).Length == 0) { Console.WriteLine(snap.screen.monitorId.Length == 0 ? "no display is out" : $"board: {snap.screen.monitorId} revision={snap.screen.revision} caption='{snap.screen.caption}'  (GET /screen/{snap.screen.revision}.jpg)"); break; }
                await Send(Msg.Market, new MarketCommand { cmd = "screen", text = S(1) + (S(2) == "analyze" ? " analyze" : "") });
                break;
            case "song":
                if (S(1) != "export") { Console.WriteLine("usage: lpctl song export [\"<description>\"]"); return 2; }
                await Send(Msg.Market, new MarketCommand { cmd = "song", text = S(2).StartsWith("--") ? "" : S(2) });
                break;
            case "paircode":
            {
                await Send(Msg.Debug, new DebugCommand { cmd = "pairCode" });
                var reply = await ReadUntil(e => e.type == Msg.Debug && e.Body<DebugCommand>()?.cmd == "pairCode", 3);
                Console.WriteLine(reply?.Body<DebugCommand>()?.text ?? "");
                break;
            }
            // documents (Shared/Docs.cs): the same doc.command the headset's board and the PC client send; making one is asked for through the host's checks
            case "doc":
            {
                var docs = snap.docs ?? new DocsView();
                string id(int i) => S(i).StartsWith("--") ? "" : S(i);
                switch (S(1))
                {
                    case "": PrintDocs(docs, snap.show); break;
                    case "report": await Send(Msg.Debug, new DebugCommand { cmd = "docReport", text = S(2) }); break;
                    case "designs": await Send(Msg.Debug, new DebugCommand { cmd = "docDesigns", px = Num(2), py = Num(3), text = id(4) }); break;
                    case "point": await Send(Msg.Debug, new DebugCommand { cmd = "docPoint", px = Num(2), py = Num(3) }); break;
                    case "open": await Send(Msg.Doc, new DocCommand { cmd = "open", id = id(2) }); break;
                    case "close": await Send(Msg.Doc, new DocCommand { cmd = "close" }); break;
                    case "page": await Send(Msg.Doc, new DocCommand { cmd = "page", n = S(2) == "next" ? docs.page + 1 : S(2) is "prev" or "previous" ? docs.page - 1 : (int)Num(2) }); break;
                    case "pick": await Send(Msg.Doc, new DocCommand { cmd = "pick", n = (int)Num(2), id = id(3).Length > 0 ? id(3) : docs.items.LastOrDefault(d => d.kind == "designs")?.id ?? "" }); break;
                    case "deliver": await Send(Msg.Doc, new DocCommand { cmd = "deliver", id = id(2) }); break;
                    case "reveal": await Send(Msg.Doc, new DocCommand { cmd = "reveal", id = id(2) }); break;
                    case "delete": await Send(Msg.Doc, new DocCommand { cmd = "delete", id = id(2) }); break;
                    default: Console.WriteLine("unknown doc command (report | designs | point | open | close | page | pick | deliver | reveal | delete)"); return 2;
                }
                break;
            }
            case "work":
            {
                // (W1) the work queue (Shared/Work.cs): `work` prints it; `work cancel|open <id>`, `work clear` are the clients' own work.command.
                // (A making is asked for with `make "<words>" [game|item|skill]`; the workshop alone, without a host: LivingPartner.Host.exe --work "<words>".)
                var jobs = snap.work?.jobs ?? new List<WorkJob>();
                switch (S(1))
                {
                    case "":
                        Console.WriteLine($"work queue: {jobs.Count(j => j.endedAt == 0)} open, {jobs.Count(j => j.endedAt > 0)} finished (revision {snap.work?.revision ?? 0})");
                        foreach (var j in jobs)
                        {
                            Console.WriteLine($"  {j.id}  {j.status,-9} {j.where}  {j.kind,-7} '{j.title}'  {j.progress * 100:0}%  round {j.round}  [{j.worker}]  {(j.endedAt > 0 ? $"{j.endedAt - j.startedAt:0} s  -> {j.resultKind} {j.resultId}  {j.note}" : j.step)}");
                            if (j.endedAt == 0 || S(2) == "log") foreach (string line in j.log) Console.WriteLine("        - " + line);
                        }
                        break;
                    case "cancel": await Send(Msg.Work, new WorkCommand { cmd = "cancel", id = S(2).Length > 0 ? S(2) : jobs.FirstOrDefault(j => j.endedAt == 0)?.id ?? "" }); break;
                    case "open": await Send(Msg.Work, new WorkCommand { cmd = "open", id = S(2).Length > 0 ? S(2) : jobs.FirstOrDefault(j => j.status == "done")?.id ?? "" }); break;
                    case "clear": await Send(Msg.Work, new WorkCommand { cmd = "clear" }); break;
                    default: Console.WriteLine("work [log] | work cancel [id] | work open [id] | work clear"); break;
                }
                break;
            }
            case "glass":
            {
                // glass games (Shared/Glass.cs): the same glass.command the clients send; `hand` is a debug hand for checks without a headset
                switch (S(1))
                {
                    case "": Console.WriteLine(GlassLine(snap.glass)); break;
                    case "start": await Send(Msg.GlassCommand, new GlassCommand { cmd = "start", game = S(2), how = "lpctl" }); break;
                    case "stop": await Send(Msg.GlassCommand, new GlassCommand { cmd = "stop", how = "lpctl" }); break;
                    case "choose": await Send(Msg.GlassCommand, new GlassCommand { cmd = "choose", pick = Math.Clamp((int)Num(2), 0, 2), how = "lpctl" }); break;
                    case "hand":
                    {
                        // one report of the RIGHT hand in front of a display, through the host to the PC client: lpctl glass hand <monitorId|-> <u> <v> <dist>
                        string mon = S(2, "-");
                        if (mon == "-") mon = snap.glass != null && snap.glass.monitorId.Length > 0 ? snap.glass.monitorId : snap.pc != null && snap.pc.monitorId.Length > 0 ? snap.pc.monitorId : snap.monitors.FirstOrDefault(m => m.primary)?.id ?? snap.monitors.FirstOrDefault()?.id ?? "";
                        var hand = new GlassHand { monitorId = mon, t = snap.hostTime + sinceSnapshot.Elapsed.TotalSeconds };
                        hand.hands[0] = new GlassHandPoint { valid = true, u = Num(3), v = Num(4), dist = args.Length > 5 ? Num(5) : 0.15f, spread = 1f };
                        await Send(Msg.GlassHand, hand);
                        Console.WriteLine($"hand -> {mon} ({hand.hands[0].u.ToString("0.00", Inv)}, {hand.hands[0].v.ToString("0.00", Inv)}) {hand.hands[0].dist.ToString("0.00", Inv)} m in front of the glass");
                        break;
                    }
                    default: Console.WriteLine("usage: lpctl glass [start <game> | stop | choose <0-2> | hand <monitorId|-> <u> <v> <dist>]"); return 2;
                }
                break;
            }
            case "arrival": await Send(Msg.TaskCommand, new TaskCommand { cmd = "analyzeOnArrival", prompt = S(1) }); break;
            case "waittask": return await WaitTask(S(1), F(2, 300));
            case "watch": await Watch(F(1, 10), false); return 0;
            case "wait": return await Watch(F(1, 30), true);
            default: Console.WriteLine("unknown command"); return 2;
        }
        await Task.Delay(250);
        if (args.Contains("--watch")) await Watch(float.Parse(args[Array.IndexOf(args, "--watch") + 1], Inv), false);
        return 0;
    }

    // ------------------------------------------------------------------ scripts: what the modules are made of
    /// <summary>
    /// `lpctl script check &lt;file.lua&gt; [game|thing|skill]`: RUNS a script in the checker exactly as the host does before
    /// it accepts one the partner wrote (ScriptRules: GlassScriptCheck for a game, RoomScriptCheck for a thing or a
    /// skill) and prints the verdict and the checker's own report. Exit 0 = it runs and is not dead, 1 = it does not.
    /// </summary>
    static int ScriptCheck(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("usage: lpctl script check <file.lua> [game|thing|skill]"); return 2; }
        string lua;
        try { lua = File.ReadAllText(args[2]); } catch (Exception e) { Console.WriteLine("cannot read " + args[2] + ": " + e.Message); return 2; }
        string kind = args.Length > 3 ? args[3].ToLowerInvariant() : "";
        // (not said: a script with the hooks of a thing is a thing's, anything else a game)
        if (kind.Length == 0) kind = System.Text.RegularExpressions.Regex.IsMatch(lua, @"function\s+on_(use|act|wear|make|stop)\b|\bwearable\s*\(|\boffer\s*\(") ? "thing" : "game";
        var v = kind == "game" ? LivingPartner.Host.ScriptRules.CheckGame(lua)
              : LivingPartner.Host.ScriptRules.CheckRoom(lua, new ItemDef { id = "thing", name = Path.GetFileNameWithoutExtension(args[2]), sizeM = 0.08f });
        Console.WriteLine($"{Path.GetFileName(args[2])}: a {kind}'s script, {LivingPartner.Host.ScriptRules.Lines(lua)} lines / {LivingPartner.Host.ScriptRules.Bytes(lua)} bytes");
        Console.WriteLine($"verdict: {v.Line}   (rank {v.Rank}/3: 3 = clean, 2 = small warnings, 1 = something does not really work, 0 = not ok)");
        if (kind != "game" && v.ran) Console.WriteLine($"offers: {(v.wearable ? "wear (" + v.slot + ")" : "not worn")}{(v.offers.Count > 0 ? ", acts: " + string.Join(", ", v.offers.Select(o => o.name + " '" + o.label + "'")) : "")}{(v.motions.Count > 0 ? "; motions: " + string.Join(", ", v.motions) : "")}; hooks: {string.Join(" ", v.hooks)}");
        foreach (string w in v.serious) Console.WriteLine("DOES NOT WORK: " + w);
        foreach (string w in v.minor) Console.WriteLine("small: " + w);
        Console.WriteLine("--- the checker's report");
        Console.WriteLine(v.report.TrimEnd());
        return v.ok ? 0 : 1;
    }

    /// <summary>`lpctl script show &lt;id|name&gt;`: the script of a game, a thing or a skill the partner has. Exit 1 when there is none of that name, or it has no script.</summary>
    static int ScriptShow(WorldState w, string what)
    {
        if (w == null) { Console.WriteLine("the host sent no world"); return 1; }
        bool Is(string id, string name) => id == what || string.Equals(name, what, StringComparison.OrdinalIgnoreCase);
        string script = null, head = "";
        if (w.games.FirstOrDefault(g => Is(g.id, g.title)) is { } game) { script = game.script; head = $"game {game.id} '{game.title}' ({game.template}){(game.art != null && game.art.Count > 0 ? ", pictures: " + string.Join(", ", game.art.Select(a => a.name + " = " + a.file)) : "")}"; }
        else if (w.items.FirstOrDefault(i => Is(i.id, i.name)) is { } item) { script = item.script; head = $"thing {item.id} '{item.name}'{(string.IsNullOrEmpty(item.wornOn) ? "" : ", worn on " + item.wornOn)}"; }
        else if (w.skills.FirstOrDefault(k => Is(k.id, k.name)) is { } skill) { script = skill.script; head = $"skill {skill.id} '{skill.name}'"; }
        if (script == null) { Console.WriteLine($"nothing the partner has is called '{what}' (games, things and skills in its bag: see `lpctl world`)"); return 1; }
        if (script.Length == 0) { Console.WriteLine(head + ": it has no script (it is one of the makers' built-in kinds, or a list of acts)"); return 1; }
        Console.WriteLine($"-- {head}: {LivingPartner.Host.ScriptRules.Lines(script)} lines");
        Console.WriteLine(script.TrimEnd());
        return 0;
    }

    static void PrintDocs(DocsView v, ShowInfo show)
    {
        Console.WriteLine($"documents: {v.items.Count} (revision {v.revision}); on the board: {(v.openId.Length > 0 ? v.openId + " page " + v.page : "-")}");
        foreach (var d in v.items)
        {
            Console.WriteLine($"  {d.id} [{d.kind}] {d.status}{(d.status == "making" ? $" {d.progress * 100:0}% '{d.stage}'" : d.status == "failed" ? $" '{d.stage}'" : "")} \"{d.title}\" pages={d.pages}{(d.delivered ? " DELIVERED" : "")}");
            if (d.asked.Length > 0) Console.WriteLine($"      asked: {d.asked}");
            foreach (var o in d.options) Console.WriteLine($"      {(o.picked ? "*" : " ")} {o.name}  /docs/{d.id}/{o.picture}");
            foreach (var f in d.files) Console.WriteLine($"      {f.kind}: {f.path}");
        }
        if (show != null && show.kind == "file") Console.WriteLine($"  show: file/{show.stage} '{show.name}' {show.icon} for {show.seconds:0} s");
    }

    static void PrintStatus(StateSnapshot s)
    {
        var t = s.transition;
        Console.WriteLine($"owner={s.character.owner} design={s.character.design} activity={s.character.activity} talk={(s.character.talk.Length > 0 ? s.character.talk : "-")} transition={t.state} id={t.transitionId} dir={t.direction} reason='{t.reason}'");
        // what it heard last (a voice clip, as transcribed) and what it made of it; what the user owns and can switch on
        if (!string.IsNullOrEmpty(s.character.heard)) Console.WriteLine($"  heard: '{s.character.heard}' -> {(string.IsNullOrEmpty(s.character.heardNote) ? "(answering)" : s.character.heardNote)} ({(s.hostTime - s.character.heardAt).ToString("0", Inv)} s ago)");
        if (s.world != null) Console.WriteLine($"  world: ambience={(s.world.ambience?.id ?? "-")} of [{string.Join(", ", (s.world.ambiences ?? new List<AmbienceDef>()).Select(a => a.id))}]  song={(s.world.song?.id ?? "-")} of [{string.Join(", ", (s.world.songs ?? new List<SongDef>()).Select(x => x.id))}]");
        Console.WriteLine($"clients=[{string.Join(",", s.connectedRoles)}] monitors={s.monitors.Count} calibrations={s.calibrations.Count} modules={s.modules.Count} tasks={s.tasks.Count}");
        foreach (var m in s.monitors) Console.WriteLine($"  {m.id} bounds={m.bounds} work={m.work} dpi={m.dpiScale:0.00} rot={m.rotation} phys={m.physWidthM * 1000:0}x{m.physHeightM * 1000:0}mm {(m.primary ? "primary" : "")}");
        if (s.tennis != null) Console.WriteLine($"  tennis: phase={s.tennis.phase} user={s.tennis.scoreUser} partner={s.tennis.scorePartner} rally={s.tennis.rally} best={s.tennis.bestRally} shot={(s.tennis.shot == null ? "-" : s.tennis.shot.by + "#" + s.tennis.shot.seq)} last={s.tennis.lastPoint} note='{s.tennis.note}' court={s.tennis.court.width * 1000:0}x{s.tennis.court.height * 1000:0}x{s.tennis.court.depth * 1000:0}mm on {s.tennis.court.monitorId}");
        Console.WriteLine("  " + GlassLine(s.glass));
        if (s.docs != null && s.docs.items.Count > 0) Console.WriteLine($"  docs: {string.Join(", ", s.docs.items.Select(d => $"{d.id} {d.kind} {d.status}{(d.status == "making" ? $" {d.progress * 100:0}% '{d.stage}'" : "")}{(d.delivered ? " delivered" : "")}"))}; open={(s.docs.openId.Length > 0 ? s.docs.openId + " p" + s.docs.page : "-")}");
        if (s.show != null && s.show.kind.Length > 0) Console.WriteLine($"  show: {s.show.kind}{(s.show.stage.Length > 0 ? "/" + s.show.stage : "")} #{s.show.seq} on {(s.show.monitorId.Length > 0 ? s.show.monitorId : "-")}{(s.show.kind == "look" ? $" rect=({s.show.u.ToString("0.00", Inv)}, {s.show.v.ToString("0.00", Inv)}, {s.show.w.ToString("0.00", Inv)}, {s.show.h.ToString("0.00", Inv)})" : $" '{s.show.name}' icon={(s.show.icon.Length > 0 ? s.show.icon : "-")} price={s.show.price}")} {s.show.seconds.ToString("0.0", Inv)} s");
        if (s.pc != null) Console.WriteLine($"  pc: mode={s.pc.mode} mon={s.pc.monitorId} feetPx=({s.pc.pxX:0},{s.pc.pxY:0}) visible={s.pc.overlayVisible} bound=0x{s.pc.boundHwnd:X} min={s.pc.boundMinimized} fps={s.pc.fps:0} suspended='{s.pc.suspendedReason}' design={s.pc.design}");
        if (s.mr != null) Console.WriteLine($"  mr: tracking={s.mr.tracking} hands={s.mr.handsTracked} onHand={s.mr.onHand} sim={s.mr.simulated} activity={s.mr.activity} fps={s.mr.fps:0} design={s.mr.design}");
        foreach (var m in s.modules) Console.WriteLine($"  module {m.moduleId} {m.kind} '{m.name}' v{m.version} status={m.status} remaining={TimerLogic.Format(TimerLogic.Remaining(m, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))} display={m.display} alert={m.alert}");
        foreach (var k in s.tasks) Console.WriteLine($"  task {k.taskId} {k.kind} {k.status} '{k.title}' modules={k.modules.Count} files=[{string.Join(",", k.changedFiles)}] branch={k.branch} commit={k.commit} stub={k.isTestDouble} :: {k.detail}");
    }

    /// <summary>The glass game that is on, in one line.</summary>
    static string GlassLine(GlassState g) => g == null || g.game.Length == 0 ? "glass: -" :
        $"glass: {g.game} '{g.title}' ({g.packId}) side={g.side}{(g.monitorId.Length > 0 ? " on " + g.monitorId : "")} run={g.runId} phase={g.phase} score={g.score} lives={g.lives} level={g.level} best={g.best} t={g.seconds.ToString("0.0", Inv)}s" +
        $"{(g.choices.Length > 0 ? " choices=[" + string.Join(" | ", g.choices) + "]" : "")}{(g.banner.Length > 0 ? $" banner='{g.banner}'" : "")}{(g.result.Length > 0 ? $" result='{g.result}'" : "")}";

    /// <summary>A .wav file (16-bit mono PCM) as the voice clip a client would send; null when it is not one.</summary>
    static VoiceClip WavClip(string path, string source, double hostNow)
    {
        try
        {
            byte[] f = File.ReadAllBytes(path);
            if (f.Length < 44 || Encoding.ASCII.GetString(f, 0, 4) != "RIFF" || Encoding.ASCII.GetString(f, 8, 4) != "WAVE") return null;
            int rate = 0, channels = 0, bits = 0, dataAt = -1, dataLen = 0;
            for (int at = 12; at + 8 <= f.Length;)
            {
                string id = Encoding.ASCII.GetString(f, at, 4); int len = BitConverter.ToInt32(f, at + 4);
                if (len < 0) return null;
                if (id == "fmt " && at + 24 <= f.Length) { channels = BitConverter.ToInt16(f, at + 10); rate = BitConverter.ToInt32(f, at + 12); bits = BitConverter.ToInt16(f, at + 22); }
                if (id == "data") { dataAt = at + 8; dataLen = Math.Min(len, f.Length - dataAt); break; }
                at += 8 + len + (len & 1);
            }
            if (dataAt < 0 || channels != 1 || bits != 16 || rate < 8000 || dataLen < 2) return null;
            double seconds = dataLen / 2.0 / rate;
            return new VoiceClip { clipId = Guid.NewGuid().ToString("N"), sampleRate = rate, pcm16 = Convert.ToBase64String(f, dataAt, dataLen), t0 = hostNow - seconds, t1 = hostNow, source = source };
        }
        catch { return null; }
    }

    static void PrintWorld(WorldState w)
    {
        if (w == null) { Console.WriteLine("the host sent no world (an older host?)"); return; }
        Console.WriteLine($"world rev={w.revision} doing={(w.doing.Length > 0 ? w.doing : "-")} making='{w.making}'");
        Console.WriteLine($"  workbench: {(w.makingKind.Length == 0 ? "-" : $"{w.makingKind} '{w.makingName}' {(w.makingProgress * 100).ToString("0", Inv)}%{(w.makingId.Length > 0 ? " id=" + w.makingId : "")}")}");
        Console.WriteLine($"  ambience: {(w.ambience == null ? "-" : $"{w.ambience.id} '{w.ambience.name}' {w.ambience.pattern} {w.ambience.c0} {w.ambience.c1} {w.ambience.c2} dim={w.ambience.dim.ToString("0.00", Inv)} speed={w.ambience.speed.ToString("0.0", Inv)} density={w.ambience.density.ToString("0.0", Inv)} beat={w.ambience.beat}")}");
        Console.WriteLine($"  song: {(w.song == null ? "-" : SongLine(w.song))}");
        // what the user owns and can switch on at any time ("*" = on now): lpctl world ambience <id>, lpctl world music <id>
        Console.WriteLine($"  ambiences ({(w.ambiences ?? new List<AmbienceDef>()).Count} owned): {string.Join(", ", (w.ambiences ?? new List<AmbienceDef>()).Select(a => $"{(w.ambience != null && w.ambience.id == a.id ? "*" : "")}{a.id} '{a.name}' {a.pattern}{(string.IsNullOrEmpty(a.image) ? "" : $" image={a.image} yaw={a.yaw.ToString("0", Inv)}")}"))}");
        // the games in the bag: a board game says which one (rule) and how well the partner plays (skill)
        Console.WriteLine($"  games ({(w.games ?? new List<GamePackDef>()).Count} in the bag): {string.Join(", ", (w.games ?? new List<GamePackDef>()).Select(g => $"{g.id} '{g.title}' {g.template}{(string.IsNullOrEmpty(g.rule) ? "" : $"/{g.rule} skill={g.skill}")}{(string.IsNullOrEmpty(g.script) ? "" : $" ({LivingPartner.Host.ScriptRules.Lines(g.script)} lines{(g.art != null && g.art.Count > 0 ? ", pictures: " + string.Join(" ", g.art.Select(a => a.name)) : "")})")}"))}");
        Console.WriteLine($"  songs ({(w.songs ?? new List<SongDef>()).Count} records): {string.Join(", ", (w.songs ?? new List<SongDef>()).Select(s => $"{(w.song != null && w.song.id == s.id ? "*" : "")}{s.id} '{s.name}'"))}");
        foreach (var i in w.items)
            Console.WriteLine($"  item {i.id} '{i.name}' {i.status}{(i.status == "making" ? $" {i.progress * 100:0}%" : "")} kind={i.kind} for=[{string.Join(",", i.uses)}] size={(i.sizeM * 100).ToString("0.0", Inv)}cm {(i.inRoom ? $"on the desk ({i.x.ToString("0.00", Inv)}, {i.z.ToString("0.00", Inv)}, {i.yaw:0} deg){(i.y > 0 || !string.IsNullOrEmpty(i.onId) ? $" {(i.y * 100).ToString("0.0", Inv)}cm up on {(string.IsNullOrEmpty(i.onId) ? "-" : i.onId)}" : "")}" : "in the bag")} model={(i.model.Length > 0 ? i.model : "-")} icon={(i.icon.Length > 0 ? i.icon : "-")}{(string.IsNullOrEmpty(i.script) ? "" : $" script={LivingPartner.Host.ScriptRules.Lines(i.script)} lines")}{(string.IsNullOrEmpty(i.wornOn) ? "" : " WORN on " + i.wornOn)} :: {i.note}");
        foreach (var k in w.skills)
        {
            Console.WriteLine($"  skill {k.id} '{k.name}' {k.status}{(k.builtin ? " built-in" : "")} item={(k.itemId.Length > 0 ? k.itemId : "-")} icon={(k.icon.Length > 0 ? k.icon : "-")} :: {k.note}");
            Console.WriteLine($"    steps: {string.Join(" > ", k.steps.Select(st => st.act + (st.arg.Length > 0 ? ":" + st.arg : "") + (st.seconds > 0 ? $" ({st.seconds.ToString("0.#", Inv)} s)" : "")))}{(string.IsNullOrEmpty(k.script) ? "" : $"   script: {LivingPartner.Host.ScriptRules.Lines(k.script)} lines")}");
            if (k.phrases.Length > 0) Console.WriteLine($"    asked with: {string.Join(" | ", k.phrases)}");
            if (k.song != null) Console.WriteLine($"    song: {SongLine(k.song)}");
            if (k.ambience != null) Console.WriteLine($"    ambience: '{k.ambience.name}' {k.ambience.pattern} {k.ambience.c0} {k.ambience.c1} {k.ambience.c2} dim={k.ambience.dim.ToString("0.00", Inv)}");
        }
    }

    static void PrintMarket(MarketView m, WorldState w)
    {
        if (m == null) { Console.WriteLine("the host sent no marketplace (an older host?)"); return; }
        Console.WriteLine($"market rev={m.revision} hub={(m.hub.Length > 0 ? m.hub : "?")} {(m.online ? "online" : "OFFLINE")} nickname='{m.nickname}' stars={m.stars} sales={m.sales} earned={m.earned} open={(m.open.Length > 0 ? m.open : "-")}{(m.notice.Length > 0 ? $" notice='{m.notice}'" : "")}");
        Console.WriteLine($"  {m.list.Count} entries: {m.list.Count(l => l.listed)} on sale ({m.list.Count(l => l.listed && l.mine)} mine), {m.list.Count(l => l.owned)} owned, {m.list.Count(l => l.installed)} installed, {m.list.Count(l => l.mine && !l.listed)} of my own not on sale   ({Encoding.UTF8.GetByteCount(Proto.ToJson(m))} bytes in every snapshot)");
        Console.WriteLine($"  offer: {(m.offerId.Length == 0 ? "-" : $"{m.offerId} '{m.list.FirstOrDefault(l => l.id == m.offerId)?.name ?? "? (not in the list)"}' suggested {m.offerPrice} stars")} (offer #{m.offerSeq}){(w != null && w.makingKind.Length > 0 ? $"   workbench: {w.makingKind} '{w.makingName}' {(w.makingProgress * 100).ToString("0", Inv)}%" : "")}");
        Console.WriteLine("  flags: S on sale, O owned, I installed, M mine, L liked");
        foreach (var l in m.list)
            Console.WriteLine($"  [{(l.listed ? "S" : "-")}{(l.owned ? "O" : "-")}{(l.installed ? "I" : "-")}{(l.mine ? "M" : "-")}{(l.liked ? "L" : "-")}] {l.kind,-8} {l.id,-20} '{l.name}'{(l.template.Length > 0 ? " (" + l.template + ")" : "")} {(l.price > 0 ? l.price + " stars" : "free")} by {(l.seller.Length > 0 ? l.seller : l.author)} got={l.installs} likes={l.likes} icon={(l.icon.Length > 0 ? l.icon : "-")} :: {l.gives}");
        if (w == null) return;
        Console.WriteLine($"  games in the world: {string.Join(", ", w.games.Select(g => $"{g.id} '{g.title}' ({g.template}{(g.enemyIcon.Length > 0 ? ", " + g.enemyIcon : "")}{(g.pickupIcon.Length > 0 ? ", " + g.pickupIcon : "")})"))}");
        Console.WriteLine($"  abilities: {(w.abilities.Count == 0 ? "-" : string.Join(", ", w.abilities.Select(a => $"{a.id} '{a.name}'")))}");
    }

    // ------------------------------------------------------------------ TEST HELPER: another account on the hub
    /// <summary>
    /// `lpctl hub-as &lt;nickname&gt; ...`: acts as a second, made-up person straight against the hub - with the very calls
    /// the host makes for the real user (HubApi.cs) - so that being bought from, and buying from somebody else, can be
    /// tried on one PC. The account is registered on first use; its token is kept in this user's own data folder (it is
    /// worth test stars only) and never printed.
    /// </summary>
    static async Task<int> HubAs(string[] args)
    {
        string nick = args.Length > 1 ? args[1] : "", verb = args.Length > 2 ? args[2] : "me", what = args.Length > 3 ? args[3] : "";
        string url = LivingPartner.Host.HubRules.CleanUrl(Environment.GetEnvironmentVariable("LP_MARKET_HUB"));
        if (url.Length == 0) url = LivingPartner.Host.HubRules.DefaultUrl;
        Console.WriteLine($"TEST HELPER - acting as a SECOND test account ('{nick}') straight against the hub {url}. This is not this PC's user.");
        if (LivingPartner.Host.HubRules.NicknameProblem(nick).Length > 0) { Console.WriteLine("usage: lpctl hub-as <nickname> buy|like \"<listing name>\" | publish-sample [price] | me      (a nickname is 2 to 16 letters, digits, spaces, - or _)"); return 2; }
        using var api = new LivingPartner.Host.HubApi(url);
        var ct = CancellationToken.None;
        if (!await api.Alive(ct)) { Console.WriteLine("no hub answers there (is the host running, or LP_MARKET_HUB set to the right address?)"); return 4; }
        try
        {
            string token = await TestAccount(api, url, nick, ct);
            if (token == null) return 5;
            switch (verb)
            {
                case "me": break;
                case "buy":
                case "like":
                {
                    var found = (await api.Listings(ct, -1, what)).list;
                    var l = found.FirstOrDefault(x => x.id == what) ?? found.FirstOrDefault(x => string.Equals(x.name, what, StringComparison.OrdinalIgnoreCase)) ?? found.FirstOrDefault();
                    if (l == null) { Console.WriteLine($"nothing on sale is called '{what}'"); return 6; }
                    if (verb == "like") { var liked = await api.Like(token, l.id, ct); Console.WriteLine($"{(liked.liked ? "liked" : "took the like back from")} '{l.name}' by {l.seller}: {liked.likes} like(s) now"); break; }
                    var b = await api.Buy(token, l.id, ct);
                    Console.WriteLine(b.already ? $"'{l.name}' by {l.seller} was this account's already: nothing paid" : $"bought '{l.name}' from {l.seller} for {b.price} stars");
                    break;
                }
                case "publish-sample":
                {
                    int price = int.TryParse(what, out int p) ? p : 15;
                    // ids that belong to this test account: running it again brings the same two listings up to date instead of piling up new ones
                    string tag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(LivingPartner.Host.HubRules.NickKey(nick))))[..8].ToLowerInvariant();
                    var ability = new MarketModule
                    {
                        id = "ability-" + tag, kind = ModuleKinds.Ability, name = "칭찬 요정", summary = "기운 없을 때 한마디. 뻔하지 않게, 방금 한 일을 콕 집어서 칭찬해 줘요.", gives = "Nilo can cheer you up with a compliment that is about you.", tags = new[] { "칭찬", "응원", "cheer", "compliment" },
                        ability = new AbilityDef
                        {
                            id = "ability-" + tag, name = "칭찬 요정",
                            prompt = "When the user says they are tired, asks for a compliment or asks to be cheered up (\"칭찬해 줘\", \"힘들어\", \"cheer me up\"), answer with one warm, specific sentence about something they said or did in this conversation - never a general phrase, never advice. If you know nothing about their day yet, ask one short question first.",
                            examples = new[] { "칭찬해 줘", "오늘 좀 힘들어", "cheer me up" },
                        },
                    };
                    var kind = ItemKinds.Find("plant");
                    var plant = new MarketModule
                    {
                        id = "item-" + tag, kind = ModuleKinds.Item, name = "선인장 화분", summary = "물을 안 줘도 되는 작은 선인장. 책상 한쪽에 두면 든든해요.", gives = "Something green (and a little prickly) for the desk.", tags = new[] { "선인장", "화분", "cactus", "plant" },
                        item = new ItemDef { id = "item-" + tag, name = "선인장 화분", asked = "작은 선인장 화분", kind = kind.id, uses = (string[])kind.uses.Clone(), sizeM = 0.07f, color = "#7FC8A0", note = "선인장이다! 물은 가끔만 주면 돼.", status = "ready", progress = 1 },
                    };
                    foreach (var (m, cost) in new[] { (ability, price), (plant, Math.Max(0, price - 5)) })
                    {
                        var l = await api.Publish(token, new LivingPartner.Host.HubPublish { module = m, price = cost }, ct);
                        Console.WriteLine($"on sale: '{l.name}' ({l.kind}, id {l.id}) by {l.seller} for {(l.price > 0 ? l.price + " stars" : "free")}");
                    }
                    break;
                }
                default: Console.WriteLine("unknown: buy | like | publish-sample | me"); return 2;
            }
            var me = await api.Me(token, ct);
            Console.WriteLine($"'{me.nickname}' now: {me.stars} stars, sold {me.sales} time(s) for {me.earned} stars, has {me.bought.Count} thing(s), sells {me.listings.Count(x => x.listed)}");
            foreach (var x in me.listings) Console.WriteLine($"  sells{(x.listed ? "" : " (off sale)")}: '{x.name}' {(x.price > 0 ? x.price + " stars" : "free")} - got {x.installs} time(s), earned {x.earned}");
            foreach (var x in me.purchases) Console.WriteLine($"  has: '{x.name}' by {x.seller} ({(x.price > 0 ? "paid " + x.price : "free")})");
            return 0;
        }
        catch (LivingPartner.Host.HubException e)
        {
            Console.WriteLine(e.Code == "not-enough-stars" ? $"the hub said no: not enough stars ({e.Missing} short)" : $"the hub said no: {(e.Code.Length > 0 ? e.Code : e.Message)}{(e.Detail.Length > 0 ? " (" + e.Detail + ")" : "")}");
            return 7;
        }
    }

    /// <summary>The token of a test account on this hub: from the file where earlier ones are kept, or from registering it now. null (and a line that says why) when the nickname is somebody else's.</summary>
    static async Task<string> TestAccount(LivingPartner.Host.HubApi api, string url, string nick, CancellationToken ct)
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LivingAIPartner", "hub-test-accounts.json");
        string key = url + "|" + LivingPartner.Host.HubRules.NickKey(nick);
        var all = (File.Exists(file) ? Proto.FromJson<Dictionary<string, string>>(File.ReadAllText(file)) : null) ?? new Dictionary<string, string>();
        if (all.TryGetValue(key, out string token))
        {
            try { await api.Me(token, ct); return token; }
            catch (LivingPartner.Host.HubException e) when (e.Status == 401) { all.Remove(key); }       // the hub's data was reset since: register again
        }
        try
        {
            var r = await api.Register(nick, ct);
            all[key] = r.token;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, Proto.ToJson(all, true));
            Console.WriteLine($"registered the test account '{r.nickname}' ({r.stars} stars to start with)");
            return r.token;
        }
        catch (LivingPartner.Host.HubException e) when (e.Code == "nickname-taken")
        {
            Console.WriteLine($"'{nick}' is somebody else's nickname on this hub (a real account, or a test account whose token is gone): choose another name");
            return null;
        }
    }

    static string SongLine(SongDef s) =>
        $"{s.id} '{s.name}' {s.bpm} bpm, {s.bars} bars, lead={s.lead}{(s.minor ? " minor" : "")} swing={s.swing.ToString("0.00", Inv)} melody={s.melody.Count(n => n > 0)} notes bass={s.bass.Count(n => n > 0)} chords=[{string.Join(",", s.chords)}] kick={(s.kick.Length > 0 ? s.kick : "-")} snare={(s.snare.Length > 0 ? s.snare : "-")} hat={(s.hat.Length > 0 ? s.hat : "-")}";

    static void PrintTasks(StateSnapshot s)
    {
        foreach (var k in s.tasks)
        {
            Console.WriteLine($"task {k.taskId} [{k.kind}] status={k.status} harness={k.harness} session={k.sessionId} exit={k.exitCode} testDouble={k.isTestDouble}");
            Console.WriteLine($"  title: {k.title}");
            Console.WriteLine($"  project: {k.projectPath}");
            Console.WriteLine($"  detail: {k.detail}");
            if (k.branch.Length > 0) Console.WriteLine($"  branch: {k.branch} commit: {k.commit} changed: {string.Join(", ", k.changedFiles)}");
            if (k.diffSummary.Length > 0) Console.WriteLine("  diff: " + k.diffSummary.Replace("\n", " | "));
            if (k.testOutput.Length > 0) Console.WriteLine("  test: " + k.testOutput.Replace("\n", " | "));
            Console.WriteLine("  events: " + string.Join(" > ", k.events.Select(e => e.name)));
            if (k.outputs.Count > 0) Console.WriteLine("  outputs: " + string.Join(" | ", k.outputs));
            foreach (var m in k.modules)
            {
                Console.WriteLine($"  module {m.moduleId} [{m.confidence}] status={m.status} test={m.testResult} :: {m.name}");
                Console.WriteLine($"    files: {string.Join(", ", m.files)}");
                Console.WriteLine($"    ui: {m.uiEvidence}");
                Console.WriteLine($"    role: {m.role}");
                Console.WriteLine($"    evidence: {m.evidence}");
                Console.WriteLine($"    proposal: {m.proposal}");
                Console.WriteLine($"    impact: {m.impact}");
            }
        }
    }

    /// <summary>Waits until the newest task reaches one of the given statuses (separated by |). Exit 0 when reached, 9 on timeout.</summary>
    static async Task<int> WaitTask(string statuses, float seconds)
    {
        var want = statuses.Split('|');
        var end = DateTime.UtcNow.AddSeconds(seconds); string last = "";
        while (DateTime.UtcNow < end)
        {
            var e = await Read((float)Math.Max(0.05, (end - DateTime.UtcNow).TotalSeconds));
            if (e == null) continue;
            TaskInfo k = null;
            if (e.type == Msg.TaskUpdate) k = e.Body<TaskInfo>();
            else if (e.type == Msg.Snapshot) k = e.Body<StateSnapshot>()?.tasks.LastOrDefault();
            else if (e.type == Msg.Say) { var s = e.Body<SayMsg>(); Console.WriteLine($"{DateTime.Now:HH:mm:ss} say: {s.text}"); }
            if (k == null) continue;
            string line = $"task {k.taskId} {k.kind} {k.status} :: {k.detail}";
            if (line != last) { last = line; Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"); }
            if (want.Contains(k.status)) return 0;
        }
        return 9;
    }

    static async Task<int> Watch(float seconds, bool untilIdle)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        string last = "", lastWorld = "", lastMarket = "", lastShow = ""; bool sawActive = false;
        while (DateTime.UtcNow < end)
        {
            var e = await Read((float)Math.Max(0.05, (end - DateTime.UtcNow).TotalSeconds));
            if (e == null) continue;
            if (e.type == Msg.Snapshot && e.Body<StateSnapshot>()?.world is { } w)
            {
                // one line whenever something of the world that matters changed (not for every step of the revision counter)
                string line = $"world doing={(w.doing.Length > 0 ? w.doing : "-")} song={(w.song?.name ?? "-")} ambience={(w.ambience?.name ?? "-")} making='{w.making}' items=[{string.Join(", ", w.items.Select(i => $"{i.name}:{i.status}{(i.model.Length > 0 ? "+model" : "")}{(i.icon.Length > 0 ? "+icon" : "")}{(i.inRoom ? "" : " (bag)")}"))}] skills=[{string.Join(", ", w.skills.Select(k => k.name + (k.status == "ready" ? "" : ":" + k.status)))}]";
                if (lastWorld.Length > 0 && line != lastWorld) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
                lastWorld = line;
            }
            if (e.type == Msg.Snapshot && e.Body<StateSnapshot>() is { market: { } mk } full)
            {
                string line = $"market {(mk.online ? "online" : "OFFLINE")} nickname='{mk.nickname}' stars={mk.stars} sales={mk.sales} earned={mk.earned} selling={mk.list.Count(l => l.mine && l.listed)} owned={mk.list.Count(l => l.owned)} installed={mk.list.Count(l => l.installed)}/{mk.list.Count} open={(mk.open.Length > 0 ? mk.open : "-")} notice='{mk.notice}' games=[{string.Join(", ", full.world?.games.Select(g => g.title) ?? new string[0])}] abilities=[{string.Join(", ", full.world?.abilities.Select(a => a.name) ?? new string[0])}] screen={(full.screen == null || full.screen.monitorId.Length == 0 ? "-" : $"{full.screen.monitorId}#{full.screen.revision} '{full.screen.caption}'")}";
                if (lastMarket.Length > 0 && line != lastMarket) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
                lastMarket = line;
            }
            if (e.type == Msg.Snapshot && e.Body<StateSnapshot>()?.show is { } sh)
            {
                string line = sh.kind.Length == 0 ? "show -" : $"show {sh.kind}{(sh.stage.Length > 0 ? "/" + sh.stage : "")} #{sh.seq} on {(sh.monitorId.Length > 0 ? sh.monitorId : "-")}{(sh.kind == "look" ? $" rect=({sh.u.ToString("0.00", Inv)}, {sh.v.ToString("0.00", Inv)}, {sh.w.ToString("0.00", Inv)}, {sh.h.ToString("0.00", Inv)})" : $" '{sh.name}' price={sh.price}")}";
                if (lastShow.Length > 0 && line != lastShow) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
                lastShow = line;
            }
            if (e.type == Msg.TransitionUpdate || e.type == Msg.Snapshot)
            {
                var t = e.type == Msg.Snapshot ? e.Body<StateSnapshot>()?.transition : e.Body<TransitionInfo>();
                if (t == null) continue;
                string line = $"transition {t.transitionId} dir={t.direction} state={t.state} owner={t.owner} reason='{t.reason}'";
                if (line != last) { last = line; Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}"); }
                var st = TransitionMachine.Parse(t.state);
                bool active = st != TState.Idle && st != TState.Complete && st != TState.Cancelled;
                if (active) sawActive = true;
                if (untilIdle && sawActive && !active) return st == TState.Complete ? 0 : 1;
            }
            else if (e.type == Msg.Say) { var s = e.Body<SayMsg>(); Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} say[{s.mood}] {s.text}   (basis: {s.basis})"); }
            else if (e.type == Msg.TaskUpdate) { var k = e.Body<TaskInfo>(); Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} task {k.taskId} {k.kind} {k.status} :: {k.detail}"); }
            else if (e.type == Msg.ModuleUpdate) { var m = e.Body<ModuleDef>(); Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} module {m.moduleId} status={m.status} v{m.version} duration={m.durationMs}ms"); }
            else if (e.type == Msg.DebugReport) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} report {e.body}");
            else if (e.type == Msg.GlassState) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {GlassLine(e.Body<GlassState>())}");
            else if (e.type == Msg.GlassCommand) { var g = e.Body<GlassCommand>(); Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} glass.command {g.cmd} {g.pick}"); }
        }
        return untilIdle ? 9 : 0;
    }

    static Task Send(string type, object body) =>
        _ws.SendAsync(Encoding.UTF8.GetBytes(Proto.Pack(type, body)), WebSocketMessageType.Text, true, CancellationToken.None);

    static readonly byte[] Buf = new byte[256 * 1024];
    static async Task<Envelope> Read(float seconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var ms = new MemoryStream();
        try
        {
            while (true)
            {
                var r = await _ws.ReceiveAsync(Buf, cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(Buf, 0, r.Count);
                if (r.EndOfMessage) return Proto.Parse(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
            }
        }
        catch { return null; }
    }

    static async Task<Envelope> ReadUntil(Func<Envelope, bool> pred, float seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            var e = await Read((float)Math.Max(0.05, (end - DateTime.UtcNow).TotalSeconds));
            if (e != null && pred(e)) return e;
            if (e == null && _ws.State != WebSocketState.Open) return null;
        }
        return null;
    }
}
