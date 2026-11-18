using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>The hub said no, or could not be reached at all.</summary>
public sealed class HubException : Exception
{
    public int Status;                  // the HTTP status (0: nothing answered)
    public string Code = "";            // the hub's word for it: not-enough-stars, own-listing, gone, nickname-taken ...
    public int Missing;                 // not-enough-stars: how many more are needed
    public string Detail = "";
    public bool Unreachable => Status == 0;
    public HubException(int status, string code, string message) : base(message) { Status = status; Code = code ?? ""; }
}

/// <summary>
/// The calls a client makes to the hub (see LivingPartner.MarketHub\Program.cs), and nothing else: no state, no files.
/// The host uses it for the user's own account; lpctl's test helper uses the very same code for a second one. A
/// token is only ever put into the Authorization header - it is in no address and in no error message.
/// </summary>
public sealed class HubApi : IDisposable
{
    readonly HttpClient _http;
    public string Url { get; }

    public HubApi(string url)
    {
        Url = HubRules.CleanUrl(url);
        // (no proxy: the hub is this PC or one on the same network, and a system proxy must not be asked about 127.0.0.1)
        _http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(4), PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = new Uri((Url.Length > 0 ? Url : HubRules.DefaultUrl) + "/"),
            Timeout = Timeout.InfiniteTimeSpan,        // each call brings its own
        };
    }

    public void Dispose() => _http.Dispose();

    async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object body, int seconds, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        var req = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) req.Content = new StringContent(Proto.ToJson(body), Encoding.UTF8, "application/json");
        HttpResponseMessage r;
        try { r = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new HubException(0, "unreachable", "the hub did not answer in time"); }
        catch (HttpRequestException e) { throw new HubException(0, "unreachable", "the hub cannot be reached: " + e.Message); }
        finally { req.Dispose(); }
        if (r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotModified) return r;
        using (r)
        {
            string text = "";
            try { text = await r.Content.ReadAsStringAsync(ct); } catch { }
            var err = Proto.FromJson<HubError>(text);
            throw new HubException((int)r.StatusCode, err?.error ?? "", $"the hub answered {(int)r.StatusCode}{(string.IsNullOrEmpty(err?.error) ? "" : " " + err.error)}{(string.IsNullOrEmpty(err?.detail) ? "" : " (" + err.detail + ")")}") { Missing = err?.missing ?? 0, Detail = err?.detail ?? "" };
        }
    }

    /// <summary>most: the answer is refused ("too-large") when it is longer than this many bytes (0 = no limit of its own).</summary>
    async Task<T> Ask<T>(HttpMethod method, string path, string token, object body, CancellationToken ct, int seconds = 8, int most = 0) where T : class
    {
        using var r = await Send(method, path, token, body, seconds, ct);
        if (r.StatusCode == HttpStatusCode.NotModified) return null;
        string text = most > 0 ? Encoding.UTF8.GetString(await Read(r, most, ct)) : await Body(r, ct);
        return Proto.FromJson<T>(text) ?? throw new HubException((int)r.StatusCode, "bad-answer", "the hub's answer could not be read");
    }

    /// <summary>
    /// The body of an answer as text. The headers have arrived by now, the body has not: a connection that breaks
    /// while it is read is the hub not being reachable, and is said so in the same way as one that never answered
    /// (it used to surface as a bare IOException that the callers took for something else).
    /// </summary>
    static async Task<string> Body(HttpResponseMessage r, CancellationToken ct)
    {
        try { return await r.Content.ReadAsStringAsync(ct); }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw new HubException(0, "unreachable", "the hub's answer broke off: " + e.Message); }
    }

    /// <summary>The body of an answer as bytes, never more than `most` of them (whatever its length is said to be).</summary>
    static async Task<byte[]> Read(HttpResponseMessage r, int most, CancellationToken ct)
    {
        if (r.Content.Headers.ContentLength > most) throw new HubException(413, "too-large", "the hub's answer is larger than it may be");
        try
        {
            using var s = await r.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = await s.ReadAsync(buf, ct)) > 0)
            {
                if (ms.Length + n > most) throw new HubException(413, "too-large", "the hub's answer is larger than it may be");
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw new HubException(0, "unreachable", "the hub's answer broke off: " + e.Message); }
    }

    static string E(string s) => Uri.EscapeDataString(s ?? "");

    /// <summary>Who answers at this address: null when it is not a hub (something else may be listening on the port: only a hub says it is one) or nothing answers.</summary>
    public async Task<HubHello> Hello(CancellationToken ct, int seconds = 2)
    {
        try
        {
            var h = await Ask<HubHello>(HttpMethod.Get, "api/hello", null, null, ct, seconds, 4096);
            return string.IsNullOrEmpty(h?.official) ? null : h;
        }
        catch (HubException) { return null; }
    }

    /// <summary>Does a hub answer at this address?</summary>
    public async Task<bool> Alive(CancellationToken ct, int seconds = 2) => await Hello(ct, seconds) != null;

    public Task<HubRegistered> Register(string nickname, CancellationToken ct) => Ask<HubRegistered>(HttpMethod.Post, "api/register", null, new HubNickname { nickname = nickname }, ct);
    public Task<HubMe> Me(string token, CancellationToken ct) => Ask<HubMe>(HttpMethod.Get, "api/me", token, null, ct);
    public Task<HubMe> Rename(string token, string nickname, CancellationToken ct) => Ask<HubMe>(HttpMethod.Put, "api/me", token, new HubNickname { nickname = nickname }, ct);
    public Task<HubProfile> Profile(string nickname, CancellationToken ct) => Ask<HubProfile>(HttpMethod.Get, "api/users/" + E(nickname), null, null, ct);

    /// <summary>What is on sale, with what the shop shows of each. since: the revision the caller has already - then null comes back when nothing changed.</summary>
    public Task<HubListings> Listings(CancellationToken ct, long since = -1, string q = "", string kind = "", string sort = "popular", string seller = "") =>
        Ask<HubListings>(HttpMethod.Get, $"api/listings?detail=1&limit=1000&sort={E(sort)}{(since >= 0 ? "&since=" + since : "")}{(q.Length > 0 ? "&q=" + E(q) : "")}{(kind.Length > 0 ? "&kind=" + E(kind) : "")}{(seller.Length > 0 ? "&seller=" + E(seller) : "")}", null, null, ct);

    public Task<HubListingInfo> Listing(string id, CancellationToken ct) => Ask<HubListingInfo>(HttpMethod.Get, "api/listings/" + E(id), null, null, ct);
    /// <summary>price: what the buyer saw it cost (null: not said). The hub charges only that price, and refuses with "price-changed" when the listing costs something else now.</summary>
    public Task<HubBought> Buy(string token, string id, CancellationToken ct, int? price = null) =>
        Ask<HubBought>(HttpMethod.Post, $"api/listings/{E(id)}/buy", token, price == null ? null : new HubBuy { price = price }, ct);
    public Task<HubLiked> Like(string token, string id, CancellationToken ct) => Ask<HubLiked>(HttpMethod.Post, $"api/listings/{E(id)}/like", token, null, ct);
    /// <summary>What a listing sells. Never more than a module may be on the hub (and a little, for the seller's name and the count it is handed out with): a hub cannot make this PC read an answer without end.</summary>
    public Task<MarketModule> Package(string token, string id, CancellationToken ct) => Ask<MarketModule>(HttpMethod.Get, $"api/listings/{E(id)}/package", token, null, ct, 20, HubRules.MaxModuleJson + 4096);
    public Task<SongDef> Song(string id, CancellationToken ct) => Ask<SongDef>(HttpMethod.Get, $"api/listings/{E(id)}/song", null, null, ct);
    public Task<HubListingInfo> Edit(string token, string id, HubEdit edit, CancellationToken ct) => Ask<HubListingInfo>(HttpMethod.Put, "api/listings/" + E(id), token, edit, ct);
    public Task<HubListingInfo> Unlist(string token, string id, CancellationToken ct) => Ask<HubListingInfo>(HttpMethod.Delete, "api/listings/" + E(id), token, null, ct);

    /// <summary>Puts a module up for sale with its files (this one may take a while: a model is several megabytes).</summary>
    public Task<HubListingInfo> Publish(string token, HubPublish offer, CancellationToken ct) => Ask<HubListingInfo>(HttpMethod.Post, "api/listings", token, offer, ct, 180);

    /// <summary>A file of a listing; never more than the hub allows a file of its kind to be (a picture 600 kB, a model 16 MB).</summary>
    public async Task<byte[]> File(string token, string name, CancellationToken ct)
    {
        if (!HubRules.SafeFile(name)) throw new HubException(404, "not-found", "not a file name");
        using var r = await Send(HttpMethod.Get, "files/" + E(name), token, null, 120, ct);
        return await Read(r, HubRules.MaxBytesFor(name), ct);
    }
}
