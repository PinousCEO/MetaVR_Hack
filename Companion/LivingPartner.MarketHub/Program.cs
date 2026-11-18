using System.Diagnostics;
using System.Net;
using System.Text;
using LivingPartner.Host;
using LivingPartner.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace LivingPartner.MarketHub;

/// <summary>
/// The hub as a web server. One process is one marketplace.
///   LivingPartner.MarketHub [--port 47620] [--data &lt;folder&gt;] [--lan] [--parent &lt;pid&gt;]
/// Without --lan it answers this machine only. --parent: it ends when that process does (the host starts it that way
/// on a PC that has no other hub to talk to, so that it never outlives it).
///
///   POST   /api/register {nickname}             -> {userId, token, nickname, stars}
///   GET    /api/me                               (Bearer)  PUT /api/me {nickname}
///   GET    /api/users/{nickname}
///   GET    /api/listings?kind=&amp;q=&amp;sort=popular|new|price&amp;seller=&amp;detail=1&amp;since=&lt;revision&gt;
///   GET    /api/listings/{id}
///   POST   /api/listings {module, price, files}  (Bearer)
///   PUT    /api/listings/{id} {price?, summary?} (Bearer, the seller)   DELETE /api/listings/{id}
///   POST   /api/listings/{id}/buy [{price}]      (Bearer; price = what the buyer saw: refused with "price-changed" when it differs)   POST /api/listings/{id}/like
///   GET    /api/listings/{id}/package            (Bearer unless it is free)
///   GET    /api/listings/{id}/song               the tune it carries, for listening before buying
///   GET    /files/{name}                         pictures for everyone, models for whoever may have them
///   GET    /api/hello                            who answers here
/// Every refusal is JSON: {error, missing?, detail?}.
/// </summary>
public static class Program
{
    static readonly object LogGate = new();
    static StreamWriter _log;

    static string Arg(string[] a, string name, string fallback = null)
    {
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return fallback;
    }

    static void Line(string s)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {s}";
        lock (LogGate)
        {
            try { Console.WriteLine(line); } catch { }
            try { _log?.WriteLine(line); } catch { }
        }
    }

    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        if (!int.TryParse(Arg(args, "--port", HubRules.DefaultPort.ToString()), out int port) || port < 1 || port > 65535) { Console.Error.WriteLine("--port needs a number between 1 and 65535"); return 2; }
        bool lan = args.Contains("--lan");
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        string data = Arg(args, "--data") ?? (string.IsNullOrEmpty(local) ? Path.Combine(Directory.GetCurrentDirectory(), "hub-data") : Path.Combine(local, "LivingAIPartner", "hub"));

        FolderHubStore store;
        Hub hub;
        try
        {
            store = new FolderHubStore(data);
            _log = new StreamWriter(Path.Combine(store.Root, "hub.log"), true, Encoding.UTF8) { AutoFlush = true };
            hub = new Hub(store);
            int seeded = hub.SeedOfficial(MarketSeed.All());
            Line($"hub: data in {store.Root}; {hub.Users} account(s), {hub.Listed} listing(s) on sale{(seeded > 0 ? $"; the official shop was brought up to date ({seeded} change(s))" : "")}");
        }
        catch (Exception e) { Console.Error.WriteLine("the hub could not open its data folder: " + e.Message); return 3; }

        if (int.TryParse(Arg(args, "--parent", ""), out int parent)) WatchParent(parent);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
        {
            if (lan) o.ListenAnyIP(port); else o.ListenLocalhost(port);
            // the one large request is a module put up for sale with its files (eight files of 16 MB, as base64, and the module)
            o.Limits.MaxRequestBodySize = 200L * 1024 * 1024;
        });
        var app = builder.Build();

        // one line per request: what was asked, what was answered, how long it took. Never a header, so never a token.
        app.Use(async (ctx, next) =>
        {
            var sw = Stopwatch.StartNew();
            try { await next(); }
            catch (BadHttpRequestException e) { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = e.StatusCode; await ctx.Response.WriteAsync(Proto.ToJson(new HubError { error = e.StatusCode == 413 ? "too-large" : "bad-request" })); } }
            catch (Exception e)
            {
                Line("hub: " + ctx.Request.Method + " " + ctx.Request.Path + " failed: " + e.GetType().Name + ": " + e.Message);
                if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 500; ctx.Response.ContentType = "application/json"; await ctx.Response.WriteAsync(Proto.ToJson(new HubError { error = "hub-error" })); }
            }
            Line($"{ctx.Connection.RemoteIpAddress} {ctx.Request.Method} {ctx.Request.Path}{Safe(ctx.Request.QueryString.Value)} -> {ctx.Response.StatusCode} {sw.ElapsedMilliseconds} ms");
        });

        // Anything that changes something must be sent as JSON. A page of some other site, open in a browser on this
        // machine, can make a browser post a form to this address - but not JSON, not without asking first, and the
        // hub never answers that question with a yes.
        app.Use(async (ctx, next) =>
        {
            string method = ctx.Request.Method;
            if ((method == "POST" || method == "PUT") && ctx.Request.ContentLength is > 0 && !(ctx.Request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            { await Refuse(ctx, 415, "json-only"); return; }
            if (method == "OPTIONS") { ctx.Response.StatusCode = 405; return; }
            await next();
        });

        // (hubId: which hub this is - a client keeps it beside its account, and so knows when another hub answers at the same address)
        app.MapGet("/api/hello", () => Json(new { name = "Bambi Market Hub", api = 1, official = HubRules.Official, hubId = hub.HubId, revision = hub.Revision, users = hub.Users, listings = hub.Listed }));

        // (each of these is written with a `return`: an async lambda that takes only the HttpContext and merely *is* an
        // expression would be taken for a plain request handler, and what it answers would be thrown away)
        app.MapPost("/api/register", async (HttpContext ctx) =>
        {
            var body = await Body<HubNickname>(ctx, 4096);
            return Answer(hub.Register(body?.nickname));
        });
        app.MapGet("/api/me", (HttpContext ctx) => Answer(hub.Me(Bearer(ctx))));
        app.MapPut("/api/me", async (HttpContext ctx) =>
        {
            var body = await Body<HubNickname>(ctx, 4096);
            return Answer(hub.Rename(Bearer(ctx), body?.nickname));
        });
        app.MapGet("/api/users/{nickname}", (string nickname) => Answer(hub.Profile(nickname)));

        app.MapGet("/api/listings", (HttpContext ctx) =>
        {
            var q = ctx.Request.Query;
            // a client that has the newest list already is told so in a few bytes (the host asks every few seconds while a shop is open)
            if (long.TryParse(q["since"], out long since) && since == hub.Revision) return Results.StatusCode(304);
            return Json(hub.Listings(q["kind"].ToString(), q["q"].ToString(), q["sort"].ToString(), q["seller"].ToString(), q["detail"] == "1", int.TryParse(q["limit"], out int limit) ? limit : 300));
        });
        app.MapGet("/api/listings/{id}", (string id) => Answer(hub.Listing(id)));
        app.MapPost("/api/listings", async (HttpContext ctx) =>
        {
            // (the account is looked at before the body is read: nobody without one makes the hub take in 200 MB)
            if (!hub.Me(Bearer(ctx)).Ok) return Answer(HubAnswer<HubListingInfo>.No(401, "unauthorized"));
            var p = await Body<HubPublish>(ctx, 200 * 1024 * 1024);
            return Answer(hub.Publish(Bearer(ctx), p));
        });
        app.MapPut("/api/listings/{id}", async (HttpContext ctx, string id) => Answer(hub.Edit(Bearer(ctx), id, await Body<HubEdit>(ctx, 8192))));
        app.MapDelete("/api/listings/{id}", (HttpContext ctx, string id) => Answer(hub.Unlist(Bearer(ctx), id)));
        // (the body is optional: {price} = what the buyer saw it cost; a client from before sends none and buys at whatever it costs now)
        app.MapPost("/api/listings/{id}/buy", async (HttpContext ctx, string id) =>
        {
            var body = ctx.Request.ContentLength is > 0 ? await Body<HubBuy>(ctx, 4096) : null;
            return Answer(hub.Buy(Bearer(ctx), id, body?.price));
        });
        app.MapPost("/api/listings/{id}/like", (HttpContext ctx, string id) => Answer(hub.Like(Bearer(ctx), id)));
        app.MapGet("/api/listings/{id}/package", (HttpContext ctx, string id) => Raw(hub.Package(Bearer(ctx), id)));
        app.MapGet("/api/listings/{id}/song", (string id) => Raw(hub.Song(id)));

        app.MapGet("/files/{name}", (HttpContext ctx, string name) =>
        {
            var a = hub.FileOf(Bearer(ctx), name);
            if (!a.Ok) return Answer(a);
            // a file never changes under its name (the name carries what it hashes to): a client may keep it for good
            ctx.Response.Headers.CacheControl = a.value.contentType == "image/png" ? "public, max-age=31536000, immutable" : "private, max-age=31536000, immutable";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return a.value.path != null ? Results.File(a.value.path, a.value.contentType) : Results.Bytes(a.value.data, a.value.contentType);
        });

        app.Lifetime.ApplicationStarted.Register(() => Line($"hub: listening on port {port} ({(lan ? "this machine and the network" : "this machine only; --lan opens it to the network")})"));
        try { app.Run(); }
        catch (IOException e) { Console.Error.WriteLine($"the hub could not listen on port {port}: {e.Message}"); return 4; }
        return 0;
    }

    /// <summary>Ends this process when the one that started it is gone.</summary>
    static void WatchParent(int pid)
    {
        new Thread(() =>
        {
            try { using var p = Process.GetProcessById(pid); p.WaitForExit(); }
            catch { /* it is gone already */ }
            Line($"hub: the process that started this hub ({pid}) has ended; stopping");
            Environment.Exit(0);
        }) { IsBackground = true, Name = "parent watch" }.Start();
    }

    /// <summary>The query of a request for the log: short, and on one line.</summary>
    static string Safe(string query)
    {
        if (string.IsNullOrEmpty(query)) return "";
        query = new string(query.Where(ch => !char.IsControl(ch)).ToArray());
        return query.Length > 120 ? query[..120] + "..." : query;
    }

    static string Bearer(HttpContext ctx)
    {
        string h = ctx.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h[7..].Trim() : "";
    }

    /// <summary>The body of a request as one of the records of HubContract.cs; null when there is none, it is too long or it is not that.</summary>
    static async Task<T> Body<T>(HttpContext ctx, long max) where T : class
    {
        var limit = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = max;
        if (ctx.Request.ContentLength > max) throw new BadHttpRequestException("too large", 413);
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        string json = await reader.ReadToEndAsync();
        return Proto.FromJson<T>(json);
    }

    static IResult Json(object o) => Results.Text(Proto.ToJson(o), "application/json; charset=utf-8");

    static async Task Refuse(HttpContext ctx, int status, string error)
    {
        ctx.Response.StatusCode = status; ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(Proto.ToJson(new HubError { error = error }));
    }

    static IResult Answer<T>(HubAnswer<T> a) => a.Ok
        ? Json(a.value)
        : Results.Text(Proto.ToJson(new HubError { error = a.error, missing = a.missing, detail = a.detail }), "application/json; charset=utf-8", statusCode: a.status);

    /// <summary>An answer that is JSON text already (a module, a tune).</summary>
    static IResult Raw(HubAnswer<string> a) => a.Ok ? Results.Text(a.value, "application/json; charset=utf-8") : Answer(a);
}
