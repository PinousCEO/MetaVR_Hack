using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>
/// Writes a sculpted thing as a REAL 3D file: binary glTF 2.0 (.glb), the format Windows' 3D Viewer, Paint 3D,
/// PowerPoint, Blender and every game engine open. The shape is PartMesh.Build's - the very triangles the headset
/// draws - so the file is the thing the user saw on the desk.
///
///   one node, one mesh; one primitive per COLOUR of the thing, each with a plain matte material of that colour
///   (pbrMetallicRoughness.baseColorFactor - colours every program shows, where vertex colours are often ignored);
///   POSITION and NORMAL as floats, indices as 16-bit (32-bit for a mesh of more than 65535 vertices).
///
/// SPACE. The thing lives in Unity's left-handed space with its front towards -z; glTF is right-handed and says the
/// front of an asset faces +z. So z is negated (and every triangle is listed the other way round): the file stands
/// upright and looks at whoever opens it. (GlbReader, which reads files INTO Unity by negating x, therefore gives
/// this model back turned half round: x and z negated. The test accounts for exactly that.)
///
/// Check() reads a file back by the rules of the format - lengths, alignment, accessor bounds, min / max of the
/// positions, indices in range, unit normals - and is run on every file before the partner carries it anywhere.
/// Pure: no state, no host types.
/// </summary>
public static class GlbWriter
{
    const uint MagicGltf = 0x46546C67, ChunkJson = 0x4E4F534A, ChunkBin = 0x004E4942;
    const int CtUShort = 5123, CtUInt = 5125, CtFloat = 5126, ArrayBuffer = 34962, ElementArrayBuffer = 34963;

    /// <summary>
    /// The mesh as a .glb. name: the thing's name (the node and the mesh carry it). metres: the length of its longest
    /// side in the file (glTF's unit is the metre; the mesh itself is 1 unit long). Null when there is nothing to write.
    /// </summary>
    public static byte[] Write(PartMesh.Data mesh, string name, float metres = 1f)
    {
        if (mesh == null || mesh.indices == null || mesh.indices.Length < 3 || mesh.positions == null || mesh.positions.Length < 9) return null;
        if (!(metres > 1e-4f) || float.IsInfinity(metres)) metres = 1f;
        name = string.IsNullOrWhiteSpace(name) ? "Bambi model" : name.Trim();
        int vertexCount = mesh.positions.Length / 3;

        // the triangles of each colour (every vertex of a part has the part's colour: the first corner speaks for the triangle)
        var groups = new List<(int key, List<int> tris)>();
        var byKey = new Dictionary<int, List<int>>();
        for (int t = 0; t + 2 < mesh.indices.Length; t += 3)
        {
            int a = mesh.indices[t], b = mesh.indices[t + 1], c = mesh.indices[t + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount) continue;
            int key = ColourKey(mesh.colors, a);
            if (!byKey.TryGetValue(key, out var list)) { list = new List<int>(); byKey[key] = list; groups.Add((key, list)); }
            list.Add(a); list.Add(b); list.Add(c);
        }
        if (groups.Count == 0) return null;

        var bin = new MemoryStream();
        var w = new BinaryWriter(bin);
        var accessors = new JArray(); var views = new JArray(); var materials = new JArray(); var primitives = new JArray();
        void Pad() { while (bin.Length % 4 != 0) w.Write((byte)0); }
        int View(long offset, long length, int target) { views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = length, ["target"] = target }); return views.Count - 1; }

        var remap = new int[vertexCount];
        foreach (var (key, tris) in groups)
        {
            // this colour's own vertices, in the order they are first used
            Array.Fill(remap, -1);
            var used = new List<int>();
            foreach (int i in tris) if (remap[i] < 0) { remap[i] = used.Count; used.Add(i); }
            int n = used.Count;

            Pad();
            long at = bin.Length;
            float x0 = float.MaxValue, y0 = x0, z0 = x0, x1 = float.MinValue, y1 = x1, z1 = x1;
            foreach (int i in used)
            {
                float x = mesh.positions[i * 3] * metres, y = mesh.positions[i * 3 + 1] * metres, z = -mesh.positions[i * 3 + 2] * metres;
                w.Write(x); w.Write(y); w.Write(z);
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
            }
            int posView = View(at, bin.Length - at, ArrayBuffer);
            accessors.Add(new JObject { ["bufferView"] = posView, ["componentType"] = CtFloat, ["count"] = n, ["type"] = "VEC3", ["min"] = new JArray(x0, y0, z0), ["max"] = new JArray(x1, y1, z1) });
            int posAcc = accessors.Count - 1;

            at = bin.Length;
            foreach (int i in used)
            {
                float x = mesh.normals[i * 3], y = mesh.normals[i * 3 + 1], z = -mesh.normals[i * 3 + 2];
                float len = MathF.Sqrt(x * x + y * y + z * z);
                if (len > 1e-6f && !float.IsNaN(len) && !float.IsInfinity(len)) { x /= len; y /= len; z /= len; } else { x = 0; y = 1; z = 0; }
                w.Write(x); w.Write(y); w.Write(z);
            }
            int nrmView = View(at, bin.Length - at, ArrayBuffer);
            accessors.Add(new JObject { ["bufferView"] = nrmView, ["componentType"] = CtFloat, ["count"] = n, ["type"] = "VEC3" });
            int nrmAcc = accessors.Count - 1;

            at = bin.Length;
            bool wide = n > 65535;
            for (int t = 0; t < tris.Count; t += 3)
            {
                // (mirrored in z: each triangle the other way round)
                int a = remap[tris[t]], b = remap[tris[t + 2]], c = remap[tris[t + 1]];
                if (wide) { w.Write((uint)a); w.Write((uint)b); w.Write((uint)c); } else { w.Write((ushort)a); w.Write((ushort)b); w.Write((ushort)c); }
            }
            int idxView = View(at, bin.Length - at, ElementArrayBuffer);
            accessors.Add(new JObject { ["bufferView"] = idxView, ["componentType"] = wide ? CtUInt : CtUShort, ["count"] = tris.Count, ["type"] = "SCALAR" });
            int idxAcc = accessors.Count - 1;

            float r = ((key >> 16) & 255) / 255f, g = ((key >> 8) & 255) / 255f, bl = (key & 255) / 255f;
            materials.Add(new JObject
            {
                ["name"] = $"#{key:X6}",
                ["pbrMetallicRoughness"] = new JObject { ["baseColorFactor"] = new JArray(Round(Linear(r)), Round(Linear(g)), Round(Linear(bl)), 1.0), ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.9 },
            });
            primitives.Add(new JObject { ["attributes"] = new JObject { ["POSITION"] = posAcc, ["NORMAL"] = nrmAcc }, ["indices"] = idxAcc, ["material"] = materials.Count - 1, ["mode"] = 4 });
        }
        Pad();
        w.Flush();
        byte[] data = bin.ToArray();

        var root = new JObject
        {
            ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "Living AI Partner (Bambi's toy maker)" },
            ["scene"] = 0,
            ["scenes"] = new JArray(new JObject { ["name"] = name, ["nodes"] = new JArray(0) }),
            ["nodes"] = new JArray(new JObject { ["name"] = name, ["mesh"] = 0 }),
            ["meshes"] = new JArray(new JObject { ["name"] = name, ["primitives"] = primitives }),
            ["materials"] = materials,
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new JArray(new JObject { ["byteLength"] = data.Length }),
        };
        byte[] json = Encoding.UTF8.GetBytes(root.ToString(Newtonsoft.Json.Formatting.None));
        int jsonPadded = (json.Length + 3) & ~3;

        var file = new MemoryStream(12 + 8 + jsonPadded + 8 + data.Length);
        var f = new BinaryWriter(file);
        f.Write(MagicGltf); f.Write((uint)2); f.Write((uint)(12 + 8 + jsonPadded + 8 + data.Length));
        f.Write((uint)jsonPadded); f.Write(ChunkJson); f.Write(json);
        for (int i = json.Length; i < jsonPadded; i++) f.Write((byte)0x20);
        f.Write((uint)data.Length); f.Write(ChunkBin); f.Write(data);
        f.Flush();
        return file.ToArray();
    }

    static int ColourKey(float[] colors, int vertex)
    {
        if (colors == null || colors.Length < vertex * 3 + 3) return 0xCC9E73;
        int Q(float v) => (int)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);
        return Q(colors[vertex * 3]) << 16 | Q(colors[vertex * 3 + 1]) << 8 | Q(colors[vertex * 3 + 2]);
    }

    /// <summary>A colour as written ("#RRGGBB" is sRGB) to the linear value glTF's baseColorFactor wants.</summary>
    public static float Linear(float srgb) => srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    static double Round(float v) => Math.Round(v, 5);

    // ====================================================================== reading it back by the rules
    /// <summary>What Check found in a file that passed.</summary>
    public sealed class Facts
    {
        public int primitives, vertices, triangles, materials;
        public float[] min = new float[3], max = new float[3];      // of all positions, in the file's own (glTF) space
        public string name = "";
    }

    /// <summary>
    /// Reads a .glb back by the rules of glTF 2.0 as far as this writer uses them. Null (and why) when any is broken:
    /// the container (magic, version, total length, chunk lengths in fours), the JSON, every buffer view inside the
    /// buffer, every accessor inside its view and aligned to its component, POSITION with min / max that are the real
    /// bounds, NORMAL of the same count and of unit length, indices in threes and inside the vertices, materials that exist.
    /// </summary>
    public static Facts Check(byte[] glb, out string why)
    {
        why = "";
        try
        {
            if (glb == null || glb.Length < 20) { why = "too short to be a GLB"; return null; }
            if (BitConverter.ToUInt32(glb, 0) != MagicGltf) { why = "it does not start with 'glTF'"; return null; }
            if (BitConverter.ToUInt32(glb, 4) != 2) { why = "not version 2"; return null; }
            if (BitConverter.ToUInt32(glb, 8) != glb.Length) { why = $"the header says {BitConverter.ToUInt32(glb, 8)} bytes, the file has {glb.Length}"; return null; }
            if (glb.Length % 4 != 0) { why = "the file's length is not a multiple of 4"; return null; }
            uint jsonLen = BitConverter.ToUInt32(glb, 12);
            if (BitConverter.ToUInt32(glb, 16) != ChunkJson) { why = "the first chunk is not JSON"; return null; }
            if (jsonLen % 4 != 0 || 20 + jsonLen + 8 > glb.Length) { why = "the JSON chunk's length is wrong"; return null; }
            int binHead = 20 + (int)jsonLen;
            uint binLen = BitConverter.ToUInt32(glb, binHead);
            if (BitConverter.ToUInt32(glb, binHead + 4) != ChunkBin) { why = "the second chunk is not BIN"; return null; }
            if (binLen % 4 != 0 || binHead + 8 + binLen != glb.Length) { why = "the BIN chunk's length is wrong"; return null; }
            int bin = binHead + 8;
            var root = JObject.Parse(Encoding.UTF8.GetString(glb, 20, (int)jsonLen));
            if ((string)root["asset"]?["version"] != "2.0") { why = "asset.version is not \"2.0\""; return null; }
            var buffers = root["buffers"] as JArray; var views = root["bufferViews"] as JArray; var accessors = root["accessors"] as JArray;
            var meshes = root["meshes"] as JArray; var nodes = root["nodes"] as JArray; var scenes = root["scenes"] as JArray; var mats = root["materials"] as JArray;
            if (buffers == null || buffers.Count != 1 || views == null || accessors == null || meshes == null || nodes == null || scenes == null) { why = "a table is missing (buffers, bufferViews, accessors, meshes, nodes, scenes)"; return null; }
            long bufLen = (long)buffers[0]["byteLength"];
            if (buffers[0]["uri"] != null) { why = "the buffer has a uri (it must be the BIN chunk)"; return null; }
            if (bufLen > binLen || binLen - bufLen > 3) { why = $"buffer.byteLength {bufLen} does not fit the BIN chunk of {binLen}"; return null; }
            foreach (var v in views)
            {
                long o = (long?)v["byteOffset"] ?? 0, l = (long)v["byteLength"];
                if ((int)v["buffer"] != 0 || o < 0 || l <= 0 || o + l > bufLen) { why = "a buffer view runs outside the buffer"; return null; }
            }
            // an accessor: (offset in the file, count, components, component type)
            (int at, int count, int comps, int type)? Acc(JToken index, string what, out string bad)
            {
                bad = "";
                int i = (int?)index ?? -1;
                if (i < 0 || i >= accessors.Count) { bad = $"{what}: no such accessor"; return null; }
                var a = (JObject)accessors[i];
                int vi = (int?)a["bufferView"] ?? -1;
                if (vi < 0 || vi >= views.Count) { bad = $"{what}: no such buffer view"; return null; }
                var v = (JObject)views[vi];
                int type = (int)a["componentType"], count = (int)a["count"];
                int comps = (string)a["type"] switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => 0 };
                int size = type switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => 0 };
                long vo = (long?)v["byteOffset"] ?? 0, ao = (long?)a["byteOffset"] ?? 0, vl = (long)v["byteLength"];
                if (comps == 0 || size == 0 || count <= 0) { bad = $"{what}: an accessor of no known kind"; return null; }
                if ((vo + ao) % size != 0) { bad = $"{what}: not aligned to its {size}-byte component"; return null; }
                if (ao + (long)count * comps * size > vl) { bad = $"{what}: {count} elements do not fit its buffer view"; return null; }
                return (bin + (int)(vo + ao), count, comps, type);
            }

            var facts = new Facts { materials = mats?.Count ?? 0, name = (string)((nodes.FirstOrDefault() as JObject)?["name"]) ?? "" };
            for (int k = 0; k < 3; k++) { facts.min[k] = float.MaxValue; facts.max[k] = float.MinValue; }
            // every node of the scene must exist, and at least one must show a mesh
            var sceneNodes = (scenes[(int?)root["scene"] ?? 0] as JObject)?["nodes"] as JArray;
            if (sceneNodes == null || sceneNodes.Count == 0) { why = "the scene shows no node"; return null; }
            foreach (var sn in sceneNodes)
            {
                int ni = (int)sn;
                if (ni < 0 || ni >= nodes.Count) { why = "the scene names a node that does not exist"; return null; }
                int mi = (int?)nodes[ni]["mesh"] ?? -1;
                if (mi < 0) continue;
                if (mi >= meshes.Count) { why = "a node names a mesh that does not exist"; return null; }
                foreach (JObject p in (JArray)meshes[mi]["primitives"])
                {
                    if (((int?)p["mode"] ?? 4) != 4) { why = "a primitive is not a list of triangles"; return null; }
                    var pos = Acc(p["attributes"]?["POSITION"], "POSITION", out why); if (pos == null) return null;
                    var nrm = Acc(p["attributes"]?["NORMAL"], "NORMAL", out why); if (nrm == null) return null;
                    var idx = Acc(p["indices"], "indices", out why); if (idx == null) return null;
                    var (pa, pc, pcomps, ptype) = pos.Value; var (na, nc, ncomps, ntype) = nrm.Value; var (ia, ic, icomps, itype) = idx.Value;
                    if (pcomps != 3 || ptype != CtFloat || ncomps != 3 || ntype != CtFloat) { why = "POSITION / NORMAL are not float VEC3"; return null; }
                    if (nc != pc) { why = $"NORMAL has {nc} elements, POSITION {pc}"; return null; }
                    if (icomps != 1 || (itype != CtUShort && itype != CtUInt && itype != 5121)) { why = "the indices are not unsigned scalars"; return null; }
                    if (ic % 3 != 0) { why = $"{ic} indices are not whole triangles"; return null; }
                    int mat = (int?)p["material"] ?? -1;
                    if (mat >= facts.materials) { why = "a primitive names a material that does not exist"; return null; }
                    // POSITION must carry min and max, and they must be the truth
                    var acc = (JObject)accessors[(int)p["attributes"]["POSITION"]];
                    if (acc["min"] is not JArray mn || acc["max"] is not JArray mx || mn.Count != 3 || mx.Count != 3) { why = "POSITION has no min / max"; return null; }
                    var lo = new[] { float.MaxValue, float.MaxValue, float.MaxValue }; var hi = new[] { float.MinValue, float.MinValue, float.MinValue };
                    for (int i = 0; i < pc; i++)
                        for (int k = 0; k < 3; k++)
                        {
                            float v = BitConverter.ToSingle(glb, pa + (i * 3 + k) * 4);
                            if (float.IsNaN(v) || float.IsInfinity(v)) { why = "a position is not a number"; return null; }
                            lo[k] = Math.Min(lo[k], v); hi[k] = Math.Max(hi[k], v);
                        }
                    for (int k = 0; k < 3; k++)
                    {
                        if (Math.Abs((float)mn[k] - lo[k]) > 1e-6f || Math.Abs((float)mx[k] - hi[k]) > 1e-6f) { why = $"POSITION min / max are not the real bounds (axis {k}: {(float)mn[k]}..{(float)mx[k]} written, {lo[k]}..{hi[k]} real)"; return null; }
                        facts.min[k] = Math.Min(facts.min[k], lo[k]); facts.max[k] = Math.Max(facts.max[k], hi[k]);
                    }
                    for (int i = 0; i < nc; i++)
                    {
                        float x = BitConverter.ToSingle(glb, na + i * 12), y = BitConverter.ToSingle(glb, na + i * 12 + 4), z = BitConverter.ToSingle(glb, na + i * 12 + 8);
                        float len = MathF.Sqrt(x * x + y * y + z * z);
                        if (!(Math.Abs(len - 1f) < 0.005f)) { why = $"a normal is not of unit length ({len})"; return null; }
                    }
                    for (int i = 0; i < ic; i++)
                    {
                        uint v = itype == CtUInt ? BitConverter.ToUInt32(glb, ia + i * 4) : itype == CtUShort ? BitConverter.ToUInt16(glb, ia + i * 2) : glb[ia + i];
                        if (v >= pc) { why = "an index points past the vertices"; return null; }
                    }
                    facts.primitives++; facts.vertices += pc; facts.triangles += ic / 3;
                }
            }
            if (facts.primitives == 0) { why = "there is nothing to draw"; return null; }
            return facts;
        }
        catch (Exception e) { why = "it cannot be read: " + e.Message; return null; }
    }
}
