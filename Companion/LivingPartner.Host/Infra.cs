using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LivingPartner.Shared;

namespace LivingPartner.Host;

public static class HostPaths
{
    /// <summary>
    /// Where the host keeps everything: %LOCALAPPDATA%\LivingAIPartner. The environment variable LP_HOME names another
    /// folder - for trying a second host out next to the real one (its own state, token, keys and world; see also the
    /// mutex in Program.cs), never for everyday use.
    /// </summary>
    public static readonly string Root = Environment.GetEnvironmentVariable("LP_HOME") is { Length: > 0 } home
        ? Path.GetFullPath(home) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LivingAIPartner");
    /// <summary>The host was started with a data folder of its own (LP_HOME): a trial next to the real one.</summary>
    public static bool Trial => Environment.GetEnvironmentVariable("LP_HOME") is { Length: > 0 };
    public static string Logs => Path.Combine(Root, "logs");
    public static string Captures => Path.Combine(Root, "captures");
    public static string LocalToken => Path.Combine(Root, "local.token");
    public static string State => Path.Combine(Root, "state.json");
    public static string Paired => Path.Combine(Root, "paired.json");
    public static string Projects => Path.Combine(Root, "projects.json");
    public static string TransitionLog => Path.Combine(Logs, "transitions.jsonl");
    public static string TaskLog => Path.Combine(Logs, "tasks.jsonl");
    /// <summary>The generated models (GLB) and icons (PNG) of the partner's things and skills; served to the clients as /world/&lt;file&gt;.</summary>
    public static string World => Path.Combine(Root, "world");
    /// <summary>The pictures of the documents the partner made in this sitting (pages, design options), one folder per document; served to the clients as /docs/&lt;id&gt;/&lt;file&gt; (Docs.cs).</summary>
    public static string Docs => Path.Combine(Root, "docs");
    public static void Ensure() { Directory.CreateDirectory(Root); Directory.CreateDirectory(Logs); Directory.CreateDirectory(Captures); Directory.CreateDirectory(World); Directory.CreateDirectory(Docs); }
}

/// <summary>Monotonic host clock expressed as unix seconds (the shared time base of all clients).</summary>
public static class HostClock
{
    static readonly double Epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    static readonly Stopwatch Sw = Stopwatch.StartNew();
    public static double Now => Epoch + Sw.Elapsed.TotalSeconds;
    public static long UnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public static class Log
{
    static readonly object Gate = new();
    static StreamWriter _w;
    public static readonly ConcurrentQueue<string> Recent = new();
    public static void Init()
    {
        try { _w = new StreamWriter(Path.Combine(HostPaths.Logs, "host.log"), true, Encoding.UTF8) { AutoFlush = true }; } catch { }
    }
    public static void Info(string s) => Write("INF", s);
    public static void Warn(string s) => Write("WRN", s);
    public static void Error(string s) => Write("ERR", s);
    static void Write(string lvl, string s)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {lvl} {s}";
        lock (Gate)
        {
            try { _w?.WriteLine(line); } catch { }
            try { Console.WriteLine(line); } catch { }
        }
        Recent.Enqueue(line);
        while (Recent.Count > 200) Recent.TryDequeue(out _);
    }
    public static void Jsonl(string path, object o)
    {
        lock (Gate) { try { File.AppendAllText(path, Proto.ToJson(o) + "\n"); } catch { } }
    }
}

/// <summary>Local secret for loopback clients and pairing tokens for LAN clients. Secrets never go to logs or the repo.</summary>
public sealed class Auth
{
    public string LocalToken { get; private set; } = "";
    public string PairCode { get; private set; } = "";
    readonly Dictionary<string, string> _pairedHashToName = new();
    int _failed; double _lockUntil;

    public void Init()
    {
        if (File.Exists(HostPaths.LocalToken)) LocalToken = File.ReadAllText(HostPaths.LocalToken).Trim();
        if (LocalToken.Length < 32)
        {
            LocalToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(HostPaths.LocalToken, LocalToken);
        }
        try
        {
            if (File.Exists(HostPaths.Paired))
            {
                var d = Proto.FromJson<Dictionary<string, string>>(File.ReadAllText(HostPaths.Paired));
                if (d != null) foreach (var kv in d) _pairedHashToName[kv.Key] = kv.Value;
            }
        }
        catch { }
        RotateCode();
    }

    public void RotateCode() { PairCode = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6"); }
    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public int PairedCount => _pairedHashToName.Count;

    public bool CheckToken(string token, bool loopback)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (loopback && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(LocalToken))) return true;
        return _pairedHashToName.ContainsKey(Hash(token));
    }

    /// <summary>Exchanges the 6-digit code shown on the PC for a long random token. Rate limited.</summary>
    public PairResult TryPair(string code, string deviceName)
    {
        double now = HostClock.Now;
        if (now < _lockUntil) return new PairResult { ok = false, reason = "too many attempts; wait a minute" };
        if (string.IsNullOrEmpty(code) || code != PairCode)
        {
            if (++_failed >= 5) { _failed = 0; _lockUntil = now + 60; RotateCode(); }
            return new PairResult { ok = false, reason = "wrong code" };
        }
        _failed = 0;
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _pairedHashToName[Hash(token)] = deviceName ?? "device";
        try { File.WriteAllText(HostPaths.Paired, Proto.ToJson(_pairedHashToName, true)); } catch { }
        RotateCode();
        return new PairResult { ok = true, token = token };
    }
}
