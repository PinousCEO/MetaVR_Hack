using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LivingPartner.Shared;

namespace LivingPartner.Character
{
    /// <summary>
    /// Client connection to the Companion Host (used by the Windows character client, the Quest client and the simulator).
    /// Handles authentication/pairing, protocol version check, reconnect with backoff, clock sync, state snapshots and
    /// duplicate-message suppression. Network I/O runs on a worker task; messages are delivered on the caller's thread by Pump().
    /// </summary>
    public sealed class HostLink : IDisposable
    {
        public string Role = Roles.Pc, ClientId = Guid.NewGuid().ToString("N"), DeviceName = "", Token = "";
        readonly string _roomSession = Guid.NewGuid().ToString("N");
        public Func<List<string>> UrlProvider;
        public string PairCode = "";

        public bool Connected { get; private set; }
        public bool NeedsPairing { get; private set; }
        public string LastError { get; private set; } = "";
        public string CurrentUrl { get; private set; } = "";
        public readonly ClockSync Clock = new ClockSync();
        public StateSnapshot Snapshot { get; private set; }
        public int ReconnectCount { get; private set; }
        public int DuplicatesDropped { get; private set; }
        /// <summary>State snapshots that were dropped unread because a newer one was already waiting behind them.</summary>
        public int SnapshotsSkipped { get; private set; }

        public event Action<Envelope> OnMessage;
        public event Action<bool> OnConnectionChanged;
        public event Action<StateSnapshot> OnSnapshot;
        public event Action<string> OnTokenIssued;

        static readonly Stopwatch Sw = Stopwatch.StartNew();
        public static double LocalNow => Sw.Elapsed.TotalSeconds;
        public double HostNow => LocalNow + Clock.Offset;

        readonly ConcurrentQueue<(string json, double recv)> _in = new ConcurrentQueue<(string, double)>();
        double _recvTime;
        // the messages taken out of _in by Pump, and how many of them have been delivered
        readonly List<(string json, double recv)> _batch = new List<(string json, double recv)>();
        int _batchAt;
        readonly ConcurrentQueue<string> _out = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        readonly SemaphoreSlim _outSignal = new SemaphoreSlim(0);
        readonly HashSet<string> _seen = new HashSet<string>();
        readonly Queue<string> _seenOrder = new Queue<string>();
        CancellationTokenSource _cts;
        volatile ClientWebSocket _ws;
        long _seq;
        double _nextPing;
        int _fastPings;

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Task.Run(() => RunAsync(ct));
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _ws?.Abort(); } catch { }
            _cts = null;
        }

        // ------------------------------------------------------------------ playing without the PC
        // A stand-in for the host behind this same link (MR/LocalWorld.cs): while it is on, nothing goes over the network.
        // What the app sends is handed to the stand-in as the objects they are, and what the stand-in answers - state
        // snapshots and the few other messages - is delivered by Pump through the same events as a host's messages.
        /// <summary>The link is served by a stand-in inside this app (GoLocal), not by a host on a PC.</summary>
        public bool Local { get; private set; }
        Action<string, object> _localSend;
        readonly Queue<(string type, object body)> _localIn = new Queue<(string type, object body)>();

        /// <summary>From now on `send` gets what the app sends, and the network is left alone. Counts as connected.</summary>
        public void GoLocal(Action<string, object> send)
        {
            try { _cts?.Cancel(); } catch { }
            try { _ws?.Abort(); } catch { }
            _cts = null;
            while (_in.TryDequeue(out _)) { }
            _batch.Clear(); _batchAt = 0;
            _localSend = send; Local = true; NeedsPairing = false; CurrentUrl = ""; LastError = "";
            if (!Connected) { Connected = true; ReconnectCount++; OnConnectionChanged?.Invoke(true); }
        }

        /// <summary>Back to looking for a host on the network.</summary>
        public void LeaveLocal()
        {
            if (!Local) return;
            Local = false; _localSend = null; _localIn.Clear(); LastError = "";      // (not the "Aborted" of the loop that GoLocal stopped)
            if (Connected) { Connected = false; OnConnectionChanged?.Invoke(false); }
            Start();
        }

        /// <summary>
        /// A message for the app from inside the app: delivered by the next Pump like one from a host (a StateSnapshot as
        /// the object it is - it is not written out and read back). Also used, in any mode, for the debug commands of a
        /// script file (MrLocalHost).
        /// </summary>
        public void DeliverLocal(string type, object body) => _localIn.Enqueue((type, body));

        void PumpLocal()
        {
            for (int n = _localIn.Count; n > 0 && _localIn.Count > 0; n--)      // (what a listener queues meanwhile waits for the next frame)
            {
                var m = _localIn.Dequeue();
                var env = new Envelope { v = Proto.Version, type = m.type, id = "", seq = 0, t = HostNow };
                if (m.body is StateSnapshot snap) { Handle(env, snap); continue; }
                env.body = m.body == null ? null : Newtonsoft.Json.Linq.JToken.FromObject(m.body);
                Handle(env);
            }
        }

        public void Send(string type, object body)
        {
            if (Local) { _localSend?.Invoke(type, body); return; }
            if (!Connected) return;
            _out.Enqueue(Proto.Pack(type, body, null, Interlocked.Increment(ref _seq), HostNow));
            _outSignal.Release();
        }

        // Live camera previews expire when the receiver's turn changes. Give commands
        // and voice priority, and never accumulate preview frames on slow Wi-Fi.
        public void SendPreview(string type, object body)
        {
            if (!Local && Connected && _out.IsEmpty) Send(type, body);
        }

        /// <summary>Call every frame on the main thread.</summary>
        public void Pump()
        {
            while (_mainThread.TryDequeue(out var a)) { try { a(); } catch (Exception e) { LastError = e.Message; } }
            if (_localIn.Count > 0) PumpLocal();
            if (Local) return;      // (no network: nothing is waiting, nobody is pinged)
            // What is waiting is taken out first. A state snapshot is the whole state (about 45 kB of text, several a
            // second), and reading one takes the main thread a while; after a pause a whole row of them is waiting. Each
            // one replaces the one before it, so only the NEWEST of those waiting is read - the older ones are dropped
            // unread. Every other message is still delivered, in the order it came.
            if (_batchAt >= _batch.Count)
            {
                _batch.Clear(); _batchAt = 0;
                int budget = 256;
                while (budget-- > 0 && _in.TryDequeue(out var item)) _batch.Add(item);
            }
            int newest = -1;
            for (int i = _batch.Count - 1; i >= _batchAt; i--) if (IsSnapshot(_batch[i].json)) { newest = i; break; }
            while (_batchAt < _batch.Count)
            {
                int i = _batchAt++;      // (counted first: should a listener throw, the rest is delivered by the next call, as before)
                string json = _batch[i].json; _recvTime = _batch[i].recv;
                _batch[i] = default;
                bool snapshot = i <= newest && IsSnapshot(json);
                if (snapshot && i < newest) { SnapshotsSkipped++; continue; }
                Envelope env = null; StateSnapshot snap = null;
                if (snapshot)
                {
                    // read straight into the state, once. (Through the envelope it was read twice: first the whole text
                    // into a tree of tokens, then that tree into the state.)
                    var se = Proto.FromJson<SnapshotEnvelope>(json);
                    if (se != null && se.type == Msg.Snapshot && se.body != null) { snap = se.body; env = new Envelope { v = se.v, type = se.type, id = se.id, seq = se.seq, t = se.t }; }
                }
                if (env == null) env = Proto.Parse(json);
                if (env == null || string.IsNullOrEmpty(env.type)) continue;
                if (!string.IsNullOrEmpty(env.id))
                {
                    if (!_seen.Add(env.id)) { DuplicatesDropped++; continue; }
                    _seenOrder.Enqueue(env.id);
                    if (_seenOrder.Count > 1024) _seen.Remove(_seenOrder.Dequeue());
                }
                Handle(env, snap);
            }
            if (Connected && LocalNow >= _nextPing)
            {
                _nextPing = LocalNow + (_fastPings-- > 0 ? 0.15 : 2.0);
                Send(Msg.Ping, new Ping { c = -1.0 });
            }
        }

        /// <summary>
        /// Is this message a state snapshot? Told from the start of the text, without reading the message: the envelope
        /// is written with its type in front of its body (Proto.Pack). A text this does not recognise is simply read
        /// like any other message.
        /// </summary>
        static bool IsSnapshot(string json) =>
            json != null && json.IndexOf("\"type\":\"" + Msg.Snapshot + "\"", 0, Math.Min(json.Length, 96), StringComparison.Ordinal) >= 0;

        /// <summary>The envelope of a state snapshot with its body as what it is (the fields of Envelope, read by name).</summary>
        sealed class SnapshotEnvelope { public int v = 0; public string type = "", id = ""; public long seq = 0; public double t = 0; public StateSnapshot body = null; }

        /// <summary>snap: the state of a snapshot that has been read already (null: it is read from the envelope's body).</summary>
        void Handle(Envelope env, StateSnapshot snap = null)
        {
            switch (env.type)
            {
                case Msg.Pong:
                    var pong = env.Body<Pong>();
                    if (pong != null) Clock.AddSample(pong.c, pong.h, _recvTime);
                    return;
                case Msg.Welcome:
                    NeedsPairing = false;
                    break;
                case Msg.Reject:
                    var rej = env.Body<Reject>();
                    LastError = rej?.reason ?? "rejected";
                    if (LastError == "pairing-required") NeedsPairing = true;
                    break;
                case Msg.PairResult:
                    var pr = env.Body<PairResult>();
                    if (pr != null && pr.ok) { Token = pr.token; NeedsPairing = false; OnTokenIssued?.Invoke(Token); SendHello(); }
                    else LastError = pr?.reason ?? "pairing failed";
                    break;
                case Msg.Snapshot:
                    // (the envelope that goes on to OnMessage below carries no body then: the state is what OnSnapshot hands over)
                    if (snap == null) snap = env.Body<StateSnapshot>();
                    if (snap != null) { Snapshot = snap; OnSnapshot?.Invoke(snap); }
                    break;
            }
            OnMessage?.Invoke(env);
        }

        public void SubmitPairCode(string code)
        {
            PairCode = code;
            RawSend(Proto.Pack(Msg.PairRequest, new PairRequest { code = code, deviceName = DeviceName, clientId = ClientId }));
        }

        void SendHello() => RawSend(Proto.Pack(Msg.Hello, new Hello { role = Role, clientId = ClientId, deviceName = DeviceName, token = Token, roomSession = Role == Roles.Quest ? _roomSession : "" }));
        void RawSend(string json) { _out.Enqueue(json); _outSignal.Release(); }

        async Task RunAsync(CancellationToken ct)
        {
            int backoff = 400; int urlIndex = 0;
            while (!ct.IsCancellationRequested)
            {
                List<string> urls = null;
                try { urls = UrlProvider?.Invoke(); } catch { }
                if (urls == null || urls.Count == 0) { await SafeDelay(700, ct); continue; }
                string url = urls[urlIndex++ % urls.Count];
                var ws = new ClientWebSocket();
                try
                {
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(2500);
                        await ws.ConnectAsync(new Uri(url), timeout.Token).ConfigureAwait(false);
                    }
                    _ws = ws; CurrentUrl = url;
                    while (_out.TryDequeue(out _)) { }
                    SendHello();
                    _mainThread.Enqueue(() => { if (ct.IsCancellationRequested) return; Connected = true; _fastPings = 8; _nextPing = 0; ReconnectCount++; OnConnectionChanged?.Invoke(true); });      // (cancelled: the app went local meanwhile)
                    backoff = 400;
                    var sender = SendLoop(ws, ct);
                    await ReceiveLoop(ws, ct).ConfigureAwait(false);
                    try { ws.Abort(); } catch { }
                    await Task.WhenAny(sender, Task.Delay(200)).ConfigureAwait(false);
                }
                catch (Exception e) { LastError = e.GetType().Name + ": " + e.Message; }
                finally
                {
                    bool was = _ws == ws;
                    if (was) _ws = null;      // (not the socket of a loop that was started after this one was cancelled: GoLocal, then LeaveLocal)
                    try { ws.Dispose(); } catch { }
                    if (was) _mainThread.Enqueue(() => { if (Connected && !Local && !ct.IsCancellationRequested) { Connected = false; OnConnectionChanged?.Invoke(false); } });
                }
                await SafeDelay(backoff, ct);
                backoff = Math.Min(backoff * 2, 3000);
            }
        }

        static async Task SafeDelay(int ms, CancellationToken ct) { try { await Task.Delay(ms, ct).ConfigureAwait(false); } catch { } }

        async Task SendLoop(ClientWebSocket ws, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    await _outSignal.WaitAsync(250, ct).ConfigureAwait(false);
                    while (ws.State == WebSocketState.Open && _out.TryDequeue(out var json))
                    {
                        if (json.Contains("\"type\":\"ping\"")) json = json.Replace("\"c\":-1.0", "\"c\":" + LocalNow.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                        var bytes = Encoding.UTF8.GetBytes(json);
                        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                    }
                }
            }
            catch { }
        }

        async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[64 * 1024];
            var sb = new System.IO.MemoryStream();
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                if (res.MessageType == WebSocketMessageType.Close) break;
                sb.Write(buf, 0, res.Count);
                if (res.EndOfMessage)
                {
                    _in.Enqueue((Encoding.UTF8.GetString(sb.GetBuffer(), 0, (int)sb.Length), LocalNow));
                    sb.SetLength(0);
                }
            }
        }
    }
}
