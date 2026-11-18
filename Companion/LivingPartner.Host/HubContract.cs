using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LivingPartner.Shared;

namespace LivingPartner.Host;

// What the hub and its clients say to each other, as plain data. The hub (LivingPartner.MarketHub) is the real
// marketplace: one server that many people's PCs talk to, where accounts have nicknames and stars, and where a thing
// one person made is bought by another. This file is compiled into the hub, the host, lpctl and the tests, so that
// all four mean the same thing by every field. Nothing in it touches the network or the disk.

/// <summary>The rules every side keeps to.</summary>
public static class HubRules
{
    public const int DefaultPort = 47620;
    public const string DefaultUrl = "http://127.0.0.1:47620";
    /// <summary>The account the makers of the partner sell under. Nobody can take this name, and nobody can sign in as it.</summary>
    public const string Official = "Bambi Studio";
    public const int StartStars = 300, MaxPrice = 999;
    public const int MaxModuleJson = 300_000, MaxFileBytes = 16 * 1024 * 1024, MaxFiles = 8, MaxIdLength = 40;
    /// <summary>
    /// A picture (.png) is a small sticker of 512 pixels: 600 kB is generous. Only a model (.glb) may be as large as
    /// MaxFileBytes. Pictures are handed to everyone who opens the shop, so a 16 MB "icon" was 16 MB for every visitor.
    /// </summary>
    public const int MaxPictureBytes = 600_000;
    public const int NickMin = 2, NickMax = 16;
    /// <summary>What the hub says in `detail` beside 401 "unauthorized" when a token was sent and no account has it: the account is not known here (any more).</summary>
    public const string UnknownAccount = "unknown-account";
    /// <summary>What the hub answers (409) when a buyer named the price they saw and the listing costs something else now.</summary>
    public const string PriceChanged = "price-changed";

    /// <summary>The most bytes a module's file of this name may have: a picture far less than a model.</summary>
    public static int MaxBytesFor(string fileName) => (fileName ?? "").EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? MaxPictureBytes : MaxFileBytes;

    static readonly Regex NickRx = new(@"^[\p{L}\p{M}\p{Nd} _-]+\z");
    static readonly Regex FileRx = new(@"^[A-Za-z0-9_-][A-Za-z0-9_.-]{0,74}\.(png|glb)\z");

    /// <summary>A nickname as it is kept: composed characters, single spaces, none at the ends.</summary>
    public static string CleanNickname(string s) => Regex.Replace((s ?? "").Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();

    /// <summary>What two nicknames are compared by: "Minsu" and "minsu" are the same person's name.</summary>
    public static string NickKey(string s) => CleanNickname(s).Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    static string Flat(string s) => new string(NickKey(s).Where(char.IsLetterOrDigit).ToArray());

    /// <summary>What is wrong with a nickname: "" (nothing), "nickname-invalid" or "nickname-reserved". 2 to 16 characters: letters of any script, digits, space, - and _.</summary>
    public static string NicknameProblem(string nickname)
    {
        string n = CleanNickname(nickname);
        int length = new StringInfo(n).LengthInTextElements;
        if (length < NickMin || length > NickMax || n.Length > NickMax * 3 || !NickRx.IsMatch(n) || !n.Any(char.IsLetterOrDigit)) return "nickname-invalid";
        // (also "bambi_studio", "BAMBI  STUDIO": nothing that reads as the makers' name)
        return Flat(n) == Flat(Official) ? "nickname-reserved" : "";
    }

    /// <summary>A plain file name of one of the two kinds a module carries (a picture, a model): nothing with a path in it.</summary>
    public static bool SafeFile(string name) => name != null && FileRx.IsMatch(name) && !name.Contains("..");

    /// <summary>The address of a hub as it is kept and compared: scheme, host and port, no path, no trailing slash. "" when it is not an http address.</summary>
    public static string CleanUrl(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return "";
        if (!url.Contains("://")) url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https") || u.Host.Length == 0) return "";
        return $"{u.Scheme}://{u.Host}{(u.IsDefaultPort ? "" : ":" + u.Port)}";
    }
}

/// <summary>One step of a skill, as the shop shows it.</summary>
public sealed class HubStep
{
    public string act = "", arg = "";
    public float seconds;
}

/// <summary>
/// What the shop shows of a module's contents before it is bought: the steps of a skill, how a game is played, the
/// name of its tune, the colours of its surroundings, what an ability is asked with. Never the payload itself (the
/// instructions of an ability, the notes of a tune, a model) - that is what is paid for.
/// </summary>
public sealed class HubDetail
{
    public HubStep[] steps;
    public string[] phrases, examples, uses, colors, tags;
    public string song = "", pattern = "", moodName = "", howTo = "", enemyName = "", pickupName = "", enemyIcon = "", pickupIcon = "", shape = "", color = "", version = "";
    public int bpm, lives;
    public float seconds, speed, spawn;
    // a module that is a script (a game or tool the seller's partner wrote, a thing with behaviour, a scripted skill):
    // how many lines its program has and how many pictures were painted for it; for a thing, where it is worn
    // ("" = it is not) and the acts it offers. The program itself is the payload: it is not shown before it is bought.
    public int scriptLines, art;
    public string wearable = "";
    public string[] acts;
}

/// <summary>One listing, as the hub tells it.</summary>
public sealed class HubListingInfo
{
    public string id = "", kind = "", name = "", summary = "", gives = "", seller = "", icon = "", template = "", version = "1.0";
    public int price, installs, likes;
    public long createdUnixMs, updatedUnixMs;
    public bool official;               // sold by the makers of the partner
    public bool listed = true;          // on sale (false: taken off sale - those who have it keep it)
    public int earned;                  // only in the seller's own view (GET /api/me): the stars it has brought in
    public string[] tags = new string[0];
    public HubDetail detail;            // only when asked for (detail=1)
}

public sealed class HubListings
{
    public long revision;               // goes up with every change on the hub; a client that sends it back (since=) is told "nothing new" instead of the list
    public List<HubListingInfo> list = new();
}

/// <summary>Something the user bought (or got for nothing).</summary>
public sealed class HubPurchase
{
    public string id = "", name = "", kind = "", seller = "", icon = "";
    public int price;                   // what was paid at the time
    public long atUnixMs;
    public bool listed;                 // still on sale
}

/// <summary>The user's own account: GET /api/me.</summary>
public sealed class HubMe
{
    public string userId = "", nickname = "";
    public int stars, sales, earned;    // sales: how many times their things were bought or got; earned: the stars that brought in
    public long joinedUnixMs, revision;
    public List<string> bought = new(), liked = new();
    public List<HubPurchase> purchases = new();
    public List<HubListingInfo> listings = new();       // everything they ever put up, also what is off sale now
}

/// <summary>A new account: POST /api/register. The token is shown this once and never again.</summary>
public sealed class HubRegistered
{
    public string userId = "", token = "", nickname = "";
    public int stars;
}

/// <summary>
/// Buying: POST /api/listings/{id}/buy. The body is optional. price: what the buyer saw the listing cost - the hub
/// then refuses ("price-changed", with the price it is now in `detail`) rather than charge another price.
/// </summary>
public sealed class HubBuy
{
    public int? price;
}

/// <summary>Who answers at an address: GET /api/hello.</summary>
public sealed class HubHello
{
    public string name = "", official = "";
    /// <summary>A random id this hub's data was given once and keeps: two hubs at one address (or one whose data was wiped) are told apart by it. "" from a hub older than this field.</summary>
    public string hubId = "";
}

public sealed class HubBought
{
    public bool ok, already;            // already: it was theirs before, nothing was paid
    public string id = "";
    public int price, stars;            // what was paid just now, and the balance after
}

public sealed class HubLiked
{
    public bool liked;
    public int likes;
}

/// <summary>What anyone may know about a seller: GET /api/users/{nickname}.</summary>
public sealed class HubProfile
{
    public string nickname = "";
    public long joinedUnixMs;
    public bool official;
    public int installs;                // how many times their things were got, all together
    public List<HubListingInfo> listings = new();
}

/// <summary>Putting a module up for sale: POST /api/listings.</summary>
public sealed class HubPublish
{
    public MarketModule module;
    public int price;
    /// <summary>The files the module names (its picture, its model, the pictures of a game), by those names, as base64.</summary>
    public Dictionary<string, string> files = new();
}

public sealed class HubEdit
{
    public int? price;
    public string summary;
}

public sealed class HubNickname
{
    public string nickname = "";
}

/// <summary>Why the hub said no: { error, missing }.</summary>
public sealed class HubError
{
    public string error = "";
    public int missing;                 // not-enough-stars: how many more are needed
    public string detail = "";          // module-invalid: what is wrong with it
}
