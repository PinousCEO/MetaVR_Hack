using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using LivingPartner.Demon;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace LivingPartner.Host;

/// <summary>Serves only the solo game for one desktop launch. Each open tab keeps it alive even on the title screen.</summary>
static class SoloDemonDesktop
{
    public static async Task<int> Run(bool openBrowser)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { Args=Array.Empty<string>(), ContentRootPath=AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0));
        await using var app=builder.Build();
        var tabs=new TabLifetime();
        app.UseWebSockets();
        SoloDemonWeb.Map(app,Path.Combine(AppContext.BaseDirectory,"web","demon-art"),standalone:true);
        app.MapGet("/demon-gate/presence",async (HttpContext c)=>{
            if(!SoloDemonWeb.OwnSocket(c)){c.Response.StatusCode=403;return;}
            if(!c.WebSockets.IsWebSocketRequest){c.Response.StatusCode=400;return;}
            using var ws=await c.WebSockets.AcceptWebSocketAsync();
            tabs.Enter();
            try{
                var buffer=new byte[16];
                while(ws.State==WebSocketState.Open){
                    var result=await ws.ReceiveAsync(new ArraySegment<byte>(buffer),c.RequestAborted);
                    if(result.MessageType==WebSocketMessageType.Close){
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"",c.RequestAborted);break;
                    }
                }
            }
            catch(OperationCanceledException){}
            catch(WebSocketException){}
            finally{tabs.Leave();}
        });
        await app.StartAsync();
        string url=app.Urls.Single().TrimEnd('/')+"/demon-gate/";
        Console.WriteLine(url);
        try
        {
            if(openBrowser) Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
            using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while(!tabs.Finished && await timer.WaitForNextTickAsync(app.Lifetime.ApplicationStopping)){}
        }
        catch(OperationCanceledException){}
        finally{await app.StopAsync();}
        return 0;
    }

    sealed class TabLifetime
    {
        readonly object sync=new();
        readonly Stopwatch idle=Stopwatch.StartNew();
        int count;
        bool seen;
        public void Enter(){lock(sync){seen=true;count++;}}
        public void Leave(){lock(sync){if(--count==0)idle.Restart();}}
        // Most browsers reconnect immediately on refresh. A launch that never opens a tab also cleans itself up.
        public bool Finished{get{lock(sync)return count==0 && idle.Elapsed.TotalSeconds>(seen?5:90);}}
    }
}

