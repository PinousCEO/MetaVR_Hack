using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

public sealed class HarnessRequest
{
    public string Prompt = "";
    public string WorkDir = "";
    public List<string> AddDirs = new();
    public string AllowedTools = "Read,Glob,Grep";
    public string PermissionMode = "default";
    public double MaxBudgetUsd = 1.0;
    public int TimeoutSec = 420;
    public string SessionId = "";
    public List<string> Images = new();   // screenshots to attach (adapters that cannot read files by path use this)
}

public sealed class HarnessEvent { public string Kind = ""; public string Detail = ""; }

public sealed class HarnessResult
{
    public bool Ok;
    public int ExitCode = -1;
    public string Text = "";
    public string SessionId = "";
    public string Error = "";
    public double CostUsd;
    public int Turns;
    public string Command = "";
}

/// <summary>Adapter boundary for developer harnesses. One real adapter (Claude Code CLI) is completed first; others plug in here.</summary>
public interface IHarnessAdapter
{
    string Name { get; }
    bool IsTestDouble { get; }
    /// <summary>Prompt wording: how this harness is given the screenshot of the pointed window.</summary>
    string ScreenshotHint { get; }
    /// <summary>Prompt wording: what an analysis (read-only) run may use to look at the project.</summary>
    string ReadRule { get; }
    /// <summary>Prompt wording: how a change run may edit files, and what it must not do.</summary>
    string EditRule { get; }
    bool IsAvailable(out string detail);
    Task<HarnessResult> RunAsync(HarnessRequest req, Action<HarnessEvent> onEvent, CancellationToken ct);
}

/// <summary>
/// Runs the installed, already-authenticated Claude Code CLI in non-interactive print mode as a MANAGED session
/// (own working directory, own session id, tool allow-list, budget cap, timeout, kill switch). It never attaches to or
/// drives a terminal session the user already has open.
/// </summary>
public sealed class ClaudeCodeAdapter : IHarnessAdapter
{
    public string Name => "claude-code";
    public bool IsTestDouble => false;
    public string ScreenshotHint => "Read it with the Read tool.";
    public string ReadRule => "Use only the Read, Glob and Grep tools; do not run commands.";
    public string EditRule => "Edit files with the Edit / Write tools only; do not run git; do not run shell commands; do not add dependencies.";
    string _exe;

    string FindExe()
    {
        if (_exe != null) return _exe;
        var candidates = new List<string>();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        candidates.Add(Path.Combine(appData, "npm", "claude.cmd"));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim(), "claude.cmd"));
            candidates.Add(Path.Combine(dir.Trim(), "claude.exe"));
        }
        _exe = candidates.FirstOrDefault(File.Exists) ?? "";
        return _exe;
    }

    public bool IsAvailable(out string detail)
    {
        string exe = FindExe();
        if (exe.Length == 0) { detail = "claude CLI not found on PATH"; return false; }
        detail = exe;
        return true;
    }

    public async Task<HarnessResult> RunAsync(HarnessRequest req, Action<HarnessEvent> onEvent, CancellationToken ct)
    {
        var res = new HarnessResult();
        string exe = FindExe();
        if (exe.Length == 0) { res.Error = "claude CLI not found"; return res; }
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--allowedTools", req.AllowedTools, "--permission-mode", req.PermissionMode,
            "--max-budget-usd", req.MaxBudgetUsd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) };
        foreach (var d in req.AddDirs) { args.Add("--add-dir"); args.Add(d); }
        if (!string.IsNullOrEmpty(req.SessionId)) { args.Add("--session-id"); args.Add(req.SessionId); }
        bool isCmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo
        {
            FileName = isCmd ? "cmd.exe" : exe,
            WorkingDirectory = req.WorkDir,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (isCmd) { psi.ArgumentList.Add("/d"); psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(exe); }
        foreach (var a in args) psi.ArgumentList.Add(a);
        // the product's managed session must not inherit the identity of whatever tool launched the host
        foreach (var k in new[] { "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT" }) psi.Environment.Remove(k);
        res.Command = "claude " + string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stderr = new StringBuilder();
        try
        {
            p.Start();
        }
        catch (Exception e) { res.Error = "failed to start claude: " + e.Message; return res; }
        using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        var errTask = Task.Run(async () => { string l; while ((l = await p.StandardError.ReadLineAsync()) != null) stderr.AppendLine(l); });
        await p.StandardInput.WriteAsync(req.Prompt);
        p.StandardInput.Close();

        var timeout = Task.Delay(TimeSpan.FromSeconds(req.TimeoutSec), ct);
        var reader = Task.Run(async () =>
        {
            string line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JObject o;
                try { o = JObject.Parse(line); } catch { continue; }
                string type = (string)o["type"] ?? "";
                if (type == "system" && (string)o["subtype"] == "init") { res.SessionId = (string)o["session_id"] ?? ""; onEvent(new HarnessEvent { Kind = "session", Detail = res.SessionId }); }
                else if (type == "assistant")
                {
                    foreach (var c in o["message"]?["content"] as JArray ?? new JArray())
                    {
                        if ((string)c["type"] == "tool_use")
                        {
                            string input = c["input"]?["file_path"]?.ToString() ?? c["input"]?["pattern"]?.ToString() ?? c["input"]?["command"]?.ToString() ?? "";
                            onEvent(new HarnessEvent { Kind = "tool", Detail = $"{(string)c["name"]} {input}" });
                        }
                    }
                }
                else if (type == "result")
                {
                    res.Text = (string)o["result"] ?? "";
                    res.Ok = !((bool?)o["is_error"] ?? false);
                    res.CostUsd = (double?)o["total_cost_usd"] ?? 0;
                    res.Turns = (int?)o["num_turns"] ?? 0;
                    if (!res.Ok && res.Error.Length == 0) res.Error = ((string)o["subtype"] ?? "error") + ": " + res.Text;
                }
            }
        });
        var done = await Task.WhenAny(reader, timeout);
        if (done == timeout)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            res.Ok = false; res.Error = ct.IsCancellationRequested ? "cancelled" : $"timed out after {req.TimeoutSec}s";
            return res;
        }
        try { await p.WaitForExitAsync(ct); } catch { }
        await Task.WhenAny(errTask, Task.Delay(500));
        res.ExitCode = p.HasExited ? p.ExitCode : -1;
        if (res.ExitCode != 0 && res.Ok) res.Ok = false;
        if (!res.Ok && res.Error.Length == 0) res.Error = $"exit code {res.ExitCode}: {Trim(stderr.ToString(), 400)}";
        return res;
    }

    static string Trim(string s, int n) => s.Length <= n ? s.Trim() : s[..n].Trim() + "...";
}

/// <summary>
/// LABELLED TEST DOUBLE. Only used when the host is started with --stub-harness. Tasks it produces carry
/// isTestDouble = true and are never reported as real analysis or real code changes.
/// </summary>
public sealed class StubHarnessAdapter : IHarnessAdapter
{
    public string Name => "stub (test double)";
    public bool IsTestDouble => true;
    public string ScreenshotHint => "";
    public string ReadRule => "";
    public string EditRule => "";
    public bool IsAvailable(out string detail) { detail = "test double"; return true; }
    public async Task<HarnessResult> RunAsync(HarnessRequest req, Action<HarnessEvent> onEvent, CancellationToken ct)
    {
        onEvent(new HarnessEvent { Kind = "session", Detail = "stub-session" });
        await Task.Delay(1500, ct);
        onEvent(new HarnessEvent { Kind = "tool", Detail = "Read (stub)" });
        await Task.Delay(1500, ct);
        string text = req.AllowedTools.Contains("Edit")
            ? "{\"summary\":\"[TEST DOUBLE] no file was changed\",\"changedFiles\":[]}"
            : "{\"summary\":\"[TEST DOUBLE] not a real analysis\",\"pointedElement\":\"[TEST DOUBLE]\",\"modules\":[{\"name\":\"[TEST DOUBLE] sample module\",\"files\":[],\"uiEvidence\":\"none (stub)\",\"role\":\"n/a\",\"evidence\":\"none (stub)\",\"proposal\":\"n/a\",\"impact\":\"n/a\",\"confidence\":\"inferred\"}]}";
        return new HarnessResult { Ok = true, ExitCode = 0, Text = text, SessionId = "stub-session", Command = "stub" };
    }
}

/// <summary>
/// Second real adapter behind the same interface: the installed, logged-in Codex CLI in non-interactive mode
/// (`codex exec --json`), again as a managed session with its own working directory, sandbox mode, timeout and kill switch.
/// Selected with `--harness codex`; Claude Code stays the default.
/// </summary>
public sealed class CodexAdapter : IHarnessAdapter
{
    public string Name => "codex";
    public bool IsTestDouble => false;
    // Codex reads and edits files through its shell / patch tools, so the rules are worded for that (a blanket
    // "no shell commands" makes it unable to open or change any file). The CLI sandbox (-s read-only / workspace-write)
    // is what actually enforces the limits.
    public string ScreenshotHint => "It is attached to this message as an image.";
    public string ReadRule => "You may run shell commands ONLY to list and read files inside the working directory (the sandbox is read-only); run nothing else.";
    public string EditRule => "Edit files with your patch tool, or with shell commands that only read and write files inside the working directory; do not run git, package managers, tests, network tools or any other program; do not add dependencies.";
    string _exe;

    string FindExe()
    {
        if (_exe != null) return _exe;
        var candidates = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd") };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim(), "codex.cmd"));
            candidates.Add(Path.Combine(dir.Trim(), "codex.exe"));
        }
        _exe = candidates.FirstOrDefault(File.Exists) ?? "";
        return _exe;
    }

    public bool IsAvailable(out string detail)
    {
        string exe = FindExe();
        if (exe.Length == 0) { detail = "codex CLI not found on PATH"; return false; }
        detail = exe;
        return true;
    }

    public async Task<HarnessResult> RunAsync(HarnessRequest req, Action<HarnessEvent> onEvent, CancellationToken ct)
    {
        var res = new HarnessResult();
        string exe = FindExe();
        if (exe.Length == 0) { res.Error = "codex CLI not found"; return res; }
        bool edits = req.AllowedTools.Contains("Edit") || req.AllowedTools.Contains("Write");
        string lastFile = Path.Combine(Path.GetTempPath(), "lp-codex-" + Guid.NewGuid().ToString("N") + ".txt");
        var args = new List<string> { "exec", "--json", "-s", edits ? "workspace-write" : "read-only", "-C", req.WorkDir, "--skip-git-repo-check", "-o", lastFile };
        foreach (var img in req.Images) { args.Add("-i"); args.Add(img); }
        bool isCmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo
        {
            FileName = isCmd ? "cmd.exe" : exe, WorkingDirectory = req.WorkDir,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (isCmd) { psi.ArgumentList.Add("/d"); psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(exe); }
        foreach (var a in args) psi.ArgumentList.Add(a);
        res.Command = "codex " + string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        using var p = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        try { p.Start(); }
        catch (Exception e) { res.Error = "failed to start codex: " + e.Message; return res; }
        using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        var errTask = Task.Run(async () => { string l; while ((l = await p.StandardError.ReadLineAsync()) != null) stderr.AppendLine(l); });
        await p.StandardInput.WriteAsync(req.Prompt);
        p.StandardInput.Close();
        string lastMessage = ""; bool failed = false;
        var reader = Task.Run(async () =>
        {
            string line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JObject o;
                try { o = JObject.Parse(line); } catch { continue; }
                string type = (string)o["type"] ?? "";
                if (o["thread_id"] != null && res.SessionId.Length == 0) { res.SessionId = (string)o["thread_id"]; onEvent(new HarnessEvent { Kind = "session", Detail = res.SessionId }); }
                var item = o["item"] as JObject;
                if (item != null)
                {
                    string itype = (string)item["type"] ?? "";
                    if (itype == "agent_message" && type == "item.completed") lastMessage = (string)item["text"] ?? lastMessage;
                    else if (type == "item.started" || type == "item.completed")
                        onEvent(new HarnessEvent { Kind = "tool", Detail = (itype + " " + ((string)item["command"] ?? (string)item["path"] ?? "")).Trim() });
                }
                if (type == "turn.completed") res.Turns++;
                if (type == "error" || type == "turn.failed") { failed = true; res.Error = (string)o["message"] ?? o["error"]?["message"]?.ToString() ?? "codex reported an error"; }
            }
        });
        var timeout = Task.Delay(TimeSpan.FromSeconds(req.TimeoutSec), ct);
        if (await Task.WhenAny(reader, timeout) == timeout)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            res.Error = ct.IsCancellationRequested ? "cancelled" : $"timed out after {req.TimeoutSec}s";
            return res;
        }
        try { await p.WaitForExitAsync(ct); } catch { }
        await Task.WhenAny(errTask, Task.Delay(500));
        res.ExitCode = p.HasExited ? p.ExitCode : -1;
        try { if (File.Exists(lastFile)) { res.Text = File.ReadAllText(lastFile); File.Delete(lastFile); } } catch { }
        if (res.Text.Length == 0) res.Text = lastMessage;
        res.Ok = res.ExitCode == 0 && !failed && res.Text.Length > 0;
        if (!res.Ok && res.Error.Length == 0) res.Error = $"exit code {res.ExitCode}: " + (stderr.Length > 400 ? stderr.ToString(0, 400) : stderr.ToString()).Trim();
        return res;
    }
}

public static class Proc
{
    /// <summary>Runs a command to completion and returns (exitCode, combined output). Used for git and project tests.</summary>
    public static async Task<(int code, string output)> Run(string file, IEnumerable<string> args, string workDir, int timeoutSec, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo { FileName = file, WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";   // never hang waiting for credentials
        psi.Environment["GCM_INTERACTIVE"] = "never";
        var sb = new StringBuilder();
        try
        {
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return (-2, sb + "\n[timed out or cancelled]"); }
            p.WaitForExit();
            return (p.ExitCode, sb.ToString());
        }
        catch (Exception e) { return (-1, "failed to run " + file + ": " + e.Message); }
    }
}
