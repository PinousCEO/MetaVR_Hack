using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using LivingPartner.Mac;
using LivingPartner.Shared;

string Option(string key, string fallback)
{
    var i = Array.IndexOf(args, key);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
var port = int.Parse(Option("--port", Proto.DefaultPort.ToString()));
if (port is < 1 or > 65535) throw new ArgumentException("Invalid port.");
var discoveryPort = int.Parse(Option("--discovery-port", Proto.BeaconPort.ToString()));
if (discoveryPort is < 1 or > 65535) throw new ArgumentException("Invalid discovery port.");
var destination = Option("--quest-ip", "");
IPAddress direct = null;
if (destination != "" && (!IPAddress.TryParse(destination, out direct) || direct.AddressFamily != AddressFamily.InterNetwork))
    throw new ArgumentException("Enter the headset's IPv4 address.");
var data = Option("--state-dir", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LivingPartner", "Mac"));
var pairing = new Pairing(data);
var stage = new Stage(pairing, data);
var root = AppContext.BaseDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>(), ContentRootPath = root, WebRootPath = Path.Combine(root, "wwwroot") });
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(o => { o.Listen(IPAddress.Any, port); o.Limits.MaxRequestBodySize = 8192; });
var app = builder.Build();
var localOrigin = $"http://127.0.0.1:{port}";
app.Use(async (ctx, next) =>
{
    // Quest networking shares the listener. Browser controls and the pairing code only belong to a local tab.
    if (ctx.Request.Path == "/ws") { await next(); return; }
    bool file = ctx.Request.Method == "GET" && (ctx.Request.Path.StartsWithSegments("/docs") || ctx.Request.Path.StartsWithSegments("/world"));
    if (file && ctx.Connection.RemoteIpAddress is {} client && !IPAddress.IsLoopback(client))
    {
        lock (stage.Sync) { if (!stage.MayFetch(client)) { ctx.Response.StatusCode = 403; return; } }
        await next(); return;
    }
    if (!(ctx.Connection.RemoteIpAddress is {} remote && IPAddress.IsLoopback(remote)) ||
        !string.Equals(ctx.Request.Host.Value, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase) ||
        (ctx.Request.Method != "GET" && ctx.Request.Headers.Origin.ToString() != localOrigin))
    { ctx.Response.StatusCode = 403; return; }
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self'; connect-src 'self'; frame-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});
LivingPartner.Host.GomokuWeb.Map(app,stage.Table,Path.Combine(root,"wwwroot","table"));
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseWebSockets();
app.Map("/ws", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest || ctx.Request.Headers.Origin.Count > 0) { ctx.Response.StatusCode = 400; return; }
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await new Peer(socket, ctx.Connection.RemoteIpAddress).Run(stage, ctx.RequestAborted);
});
app.MapGet("/state", () => { lock (stage.Sync) return Results.Json(stage.View()); });
LivingPartner.Demon.SoloDemonWeb.Map(app, Path.Combine(root, "wwwroot", "art"));
app.MapGet("/health", () => Results.Text("Bambi Mac Companion"));
app.MapGet("/game-sfx/{name}", GameAudio.Get);
app.MapPost("/swarm/visible", () => { lock (stage.Sync) stage.SwarmVisible(); return Results.NoContent(); });
app.MapGet("/displays", Displays.Active);
app.MapGet("/docs/{id}/{name}", (HttpContext ctx,string id,string name) =>
{ lock(stage.Sync) return stage.PreviewFor(ctx.Connection.RemoteIpAddress,id,name); });
app.MapGet("/world/{name}", (HttpContext ctx,string name) =>
{ lock(stage.Sync) return stage.WorldFile(ctx.Connection.RemoteIpAddress,name); });
app.MapGet("/work-file/{id}/{name}", (string id,string name) =>
{ lock(stage.Sync) return stage.WorkFile(id,name); });
app.MapPost("/work/{cmd}/{id}", (string cmd,string id) =>
{
    lock(stage.Sync)
    {
        string message=stage.WorkButton(cmd,id);
        return message.Length==0 ? Results.NoContent() : Results.BadRequest(message);
    }
});
app.MapPost("/utter", (SpeechInput input) =>
{
    if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 200) return Results.BadRequest();
    lock (stage.Sync) stage.Dispatch(input.Text);
    return Results.NoContent();
});
app.MapPost("/display", (ScreenInput input) =>
{
    lock (stage.Sync)
    {
        try { stage.SetScreen(input); return Results.NoContent(); }
        catch (ArgumentException e) { return Results.BadRequest(e.Message); }
    }
});
app.MapPost("/action/{cmd}", (string cmd) =>
{
    lock (stage.Sync) { var error = stage.Command(cmd); return error.Length == 0 ? Results.NoContent() : Results.BadRequest(error); }
});
var stopping = app.Lifetime.ApplicationStopping;
await app.StartAsync();
Console.WriteLine($"Bambi Mac Companion  {localOrigin}");
if (OperatingSystem.IsMacOS() && !args.Contains("--no-browser"))
    Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { localOrigin }, UseShellExecute = false });
var beacons = args.Contains("--no-beacon") ? Task.CompletedTask : Advertise();
try
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(210));
    while (await timer.WaitForNextTickAsync(stopping)) lock (stage.Sync) stage.Tick();
}
catch (OperationCanceledException) { }
await beacons;
await app.StopAsync();

async Task Advertise()
{
    byte[] bytes = Encoding.UTF8.GetBytes($"{Proto.BeaconMagic} {port} {Environment.MachineName}");
    try
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        while (!stopping.IsCancellationRequested)
        {
            var destinations = new HashSet<IPAddress> { direct ?? IPAddress.Broadcast };
            if (direct == null)
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                foreach (var host in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.IPv4Mask != null))
                {
                    var address = host.Address.GetAddressBytes(); var mask = host.IPv4Mask.GetAddressBytes();
                    for (int i = 0; i < address.Length; i++) address[i] |= (byte)~mask[i];
                    destinations.Add(new IPAddress(address));
                }
            foreach (var address in destinations)
                try { await udp.SendAsync(bytes, new IPEndPoint(address, discoveryPort), stopping); } catch (SocketException) { }
            await Task.Delay(1100, stopping);
        }
    }
    catch (OperationCanceledException) { }
}
