using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Shared
{
    /// <summary>
    /// A small reader for binary glTF 2.0 (.glb) files, the kind a text-to-3D service hands back.
    ///
    /// It turns the whole file into ONE triangle mesh that is ready for Unity: every primitive of the scene is
    /// baked into world space and merged, and the result is already in Unity's left-handed space. That is all a
    /// "show this model in the room" feature needs, and it keeps the Unity side to a few lines (fill a Mesh,
    /// decode one texture).
    ///
    /// This file is compiled into Unity, into the .NET host and into the tests, so it uses no Unity types.
    ///
    /// What is deliberately left out, because a static model does not need it: animation, skins (a rigged model
    /// is shown in its bind pose, at its node's transform), morph targets, cameras, lights, and every material
    /// property except the base colour. Because there is only one mesh and one texture, a model that uses
    /// several different textures is shown with the first usable one only.
    /// </summary>
    public static class GlbReader
    {
        public sealed class Model
        {
            public float[] positions;   // x,y,z per vertex, already converted to Unity's left-handed space
            public float[] normals;     // x,y,z per vertex (computed if the file has none)
            public float[] uvs;         // u,v per vertex (0,0 if none), v flipped for Unity (v' = 1 - v)
            public float[] colors;      // r,g,b,a per vertex: COLOR_0 times the primitive material's baseColorFactor (1,1,1,1 if neither)
            public int[] indices;       // triangles, winding already right for Unity
            public byte[] texture;      // the bytes (PNG or JPEG file) of the base colour texture of the first material that has one, else null
            public string textureMime;  // "image/png" / "image/jpeg" / ""
            public Vector3 min, max;    // bounds of positions
            public int primitives;      // how many primitives were merged
        }

        // The three little-endian words every GLB container is built from.
        const uint MagicGltf = 0x46546C67;  // "glTF"
        const uint ChunkJson = 0x4E4F534A;  // "JSON"
        const uint ChunkBin = 0x004E4942;   // "BIN\0"

        // glTF's numeric codes for what one number in a buffer is.
        const int CtByte = 5120, CtUByte = 5121, CtShort = 5122, CtUShort = 5123, CtUInt = 5125, CtFloat = 5126;

        const int ModeTriangles = 4;

        // Far beyond anything a headset can draw. It only exists so that a file which instances one mesh a
        // million times is refused with a sentence instead of an attempt to allocate gigabytes.
        const long MaxElements = 30000000;

        /// <summary>Reads a .glb. Returns null and a human-readable <paramref name="error"/> on failure; never throws.</summary>
        public static Model Read(byte[] glb, out string error)
        {
            // Every offset is checked before it is used, so the catch below should never be reached. It is there
            // because this runs on files that come from the internet, inside a headset, where an exception in a
            // loading coroutine is far worse than a message saying the model could not be shown.
            try
            {
                Model m = ReadCore(glb, out error);
                if (m != null) error = null;
                else if (string.IsNullOrEmpty(error)) error = "The GLB could not be read.";
                return m;
            }
            catch (JsonException e)
            {
                error = "The GLB is damaged: its JSON part cannot be parsed (" + e.Message + ").";
                return null;
            }
            catch (Exception e)
            {
                error = "The GLB is damaged or uses something this reader does not understand (" + e.GetType().Name + ": " + e.Message + ").";
                return null;
            }
        }

        /// <summary>
        /// In place: scales the model uniformly so the longest side of its bounds is <paramref name="longestSide"/>,
        /// and moves it so that the centre of the bottom face (min y) is at the origin. Updates min/max.
        /// A generated model arrives in arbitrary units around an arbitrary point; after this it can simply be
        /// put on a table or on the floor.
        /// </summary>
        public static void Normalize(Model m, float longestSide)
        {
            if (m == null || m.positions == null || m.positions.Length < 3) return;
            float[] p = m.positions;

            // Measured again rather than trusted, so this still does the right thing if the caller has edited the positions.
            Bounds(p, out Vector3 min, out Vector3 max);
            float longest = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
            // A single point (or a nonsense target size) has no size to scale; it is still moved to the origin.
            float scale = longest > 1e-12f && longestSide > 0f ? longestSide / longest : 1f;
            float cx = (min.X + max.X) * 0.5f, cy = min.Y, cz = (min.Z + max.Z) * 0.5f;

            for (int i = 0; i + 2 < p.Length; i += 3)
            {
                p[i] = (p[i] - cx) * scale;
                p[i + 1] = (p[i + 1] - cy) * scale;
                p[i + 2] = (p[i + 2] - cz) * scale;
            }
            // Normals stay as they are: a uniform scale and a move do not turn them.
            Bounds(p, out m.min, out m.max);
        }

        // ------------------------------------------------------------------------------------------------
        // The file as a whole

        /// <summary>Everything the helpers need to find data: the parsed JSON's tables and where the binary chunk sits.</summary>
        sealed class Doc
        {
            public byte[] glb;
            public int binStart, binLength = -1;        // the BIN chunk inside 'glb' (it is read in place, never copied)
            public JArray accessors, bufferViews, buffers, materials, textures, images, meshes, nodes;
            public byte[][] decodedBuffers;             // buffers given as base64 "data:" uris, decoded once
            public bool[] bufferTried, textureTried;
            public string error;                        // set when the file is broken: reading stops
            public string skipped;                      // why geometry was left out; only shown if nothing at all is left
        }

        /// <summary>One accessor, resolved down to "these bytes, this far apart". Checked to lie inside its buffer when it is made.</summary>
        struct Acc
        {
            public byte[] data;
            public int offset, stride, count, comps, compType;
            public bool normalized;
        }

        /// <summary>One drawable primitive as placed by one node (a mesh used by two nodes gives two of these).</summary>
        struct Prim
        {
            public JObject json;
            public Matrix4x4 world;
            public Acc pos, idx;
            public bool indexed;
            public int vertexCount, indexCount;
        }

        static Model ReadCore(byte[] glb, out string error)
        {
            error = null;
            var d = new Doc { glb = glb };
            JObject root = ReadContainer(d, out error);
            if (root == null) return null;

            // A file may say "you cannot show me without extension X". For these two that is simply true: the
            // vertex data is compressed and there is no plain copy to fall back on. Other required extensions
            // (materials, texture transforms, quantised vertices) only change details, so the file is still tried.
            if (root["extensionsRequired"] is JArray required)
            {
                foreach (JToken t in required)
                {
                    string name = t != null && t.Type == JTokenType.String ? (string)t : null;
                    if (name == "KHR_draco_mesh_compression")
                    {
                        error = "This GLB is Draco-compressed (KHR_draco_mesh_compression), which this reader cannot decode. Export the model without mesh compression.";
                        return null;
                    }
                    if (name == "EXT_meshopt_compression" || name == "MESHOPT_compression")
                    {
                        error = "This GLB is meshopt-compressed (" + name + "), which this reader cannot decode. Export the model without mesh compression.";
                        return null;
                    }
                }
            }

            d.accessors = root["accessors"] as JArray;
            d.bufferViews = root["bufferViews"] as JArray;
            d.buffers = root["buffers"] as JArray;
            d.materials = root["materials"] as JArray;
            d.textures = root["textures"] as JArray;
            d.images = root["images"] as JArray;
            d.meshes = root["meshes"] as JArray;
            d.nodes = root["nodes"] as JArray;
            int bufferCount = d.buffers != null ? d.buffers.Count : 0;
            d.decodedBuffers = new byte[bufferCount][];
            d.bufferTried = new bool[bufferCount];
            d.textureTried = new bool[d.textures != null ? d.textures.Count : 0];

            // Pass 1: walk the scene and note every triangle primitive with where it ends up. Knowing the totals
            // first means the big arrays are allocated once, at their final size.
            var prims = new List<Prim>();
            CollectScene(d, root, prims);
            if (d.error != null) { error = d.error; return null; }

            long vertexTotal = 0, indexTotal = 0;
            for (int i = 0; i < prims.Count; i++) { vertexTotal += prims[i].vertexCount; indexTotal += prims[i].indexCount; }
            if (prims.Count == 0 || indexTotal == 0)
            {
                error = "The GLB has no triangles to show" + (d.skipped != null ? " (" + d.skipped + ")." : ".");
                return null;
            }
            if (vertexTotal > MaxElements || indexTotal > MaxElements)
            {
                error = "The GLB is too large to show (" + vertexTotal + " vertices, " + (indexTotal / 3) + " triangles).";
                return null;
            }

            var m = new Model
            {
                positions = new float[vertexTotal * 3],
                normals = new float[vertexTotal * 3],
                uvs = new float[vertexTotal * 2],
                colors = new float[vertexTotal * 4],
                indices = new int[indexTotal],
                textureMime = "",
                primitives = prims.Count,
            };

            // Pass 2: copy each primitive into its slice of the arrays.
            int v0 = 0, i0 = 0;
            for (int i = 0; i < prims.Count; i++)
            {
                Prim p = prims[i];
                if (!FillPrimitive(d, ref p, m, v0, i0)) { error = d.error ?? "The GLB could not be read."; return null; }
                v0 += p.vertexCount;
                i0 += p.indexCount;
            }

            Bounds(m.positions, out m.min, out m.max);
            return m;
        }

        /// <summary>Checks the 12-byte header, finds the JSON and BIN chunks and parses the JSON.</summary>
        static JObject ReadContainer(Doc d, out string error)
        {
            error = null;
            byte[] glb = d.glb;
            if (glb == null || glb.Length == 0) { error = "There is no data: the GLB is empty."; return null; }
            if (glb.Length < 4 || U32(glb, 0) != MagicGltf)
            {
                // The most likely mix-up deserves its own sentence: the text flavour of the same format.
                int first = 0;
                while (first < glb.Length && (glb[first] == 0xEF || glb[first] == 0xBB || glb[first] == 0xBF || glb[first] <= 0x20)) first++;
                error = first < glb.Length && glb[first] == (byte)'{'
                    ? "This is not a binary GLB: it looks like a text .gltf (JSON) file, which this reader does not load."
                    : "This is not a GLB file: it does not start with 'glTF'.";
                return null;
            }
            if (glb.Length < 12) { error = "The GLB is truncated: it ends inside its 12-byte header."; return null; }
            uint version = U32(glb, 4);
            if (version != 2) { error = "This GLB is glTF version " + version + "; only version 2 can be read."; return null; }

            long total = U32(glb, 8);
            if (total > glb.Length)
            {
                error = "The GLB is truncated: its header says " + total + " bytes but only " + glb.Length + " arrived.";
                return null;
            }
            if (total < 12) { error = "The GLB is damaged: its header gives an impossible length (" + total + ")."; return null; }
            int end = (int)total;   // anything after the declared length is not part of the file

            int jsonStart = -1, jsonLength = 0;
            int at = 12;
            while (at < end)
            {
                if (end - at < 8) { error = "The GLB is truncated: it ends in the middle of a chunk header."; return null; }
                long length = U32(glb, at);
                uint type = U32(glb, at + 4);
                long dataStart = at + 8L;
                if (dataStart + length > end)
                {
                    error = "The GLB is truncated: a chunk says it is " + length + " bytes long but only " + (end - dataStart) + " are left.";
                    return null;
                }
                if (type == ChunkJson && jsonStart < 0) { jsonStart = (int)dataStart; jsonLength = (int)length; }
                else if (type == ChunkBin && d.binLength < 0) { d.binStart = (int)dataStart; d.binLength = (int)length; }
                // Chunks of any other type are skipped, as the format asks. A chunk's length already includes its padding.
                at = (int)(dataStart + length);
            }
            if (jsonStart < 0) { error = "The GLB is damaged: it has no JSON chunk."; return null; }

            // The JSON is padded to a multiple of four with spaces (some writers use zero bytes), and a few
            // writers put a byte-order mark in front. Neither belongs to the text.
            while (jsonLength > 0 && (glb[jsonStart + jsonLength - 1] == 0 || glb[jsonStart + jsonLength - 1] == 0x20)) jsonLength--;
            if (jsonLength >= 3 && glb[jsonStart] == 0xEF && glb[jsonStart + 1] == 0xBB && glb[jsonStart + 2] == 0xBF) { jsonStart += 3; jsonLength -= 3; }
            if (jsonLength == 0) { error = "The GLB is damaged: its JSON chunk is empty."; return null; }

            return JObject.Parse(Encoding.UTF8.GetString(glb, jsonStart, jsonLength));   // a parse error is reported by Read
        }

        // ------------------------------------------------------------------------------------------------
        // The scene: which meshes are shown, and where

        static void CollectScene(Doc d, JObject root, List<Prim> prims)
        {
            int nodeCount = d.nodes != null ? d.nodes.Count : 0;
            if (nodeCount == 0) return;

            // Which nodes to start from: the scene the file names, else its first scene, else (no scenes at all,
            // or a scene without a node list) every node that is nobody's child.
            JArray roots = null;
            if (root["scenes"] is JArray scenes && scenes.Count > 0)
            {
                int sceneIndex = AsInt(root["scene"], 0);
                if (sceneIndex < 0 || sceneIndex >= scenes.Count) sceneIndex = 0;
                roots = (scenes[sceneIndex] as JObject)?["nodes"] as JArray;
            }

            // Walked with an explicit stack: a long bone chain must not be able to overflow the call stack.
            var stackNode = new Stack<int>();
            var stackWorld = new Stack<Matrix4x4>();
            if (roots != null)
            {
                for (int i = roots.Count - 1; i >= 0; i--) { stackNode.Push(AsInt(roots[i], -1)); stackWorld.Push(Matrix4x4.Identity); }
            }
            else
            {
                var isChild = new bool[nodeCount];
                for (int i = 0; i < nodeCount; i++)
                {
                    if (!((d.nodes[i] as JObject)?["children"] is JArray kids)) continue;
                    foreach (JToken k in kids) { int c = AsInt(k, -1); if (c >= 0 && c < nodeCount) isChild[c] = true; }
                }
                for (int i = nodeCount - 1; i >= 0; i--)
                    if (!isChild[i]) { stackNode.Push(i); stackWorld.Push(Matrix4x4.Identity); }
            }

            // In a valid file the nodes form a tree, so each is reached once. Remembering which were seen costs
            // nothing and means a file whose nodes point at each other in a circle cannot loop forever.
            var seen = new bool[nodeCount];
            while (stackNode.Count > 0)
            {
                int index = stackNode.Pop();
                Matrix4x4 parentWorld = stackWorld.Pop();
                if (index < 0 || index >= nodeCount || seen[index]) continue;
                seen[index] = true;
                if (!(d.nodes[index] is JObject node)) continue;

                // System.Numerics treats vectors as rows (v * M), so "child first, then parent" reads left to right.
                Matrix4x4 world = LocalMatrix(node) * parentWorld;

                JToken mesh = node["mesh"];
                if (mesh != null && mesh.Type != JTokenType.Null)
                {
                    CollectMesh(d, AsInt(mesh, -1), world, prims);
                    if (d.error != null) return;
                }

                // Pushed last-to-first so that they come off the stack in the order the file lists them; the
                // merged mesh then has the same order as the file, which makes "the first material" mean what you expect.
                if (node["children"] is JArray children)
                    for (int i = children.Count - 1; i >= 0; i--) { stackNode.Push(AsInt(children[i], -1)); stackWorld.Push(world); }
            }
        }

        /// <summary>A node's own transform: either a ready-made matrix or translation, rotation and scale.</summary>
        static Matrix4x4 LocalMatrix(JObject node)
        {
            if (node["matrix"] is JArray m && m.Count == 16)
            {
                // glTF stores the matrix column by column for column vectors. Read in the same order into a
                // System.Numerics matrix (row by row, for row vectors) that is exactly the same transform -
                // the two conventions are each other's transpose, and so are the two storage orders.
                return new Matrix4x4(
                    AsFloat(m[0], 1), AsFloat(m[1], 0), AsFloat(m[2], 0), AsFloat(m[3], 0),
                    AsFloat(m[4], 0), AsFloat(m[5], 1), AsFloat(m[6], 0), AsFloat(m[7], 0),
                    AsFloat(m[8], 0), AsFloat(m[9], 0), AsFloat(m[10], 1), AsFloat(m[11], 0),
                    AsFloat(m[12], 0), AsFloat(m[13], 0), AsFloat(m[14], 0), AsFloat(m[15], 1));
            }

            Vector3 t = Vector3.Zero, s = Vector3.One;
            Quaternion r = Quaternion.Identity;
            if (node["translation"] is JArray ta && ta.Count >= 3) t = new Vector3(AsFloat(ta[0], 0), AsFloat(ta[1], 0), AsFloat(ta[2], 0));
            if (node["scale"] is JArray sa && sa.Count >= 3) s = new Vector3(AsFloat(sa[0], 1), AsFloat(sa[1], 1), AsFloat(sa[2], 1));
            if (node["rotation"] is JArray ra && ra.Count >= 4)
            {
                var q = new Quaternion(AsFloat(ra[0], 0), AsFloat(ra[1], 0), AsFloat(ra[2], 0), AsFloat(ra[3], 1));
                // Files round their quaternions; one that is slightly off unit length would add a small unwanted scale.
                float len = q.Length();
                if (len > 1e-6f && !float.IsNaN(len) && !float.IsInfinity(len)) r = new Quaternion(q.X / len, q.Y / len, q.Z / len, q.W / len);
            }
            // Scale first, then turn, then move.
            return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
        }

        static void CollectMesh(Doc d, int meshIndex, Matrix4x4 world, List<Prim> prims)
        {
            if (!(At(d.meshes, meshIndex)?["primitives"] is JArray list)) return;
            foreach (JToken token in list)
            {
                if (!(token is JObject pj)) continue;
                // Points, lines, strips and fans are skipped: generated models are plain triangle lists.
                if (AsInt(pj["mode"], ModeTriangles) != ModeTriangles) continue;
                if (!(pj["attributes"] is JObject attributes)) continue;

                var p = new Prim { json = pj, world = world };
                if (!Resolve(d, attributes["POSITION"], "vertex positions", out p.pos))
                {
                    if (d.error != null) return;
                    // Draco that is not marked as required normally comes with a plain copy of the data. When it
                    // does not, the positions have nowhere to come from, and the user should hear why.
                    if ((pj["extensions"] as JObject)?["KHR_draco_mesh_compression"] != null)
                        d.skipped = "its geometry is Draco-compressed, which this reader cannot decode";
                    continue;
                }
                if (p.pos.comps != 3) continue;
                p.vertexCount = p.pos.count;

                JToken indices = pj["indices"];
                if (indices != null && indices.Type != JTokenType.Null)
                {
                    if (!Resolve(d, indices, "triangle indices", out p.idx))
                    {
                        if (d.error != null) return;
                        continue;
                    }
                    if (p.idx.comps != 1 || (p.idx.compType != CtUByte && p.idx.compType != CtUShort && p.idx.compType != CtUInt)) continue;
                    p.indexed = true;
                    p.indexCount = p.idx.count - p.idx.count % 3;
                }
                else
                {
                    // No index list: the vertices are simply taken three at a time.
                    p.indexCount = p.vertexCount - p.vertexCount % 3;
                }
                if (p.indexCount == 0) continue;
                prims.Add(p);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // One primitive into the merged arrays

        static bool FillPrimitive(Doc d, ref Prim p, Model m, int v0, int i0)
        {
            int vc = p.vertexCount;
            var attributes = (JObject)p.json["attributes"];
            Matrix4x4 w = p.world;

            // --- positions: file -> world -> Unity. glTF is right-handed and Unity left-handed; negating X is
            // the usual way across (it keeps "up" up and the model facing the same way as in other viewers).
            float[] pos = m.positions;
            ReadFloats(p.pos, pos, v0 * 3, 3, vc);
            for (int i = v0 * 3, e = (v0 + vc) * 3; i < e; i += 3)
            {
                float x = pos[i], y = pos[i + 1], z = pos[i + 2];
                pos[i] = -(x * w.M11 + y * w.M21 + z * w.M31 + w.M41);
                pos[i + 1] = x * w.M12 + y * w.M22 + z * w.M32 + w.M42;
                pos[i + 2] = x * w.M13 + y * w.M23 + z * w.M33 + w.M43;
            }

            // --- triangles. Mirroring the model (the X flip above) turns every triangle inside out, so each one
            // is listed the other way round to face outwards again. A node whose own transform mirrors (a negative
            // scale) has already turned its triangles once, so for those the two flips cancel.
            bool nodeMirrors = w.GetDeterminant() < 0f;
            if (!WriteIndices(ref p, m.indices, i0, v0, !nodeMirrors))
            {
                d.error = "The GLB is damaged: a triangle refers to a vertex that does not exist.";
                return false;
            }

            // --- normals
            bool haveNormals = false;
            if (Resolve(d, attributes["NORMAL"], "normals", out Acc a) && a.comps == 3 && a.count >= vc)
            {
                ReadFloats(a, m.normals, v0 * 3, 3, vc);
                TransformNormals(m.normals, v0, vc, w);
                haveNormals = true;
            }
            else if (d.error != null) return false;
            // Computed from the finished triangles, so they need no transforming of their own.
            if (!haveNormals) ComputeNormals(pos, m.normals, m.indices, v0, vc, i0, p.indexCount);

            // --- material: only its base colour matters here
            float cr = 1f, cg = 1f, cb = 1f, ca = 1f;
            int uvSet = 0;
            var pbr = At(d.materials, AsInt(p.json["material"], -1))?["pbrMetallicRoughness"] as JObject;
            if (pbr != null)
            {
                if (pbr["baseColorFactor"] is JArray f && f.Count >= 3)
                {
                    cr = AsFloat(f[0], 1); cg = AsFloat(f[1], 1); cb = AsFloat(f[2], 1);
                    if (f.Count >= 4) ca = AsFloat(f[3], 1);
                }
                if (pbr["baseColorTexture"] is JObject tex)
                {
                    // A material may say its texture uses the second set of uvs; then that is the set worth keeping.
                    uvSet = Math.Max(0, AsInt(tex["texCoord"], 0));
                    // "First material that has one" means: first in the order the primitives were merged, and
                    // one whose image really is in this file as a PNG or JPEG. A texture that cannot be used is
                    // passed over so that a later, usable one still gets its chance.
                    if (m.texture == null) LoadTexture(d, AsInt(tex["index"], -1), m);
                }
            }

            // --- uvs: glTF counts v from the top of the image, Unity from the bottom
            if (Resolve(d, attributes[uvSet == 0 ? "TEXCOORD_0" : "TEXCOORD_" + uvSet], "texture coordinates", out a) && a.comps == 2 && a.count >= vc)
            {
                float[] uv = m.uvs;
                ReadFloats(a, uv, v0 * 2, 2, vc);
                for (int i = v0 * 2 + 1, e = (v0 + vc) * 2; i < e; i += 2) uv[i] = 1f - uv[i];
            }
            else if (d.error != null) return false;

            // --- colours: the vertex colour (white if there is none) times the material's colour, so that a
            // model coloured only through its materials still shows its colours on the one merged mesh.
            float[] col = m.colors;
            int c0 = v0 * 4, c1 = (v0 + vc) * 4;
            for (int i = c0; i < c1; i++) col[i] = 1f;
            if (Resolve(d, attributes["COLOR_0"], "vertex colours", out a) && (a.comps == 3 || a.comps == 4) && a.count >= vc)
            {
                // Whole-number colours are always fractions of their largest value; some writers forget to say so.
                if (a.compType != CtFloat) a.normalized = true;
                ReadFloats(a, col, c0, 4, vc);   // rgb-only colours leave the alpha at the 1 written just above
            }
            else if (d.error != null) return false;
            if (cr != 1f || cg != 1f || cb != 1f || ca != 1f)
                for (int i = c0; i < c1; i += 4) { col[i] *= cr; col[i + 1] *= cg; col[i + 2] *= cb; col[i + 3] *= ca; }

            return true;
        }

        /// <summary>Turns normals read from the file into world space and then into Unity space, in place.</summary>
        static void TransformNormals(float[] n, int v0, int vc, Matrix4x4 w)
        {
            // A normal is turned by the node's rotation. Taking the inverse of the matrix (and reading it
            // transposed, below) is that same rotation whenever the scale is the same on all axes, and it also
            // stays correct when it is not - a squashed sphere's normals must not be squashed the same way.
            // A matrix that cannot be inverted (a scale of zero) has no meaningful normals; the plain matrix
            // is used so the numbers at least stay finite.
            Matrix4x4 inv;
            float a11, a12, a13, a21, a22, a23, a31, a32, a33;
            if (Matrix4x4.Invert(w, out inv))
            {
                a11 = inv.M11; a12 = inv.M21; a13 = inv.M31;
                a21 = inv.M12; a22 = inv.M22; a23 = inv.M32;
                a31 = inv.M13; a32 = inv.M23; a33 = inv.M33;
            }
            else
            {
                a11 = w.M11; a12 = w.M12; a13 = w.M13;
                a21 = w.M21; a22 = w.M22; a23 = w.M23;
                a31 = w.M31; a32 = w.M32; a33 = w.M33;
            }
            for (int i = v0 * 3, e = (v0 + vc) * 3; i < e; i += 3)
            {
                float x = n[i], y = n[i + 1], z = n[i + 2];
                float nx = x * a11 + y * a21 + z * a31;
                float ny = x * a12 + y * a22 + z * a32;
                float nz = x * a13 + y * a23 + z * a33;
                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-20f && !float.IsInfinity(len)) { float k = 1f / len; n[i] = -nx * k; n[i + 1] = ny * k; n[i + 2] = nz * k; }   // -x: into Unity space
                else { n[i] = 0f; n[i + 1] = 1f; n[i + 2] = 0f; }   // a zero normal in the file: any unit vector beats NaN
            }
        }

        /// <summary>Smooth normals for a primitive that came without any, from its finished (Unity-space) triangles.</summary>
        static void ComputeNormals(float[] pos, float[] nrm, int[] idx, int v0, int vc, int i0, int ic)
        {
            Array.Clear(nrm, v0 * 3, vc * 3);
            for (int t = i0, e = i0 + ic; t + 2 < e; t += 3)
            {
                int a = idx[t] * 3, b = idx[t + 1] * 3, c = idx[t + 2] * 3;
                float ux = pos[b] - pos[a], uy = pos[b + 1] - pos[a + 1], uz = pos[b + 2] - pos[a + 2];
                float vx = pos[c] - pos[a], vy = pos[c + 1] - pos[a + 1], vz = pos[c + 2] - pos[a + 2];
                // For a triangle wound the way Unity wants it, this cross product points out of its front. Its
                // length is twice the triangle's area, so adding it unnormalised lets big triangles count for more.
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                nrm[a] += nx; nrm[a + 1] += ny; nrm[a + 2] += nz;
                nrm[b] += nx; nrm[b + 1] += ny; nrm[b + 2] += nz;
                nrm[c] += nx; nrm[c + 1] += ny; nrm[c + 2] += nz;
            }
            for (int i = v0 * 3, e = (v0 + vc) * 3; i < e; i += 3)
            {
                float x = nrm[i], y = nrm[i + 1], z = nrm[i + 2];
                float len = (float)Math.Sqrt(x * x + y * y + z * z);
                if (len > 1e-20f && !float.IsInfinity(len)) { float k = 1f / len; nrm[i] = x * k; nrm[i + 1] = y * k; nrm[i + 2] = z * k; }
                else { nrm[i] = 0f; nrm[i + 1] = 1f; nrm[i + 2] = 0f; }   // a vertex no real triangle uses
            }
        }

        /// <summary>Copies a primitive's triangles, shifted to where its vertices sit in the merged mesh. False if an index is out of range.</summary>
        static bool WriteIndices(ref Prim p, int[] dst, int at, int baseVertex, bool reverse)
        {
            int n = p.indexCount;
            // Where the second and third corner of each triangle go: swapped when the triangle has to be turned over.
            int second = reverse ? 2 : 1, third = reverse ? 1 : 2;

            if (!p.indexed)
            {
                for (int i = 0; i < n; i += 3)
                {
                    dst[at + i] = baseVertex + i;
                    dst[at + i + second] = baseVertex + i + 1;
                    dst[at + i + third] = baseVertex + i + 2;
                }
                return true;
            }

            byte[] d = p.idx.data;
            int o = p.idx.offset, stride = p.idx.stride, type = p.idx.compType;
            uint limit = (uint)p.vertexCount;
            for (int i = 0; i < n; i += 3)
            {
                uint a = Index(d, o + i * stride, type), b = Index(d, o + (i + 1) * stride, type), c = Index(d, o + (i + 2) * stride, type);
                // Checked here, once, so that nothing downstream (the normals above, Unity's mesh upload) can be
                // sent outside the vertex arrays by a damaged file.
                if (a >= limit || b >= limit || c >= limit) return false;
                dst[at + i] = baseVertex + (int)a;
                dst[at + i + second] = baseVertex + (int)b;
                dst[at + i + third] = baseVertex + (int)c;
            }
            return true;
        }

        static uint Index(byte[] d, int o, int type)
        {
            if (type == CtUShort) return (uint)(d[o] | d[o + 1] << 8);
            if (type == CtUByte) return d[o];
            return U32(d, o);
        }

        // ------------------------------------------------------------------------------------------------
        // Textures

        static void LoadTexture(Doc d, int textureIndex, Model m)
        {
            if (d.textures == null || textureIndex < 0 || textureIndex >= d.textures.Count) return;
            if (d.textureTried[textureIndex]) return;   // many primitives usually share one texture: look at it once
            d.textureTried[textureIndex] = true;

            // A texture whose only image is WebP or KTX2 keeps it under an extension and has no plain "source";
            // Unity could not decode those at run time anyway, so such a texture counts as not there.
            JObject image = At(d.images, AsInt(At(d.textures, textureIndex)?["source"], -1));
            if (image == null) return;

            byte[] bytes = null;
            JObject view = At(d.bufferViews, AsInt(image["bufferView"], -1));
            if (view != null)
            {
                // The normal case in a GLB: the image file sits whole inside the binary chunk.
                if (GetBuffer(d, AsInt(view["buffer"], -1), out byte[] data, out int start, out int length))
                {
                    long offset = AsInt(view["byteOffset"], 0), size = AsInt(view["byteLength"], 0);
                    if (offset >= 0 && size > 0 && offset + size <= length)
                    {
                        // Copied out, because the caller hands it to an image decoder and may let go of the GLB.
                        bytes = new byte[size];
                        Buffer.BlockCopy(data, (int)(start + offset), bytes, 0, (int)size);
                    }
                }
            }
            else
            {
                // Inline base64 works; a file name next to the .glb cannot, because all we were given is bytes.
                bytes = DecodeDataUri(AsString(image["uri"]));
            }

            // The first bytes decide, not the file's "mimeType" label, which is optional and sometimes wrong.
            string mime = SniffImage(bytes);
            if (mime.Length == 0) return;
            m.texture = bytes;
            m.textureMime = mime;
        }

        static string SniffImage(byte[] b)
        {
            if (b == null) return "";
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            return "";
        }

        /// <summary>The bytes of a "data:...;base64,..." uri, or null for anything else (a file name, a web address, bad base64).</summary>
        static byte[] DecodeDataUri(string uri)
        {
            if (uri == null || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
            int comma = uri.IndexOf(',');
            if (comma < 0 || uri.IndexOf(";base64", 0, comma, StringComparison.OrdinalIgnoreCase) < 0) return null;
            try { return Convert.FromBase64String(uri.Substring(comma + 1)); }
            catch (FormatException) { return null; }
        }

        // ------------------------------------------------------------------------------------------------
        // Buffers and accessors

        /// <summary>Where a buffer's bytes are. False when they are not in this file (an external .bin) or the index is bad.</summary>
        static bool GetBuffer(Doc d, int index, out byte[] data, out int start, out int length)
        {
            data = null; start = 0; length = 0;
            JObject buffer = At(d.buffers, index);
            if (buffer == null) return false;

            string uri = AsString(buffer["uri"]);
            if (uri == null)
            {
                // Only the first buffer may stand for the BIN chunk. Any other buffer without a uri is a
                // placeholder that some extension was supposed to fill, and so has no data we can read.
                if (index != 0 || d.binLength < 0) return false;
                data = d.glb; start = d.binStart; length = d.binLength;
            }
            else
            {
                if (!d.bufferTried[index]) { d.bufferTried[index] = true; d.decodedBuffers[index] = DecodeDataUri(uri); }
                data = d.decodedBuffers[index];
                if (data == null) return false;
                length = data.Length;
            }
            // The chunk may be longer than the buffer because of padding; it must never be treated as longer than it is.
            int declared = AsInt(buffer["byteLength"], -1);
            if (declared >= 0 && declared < length) length = declared;
            return true;
        }

        /// <summary>
        /// Looks up an accessor and checks that all of it lies inside its buffer, so the copying loops need no checks.
        /// Returns false with d.error left alone when the attribute is simply not there or not usable (the caller
        /// then does without it), and false with d.error set when the file is broken.
        /// </summary>
        static bool Resolve(Doc d, JToken indexToken, string what, out Acc a)
        {
            a = default(Acc);
            JObject acc = At(d.accessors, AsInt(indexToken, -1));
            if (acc == null) return false;

            // Sparse accessors ("these values, except for the following few") are REFUSED WITH AN ERROR when
            // they hold something we draw. Ignoring the exceptions would show a subtly wrong model with no
            // hint as to why; they are rare (mostly morph targets, which are not read at all) and no
            // text-to-3D service writes them for plain geometry. A sparse accessor we never look at is harmless.
            JToken sparse = acc["sparse"];
            if (sparse != null && sparse.Type != JTokenType.Null)
            {
                d.error = "The GLB stores its " + what + " in a sparse accessor, which this reader does not support.";
                return false;
            }

            int count = AsInt(acc["count"], 0);
            int comps = ComponentCount(AsString(acc["type"]));
            int compType = AsInt(acc["componentType"], 0);
            int compSize = ComponentSize(compType);
            if (count <= 0 || comps == 0 || compSize == 0) return false;

            // An accessor without a buffer view means "all zeros" - nothing worth drawing or keeping.
            JObject view = At(d.bufferViews, AsInt(acc["bufferView"], -1));
            if (view == null) return false;
            if (!GetBuffer(d, AsInt(view["buffer"], -1), out byte[] data, out int bufferStart, out int bufferLength))
            {
                d.skipped = "its " + what + " are in a separate file that was not given";
                return false;
            }

            long viewOffset = AsInt(view["byteOffset"], 0), viewLength = AsInt(view["byteLength"], 0), accOffset = AsInt(acc["byteOffset"], 0);
            int element = comps * compSize;
            // A stride lets several attributes share one block of bytes, vertex after vertex. Without one the values sit back to back.
            int stride = AsInt(view["byteStride"], 0);
            if (stride <= 0) stride = element;
            long needed = (long)(count - 1) * stride + element;
            if (stride < element || viewOffset < 0 || viewLength < 0 || accOffset < 0 || viewOffset + viewLength > bufferLength || accOffset + needed > viewLength)
            {
                d.error = "The GLB is truncated or damaged: its " + what + " run past the end of the data.";
                return false;
            }

            a.data = data;
            a.offset = (int)(bufferStart + viewOffset + accOffset);
            a.stride = stride;
            a.count = count;
            a.comps = comps;
            a.compType = compType;
            a.normalized = acc["normalized"] is JValue nv && nv.Type == JTokenType.Boolean && (bool)nv;
            return true;
        }

        static int ComponentCount(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                default: return 0;   // matrices are never vertex data we read
            }
        }

        static int ComponentSize(int compType)
        {
            switch (compType)
            {
                case CtByte: case CtUByte: return 1;
                case CtShort: case CtUShort: return 2;
                case CtUInt: case CtFloat: return 4;
                default: return 0;
            }
        }

        /// <summary>
        /// Copies <paramref name="count"/> elements of an accessor into a float array that holds
        /// <paramref name="dstComps"/> floats per element. If the accessor has fewer components than that
        /// (rgb into rgba), the remaining floats are left as the caller prepared them.
        /// </summary>
        static void ReadFloats(Acc a, float[] dst, int dstStart, int dstComps, int count)
        {
            int n = Math.Min(a.comps, dstComps);
            byte[] d = a.data;

            if (a.compType == CtFloat)
            {
                // The common case by far - tightly packed floats - is one block copy. GLB is little-endian, and
                // so is every machine this runs on (PCs, Quest); the check keeps a big-endian one correct anyway.
                if (BitConverter.IsLittleEndian && a.comps == dstComps && a.stride == dstComps * 4)
                {
                    Buffer.BlockCopy(d, a.offset, dst, dstStart * 4, count * dstComps * 4);
                    return;
                }
                for (int i = 0; i < count; i++)
                {
                    int o = a.offset + i * a.stride, w = dstStart + i * dstComps;
                    for (int c = 0; c < n; c++) dst[w + c] = Single(d, o + c * 4);
                }
                return;
            }

            // Whole numbers: uvs and colours packed into bytes or shorts to save space. "Normalized" means the
            // number is a fraction of the largest value the type can hold.
            int type = a.compType, size = ComponentSize(type);
            bool norm = a.normalized;
            for (int i = 0; i < count; i++)
            {
                int o = a.offset + i * a.stride, w = dstStart + i * dstComps;
                for (int c = 0; c < n; c++) dst[w + c] = Integer(d, o + c * size, type, norm);
            }
        }

        static float Integer(byte[] d, int o, int type, bool norm)
        {
            switch (type)
            {
                case CtUByte: return norm ? d[o] / 255f : d[o];
                case CtUShort: { int v = d[o] | d[o + 1] << 8; return norm ? v / 65535f : v; }
                // Signed types have one more negative value than positive ones; it is clamped so -1 stays -1.
                case CtByte: { int v = (sbyte)d[o]; return norm ? Math.Max(v / 127f, -1f) : v; }
                case CtShort: { int v = (short)(d[o] | d[o + 1] << 8); return norm ? Math.Max(v / 32767f, -1f) : v; }
                default: { uint v = U32(d, o); return norm ? (float)(v / 4294967295.0) : v; }
            }
        }

        static float Single(byte[] d, int o)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(d, o);
            // No unsafe code and no allocation: the bytes are put in order by hand and reinterpreted.
            return new FloatBits { bits = U32(d, o) }.value;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct FloatBits
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public uint bits;
            [System.Runtime.InteropServices.FieldOffset(0)] public float value;
        }

        static uint U32(byte[] d, int o)
        {
            return unchecked((uint)(d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24));
        }

        // ------------------------------------------------------------------------------------------------
        // Small helpers

        static void Bounds(float[] p, out Vector3 min, out Vector3 max)
        {
            float x0 = float.PositiveInfinity, y0 = x0, z0 = x0, x1 = float.NegativeInfinity, y1 = x1, z1 = x1;
            for (int i = 0; i + 2 < p.Length; i += 3)
            {
                float x = p[i], y = p[i + 1], z = p[i + 2];
                // Written so that a NaN (which fails every comparison) can never become a bound.
                if (x < x0) x0 = x; if (x > x1) x1 = x;
                if (y < y0) y0 = y; if (y > y1) y1 = y;
                if (z < z0) z0 = z; if (z > z1) z1 = z;
            }
            if (x0 > x1) { x0 = 0; x1 = 0; }
            if (y0 > y1) { y0 = 0; y1 = 0; }
            if (z0 > z1) { z0 = 0; z1 = 0; }
            min = new Vector3(x0, y0, z0);
            max = new Vector3(x1, y1, z1);
        }

        // The JSON comes from outside, so nothing about its shape is taken for granted: a wrong type or a
        // missing entry reads as "not there" instead of throwing.

        static JObject At(JArray array, int index)
        {
            return array != null && index >= 0 && index < array.Count ? array[index] as JObject : null;
        }

        static int AsInt(JToken t, int fallback)
        {
            if (t == null) return fallback;
            if (t.Type == JTokenType.Integer)
            {
                long v;
                try { v = (long)t; } catch (Exception) { return fallback; }   // a number too big even for a long
                return v < int.MinValue || v > int.MaxValue ? fallback : (int)v;
            }
            if (t.Type == JTokenType.Float)
            {
                double v = (double)t;   // some writers put "4.0" where a whole number belongs
                return v >= int.MinValue && v <= int.MaxValue ? (int)v : fallback;
            }
            return fallback;
        }

        static float AsFloat(JToken t, float fallback)
        {
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) return fallback;
            try { return (float)(double)t; } catch (Exception) { return fallback; }
        }

        static string AsString(JToken t)
        {
            return t != null && t.Type == JTokenType.String ? (string)t : null;
        }
    }
}
