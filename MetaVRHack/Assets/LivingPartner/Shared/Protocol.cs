using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Shared
{
    public static class Proto
    {
        public const int Version = 1;
        public const int DefaultPort = 47610;
        public const int BeaconPort = 47611;
        public const string BeaconMagic = "LIVING-AI-PARTNER/1";

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Formatting = Formatting.None,
        };
        static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Pack(string type, object body, string id = null, long seq = 0, double t = 0)
        {
            var env = new Envelope { v = Version, type = type, id = id ?? Guid.NewGuid().ToString("N"), seq = seq, t = t, body = body == null ? null : JToken.FromObject(body, Serializer) };
            return JsonConvert.SerializeObject(env, Settings);
        }

        public static Envelope Parse(string json)
        {
            try { return JsonConvert.DeserializeObject<Envelope>(json, Settings); }
            catch { return null; }
        }

        public static string ToJson(object o, bool indented = false) => JsonConvert.SerializeObject(o, indented ? Formatting.Indented : Formatting.None, Settings);
        public static T FromJson<T>(string json) { try { return JsonConvert.DeserializeObject<T>(json, Settings); } catch { return default; } }
    }

    public sealed class Envelope
    {
        public int v;
        public string type;
        public string id;      // unique message id (used for de-duplication)
        public long seq;       // per-sender sequence
        public double t;       // host time (s) when sent by the host, sender-estimated host time otherwise
        public JToken body;
        public T Body<T>() where T : class { try { return body?.ToObject<T>(); } catch { return null; } }
    }

    /// <summary>Message type names.</summary>
    public static class Msg
    {
        public const string Hello = "hello", Welcome = "welcome", Reject = "reject";
        public const string PairRequest = "pair.request", PairResult = "pair.result";
        public const string Ping = "ping", Pong = "pong";
        public const string Snapshot = "state.snapshot";
        public const string CalibBegin = "calib.begin", CalibEnd = "calib.end", CalibCommit = "calib.commit", CalibClear = "calib.clear", CalibFlash = "calib.flash";
        public const string TargetQuery = "target.query", TargetResult = "target.result";
        public const string TransitionRequest = "transition.request", TransitionUpdate = "transition.update", TransitionEvent = "transition.event";
        public const string HeadPose = "head.pose";
        public const string PcStatus = "pc.status";
        public const string PointerSample = "pointer.sample";
        public const string Utterance = "utterance", IntentResult = "intent.result";
        public const string TaskCommand = "task.command", TaskUpdate = "task.update";
        public const string ModuleCommand = "module.command", ModuleUpdate = "module.update";
        public const string Say = "character.say";
        public const string Observation = "observation";
        public const string Debug = "debug.command", DebugReport = "debug.report";
        public const string Settings = "settings.update";
        public const string MrStatus = "mr.status";
        public const string VoiceListen = "voice.listen", VoiceClip = "voice.clip", VoiceReply = "voice.reply";
        public const string GameCommand = "game.command", GameState = "game.state";
        public const string World = "world.command";     // client -> host, body WorldCommand (World.cs); the result comes back in the snapshot's world
        public const string Market = "market.command";
        public const string GlassHand = "glass.hand";         // headset -> host -> PC client, body GlassHand (Glass.cs): where the user's hands are in front of a display
        public const string GlassCommand = "glass.command";   // any client -> host, body GlassCommand: start | stop | choose
        public const string GlassOut = "glass.out", GlassOutResult = "glass.out.result";   // things a game sends out of the glass toward the user, and what became of them (Glass.cs)
        public const string GlassState = "glass.state";
        public const string Doc = "doc.command";            // client -> host, body DocCommand (Docs.cs): the documents the partner made (reports, design options)       // host -> all (who runs it, which game), and the side that runs it -> host (the score ...): body GlassState   // client -> host, body MarketCommand (Market.cs); the result comes back in the snapshot's market (and world)
        public const string Work = "work.command";          // client -> host, body WorkCommand (Work.cs): the work queue (cancel | clear | open)
        public const string EyeRequest = "eye.request", EyeFrame = "eye.frame";   // host -> headset: ONE picture of what the user is holding up (body EyeRequest); headset -> host: the picture, or why there is none (body EyeFrame)
        public const string PcCommand = "pc.command";       // host -> PC client, body PcCommand: desktop (leave the window you are bound to and walk the desktop)
        public const string SwarmState = "swarm.state";     // headset -> host -> PC client, body SwarmGlass (Swarm.cs): the monsters of Screen Scamps that are behind the glass of a screen
    }

    public static class Roles { public const string Quest = "quest", Pc = "pc", Sim = "sim", Tool = "tool"; }
    public static class Env { public const string Mr = "mr", Pc = "pc"; }

    public sealed class Hello
    {
        public int protocol = Proto.Version;
        public string role = "";
        public string clientId = "";
        public string roomSession = "";  // A new Quest app launch, stable across socket reconnects.
        public string deviceName = "";
        public string token = "";       // pairing token (quest/sim/tool over LAN) or local secret (loopback pc)
        public long lastSeq;            // last host sequence seen (for reconnect diagnostics)
    }

    public sealed class Welcome
    {
        public int protocol = Proto.Version;
        public string sessionId = "";
        public double hostTime;
        public string hostName = "";
        public bool resumed;
    }

    public sealed class Reject { public string reason = ""; public int protocol = Proto.Version; }
    public sealed class PairRequest { public string code = ""; public string deviceName = ""; public string clientId = ""; }
    public sealed class PairResult { public bool ok; public string token = ""; public string reason = ""; }
    public sealed class Ping { public double c; }
    public sealed class Pong { public double c; public double h; }

    public sealed class TargetQuery
    {
        public string queryId = "";
        public string monitorId = "";
        public float u, v;              // normalised position on the lit display area
        public float px, py;            // used instead of u,v when usePixel is true (PC-originated requests)
        public bool usePixel;
        public int direction;           // 0 MR->PC, 1 PC->MR
        public bool preview;            // preview queries are not stored
    }

    public sealed class TargetInfo
    {
        public string queryId = "";
        public bool valid;
        public bool adjusted;           // true when the requested point had to move; must be shown and confirmed
        public string reason = "";
        public string monitorId = "";
        public float reqPxX, reqPxY;    // what the user asked for (physical px, virtual desktop)
        public float pxX, pxY;          // resolved portal pixel
        public float u, v;
        public string anchorKind = "screen"; // "screen" = fixed display point, "window" = follows the window
        public long hwnd;
        public string windowTitle = "";
        public string processName = "";
        public RectI windowRect;        // visible frame of the window (physical px)
        public float winU, winV;        // position relative to the window when anchorKind == "window"
        public float floorPxY;          // y of the surface the partner stands on inside the PC
        public float bottomEdgePxY;     // bottom of the allowed area
        public float topEdgePxY;        // top of the lit display area
        public float metersPerPixel;
        public string sizeSource = "";  // calibration | edid | default
        public double resolvedAt;
    }

    public sealed class TransitionRequest
    {
        public string requestId = "";
        public int direction;
        public TargetInfo target;
        public V3 mrFootLocal;          // MR launch/landing point relative to the portal point, in the display frame (m)
        public V3 worldUpLocal = new V3(0, 1, 0); // world up in the display frame (tilt); from calibration
        public bool debugLoop;
    }

    public enum TState { Idle, TargetSelected, Approaching, ReceiverReady, Crossing, Settling, Complete, Hold, Recovering, Cancelled }
    public enum TEvent { Select, BeginApproach, ApproachDone, ReceiverReady, StartCrossing, CrossingFinished, Settled, Hold, Resume, Cancel, RecoverDone }

    public sealed class TransitionInfo
    {
        public string transitionId = "";
        public string characterId = "";
        public int direction;
        public string state = nameof(TState.Idle);
        public string heldState = "";
        public string reason = "";
        public string owner = Env.Mr;       // environment that owns the whole character outside of Crossing
        public string source = Env.Mr, destination = Env.Pc;
        public bool approachDone, receiverReady;
        public TargetInfo target;
        public CrossingPlan plan;
        public CrossClock clock;
        public double startedAt, updatedAt;
        public int revision;
    }

    public sealed class TransitionEventMsg
    {
        public string transitionId = "";
        public string kind = "";   // approachDone | receiverReady | settled | cancel | hold | resume | renderBeat
        public string from = "";
        public string reason = "";
        public float tau;
        public float progress;
        public float visibleFraction;
        public V3 vec;             // geometry payload (MR standing point in the portal frame for approachDone / receiverReady)
    }

/// <summary>Host -> PC client (Msg.PcCommand). cmd: desktop = leave the window the partner is bound to (also when it is minimised) and walk the desktop of monitorId ("" = the display it is on).</summary>
    public sealed class PcCommand { public string cmd = ""; public string monitorId = ""; }

    public sealed class HeadPose { public string monitorId = ""; public V3 eye; public bool tracked; public double t; }

    public sealed class PcStatus
    {
        public bool overlayVisible;
        public string mode = "DesktopRoaming";   // DesktopRoaming | WorkspaceBound | Hidden | Suspended
        public string monitorId = "";
        public float pxX, pxY;                    // feet position (physical px)
        public long boundHwnd;
        public bool boundMinimized;
        public string suspendedReason = "";
        public float fps;
        public string design = "";                // the look this client is actually rendering (PartnerRig.DesignId)
    }

    public sealed class MrStatus
    {
        public bool tracking = true;
        public bool handsTracked;
        public bool onHand;
        public string spaceEpoch = "";
        public string activity = "";
        public float fps;
        public bool simulated;
        public string design = "";                // the look this client is actually rendering
    }

    public sealed class PointerSample
    {
        public double t; public string monitorId = ""; public float u, v; public bool pinch; public bool valid;
        // where the user is LOOKING (the middle of their view met a display): used when no hand was pointing
        public bool gazeValid; public string gazeMonitorId = ""; public float gazeU, gazeV;
    }
    public sealed class Utterance { public string text = ""; public double t0, t1; public string source = ""; }

    public sealed class SayMsg { public string text = ""; public string mood = "neutral"; public float seconds = 4f; public string basis = ""; }

    /// <summary>A client tells the host that it has started (or stopped) recording the user's voice.</summary>
    public sealed class VoiceListen { public bool on; public string reason = ""; }

    /// <summary>
    /// One recorded utterance, sent only after the user asked to talk (a pinch on the partner or the Talk button).
    /// 16-bit mono PCM, base64. t0 / t1 are host times of the first and last sample, so that what the user was pointing
    /// at WHILE speaking can be looked up.
    /// </summary>
    public sealed class VoiceClip
    {
        public string clipId = "";
        public int sampleRate = 16000;
        public string pcm16 = "";
        public double t0, t1;
        public string source = "microphone";   // microphone | test-file (a recording fed in by a check script, labelled as such)
    }

    /// <summary>What the partner says out loud: the same words as the speech bubble, as 16-bit mono PCM (base64).</summary>
    public sealed class VoiceReply
    {
        public string text = "";
        public string mood = "neutral";
        public int sampleRate = 24000;
        public string pcm16 = "";
        public string playOn = Env.Mr;         // the side the partner is on plays it
        public bool followUp;                  // an answer in a conversation: the listener may go on talking without asking again
        public float seconds;
    }

    /// <summary>
    /// The partner's looks. Ids match the PartnerDesign assets on the Partner prefab. There is one: the owner's own
    /// character model. The list and the shared `design` field stay so that another look can be added as an asset.
    /// </summary>
    public static class Designs
    {
        public static readonly string[] Ids = { "momo" };
        public static string Next(string current)
        {
            int i = System.Array.IndexOf(Ids, current);
            return Ids[(i + 1 + Ids.Length) % Ids.Length];
        }
    }

    public sealed class CharacterInfo
    {
        public string characterId = "partner-001";
        public string name = "Nilo";
        public string design = "momo";     // which look both clients show: one of Designs.Ids
        public float heightM = 0.10f;
        public string personality = "curious, gentle, careful; suggests rather than asserts";
        public string owner = Env.Mr;
        public string activity = "idle";   // idle | analysis | modify | test | done | error | approval
        public string mood = "calm";
        public string talk = "";           // "" | listening | thinking | speaking  (the conversation, shown by both clients)
        public bool quietMode;
        public string heard = "";         // the last thing the partner heard the user say, as it understood the words (shown for a few seconds: "did it hear me?")
        public string heardNote = "";     // what it made of it: "" (it is answering) | "not for me" (its name was not said) | the action it took
        public double heardAt;             // host time
    }

    public sealed class TaskInfo
    {
        public string taskId = "";
        public string kind = "";             // analysis (code, through the harness) | review (a screen or picture) | read (a file) | change | publish
        public string title = "";
        public string status = "TaskQueued";
        public string projectPath = "";
        public string harness = "";
        public string sessionId = "";
        public long hwnd;
        public string detail = "";
        public string proposalId = "";
        public int exitCode = -1;
        public double createdAt, updatedAt;
        public List<TaskEventRec> events = new List<TaskEventRec>();
        public List<ProposalModule> modules = new List<ProposalModule>();
        public List<string> changedFiles = new List<string>();
        public string diffSummary = "";
        public string testOutput = "";
        public string branch = "";
        public string commit = "";
        public bool isTestDouble;            // true when produced by a labelled stub harness (never real evidence)
        public string outputDir = "";        // review / read: the folder on the PC the result was saved in
        public List<string> outputs = new List<string>();   // review / read: the files written there (the first one is the document to open)
    }

    public sealed class TaskEventRec { public string name = ""; public double t; public string detail = ""; }

    public sealed class ProposalModule
    {
        public string moduleId = "";
        public string name = "";
        public List<string> files = new List<string>();
        public string uiEvidence = "";
        public string role = "";
        public string evidence = "";
        public string proposal = "";
        public string impact = "";
        public string confidence = "";     // confirmed | inferred
        public string taskId = "";
        public string status = "proposed";  // proposed | selected | changing | testing | passed | failed
        public string testResult = "";
    }

    public sealed class TaskCommand
    {
        public string cmd = "";     // analyze | read (projectPath = a file, or the file shown in the window) | select | cancel | approvePublish | preview | dismiss
        public string taskId = "";
        public string moduleId = "";
        public string projectPath = "";
        public string prompt = "";
        public long hwnd;
        public float pxX, pxY;
    }

    public sealed class ModuleCommand
    {
        public string cmd = "";    // create | start | pause | resume | reset | setDuration | setDisplay | setAlert | delete | place
        public string moduleId = "";
        public string kind = "timer";
        public string name = "";
        public long durationMs;
        public string value = "";
        public V3 position;
    }

    /// <summary>
    /// The headset asks the PC to light displays with a plain colour so that its camera can find them (repeated a few
    /// times a second while it is looking; an empty list, or silence, takes the colours away).
    /// </summary>
    public sealed class CalibFlash
    {
        public List<FlashItem> items = new List<FlashItem>();
        public string stage = "";     // "" | "hint" (nothing seen yet) | "done" | "partial": the partner says a line about it
        public int found, total;
    }

    public sealed class FlashItem
    {
        public string monitorId = "";
        public int r, g, b;
    }

    /// <summary>
    /// Host -> headset (Msg.EyeRequest): "the user shows you something - take ONE picture of it". The headset asks for
    /// its camera only now (never at start), shows that the partner is looking while it does, and reads no further
    /// picture once it has answered.
    /// </summary>
    public sealed class EyeRequest
    {
        public string id = "";          // comes back in the EyeFrame
        public int maxSide = 1280;      // the longest side of the picture, in pixels
    }

    /// <summary>Headset -> host (Msg.EyeFrame): the one picture (a JPEG, cut round the user's hands when they are seen), or why there is none.</summary>
    public sealed class EyeFrame
    {
        public string id = "";
        public bool ok;
        public string why = "";         // not ok: denied (the user did not allow the camera) | nocamera (no pictures arrive: an older system, a start-up fault) | simulator (the desktop simulator has no camera)
        public string jpeg = "";        // base64
        public int width, height;
        public bool cropped;            // cut round the hands (else the middle of the view)
    }

    public sealed class DebugCommand
    {
        public string cmd = "";   // freeze | play | slow | rewind | loop | overlay | selftest | gotoPixel | returnMr | say
        public float value;
        public string text = "";
        public float px, py;
    }

    public sealed class Observation
    {
        public string source = "";      // passthrough-camera | screen-capture | simulated
        public double t;
        public float confidence;
        public string fact = "";
        public string detail = "";
    }

    public sealed class SettingsMsg
    {
        public bool quietMode;
        public bool cameraEnabled = true;
        public bool micEnabled = true;     // voice conversation allowed (the microphone is only open after the user asks to talk)
        public bool voiceReplies = true;   // the partner answers out loud as well as in its speech bubble
        public float adviceIntervalSec = 600;
        public bool rain;
        public bool debugOverlay;
    }

    public sealed class StateSnapshot
    {
        public string sessionId = "";
        public double hostTime;
        public long seq;
        public CharacterInfo character = new CharacterInfo();
        public List<MonitorDesc> monitors = new List<MonitorDesc>();
        public List<MonitorCalibration> calibrations = new List<MonitorCalibration>();
        public TransitionInfo transition = new TransitionInfo();
        public PcStatus pc;
        public MrStatus mr;
        public List<TaskInfo> tasks = new List<TaskInfo>();
        public List<ModuleDef> modules = new List<ModuleDef>();
        public SettingsMsg settings = new SettingsMsg();
        public string calibratingMonitorId = "";
        public List<string> connectedRoles = new List<string>();
        public string returnPolicy = "restoreOrCall"; // what happens when analysis ends while the bound window is minimised
        public TennisState tennis;                    // the tennis game, when one is being played (also sent on its own as game.state)
        public WorldState world = new WorldState();   // the partner's things, skills, tune and surroundings (World.cs); world.revision goes up with every change
        public MarketView market = new MarketView();  // the marketplace: the catalogue, what is installed, the stars (Market.cs); market.revision goes up with every change
        public GlassState glass = new GlassState();   // a game played at / through the glass of a display, or on the desk (Glass.cs); game "" = none
        public ShowInfo show = new ShowInfo();        // something the partner is visibly doing in the PC right now (Glass.cs); kind "" = nothing
        public DocsView docs = new DocsView();        // documents the partner made for the user (Docs.cs): shown on a board in the room, carried into the PC
        public ScreenBoard screen = new ScreenBoard(); // a display of the PC brought into the room (GET /screen/<revision>.jpg); monitorId "" = none
        public WorkView work = new WorkView();        // the work queue (Work.cs): what the partner is making or doing for the user, in the room (hammered) or in the PC workshop
    }
}
