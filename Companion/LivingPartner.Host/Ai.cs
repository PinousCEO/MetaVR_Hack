using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>
/// Where the OpenAI key comes from: the OPENAI_API_KEY environment variable, or a file in the host's data folder that
/// only this Windows user can decrypt (DPAPI). It is written by tools\Set-OpenAiKey.ps1. The key is never logged, never
/// put in a snapshot and never stored in the repository.
/// </summary>
public static class AiKey
{
    public static string File => Path.Combine(HostPaths.Root, "openai.key.dpapi");

    public static string Load(out string source)
    {
        source = "";
        string env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) { source = "environment"; return env.Trim(); }
        try
        {
            if (System.IO.File.Exists(File))
            {
                string key = Encoding.UTF8.GetString(ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), null, DataProtectionScope.CurrentUser)).Trim();
                if (key.Length > 0) { source = "protected file"; return key; }
            }
        }
        catch (Exception e) { Log.Warn("the stored OpenAI key could not be read: " + e.GetType().Name); }
        return null;
    }
}

/// <summary>What the language model decided for one thing the user said.</summary>
public sealed class AiTurn
{
    public string reply = "", mood = "neutral", action = "none", game = "";
    /// <summary>The words an action of the partner's world needs: what to make, the instruction to learn, a skill's or a thing's name ... ("" for every other action).</summary>
    public string thing = "";
    public double minutes;
    public bool start;
    /// <summary>The shop only: the number of stars the user named when selling something or changing its price (0 for every other action).</summary>
    public int price;
    /// <summary>Scripted things only: wearThing - "on" | "off"; actThing - the name of the act the thing offers ("" for every other action).</summary>
    public string act = "";
    /// <summary>pcDo only: which of the PC actions (PcRules.Dos; the JSON field is "do") and its words ("" for every other action).</summary>
    [Newtonsoft.Json.JsonProperty("do")] public string pc = "";
    public string arg = "";
}

/// <summary>The three OpenAI calls the partner needs: speech to text, a reply with at most one action, text to speech.</summary>
public sealed partial class OpenAiClient
{
    public async Task<string> TranscribeAsync(byte[] pcm16, int sampleRate, CancellationToken ct, string shortClipLanguage = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(shortClipLanguage == null ? SttModel : _shortClipModel), "model");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent(shortClipLanguage ?? "en"), "language");
        // (the hint is the name and nothing else: example sentences came back as transcripts - HearRules)
        form.Add(new StringContent(HearRules.Prompt), "prompt");
        var file = new ByteArrayContent(Wav(pcm16, sampleRate));
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "utterance.wav");
        using var r = await _http.PostAsync("audio/transcriptions", form, ct);
        if (!r.IsSuccessStatusCode) throw new Exception("transcription: " + await Fail(r));
        return (JObject.Parse(await r.Content.ReadAsStringAsync(ct))["text"]?.ToString() ?? "").Trim();
    }

    static string[] Actions => TurnRules.Actions;

    /// <summary>The games the model may name in the field "game" (GlassRules.PickGame knows the module of each).</summary>
    public static readonly string[] Games = TurnRules.Games;

    public async Task<AiTurn> ChatAsync(string system, IEnumerable<(string role, string text)> history, string user, CancellationToken ct, bool allowSilence = false)
    {
        var body = TurnRules.Request(ChatModel, system, history, user);       // (the same request the recorded check of the tests sends)
        using var r = await _http.PostAsync("chat/completions", new StringContent(body.ToString(), Encoding.UTF8, "application/json"), ct);
        if (!r.IsSuccessStatusCode) throw new Exception("chat: " + await Fail(r));
        string content = JObject.Parse(await r.Content.ReadAsStringAsync(ct))["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
        var turn = Proto.FromJson<AiTurn>(content);
        if (turn == null || (!allowSilence && string.IsNullOrWhiteSpace(turn.reply))) throw new Exception("chat: the model returned no usable reply");
        if (Array.IndexOf(Actions, turn.action) < 0) turn.action = "none";
        return turn;
    }

    public async Task<byte[]> SpeakAsync(string text, string mood, CancellationToken ct)
    {
        string manner = mood switch
        {
            "happy" => "bright and pleased",
            "worried" => "a little concerned, soft",
            "curious" => "curious, with a small lift at the end",
            _ => "calm and friendly",
        };
        var body = new JObject
        {
            ["model"] = TtsModel, ["voice"] = Voice, ["input"] = text, ["response_format"] = "pcm",
            ["instructions"] = $"You are the voice of Nilo, a tiny gentle companion who lives on a desk. Light, warm, a touch playful, never babyish. Natural pace. Right now: {manner}.",
        };
        using var r = await _http.PostAsync("audio/speech", new StringContent(body.ToString(), Encoding.UTF8, "application/json"), ct);
        if (!r.IsSuccessStatusCode) throw new Exception("speech: " + await Fail(r));
        return await r.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>16-bit mono PCM in a minimal WAV container.</summary>
    public static byte[] Wav(byte[] pcm16, int sampleRate)
    {
        using var ms = new MemoryStream(44 + pcm16.Length);
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm16.Length); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm16.Length); w.Write(pcm16);
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>
/// The partner's conversation: what the user said (typed, or spoken and transcribed) becomes a short reply and at most
/// one action, and the reply is spoken. The host stays the authority: the model only chooses among the actions the host
/// already implements, the host carries them out and reports what really happened. Without a key, or when a call
/// fails, the deterministic IntentParser and the built-in lines are used instead, in text only.
/// All state is touched on the core thread; the network calls run on the thread pool and post their results back.
/// </summary>
public sealed class Conversation
{
    readonly Core _core;
    readonly OpenAiClient _ai;
    readonly List<(string role, string text)> _history = new();
    readonly List<string> _notes = new();      // what it has looked at or read lately (Review.cs), so that it can talk about it
    readonly SemaphoreSlim _speechGate = new(1, 1);
    double _speakingUntil, _listeningSince, _thinkingSince;
    int _turns, _spoken;
    bool _openBusy;        // an overheard clip is being worked on: the next one waits
    (Client c, VoiceClip clip) _openNext;
    double _attentionUntil = -1;
    int _nameCalls;
    // The partner's name as the transcription writes it - 밤비야, 밤미야, 반비야, Bamby ... - is NameRules (TalkRules.cs).
    static System.Text.RegularExpressions.Regex NameRx => NameRules.NameRx;


    /// <summary>Does this name the partner? "only": nothing but the name (a call, "밤비야!").</summary>
    internal static bool Named(string text, out bool only) => NameRules.Named(text, out only);

    public bool Enabled => _ai != null;
    public OpenAiClient Ai => _ai;
    public string KeySource { get; private set; } = "";
    public string LastError { get; private set; } = "";
    public string LastLanguage { get; private set; } = "English";
    /// <summary>For the status page: is conversation set up, and how did the last exchanges go.</summary>
    public object Diagnostics => new { enabled = Enabled, keySource = KeySource, chat = _ai?.ChatModel ?? "", stt = _ai?.SttModel ?? "", voice = "text only (no speech)", turns = _turns, notes = _notes.Count, lastLanguage = LastLanguage, lastError = LastError };

    /// <summary>Something the partner looked at or read: kept (the last few) so that it can answer questions about it.</summary>
    public void Remember(string note)
    {
        if (string.IsNullOrWhiteSpace(note)) return;
        _notes.Add(note.Length > 1400 ? note[..1400] : note);
        if (_notes.Count > 5) _notes.RemoveAt(0);
    }


    public Conversation(Core core)
    {
        _core = core;
        string key = AiKey.Load(out string source);
        if (key != null) { _ai = new OpenAiClient(key, Log.Warn); KeySource = source; }
        Log.Info(Enabled ? $"conversation: OpenAI key found ({source}); chat={_ai.ChatModel} stt={_ai.SttModel} tts={_ai.TtsModel}" : "conversation: no OpenAI key; text commands use the built-in parser, there is no voice");
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _ready = new();

    /// <summary>Short lines that are said again and again are spoken once and kept.</summary>
    async Task<byte[]> SpeakCached(string text, string mood, CancellationToken ct)
    {
        string key = mood + "|" + text;
        if (text.Length <= 24 && _ready.TryGetValue(key, out var have)) return have;
        byte[] pcm = await _ai.SpeakAsync(text, mood, ct);
        if (text.Length <= 24 && _ready.Count < 200) _ready[key] = pcm;
        return pcm;
    }

    // ---- (H1) "did it hear me?": character.heard / heardNote / heardAt travel with the next snapshot
    /// <summary>A voice clip was transcribed: these are the words as the partner understood them (80 characters at most). What it makes of them is not known yet ("").</summary>
    void Heard(string text)
    {
        var ch = _core.State.character;
        ch.heard = WorldRules.Heard(text); ch.heardNote = ""; ch.heardAt = HostClock.Now;
        _core.MarkDirty(false);
    }

    /// <summary>What the partner made of what it heard last: "not for me" (its name was not said), the action it took, or "" (it simply answers). Only for what was SPOKEN.</summary>
    void HeardAs(Utterance u, string note)
    {
        note = WorldRules.HeardNote(u?.source, note);
        var ch = _core.State.character;
        if (note == null || ch.heardNote == note) return;
        ch.heardNote = note;
        _core.MarkDirty(false);
    }

    void SetTalk(string talk)
    {
        if (_core.State.character.talk == talk) return;
        _core.State.character.talk = talk;
        _core.MarkDirty(false);
    }

    public void Tick(double now)
    {
        string talk = _core.State.character.talk;
        if (talk == "speaking" && now > _speakingUntil) SetTalk("");
        // a client that stopped listening without telling us, or a call that never came back, must not leave the partner frozen
        if (talk == "listening" && now - _listeningSince > 25) SetTalk("");
        if (talk == "thinking" && now - _thinkingSince > 45) SetTalk("");
    }

    // ------------------------------------------------------------------ input
    public void OnVoiceListen(Client c, VoiceListen v)
    {
        Log.Info($"voice: {c.Role} {(v.on ? "is listening" : "stopped listening")} {v.reason}");
        if (v.on) { _listeningSince = HostClock.Now; SetTalk("listening"); }
        else if (_core.State.character.talk == "listening") SetTalk("");
    }

    public void OnVoiceClip(Client c, VoiceClip clip)
    {
        byte[] pcm;
        try { pcm = Convert.FromBase64String(clip.pcm16 ?? ""); } catch { pcm = Array.Empty<byte>(); }
        double seconds = pcm.Length / 2.0 / Math.Max(1, clip.sampleRate);
        Log.Info($"voice clip from {c.Role}: {seconds:0.0}s at {clip.sampleRate} Hz, source={clip.source}");
        if (!Enabled) { Say("I can't hear you yet: voice needs an AI key on the PC.", "worried", 5, "no key"); return; }
        if (seconds < 0.25) { SetTalk(""); return; }
        if (seconds > 20) { pcm = pcm[..(20 * clip.sampleRate * 2)]; }
        // "microphone-open": the headset's ears are open and this was simply said in the room - perhaps to the partner,
        // perhaps not. It is listened to quietly (no "thinking" face), one at a time, and never while the partner speaks.
        bool open = clip.source == "microphone-open";
        if (open && seconds < 0.35) return;
        // one at a time; what is said meanwhile is not thrown away (it was: "Nilo!" said while an earlier sentence was
        // still being worked on simply vanished) - the newest waits its turn
        if (open && _openBusy) { _openNext = (c, clip); return; }
        if (open) _openBusy = true; else { _thinkingSince = HostClock.Now; SetTalk("thinking"); }
        string language = open && seconds <= 2.0 ? "en" : null;
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string text = await _ai.TranscribeAsync(pcm, clip.sampleRate, cts.Token, language);
                Log.Info($"voice: transcribed in {sw.ElapsedMilliseconds} ms: '{text}'");
                _core.Post(() =>
                {
                    if (open)
                    {
                        _openBusy = false;
                        if (_openNext.clip != null) { var next = _openNext; _openNext = default; _core.Post(() => OnVoiceClip(next.c, next.clip)); }
                        if (text.Trim().Length < 2) return;
                    }
                    if (text.Length == 0) { Say("I didn't catch that. Could you say it again?", "curious", 4, "empty transcript"); return; }
                    // what the recogniser made up (its own hint given back, a list of "밤비야, ..." sentences, the credits it writes over silence) was never said: HearRules
                    if (HearRules.Drop(text) is { Length: > 0 } madeUp)
                    {
                        Log.Info("voice: not taken as something said - " + madeUp);
                        if (!open) Say("I didn't catch that. Could you say it again?", "curious", 4, "conversation: unclear");
                        return;
                    }
                    Heard(text);        // (H1) "did it hear me?": the words as understood; what was made of them follows (HeardAs)
                    OnText(c, new Utterance { text = text, t0 = clip.t0, t1 = clip.t1, source = clip.source == "microphone" ? "voice" : "voice:" + clip.source });
                });
            }
            catch (Exception e) { if (open) { LastError = e.Message; Log.Warn("conversation (overheard): " + e.Message); _core.Post(() => { _openBusy = false; if (_openNext.clip != null) { var next = _openNext; _openNext = default; OnVoiceClip(next.c, next.clip); } }); } else Fail("I couldn't make out what you said just now.", e); }
        });
    }

    /// <summary>Something the user said, as text. Runs on the core thread.</summary>
    public void OnText(Client c, Utterance u)
    {
        // what the user pointed at WHILE speaking (not where the hand is when the answer arrives)
        PointerSample pt = _core.ResolvePointing(c.ClientId, u.t0, u.t1);
        bool open = u.source == "voice:microphone-open";
        // The partner answers when it is called by its name, and only then. ("It tries to answer everything I say" - with
        // the ears always open, every remark in the room used to go to the model.) A call with nothing else ("밤비야!")
        // is answered, and the ONE thing said next within a few seconds is taken as said to it. Text typed on the PC and
        // speech after the Talk button are addressed by definition.
        bool nameOnly;
        bool named = open ? NameRules.Wake(u.text, out nameOnly) : Named(u.text, out nameOnly);
        if (open && !named && HostClock.Now > _attentionUntil && !_core.Shop.Listening) { HeardAs(u, "not for me"); return; }
        if (named && nameOnly)
        {
            bool koName = u.text.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3);
            LastLanguage = koName ? "Korean" : "English";
            string[] lines = koName ? new[] { "응, 왜?", "응, 나 여기 있어!", "응, 말해 봐." } : new[] { "Yes?", "I'm here!", "Yes, tell me." };
            string hello = lines[_nameCalls++ % lines.Length];
            Log.Info($"conversation: called by name '{u.text.Trim()}'");
            _attentionUntil = HostClock.Now + 8;
            Say(hello, "happy", 3, "conversation: name");
            return;
        }
        string gameWish = NameRules.Strip(u.text);
        if (SightRules.WantsSip(gameWish) && (!open || named || HostClock.Now <= _attentionUntil))
        {
            if (open && !named) _attentionUntil = -1;
            LastLanguage = "English";
            var cup = SightRules.DrinkingCup(_core.State.world.items);
            if (cup == null) Say("Show me your cup first.", "curious", 3, "coffee");
            else if (_core.State.character.owner != Env.Mr) Say("Call me back into the room for a sip.", "curious", 3, "coffee");
            else
            {
                if (SightRules.RestoreCupScript(cup)) _core.World.Changed();
                _core.World.Run(c, new WorldCommand { cmd = "act", id = cup.id, text = "sip" }, true);
            }
            HeardAs(u, "actThing");
            return;
        }
        string sight = SightRules.Plain(gameWish) ?? (SightRules.WantsModelFile(gameWish) ? SightRules.Export : SightRules.WantsIdentification(gameWish) ? SightRules.Identify : null);
        if (sight != null && (!open || named || HostClock.Now <= _attentionUntil))
        {
            if (open && !named) _attentionUntil = -1;
            LastLanguage = gameWish.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) ? "Korean" : "English";
            _core.Sight.Act(c, sight, sight == SightRules.Export ? gameWish : "", u);
            HeardAs(u, sight);
            return;
        }
        if ((!open || named || HostClock.Now <= _attentionUntil) && SightRules.BagCommand(_core.State.world.items, gameWish) is { } placement)
        {
            if (open && !named) _attentionUntil = -1;
            LastLanguage = "English";
            _core.World.Run(c, placement, true);
            HeardAs(u, placement.cmd == "place" ? "placeItem" : "storeItem");
            return;
        }
        string tableAsk=gameWish.ToLowerInvariant();
        var boardIntent=IntentParser.Parse(tableAsk);
        if((boardIntent.kind=="playGame"&&boardIntent.name==StarterGames.GomokuId ||
            boardIntent.kind=="stopTennis"&&(tableAsk.Contains("오목")||tableAsk.Contains("gomoku")))&&
            (!open||named||HostClock.Now<=_attentionUntil))
        {
            if (open && !named) _attentionUntil = -1;
            bool stop=boardIntent.kind=="stopTennis";
            if(stop)_core.Table.Stop();
            string reason=stop?"":_core.Table.Request(pt?.monitorId);
            Say(reason.Length>0?reason:stop?"Gomoku finished.":"Opening Gomoku on the TV.","happy",4,"two-player TV board");
            HeardAs(u,stop?"stopTennis":"playGame");return;
        }
        if (Market.AskedForGame(gameWish))
        {
            if (open && !named && HostClock.Now > _attentionUntil) { HeardAs(u, "not for me"); return; }
            if (open && !named) _attentionUntil = -1;
            LastLanguage = u.text.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) ? "Korean" : "English";
            _core.Shop.Run(c, new MarketCommand { cmd = "make", id = ModuleKinds.Game, text = gameWish }, true);
            HeardAs(u, "makeModule");
            return;
        }
        if (!Enabled) { RunParser(c, u, pt); return; }
        // The partner has just asked something - "shall I put it up in the marketplace?" (Market.Reveal), or "I don't
        // have that game yet, shall I get it?" (Market.MissingGame): for 25 seconds what is said in the room may be
        // the answer, and an answer needs no name - like the one sentence after a bare "밤비야".
        // A plain yes or no is taken at once (below); anything else is shown to the model as something overheard,
        // which it may well leave unanswered.
        bool afterOffer = open && !named && HostClock.Now > _attentionUntil && _core.Shop.Listening;
        if (open && !named && !afterOffer)
        {
            if (HostClock.Now > _attentionUntil) { Log.Info($"conversation: not said to me (no name): '{u.text.Trim()}'"); HeardAs(u, "not for me"); return; }
            _attentionUntil = -1;      // the one sentence after the call
        }
        open = false;
        // The answer to that question in plain words ("응", "그래 올려줘", "20별에 올려줘", "아니", "됐어") is carried out at
        // once, with no model in between - with or without the name, while the question is fresh (later the model decides, which knows what else was said since).
        if (_core.Shop.Listening)
        {
            string before = LastLanguage;
            if (u.text.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) LastLanguage = "Korean";
            if (_core.Shop.AnswerInWords(NameRules.Strip(u.text)) is { } heard)
            {
                HeardAs(u, "answerOffer");
                _history.Add(("user", u.text)); _history.Add(("assistant", heard.Length > 0 ? heard : "(done)"));
                if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
                if (heard.Length == 0) SetTalk("");
                return;
            }
            LastLanguage = before;
        }
        open = afterOffer;         // (not an answer in plain words, and nobody was named: overheard - the model decides whether it was meant for the partner)
        // WHO DECIDES (round 4, 2026-11-18): the language model, given where the partner is, what is open and what the
        // user points at (Context). The rules that used to act before it - "plain commands" - opened the video site for
        // "여기 유튜브 로고 보여?", took "마켓플레이스 들어가 봐" for going into the screen and went into the screen for a
        // question about what is in it. Only a handful of exact short orders - stop, come out, quiet, cancel - are still
        // carried out at once (TurnRules.ExactOrder): a stop must never wait for a model. (With no model at hand, or
        // when its call fails, the built-in parser stands in: RunParser.)
        string said = u.text.Trim();
        string exact = afterOffer ? null : TurnRules.ExactOrder(said);
        if (exact != null)
        {
            bool ko = said.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3);
            LastLanguage = ko ? "Korean" : "English";
            var quick = IntentParser.Parse(said);
            if (quick.kind != exact) quick = new Intent { kind = exact, text = said };
            quick.alsoStart = false;
            var done = _core.ExecuteIntent(c, quick, u, pt);
            // (a stop says WHAT was stopped: that line is the host's own, already in the language being spoken)
            string line = quick.kind == "stop" && done.line.Length > 0 ? done.line : done.ok ? QuickLine(quick.kind, ko) : (ko ? KoLine(done.line) : done.line);
            Log.Info($"conversation: exact order '{said}' -> {quick.kind} ok={done.ok}");
            HeardAs(u, quick.kind);
            _history.Add(("user", u.text)); _history.Add(("assistant", line));
            if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
            if (!_core.IntentSpoke) Say(line, done.ok ? "happy" : quick.kind == "stop" ? "neutral" : "worried", 3, "conversation: command " + quick.kind);
            return;
        }
        if (!open) { _thinkingSince = HostClock.Now; SetTalk("thinking"); }
        // the reply follows the language of THIS message; the model alone drifts to the language used earlier in the talk
        string language = u.text.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) ? "Korean" : "English";
        LastLanguage = language;
        string system = TurnRules.SystemPrompt(Context(c, u, language, afterOffer ? "afterOffer" : open ? "overheard" : ""));
        var history = _history.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var turn = await _ai.ChatAsync(system, history, u.text, cts.Token, open);
                // HELD TO ITS WORD: a line that says it will do something, with no action behind it ("응, 바탕화면으로 갈게"
                // and nothing happened, 11:04:27), is asked again once - an action that does it, or an honest "I can't".
                if (turn.action == "none" && TurnRules.Promises(turn.reply))
                {
                    Log.Info($"conversation: '{turn.reply}' promises something with action none: asked again");
                    var again = await _ai.ChatAsync(system + TurnRules.PromiseNote, history, u.text, cts.Token, open);
                    if (again.action == "none" && TurnRules.Promises(again.reply)) again.reply = language == "Korean" ? "미안, 그건 아직 내가 못 해." : "Sorry, I can't do that yet.";
                    turn = again;
                }
                Log.Info($"conversation: reply in {sw.ElapsedMilliseconds} ms: action={turn.action} minutes={turn.minutes} start={turn.start} mood={turn.mood}{(string.IsNullOrEmpty(turn.thing) ? "" : $" thing='{turn.thing}'")}{(string.IsNullOrEmpty(turn.act) ? "" : $" act='{turn.act}'")}{(string.IsNullOrEmpty(turn.pc) ? "" : $" do='{turn.pc}'")} '{turn.reply}'");
                _core.Post(() => Apply(c, u, pt, turn));
            }
            catch (Exception e)
            {
                LastError = e.Message; Log.Warn("conversation: " + e.Message + (open ? " (overheard: ignored)" : " -> built-in parser"));
                if (!open) _core.Post(() => RunParser(c, u, pt));
            }
        });
    }

    /// <summary>
    /// Carried out at once, with no model in between: a PC action in its plainest wording (PcRules.Plain), something
    /// that is open put away (CloseRules.Plain), "방금 만든 거 틀어 봐" (PlayRules). true when the sentence was one of them.
    /// </summary>
    bool PlainNew(Client c, Utterance u, PointerSample pt, string said)
    {
        string bare = NameRules.Strip(said).Trim(' ', ',', '.', '!', '?', '~');
        if (bare.Length == 0 || bare.Length > 120) return false;
        string before = LastLanguage;
        LastLanguage = said.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) ? "Korean" : "English";
        string line = null, what = "";
        // (H6) documents in their plainest wordings: "... 알아보고 PDF로 만들어 줘", "이 아이콘 새로 만들어 줘", "PDF 보여 줘", "다음 페이지", "2번으로 할게", "컴퓨터에 넣어 줘"
        if (_core.Docs.Plain(bare) is { } doc) { line = _core.Docs.Act(c, doc, u); what = doc.action; }
        // (C1) "이거 만들어 줘" / "make this": what the user holds up is looked at and sculpted (SightRules.Plain)
        else if (SightRules.Plain(bare) is { } sight) { line = _core.Sight.Act(c, sight, "", u); what = sight; }
        else if (bare.Length > 80) { LastLanguage = before; return false; }
        else if (TurnRules.PlainDesktopOrder(bare))
        {
            var moved = _core.GoDesktop(c, u, pt);
            line = moved.line; what = "goDesktop";
            Say(line, moved.ok ? "happy" : "worried", 4, "conversation: goDesktop");
        }
        else if (bare.Length <= 40 && PlayRules.JustMade(bare) && PlayRules.PlayWord(bare) && !PlayRules.MakeWord(bare)) { line = _core.Shop.PlayLastMade(c, u, pt); what = "playGame"; }
        else if (CloseRules.Plain(bare) is { } close && !(IntentParser.Parse(said).kind is "closeMenu" or "openMenu"))
        {
            line = _core.CloseThing(c, close, u).line; what = "closeThing";
            Say(line, "neutral", 3, "conversation: closeThing");
        }
        else if (PcRules.Plain(bare) is { } pc)
        {
            // a program's name that is also something in the bag ("계산기 켜 줘" with the user's own calculator in it): the bag's - unless the PC is named
            if (pc.@do == "openApp" && !PlayRules.OnPc(bare) && _core.Shop.PlayNamed(c, "", bare, u, pt) is { } played) { line = played; what = "playGame"; }
            else
            {
                var o = _core.PcDo.Do(c, pc.@do, pc.arg, u);
                line = o.line; what = "pcDo";
                Say(line, o.ok ? "happy" : "worried", 4, "conversation: pcDo");
            }
        }
        if (line == null) { LastLanguage = before; return false; }
        Log.Info($"conversation: plain command '{said}' -> {what}");
        HeardAs(u, what);
        _history.Add(("user", u.text)); _history.Add(("assistant", line.Length > 0 ? line : "(done)"));
        if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
        if (_core.State.character.talk is "thinking" or "listening") SetTalk("");
        return true;
    }

    void RunParser(Client c, Utterance u, PointerSample pt)
    {
        if (!Enabled && PlainNew(c, u, pt, (u.text ?? "").Trim())) return;
        // no language model at hand: the answer to the partner's own "shall I put it up?" is still understood in plain words
        if (_core.Shop.Listening)
        {
            string was = LastLanguage;
            if (u.text.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) LastLanguage = "Korean";
            if (_core.Shop.AnswerInWords(NameRules.Strip(u.text)) is { } heard)
            {
                Log.Info($"utterance from {c.Role} [{u.source}]: '{u.text}' -> the answer to the partner's question (built-in wording)");
                if (heard.Length == 0) SetTalk("");
                return;
            }
            LastLanguage = was;
        }
        var it = IntentParser.Parse(u.text);
        // no language model at hand: the shop is still understood in its plainest wording ("상점 열어줘", "의자 사줘", "1번 화면 가져와")
        if (it.kind == "unknown")
        {
            string before = LastLanguage;
            LastLanguage = u.text.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) ? "Korean" : "English";
            if (_core.Shop.Plain(c, u.text) is { } said)
            {
                Log.Info($"utterance from {c.Role} [{u.source}]: '{u.text}' -> the shop (built-in wording)");
                if (said.Length == 0) SetTalk("");
                return;
            }
            LastLanguage = before;
        }
        // no language model at hand: the partner's own things and skills are still understood in their plainest wording
        // ("책상 하나 줄게", "춤춰", "노래 틀어 줘"); the Maker carries them out and says the line
        // ... and so is a glass game asked for by its name ("뱀파이어 서바이벌 하자", "play Shadow Umbrella"): GlassHost starts it and says the line
        if (it.kind == "unknown" && _core.Glass.Plain(c, u.text) is { } glassLine)
        {
            Log.Info($"utterance from {c.Role} [{u.source}]: '{u.text}' -> a glass game (built-in wording)");
            if (glassLine.Length == 0) SetTalk("");
            return;
        }
        if (it.kind == "unknown" && _core.World.Plain(u.text, true) is { } plain)
        {
            LastLanguage = u.text.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) ? "Korean" : "English";
            Log.Info($"utterance from {c.Role} [{u.source}]: '{u.text}' -> world {plain.cmd} (built-in wording)");
            if (_core.World.Run(c, plain, true).Length == 0) SetTalk("");
            return;
        }
        HeardAs(u, it.kind);
        var res = _core.ExecuteIntent(c, it, u, pt);
        if (_core.IntentSpoke) SetTalk("");         // (said already: a stop that put the room back)
        else if (res.line.Length > 0) Say(res.line, res.ok ? "happy" : "neutral", 4, "built-in parser: " + it.kind);
        else if (it.kind == "unknown") Say(Enabled ? "Sorry, I lost my train of thought. Could you say that again?" : "I only understand a few commands without my voice set up: timers, rain, come back, go in there.", "worried", 5, "unknown intent");
        else SetTalk("");
    }

    void Apply(Client c, Utterance u, PointerSample pt, AiTurn turn)
    {
        // overheard and not for the partner: nothing happens, nothing is remembered
        if (u.source == "voice:microphone-open" && string.IsNullOrWhiteSpace(turn.reply) && turn.action == "none") { Log.Info($"conversation: overheard, not for me: '{u.text}'"); HeardAs(u, "not for me"); return; }
        // "여기 어떤 폴더가 있어?" asked while pointing or looking at a display is ALWAYS looked at (Look.cs). The model
        // sometimes answers such a question from its notes instead - which are about where the user pointed BEFORE,
        // perhaps on another display: exactly the wrong answer. So that case is not left to it.
        if (turn.action == "none" && LookRules.AsksAboutASpot(u.text) && _core.Tasks.ReviewEnabled && _core.Tasks.ResolveLook(c, u.t0, u.t1).Pointed)
        {
            Log.Info($"conversation: '{u.text.Trim()}' asks about a place on a screen and the model answered without looking ('{turn.reply}'): looking instead");
            turn.action = "lookAtScreen";
        }
        // A QUESTION about something is never a MOVE ("화면 안에 뭐가 있어?" went INTO the screen): it is looked at.
        if (TurnRules.IsMove(turn.action) && TurnRules.AsksNotMoves(u.text))
        {
            Log.Info($"conversation: '{u.text.Trim()}' is a question with no order to go anywhere in it and the model chose {turn.action}: looking instead");
            turn.action = _core.Tasks.ReviewEnabled ? "lookAtScreen" : "none";
        }
        if ((turn.action is "readFile" or "none" or "pcOperate") && ScreenReading.Requested(u.text))
            turn.action = "lookAtScreen";
        HeardAs(u, turn.action);        // (H1) what was made of what was heard: the action taken, "" for a plain answer
        if (string.IsNullOrWhiteSpace(turn.reply)) turn.reply = LastLanguage == "Korean" ? "응!" : "Okay!";
        _turns++;
        // The partner's things and skills are the Maker's: it needs neither the headset's answer nor an intent, and it
        // knows what really happened (what is missing, what is still being made), so the line is its own. Only when it
        // has nothing to say is the model's reply shown - never two bubbles for one request.
        void Remembered(string line)
        {
            _history.Add(("user", u.text)); _history.Add(("assistant", string.IsNullOrEmpty(line) ? "(done)" : line));
            if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
            if (_core.State.character.talk is "thinking" or "listening") SetTalk("");
        }
        // something that is open is put away: the host knows what is open, and says what it closed
        if (turn.action == "closeThing")
        {
            string line = _core.CloseThing(c, turn.thing, u).line;
            Say(line, "neutral", 3, "conversation: closeThing");
            Remembered(line); return;
        }
        // MAKE or PLAY (PlayRules): a sentence with a word of playing and none of making never makes anything - what it
        // names in the bag is played or used ("계산기를 틀어볼래?" made a second calculator on 2026-11-17), and "방금 만든
        // 거" is what was made last.
        bool playWord = PlayRules.PlayWord(u.text) && !PlayRules.MakeWord(u.text);
        if (playWord && PlayRules.JustMade(u.text) && turn.action is "none" or "playGame" or "makeModule" or "makeItem" or "placeItem" or "useSkill" or "pcDo")
        {
            Log.Info($"conversation: '{u.text.Trim()}' asks for what was made last (the model chose {turn.action})");
            HeardAs(u, "playGame");
            Remembered(_core.Shop.PlayLastMade(c, u, pt)); return;
        }
        if (playWord && turn.action is "makeModule" or "makeItem")
        {
            string line = _core.Shop.PlayNamed(c, turn.thing, u.text, u, pt);
            Log.Info($"conversation: '{u.text.Trim()}' has a word of playing and none of making: nothing is made (the model chose {turn.action}) - {(line != null ? "what it names in the bag is played" : "and nothing in the bag is named")}");
            if (line == null)
            {
                line = LastLanguage == "Korean" ? "그런 건 아직 가방에 없어. 만들어 달라고 하면 만들어 줄게!" : "That isn't in my bag yet. Ask me to make it and I will!";
                Say(line, "curious", 4, "conversation: play, not make");
            }
            HeardAs(u, "playGame");
            Remembered(line); return;
        }
        // (H6) documents: the host makes, shows and carries them, and says what really happened
        if ((turn.action is "makeItem" or "makeDesigns" or "makeModule" or "makeReport" or "none" or "pcOperate") &&
            DocsRules.ExplicitFile(NameRules.Strip(u.text)) is {} file)
        {
            var requested = _core.Docs.Plain(NameRules.Strip(u.text)) ?? file;
            HeardAs(u, requested.action);
            Remembered(_core.Docs.Act(c, requested, u)); return;
        }
        // (C1) made from what the user shows / a thing carried into the PC as a 3D file: the host does it and says what really happened
        // ("이거 만들어 줘" names no thing: should the model still choose makeItem for it, it is the thing in the user's hand all the same - never a thing called "이거")
        if (turn.action == "makeItem" && SightRules.Plain(NameRules.Strip(u.text ?? "")) != null) { Log.Info($"conversation: '{u.text.Trim()}' names nothing to make (the model chose makeItem '{turn.thing}'): what the user holds up is looked at"); turn.action = SightRules.Make; turn.thing = ""; HeardAs(u, turn.action); }
        if (SightRules.IsAction(turn.action)) { Remembered(_core.Sight.Act(c, turn.action, (turn.thing ?? "").Trim(), u)); return; }
        if (DocsRules.Map(turn.action, turn.thing) is { } docAsk)
        {
            // (a report's topic the model left out: the sentence itself)
            if (docAsk.action == DocsRules.MakeReport && docAsk.arg.Length == 0) docAsk.arg = DocsRules.Topic(NameRules.Strip(u.text));
            string line = _core.Docs.Act(c, docAsk, u);
            Remembered(line); return;
        }
        // "바탕화면으로 와": out of the window it is bound to (also a minimised one) onto the desktop - from the room, into the PC first. The host says what it really did.
        if (turn.action == "goDesktop")
        {
            var d = _core.GoDesktop(c, u, pt);
            Say(d.line, d.ok ? "happy" : "worried", 4, "conversation: goDesktop");
            Remembered(d.line); return;
        }
        // working on the computer by seeing and acting (PcOperate.cs): a job in the work queue; the line now says that it has started (or why not), the result is said when it is over
        if (turn.action == "pcOperate")
        {
            var o = _core.PcDo.Operate(c, turn.thing, u);
            Say(o.line, o.ok ? "happy" : "worried", 4, "conversation: pcOperate");
            Remembered(o.line); return;
        }
        // something on the user's computer, from the fixed list - the host does it and says what really happened
        if (turn.action == "pcDo")
        {
            // (a program's name that is also something in the bag - the user's own calculator - is the bag's, unless the PC is named)
            if (turn.pc == "openApp" && !PlayRules.OnPc(u.text) && _core.Shop.PlayNamed(c, turn.arg, u.text, u, pt) is { } played) { HeardAs(u, "playGame"); Remembered(played); return; }
            var o = _core.PcDo.Do(c, turn.pc, turn.arg, u);
            Say(o.line, o.ok ? turn.mood : "worried", 4, "conversation: pcDo");
            Remembered(o.line); return;
        }
        string thing = string.IsNullOrWhiteSpace(turn.thing) ? u.text.Trim() : turn.thing.Trim();
        var world = turn.action switch
        {
            "makeItem" => new WorldCommand { cmd = "makeItem", text = thing },
            "teachSkill" => new WorldCommand { cmd = "teach", text = thing },
            "useSkill" => new WorldCommand { cmd = "useSkill", text = thing },
            "setAmbience" => new WorldCommand { cmd = "ambience", text = thing },
            "playMusic" => new WorldCommand { cmd = "music", text = (turn.thing ?? "").Trim() },       // "" is a wish of its own: any tune
            "doActivity" => new WorldCommand { cmd = "do", text = thing },
            "storeItem" => new WorldCommand { cmd = "store", text = thing },
            "placeItem" => new WorldCommand { cmd = "place", text = thing },
            // a thing with a script: worn / taken off, or asked for one of the acts it offers (the Maker finds the thing by its name)
            "wearThing" => new WorldCommand { cmd = (turn.act ?? "").Trim().ToLowerInvariant() is "off" or "false" or "unwear" ? "unwear" : "wear", id = thing },
            "actThing" => new WorldCommand { cmd = "act", id = string.IsNullOrWhiteSpace(turn.thing) ? "" : turn.thing.Trim(), text = string.IsNullOrWhiteSpace(turn.act) ? u.text.Trim() : turn.act.Trim() },
            _ => null,
        };
        // The same for the shop, a display brought over and a tune saved as a file: the Market carries them out and says the line.
        if (turn.action is "openMarket" or "installModule" or "removeModule" or "publishModule" or "makeModule" or "bringScreen" or "exportSong" or "setPrice" or "unpublishModule" or "answerOffer")
        {
            // (an empty thing is a wish of its own for two of them: the shop wherever the user is, the tune that is playing; the answer to the offer is yes / no, never the sentence)
            string words = turn.action is "openMarket" or "exportSong" or "answerOffer" ? (turn.thing ?? "").Trim() : thing;
            // (what goes through the hub - buying, selling - is answered a moment later by the Market itself: until then the model's own short reply stands)
            string line = _core.Shop.Act(c, turn.action, words, u, pt, turn.price);
            if (line.Length == 0) { line = turn.reply.Trim(); Say(line, turn.mood, Math.Clamp(2.5f + line.Length * 0.06f, 3f, 12f), "conversation: " + turn.action); }
            _history.Add(("user", u.text)); _history.Add(("assistant", line));
            if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
            return;
        }
        if (world != null)
        {
            string line = _core.World.Run(c, world, true);
            if (line.Length == 0) { line = turn.reply.Trim(); Say(line, turn.mood, Math.Clamp(2.5f + line.Length * 0.06f, 3f, 12f), "conversation: " + turn.action); }
            _history.Add(("user", u.text)); _history.Add(("assistant", line));
            if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
            return;
        }
        var it = new Intent { text = u.text, durationMs = (long)Math.Round(Math.Max(0, turn.minutes) * 60000.0), alsoStart = turn.start };
        it.kind = turn.action switch
        {
            "enterScreen" => "enterPortal", "comeBack" => "returnToMr", "lookAtScreen" => "lookScreen", "reviewScreen" => "analyze", "reviseScreen" => "revise", "playGame" => "playGame", "comeHere" => "comeHere",
            "createTimer" => "createTimer", "startTimer" => "timerStart", "pauseTimer" => "timerPause", "resumeTimer" => "timerResume",
            "resetTimer" => "timerReset", "setTimer" => "timerSetDuration",
            "rainOn" => "rainOn", "rainOff" => "rainOff", "quietOn" => "quietOn", "quietOff" => "quietOff", "cancel" => "cancel",
            "playTennis" => "playTennis", "stopTennis" => "stopTennis", "readFile" => "readFile", "calibrate" => "calibrate",
            "openMenu" => "openMenu", "closeMenu" => "closeMenu",
            _ => "unknown",
        };
        if (it.kind == "enterPortal" && turn.action == "enterScreen") it.alsoStart = false;       // going in and looking is the separate lookAtScreen action
        if (it.kind == "playGame")
        {
            // which game is worked out by the host from all three (GlassRules.PickGame): the words the model gave for
            // it (a pack's title) go before its choice of a built-in game, and nothing is started when neither names
            // a game in the bag
            it.text = (turn.game ?? "").Trim(); it.name = (turn.thing ?? "").Trim();
            Log.Info($"conversation: playGame game='{it.text}' thing='{it.name}'");
        }
        string reply = turn.reply.Trim(), mood = turn.mood;
        // looking takes a moment and the answer follows by itself: what is said meanwhile is a plain "let me look" (the
        // model liked to say "I would have to look again to know", which sounds like a refusal)
        if (it.kind == "lookScreen") reply = LastLanguage == "Korean" ? "잠깐, 볼게." : "Let me look.";
        if (it.kind != "unknown")
        {
            var res = _core.ExecuteIntent(c, it, u, pt);
            // the host knows what really happened: when the action could not be carried out, say that instead of the model's line
            if (!res.ok && res.line.Length > 0) { reply = res.line; mood = "worried"; }
            // a move that was handed on is said in the host's own words for THAT move: the model's line may be about something else ("차례로 하나씩 해 볼게" while it went into the screen)
            else if (res.ok && it.kind is "enterPortal" or "returnToMr" or "comeHere" or "calibrate" or "openMenu" or "closeMenu") reply = QuickLine(it.kind, LastLanguage == "Korean");
        }
        _history.Add(("user", u.text)); _history.Add(("assistant", reply));
        if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
        Say(reply, mood, Math.Clamp(2.5f + reply.Length * 0.06f, 3f, 12f), "conversation: " + turn.action);
    }

    /// <summary>
    /// What the model is told about the moment (TurnRules.SystemPrompt writes it out): where the partner is - the room,
    /// or which display and which window it is bound to -, what the user points or looks at, what is open and going
    /// on, and the sections of everything that has actions of its own. Core thread.
    /// (The titles of the user's windows go to the model with the sentence, as a picture of the screen does when it
    /// looks; they are never logged.)
    /// </summary>
    TurnContext Context(Client c, Utterance u, string language, string mode)
    {
        var st = _core.State; var ch = st.character;
        var x = new TurnContext { name = ch.name, personality = ch.personality, activity = ch.activity, language = language, mode = mode, insidePc = ch.owner == Env.Pc };
        var mons = _core.Desktop.Monitors;
        string Label(string monitorId) { var m = ScreenMath.FindMonitor(mons, monitorId); return m == null ? "a display" : "display " + (m.index + 1) + (m.primary ? " (the main one)" : ""); }
        static string Title(IntPtr h)
        {
            string t = "";
            try { t = h == IntPtr.Zero ? "" : Win32.WindowTitle(h) ?? ""; } catch { }
            t = t.Replace("\"", "'").Trim();
            return t.Length > 70 ? t[..70] + "..." : t;
        }
        {
            var pc = _core.Pc;
            bool bound = pc != null && pc.mode == "WorkspaceBound" && pc.boundHwnd != 0;
            x.where = TurnRules.Where(ch.owner == Env.Pc, bound, pc == null ? "" : Label(pc.monitorId), bound ? Title(new IntPtr(pc.boundHwnd)) : "", bound && pc.boundMinimized);
        }
        // what the user points or looks at - the same working-out as for looking (hand, then gaze, then the last seconds)
        uint own = (uint)Environment.ProcessId;
        var spot = _core.Tasks.ResolveLook(c, u.t0, u.t1);
        if (spot.Pointed && ScreenMath.FindMonitor(mons, spot.monitorId) is { } pm)
        {
            ScreenMath.UVToPixel(pm, spot.u, spot.v, out float px, out float py);
            string under = Title(Win32.WorkspaceWindowAt((int)px, (int)py, own));
            x.pointing = TurnRules.Pointing(spot.source == LookRules.Gaze, Label(pm.id), under);
        }
        IntPtr last = _core.Desktop.LastWorkspace;
        if (last != IntPtr.Zero && Win32.IsWorkspaceCandidate(last, own) && Title(last) is { Length: > 0 } lastTitle) x.open.Add(TurnRules.LastWindowLine(lastTitle));
        if (_core.Screen.Shown.Length > 0) x.open.Add(TurnRules.ScreenBoardLine(Label(_core.Screen.Shown)));
        if (_core.Docs.IsOpen) x.open.Add("A document you made is open on the board in the room (closeDoc puts it away).");
        if (_core.Shop.BoardOpen) x.open.Add("The shop's board is open in the room (closeThing with thing \"shop\" closes it).");
        foreach (var j in _core.Work.Open.Take(4)) x.open.Add($"In the work queue, being worked on now: \"{j.title}\" ({j.kind}{(j.step.Length > 0 ? "; " + j.step : "")}){(j.kind == "pc" ? " - you are operating the computer for it; cancel stops it" : "")}.");
        var tn = _core.Tennis.S;
        x.open.Add(tn.active ? $"You are playing tennis with the user right now: the user has {tn.scoreUser}, you have {tn.scorePartner} (game: {(tn.mode.Length > 0 ? tn.mode : "tennis")})."
            : _core.Glass.Active ? TurnRules.GameLine(_core.Glass.S.title, _core.Glass.S.side == GlassRules.Pc, _core.Glass.S.score, _core.Glass.S.level)
            : "No game is being played.");
        long now = HostClock.UnixMs;
        var timers = st.modules.Where(m => m.kind == "timer").ToList();
        x.state.Add(timers.Count == 0 ? "There is no timer." : "Timers: " + string.Join("; ", timers.Select(m => $"{m.name} {m.status}, {TimerLogic.Format(TimerLogic.Remaining(m, now))} left of {TimerLogic.Format(m.durationMs)}")));
        x.state.Add($"Virtual rain is {(st.settings.rain ? "on" : "off")}. Quiet mode is {(st.settings.quietMode ? "on" : "off")}.");
        x.state.Add($"Local time: {DateTime.Now:dddd HH:mm}.");
        // the computer (the fixed quick actions, then seeing-and-acting), documents, what it owns and has learned, the shop, what the installed abilities tell it to do
        x.sections = PcRules.PromptSection() + TurnRules.OperateSection() + _core.Docs.PromptSection() + _core.Sight.PromptSection() /* (C1) makeFromSight, exportThing */ + _core.World.PromptSection() + _core.Shop.PromptSection() + _core.Shop.AbilityPrompt();
        // (a check: LP_DUMP_SECTIONS names a file that gets what the host itself adds about documents, the world and the shop - the fixture of the recorded check of the tests)
        if (Environment.GetEnvironmentVariable("LP_DUMP_SECTIONS") is { Length: > 0 } dump)
            try { File.WriteAllText(dump, _core.Docs.PromptSection() + _core.World.PromptSection() + _core.Shop.PromptSection() + _core.Shop.AbilityPrompt()); } catch { }
        x.notes.AddRange(_notes);
        return x;
    }

    // ------------------------------------------------------------------ output
    /// <summary>
    /// Shows a line in the speech bubble and, when voice is available and allowed, speaks it. The bubble and the sound
    /// are sent together so that they start at the same moment; if the speech call fails the line is still shown.
    /// </summary>
    public void Say(string text, string mood, float seconds, string basis)
    {
        if (LastLanguage == "Korean") text = HostLines.Ko(text);
        // Text only. The partner does not speak aloud: the owner did not ask for a voice and did not want one. What it
        // says is the bubble over its head (in the room and on the PC), long enough to be read.
        var s = _core.State.settings;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (s.quietMode && !Asked(basis)) { Log.Info("quiet: not said: " + text); return; }
        _core.SayText(text, mood, Math.Clamp(Math.Max(seconds, 2.5f + text.Length * 0.11f), 3f, 16f), basis);
        if (_core.State.character.talk is "thinking" or "listening" or "speaking") SetTalk("");
    }

    /// <summary>Was this line asked for (an answer, the result of something the user started)? Quiet mode silences only the rest.</summary>
    static bool Asked(string basis) => basis.StartsWith("conversation") || basis.StartsWith("look") || basis.StartsWith("game") || basis.StartsWith("review") || basis.StartsWith("read") || basis.StartsWith("task") ||
                                       basis.StartsWith("tennis") || basis.StartsWith("calibration") || basis.StartsWith("debug") || basis.StartsWith("built-in") ||
                                       basis.StartsWith("world");      // a thing that was asked for arriving, a skill that was asked for starting

    static string QuickLine(string kind, bool ko) => kind switch
    {
        "enterPortal" => ko ? "응, 들어갈게!" : "Going in!",
        "returnToMr" => ko ? "지금 나갈게!" : "Coming out!",
        "comeHere" => ko ? "응, 갈게!" : "Coming!",
        "playTennis" => ko ? "좋아, 테니스 하자!" : "Let's play!",
        "stopTennis" => ko ? "그만할게." : "Okay, stopping.",
        "rainOn" => ko ? "비 내릴게." : "Here comes the rain.",
        "rainOff" => ko ? "비 그칠게." : "Rain off.",
        "openMenu" => ko ? "응, 가방 열게!" : "Here's my bag!",
        "closeMenu" => ko ? "응, 닫을게." : "Okay, I closed it.",
        "calibrate" => ko ? "화면 다시 찾을게. 모니터에 색이 잠깐 뜰 거야." : "Finding your screens again - they will flash a colour for a moment.",
        "quietOn" => ko ? "응, 조용히 있을게." : "Okay, I'll be quiet.",
        "cancel" => ko ? "응, 취소했어." : "Okay, cancelled.",
        _ => ko ? "응!" : "Okay!",
    };

    static string KoLine(string line) => line switch
    {
        "I'm already inside the PC." => "나 이미 화면 안에 있어.",
        "I'm already out here on the desk." => "나 이미 밖에 나와 있어.",
        "We are already playing!" => "이미 하고 있잖아!",
        "We are not playing right now." => "지금은 안 하고 있어.",
        "You can see it in the headset." => "헤드셋에서 볼 수 있어.",
        _ => line,
    };

    void Fail(string line, Exception e)
    {
        LastError = e.Message; Log.Warn("conversation: " + e.Message);
        _core.Post(() => { _core.SayText(line, "worried", 4, "error"); SetTalk(""); });
    }
}
