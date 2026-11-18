using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using System.Text.RegularExpressions;

namespace LivingPartner.Host;

internal static class RoomObjects
{
    const int Side = 640;
    static readonly string[] Labels = ("person|bicycle|car|motorcycle|airplane|bus|train|truck|boat|traffic light|fire hydrant|stop sign|parking meter|" +
        "bench|bird|cat|dog|horse|sheep|cow|elephant|bear|zebra|giraffe|backpack|umbrella|handbag|tie|suitcase|frisbee|skis|snowboard|sports ball|" +
        "kite|baseball bat|baseball glove|skateboard|surfboard|tennis racket|bottle|wine glass|cup|fork|knife|spoon|bowl|banana|apple|sandwich|orange|" +
        "broccoli|carrot|hot dog|pizza|donut|cake|chair|couch|potted plant|bed|dining table|toilet|tv|laptop|mouse|remote|keyboard|cell phone|" +
        "microwave|oven|toaster|sink|refrigerator|book|clock|vase|scissors|teddy bear|hair drier|toothbrush").Split('|');
    static readonly Lazy<InferenceSession> Network = new(() =>
    {
        using var options = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        return new InferenceSession(Path.Combine(AppContext.BaseDirectory, "Models", "room-yolox.onnx"), options);
    });

    internal sealed record Detection(string Label, SKRect Box, float Score);
    internal sealed record Target(string Label, byte[] Crop, SKRect Bounds, float Score);
    internal sealed class Unclear(string message) : Exception(message);

    internal static string RequestedClass(string words)
    {
        if (Regex.IsMatch(words, @"\b(?:controllers?|game\s?pads?|joy-?cons?|dualsense|dualshock)\b|컨트롤러|콘트롤러|게임\s*패드", RegexOptions.IgnoreCase)) return "controller";
        if (Regex.IsMatch(words, @"\b(?:mug|coffee(?: mug| cup)?|teacup)s?\b|컵|머그", RegexOptions.IgnoreCase)) return "cup";
        // The object can be sitting on a table or by a laptop. Read the object before its location.
        string subject = Regex.Split(words.ToLowerInvariant(), @"\s+(?:on|near|beside|next to|in front of|behind)\s+")[0];
        return Labels.FirstOrDefault(label => Regex.IsMatch(subject, @"\b" + label + @"s?\b"));
    }

    public static Target Locate(byte[] frame, string words, float aimX, float aimY, CancellationToken ct)
    {
        using var bytes = SKData.CreateCopy(frame);
        using var codec = SKCodec.Create(bytes);
        if (codec == null || codec.Info.Width is < 1 or > 4096 || codec.Info.Height is < 1 or > 4096 ||
            (long)codec.Info.Width * codec.Info.Height > 9_000_000) throw new InvalidDataException("Invalid room camera image.");
        using var photo = SKBitmap.Decode(codec);
        if (photo == null) throw new InvalidDataException("Unable to decode the room camera image.");
        ct.ThrowIfCancellationRequested();
        float scale = Side / (float)Math.Max(photo.Width, photo.Height);
        int width = Math.Max(1, (int)(photo.Width * scale)), height = Math.Max(1, (int)(photo.Height * scale));
        using var small = photo.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        var input = new float[3 * Side * Side];
        Array.Fill(input, 114f);
        var colors = small.Pixels;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var color = colors[y * width + x];
            int i = y * Side + x;
            input[i] = color.Red; input[i + Side * Side] = color.Green; input[i + 2 * Side * Side] = color.Blue;
        }
        var network = Network.Value;
        using var outputs = network.Run(new[] { NamedOnnxValue.CreateFromTensor(network.InputMetadata.Keys.Single(),
            new DenseTensor<float>(input, new[] { 1, 3, Side, Side })) });
        ct.ThrowIfCancellationRequested();
        var detections = Decode(outputs.Single().AsTensor<float>(), width, height);
        var selected = Select(detections, RequestedClass(words), aimX, aimY);
        return Cut(photo, selected);
    }

    internal static Target CropRegion(byte[] bytes, string label, SKRect box)
    {
        using var photo = SKBitmap.Decode(bytes);
        return Cut(photo, new Detection(label, box, 0));
    }

    // The detector has no game-controller class. The vision model traces the visible shell,
    // including holes and the gaps between grips. Only the masked PNG leaves for Tripo.
    internal static Target ControllerCutout(byte[] bytes, SKRect box, List<SKPoint[]> rings)
    {
        const string retry = "Hold one controller clear of your hand and the screen, then try again.";
        if (rings.Count == 0 || rings[0].Length < 6) throw new Unclear(retry);
        CheckBounds(box);
        using var photo = SKBitmap.Decode(bytes);
        if (photo == null) throw new Unclear(retry);
        int left = (int)(box.Left * photo.Width), top = (int)(box.Top * photo.Height);
        int w = (int)Math.Ceiling(box.Right * photo.Width) - left, h = (int)Math.Ceiling(box.Bottom * photo.Height) - top;
        if (Math.Min(w,h) < 18) throw new Unclear(retry);
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var ring in rings)
        {
            if (ring.Length is < 3 or > 128) throw new Unclear(retry);
            foreach (var p in ring)
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < box.Left-.005f || p.X > box.Right+.005f || p.Y < box.Top-.005f || p.Y > box.Bottom+.005f) throw new Unclear(retry);
            path.AddPoly(ring.Select(p => new SKPoint(p.X * photo.Width - left, p.Y * photo.Height - top)).ToArray(), true);
        }
        // Reject a rectangle drawn around the controller and large areas of unrecognised background.
        using var isolated = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(isolated))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            canvas.DrawBitmap(photo, -left, -top);
        }
        int opaque = isolated.Pixels.Count(p => p.Alpha > 128);
        if (opaque < w*h*.12 || opaque > w*h*.95) throw new Unclear(retry);
        using var image = SKImage.FromBitmap(isolated);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new Target("controller", png.ToArray(), box, 0);
    }

    static void CheckBounds(SKRect box)
    {
        if (!float.IsFinite(box.Width) || !float.IsFinite(box.Height) || box.Left < 0 || box.Top < 0 ||
            box.Right > 1 || box.Bottom > 1 || box.Width < .02f || box.Height < .02f || box.Width * box.Height > .68f)
            throw new Unclear("I need a clear outline. Leave some space around the object and try again.");
    }

    static Target Cut(SKBitmap photo, Detection selected)
    {
        SKRect box = selected.Box;
        CheckBounds(box);
        // Leave a narrow margin around handles and rims, clamped to the real frame.
        var crop = new SKRectI(Math.Max(0, (int)((box.Left - box.Width * .04f) * photo.Width)),
            Math.Max(0, (int)((box.Top - box.Height * .04f) * photo.Height)),
            Math.Min(photo.Width, (int)Math.Ceiling((box.Right + box.Width * .04f) * photo.Width)),
            Math.Min(photo.Height, (int)Math.Ceiling((box.Bottom + box.Height * .04f) * photo.Height)));
        using var region = new SKBitmap();
        if (!photo.ExtractSubset(region, crop)) throw new InvalidDataException("Unable to isolate the object.");
        using var image = SKImage.FromBitmap(region);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 94);
        return new Target(selected.Label, jpeg.ToArray(), box, selected.Score);
    }

    static IEnumerable<Detection> Decode(Tensor<float> output, int width, int height)
    {
        var boxes = new List<Detection>();
        int row = 0;
        foreach (int stride in new[] { 8, 16, 32 })
        for (int y = 0; y < Side / stride; y++)
        for (int x = 0; x < Side / stride; x++, row++)
        {
            float objectness = output[0, row, 4];
            if (objectness < .35f) continue;
            int label = 0;
            for (int c = 1; c < Labels.Length; c++)
                if (output[0, row, 5 + c] > output[0, row, 5 + label]) label = c;
            float score = objectness * output[0, row, label + 5];
            if (score < .35f) continue;
            float cx = (output[0, row, 0] + x) * stride, cy = (output[0, row, 1] + y) * stride;
            float hw = MathF.Exp(output[0, row, 2]) * stride * .5f, hh = MathF.Exp(output[0, row, 3]) * stride * .5f;
            var box = SKRect.Intersect(new SKRect((cx-hw)/width, (cy-hh)/height, (cx+hw)/width, (cy+hh)/height), new SKRect(0,0,1,1));
            if (box.Width * width >= 8 && box.Height * height >= 8) boxes.Add(new Detection(Labels[label], box, score));
        }
        var kept = new List<Detection>();
        foreach (var candidate in boxes.OrderByDescending(d => d.Score))
        {
            if (kept.Any(d => d.Label == candidate.Label && Overlap(d.Box, candidate.Box) > .5f)) continue;
            kept.Add(candidate);
        }
        return kept;
    }

    static float Overlap(SKRect a, SKRect b)
    {
        var intersection = SKRect.Intersect(a,b);
        float area = intersection.Width * intersection.Height;
        return area / (a.Width * a.Height + b.Width * b.Height - area);
    }

    internal static Detection Select(IEnumerable<Detection> boxes, string requested, float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) { x = .5f; y = .5f; }
        var filtered = boxes.Where(d => requested == null ? d.Label is not ("person" or "dining table" or "tv" or "bed" or "couch") : d.Label == requested).ToList();
        if (requested != null && filtered.Count == 1) return filtered[0];
        var underRay = filtered.Where(d => d.Box.Contains(x,y)).OrderBy(d => d.Box.Width * d.Box.Height).ToList();
        if (underRay.Count > 0) return underRay[0];
        var nearest = filtered.Select(d => (d, gap: MathF.Sqrt(MathF.Pow(x - Math.Clamp(x,d.Box.Left,d.Box.Right),2) +
            MathF.Pow(y - Math.Clamp(y,d.Box.Top,d.Box.Bottom),2)))).OrderBy(p => p.gap).ToList();
        if (nearest.Count > 0 && nearest[0].gap < .12f && (nearest.Count == 1 || nearest[1].gap - nearest[0].gap > .035f)) return nearest[0].d;
        throw new Unclear(filtered.Count == 0 && requested != null ? $"I can’t see a {requested}. Look at it and try again." :
            "Point at the object you mean and ask me again.");
    }
}
