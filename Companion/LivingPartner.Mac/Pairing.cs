using System.Security.Cryptography;
using System.Text;

namespace LivingPartner.Mac;

sealed class Pairing
{
    readonly string path;
    string token;
    readonly Dictionary<string, Queue<DateTime>> attempts = new();
    public string Code { get; private set; } = NewCode();

    public Pairing(string dir)
    {
        Directory.CreateDirectory(dir);
        path = Path.Combine(dir, "headset.token");
        token = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
    }
    static string NewCode() => RandomNumberGenerator.GetInt32(1000000).ToString("D6");
    public bool Trusted(string value) => token.Length > 0 && value != null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(value));

    public string Pair(string address, string code)
    {
        var now = DateTime.UtcNow;
        foreach (var stale in attempts.Where(p => p.Value.Last() < now.AddMinutes(-1)).Select(p => p.Key).ToArray()) attempts.Remove(stale);
        if (!attempts.TryGetValue(address, out var recent)) attempts[address] = recent = new();
        while (recent.Count > 0 && recent.Peek() < now.AddMinutes(-1)) recent.Dequeue();
        if (recent.Count >= 6) return "";
        recent.Enqueue(now);
        if (code != Code) return "";
        string fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string temp = path + ".new";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var file = new FileStream(temp, options))
            file.Write(Encoding.UTF8.GetBytes(fresh));
        File.Move(temp, path, true);
        token = fresh;
        Code = NewCode();
        attempts.Clear();
        return fresh;
    }
}
