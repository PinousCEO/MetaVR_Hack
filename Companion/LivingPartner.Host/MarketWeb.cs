using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using LivingPartner.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LivingPartner.Host;

/// <summary>
/// The shop on the PC: one self-contained page (web\market.html next to the exe, with its font and its sticker art),
/// and the small JSON API it talks to. Everything here is for this PC only - a browser on the same machine - and it
/// asks for nothing from anywhere else.
///   GET  /market                    the page
///   GET  /market/art/{file}.png     the baked sticker art (pictures by kind, for modules that have none of their own)
///   GET  /market/font/{file}.ttf    the heading font
///   GET  /market/api/state          the user's account on the hub, what is on sale, what is theirs, what is being made
///   POST /market/api/command        a MarketCommand (buy, install, remove, publish, setPrice, unpublish, like, nickname, make ...)
///   POST /market/api/say            {text}: something typed to the partner in the page's box - it enters the conversation as a sentence said to it by name
///   POST /market/api/import         the bytes of a .bambi file: it becomes a thing of the user's own
///   GET  /market/export/{id}.bambi  one of the user's own things as a file to hand to somebody
///   GET  /market/song/{id}.wav      the tune of a module, rendered by the same synth the headset plays it with
/// Module icons are the world's files and come from /world/{file}. The page never talks to the hub itself: the host
/// does, in the user's name (their token never reaches a browser).
/// </summary>
public static class MarketWeb
{
    static string Root => Path.Combine(AppContext.BaseDirectory, "web");
    static readonly Regex ArtName = new(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,40}\.png\z");
    static readonly ConcurrentDictionary<string, byte[]> Tunes = new();      // rendered once per tune (a tune never changes under its id and its notes)

    /// <summary>
    /// From this machine, and meant for this host by name. The second half matters as much as the first: a page of
    /// some other site, open in the user's browser, can make requests to 127.0.0.1 - but not under one of these names
    /// unless it was typed (a name that merely resolves to this machine is turned away).
    /// </summary>
    static bool Local(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip == null || !IPAddress.IsLoopback(ip)) return false;
        return NamedLocally(ctx);
    }

    /// <summary>
    /// The request names this host as this machine (127.0.0.1 / localhost), as every local client does. The host's own
    /// local addresses (/status, /world, /screen) ask the same of a caller on this machine: a web page whose name was
    /// made to resolve to 127.0.0.1 ("DNS rebinding") arrives as a local connection, but under its own name.
    /// </summary>
    internal static bool NamedLocally(HttpContext ctx)
    {
        string host = ctx.Request.Host.Host;
        return host is "127.0.0.1" or "localhost" or "[::1]" or "::1";
    }

    /// <summary>A command must come from the shop's own page (or from a tool that is not a browser at all): never from another site's script.</summary>
    static bool OwnPage(HttpContext ctx, string contentType = "application/json")
    {
        string origin = ctx.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !(Uri.TryCreate(origin, UriKind.Absolute, out var o) && o.Host is "127.0.0.1" or "localhost" or "[::1]" && o.Port == ctx.Request.Host.Port)) return false;
        string site = ctx.Request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0 && site != "same-origin" && site != "none") return false;
        // JSON only (or, for a file, plain bytes): a plain HTML form on another site can send neither
        return (ctx.Request.ContentType ?? "").StartsWith(contentType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The body of POST /market/api/say.</summary>
    sealed class SayText { public string text = ""; }

    /// <summary>Runs something on the core thread (where all state lives) and hands its result to the request.</summary>
    static Task<T> OnCore<T>(Core core, Func<T> f)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        core.Post(() => { try { done.SetResult(f()); } catch (Exception e) { done.SetException(e); } });
        return done.Task;
    }

    public static void Map(WebApplication app, Core core)
    {
        app.MapGet("/market", (HttpContext ctx) =>
        {
            if (!Local(ctx)) return Results.StatusCode(403);
            string page = Path.Combine(Root, "market.html");
            if (!File.Exists(page)) return Results.Text("The marketplace page (web\\market.html) is missing next to the host's exe.", "text/plain", statusCode: 500);
            // read each time: it is small, and the page can then be worked on without restarting the host
            ctx.Response.Headers.CacheControl = "no-cache";
            return Results.Text(File.ReadAllText(page), "text/html; charset=utf-8");
        });

        app.MapGet("/market/art/{file}", (HttpContext ctx, string file) =>
        {
            if (!Local(ctx)) return Results.StatusCode(403);
            if (!ArtName.IsMatch(file)) return Results.NotFound();
            string path = Path.Combine(Root, "art", file);
            if (!File.Exists(path)) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(path, "image/png");
        });

        app.MapGet("/market/font/{file}", (HttpContext ctx, string file) =>
        {
            if (!Local(ctx)) return Results.StatusCode(403);
            if (file != "Jua-Regular.ttf") return Results.NotFound();
            string path = Path.Combine(Root, file);
            if (!File.Exists(path)) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(path, "font/ttf");
        });

        app.MapGet("/market/api/state", async (HttpContext ctx) =>
        {
            if (!Local(ctx)) return Results.StatusCode(403);
            // (turned into text on the core thread as well: the lists it is made from are the live ones)
            // (and the shop knows it is being looked at: while the page keeps asking, the hub is asked often)
            string json = await OnCore(core, () => { core.Shop.PagePolled(); return Proto.ToJson(core.Shop.WebState()); });
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Text(json, "application/json; charset=utf-8");
        });

        app.MapPost("/market/api/command", async (HttpContext ctx) =>
        {
            if (!Local(ctx) || !OwnPage(ctx)) return Results.StatusCode(403);
            string body;
            using (var reader = new StreamReader(ctx.Request.Body)) body = await reader.ReadToEndAsync();
            var cmd = body.Length > 8000 ? null : Proto.FromJson<MarketCommand>(body);
            if (cmd == null || string.IsNullOrEmpty(cmd.cmd)) return Results.BadRequest();
            // the page is the PC's: "open" from it never opens anything (it is the shop already), and it brings no display into the room
            if (cmd.cmd is "open" or "screen") return Results.BadRequest();
            core.Post(() => core.Shop.OnCommand(null, cmd));
            return Results.Text("{\"ok\":true}", "application/json");
        });

        // The box on the page where the user talks to the partner (this is where things are MADE: by asking for them).
        // The text goes into the conversation exactly as a typed, addressed sentence does (Core.Typed) - the same
        // way `lpctl say` goes. From this machine and from the shop's own page only, like every command.
        app.MapPost("/market/api/say", async (HttpContext ctx) =>
        {
            if (!Local(ctx) || !OwnPage(ctx)) return Results.StatusCode(403);
            string body;
            using (var reader = new StreamReader(ctx.Request.Body)) body = await reader.ReadToEndAsync();
            string text = body.Length > 8000 ? "" : WorldRules.TypedText(Proto.FromJson<SayText>(body)?.text);
            if (text.Length == 0) return Results.BadRequest();
            core.Post(() => core.Typed(text, "the page"));
            return Results.Text("{\"ok\":true}", "application/json");
        });

        // a .bambi file the user picked or dropped on the page: its bytes, as they are
        app.MapPost("/market/api/import", async (HttpContext ctx) =>
        {
            if (!Local(ctx) || !OwnPage(ctx, "application/octet-stream")) return Results.StatusCode(403);
            const long max = MarketPack.MaxBambiBytes;       // (40 MB: no module file is larger)
            if (ctx.Request.ContentLength > max) return Results.StatusCode(413);
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = await ctx.Request.Body.ReadAsync(buf)) > 0)
            {
                if (ms.Length + n > max) return Results.StatusCode(413);
                ms.Write(buf, 0, n);
            }
            byte[] data = ms.ToArray();
            string label = ctx.Request.Query["name"].ToString();
            // what came of it is said by the partner and shown as the notice, like every other command
            // (unzipped on the thread pool, not on the core thread: ImportBytes sees to that itself)
            core.Shop.ImportBytes(data, label.Length > 0 ? label : "a file from the page");
            return Results.Text("{\"ok\":true}", "application/json");
        });

        app.MapGet("/market/export/{file}", async (HttpContext ctx, string file) =>
        {
            if (!Local(ctx)) return Results.StatusCode(403);
            if (!file.EndsWith(MarketPack.Extension, StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
            string id = file[..^MarketPack.Extension.Length];
            // what goes into the file is decided on the core thread; reading the model and packing it is done here, off it
            var plan = await OnCore(core, () => core.Shop.PlanExport(id, out _, true));
            if (plan == null) return Results.NotFound();
            byte[] data = await Task.Run(() => Market.PackExport(plan));
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.File(data, "application/octet-stream", plan.fileName);
        });

        app.MapGet("/market/song/{file}", async (HttpContext ctx, string file) =>
        {
            if (!Local(ctx)) return Results.StatusCode(403);
            if (!file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
            string id = file[..^4];
            // what this PC holds; otherwise the hub lets a tune be listened to before the thing is bought
            var song = await OnCore(core, () => core.Shop.SongFor(id)) ?? await core.Shop.HubSong(id);
            if (song == null) return Results.NotFound();
            string key = Proto.ToJson(song);
            if (!Tunes.TryGetValue(key, out byte[] wav))
            {
                wav = MarketRules.Wav(song, 1, out _);      // one pass: the page loops it, and a pass is made to loop without a seam
                if (Tunes.Count > 24) Tunes.Clear();
                Tunes[key] = wav;
            }
            ctx.Response.Headers.CacheControl = "no-cache";
            return Results.Bytes(wav, "audio/wav");
        });
    }
}
