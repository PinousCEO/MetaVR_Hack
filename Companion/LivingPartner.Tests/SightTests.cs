using LivingPartner.Host;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>
/// "When I SHOW a real object, Nilo makes it and brings it into the PC" (Sight.cs), as far as it is plain rules:
/// the one tessellation of a sculpted thing that the headset draws and the host writes out (Shared/PartMesh), the
/// .glb the host writes from it (GlbWriter: read back by the rules of glTF and by the project's own GlbReader), the
/// picture of it (ModelRender), the cut round the hands (Shared/EyeMath), the plain wordings and the lines
/// (SightRules), and the model's file among the documents (DocsRules.Carried / Reveal).
/// </summary>
public class SightTests
{
    [Fact]
    public void Firewood_logs_and_cut_ends_build_as_one_grounded_mesh()
    {
        var wood = WorldRules.Sculpt(WorldRules.FirewoodShape(), out string error);
        Assert.True(wood != null, error);
        Assert.Empty(wood.loose);
        Assert.Equal(5, wood.parts.Count(p => p.shape == "box"));
        Assert.Contains(wood.parts, p => p.rx == 90);
        Assert.Contains(wood.parts, p => p.rz == 90);
        Assert.True(PartMesh.Build(wood.parts).indices.Length > 0);
        Assert.True(WorldRules.IsFirewood("장작. 장작에서 불이 활활 타올라야 돼."));
        Assert.False(WorldRules.IsFirewood("장작 난로"));
    }

    static ShapePart Part(string shape, float x, float y, float z, float sx, float sy, float sz, string color = "#F4A9A0", float round = 0, float rx = 0, float ry = 0, float rz = 0) =>
        new ShapePart { shape = shape, x = x, y = y, z = z, sx = sx, sy = sy, sz = sz, color = color, round = round, rx = rx, ry = ry, rz = rz };

    /// <summary>A mug: every kind of solid once, some turned, three colours.</summary>
    static List<ShapePart> Mug() => WorldRules.ValidateParts(new List<ShapePart>
    {
        Part("cylinder", 0, 0.45f, 0, 0.7f, 0.9f, 0.7f, "#E8697D", 0.2f),
        Part("cylinder", 0, 0.89f, 0, 0.58f, 0.04f, 0.58f, "#8C6A5D", 0.1f),
        Part("torus", 0.42f, 0.45f, 0, 0.42f, 0.1f, 0.42f, "#E8697D", 0, 90f),
        Part("box", 0, 0.45f, -0.345f, 0.3f, 0.2f, 0.04f, "#FFFFFF", 0.3f),
        Part("sphere", 0, 0.98f, 0, 0.12f, 0.1f, 0.12f, "#FFFFFF"),
        Part("cone", -0.2f, 0.98f, 0.1f, 0.1f, 0.14f, 0.1f, "#FFFFFF", 0.3f, 15f, 0, 20f),
        Part("capsule", 0.1f, 1.0f, 0.1f, 0.06f, 0.2f, 0.06f, "#8C6A5D", 0, 0, 30f, -25f),
    }, out _);

    static byte[] CoffeeOnTable() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "coffee.jpg"));

    [Fact]
    public void Recognizes_a_cup_on_a_table_away_from_the_aim_and_rejects_an_absent_bottle()
    {
        var photo = CoffeeOnTable();
        var cup = RoomObjects.Locate(photo, "Copy the cup on my desk", .95f, .05f, default);
        Assert.Equal("cup", cup.Label);
        Assert.InRange(cup.Score, .35f, 1f);
        Assert.InRange(cup.Bounds.Width * cup.Bounds.Height, .015f, .9f);
        Assert.Throws<RoomObjects.Unclear>(() => RoomObjects.Locate(photo, "Copy this bottle", .5f, .5f, default));
        var two = new[] { new RoomObjects.Detection("cup",new(.05f,.3f,.30f,.75f),.91f),
            new RoomObjects.Detection("cup",new(.67f,.3f,.85f,.75f),.65f) };
        Assert.Same(two[1], RoomObjects.Select(two, "cup", .72f, .4f));
        Assert.Throws<RoomObjects.Unclear>(() => RoomObjects.Select(two, "cup", .48f,.2f));
    }

    sealed class Recorder(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request);
    }

    [Fact]
    public void Filming_commands_use_the_copied_cup_and_return_it_after_a_sip()
    {
        string request = NameRules.Strip("Hey, Nilo, copy this cup and add a sip.");
        Assert.Equal(SightRules.Make, SightRules.Plain(request));
        Assert.Equal("cup", RoomObjects.RequestedClass(request));
        Assert.True(SightRules.WantsSip(NameRules.Strip("Hey, Nilo, take a sip.")));
        Assert.True(SightRules.WantsModelFile("Send the cup to my PC."));
        Assert.False(SightRules.WantsModelFile("Send the PDF to my PC."));
        var cup = new ItemDef { id = "my-cup", kind = "cup", name = "Cup", model = "my-cup.glb",
            status = "ready", sizeM = .08f, x = .12f, z = -.08f };
        var items = new[] { cup, new ItemDef { id = "shop-cup", name = "Cup", kind = "cup", status = "ready" } };
        Assert.Same(cup, SightRules.DrinkingCup(items));
        Assert.Same(cup, SightRules.ExportTarget(items, "Send the cup to my PC."));
        Assert.True(SightRules.RestoreCupScript(cup));
        Assert.False(SightRules.RestoreCupScript(cup));
        var room = new FakeRoom();
        var script = new RoomScript(cup.script, cup, room);
        Assert.True(script.Act("sip"));
        bool lifted = false;
        for (int frame = 0; frame < 600; frame++)
        {
            room.Step(1f / 60, script);
            script.Tick(1f / 60);
            if (script.Pet.motion == "sip" && script.Pet.MotionU is > .4f and < .7f)
                lifted |= room.worn == "handR";
        }
        Assert.True(lifted);
        Assert.Equal(new[] { "handR", "" }, room.wears);
        Assert.Equal(cup.x, script.Me.body.x, 3);
        Assert.Equal(cup.z, script.Me.body.z, 3);
        script.Dispose();
    }

    [Fact]
    public async Task The_selected_cup_region_drives_one_Tripo_task_and_preserves_its_GLB()
    {
        byte[] frame = CoffeeOnTable();
        var cup = RoomObjects.Locate(frame, "copy this cup", .5f, .5f, default);
        var source = GlbWriter.Write(PartMesh.Build(Mug()), "Mug", .1f);
        var calls = new List<string>();
        using var tripo = new TripoClient("test-key", api: new Recorder(async r =>
        {
            string path = r.RequestUri.AbsolutePath;
            calls.Add(path);
            Assert.Equal("Bearer", r.Headers.Authorization?.Scheme);
            string data;
            if (path == "/v3/files")
            {
                var file = Assert.Single(Assert.IsType<MultipartFormDataContent>(r.Content));
                Assert.Equal("file", file.Headers.ContentDisposition.Name.Trim('"'));
                Assert.Equal(cup.Crop, await file.ReadAsByteArrayAsync());
                Assert.False(frame.SequenceEqual(cup.Crop));
                data = "{'file_token':'file_cup'}";
            }
            else if (path == "/v3/generation/image-to-model")
            {
                var request = JObject.Parse(await r.Content.ReadAsStringAsync());
                Assert.Equal("file_cup", (string)request["input"]);
                Assert.Null(request["prompt"]);
                Assert.True((bool)request["texture"]);
                data = "{'task_id':'one'}";
            }
            else { Assert.Equal("/v3/tasks/one", path); data = "{'status':'success','output':{'model_url':'https://example.test/cup.glb'}}"; }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{'code':0,'data':" + data + "}") };
        }), downloads: new Recorder(r =>
        {
            Assert.Null(r.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(source) });
        }));
        Assert.Equal(source, await tripo.ImageToModelAsync(cup.Crop, null, default));
        Assert.Equal(new[] { "/v3/files", "/v3/generation/image-to-model", "/v3/tasks/one" }, calls);
    }

    static (float[] lo, float[] hi) Bounds(float[] p)
    {
        var lo = new[] { float.MaxValue, float.MaxValue, float.MaxValue }; var hi = new[] { float.MinValue, float.MinValue, float.MinValue };
        for (int i = 0; i < p.Length; i++) { lo[i % 3] = Math.Min(lo[i % 3], p[i]); hi[i % 3] = Math.Max(hi[i % 3], p[i]); }
        return (lo, hi);
    }

    // ------------------------------------------------------------------ the shape (Shared/PartMesh)
    [Fact]
    public void Every_solid_is_built_to_the_common_measure()
    {
        var parts = Mug();
        var m = PartMesh.Build(parts);
        Assert.NotNull(m);
        Assert.Equal(m.positions.Length, m.normals.Length); Assert.Equal(m.positions.Length, m.colors.Length);
        Assert.True(m.Triangles > 500 && m.indices.Length % 3 == 0);
        Assert.All(m.indices, i => Assert.InRange(i, 0, m.Vertices - 1));
        var (lo, hi) = Bounds(m.positions);
        float longest = Math.Max(hi[0] - lo[0], Math.Max(hi[1] - lo[1], hi[2] - lo[2]));
        Assert.InRange(longest, 0.97f, 1.03f);                 // its longest side is 1
        Assert.InRange(lo[1], -0.02f, 0.02f);                   // it stands on y = 0
        Assert.InRange((lo[0] + hi[0]) * 0.5f, -0.03f, 0.03f);  // its middle over the origin
        Assert.Equal(hi[1] - lo[1], m.height, 2);
        for (int i = 0; i < m.Vertices; i++)
        {
            float x = m.normals[i * 3], y = m.normals[i * 3 + 1], z = m.normals[i * 3 + 2];
            Assert.InRange(MathF.Sqrt(x * x + y * y + z * z), 0.99f, 1.01f);
        }
    }

    [Fact]
    public void Every_triangle_faces_outwards()
    {
        // Unity's winding: the cross product of the first two edges, as numbers, points out of the front - with the normals of its corners
        foreach (string shape in ShapePart.Shapes)
            foreach (var turn in new[] { (0f, 0f, 0f), (35f, 20f, -50f) })
            {
                var m = PartMesh.Build(new List<ShapePart> { Part(shape, 0, 0.5f, 0, 0.8f, 1f, shape == "torus" ? 0.8f : 0.6f, "#9BD3A0", 0.3f, turn.Item1, turn.Item2, turn.Item3) });
                Assert.NotNull(m);
                int facing = 0, counted = 0;
                for (int t = 0; t < m.indices.Length; t += 3)
                {
                    int a = m.indices[t] * 3, b = m.indices[t + 1] * 3, c = m.indices[t + 2] * 3;
                    float ux = m.positions[b] - m.positions[a], uy = m.positions[b + 1] - m.positions[a + 1], uz = m.positions[b + 2] - m.positions[a + 2];
                    float vx = m.positions[c] - m.positions[a], vy = m.positions[c + 1] - m.positions[a + 1], vz = m.positions[c + 2] - m.positions[a + 2];
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    if (nx * nx + ny * ny + nz * nz < 1e-14f) continue;      // (a sliver at a pole)
                    float sx = m.normals[a] + m.normals[b] + m.normals[c], sy = m.normals[a + 1] + m.normals[b + 1] + m.normals[c + 1], sz = m.normals[a + 2] + m.normals[b + 2] + m.normals[c + 2];
                    counted++;
                    if (nx * sx + ny * sy + nz * sz > 0) facing++;
                }
                Assert.True(counted > 20, shape);
                Assert.True(facing == counted, $"{shape} turned {turn}: {counted - facing} of {counted} triangles face inwards");
            }
    }

    [Fact]
    public void A_part_is_turned_as_Unity_turns_it()
    {
        // a rod standing along y; rz = 90 lays it along x, rx = 90 along z (z first, then x, then y: Quaternion.Euler)
        (float w, float h, float d) Size(float rx, float ry, float rz)
        {
            // (a flat plate under it keeps the whole from being re-measured to the rod alone)
            var m = PartMesh.Build(new List<ShapePart> { Part("box", 0, 0.5f, 0, 1f, 1f, 1f, "#FFFFFF", 0.1f), Part("capsule", 0, 0.5f, 0, 0.1f, 2f, 0.1f, "#4A2F27", 0, rx, ry, rz) });
            int first = -1;
            for (int i = 0; i < m.Vertices && first < 0; i++) if (m.colors[i * 3] < 0.5f) first = i;
            var p = m.positions.Skip(first * 3).ToArray();
            var (lo, hi) = Bounds(p);
            return (hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]);
        }
        var up = Size(0, 0, 0); Assert.True(up.h > up.w * 5 && up.h > up.d * 5);
        var alongX = Size(0, 0, 90); Assert.True(alongX.w > alongX.h * 5 && alongX.w > alongX.d * 5);
        var alongZ = Size(90, 0, 0); Assert.True(alongZ.d > alongZ.h * 5 && alongZ.d > alongZ.w * 5);
        // rz = 20 leans its top to the LEFT (-x); rx = 20 leans its top BACK (+z): the sculptor's guide says so
        var lean = PartMesh.Build(new List<ShapePart> { Part("box", 0, 0.05f, 0, 1f, 0.1f, 1f, "#FFFFFF", 0.1f), Part("capsule", 0, 0.5f, 0, 0.06f, 0.8f, 0.06f, "#4A2F27", 0, 20f, 0, 20f) });
        float topY = float.MinValue, topX = 0, topZ = 0;
        for (int i = 0; i < lean.Vertices; i++) if (lean.colors[i * 3] < 0.5f && lean.positions[i * 3 + 1] > topY) { topY = lean.positions[i * 3 + 1]; topX = lean.positions[i * 3]; topZ = lean.positions[i * 3 + 2]; }
        Assert.True(topX < -0.05f, "the top leans to -x");
        Assert.True(topZ > 0.05f, "the top leans to +z");
    }

    [Fact]
    public void The_shape_built_is_the_one_the_host_measured()
    {
        // the sculptor's own worked examples: checked by the host (WorldRules.Sculpt measures every part), built by PartMesh - both must agree on how tall the thing is
        int tried = 0;
        foreach (var (what, answer) in WorldRules.SculptExamples)
        {
            var s = WorldRules.Sculpt(JObject.Parse(answer), out string why);
            Assert.True(s != null, what + ": " + why);
            var m = PartMesh.Build(s.parts);
            Assert.NotNull(m);
            var (lo, hi) = Bounds(m.positions);
            float longest = Math.Max(hi[0] - lo[0], Math.Max(hi[1] - lo[1], hi[2] - lo[2]));
            Assert.InRange(longest, 0.95f, 1.05f);
            Assert.InRange(m.height - s.height, -0.04f, 0.04f);
            Assert.InRange(lo[1], -0.03f, 0.03f);
            tried++;
        }
        Assert.True(tried >= 2);
    }

    [Fact]
    public void Nothing_to_build_gives_nothing()
    {
        Assert.Null(PartMesh.Build(null));
        Assert.Null(PartMesh.Build(new List<ShapePart>()));
        Assert.Null(PartMesh.Build(new List<ShapePart> { Part("pyramid", 0, 0, 0, 1, 1, 1) }));
        Assert.Null(PartMesh.Build(new List<ShapePart> { Part("box", float.NaN, 0, 0, 1, 1, 1) }));
        // a shape that is not known is left out; the rest is still built
        Assert.NotNull(PartMesh.Build(new List<ShapePart> { Part("pyramid", 0, 0, 0, 1, 1, 1), null, Part("sphere", 0, 0.5f, 0, 1, 1, 1) }));
        Assert.Equal(new System.Numerics.Vector3(1f, 0.6f, 0.2f), PartMesh.Hex("#FF9933", default));
        Assert.Equal(new System.Numerics.Vector3(0.5f, 0.5f, 0.5f), PartMesh.Hex("red", new System.Numerics.Vector3(0.5f)));
    }

    // ------------------------------------------------------------------ the 3D file (GlbWriter)
    [Fact]
    public void The_glb_passes_the_rules_of_gltf()
    {
        var m = PartMesh.Build(Mug());
        byte[] glb = GlbWriter.Write(m, "Red mug", 0.1f);
        Assert.NotNull(glb);
        // the container: "glTF", version 2, the whole length; a JSON chunk and a BIN chunk, each a multiple of four long
        Assert.Equal(0x46546C67u, BitConverter.ToUInt32(glb, 0)); Assert.Equal(2u, BitConverter.ToUInt32(glb, 4)); Assert.Equal((uint)glb.Length, BitConverter.ToUInt32(glb, 8));
        uint jsonLen = BitConverter.ToUInt32(glb, 12);
        Assert.Equal(0u, jsonLen % 4); Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(glb, 16));
        uint binLen = BitConverter.ToUInt32(glb, 20 + (int)jsonLen);
        Assert.Equal(0u, binLen % 4); Assert.Equal(0x004E4942u, BitConverter.ToUInt32(glb, 24 + (int)jsonLen));
        Assert.Equal(glb.Length, 28 + (int)jsonLen + (int)binLen);

        var facts = GlbWriter.Check(glb, out string why);
        Assert.True(facts != null, why);
        Assert.Equal(m.Triangles, facts.triangles);
        Assert.Equal(3, facts.primitives);      // one per colour: berry, cocoa, white
        Assert.Equal(3, facts.materials);
        Assert.Equal("Red mug", facts.name);
        // 1 unit long, written at 0.1 m: the longest side of the file is 10 cm, and it stands on y = 0
        float longest = Math.Max(facts.max[0] - facts.min[0], Math.Max(facts.max[1] - facts.min[1], facts.max[2] - facts.min[2]));
        Assert.InRange(longest, 0.097f, 0.103f);
        Assert.InRange(facts.min[1], -0.003f, 0.003f);

        // every accessor and buffer view by hand once more: counts, lengths, alignment, min / max on POSITION only
        var root = JObject.Parse(System.Text.Encoding.UTF8.GetString(glb, 20, (int)jsonLen));
        Assert.Equal("2.0", (string)root["asset"]["version"]);
        var views = (JArray)root["bufferViews"]; var accessors = (JArray)root["accessors"];
        Assert.Equal(9, accessors.Count); Assert.Equal(9, views.Count);
        long end = 0;
        foreach (var v in views)
        {
            long o = (long)v["byteOffset"], l = (long)v["byteLength"];
            Assert.Equal(0, o % 4); Assert.True(o >= end, "buffer views overlap"); end = o + l;
            Assert.Contains((int)v["target"], new[] { 34962, 34963 });
        }
        Assert.True(end <= (long)root["buffers"][0]["byteLength"] && (long)root["buffers"][0]["byteLength"] == binLen);
        foreach (JObject prim in (JArray)root["meshes"][0]["primitives"])
        {
            var pos = (JObject)accessors[(int)prim["attributes"]["POSITION"]]; var nrm = (JObject)accessors[(int)prim["attributes"]["NORMAL"]]; var idx = (JObject)accessors[(int)prim["indices"]];
            Assert.Equal("VEC3", (string)pos["type"]); Assert.Equal(5126, (int)pos["componentType"]); Assert.NotNull(pos["min"]); Assert.NotNull(pos["max"]);
            Assert.Equal((int)pos["count"], (int)nrm["count"]); Assert.Null(nrm["min"]);
            Assert.Equal("SCALAR", (string)idx["type"]); Assert.Equal(5123, (int)idx["componentType"]); Assert.Equal(0, (int)idx["count"] % 3);
            Assert.Equal((long)pos["count"] * 12, (long)views[(int)pos["bufferView"]]["byteLength"]);
            Assert.Equal((long)idx["count"] * 2, (long)views[(int)idx["bufferView"]]["byteLength"]);
            var colour = (JArray)root["materials"][(int)prim["material"]]["pbrMetallicRoughness"]["baseColorFactor"];
            Assert.Equal(4, colour.Count); Assert.Equal(1.0, (double)colour[3]);
        }
    }

    [Fact]
    public void The_glb_read_back_is_the_shape_the_headset_draws()
    {
        var m = PartMesh.Build(Mug());
        const float Metres = 0.25f;
        byte[] glb = GlbWriter.Write(m, "Mug", Metres);
        var back = GlbReader.Read(glb, out string error);
        Assert.True(back != null, error);
        Assert.Equal(m.Triangles, back.indices.Length / 3);
        // (corners no triangle uses - the spare ones along the axis of a turned solid - are not written)
        Assert.InRange(back.positions.Length / 3, m.Vertices * 9 / 10, m.Vertices);
        Assert.Equal(3, back.primitives);

        // The file's front faces +z (glTF) where the thing's faces -z (Unity): z is negated on the way out. The
        // project's reader comes into Unity by negating x. So the thing comes back turned half round - x and z
        // negated - with every triangle facing outwards again. Triangle by triangle (the file is sorted by colour):
        static string Key(float x, float y, float z) => $"{(int)MathF.Round(x * 2000f)},{(int)MathF.Round(y * 2000f)},{(int)MathF.Round(z * 2000f)}";      // (whole numbers: no "-0")
        var want = new Dictionary<string, int>();
        for (int t = 0; t < m.indices.Length; t += 3)
        {
            var corners = new string[3];
            for (int k = 0; k < 3; k++) { int i = m.indices[t + k] * 3; corners[k] = Key(-m.positions[i] * Metres, m.positions[i + 1] * Metres, -m.positions[i + 2] * Metres); }
            // (the same triangle whichever corner it starts at; its direction of winding is part of it)
            string a = string.Join("|", corners[0], corners[1], corners[2]), b = string.Join("|", corners[1], corners[2], corners[0]), c = string.Join("|", corners[2], corners[0], corners[1]);
            string key = new[] { a, b, c }.Min(StringComparer.Ordinal);
            want[key] = want.GetValueOrDefault(key) + 1;
        }
        int matched = 0;
        for (int t = 0; t < back.indices.Length; t += 3)
        {
            var corners = new string[3];
            for (int k = 0; k < 3; k++) { int i = back.indices[t + k] * 3; corners[k] = Key(back.positions[i], back.positions[i + 1], back.positions[i + 2]); }
            string a = string.Join("|", corners[0], corners[1], corners[2]), b = string.Join("|", corners[1], corners[2], corners[0]), c = string.Join("|", corners[2], corners[0], corners[1]);
            string key = new[] { a, b, c }.Min(StringComparer.Ordinal);
            if (want.TryGetValue(key, out int n) && n > 0) { want[key] = n - 1; matched++; }
        }
        Assert.True(m.Triangles == matched, $"{m.Triangles - matched} of {m.Triangles} triangles came back different, e.g. {string.Join(" ; ", want.Where(kv => kv.Value > 0).Take(3).Select(kv => kv.Key))}");

        // the normals came along (turned the same way), and the colours are the parts' own (linear in the file, as glTF wants)
        var colours = new HashSet<string>();
        for (int i = 0; i < back.colors.Length; i += 4) colours.Add($"{back.colors[i]:0.000},{back.colors[i + 1]:0.000},{back.colors[i + 2]:0.000}");
        Assert.Equal(3, colours.Count);
        Assert.Contains("1.000,1.000,1.000", colours);
        Assert.Contains($"{GlbWriter.Linear(0xE8 / 255f):0.000},{GlbWriter.Linear(0x69 / 255f):0.000},{GlbWriter.Linear(0x7D / 255f):0.000}", colours);
        for (int t = 0; t < back.indices.Length; t += 3)
        {
            int a = back.indices[t] * 3, b = back.indices[t + 1] * 3, c = back.indices[t + 2] * 3;
            float ux = back.positions[b] - back.positions[a], uy = back.positions[b + 1] - back.positions[a + 1], uz = back.positions[b + 2] - back.positions[a + 2];
            float vx = back.positions[c] - back.positions[a], vy = back.positions[c + 1] - back.positions[a + 1], vz = back.positions[c + 2] - back.positions[a + 2];
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (nx * nx + ny * ny + nz * nz < 1e-16f) continue;
            float d = nx * (back.normals[a] + back.normals[b] + back.normals[c]) + ny * (back.normals[a + 1] + back.normals[b + 1] + back.normals[c + 1]) + nz * (back.normals[a + 2] + back.normals[b + 2] + back.normals[c + 2]);
            Assert.True(d > 0, "a triangle of the file read back faces inwards");
        }
    }

    [Fact]
    public void A_broken_glb_is_not_passed()
    {
        byte[] good = GlbWriter.Write(PartMesh.Build(Mug()), "Mug");
        Assert.NotNull(GlbWriter.Check(good, out _));
        Assert.Null(GlbWriter.Write(null, "x"));
        Assert.Null(GlbWriter.Check(null, out _));
        Assert.Null(GlbWriter.Check(good.Take(good.Length - 4).ToArray(), out string why)); Assert.Contains("header", why);
        // the header's length changed to fit a file that lost its last four bytes: now the BIN chunk does not fit
        var cut = good.Take(good.Length - 4).ToArray(); BitConverter.GetBytes((uint)cut.Length).CopyTo(cut, 8);
        Assert.Null(GlbWriter.Check(cut, out why)); Assert.Contains("BIN", why);
        // a position moved: POSITION's min / max are not the truth any more
        var moved = (byte[])good.Clone();
        int bin = 28 + (int)BitConverter.ToUInt32(good, 12);
        BitConverter.GetBytes(99f).CopyTo(moved, bin);
        Assert.Null(GlbWriter.Check(moved, out why)); Assert.Contains("min / max", why);
        // not a GLB at all
        Assert.Null(GlbWriter.Check(System.Text.Encoding.ASCII.GetBytes("{\"asset\":{\"version\":\"2.0\"}}    "), out why));
    }

    // ------------------------------------------------------------------ the picture of it (ModelRender)
    [Fact]
    public void The_preview_shows_the_thing_with_an_ink_line()
    {
        var m = PartMesh.Build(Mug());
        const int S = 96;
        byte[] cut = ModelRender.Render(m, S, 0u);
        Assert.Equal(S * S * 4, cut.Length);
        Assert.Equal(0, cut[3]);                                    // the corner is see-through
        int solid = 0, ink = 0, berry = 0;
        for (int i = 0; i < cut.Length; i += 4)
        {
            if (cut[i + 3] < 250) continue;
            solid++;
            if (Math.Abs(cut[i + 2] - 0x4A) < 12 && Math.Abs(cut[i + 1] - 0x2F) < 12 && Math.Abs(cut[i] - 0x27) < 12) ink++;
            if (cut[i + 2] > 150 && cut[i + 1] < 160 && cut[i] < 180 && cut[i + 2] - cut[i + 1] > 50) berry++;
        }
        Assert.InRange(solid, S * S / 5, S * S * 9 / 10);           // it fills the picture, and is not the whole picture
        Assert.True(ink > 60, $"an ink line ({ink} pixels)");
        Assert.True(berry > solid / 5, $"the mug's own colour ({berry} of {solid} pixels)");
        byte[] paper = ModelRender.Render(m, S);
        Assert.Equal(255, paper[3]); Assert.Equal(0xEA, paper[0]); Assert.Equal(0xF8, paper[1]); Assert.Equal(0xFF, paper[2]);       // cream paper in the corner (blue, green, red)
    }

    // ------------------------------------------------------------------ the cut round the hands (Shared/EyeMath)
    [Fact]
    public void The_picture_is_cut_round_the_hands()
    {
        // no hand seen: the middle of the view
        EyeMath.Crop(1280, 1280, default, out int x, out int y, out int w, out int h);
        Assert.Equal((179, 179, 921, 921), (x, y, w, h));
        // a hand in the lower right: a box round it, wide enough for what it holds, inside the picture
        var hands = new EyeMath.Box(); hands.Add(900, 800); hands.Add(1000, 950); hands.Add(960, 700);
        EyeMath.Crop(1280, 1280, hands, out x, out y, out w, out h);
        Assert.Equal(820, w); Assert.Equal(820, h);                 // 2 x 250 + a quarter of 1280
        Assert.True(x <= 900 - 150 && x + w == 1280 || x + w >= 1000 + 150, "the hand is well inside");
        Assert.True(x >= 0 && y >= 0 && x + w <= 1280 && y + h <= 1280);
        // a small hand far away: never less than the least side
        var far = new EyeMath.Box(); far.Add(640, 640); far.Add(650, 650);
        EyeMath.Crop(1280, 960, far, out x, out y, out w, out h);
        Assert.Equal(560, w); Assert.Equal(560, h);
        // a point straight ahead lands on the lens's centre; up in the room is up in the picture (a smaller row)
        Assert.True(EyeMath.Project(0, 0, 0.4f, 866, 866, 640, 640, out float px, out float py)); Assert.Equal(640f, px, 2); Assert.Equal(640f, py, 2);
        Assert.True(EyeMath.Project(0.1f, 0.1f, 0.4f, 866, 866, 640, 640, out px, out py)); Assert.True(px > 640 && py < 640);
        Assert.False(EyeMath.Project(0, 0, -0.4f, 866, 866, 640, 640, out _, out _));
    }

    [Fact]
    public void The_cut_is_made_smaller_and_turned_for_the_encoder()
    {
        // 8 x 4, each pixel's red = its column * 10, green = its row * 10 (top row first)
        const int W = 8, H = 4;
        var img = new byte[W * H * 4];
        for (int r = 0; r < H; r++) for (int c = 0; c < W; c++) { int o = (r * W + c) * 4; img[o] = (byte)(c * 10); img[o + 1] = (byte)(r * 10); img[o + 3] = 255; }
        byte[] cut = EyeMath.Cut(img, W, H, 2, 1, 4, 2, 1280, out int w, out int h);
        Assert.Equal((4, 2), (w, h));
        // the BOTTOM row of the cut comes first: row 2 of the picture, columns 2..5
        Assert.Equal(20, cut[0]); Assert.Equal(20, cut[1]); Assert.Equal(50, cut[3 * 4]);
        Assert.Equal(10, cut[(1 * 4) * 4 + 1]);                     // then row 1
        // made smaller by whole steps until it fits: 8 x 4 into at most 16 stays (16 is the least a side is cut to); averaged when a step is taken
        byte[] small = EyeMath.Cut(new byte[64 * 32 * 4], 64, 32, 0, 0, 64, 32, 16, out w, out h);
        Assert.Equal((16, 8), (w, h)); Assert.Equal(16 * 8 * 4, small.Length);
        Assert.Null(EyeMath.Cut(new byte[10], 8, 4, 0, 0, 8, 4, 16, out _, out _));
    }

    // ------------------------------------------------------------------ what is said and understood (SightRules)
    [Theory]
    [InlineData("이거 만들어 줘")]
    [InlineData("이거 3D로 만들어 줘")]
    [InlineData("이것 좀 만들어 줄래?")]
    [InlineData("이거랑 똑같은 거 만들어 줘")]
    [InlineData("내가 들고 있는 거 만들어 줘")]
    [InlineData("이 물건 3D 모델로 만들어 줘")]
    [InlineData("make this")]
    [InlineData("Can you make this in 3D?")]
    [InlineData("copy the cup on my desk")]
    [InlineData("make this cup")]
    [InlineData("책상 위에 있는 컵을 만들어줘")]
    [InlineData("이 오브젝트를 만들어줘")]
    [InlineData("make what I'm holding")]
    public void Showing_a_thing_and_asking_for_it_is_understood_plainly(string said) => Assert.Equal(SightRules.Make, SightRules.Plain(said));

    [Theory]
    [InlineData("의자 만들어 줘")]                 // a thing that is NAMED: makeItem
    [InlineData("이 아이콘 새로 만들어 줘")]        // a design on a screen: makeDesigns
    [InlineData("이거 뭐야?")]
    [InlineData("이거 봐 봐")]
    [InlineData("make a chair")]
    [InlineData("make this window bigger")]
    [InlineData("")]
    public void Other_sentences_are_left_to_the_others(string said) => Assert.Null(SightRules.Plain(said));

    [Fact]
    public void Without_the_camera_the_partner_says_so_and_asks_for_a_description()
    {
        foreach (string why in new[] { "denied", "nocamera", "timeout", "notFound" })
        {
            string ko = SightRules.Line(why, true), en = SightRules.Line(why, false);
            Assert.True(ko.Length > 0 && en.Length > 0, why);
            Assert.Contains("말로", ko);                              // "tell me in words what it is"
            Assert.Contains("ell me what it is", en);
        }
        Assert.Contains("camera", SightRules.Line("denied", false));
        Assert.Contains("머그컵이구나", SightRules.Line("seen", true, "머그컵"));
        Assert.Contains("가위구나", SightRules.Line("seen", true, "가위"));
        Assert.Equal("", SightRules.Line("no such line", false));
        // the two actions are known to the conversation, and its prompt tells them from makeItem and deliverDoc
        Assert.True(SightRules.IsAction("makeFromSight") && SightRules.IsAction("exportThing") && !SightRules.IsAction("makeItem"));
        string prompt = SightRules.PromptSection(true);
        Assert.Contains("makeFromSight", prompt); Assert.Contains("exportThing", prompt); Assert.Contains("makeItem", prompt); Assert.Contains("deliverDoc", prompt);
        Assert.Contains("no headset", SightRules.PromptSection(false));
    }

    [Fact]
    public void The_vision_model_is_asked_for_what_the_sculptor_needs()
    {
        var kinds = ItemKinds.All.Select(k => k.id).ToList();
        var shape = SightRules.SeeShape(kinds, ItemUses.All);
        var required = ((JArray)shape["required"]).Select(t => (string)t).ToList();
        foreach (string field in new[] { "found", "what", "name", "kind", "uses", "realSizeCm", "colour", "note", "modelPrompt", "iconPrompt" }) Assert.Contains(field, required);
        Assert.Equal(required.Count, ((JObject)shape["properties"]).Count);      // strict: every field is required
        Assert.False((bool)shape["additionalProperties"]);
        // what it answers is read by the same rules as a thing made from words
        var idea = WorldRules.Item(JObject.Parse("{\"found\":true,\"what\":\"red ceramic mug\",\"name\":\"머그컵\",\"kind\":\"cup\",\"uses\":[\"decor\"],\"realSizeCm\":10,\"colour\":\"#E8697D\",\"note\":\"머그컵이다!\",\"modelPrompt\":\"A stout cylinder ...\",\"iconPrompt\":\"a red mug\"}"), ItemKinds.Guess("red ceramic mug"));
        Assert.Equal("머그컵", idea.name); Assert.Equal("cup", idea.kind); Assert.Equal(10f, idea.realCm); Assert.StartsWith("A stout", idea.modelPrompt);
        string note = SightRules.SculptNote("red ceramic mug", idea.modelPrompt);
        Assert.Contains("PHOTO", note); Assert.Contains("red ceramic mug", note); Assert.Contains("A stout cylinder", note); Assert.Contains("Never sculpt the hand", note);
        Assert.Contains("\"이거 '만들어' 줘\"", SightRules.SeeAsk("이거 \"만들어\" 줘", "Korean", kinds, ItemUses.All));
    }

    [Fact]
    public void The_files_are_named_after_the_thing()
    {
        Assert.Equal("Red ceramic mug", SightRules.Title(" red ceramic mug. "));
        Assert.Equal("Nilo model", SightRules.Title(""));
        Assert.True(SightRules.Title(new string('a', 90)).Length <= 40);
        var now = new DateTime(2026, 11, 18, 14, 5, 0);
        Assert.Equal("2026-11-18 1405 Red ceramic mug", SightRules.FolderName(now, "Red ceramic mug", _ => false));
        Assert.Equal("2026-11-18 1405 Red ceramic mug (2)", SightRules.FolderName(now, "Red ceramic mug", n => n == "2026-11-18 1405 Red ceramic mug"));
        Assert.Equal("Toy car", SightRules.FileStem("Toy car"));
        Assert.DoesNotContain("/", SightRules.FileStem("a/b:c"));
        // the size written into the file: the real object's when known, else a hand's width
        Assert.Equal(0.1f, SightRules.Metres(10f), 4); Assert.Equal(0.1f, SightRules.Metres(0f), 4); Assert.Equal(0.1f, SightRules.Metres(float.NaN), 4);
    }

    // ------------------------------------------------------------------ the model's file among the documents
    [Fact]
    public void A_model_is_carried_and_revealed_as_its_glb()
    {
        var book = new DocsBook();
        book.Begin("mod-0a1b2c3d", DocsRules.Model, "Red mug", "이거 만들어 줘", 1, "", out _);
        Assert.True(book.Ready("mod-0a1b2c3d", "Red mug", 1, null, new[]
        {
            new DocFile { name = "Red mug.glb", kind = "glb", path = @"C:\Docs\Nilo\Models\x\Red mug.glb" },
            new DocFile { name = "Red mug.png", kind = "png", path = @"C:\Docs\Nilo\Models\x\Red mug.png" },
        }, @"C:\Docs\Nilo\Models\x"));
        var d = book.Find("mod-0a1b2c3d");
        Assert.True(DocsRules.SafeId(d.id));
        Assert.True(DocsRules.SafeServed(d.id, "page-1-s.png"));
        var (file, icon) = DocsRules.Carried(d);
        Assert.Equal("Red mug.glb", file.name); Assert.Equal("glb", file.kind);
        Assert.Equal("/docs/mod-0a1b2c3d/page-1-s.png", icon);
        // a click on it: the folder is shown with the file selected (the copy on the Desktop when it is there)
        Assert.Equal((DocsRules.RevealSelect, @"C:\Docs\Nilo\Models\x\Red mug.glb"), DocsRules.Reveal(d, p => p.EndsWith(".glb"), _ => true));
        Assert.Equal((DocsRules.RevealSelect, @"C:\Desktop\Red mug.glb"), DocsRules.Reveal(d, p => p.EndsWith(".glb"), _ => true, @"C:\Desktop\Red mug.glb"));
        Assert.Equal((DocsRules.RevealFolder, @"C:\Docs\Nilo\Models\x"), DocsRules.Reveal(d, _ => false, _ => true));
        // a report beside it is still its PDF, opened
        book.Begin("rep-0a1b2c3d", DocsRules.Report, "Notes", "", 2, "", out _);
        book.Ready("rep-0a1b2c3d", "Notes", 2, null, new[] { new DocFile { name = "Notes.pdf", kind = "pdf", path = @"C:\n.pdf" } }, @"C:\");
        Assert.Equal((DocsRules.RevealOpen, @"C:\n.pdf"), DocsRules.Reveal(book.Find("rep-0a1b2c3d"), _ => true, _ => true));
    }

    [Fact]
    public void The_request_and_the_picture_travel_as_json()
    {
        var f = Proto.FromJson<EyeFrame>(Proto.ToJson(new EyeFrame { id = "eye-1", ok = true, jpeg = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 1, 2 }), width = 820, height = 820, cropped = true }));
        Assert.True(f.ok && f.cropped && f.id == "eye-1" && f.width == 820);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 1, 2 }, Convert.FromBase64String(f.jpeg));
        var no = Proto.FromJson<EyeFrame>("{\"id\":\"eye-2\",\"why\":\"denied\"}");
        Assert.False(no.ok); Assert.Equal("denied", no.why); Assert.Equal("", no.jpeg);
        Assert.Equal(1280, Proto.FromJson<EyeRequest>("{\"id\":\"eye-3\"}").maxSide);
        Assert.Equal("eye.request", Msg.EyeRequest); Assert.Equal("eye.frame", Msg.EyeFrame);
    }
}
