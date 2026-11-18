using System.Security.Cryptography;
using System.Text;

namespace LivingPartner.Host;

/// <summary>
/// Where the Tripo key comes from: the LP_TRIPO_KEY or TRIPO_API_KEY environment variable, or a file in the host's data
/// folder that only this Windows user can decrypt (DPAPI). It is written by tools\Set-TripoKey.ps1. Like the OpenAI key
/// it is never logged, never put in a snapshot and never stored in the repository.
/// </summary>
public static class TripoKey
{
    public static string File => Path.Combine(HostPaths.Root, "tripo.key.dpapi");

    public static string Load(out string source)
    {
        source = "";
        // our own name first, so that this app can be given a key of its own on a machine where another tool sets TRIPO_API_KEY
        foreach (string name in new[] { "LP_TRIPO_KEY", "TRIPO_API_KEY" })
        {
            string env = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(env)) { source = "environment"; return env.Trim(); }
        }
        try
        {
            if (System.IO.File.Exists(File))
            {
                string key = Encoding.UTF8.GetString(ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), null, DataProtectionScope.CurrentUser)).Trim();
                if (key.Length > 0) { source = "protected file"; return key; }
            }
        }
        // only the kind of error is logged: the message of a decryption error says nothing useful and nothing of the key may leak
        catch (Exception e) { Log.Warn("the stored Tripo key could not be read: " + e.GetType().Name); }
        return null;
    }
}
