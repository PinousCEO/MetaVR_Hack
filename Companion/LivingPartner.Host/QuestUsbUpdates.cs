using System.Diagnostics;
using System.Security.Cryptography;

namespace LivingPartner.Host;

/// <summary>Keep the bundled Quest app in sync while its desktop companion is running.</summary>
internal static class QuestUsbUpdates
{
    const string Package = "com.livingaipartner.quest";

    public static async Task Watch(string apk, int port, Action connected, Action<string> log, CancellationToken stop)
    {
        var adb = FindAdb();
        if (adb == null || !Directory.Exists(Path.GetDirectoryName(apk))) return;
        string lastTransport = "", previousError = "";
        long syncedWrite = 0, seenWrite = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(4000, stop);
                var file = new FileInfo(apk);
                if (!file.Exists) continue;
                long written = file.LastWriteTimeUtc.Ticks;
                if (seenWrite != written) { seenWrite = written; continue; }
                var devices = await Run(adb, stop, "devices", "-l");
                var quests = devices.Split('\n').Select(line => line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    .Where(cols => cols.Length > 2 && cols[1] == "device" && cols.Any(x => x.StartsWith("model:") && x.Contains("Quest", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (quests.Length != 1) { lastTransport = ""; continue; }
                var quest = quests[0];
                string transport = quest[0] + "/" + quest.FirstOrDefault(x => x.StartsWith("transport_id:"));
                if (transport == lastTransport && syncedWrite == written) continue;
                if (await Run(adb, stop, "-d", "get-serialno") != quest[0]) continue;
                Task<string> Device(params string[] args) => Run(adb, stop, new[] { "-s", quest[0] }.Concat(args).ToArray());

                // A desktop build finishing mid-session must not interrupt play. A fresh USB connection
                // may apply it straight away, or it can be installed after the user closes the game.
                if (transport == lastTransport && (await Device("shell", "pidof", Package)).Length > 0) continue;
                await Device("reverse", $"tcp:{port}", $"tcp:{port}");
                connected?.Invoke();
                string installed = await Device("shell", "pm", "path", Package);
                string path = installed.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("package:/") && x.EndsWith("/base.apk"));
                string remoteHash = path == null ? "" : (await Device("shell", "sha256sum", path[8..])).Split(' ')[0];
                using var input = file.OpenRead();
                string localHash = Convert.ToHexString(await SHA256.HashDataAsync(input, stop));
                if (!localHash.Equals(remoteHash, StringComparison.OrdinalIgnoreCase))
                {
                    bool resume = (await Device("shell", "pidof", Package)).Length > 0;
                    string result = await Device("install", "-r", apk);
                    if (!result.Split('\n').Any(line => line.Trim() == "Success")) throw new IOException("Quest install: " + result);
                    if (resume) await Device("shell", "am", "start", "-n", Package + "/com.unity3d.player.UnityPlayerGameActivity");
                    log("Quest USB update installed.");
                }
                else log("Quest USB connected. Installed APK matches the desktop build.");
                lastTransport = transport;
                syncedWrite = written;
                previousError = "";
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                if (e.Message != previousError) log("Quest USB: " + e.Message);
                previousError = e.Message;
            }
        }
    }

    static string FindAdb()
    {
        string exe = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).ToList();
        foreach (string variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } sdk) dirs.Add(Path.Combine(sdk, "platform-tools"));
        if (OperatingSystem.IsWindows())
        {
            string editors = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
            if (Directory.Exists(editors)) dirs.AddRange(Directory.EnumerateDirectories(editors).OrderDescending()
                .Select(d => Path.Combine(d, "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools")));
        }
        else
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.AddRange(new[] { Path.Combine(user, "Library/Android/sdk/platform-tools"), Path.Combine(user, "Downloads/platform-tools"), "/opt/homebrew/bin", "/usr/local/bin" });
        }
        return dirs.Where(d => Path.IsPathFullyQualified(d)).Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
    }

    static async Task<string> Run(string adb, CancellationToken stop, params string[] args)
    {
        var start = new ProcessStartInfo(adb) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(args.Contains("install") ? 150 : 15));
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { try { process.Kill(); } catch (InvalidOperationException) { } throw; }
        string result = (await output).Trim(), error = (await errors).Trim();
        // Missing package or process is an ordinary first install / closed game.
        if (process.ExitCode != 0 && !(result.Length == 0 && error.Length == 0 && (args.Contains("pidof") || args.Contains("path"))))
            throw new IOException(error.Length > 0 ? error : result.Length > 0 ? result : $"adb {args.Last()} exited {process.ExitCode}");
        return result;
    }
}
