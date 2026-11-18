using System.Security.Cryptography;
using System.Text;
using LivingPartner.Host;
using LivingPartner.Shared;

namespace LivingPartner.MarketHub;

// The marketplace itself, without HTTP: accounts with nicknames and stars, listings with their modules and files,
// and what buying, selling, liking and taking off sale mean. Program.cs puts a web server in front of it; the tests
// call it directly with a store that lives in memory. Every call takes the one lock, so that a purchase - stars off
// one account, on to another, the purchase written down - is one step that nothing can come between.

/// <summary>Where the hub keeps what it knows. (A folder on the server; a dictionary in the tests.)</summary>
public interface IHubStore
{
    string ReadIndex();
    void WriteIndex(string json);
    string ReadModule(string id);
    void WriteModule(string id, string json);
    bool HasFile(string name);
    byte[] ReadFile(string name);
    void WriteFile(string name, byte[] data);
    void DeleteFile(string name);
    /// <summary>The file on disk, when there is one (so that a large model can be sent without being read into memory first); null otherwise.</summary>
    string PathOf(string name);
}

public sealed class MemoryHubStore : IHubStore
{
    string _index;
    readonly Dictionary<string, string> _modules = new();
    readonly Dictionary<string, byte[]> _files = new();
    public int IndexWrites;
    public string ReadIndex() => _index;
    public void WriteIndex(string json) { _index = json; IndexWrites++; }
    public string ReadModule(string id) => _modules.TryGetValue(id, out string j) ? j : null;
    public void WriteModule(string id, string json) => _modules[id] = json;
    public bool HasFile(string name) => _files.ContainsKey(name);
    public byte[] ReadFile(string name) => _files.TryGetValue(name, out byte[] d) ? d : null;
    public void WriteFile(string name, byte[] data) => _files[name] = data;
    public void DeleteFile(string name) => _files.Remove(name);
    public string PathOf(string name) => null;
    public IEnumerable<string> FileNames => _files.Keys;
}

/// <summary>hub.json (accounts and listings), modules\&lt;id&gt;.json (what each listing sells) and files\ (pictures and models), in one folder. Every file is written whole under another name and then moved into place, so that a crash never leaves half a file.</summary>
public sealed class FolderHubStore : IHubStore
{
    readonly string _root;
    public FolderHubStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root); Directory.CreateDirectory(Path.Combine(_root, "modules")); Directory.CreateDirectory(Path.Combine(_root, "files"));
    }
    public string Root => _root;
    string Index => Path.Combine(_root, "hub.json");
    string Module(string id) => Path.Combine(_root, "modules", id + ".json");
    string FileAt(string name) => Path.Combine(_root, "files", name);
    static void Atomic(string path, byte[] data)
    {
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, true);
    }
    public string ReadIndex() => File.Exists(Index) ? File.ReadAllText(Index) : null;
    public void WriteIndex(string json) => Atomic(Index, Encoding.UTF8.GetBytes(json));
    public string ReadModule(string id) => File.Exists(Module(id)) ? File.ReadAllText(Module(id)) : null;
    public void WriteModule(string id, string json) => Atomic(Module(id), Encoding.UTF8.GetBytes(json));
    public bool HasFile(string name) => File.Exists(FileAt(name));
    public byte[] ReadFile(string name) => File.Exists(FileAt(name)) ? File.ReadAllBytes(FileAt(name)) : null;
    public void WriteFile(string name, byte[] data) => Atomic(FileAt(name), data);
    public void DeleteFile(string name) { try { File.Delete(FileAt(name)); } catch { } }
    public string PathOf(string name) => File.Exists(FileAt(name)) ? FileAt(name) : null;
}

public sealed class HubBoughtEntry
{
    public string id = "";
    public int price;
    public long atUnixMs;
}

public sealed class HubUser
{
    public string id = "", nickname = "";
    public string tokenHash = "";       // the token itself is never kept: only what it hashes to
    public int stars, sales, earned;
    public long joinedUnixMs;
    public bool official;
    public List<HubBoughtEntry> bought = new();
    public List<string> liked = new();
}

public sealed class HubEntry
{
    public string id = "", sellerId = "";
    public string kind = "", name = "", summary = "", gives = "", icon = "", template = "", version = "1.0";
    public string[] tags = new string[0];
    public int price, installs, likes, earned;
    public long createdUnixMs, updatedUnixMs;
    public bool listed = true;
    public List<string> files = new();  // the files that belong to it (in the store, under these names)
    public HubDetail detail = new();
}

public sealed class HubIndex
{
    public int format = 1;
    /// <summary>A random id given to this hub's data once (GET /api/hello shows it): a client can tell this hub from another one at the same address, or from this one after its data was wiped.</summary>
    public string hubId = "";
    public long revision;
    public List<HubUser> users = new();
    public List<HubEntry> listings = new();
}

/// <summary>The answer to one call: what was asked for, or why not (an HTTP status and a word for it).</summary>
public sealed class HubAnswer<T>
{
    public int status = 200;
    public string error = "";
    public int missing;
    public string detail = "";
    public T value;
    public bool Ok => error.Length == 0;
    public static HubAnswer<T> Of(T value) => new() { value = value };
    public static HubAnswer<T> No(int status, string error, int missing = 0, string detail = "") => new() { status = status, error = error, missing = missing, detail = detail };
}

/// <summary>A file of a listing, for whoever may have it.</summary>
public sealed class HubFile
{
    public string name = "", contentType = "", path;
    public byte[] data;
}

public sealed class Hub
{
    readonly IHubStore _store;
    readonly object _gate = new();
    readonly Func<long> _now;
    HubIndex _x;
    readonly Dictionary<string, HubUser> _byToken = new(), _byId = new(), _byNick = new();
    readonly Dictionary<string, HubEntry> _listings = new();

    public Hub(IHubStore store, Func<long> nowUnixMs = null)
    {
        _store = store;
        _now = nowUnixMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _x = Proto.FromJson<HubIndex>(store.ReadIndex() ?? "") ?? new HubIndex();
        _x.users ??= new(); _x.listings ??= new();
        _x.users.RemoveAll(u => u == null || string.IsNullOrEmpty(u.id));
        _x.listings.RemoveAll(l => l == null || string.IsNullOrEmpty(l.id));
        foreach (var u in _x.users)
        {
            u.bought ??= new(); u.liked ??= new(); u.nickname ??= ""; u.tokenHash ??= "";
            _byId[u.id] = u; _byNick[HubRules.NickKey(u.nickname)] = u;
            if (u.tokenHash.Length > 0) _byToken[u.tokenHash] = u;
        }
        foreach (var l in _x.listings) { l.files ??= new(); l.tags ??= new string[0]; l.detail ??= new HubDetail(); _listings[l.id] = l; }
        // data from before the hub had an id (or brand new data) is given one now, and it is written down at once so
        // that it is the same the next time (the revision is the listings' and stays as it is)
        if (string.IsNullOrEmpty(_x.hubId))
        {
            _x.hubId = "hub-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            _store.WriteIndex(Proto.ToJson(_x));
        }
    }

    /// <summary>This hub's own id (see HubIndex.hubId).</summary>
    public string HubId { get { lock (_gate) return _x.hubId; } }

    /// <summary>
    /// "Not signed in" (401). When a token was sent and no account has it, the answer says so in so many words
    /// (detail "unknown-account"): only then does a client know its account is gone here and makes a new one - a 401
    /// from anything else in between must not cost anybody their account.
    /// </summary>
    static HubAnswer<T> NoAccount<T>(string token) => HubAnswer<T>.No(401, "unauthorized", 0, string.IsNullOrEmpty(token) ? "" : HubRules.UnknownAccount);

    public long Revision { get { lock (_gate) return _x.revision; } }
    public int Users { get { lock (_gate) return _x.users.Count; } }
    public int Listed { get { lock (_gate) return _x.listings.Count(l => l.listed); } }

    void Save() { _x.revision++; _store.WriteIndex(Proto.ToJson(_x)); }

    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));

    HubUser Who(string token) => !string.IsNullOrEmpty(token) && _byToken.TryGetValue(Hash(token), out var u) ? u : null;

    string SellerName(HubEntry l) => _byId.TryGetValue(l.sellerId, out var u) ? u.nickname : "?";
    bool Official(HubEntry l) => _byId.TryGetValue(l.sellerId, out var u) && u.official;

    HubListingInfo Info(HubEntry l, bool detail, bool forSeller = false) => new HubListingInfo
    {
        id = l.id, kind = l.kind, name = l.name, summary = l.summary, gives = l.gives, seller = SellerName(l), icon = l.icon, template = l.template, version = l.version,
        price = l.price, installs = l.installs, likes = l.likes, createdUnixMs = l.createdUnixMs, updatedUnixMs = l.updatedUnixMs, official = Official(l), listed = l.listed,
        earned = forSeller ? l.earned : 0, tags = l.tags, detail = detail ? l.detail : null,
    };

    // ------------------------------------------------------------------ accounts
    /// <summary>A new account under a nickname nobody has, with the stars every newcomer gets. The token in the answer is the only copy.</summary>
    public HubAnswer<HubRegistered> Register(string nickname)
    {
        string nick = HubRules.CleanNickname(nickname), problem = HubRules.NicknameProblem(nick);
        if (problem.Length > 0) return HubAnswer<HubRegistered>.No(problem == "nickname-reserved" ? 409 : 400, problem);
        lock (_gate)
        {
            if (_byNick.ContainsKey(HubRules.NickKey(nick))) return HubAnswer<HubRegistered>.No(409, "nickname-taken");
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var u = new HubUser { id = "u-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), nickname = nick, tokenHash = Hash(token), stars = HubRules.StartStars, joinedUnixMs = _now() };
            _x.users.Add(u); _byId[u.id] = u; _byNick[HubRules.NickKey(nick)] = u; _byToken[u.tokenHash] = u;
            Save();
            return HubAnswer<HubRegistered>.Of(new HubRegistered { userId = u.id, token = token, nickname = u.nickname, stars = u.stars });
        }
    }

    public HubAnswer<HubMe> Rename(string token, string nickname)
    {
        string nick = HubRules.CleanNickname(nickname), problem = HubRules.NicknameProblem(nick);
        lock (_gate)
        {
            var u = Who(token);
            if (u == null) return NoAccount<HubMe>(token);
            if (problem.Length > 0) return HubAnswer<HubMe>.No(problem == "nickname-reserved" ? 409 : 400, problem);
            string key = HubRules.NickKey(nick);
            if (_byNick.TryGetValue(key, out var other) && other != u) return HubAnswer<HubMe>.No(409, "nickname-taken");
            if (u.nickname != nick)
            {
                _byNick.Remove(HubRules.NickKey(u.nickname));
                u.nickname = nick; _byNick[key] = u;
                Save();
            }
            return HubAnswer<HubMe>.Of(MeOf(u));
        }
    }

    public HubAnswer<HubMe> Me(string token)
    {
        lock (_gate)
        {
            var u = Who(token);
            return u == null ? NoAccount<HubMe>(token) : HubAnswer<HubMe>.Of(MeOf(u));
        }
    }

    HubMe MeOf(HubUser u) => new HubMe
    {
        userId = u.id, nickname = u.nickname, stars = u.stars, sales = u.sales, earned = u.earned, joinedUnixMs = u.joinedUnixMs, revision = _x.revision,
        bought = u.bought.Select(b => b.id).ToList(), liked = u.liked.ToList(),
        purchases = u.bought.Where(b => _listings.ContainsKey(b.id)).Select(b =>
        {
            var l = _listings[b.id];
            return new HubPurchase { id = l.id, name = l.name, kind = l.kind, seller = SellerName(l), icon = l.icon, price = b.price, atUnixMs = b.atUnixMs, listed = l.listed };
        }).ToList(),
        listings = _x.listings.Where(l => l.sellerId == u.id).Select(l => Info(l, true, true)).ToList(),
    };

    /// <summary>What anyone may know about a seller.</summary>
    public HubAnswer<HubProfile> Profile(string nickname)
    {
        lock (_gate)
        {
            string key = HubRules.NickKey(nickname);
            if (key == "bambi studio") key = HubRules.NickKey(HubRules.Official);
            if (!_byNick.TryGetValue(key, out var u)) return HubAnswer<HubProfile>.No(404, "not-found");
            var theirs = _x.listings.Where(l => l.sellerId == u.id).ToList();
            return HubAnswer<HubProfile>.Of(new HubProfile
            {
                nickname = u.nickname, joinedUnixMs = u.joinedUnixMs, official = u.official, installs = theirs.Sum(l => l.installs),
                listings = Sorted(theirs.Where(l => l.listed), "popular").Select(l => Info(l, false)).ToList(),
            });
        }
    }

    // ------------------------------------------------------------------ looking round the shop
    static IEnumerable<HubEntry> Sorted(IEnumerable<HubEntry> list, string sort) => sort switch
    {
        "new" => list.OrderByDescending(l => l.createdUnixMs).ThenBy(l => l.name, StringComparer.OrdinalIgnoreCase),
        "price" => list.OrderBy(l => l.price).ThenByDescending(l => l.installs).ThenBy(l => l.name, StringComparer.OrdinalIgnoreCase),
        _ => list.OrderByDescending(l => l.installs).ThenByDescending(l => l.likes).ThenByDescending(l => l.createdUnixMs),
    };

    /// <summary>What is on sale. kind, q (every word of it must be found in the name, the lines, the tags or the seller's name) and seller narrow it down; sort: popular | new | price.</summary>
    public HubListings Listings(string kind = "", string q = "", string sort = "popular", string seller = "", bool detail = false, int limit = 300)
    {
        lock (_gate)
        {
            IEnumerable<HubEntry> list = _x.listings.Where(l => l.listed);
            if (!string.IsNullOrEmpty(kind)) list = list.Where(l => l.kind == kind);
            if (!string.IsNullOrWhiteSpace(seller)) { string key = HubRules.NickKey(seller); list = list.Where(l => HubRules.NickKey(SellerName(l)) == key); }
            string[] words = (q ?? "").ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
                list = list.Where(l =>
                {
                    string hay = string.Join(" ", new[] { l.name, l.summary, l.gives, l.kind, SellerName(l) }.Concat(l.tags)).ToLowerInvariant();
                    return words.All(w => hay.Contains(w));
                });
            return new HubListings { revision = _x.revision, list = Sorted(list, sort ?? "").Take(Math.Clamp(limit, 1, 1000)).Select(l => Info(l, detail)).ToList() };
        }
    }

    /// <summary>One listing, on sale or not (somebody who bought it earlier still has a page to look at).</summary>
    public HubAnswer<HubListingInfo> Listing(string id)
    {
        lock (_gate) return _listings.TryGetValue(id ?? "", out var l) ? HubAnswer<HubListingInfo>.Of(Info(l, true)) : HubAnswer<HubListingInfo>.No(404, "gone");
    }

    // ------------------------------------------------------------------ selling
    /// <summary>
    /// Puts a module up for sale under the seller's name - or brings their listing of the same id up to date (a new
    /// version, a new price, back on sale). The module is checked exactly as one the host would install; the files it
    /// names are kept under names of the hub's own making (the listing's id and what the file hashes to), so that a
    /// file never changes under its name and nobody's upload can land on somebody else's.
    /// </summary>
    public HubAnswer<HubListingInfo> Publish(string token, HubPublish p)
    {
        if (p?.module == null) return HubAnswer<HubListingInfo>.No(400, "bad-request");
        var m = p.module;
        if (p.price < 0 || p.price > HubRules.MaxPrice) return HubAnswer<HubListingInfo>.No(400, "price-invalid");
        string problem = MarketPack.Sanitise(m);
        if (problem.Length == 0 && m.id.Length > HubRules.MaxIdLength) problem = "the id is too long";
        if (problem.Length > 0) return HubAnswer<HubListingInfo>.No(400, "module-invalid", 0, problem);
        var files = p.files ?? new Dictionary<string, string>();
        if (files.Count > HubRules.MaxFiles) return HubAnswer<HubListingInfo>.No(413, "too-many-files");
        var data = new Dictionary<string, byte[]>();
        foreach (var f in files)
        {
            if (!HubRules.SafeFile(f.Key)) return HubAnswer<HubListingInfo>.No(400, "file-invalid", 0, "not a plain .png or .glb file name");
            // (the length is known from the text before anything is decoded: nothing oversized is ever unpacked)
            // (a picture may be 600 kB, a model 16 MB: see HubRules.MaxPictureBytes)
            int most = HubRules.MaxBytesFor(f.Key);
            if (f.Value == null || f.Value.Length / 4 * 3 > most + 3) return HubAnswer<HubListingInfo>.No(413, "too-large", 0, f.Key);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(f.Value); } catch { return HubAnswer<HubListingInfo>.No(400, "file-invalid", 0, f.Key); }
            if (bytes.Length > most) return HubAnswer<HubListingInfo>.No(413, "too-large", 0, f.Key);
            if (!MarketPack.LooksRight(f.Key, bytes)) return HubAnswer<HubListingInfo>.No(400, "file-invalid", 0, f.Key + " is not what its name says");
            data[f.Key] = bytes;
        }
        lock (_gate)
        {
            var u = Who(token);
            if (u == null) return NoAccount<HubListingInfo>(token);
            _listings.TryGetValue(m.id, out var old);
            if (old != null && old.sellerId != u.id) return HubAnswer<HubListingInfo>.No(409, "id-taken");
            long now = _now();
            // every file the module names: one that came with this request is stored, one the listing has already is kept, anything else is dropped
            var kept = new List<string>();
            foreach (var r in MarketPack.FileRefs(m))
            {
                if (r.name.Length == 0) continue;
                if (data.TryGetValue(r.name, out byte[] bytes) && r.name.EndsWith(r.Ext, StringComparison.OrdinalIgnoreCase))
                {
                    string stored = $"{m.id}-{Convert.ToHexString(SHA256.HashData(bytes))[..10].ToLowerInvariant()}{r.Ext}";
                    if (!_store.HasFile(stored)) _store.WriteFile(stored, bytes);
                    r.set(stored);
                    if (!kept.Contains(stored)) kept.Add(stored);
                }
                else if (old != null && old.files.Contains(r.name)) { if (!kept.Contains(r.name)) kept.Add(r.name); }
                else r.set("");
            }
            m.author = u.nickname; m.price = p.price; m.publishedUnixMs = old?.createdUnixMs ?? now;
            string json = Proto.ToJson(m);
            if (Encoding.UTF8.GetByteCount(json) > HubRules.MaxModuleJson)
            {
                foreach (string f in kept) if (old == null || !old.files.Contains(f)) _store.DeleteFile(f);
                return HubAnswer<HubListingInfo>.No(413, "too-large", 0, "the module itself");
            }
            var l = old ?? new HubEntry { id = m.id, sellerId = u.id, createdUnixMs = now };
            foreach (string f in l.files) if (!kept.Contains(f)) _store.DeleteFile(f);
            Fill(l, m);
            l.files = kept; l.listed = true; l.updatedUnixMs = now;
            if (old == null) { _x.listings.Add(l); _listings[l.id] = l; }
            _store.WriteModule(l.id, json);
            Save();
            return HubAnswer<HubListingInfo>.Of(Info(l, true, true));
        }
    }

    static void Fill(HubEntry l, MarketModule m)
    {
        l.kind = m.kind; l.name = m.name; l.summary = m.summary; l.gives = m.gives; l.icon = m.icon ?? ""; l.template = m.game?.template ?? ""; l.version = m.version; l.tags = m.tags; l.price = m.price;
        l.detail = MarketPack.Detail(m);
    }

    /// <summary>The seller changes the price or the line under the name.</summary>
    public HubAnswer<HubListingInfo> Edit(string token, string id, HubEdit e)
    {
        lock (_gate)
        {
            var u = Who(token);
            if (u == null) return NoAccount<HubListingInfo>(token);
            if (!_listings.TryGetValue(id ?? "", out var l)) return HubAnswer<HubListingInfo>.No(404, "gone");
            if (l.sellerId != u.id) return HubAnswer<HubListingInfo>.No(403, "not-yours");
            if (e == null) return HubAnswer<HubListingInfo>.No(400, "bad-request");
            if (e.price is { } price && (price < 0 || price > HubRules.MaxPrice)) return HubAnswer<HubListingInfo>.No(400, "price-invalid");
            bool changed = false;
            if (e.price is { } to && to != l.price) { l.price = to; changed = true; }
            if (e.summary != null) { string s = WorldRules.Short(e.summary, 200); if (s != l.summary) { l.summary = s; changed = true; } }
            if (changed)
            {
                // the module that is handed out says the same as the listing
                var m = Proto.FromJson<MarketModule>(_store.ReadModule(l.id) ?? "");
                if (m != null) { m.price = l.price; m.summary = l.summary; _store.WriteModule(l.id, Proto.ToJson(m)); }
                l.updatedUnixMs = _now();
                Save();
            }
            return HubAnswer<HubListingInfo>.Of(Info(l, true, true));
        }
    }

    /// <summary>The seller takes it off sale. Nobody new can get it; those who have it keep it, and can still fetch it.</summary>
    public HubAnswer<HubListingInfo> Unlist(string token, string id)
    {
        lock (_gate)
        {
            var u = Who(token);
            if (u == null) return NoAccount<HubListingInfo>(token);
            if (!_listings.TryGetValue(id ?? "", out var l)) return HubAnswer<HubListingInfo>.No(404, "gone");
            if (l.sellerId != u.id) return HubAnswer<HubListingInfo>.No(403, "not-yours");
            if (l.listed) { l.listed = false; l.updatedUnixMs = _now(); Save(); }
            return HubAnswer<HubListingInfo>.Of(Info(l, true, true));
        }
    }

    // ------------------------------------------------------------------ buying
    /// <summary>
    /// The buyer gets the listing: its price in stars goes from them to the seller, the purchase is written down and
    /// the listing has been got once more - all of it in one step. Something free is simply got. What they have
    /// already costs nothing a second time.
    /// expectedPrice: what the buyer saw it cost (null: they did not say). A seller can change a price at any moment;
    /// when it is not what the buyer saw, nothing is charged and the answer is "price-changed" with the price it is now.
    /// </summary>
    public HubAnswer<HubBought> Buy(string token, string id, int? expectedPrice = null)
    {
        lock (_gate)
        {
            var u = Who(token);
            if (u == null) return NoAccount<HubBought>(token);
            if (!_listings.TryGetValue(id ?? "", out var l)) return HubAnswer<HubBought>.No(404, "gone");
            if (l.sellerId == u.id) return HubAnswer<HubBought>.No(403, "own-listing");
            if (u.bought.Any(b => b.id == l.id)) return HubAnswer<HubBought>.Of(new HubBought { ok = true, already = true, id = l.id, price = 0, stars = u.stars });
            if (!l.listed) return HubAnswer<HubBought>.No(404, "gone");
            if (expectedPrice is { } seen && seen != l.price) return HubAnswer<HubBought>.No(409, HubRules.PriceChanged, 0, l.price.ToString());
            if (u.stars < l.price) return HubAnswer<HubBought>.No(402, "not-enough-stars", l.price - u.stars);
            int price = l.price;
            u.stars -= price;
            if (_byId.TryGetValue(l.sellerId, out var seller)) { seller.stars += price; seller.sales++; seller.earned += price; }
            l.installs++; l.earned += price;
            u.bought.Add(new HubBoughtEntry { id = l.id, price = price, atUnixMs = _now() });
            Save();
            return HubAnswer<HubBought>.Of(new HubBought { ok = true, id = l.id, price = price, stars = u.stars });
        }
    }

    /// <summary>May this account (null: nobody in particular) have what the listing sells? Its seller, somebody who bought it, or anyone at all while it is on sale for nothing.</summary>
    bool MayHave(HubUser u, HubEntry l) => (l.price == 0 && l.listed) || (u != null && (l.sellerId == u.id || u.bought.Any(b => b.id == l.id)));

    /// <summary>The module a listing sells, as JSON, for whoever may have it.</summary>
    public HubAnswer<string> Package(string token, string id)
    {
        lock (_gate)
        {
            var u = Who(token);
            if (!_listings.TryGetValue(id ?? "", out var l)) return HubAnswer<string>.No(404, "gone");
            if (!MayHave(u, l)) return u == null ? NoAccount<string>(token) : HubAnswer<string>.No(403, "not-owned");
            var m = Proto.FromJson<MarketModule>(_store.ReadModule(l.id) ?? "");
            if (m == null) return HubAnswer<string>.No(404, "gone");
            m.author = SellerName(l);       // under the name the seller goes by today
            m.installs = l.installs;
            return HubAnswer<string>.Of(Proto.ToJson(m));
        }
    }

    /// <summary>The tune a listing carries, for listening to in the shop before buying ("" when it has none).</summary>
    public HubAnswer<string> Song(string id)
    {
        lock (_gate)
        {
            if (!_listings.TryGetValue(id ?? "", out var l)) return HubAnswer<string>.No(404, "gone");
            var song = MarketRules.SongOf(Proto.FromJson<MarketModule>(_store.ReadModule(l.id) ?? ""));
            return song == null ? HubAnswer<string>.No(404, "no-tune") : HubAnswer<string>.Of(Proto.ToJson(song));
        }
    }

    /// <summary>A file of a listing. Pictures are for everyone (the shop shows them); a model is part of what is sold.</summary>
    public HubAnswer<HubFile> FileOf(string token, string name)
    {
        if (!HubRules.SafeFile(name)) return HubAnswer<HubFile>.No(404, "not-found");
        lock (_gate)
        {
            var l = _x.listings.FirstOrDefault(x => x.files.Contains(name));
            if (l == null || !_store.HasFile(name)) return HubAnswer<HubFile>.No(404, "not-found");
            bool picture = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            if (!picture)
            {
                var u = Who(token);
                if (!MayHave(u, l)) return u == null ? NoAccount<HubFile>(token) : HubAnswer<HubFile>.No(403, "not-owned");
            }
            string path = _store.PathOf(name);
            return HubAnswer<HubFile>.Of(new HubFile { name = name, contentType = picture ? "image/png" : "model/gltf-binary", path = path, data = path == null ? _store.ReadFile(name) : null });
        }
    }

    /// <summary>Likes a listing, or takes the like back.</summary>
    public HubAnswer<HubLiked> Like(string token, string id)
    {
        lock (_gate)
        {
            var u = Who(token);
            if (u == null) return NoAccount<HubLiked>(token);
            if (!_listings.TryGetValue(id ?? "", out var l)) return HubAnswer<HubLiked>.No(404, "gone");
            bool liked = !u.liked.Remove(l.id);
            if (liked) u.liked.Add(l.id);
            l.likes = Math.Max(0, l.likes + (liked ? 1 : -1));
            Save();
            return HubAnswer<HubLiked>.Of(new HubLiked { liked = liked, likes = l.likes });
        }
    }

    // ------------------------------------------------------------------ the official shop
    /// <summary>
    /// What the makers of the partner sell: every module that comes with it, listed under "Nilo Studio" at the price
    /// it has there. Run at every start: a new module appears, a changed one is brought up to date (how often it was
    /// got and liked stays), one that no longer exists goes off sale. Returns how many listings changed.
    /// </summary>
    public int SeedOfficial(IEnumerable<MarketSeedEntry> seeds)
    {
        lock (_gate)
        {
            int changed = 0;
            var studio = _x.users.FirstOrDefault(u => u.official);
            if (studio == null)
            {
                // no token: nobody signs in as the makers
                studio = new HubUser { id = "u-official", nickname = HubRules.Official, official = true, joinedUnixMs = _now() };
                _x.users.Add(studio); _byId[studio.id] = studio; _byNick[HubRules.NickKey(studio.nickname)] = studio;
                changed++;
            }
            else if (studio.nickname != HubRules.Official && !_byNick.ContainsKey(HubRules.NickKey(HubRules.Official)))
            {
                _byNick.Remove(HubRules.NickKey(studio.nickname));
                studio.nickname = HubRules.Official;
                _byNick[HubRules.NickKey(studio.nickname)] = studio;
                changed++;
            }
            var ids = new HashSet<string>();
            foreach (var seed in seeds)
            {
                var m = MarketRules.Clone(seed.module);
                m.author = HubRules.Official;
                ids.Add(m.id);
                string json = Proto.ToJson(m);
                _listings.TryGetValue(m.id, out var l);
                if (l != null && l.sellerId != studio.id) continue;       // (somebody's own listing got there first under this id: theirs is left alone)
                if (l != null && l.listed && _store.ReadModule(m.id) == json) continue;
                if (l == null) { l = new HubEntry { id = m.id, sellerId = studio.id, createdUnixMs = _now() }; _x.listings.Add(l); _listings[l.id] = l; }
                Fill(l, m);
                l.listed = true; l.updatedUnixMs = _now();
                _store.WriteModule(m.id, json);
                changed++;
            }
            foreach (var l in _x.listings.Where(l => l.sellerId == studio.id && l.listed && !ids.Contains(l.id))) { l.listed = false; changed++; }
            if (changed > 0) Save();
            return changed;
        }
    }
}
