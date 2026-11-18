using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace LivingPartner.Host;

public static class GomokuWeb
{
    public static void Map(WebApplication app,GomokuTable table,string root)
    {
        app.UseWhen(ctx=>ctx.Request.Path.StartsWithSegments("/table"),branch=>branch.Use(async(ctx,next)=>{
            var ip=ctx.Connection.RemoteIpAddress;
            if(ip==null || !IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6?ip.MapToIPv4():ip) ||
               ctx.Request.Host.Host is not ("127.0.0.1" or "localhost" or "[::1]") ||
               ctx.Request.Method=="POST" && ctx.Request.Headers.Origin.ToString()!=$"{ctx.Request.Scheme}://{ctx.Request.Host}")
            {ctx.Response.StatusCode=403;return;}
            ctx.Response.Headers.CacheControl="no-store";
            ctx.Response.Headers.XContentTypeOptions="nosniff";
            ctx.Response.Headers.ContentSecurityPolicy="default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; worker-src 'self' blob:; frame-ancestors 'self'; base-uri 'none'";
            await next();
        }));
        var types=new FileExtensionContentTypeProvider();types.Mappings[".task"]="application/octet-stream";types.Mappings[".mjs"]="text/javascript";types.Mappings[".wasm"]="application/wasm";
        var provider=new PhysicalFileProvider(Path.GetFullPath(root));
        app.UseStaticFiles(new StaticFileOptions{FileProvider=provider, RequestPath="/table",ContentTypeProvider=types});
        app.MapGet("/table/",()=>Results.File(Path.Combine(root,"board.html"),"text/html; charset=utf-8"));
        app.MapGet("/table/api/state",()=>Results.Json(table.View()));
        app.MapPost("/table/api/layout",(TableLayout p)=>table.Register(p)?Results.NoContent():Results.Conflict());
        app.MapPost("/table/api/{action}",(string action,TableClick p)=>{
            if(action=="guest")return table.Guest(p)?Results.NoContent():Results.Conflict();
            string error=table.Button(p,action);return error.Length==0?Results.NoContent():Results.BadRequest(error);
        });
        app.MapGet("/table/api/frame",(string viewer,long after)=>table.Latest(viewer,after) is string json?Results.Text(json,"application/json"):Results.NoContent());
    }
}
