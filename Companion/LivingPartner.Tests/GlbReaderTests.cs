using System.Numerics;
using System.Text;
using LivingPartner.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

/// <summary>The .glb reader: a generated model comes out as one mesh in Unity's space, and a bad file as a sentence, never a crash.</summary>
public class GlbReaderTests
{
    // ---- a tiny GLB writer, so every test states its file in full -------------------------------------

    sealed class Glb
    {
        readonly MemoryStream bin = new();
        readonly JArray views = new(), accessors = new();

        /// <summary>Adds raw bytes to the binary chunk as a buffer view (aligned to 4, like real writers do).</summary>
        public int View(byte[] data, int stride = 0)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var v = new JObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = data.Length };
            if (stride > 0) v["byteStride"] = stride;
            bin.Write(data, 0, data.Length);
            views.Add(v);
            return views.Count - 1;
        }

        public int Accessor(int view, int componentType, string type, int count, int byteOffset = 0, bool normalized = false)
        {
            var a = new JObject { ["bufferView"] = view, ["componentType"] = componentType, ["type"] = type, ["count"] = count };
            if (byteOffset != 0) a["byteOffset"] = byteOffset;
            if (normalized) a["normalized"] = true;
            accessors.Add(a);
            return accessors.Count - 1;
        }

        static int Comps(string type) => type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, _ => 4 };

        public int Floats(string type, params float[] v)
        {
            var b = new byte[v.Length * 4];
            Buffer.BlockCopy(v, 0, b, 0, b.Length);
            return Accessor(View(b), 5126, type, v.Length / Comps(type));
        }
        public int UShorts(string type, bool normalized, params ushort[] v)
        {
            var b = new byte[v.Length * 2];
            Buffer.BlockCopy(v, 0, b, 0, b.Length);
            return Accessor(View(b), 5123, type, v.Length / Comps(type), 0, normalized);
        }
        public int UInts(string type, params uint[] v)
        {
            var b = new byte[v.Length * 4];
            Buffer.BlockCopy(v, 0, b, 0, b.Length);
            return Accessor(View(b), 5125, type, v.Length / Comps(type));
        }
        public int UBytes(string type, bool normalized, params byte[] v) => Accessor(View(v), 5121, type, v.Length / Comps(type), 0, normalized);

        /// <summary>The finished file: the given scene description plus the buffer tables collected so far.</summary>
        public byte[] Build(string json, Action<JObject> edit = null)
        {
            var root = JObject.Parse(json);
            root["asset"] = new JObject { ["version"] = "2.0" };
            root["buffers"] = new JArray(new JObject { ["byteLength"] = bin.Length });
            root["bufferViews"] = views;
            root["accessors"] = accessors;
            edit?.Invoke(root);
            return Pack(root.ToString(Formatting.None), bin.ToArray());
        }
    }

    /// <summary>The container itself: 12-byte header, JSON chunk padded with spaces, BIN chunk padded with zeros.</summary>
    static byte[] Pack(string json, byte[] bin)
    {
        byte[] j = Encoding.UTF8.GetBytes(json);
        int jLen = (j.Length + 3) & ~3, bLen = bin == null ? 0 : (bin.Length + 3) & ~3;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + jLen + (bin == null ? 0 : 8 + bLen)));
        w.Write((uint)jLen); w.Write(0x4E4F534Au); w.Write(j); for (int i = j.Length; i < jLen; i++) w.Write((byte)' ');
        if (bin != null) { w.Write((uint)bLen); w.Write(0x004E4942u); w.Write(bin); for (int i = bin.Length; i < bLen; i++) w.Write((byte)0); }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>A unit quad in the XY plane facing +Z (glTF space), under a node that moves and doubles it, with a tinted material.</summary>
    static byte[] Quad(string extraJson = "", string nodeJson = """ "translation": [1, 2, 3], "scale": [2, 2, 2] """)
    {
        var g = new Glb();
        int pos = g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0);
        int nrm = g.Floats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1);
        int uv = g.Floats("VEC2", 0, 0, 1, 0, 1, 0.75f, 0, 0.75f);
        int idx = g.UShorts("SCALAR", false, 0, 1, 2, 0, 2, 3);
        return g.Build($$"""
            { "scene": 0, "scenes": [ { "nodes": [0] } ],
              "nodes": [ { "mesh": 0, {{nodeJson}} } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}}, "NORMAL": {{nrm}}, "TEXCOORD_0": {{uv}} }, "indices": {{idx}}, "material": 0 } ] } ],
              "materials": [ { "pbrMetallicRoughness": { "baseColorFactor": [1, 0.5, 0.25, 1] } } ]
              {{extraJson}} }
            """);
    }

    static GlbReader.Model Ok(byte[] glb)
    {
        var m = GlbReader.Read(glb, out string error);
        Assert.True(m != null, error);
        Assert.Null(error);
        return m;
    }

    static string Refused(byte[] glb)
    {
        var m = GlbReader.Read(glb, out string error);
        Assert.Null(m);
        Assert.False(string.IsNullOrWhiteSpace(error));
        return error;
    }

    static void Near(float[] expected, float[] actual, float tolerance = 1e-5f)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance, $"[{i}] expected {expected[i]} but was {actual[i]}\n expected: {string.Join(", ", expected)}\n actual:   {string.Join(", ", actual)}");
    }

    static Vector3 V(float[] a, int vertex) => new(a[vertex * 3], a[vertex * 3 + 1], a[vertex * 3 + 2]);

    /// <summary>
    /// In Unity a triangle is seen from the side on which its corners run clockwise, and (b-a)x(c-a) points to that side.
    /// So every triangle's own facing must agree with the normals of its corners - whatever the reader did to get there.
    /// </summary>
    static void AssertWindingAgreesWithNormals(GlbReader.Model m)
    {
        Assert.Equal(0, m.indices.Length % 3);
        for (int t = 0; t < m.indices.Length; t += 3)
        {
            int a = m.indices[t], b = m.indices[t + 1], c = m.indices[t + 2];
            Vector3 face = Vector3.Cross(V(m.positions, b) - V(m.positions, a), V(m.positions, c) - V(m.positions, a));
            Vector3 normal = V(m.normals, a) + V(m.normals, b) + V(m.normals, c);
            Assert.True(Vector3.Dot(face, normal) > 0, $"triangle {t / 3} faces away from its normals");
        }
    }

    // ---- (1) one indexed quad ------------------------------------------------------------------------

    [Fact]
    public void An_indexed_quad_arrives_placed_by_its_node_and_converted_to_Unity_space()
    {
        var m = Ok(Quad());

        Assert.Equal(1, m.primitives);
        Assert.Equal(4, m.positions.Length / 3);
        // scaled by 2, moved by (1,2,3), then X negated
        Near(new float[] { -1, 2, 3, -3, 2, 3, -3, 4, 3, -1, 4, 3 }, m.positions);
        Near(new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 }, m.normals);
        // each triangle turned over: (0,1,2) -> (0,2,1)
        Assert.Equal(new[] { 0, 2, 1, 0, 3, 2 }, m.indices);
        AssertWindingAgreesWithNormals(m);
        // v counted from the bottom instead of the top
        Near(new float[] { 0, 1, 1, 1, 1, 0.25f, 0, 0.25f }, m.uvs);
        // no vertex colours: the material's colour on every vertex
        Near(new float[] { 1, 0.5f, 0.25f, 1, 1, 0.5f, 0.25f, 1, 1, 0.5f, 0.25f, 1, 1, 0.5f, 0.25f, 1 }, m.colors);
        Assert.Equal(new Vector3(-3, 2, 3), m.min);
        Assert.Equal(new Vector3(-1, 4, 3), m.max);
        Assert.Null(m.texture);
        Assert.Equal("", m.textureMime);
    }

    [Fact]
    public void A_mirroring_node_does_not_leave_its_triangles_inside_out()
    {
        // a negative scale turns the triangles over once, the conversion to Unity a second time
        var m = Ok(Quad(nodeJson: """ "scale": [-1, 1, 1] """));
        Near(new float[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0 }, m.positions);
        Near(new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 }, m.normals);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, m.indices);
        AssertWindingAgreesWithNormals(m);
    }

    [Fact]
    public void Parents_move_their_children_and_a_file_without_scenes_starts_at_its_root_nodes()
    {
        var g = new Glb();
        int pos = g.Floats("VEC3", 1, 1, 1, 2, 1, 1, 1, 2, 1);
        // node 1 is the root (a matrix that moves by 10 along x, stored column by column); node 0 is its child
        byte[] glb = g.Build($$"""
            { "nodes": [ { "mesh": 0, "scale": [2, 3, 4] },
                         { "children": [0], "matrix": [1,0,0,0, 0,1,0,0, 0,0,1,0, 10,0,0,1] } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}} } } ] } ] }
            """);
        var m = Ok(glb);
        Assert.Equal(1, m.primitives);   // reached once, through its parent - not a second time as a root
        Near(new float[] { -12, 3, 4, -14, 3, 4, -12, 6, 4 }, m.positions);
        // unevenly scaled, yet the computed normal is still the triangle's true facing (+Z in both spaces)
        Near(new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, m.normals);
        Near(new float[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, m.colors);
        Near(new float[6], m.uvs);
    }

    [Fact]
    public void Normals_from_the_file_stay_perpendicular_under_an_uneven_scale()
    {
        // a slope rising 45 degrees along x, then stretched 3x along x: the surface gets flatter, so its normal must get steeper
        var g = new Glb();
        float s = MathF.Sqrt(0.5f);
        int pos = g.Floats("VEC3", 0, 0, 0, 1, 1, 0, 0, 0, -1);
        int nrm = g.Floats("VEC3", -s, s, 0, -s, s, 0, -s, s, 0);
        var m = Ok(g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0, "scale": [3, 1, 1] } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}}, "NORMAL": {{nrm}} } } ] } ] }
            """));
        var expected = Vector3.Normalize(new Vector3(1, 3, 0));   // glTF (-1,3,0)/len, X negated for Unity
        for (int i = 0; i < 3; i++)
        {
            Assert.True((V(m.normals, i) - expected).Length() < 1e-5f, V(m.normals, i).ToString());
            // perpendicular to both edges of the stretched triangle
            Assert.True(Math.Abs(Vector3.Dot(V(m.normals, i), V(m.positions, 1) - V(m.positions, 0))) < 1e-5f);
            Assert.True(Math.Abs(Vector3.Dot(V(m.normals, i), V(m.positions, 2) - V(m.positions, 0))) < 1e-5f);
        }
        AssertWindingAgreesWithNormals(m);
    }

    // ---- (2) two primitives in two nodes -------------------------------------------------------------

    [Fact]
    public void Two_nodes_are_merged_into_one_mesh_and_missing_normals_are_computed()
    {
        var g = new Glb();
        // A: one indexed triangle facing +Z with rgb vertex colours, turned a quarter turn about Y by its node
        int posA = g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        int nrmA = g.Floats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1);
        int colA = g.Floats("VEC3", 1, 0, 0, 0, 1, 0, 0, 0, 1);
        int idxA = g.UBytes("SCALAR", false, 0, 1, 2);
        // B: two triangles lying on the ground facing up, no indices and no normals, moved 5 along z
        int posB = g.Floats("VEC3", 0, 0, 0, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 1);
        string h = MathF.Sqrt(0.5f).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        byte[] glb = g.Build($$"""
            { "scenes": [ { "nodes": [0, 1] } ],
              "nodes": [ { "mesh": 0, "rotation": [0, {{h}}, 0, {{h}}] }, { "mesh": 1, "translation": [0, 0, 5] } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{posA}}, "NORMAL": {{nrmA}}, "COLOR_0": {{colA}} }, "indices": {{idxA}}, "material": 0 } ] },
                          { "primitives": [ { "attributes": { "POSITION": {{posB}} }, "mode": 4 } ] } ],
              "materials": [ { "pbrMetallicRoughness": { "baseColorFactor": [0.5, 0.5, 0.5, 0.5] } } ] }
            """);
        var m = Ok(glb);

        Assert.Equal(2, m.primitives);
        Assert.Equal(9, m.positions.Length / 3);
        Assert.Equal(9, m.normals.Length / 3);
        Assert.Equal(9, m.uvs.Length / 2);
        Assert.Equal(9, m.colors.Length / 4);
        Assert.Equal(new[] { 0, 2, 1, 3, 5, 4, 6, 8, 7 }, m.indices);

        // A: a quarter turn about Y takes +X to -Z and +Z to +X; Unity then negates X
        Near(new float[] { 0, 0, 0, 0, 0, -1, 0, 1, 0 }, m.positions.Take(9).ToArray());
        Near(new float[] { -1, 0, 0, -1, 0, 0, -1, 0, 0 }, m.normals.Take(9).ToArray());
        // vertex colour times the material's colour; rgb colours are opaque
        Near(new float[] { 0.5f, 0, 0, 0.5f, 0, 0.5f, 0, 0.5f, 0, 0, 0.5f, 0.5f }, m.colors.Take(12).ToArray());

        // B: placed after A, moved by its node, X negated
        Near(new float[] { 0, 0, 5, 0, 0, 6, -1, 0, 5, -1, 0, 5, 0, 0, 6, -1, 0, 6 }, m.positions.Skip(9).ToArray());
        for (int v = 3; v < 9; v++)
        {
            Assert.Equal(1f, V(m.normals, v).Length(), 5);
            Assert.True((V(m.normals, v) - Vector3.UnitY).Length() < 1e-5f, $"normal {v} = {V(m.normals, v)}");
        }
        // B has no material and no colours: white
        Near(Enumerable.Repeat(1f, 24).ToArray(), m.colors.Skip(12).ToArray());

        AssertWindingAgreesWithNormals(m);
        Near(new float[] { -1, 0, -1 }, new[] { m.min.X, m.min.Y, m.min.Z });
        Near(new float[] { 0, 1, 6 }, new[] { m.max.X, m.max.Y, m.max.Z });
    }

    [Fact]
    public void Computed_normals_are_smooth_across_shared_vertices()
    {
        // a roof: two slopes sharing the ridge. The ridge's normal is the average of both slopes: straight up.
        var g = new Glb();
        int pos = g.Floats("VEC3", -1, 0, 0, -1, 0, 1, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 1);
        int idx = g.UInts("SCALAR", 0, 1, 3, 0, 3, 2, 2, 3, 4, 3, 5, 4);
        var m = Ok(g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}} }, "indices": {{idx}} } ] } ] }
            """));
        float s = MathF.Sqrt(0.5f);
        for (int v = 0; v < 6; v++) Assert.Equal(1f, V(m.normals, v).Length(), 5);
        // glTF x = -1 is Unity x = +1: that slope leans outwards to +x in Unity
        Assert.True((V(m.normals, 0) - new Vector3(s, s, 0)).Length() < 1e-5f, V(m.normals, 0).ToString());
        Assert.True((V(m.normals, 2) - Vector3.UnitY).Length() < 1e-5f, V(m.normals, 2).ToString());
        Assert.True((V(m.normals, 3) - Vector3.UnitY).Length() < 1e-5f, V(m.normals, 3).ToString());
        Assert.True((V(m.normals, 5) - new Vector3(-s, s, 0)).Length() < 1e-5f, V(m.normals, 5).ToString());
        AssertWindingAgreesWithNormals(m);
    }

    // ---- (3) textures --------------------------------------------------------------------------------

    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x02, 0x03, 0xFF, 0xD9 };

    /// <summary>A triangle whose material(s) are given by the caller; the image view (if any) sits between other data in the buffer.</summary>
    static byte[] Textured(Func<int, string> rest, byte[] image = null)
    {
        var g = new Glb();
        int pos = g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        int view = image != null ? g.View(image) : -1;
        int uv = g.Floats("VEC2", 0, 0, 1, 0, 0, 1);
        return g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}}, "TEXCOORD_0": {{uv}} }, "material": 0 },
                                            { "attributes": { "POSITION": {{pos}}, "TEXCOORD_0": {{uv}} }, "material": 1 } ] } ],
              {{rest(view)}} }
            """);
    }

    [Fact]
    public void An_embedded_png_comes_back_byte_for_byte()
    {
        Assert.NotEqual(0, Png.Length % 4);   // so that the padding after it would show up if it were included
        var m = Ok(Textured(view => $$"""
            "materials": [ { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } }, { } ],
            "textures": [ { "source": 0 } ],
            "images": [ { "bufferView": {{view}}, "mimeType": "image/png" } ]
            """, Png));
        Assert.Equal(Png, m.texture);
        Assert.Equal("image/png", m.textureMime);
        Assert.Equal(2, m.primitives);
        Near(new float[] { 0, 1, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0 }, m.uvs);
    }

    [Fact]
    public void An_image_given_as_a_base64_data_uri_works_and_is_recognised_by_its_content()
    {
        // labelled png, but really a jpeg: the bytes decide. And the first material has no texture, so the second one's is taken.
        var m = Ok(Textured(_ => $$"""
            "materials": [ { "pbrMetallicRoughness": { "baseColorFactor": [1, 0, 0, 1] } },
                           { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } } ],
            "textures": [ { "source": 0 } ],
            "images": [ { "uri": "data:image/png;base64,{{Convert.ToBase64String(Jpeg)}}" } ]
            """));
        Assert.Equal(Jpeg, m.texture);
        Assert.Equal("image/jpeg", m.textureMime);
    }

    [Fact]
    public void An_image_in_another_file_leaves_the_model_without_a_texture_but_still_readable()
    {
        var m = Ok(Textured(_ => """
            "materials": [ { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } }, { } ],
            "textures": [ { "source": 0 } ],
            "images": [ { "uri": "skin.png" } ]
            """));
        Assert.Null(m.texture);
        Assert.Equal("", m.textureMime);
        Assert.Equal(6, m.positions.Length / 3);
    }

    [Fact]
    public void A_texture_that_cannot_be_used_does_not_hide_a_later_one_that_can()
    {
        var m = Ok(Textured(view => $$"""
            "materials": [ { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } } },
                           { "pbrMetallicRoughness": { "baseColorTexture": { "index": 1 } } } ],
            "textures": [ { "source": 0 }, { "source": 1 } ],
            "images": [ { "uri": "https://example.invalid/skin.png" }, { "bufferView": {{view}} } ]
            """, Png));
        Assert.Equal(Png, m.texture);
        Assert.Equal("image/png", m.textureMime);
    }

    // ---- (4) interleaved vertex data -----------------------------------------------------------------

    [Fact]
    public void Interleaved_vertices_are_read_through_their_stride()
    {
        // 32 bytes per vertex: position (3 floats), normal (3 floats), uv (2 normalised ushorts), colour (4 normalised bytes)
        var positions = new float[] { 0, 0, 0, 2, 0, 0, 0, 2, 0 };
        var uvs = new ushort[] { 0, 0, 65535, 0, 0, 13107 };
        var colours = new byte[] { 255, 0, 0, 255, 0, 255, 0, 51, 0, 0, 255, 0 };
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22 });   // 8 bytes of something else before the first vertex
        for (int v = 0; v < 3; v++)
        {
            for (int c = 0; c < 3; c++) w.Write(positions[v * 3 + c]);
            w.Write(0f); w.Write(0f); w.Write(1f);
            w.Write(uvs[v * 2]); w.Write(uvs[v * 2 + 1]);
            w.Write(colours, v * 4, 4);
        }
        w.Flush();

        var g = new Glb();
        g.View(new byte[] { 1, 2, 3 });   // pushes the interleaved view to a non-zero offset in the buffer
        int view = g.View(ms.ToArray(), 32);
        int pos = g.Accessor(view, 5126, "VEC3", 3, 8);
        int nrm = g.Accessor(view, 5126, "VEC3", 3, 8 + 12);
        int uv = g.Accessor(view, 5123, "VEC2", 3, 8 + 24, normalized: true);
        int col = g.Accessor(view, 5121, "VEC4", 3, 8 + 28, normalized: true);
        int idx = g.UInts("SCALAR", 0, 1, 2);
        var m = Ok(g.Build($$"""
            { "scene": 0, "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}}, "NORMAL": {{nrm}}, "TEXCOORD_0": {{uv}}, "COLOR_0": {{col}} }, "indices": {{idx}} } ] } ] }
            """));

        Near(new float[] { 0, 0, 0, -2, 0, 0, 0, 2, 0 }, m.positions);
        Near(new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, m.normals);
        Near(new float[] { 0, 1, 1, 1, 0, 0.8f }, m.uvs);
        Near(new float[] { 1, 0, 0, 1, 0, 1, 0, 0.2f, 0, 0, 1, 0 }, m.colors);
        Assert.Equal(new[] { 0, 2, 1 }, m.indices);
        AssertWindingAgreesWithNormals(m);
    }

    // ---- (5) files that must be refused --------------------------------------------------------------

    [Fact]
    public void Garbage_is_refused_with_a_reason()
    {
        Assert.Contains("not a GLB", Refused(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }));
        Assert.Contains("empty", Refused(Array.Empty<byte>()));
        Refused(null);
        Refused(new byte[] { 0x67 });
        Assert.Contains(".gltf", Refused(Encoding.UTF8.GetBytes("""{ "asset": { "version": "2.0" } }""")));
        // right magic, wrong version
        byte[] v1 = Quad(); v1[4] = 1;
        Assert.Contains("version 1", Refused(v1));
        // right container, JSON that is not JSON
        Assert.Contains("JSON", Refused(Pack("{ this is not json", new byte[4])));
        Assert.Contains("JSON", Refused(Pack("    ", new byte[4])));
    }

    [Fact]
    public void A_truncated_file_is_refused_wherever_it_was_cut()
    {
        byte[] whole = Quad();
        Ok(whole);
        for (int length = 0; length < whole.Length; length++)
        {
            string error = Refused(whole.Take(length).ToArray());
            if (length >= 12) Assert.Contains("truncated", error);
        }

        // cut, and the header's length patched to match: now it is the BIN chunk that claims more than there is
        byte[] cut = whole.Take(whole.Length - 8).ToArray();
        BitConverter.GetBytes((uint)cut.Length).CopyTo(cut, 8);
        Assert.Contains("truncated", Refused(cut));

        // a whole container whose vertex data is shorter than its accessors say
        var g = new Glb();
        int pos = g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        byte[] shortData = g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}} } } ] } ] }
            """, root => root["accessors"][0]["count"] = 4);
        Assert.Contains("past the end", Refused(shortData));
    }

    [Theory]
    [InlineData("KHR_draco_mesh_compression", "Draco")]
    [InlineData("EXT_meshopt_compression", "meshopt")]
    public void A_file_that_requires_mesh_compression_is_refused(string extension, string word)
    {
        string error = Refused(Quad($$""", "extensionsUsed": ["{{extension}}"], "extensionsRequired": ["{{extension}}"] """));
        Assert.Contains(word, error);
        Assert.Contains(extension, error);
        // an extension that only changes details does not stop the file from being read
        Ok(Quad(""", "extensionsUsed": ["KHR_materials_unlit"], "extensionsRequired": ["KHR_materials_unlit"] """));
    }

    [Fact]
    public void A_file_without_triangles_is_refused()
    {
        var g = new Glb();
        int pos = g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        // only a line strip: skipped, and then nothing is left
        Assert.Contains("no triangles", Refused(g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}} }, "mode": 3 } ] } ] }
            """)));
        // a mesh that no node of the scene shows
        Assert.Contains("no triangles", Refused(g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "name": "empty" } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}} } } ] } ] }
            """)));
        Assert.Contains("no triangles", Refused(Pack("""{ "asset": { "version": "2.0" } }""", null)));
    }

    [Fact]
    public void Lines_and_points_are_skipped_while_the_triangles_beside_them_are_kept()
    {
        var g = new Glb();
        int pos = g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var m = Ok(g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}} }, "mode": 1 },
                                            { "attributes": { "POSITION": {{pos}} } },
                                            { "attributes": { "POSITION": {{pos}} }, "mode": 0 } ] } ] }
            """));
        Assert.Equal(1, m.primitives);
        Assert.Equal(3, m.positions.Length / 3);
    }

    [Fact]
    public void Sparse_geometry_and_out_of_range_indices_are_refused()
    {
        var g = new Glb();
        int pos = g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        int idx = g.UShorts("SCALAR", false, 0, 1, 3);
        string scene = $$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}} }, "indices": {{idx}} } ] } ] }
            """;
        Assert.Contains("does not exist", Refused(g.Build(scene)));
        Assert.Contains("sparse", Refused(g.Build(scene, root =>
            root["accessors"][0]["sparse"] = JObject.Parse("""{ "count": 1, "indices": { "bufferView": 1, "componentType": 5123 }, "values": { "bufferView": 0 } }"""))));
    }

    [Fact]
    public void Damaged_files_never_throw()
    {
        byte[] whole = Textured(view => $$"""
            "materials": [ { "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 }, "baseColorFactor": [1, 1, 1, 1] } }, { } ],
            "textures": [ { "source": 0 } ],
            "images": [ { "bufferView": {{view}} } ]
            """, Png);
        Ok(whole);
        var random = new Random(20261116);
        for (int round = 0; round < 4000; round++)
        {
            byte[] damaged = (byte[])whole.Clone();
            int hits = 1 + random.Next(4);
            // from byte 12 on: the header's own damage is covered above, and here the damage should reach the JSON and the data
            for (int h = 0; h < hits; h++) damaged[12 + random.Next(damaged.Length - 12)] = (byte)random.Next(256);
            var m = GlbReader.Read(damaged, out string error);   // must come back, one way or the other
            if (m == null) Assert.False(string.IsNullOrWhiteSpace(error));
            else
            {
                Assert.Null(error);
                Assert.Equal(m.positions.Length, m.normals.Length);
                Assert.All(m.indices, i => Assert.InRange(i, 0, m.positions.Length / 3 - 1));
            }
        }
    }

    // ---- (6) Normalize -------------------------------------------------------------------------------

    [Fact]
    public void Normalize_stands_the_model_on_the_origin_at_the_asked_size()
    {
        var g = new Glb();
        // a box-shaped cloud of corners: 4 wide (x 10..14), 8 tall (y -3..5), 2 deep (z 7..9)
        int pos = g.Floats("VEC3", 10, -3, 7, 14, -3, 7, 10, 5, 9, 14, 5, 9, 12, 1, 8, 10, -3, 9);
        var m = Ok(g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{pos}} } } ] } ] }
            """));
        Assert.Equal(new Vector3(-14, -3, 7), m.min);
        Assert.Equal(new Vector3(-10, 5, 9), m.max);
        float[] normalsBefore = (float[])m.normals.Clone();

        GlbReader.Normalize(m, 0.4f);

        // the longest side (the height, 8) is now 0.4: everything is 1/20 of its size
        Near(new float[] { -0.1f, 0, -0.05f }, new[] { m.min.X, m.min.Y, m.min.Z }, 1e-6f);
        Near(new float[] { 0.1f, 0.4f, 0.05f }, new[] { m.max.X, m.max.Y, m.max.Z }, 1e-6f);
        // Unity x = -glTF x, so glTF (10,-3,7) is the corner at +x, bottom, front
        Near(new float[] { 0.1f, 0, -0.05f, -0.1f, 0, -0.05f, 0.1f, 0.4f, 0.05f, -0.1f, 0.4f, 0.05f, 0, 0.2f, 0, 0.1f, 0, 0.05f }, m.positions, 1e-6f);
        Assert.Equal(normalsBefore, m.normals);

        // doing it again changes nothing, and a different size only rescales about the same foot point
        GlbReader.Normalize(m, 0.4f);
        Near(new float[] { 0.1f, 0.4f, 0.05f }, new[] { m.max.X, m.max.Y, m.max.Z }, 1e-6f);
        GlbReader.Normalize(m, 2f);
        Near(new float[] { -0.5f, 0, -0.25f }, new[] { m.min.X, m.min.Y, m.min.Z }, 1e-5f);
        Near(new float[] { 0.5f, 2f, 0.25f }, new[] { m.max.X, m.max.Y, m.max.Z }, 1e-5f);

        GlbReader.Normalize(null, 1f);   // nothing to do, and no exception
    }

    // ---- a model of the size the reader is meant for ---------------------------------------------------

    [Fact]
    public void A_fifty_thousand_triangle_model_is_read_whole()
    {
        // a bumpy sheet of 161 x 161 vertices = 51,200 triangles, uint indices, no normals
        const int n = 161;
        var pos = new float[n * n * 3];
        var uv = new float[n * n * 2];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int v = z * n + x;
                pos[v * 3] = x * 0.01f; pos[v * 3 + 1] = 0.02f * MathF.Sin(x * 0.2f) * MathF.Cos(z * 0.15f); pos[v * 3 + 2] = z * 0.01f;
                uv[v * 2] = x / (n - 1f); uv[v * 2 + 1] = z / (n - 1f);
            }
        var idx = new List<uint>();
        for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                uint a = (uint)(z * n + x), b = a + 1, c = a + n, d = c + 1;
                idx.AddRange(new[] { a, c, b, b, c, d });   // counter-clockwise seen from above, as glTF wants
            }
        var g = new Glb();
        int p = g.Floats("VEC3", pos), t = g.Floats("VEC2", uv), i = g.UInts("SCALAR", idx.ToArray());
        var m = Ok(g.Build($$"""
            { "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": {{p}}, "TEXCOORD_0": {{t}} }, "indices": {{i}} } ] } ] }
            """));
        Assert.Equal(n * n, m.positions.Length / 3);
        Assert.Equal(51200 * 3, m.indices.Length);
        for (int v = 0; v < n * n; v++)
        {
            Vector3 normal = V(m.normals, v);
            Assert.True(Math.Abs(normal.Length() - 1f) < 1e-4f && normal.Y > 0.5f, $"normal {v} = {normal}");
        }
        AssertWindingAgreesWithNormals(m);
    }
}
