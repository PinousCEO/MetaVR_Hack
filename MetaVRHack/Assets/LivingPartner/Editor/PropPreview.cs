using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEditor;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// Authoring-time look at the things the language model sculpts out of simple parts: reads ItemDef JSON files (as
    /// the host writes them with `LivingPartner.Host.exe --sculpt "words" --out file.json`), builds each with the mesh
    /// builder the headset uses (PropMeshes.BuildParts) and renders them side by side, with the prop shader, into one
    /// PNG. Edit mode only; the open scene is not touched and nothing is saved as an asset: the camera and the objects
    /// it needs are hidden, temporary, and destroyed again before it returns.
    /// </summary>
    public static class PropPreview
    {
        const string ShaderPath = "Assets/LivingPartner/Resources/Shaders/LPProp.shader";
        static readonly Vector3 Origin = new Vector3(600, 0, 0);        // far from anything in the open scene
        static readonly Color Paper = new Color(0.97f, 0.94f, 0.88f, 1f), Ink = new Color(0.29f, 0.19f, 0.16f, 1f);

        [MenuItem("LivingPartner/Render Sculpt Sample Sheet")]
        public static void RenderSamples()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            Debug.Log(RenderSheet(Path.Combine(repo, "artifacts", "sculpt-samples"), Path.Combine(repo, "artifacts", "sculpt-sheet.png")));
        }

        /// <summary>
        /// Every ItemDef JSON file of a folder (or the one file), in the order of their names, in a three-quarter view
        /// from the front right, its name under each. A thing without parts is shown as the built-in shape of its kind.
        /// Returns one line per thing (parts, triangles, height), or what went wrong.
        /// </summary>
        public static string RenderSheet(string jsonFolderOrFile, string pngPath) => RenderSheet(jsonFolderOrFile, pngPath, 35f, 440);

        /// <summary>yaw: from how far to the right of the front the things are looked at, in degrees (180 = from behind). cell: pixels per thing.</summary>
        public static string RenderSheet(string jsonFolderOrFile, string pngPath, float yaw, int cell)
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string source = Path.IsPathRooted(jsonFolderOrFile) ? jsonFolderOrFile : Path.Combine(repo, jsonFolderOrFile);
            string target = Path.IsPathRooted(pngPath) ? pngPath : Path.Combine(repo, pngPath);
            var files = new List<string>();
            if (Directory.Exists(source)) { files.AddRange(Directory.GetFiles(source, "*.json")); files.Sort(StringComparer.OrdinalIgnoreCase); }
            else if (File.Exists(source)) files.Add(source);
            // (the answers of the model as they came are kept next to the things, for reading: they are not things)
            files.RemoveAll(f => f.EndsWith(".raw.json", StringComparison.OrdinalIgnoreCase));
            if (files.Count == 0) return "nothing to render: no .json file at " + source;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) return "the prop shader is missing: " + ShaderPath;

            int cols = files.Count <= 4 ? files.Count : Mathf.CeilToInt(Mathf.Sqrt(files.Count * 4f / 3f)), rows = (files.Count + cols - 1) / cols;
            var report = new StringBuilder();
            var made = new List<UnityEngine.Object>();          // everything created here, destroyed at the end whatever happens
            try
            {
                var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGB24, false); made.Add(sheet);
                var fill = new Color32[sheet.width * sheet.height]; Color32 paper = Paper;
                for (int i = 0; i < fill.Length; i++) fill[i] = paper;
                sheet.SetPixels32(fill);

                var camGo = new GameObject("PropPreviewCamera") { hideFlags = HideFlags.HideAndDontSave }; made.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Paper; cam.orthographic = true; cam.nearClipPlane = 0.05f; cam.farClipPlane = 12f; cam.enabled = false;
                // the whole of a thing one unit long, with room for its name underneath
                cam.orthographicSize = 0.80f;
                Vector3 look = Origin + new Vector3(0, 0.36f, 0);
                const float pitch = 24f;
                float ya = yaw * Mathf.Deg2Rad, pa = pitch * Mathf.Deg2Rad;
                camGo.transform.position = look + new Vector3(Mathf.Sin(ya) * Mathf.Cos(pa), Mathf.Sin(pa), -Mathf.Cos(ya) * Mathf.Cos(pa)) * 4f;
                camGo.transform.rotation = Quaternion.LookRotation(look - camGo.transform.position, Vector3.up);

                var propGo = new GameObject("PropPreviewThing") { hideFlags = HideFlags.HideAndDontSave }; made.Add(propGo);
                propGo.transform.position = Origin;
                var filter = propGo.AddComponent<MeshFilter>(); var renderer = propGo.AddComponent<MeshRenderer>();
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; made.Add(material);
                renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock();
                block.SetTexture("_MainTex", Texture2D.whiteTexture); block.SetColor("_Color", Color.white); block.SetFloat("_Ghost", 0f); block.SetFloat("_Fill", 2f);
                renderer.SetPropertyBlock(block);

                // a soft shadow on the ground it stands on, so that it does not hang in the paper
                var shadowShader = Shader.Find("Sprites/Default");
                GameObject shadowGo = null;
                if (shadowShader != null)
                {
                    shadowGo = new GameObject("PropPreviewShadow") { hideFlags = HideFlags.HideAndDontSave }; made.Add(shadowGo);
                    var shadowMesh = ShadowDisc(); made.Add(shadowMesh);
                    var shadowMat = new Material(shadowShader) { hideFlags = HideFlags.HideAndDontSave }; made.Add(shadowMat);
                    shadowGo.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
                    shadowGo.AddComponent<MeshRenderer>().sharedMaterial = shadowMat;
                }

                // its name: a font of the system that has Korean letters
                var labelGo = new GameObject("PropPreviewLabel") { hideFlags = HideFlags.HideAndDontSave }; made.Add(labelGo);
                labelGo.transform.SetParent(camGo.transform, false);
                labelGo.transform.localPosition = new Vector3(0, -0.69f, 1f);
                var font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Segoe UI", "Arial" }, 48); made.Add(font);
                var label = labelGo.AddComponent<TextMesh>();
                label.font = font; label.fontSize = 48; label.characterSize = 0.0135f; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = Ink;
                labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

                var rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; made.Add(rt);
                cam.targetTexture = rt;
                for (int k = 0; k < files.Count; k++)
                {
                    string file = Path.GetFileNameWithoutExtension(files[k]);
                    ItemDef item = null;
                    try { item = Proto.FromJson<ItemDef>(File.ReadAllText(files[k], Encoding.UTF8)); } catch { }
                    if (item == null) { report.AppendLine($"{file}: not an ItemDef"); continue; }
                    bool sculpted = item.parts != null && item.parts.Count > 0;
                    float height = 1f;
                    Mesh mesh = sculpted ? PropMeshes.BuildParts(item.parts, out height) : null;
                    if (mesh == null) { sculpted = false; PropMeshes.Measures(item.kind, out height, out _); mesh = PropMeshes.Build(item.kind, item.color); }
                    made.Add(mesh);
                    filter.sharedMesh = mesh;
                    if (shadowGo != null)
                    {
                        var bounds = mesh.bounds;
                        shadowGo.transform.position = Origin + new Vector3(bounds.center.x, 0.002f, bounds.center.z);
                        shadowGo.transform.localScale = new Vector3(bounds.extents.x * 1.25f + 0.05f, 1f, bounds.extents.z * 1.25f + 0.05f);
                    }
                    string name = string.IsNullOrEmpty(item.name) ? file : item.name;
                    label.text = sculpted ? name : name + " (built-in " + item.kind + ")";
                    font.RequestCharactersInTexture(label.text, 48);
                    cam.Render(); cam.Render();         // (the first render after a text has changed can miss its letters)
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, cell, cell), (k % cols) * cell, (rows - 1 - k / cols) * cell);
                    RenderTexture.active = null;
                    report.AppendLine($"{file}: '{name}' {(sculpted ? item.parts.Count + " parts" : "built-in " + item.kind)}, {mesh.triangles.Length / 3} triangles, height {height:0.00}" +
                                      (sculpted && (item.seat > 0 || item.top > 0) ? $", seat {item.seat:0.00} top {item.top:0.00}" : ""));
                }
                cam.targetTexture = null;
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, sheet.EncodeToPNG());
                report.Insert(0, $"{files.Count} thing(s) -> {target} ({sheet.width}x{sheet.height})\n");
            }
            catch (Exception e) { report.AppendLine("failed: " + e); }
            finally
            {
                RenderTexture.active = null;
                // (objects before the assets they use; a camera lets go of its target when it goes)
                made.Sort((a, b) => (a is GameObject ? 0 : 1).CompareTo(b is GameObject ? 0 : 1));
                foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
            return report.ToString();
        }

        /// <summary>A flat round patch, dark in the middle and fading to nothing at its rim (radius 1, lying on the ground).</summary>
        static Mesh ShadowDisc()
        {
            const int seg = 28;
            float[] radius = { 0f, 0.55f, 1f }, alpha = { 0.26f, 0.20f, 0f };
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int ring = 0; ring < radius.Length; ring++)
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius[ring]);
                    c.Add(new Color(Ink.r, Ink.g, Ink.b, alpha[ring]));
                }
            for (int ring = 0; ring < radius.Length - 1; ring++)
                for (int s = 0; s < seg; s++)
                {
                    int a = ring * (seg + 1) + s, b = a + seg + 1;
                    t.Add(a); t.Add(b + 1); t.Add(b); t.Add(a); t.Add(a + 1); t.Add(b + 1);      // (the sprite shader shows both sides)
                }
            var m = new Mesh { name = "PropPreview shadow", hideFlags = HideFlags.HideAndDontSave };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            return m;
        }
    }
}
