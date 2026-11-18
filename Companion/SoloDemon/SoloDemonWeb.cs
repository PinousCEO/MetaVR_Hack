using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LivingPartner.Demon;

public static class SoloDemonWeb
{
    static readonly Regex ArtName = new(@"^(demon-(gate|guard)\.jpg|demon-(felt|wand)\.png|demon-charm-(0[1-9]|1[0-2])\.png|swarm-meshes\.json)\z");
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static bool Local(HttpContext c) => c.Connection.RemoteIpAddress is {} ip && IPAddress.IsLoopback(ip)
        && c.Request.Host.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
    internal static bool OwnSocket(HttpContext c) => Local(c) &&
        string.Equals(c.Request.Headers.Origin.ToString(),$"{c.Request.Scheme}://{c.Request.Host.Value}",StringComparison.OrdinalIgnoreCase);

    static IResult FileAt(string root,string name)
    {
        string path=Path.Combine(root,name);
        if (!File.Exists(path)) return Results.NotFound();
        return Results.File(path,Path.GetExtension(path) switch {
            ".js"=>"text/javascript; charset=utf-8",".css"=>"text/css; charset=utf-8",".html"=>"text/html; charset=utf-8",
            ".png"=>"image/png",".jpg"=>"image/jpeg",".ttf"=>"font/ttf",_=>"application/json"
        });
    }

    public static void Map(WebApplication app, string art, bool standalone = false)
    {
        var root=Path.Combine(AppContext.BaseDirectory,"DemonGate");
        app.MapGet("/demon-gate/", (HttpContext c) => {
            if(!Local(c)) return Results.StatusCode(403);
            // WebKit versions differ on whether connect-src 'self' includes the corresponding ws:// origin.
            string socketScheme=c.Request.IsHttps?"wss":"ws";
            c.Response.Headers.ContentSecurityPolicy=$"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self'; connect-src 'self' {socketScheme}://{c.Request.Host.Value}; frame-ancestors 'none'; base-uri 'none'";
            if(standalone){
                string html=File.ReadAllText(Path.Combine(root,"index.html")).Replace("<html lang=","<html data-standalone lang=");
                return Results.Content(html,"text/html; charset=utf-8");
            }
            return FileAt(root,"index.html");
        });
        app.MapGet("/demon-gate/{file}", (HttpContext c,string file) =>
            !Local(c)?Results.StatusCode(403):file is "game.js" or "mesh.js" or "game.css" or "Jua-Regular.ttf"?FileAt(root,file):Results.NotFound());
        app.MapGet("/demon-gate/art/{file}",(HttpContext c,string file) =>
            !Local(c)?Results.StatusCode(403):ArtName.IsMatch(file)?FileAt(art,file):Results.NotFound());
        app.MapGet("/demon-gate/play", Play);
    }

    static async Task Play(HttpContext c)
    {
        if (!OwnSocket(c))
        { c.Response.StatusCode=403; return; }
        if (!c.WebSockets.IsWebSocketRequest) { c.Response.StatusCode=400; return; }
        using var ws=await c.WebSockets.AcceptWebSocketAsync();
        var bytes=new byte[1024];
        var clock=Stopwatch.StartNew(); double last=clock.Elapsed.TotalSeconds;
        var run=new SoloDemonRun((uint)Random.Shared.Next());
        try
        {
            while (ws.State==WebSocketState.Open)
            {
                int n=0; WebSocketReceiveResult packet;
                do {
                    packet=await ws.ReceiveAsync(new ArraySegment<byte>(bytes,n,bytes.Length-n),c.RequestAborted);
                    if (packet.MessageType==WebSocketMessageType.Close) { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"",c.RequestAborted); return; }
                    if (packet.MessageType!=WebSocketMessageType.Text || n+packet.Count>=bytes.Length) { await ws.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData,"input",c.RequestAborted); return; }
                    n+=packet.Count;
                } while (!packet.EndOfMessage);
                var controls=JsonSerializer.Deserialize<SoloControls>(bytes.AsSpan(0,n),Json);
                if (controls==null || !float.IsFinite(controls.X) || !float.IsFinite(controls.Y) || !float.IsFinite(controls.Aspect)) return;
                double now=clock.Elapsed.TotalSeconds;
                if (controls.Restart) { run=new SoloDemonRun((uint)Random.Shared.Next()); last=now; }
                run.Advance(controls,(float)(now-last)); last=now;
                var encoded=JsonSerializer.SerializeToUtf8Bytes(run.Frame(),Json);
                await ws.SendAsync(new ArraySegment<byte>(encoded),WebSocketMessageType.Text,true,c.RequestAborted);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (JsonException) { if(ws.State==WebSocketState.Open) await ws.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData,"input",CancellationToken.None); }
    }
}

