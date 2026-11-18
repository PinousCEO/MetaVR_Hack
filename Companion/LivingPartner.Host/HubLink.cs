using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>Who the user is on one hub. The token is the key to their stars: it is kept protected (see HubLink) and never logged.</summary>
public sealed class HubIdentity
{
    public string userId = "", token = "", nickname = "";
    public bool generated;              // the nickname was made up by the host on first contact; the user has not chosen one yet
    public string hubId = "";           // which hub the account is on (GET /api/hello); "" = the hub never said (an older hub, or an account from before this was kept)
}

/// <summary>
/// The host's line to the marketplace hub: where the hub is, who the user is there, and - when no hub is named and
/// none answers on this PC - the hub itself, started from the host's own folder as a hidden child process.
///   Where: the environment variable LP_MARKET_HUB, else the address the user saved (market.hub in the state file,
///   `lpctl hub &lt;address&gt;`), else http://127.0.0.1:47620.
///   Who: on first contact the host registers by itself under a friendly made-up nickname, so that buying and selling
///   work at once; the user renames it when they like. One identity per hub address, all of them in one file that
///   Windows protects for this user (DPAPI, as the OpenAI key is).
/// The calls run on the thread pool; the few fields shared with the core thread are guarded by one lock.
/// </summary>
public sealed class HubLink : IDisposable
{
    static string IdentityFile => Path.Combine(HostPaths.Root, "market-identity.dpapi");
    static readonly object FileGate = new();
    static Process _child;              // the hub this host started (one per host, whatever address is in use)
    static double _childTriedAt = -1000;
    const int MaxChildStarts = 3;
    static int _childFailed;            // how many starts in a row came to nothing (MaxChildStarts: no more are tried until a hub answers or the host starts again)
    static bool _childCounted = true;   // the end of the child that was started last has been looked at (and logged, once)
    /// <summary>
    /// Why this PC's own hub is not running, once the host has given up starting it: "" (no problem), "port"
    /// (something that is not a hub holds its port), "missing" (its exe is not next to the host) or "failed". The shop
    /// page shows a line about it instead of only "cannot be reached".
    /// </summary>
    public static string ChildProblem { get; private set; } = "";

    public readonly string Url;
    public readonly string Source;      // "LP_MARKET_HUB" | "saved" | "default"
    public readonly bool LocalDefault;  // this PC's own hub: the host starts it when it is not running
    public readonly HubApi Api;
    readonly object _gate = new();
    // one registration at a time: the first look at the hub and a button pressed in the same moment must not make two accounts
    readonly SemaphoreSlim _registering = new(1, 1);
    HubIdentity _id;
    string _hubIdSeen = "";             // the id the hub at this address gave when it last answered ("" = it gave none)
    bool _unreadable;                   // the identity file was there and could not be read when it was last looked at

    public HubLink(string saved)
    {
        string env = HubRules.CleanUrl(Environment.GetEnvironmentVariable("LP_MARKET_HUB"));
        string own = HubRules.CleanUrl(saved);
        Url = env.Length > 0 ? env : own.Length > 0 ? own : HubRules.DefaultUrl;
        Source = env.Length > 0 ? "LP_MARKET_HUB" : own.Length > 0 ? "saved" : "default";
        LocalDefault = Url == HubRules.DefaultUrl;
        Api = new HubApi(Url);
        _id = LoadAll(out _unreadable).TryGetValue(Url, out var id) && !string.IsNullOrEmpty(id?.token) ? id : null;
    }

    public void Dispose() => Api.Dispose();

    /// <summary>The nickname as last known ("" before the first contact).</summary>
    public string Nickname { get { lock (_gate) return _id?.nickname ?? ""; } }
    public string UserId { get { lock (_gate) return _id?.userId ?? ""; } }
    public bool Generated { get { lock (_gate) return _id?.generated ?? false; } }
    public bool Known { get { lock (_gate) return _id != null; } }

    // ------------------------------------------------------------------ the identity file
    /// <summary>
    /// Every account this PC has, by hub address. unreadable: the file is there and could not be read (busy, damaged,
    /// protected for another Windows user) - which is not the same as "there are none": whoever writes the file next
    /// must not write over it (see Keep).
    /// </summary>
    static Dictionary<string, HubIdentity> LoadAll(out bool unreadable)
    {
        unreadable = false;
        lock (FileGate)
        {
            try
            {
                if (!File.Exists(IdentityFile)) return new();
                var all = Proto.FromJson<Dictionary<string, HubIdentity>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(IdentityFile), null, DataProtectionScope.CurrentUser)));
                if (all != null) return all;
                Log.Warn("market: the stored hub identity could not be read: it is not a list of accounts");
            }
            // only the kind of error is logged: nothing of the file's content may leak
            catch (Exception e) { Log.Warn("market: the stored hub identity could not be read: " + e.GetType().Name); }
            unreadable = true;
            return new();
        }
    }

    void Keep(HubIdentity id)
    {
        lock (_gate) _id = id;
        lock (FileGate)
        {
            try
            {
                var all = LoadAll(out bool unreadable);
                if (unreadable)
                {
                    // A file that cannot be read is never written over: it may hold the keys to the user's stars, here
                    // and on other hubs. It is put aside under another name first (if that fails, nothing is written).
                    string aside = IdentityFile + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Move(IdentityFile, aside);
                    Log.Warn("market: the hub identity file could not be read; it was put aside as " + Path.GetFileName(aside) + " and a new one is begun");
                }
                if (id == null) all.Remove(Url);
                else
                {
                    // the account on ANOTHER hub that answered at this same address is kept beside, under that hub's id (RegisterAgain finds it there when that hub is back)
                    if (all.TryGetValue(Url, out var was) && was != null && was.token != id.token && !string.IsNullOrEmpty(was.hubId) && was.hubId != id.hubId) all[Url + "|" + was.hubId] = was;
                    all[Url] = id;
                }
                string tmp = IdentityFile + ".tmp";
                File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(Proto.ToJson(all)), null, DataProtectionScope.CurrentUser));
                File.Move(tmp, IdentityFile, true);
            }
            catch (Exception e) { Log.Warn("market: the hub identity could not be stored: " + e.GetType().Name); }
        }
    }

    /// <summary>A friendly name for somebody who has not chosen one: "NiloFriend-4821".</summary>
    public static string FriendlyNickname() =>
        "NiloFriend-" + RandomNumberGenerator.GetInt32(1000, 10000);

    /// <summary>The user's account on this hub; registered now when there is none yet.</summary>
    public async Task<HubIdentity> Identity(CancellationToken ct)
    {
        lock (_gate) if (_id != null) return _id;
        await _registering.WaitAsync(ct);
        try
        {
            lock (_gate) if (_id != null) return _id;      // (somebody else registered while this call waited)
            // The identity file could not be read when it was last looked at. Before a new account is made - which
            // would leave the old one and its stars behind - it is tried once more: it may only have been busy.
            if (_unreadable)
            {
                var all = LoadAll(out _unreadable);
                if (!_unreadable && all.TryGetValue(Url, out var back) && !string.IsNullOrEmpty(back?.token)) { lock (_gate) _id = back; return back; }
            }
            return await Register(null, ct);
        }
        finally { _registering.Release(); }
    }

    /// <summary>
    /// A new account. keep: the nickname to ask for first (the one the user had, when the hub has forgotten them) -
    /// otherwise, and when that one is taken, a made-up one. A handful of tries: a made-up name can be taken too.
    /// </summary>
    async Task<HubIdentity> Register(HubIdentity keep, CancellationToken ct)
    {
        // which hub the account is made on: kept beside it, so that it is known later whether the same hub is still answering here
        string hubId = (await Api.Hello(ct))?.hubId ?? "";
        for (int attempt = 0; ; attempt++)
        {
            bool own = attempt == 0 && keep != null && !keep.generated && keep.nickname.Length > 0;
            string nick = own ? keep.nickname : FriendlyNickname();
            try
            {
                var r = await Api.Register(nick, ct);
                var id = new HubIdentity { userId = r.userId, token = r.token, nickname = r.nickname, generated = !own, hubId = hubId };
                Keep(id);
                Log.Info($"market: registered on the hub {Url} as '{id.nickname}'{(own ? "" : " (a made-up nickname: the user can choose their own)")}");
                return id;
            }
            catch (HubException e) when (e.Code is "nickname-taken" or "nickname-invalid" or "nickname-reserved" && attempt < 5) { }
        }
    }

    /// <summary>
    /// A call in the user's name was answered 401. stale: the identity that was turned away; refusal: the answer.
    /// The account - the key to the user's stars - is given up only when it is certain that it is gone:
    ///   - the hub says so itself (detail "unknown-account"), or
    ///   - another hub answers at this address now (its id is not the one kept beside the account), or
    ///   - the hub is one from before it could say either, and its answer was the hub's own "unauthorized".
    /// A 401 from anything else (something in between, something that is not a hub) is passed on as the failure it
    /// is, and the account stays. And the old account stays in place until the new registration has succeeded: a hub
    /// that goes away in the middle of it costs nothing.
    /// </summary>
    public async Task<HubIdentity> RegisterAgain(HubIdentity stale, HubException refusal, CancellationToken ct)
    {
        refusal ??= new HubException(401, "unauthorized", "the hub turned the account away");
        await _registering.WaitAsync(ct);
        try
        {
            lock (_gate) { if (_id != null && _id.token != stale?.token) return _id; }      // (another call has registered again already)
            var hello = await Api.Hello(ct);
            if (hello == null) throw refusal;                                              // whatever said 401, no hub answers here just now
            string now = hello.hubId ?? "", was = stale?.hubId ?? "";
            bool otherHub = now.Length > 0 && was.Length > 0 && now != was;
            bool saysUnknown = refusal.Detail == HubRules.UnknownAccount;
            bool olderHub = now.Length == 0 && refusal.Code == "unauthorized";
            if (!otherHub && !saysUnknown && !olderHub) throw refusal;
            if (otherHub)
            {
                // the account this PC had on THAT hub, when it answered here before
                var all = LoadAll(out _);
                if (all.TryGetValue(Url + "|" + now, out var back) && !string.IsNullOrEmpty(back?.token) && back.token != stale?.token)
                {
                    Log.Info($"market: another hub answers at {Url} now; this PC's account there ('{back.nickname}') is used again");
                    Keep(back);
                    return back;
                }
            }
            Log.Warn($"market: the hub {Url} does not know this PC's account {(otherHub ? "(another hub answers at this address now)" : "any more")}; registering again");
            return await Register(stale, ct);
        }
        finally { _registering.Release(); }
    }

    /// <summary>The user chose (or the hub confirmed) a nickname.</summary>
    public void Renamed(string nickname)
    {
        HubIdentity id;
        lock (_gate) { if (_id == null) return; id = new HubIdentity { userId = _id.userId, token = _id.token, nickname = nickname, generated = false, hubId = _id.hubId ?? "" }; }
        Keep(id);
    }

    /// <summary>
    /// The hub has just answered for this account, and reports its nickname (it is the authority; another PC of the
    /// same user cannot have changed it, but a restored state file can be behind). Which hub that was is kept beside
    /// the account as well - an account from before had no hub id.
    /// </summary>
    public void Heard(string nickname)
    {
        HubIdentity id;
        lock (_gate)
        {
            if (_id == null) return;
            bool name = !string.IsNullOrEmpty(nickname) && _id.nickname != nickname;
            bool hub = _hubIdSeen.Length > 0 && _id.hubId != _hubIdSeen;
            if (!name && !hub) return;
            id = new HubIdentity { userId = _id.userId, token = _id.token, nickname = name ? nickname : _id.nickname, generated = _id.generated, hubId = hub ? _hubIdSeen : _id.hubId ?? "" };
        }
        Keep(id);
    }

    // ------------------------------------------------------------------ this PC's own hub
    /// <summary>
    /// When the hub is this PC's own and nothing answers there: starts LivingPartner.MarketHub.exe from the host's
    /// folder, hidden, and waits a moment for it to come up. Does nothing for a hub somewhere else (that one is
    /// somebody's server: it is up or it is not). Returns whether a hub answers.
    /// </summary>
    public async Task<bool> EnsureRunning(CancellationToken ct)
    {
        if (await Answers(ct, 2)) return true;
        if (!LocalDefault) return false;
        lock (FileGate)
        {
            if (_child is { HasExited: false }) return false;          // started and not answering yet: give it time
            // The hub that was started last has ended without a hub ever answering. Why, said once: its exit code
            // (4 = it could not listen: something that is not a hub holds the port).
            string why = "failed";
            if (_child != null && !_childCounted)
            {
                _childCounted = true; _childFailed++;
                int code = -1;
                try { code = _child.ExitCode; } catch { }
                if (code == 4) why = "port";
                Log.Warn($"market: this PC's own hub ended with exit code {code}{(code == 4 ? $" (it could not listen on port {HubRules.DefaultPort}: another program has it)" : "")}; that is failed start {_childFailed} of {MaxChildStarts}");
                if (_childFailed >= MaxChildStarts) ChildProblem = why;
            }
            // Three starts that came to nothing are enough: it used to be started again every 20 seconds for as long
            // as the host ran. It is tried again when the host starts again (or a hub answers here by itself).
            if (_childFailed >= MaxChildStarts)
            {
                if (ChildProblem.Length == 0) ChildProblem = why;
                return false;
            }
            if (HostClock.Now - _childTriedAt < 20) return false;      // (not in a loop when it cannot start)
            _childTriedAt = HostClock.Now;
            string exe = Path.Combine(AppContext.BaseDirectory, "LivingPartner.MarketHub.exe");
            if (!File.Exists(exe))
            {
                _childFailed = MaxChildStarts; ChildProblem = "missing";      // (it will not appear by waiting: said once, not every 20 seconds)
                Log.Warn($"market: no hub answers at {Url} and LivingPartner.MarketHub.exe is not next to the host ({AppContext.BaseDirectory}): the shop stays offline. Build or publish the host again, or name a hub with LP_MARKET_HUB.");
                return false;
            }
            try
            {
                // --parent: the hub ends by itself when this host does, however the host ends
                _child = Process.Start(new ProcessStartInfo(exe, $"--port {HubRules.DefaultPort} --parent {Environment.ProcessId}") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory });
                _childCounted = false;
                Log.Info($"market: started this PC's own hub (pid {_child?.Id}) on port {HubRules.DefaultPort}");
            }
            catch (Exception e)
            {
                _childFailed++;
                Log.Warn($"market: this PC's own hub could not be started: {e.Message}; that is failed start {_childFailed} of {MaxChildStarts}");
                if (_childFailed >= MaxChildStarts) ChildProblem = "failed";
                return false;
            }
        }
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(250, ct);
            if (await Answers(ct, 1)) return true;
        }
        Log.Warn("market: this PC's own hub was started but does not answer yet");
        return false;
    }

    /// <summary>Does a hub answer at this address just now? Its id is noted (see Heard), and a hub that answers ends any trouble there was with starting this PC's own.</summary>
    async Task<bool> Answers(CancellationToken ct, int seconds)
    {
        var hello = await Api.Hello(ct, seconds);
        if (hello == null) return false;
        lock (_gate) _hubIdSeen = hello.hubId ?? "";
        if (LocalDefault) lock (FileGate) { _childFailed = 0; ChildProblem = ""; }
        return true;
    }

    /// <summary>The host is closing: the hub it started goes with it.</summary>
    public static void StopChild()
    {
        lock (FileGate)
        {
            try { if (_child is { HasExited: false }) { _child.Kill(); Log.Info("market: this PC's own hub stopped"); } } catch { }
            _child = null;
        }
    }
}
