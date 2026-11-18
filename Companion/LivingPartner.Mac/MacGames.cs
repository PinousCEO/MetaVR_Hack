using LivingPartner.Host;
using LivingPartner.MR;
using LivingPartner.Shared;

namespace LivingPartner.Mac;

sealed partial class Stage
{
    LocalWorld bag;
    GlassGame tvGame;
    GamePackDef tvPack;
    GlassHand hand;
    readonly GlassInput controls = new();
    readonly bool[] pendingFire = new bool[2];
    sealed record GameSound(long Id, string Name, double At);
    readonly Queue<GameSound> sounds = new();
    long soundId;
    bool pinch;
    int serial = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 1000000) * 100;
    double handAt, gameAt, overAt;
    float scale = 1, floor;

    void InitGames()
    {
        Match.Changed += TennisChanged;
        bag = new LocalWorld(Path.Combine(dataDir, "bag.json"), () => Now, (type, value) =>
        {
            if (value is StateSnapshot room)
            {
                snap.world = room.world;
                if (tvGame == null) snap.glass = room.glass;
                snap.settings.quietMode = room.settings.quietMode;
                snap.settings.rain = room.settings.rain;
            }
            else if (tvGame == null || type != Msg.GlassState)
            {
                if (value is GlassState glass) snap.glass = glass;
                Send(headset, type, value);
            }
        });
        RestoreCreations();
        StockChart.UpgradeSaved(bag.World.games);
        bag.Tick();
    }

    string GameButton(string id)
    {
        if(id=="game-gomoku")return Table.Request(screen?.id);
        if (id is "game-keeper" or "game-tennis" or "game-swarm") return LaunchNative(id);
        if (id == "stop")
        {
            Table.Stop();
            StopNativeGames(true);
            StopGlassGames();
            return "";
        }
        if (id.StartsWith("choose-") && int.TryParse(id[7..], out int choice) && choice is >= 0 and <= 2)
        {
            if (tvGame != null) tvGame.Choose(choice);
            else bag.Handle(Msg.GlassCommand, new GlassCommand { cmd = "choose", pick = choice });
            return "";
        }
        var pack = bag.World.games.FirstOrDefault(p => p.id == id || p.title == id);
        if (pack == null || !IsCreationId(pack.id, "game")) return "The game is missing from your bag.";
        if (machine.IsActive) return "Wait for Nilo to finish crossing the screen.";
        if (Match.State.active || swarm != null || afterTransfer.Length > 0) StopNativeGames(true);
        if (T.owner == Env.Mr)
        {
            EndScreenGame();
            bag.Handle(Msg.GlassCommand, new GlassCommand { cmd = "start", game = pack.id });
            if (headset != null && screen != null && snap.calibrations.Exists(c => c.monitorId == screen.id) &&
                (pack.template == "survival" || pack.script?.Contains("shoot_out(", StringComparison.Ordinal) == true))
            {
                needReturn = false;
                Command("enter");
            }
            return "";
        }
        if (screen == null || snap.calibrations.Count == 0) return "Calibrate the TV first.";
        needReturn = false;
        if (tvPack?.id == pack.id && tvGame?.phase != "over") return "";
        var game = GlassGames.Create(pack.template);
        if (game == null) return "Unable to start this game.";
        bag.Handle(Msg.GlassCommand, new GlassCommand { cmd = "stop" });
        tvGame = game; tvPack = pack;
        game.W = screen.bounds.w / (float)screen.bounds.h; game.inside = true;
        if (game is GlassScript script) script.Rounded = true;
        game.Start(pack, ++serial);
        scale = GlassDisplay.Scale(pack, game.flat); floor = game.flat ? GlassDisplay.FlatBottom : GlassDisplay.Floor;
        snap.glass = new GlassState { runId = serial, game = pack.template, packId = pack.id, title = pack.title, side = Env.Pc, monitorId = screen.id };
        hand = null; pinch = false; Array.Clear(pendingFire); sounds.Clear(); gameAt = Now; overAt = -1;
        RecordSounds(game, gameAt);
        game.FillState(snap.glass);
        Send(headset, Msg.GlassState, snap.glass);
        return "";
    }

    void EndScreenGame()
    {
        if (tvGame == null) return;
        tvGame = null; tvPack = null; hand = null; sounds.Clear();
        snap.glass = new GlassState { runId = serial };
        Send(headset, Msg.GlassState, snap.glass);
    }

    void StopGlassGames()
    {
        EndScreenGame();
        bag.Handle(Msg.GlassCommand, new GlassCommand { cmd = "stop" });
    }

    bool GameMessage(Envelope e)
    {
        switch (e.type)
        {
            case Msg.Settings:
                var settings = e.Body<SettingsMsg>();
                if (settings == null) break;
                bag.Handle(e.type, settings);
                snap.settings.quietMode = settings.quietMode;
                snap.character.quietMode = settings.quietMode;
                snap.settings.cameraEnabled = settings.cameraEnabled;
                snap.settings.micEnabled = settings.micEnabled && File.Exists(modelPath);
                if (settings.quietMode) sounds.Clear();
                break;
            case Msg.World:
                var world = e.Body<WorldCommand>();
                if (world?.cmd == "makeItem" && !string.IsNullOrWhiteSpace(world.text))
                {
                    string words = WorldRules.TypedText(world.text);
                    Say(QueueWork("item", WorldRules.RoughName(words, ItemKinds.Guess(words)), words, (job, ct) => MakeSculpture(job, words, ct)));
                }
                else bag.Handle(e.type, world);
                break;
            case Msg.Market:
                var request = e.Body<MarketCommand>();
                if (request?.cmd != "make" || string.IsNullOrWhiteSpace(request.text)) return false;
                string wish = WorldRules.TypedText(request.text);
                string title = GamePattern(wish).name;
                Say(QueueWork("game", title.Length > 0 ? title : "New game", wish, (job, ct) => MakeGame(job, wish, ct)));
                break;
            case Msg.GlassCommand:
                var cmd = e.Body<GlassCommand>();
                if (cmd?.cmd == "stop") StopGlassGames();
                else if (cmd != null) GameButton(cmd.cmd == "start" ? cmd.game ?? "" : cmd.cmd == "choose" ? "choose-" + cmd.pick : cmd.cmd ?? "");
                break;
            case Msg.GlassState: if (tvGame == null) bag.Handle(e.type, e.Body<GlassState>()); break;
            case Msg.GlassHand:
                var h = e.Body<GlassHand>();
                if (tvGame == null || h?.monitorId != screen?.id || h == null) break;
                hand = h; handAt = Now;
                bool pressed = h.pointValid && h.pointPinch;
                if (pressed && !pinch) pendingFire[h.pointHand == 1 ? 1 : 0] = true;
                pinch = pressed;
                break;
            case Msg.GlassOutResult:
                var answer = e.Body<GlassOutResult>();
                if (tvGame != null && answer?.runId == serial && answer.what is "blocked" or "hit" or "missed")
                    tvGame.OutResult(answer.id, answer.what);
                break;
            default: return false;
        }
        return true;
    }

    void TickGames()
    {
        bag.Tick();
        if (!machine.IsActive && tvGame != null && T.owner == Env.Mr && !GlassDisplay.IsStockChart(tvPack))
        {
            var pack = tvPack; EndScreenGame();
            bag.Handle(Msg.GlassCommand, new GlassCommand { cmd = "start", game = pack.id });
        }
        else if (!machine.IsActive && tvGame == null && T.owner == Env.Pc && snap.glass?.side == Env.Mr && snap.glass.phase != "over")
        {
            if (!GlassDisplay.IsStockChart(bag.World.games.Find(p => p.id == snap.glass.packId))) GameButton(snap.glass.packId);
        }
        AdvanceGame();
    }

    void AdvanceGame()
    {
        if (tvGame == null) return;
        double now = Now;
        if (machine.IsActive) { gameAt = now; return; }
        if (overAt >= 0 && now - overAt >= 6) { EndScreenGame(); return; }
        if (now - gameAt < .009) return;
        float dt = (float)Math.Min(.25, now - gameAt); gameAt = now;
        GlassHand h = now - handAt < .4 ? hand : null;
        if (h == null) pinch = false;
        float X(float u) => tvGame.W * (.5f + (u - .5f) / scale);
        float Y(float v) => (1 - v - floor) / scale;
        for (int i = 0; i < 2; i++)
        {
            var hp = h?.hands is { } hands && hands.Length > i ? hands[i] : null;
            bool tracked = hp != null && float.IsFinite(hp.u) && float.IsFinite(hp.v) && float.IsFinite(hp.dist);
            controls.seen[i] = tracked && (hp.seen || hp.valid); controls.valid[i] = tracked && hp.valid;
            if (controls.seen[i])
            {
                controls.x[i] = X(hp.u); controls.y[i] = Y(hp.v); controls.dist[i] = Math.Max(0, hp.dist);
                controls.spread[i] = Math.Clamp(hp.spread, 0, 1); controls.speed[i] = Math.Clamp(hp.speed, 0, 50); controls.fingers[i] = hp.fingers;
            }
            controls.aimValid[i] = h != null && h.pointValid && h.pointHand == i && float.IsFinite(h.pointU) && float.IsFinite(h.pointV);
            if (controls.aimValid[i]) { controls.aimX[i] = X(h.pointU); controls.aimY[i] = Y(h.pointV); }
            controls.fire[i] = controls.aimValid[i] && pendingFire[i]; pendingFire[i] = false;
        }
        var game = tvGame;
        game.Step(dt, controls);
        RecordSounds(game, now);
        foreach (var shot in game.outs) { shot.runId = serial; Send(headset, Msg.GlassOut, shot); }
        game.outs.Clear();
        string before = snap.glass.phase;
        game.FillState(snap.glass);
        if (snap.glass.phase != before) Send(headset, Msg.GlassState, snap.glass);
        if (game.phase == "over" && overAt < 0) overAt = now;
    }

    void RecordSounds(GlassGame game, double now)
    {
        if (!snap.settings.quietMode)
            foreach (string name in game.sounds) sounds.Enqueue(new GameSound(++soundId, name, now));
        while (sounds.Count > 24 || (sounds.Count > 0 && now - sounds.Peek().At > .65)) sounds.Dequeue();
    }

    object GameFrame()
    {
        AdvanceGame();
        var g = tvGame;
        if (g == null) return null;
        return new { w = g.W, scale, floor, title = tvPack.title, g.score, g.lives, g.level, g.phase, g.choices, g.banner, g.result,
            runId = serial, now = Now, quiet = snap.settings.quietMode, sounds = sounds.ToArray(),
            pet = new { visible = !GlassDisplay.IsStockChart(tvPack), x = g.petX, y = g.petY, right = g.petFacesRight, line = g.petLineLeft > 0 ? g.petLine : "" },
            sprites = g.sprites.Select(s => new { s.art, s.x, s.y, s.size, s.w, s.rot, s.alpha, s.color, s.text, s.text2, s.frame }).ToArray(),
            hands = Enumerable.Range(0, 2).Where(i => controls.aimValid[i] || controls.valid[i])
                .Select(i => new { x = controls.aimValid[i] ? controls.aimX[i] : controls.x[i], y = controls.aimValid[i] ? controls.aimY[i] : controls.y[i] }).ToArray() };
    }
}
