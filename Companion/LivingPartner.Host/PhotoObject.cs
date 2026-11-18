using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>A single captured image produces one GLB shared by the room, bag and file export.</summary>
internal static class PhotoObject
{
    public static async Task<RoomObjects.Target> LocateAsync(OpenAiClient vision, byte[] pixels, string words, EyeFrame frame, CancellationToken ct)
    {
        float x = frame?.aimX ?? .5f, y = frame?.aimY ?? .5f;
        try { return RoomObjects.Locate(pixels, words, x, y, ct); }
        catch (RoomObjects.Unclear) when (vision != null && RoomObjects.RequestedClass(words) == null) { }

        // Open-vocabulary fallback for objects outside the local detector's classes.
        var fields = new JObject();
        foreach (var name in new[] { "found", "name", "left", "top", "right", "bottom" })
            fields[name] = new JObject { ["type"] = name == "found" ? "boolean" : name == "name" ? "string" : "integer" };
        var schema = new JObject { ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = fields, ["required"] = new JArray(fields.Properties().Select(p => p.Name)) };
        string prompt = "Identify the specific physical object the user means in this headset view. It can be on furniture or in a hand. " +
            "Use the requested noun, then the projected target to disambiguate. Exclude people, hands and surrounding furniture. " +
            "If the object is absent, ambiguous or hidden, set found=false. Otherwise give its short English name and tight box " +
            "(left,top,right,bottom) in integers from 0 to 1000, origin top left.";
        var mime = pixels[0] == 0x89 ? "image/png" : "image/jpeg";
        var result = await vision.AskJsonAsync(prompt, FormattableString.Invariant($"User: {words}. Projected target: ({x*1000:0}, {y*1000:0})."),
            new[] { (mime, pixels, "room" + (mime == "image/png" ? ".png" : ".jpg")) }, schema, 400, ct);
        var box = new SkiaSharp.SKRect((float?)result["left"] / 1000 ?? 0, (float?)result["top"] / 1000 ?? 0,
            (float?)result["right"] / 1000 ?? 0, (float?)result["bottom"] / 1000 ?? 0);
        string label = WorldRules.Text(result["name"], 35);
        if ((bool?)result["found"] != true || label.Length == 0 || box.Left < 0 || box.Top < 0 || box.Right > 1 ||
            box.Bottom > 1 || box.Width < .02f || box.Height < .02f)
            throw new RoomObjects.Unclear("Point at the object you mean and ask me again.");
        return RoomObjects.CropRegion(pixels, label, box);
    }

    public static async Task<(ItemDef item, byte[] glb)> CopyAsync(TripoClient tripo, string id,
        RoomObjects.Target target, string words, Action<float, string> progress, CancellationToken ct)
    {
        var kind = ItemKinds.Guess(target.Label);
        var idea = WorldRules.Item(new JObject { ["name"] = SightRules.Title(target.Label) }, kind);
        progress?.Invoke(.20f, "Sending the photo to Tripo");
        byte[] glb = await tripo.ImageToModelAsync(target.Crop, (p, step) => progress?.Invoke(.20f + .72f * p, step), ct);
        ct.ThrowIfCancellationRequested();
        var item = new ItemDef { id = id, name = idea.name, asked = words, kind = idea.kind, uses = idea.uses,
            sizeM = idea.sizeM, color = idea.color, note = idea.note, model = id + ".glb", status = "ready",
            script = idea.kind == "cup" ? RoomScriptExamples.Cup : "",
            progress = 1, inRoom = false, madeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        return (item, glb);
    }
}
