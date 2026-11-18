using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LivingPartner.Character;
using LivingPartner.MR;
using LivingPartner.Shared;
using LivingPartner.Windows;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// Authors everything the clients show as real assets in the project: materials and meshes (Art/), prefabs (Prefabs/)
    /// and one scene per client (Scenes/). Every reference a runtime script needs is wired here into a serialized field,
    /// so the players never create objects, add components or look anything up by name.
    /// Run from the menu (LivingPartner/Author Assets, Prefabs and Scenes) or through Unity MCP; it is idempotent and keeps
    /// asset GUIDs, so it can be re-run after changing a layout value below. After it has run, the objects can equally be
    /// edited by hand in the Hierarchy / Inspector - the runtime only uses the serialized references.
    /// </summary>
    public static partial class LPSceneBuilder      // (partial: LPSceneRoom.cs authors the record picker, the pointer ring, the shows)
    {
        const string Root = "Assets/LivingPartner";
        const string ArtDir = Root + "/Art", PrefabDir = Root + "/Prefabs", SceneDir = Root + "/Scenes";
        public const string MainScene = SceneDir + "/Main.unity";
        public const string PcScene = SceneDir + "/PcCharacter.unity";
        public const string SimScene = SceneDir + "/MrSimulator.unity";
        public const string QuestScene = SceneDir + "/QuestMR.unity";
        public const string GalleryScene = SceneDir + "/CharacterGallery.unity";
        public const string MrCorePrefab = PrefabDir + "/MrCore.prefab";
        public const float PartnerHeight = 0.10f;
        const int Layer = 0;

        /// <summary>Set by the XR editor assembly: authors Scenes/QuestMR.unity (it needs the XR packages).</summary>
        public static Func<List<string>, bool> QuestSceneAuthor;
        public static string LastResult = "";

        static Material _fxMat, _bodyMat, _faceMat, _cutMat, _encodeMat, _ballMat, _spriteMat, _propMat, _ambienceMat;
        const string UiArtDir = Root + "/Art/Ui";
        static Mesh _quad, _ballMesh;
        static Font _fontRegular, _fontBold, _fontFallback;

        [MenuItem("LivingPartner/Author Assets, Prefabs and Scenes")]
        public static void BuildAll()
        {
            var problems = new List<string>();
            var log = new List<string>();
            try
            {
                foreach (var d in new[] { ArtDir, ArtDir + "/Materials", ArtDir + "/Meshes", PrefabDir, SceneDir })
                    if (!AssetDatabase.IsValidFolder(d)) { Directory.CreateDirectory(d); AssetDatabase.Refresh(); }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                CreateArt();
                CreateDemonArt();
                var partner = BuildPartner();
                var portal = BuildPortalFx();
                var sparks = BuildSparkFx();
                var timer = BuildTimerPanel();
                var mrCore = BuildMrCore(partner, portal, sparks, timer);
                log.Add("prefabs: Partner, PortalFx, SparkFx, TimerPanel, MrCore");

                BuildMainScene();
                BuildPcScene(partner, portal, sparks, problems);
                BuildSimScene(mrCore, problems);
                BuildGalleryScene(partner, problems);
                log.Add("scenes: Main, PcCharacter, MrSimulator, CharacterGallery");
                log.Add(_modelReport.TrimEnd());
                bool quest = QuestSceneAuthor != null && QuestSceneAuthor(problems);
                log.Add(quest ? "scene: QuestMR" : "scene: QuestMR NOT authored (XR editor assembly not loaded)");

                foreach (var p in new[] { "Partner", "PortalFx", "SparkFx", "TimerPanel", "MrCore" })
                    Validate(AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{p}.prefab"), p + ".prefab", problems);

                var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MainScene, true), new EditorBuildSettingsScene(PcScene, true), new EditorBuildSettingsScene(SimScene, true) };
                if (File.Exists(QuestScene)) scenes.Add(new EditorBuildSettingsScene(QuestScene, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                AssetDatabase.SaveAssets();
                EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            }
            catch (Exception e) { problems.Add("EXCEPTION " + e); }
            LastResult = (problems.Count == 0 ? "OK" : "PROBLEMS " + problems.Count) + "\n" + string.Join("\n", log) + (problems.Count > 0 ? "\n" + string.Join("\n", problems) : "\nall serialized references assigned");
            Debug.Log("[LPSceneBuilder] " + LastResult);
            try
            {
                string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                File.WriteAllText(Path.Combine(repo, "artifacts", "scene-build.txt"), DateTime.Now.ToString("s") + "\n" + LastResult);
            }
            catch { }
        }

        // ------------------------------------------------------------------ assets
        static T SaveAsset<T>(T obj, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                // CopySerialized leaves a mesh with its OLD vertex data when the vertex count changes; copy meshes through the Mesh API
                if (obj is Mesh src && existing is Mesh dst) CopyMesh(src, dst);
                else EditorUtility.CopySerialized(obj, existing);
                Object.DestroyImmediate(obj);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        static void CopyMesh(Mesh src, Mesh dst)
        {
            dst.Clear();
            dst.name = src.name; dst.indexFormat = src.indexFormat;
            dst.vertices = src.vertices; dst.normals = src.normals; dst.colors = src.colors; dst.uv = src.uv;
            if (src.uv2.Length > 0) dst.uv2 = src.uv2;
            if (src.boneWeights.Length > 0) dst.boneWeights = src.boneWeights;
            dst.bindposes = src.bindposes;
            dst.subMeshCount = src.subMeshCount;
            for (int i = 0; i < src.subMeshCount; i++) dst.SetTriangles(src.GetTriangles(i), i);
            dst.bounds = src.bounds;
        }

        static void CreateArt()
        {
            // the project's own typeface (drawn by StoryFontBuilder); the system font only stands in for characters it lacks
            var story = StoryFontBuilder.Build();
            _fontRegular = story.regular; _fontBold = story.bold;
            // Korean (what the partner says, the feedback board): Jua, an open-licensed rounded face that is shipped with the app.
            // (Before, whatever the system had stood in: thin, and different on the PC and on the headset.)
            var jua = AssetDatabase.LoadAssetAtPath<Font>(ArtDir + "/Fonts/Jua-Regular.ttf");
            _fontFallback = jua != null ? jua : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _quad = SaveAsset(MeshBuilder.Quad("LPQuad"), ArtDir + "/Meshes/LPQuad.asset");

            var fx = new Material(Shader.Find("LivingPartner/Fx")) { name = "LPFx" };
            _fxMat = SaveAsset(fx, ArtDir + "/Materials/LPFx.mat");
            var spriteShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Resources/Shaders/LPSprite.shader");
            if (spriteShader == null) throw new Exception("sprite shader not found");
            _spriteMat = SaveAsset(new Material(spriteShader) { name = "LPSprite" }, ArtDir + "/Materials/LPSprite.mat");
            var propShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Resources/Shaders/LPProp.shader");
            var ambienceShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Resources/Shaders/LPAmbience.shader");
            if (propShader == null || ambienceShader == null) throw new Exception("prop / ambience shader not found");
            _propMat = SaveAsset(new Material(propShader) { name = "LPProp" }, ArtDir + "/Materials/LPProp.mat");
            _ambienceMat = SaveAsset(new Material(ambienceShader) { name = "LPAmbience" }, ArtDir + "/Materials/LPAmbience.mat");

            var encodeShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Resources/Shaders/LPEncode.shader");
            _encodeMat = SaveAsset(new Material(encodeShader) { name = "LPEncode" }, ArtDir + "/Materials/LPEncode.mat");

            // the partner: the model's own texture on the body and on the eyes
            var albedoImporter = (TextureImporter)AssetImporter.GetAtPath(PartnerModelBuilder.AlbedoPath);
            if (albedoImporter == null) throw new Exception("character texture not found: " + PartnerModelBuilder.AlbedoPath);
            if (albedoImporter.isReadable || !albedoImporter.sRGBTexture || !albedoImporter.mipmapEnabled || albedoImporter.wrapMode != TextureWrapMode.Clamp)
            {
                albedoImporter.isReadable = false; albedoImporter.sRGBTexture = true; albedoImporter.mipmapEnabled = true;
                albedoImporter.wrapMode = TextureWrapMode.Clamp; albedoImporter.filterMode = FilterMode.Trilinear;
                albedoImporter.SaveAndReimport();
            }
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(PartnerModelBuilder.AlbedoPath);
            var partnerShader = Shader.Find("LivingPartner/Partner");
            var body = new Material(partnerShader) { name = "PartnerBody" };
            body.SetTexture("_MainTex", albedo);
            body.SetColor("_ShadeTint", PartnerRig.ShadeTint);          // colour properties are given in sRGB; Unity converts them
            body.SetColor("_OutlineColor", PartnerRig.InkColor);
            body.SetFloat("_OutlineWidth", PartnerRig.OutlineWidthPerMetre * PartnerHeight);
            body.SetColor("_CapColor", PartnerRig.CapColor);
            _bodyMat = SaveAsset(body, ArtDir + "/Materials/PartnerBody.mat");

            var face = new Material(partnerShader) { name = "PartnerFace" };       // eyes: the same texture, flat, no outline
            face.SetTexture("_MainTex", albedo);
            face.SetFloat("_OutlineWidth", 0f); face.SetFloat("_Flat", 1f);
            face.SetColor("_CapColor", PartnerRig.CapColor);
            _faceMat = SaveAsset(face, ArtDir + "/Materials/PartnerFace.mat");

            // the ball of the tennis game: a sphere mesh and its toon material
            _ballMesh = SaveAsset(MeshBuilder.Sphere("LPBall"), ArtDir + "/Meshes/LPBall.asset");
            var ballShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Resources/Shaders/LPBall.shader");
            if (ballShader == null) throw new Exception("ball shader not found");
            var ball = new Material(ballShader) { name = "LPBall" };
            ball.SetColor("_OutlineColor", PartnerRig.InkColor);
            _ballMat = SaveAsset(ball, ArtDir + "/Materials/LPBall.mat");

            var cut = new Material(Shader.Find("LivingPartner/PartnerCut")) { name = "PartnerCut" };   // the flat face where the screen cuts the body
            cut.SetColor("_CapColor", PartnerRig.CapColor);
            _cutMat = SaveAsset(cut, ArtDir + "/Materials/PartnerCut.mat");
        }

        // ------------------------------------------------------------------ authoring helpers
        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name) { layer = Layer };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>mode: 0 soft disc, 1 ring, 2 gradient panel, 3 frame, 4 radial progress, 5 sparkle, 6 ripple, 7 dashed ring, 8 glow.</summary>
        static FxQuad Fx(string name, Transform parent, int mode, Color color, Vector4 param, int order = 0, Color? color2 = null, Mesh mesh = null)
        {
            var go = Node(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh != null ? mesh : _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _fxMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var q = go.AddComponent<FxQuad>();
            q.mode = mode; q.color = color; q.color2 = color2 ?? color; q.param = param; q.order = order;
            q.Bind(r);
            return q;
        }

        static FxQuad Panel(string name, Transform parent, float width, float height, float cornerMeters, Color top, Color bottom, int order = 0, float rimMeters = 0f)
        {
            var q = Fx(name, parent, 2, top, Vector4.zero, order, bottom);
            Ui.SizePanel(q, width, height, cornerMeters, rimMeters);
            return q;
        }

        /// <summary>The tennis ball as an object: sphere mesh, ball material, BallView. Authored hidden.</summary>
        static BallView[] FootballPool(Transform parent)
        {
            var balls = new BallView[8];
            for (int i = 0; i < balls.Length; i++) balls[i] = Ball("Football " + (i + 1), parent);
            return balls;
        }

        static BallView Ball(string name, Transform parent)
        {
            var go = Node(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = _ballMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _ballMat; r.enabled = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            go.transform.localScale = Vector3.one * Tennis.Radius * 2f;
            var b = go.AddComponent<BallView>();
            Wire(b, "_renderer", r);
            return b;
        }

        /// <summary>Authored hidden: the runtime switches it on when it has something to show.</summary>
        static FxQuad Hidden(FxQuad q) { q.enabled = false; q.Renderer.enabled = false; return q; }

        static StoryLabel Label(string name, Transform parent, string text, float worldHeight, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = false)
        {
            var go = Node(name, parent);
            var tm = go.AddComponent<TextMesh>();
            Font font = bold ? _fontBold : _fontRegular;
            tm.font = font; tm.fontSize = 0; tm.characterSize = worldHeight * 10f / 64f * 0.72f;      // 64 = the em the font is measured on
            tm.anchor = anchor; tm.alignment = TextAlignment.Center; tm.color = color; tm.text = text; tm.richText = false;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = font.material;
            r.sortingOrder = 100;                 // text is always drawn above the panels it sits on
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            var label = go.AddComponent<StoryLabel>();
            Wire(label, "_mesh", tm, "_renderer", r, "_story", font, "_fallback", _fontFallback, "_bold", bold);
            return label;
        }

        /// <summary>Assigns private [SerializeField] fields by name; throws if a field is missing or a value is null.</summary>
        static void Wire(Object target, params object[] kv)
        {
            var so = new SerializedObject(target);
            for (int i = 0; i < kv.Length; i += 2)
            {
                string field = (string)kv[i]; object v = kv[i + 1];
                var p = so.FindProperty(field);
                if (p == null) throw new Exception($"{target.GetType().Name}: no serialized field '{field}'");
                if (v == null) throw new Exception($"{target.GetType().Name}.{field}: value is null");
                switch (v)
                {
                    case Object o: p.objectReferenceValue = o; break;
                    case float f: p.floatValue = f; break;
                    case int n: p.intValue = n; break;
                    case bool b: p.boolValue = b; break;
                    case string s: p.stringValue = s; break;
                    case Vector3 v3: p.vector3Value = v3; break;
                    case Color c: p.colorValue = c; break;
                    case IList list:
                        p.arraySize = list.Count;
                        for (int j = 0; j < list.Count; j++)
                        {
                            if (list[j] == null) throw new Exception($"{target.GetType().Name}.{field}[{j}]: value is null");
                            p.GetArrayElementAtIndex(j).objectReferenceValue = (Object)list[j];
                        }
                        break;
                    default: throw new Exception($"{target.GetType().Name}.{field}: unsupported type {v.GetType().Name}");
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject SavePrefab(GameObject root, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static T Instance<T>(GameObject prefab, Transform parent, string name = null) where T : Component
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (name != null) go.name = name;
            return go.GetComponent<T>();
        }

        /// <summary>Reports every unassigned object reference on LivingPartner components under root.</summary>
        public static void Validate(GameObject root, string where, List<string> problems)
        {
            if (root == null) { problems.Add(where + ": missing"); return; }
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) { problems.Add(where + ": missing script"); continue; }
                string ns = mb.GetType().Namespace ?? "";
                if (!ns.StartsWith("LivingPartner")) continue;
                var so = new SerializedObject(mb);
                var p = so.GetIterator();
                bool enter = true;
                while (p.NextVisible(enter))
                {
                    enter = true;
                    if (mb is UiButton && p.name == "art") continue;      // (a pill button has no picture: it is drawn by its bg and its label)
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.name != "m_Script" && p.objectReferenceValue == null)
                        problems.Add($"{where}: {mb.name} ({mb.GetType().Name}).{p.propertyPath} is not assigned");
                }
            }
        }

        static void ValidateOpenScene(string where, List<string> problems)
        {
            foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects()) Validate(go, where, problems);
        }

        static PartnerDesign[] _designAssets;
        static string _modelReport = "";

        // ------------------------------------------------------------------ prefab: the partner
        static GameObject BuildPartner()
        {
            float h = PartnerHeight;
            if (!AssetDatabase.IsValidFolder(ArtDir + "/Characters")) AssetDatabase.CreateFolder(ArtDir, "Characters");
            var root = Node("Partner", null);
            var rig = root.AddComponent<PartnerRig>();
            var visual = Node("Visual", root.transform).transform;
            visual.localScale = Vector3.one * h;

            var bones = new Transform[Skeleton.N];
            for (int i = 0; i < Skeleton.N; i++)
            {
                int p = Skeleton.Parent[i];
                var t = Node(((Bone)i).ToString(), p < 0 ? visual : bones[p]).transform;
                t.localPosition = Conv.U(Skeleton.RestLocal[i]);
                var e = Skeleton.RestEuler[i];
                t.localRotation = Quaternion.Euler(e.X, e.Y, e.Z);       // the pose a player starts from, so the prefab looks the same in the Editor
                bones[i] = t;
            }

            // the owner's model, skinned to these bones; its eyes are separate meshes on the head so the rig can blink
            var model = PartnerModelBuilder.Build();
            _modelReport = model.report;
            int builtVerts = model.body.vertexCount;
            var bodyMesh = SaveAsset(model.body, ArtDir + "/Characters/PartnerBody_momo.asset");
            if (bodyMesh.vertexCount != builtVerts) throw new Exception($"PartnerBody_momo: the saved mesh has {bodyMesh.vertexCount} vertices, the model gives {builtVerts}");
            var eyeLMesh = SaveAsset(model.eyeL, ArtDir + "/Characters/PartnerEyeL_momo.asset");
            var eyeRMesh = SaveAsset(model.eyeR, ArtDir + "/Characters/PartnerEyeR_momo.asset");

            var design = ScriptableObject.CreateInstance<PartnerDesign>();
            design.name = "Design_momo";
            design.id = Designs.Ids[0]; design.displayName = "Nilo"; design.bodyMesh = bodyMesh;
            design.eyeL = model.eyeLPos; design.eyeR = model.eyeRPos; design.eyeScale = Vector3.one;
            _designAssets = new[] { SaveAsset(design, $"{ArtDir}/Characters/Design_momo.asset") };
            var first = _designAssets[0];

            var smr = Node("Body", visual).AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = first.bodyMesh; smr.bones = bones; smr.rootBone = bones[(int)Bone.Hips];
            smr.sharedMaterial = _bodyMat; smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; smr.receiveShadows = false;
            smr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; smr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 3f);

            // face: two eyes, nothing else
            var face = new List<MeshRenderer>();
            Transform head = bones[(int)Bone.Head];
            Transform eyeL = FacePart("EyeL", eyeLMesh, head, first.eyeL, first.eyeScale, face);
            Transform eyeR = FacePart("EyeR", eyeRMesh, head, first.eyeR, first.eyeScale, face);

            // contact shadow (follows the feet, stays flat)
            var shadow = Fx("ContactShadow", root.transform, 0, new Color(0.10f, 0.08f, 0.25f, 0.30f), new Vector4(0.6f, 0, 0, 0));
            shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            shadow.transform.localScale = Vector3.one * h * 0.62f;

            // ears and voice: on the head, so the voice comes from where the partner is
            var voiceGo = Node("Voice", head);
            voiceGo.transform.localPosition = new Vector3(0, Skeleton.HeadCenterUp, 0);
            var audio = voiceGo.AddComponent<AudioSource>();
            audio.playOnAwake = false; audio.loop = false; audio.spatialBlend = 1f; audio.dopplerLevel = 0f;
            audio.rolloffMode = AudioRolloffMode.Linear; audio.minDistance = 0.35f; audio.maxDistance = 4f;      // a small voice at arm's length: clear nearby, gone across the room
            var voice = voiceGo.AddComponent<VoiceIo>();
            Wire(voice, "_source", audio);

            // the face that closes the body where the screen cuts it: placed on the display plane while a crossing runs
            var cutGo = Node("CutFace", root.transform);
            cutGo.AddComponent<MeshFilter>().sharedMesh = _quad;
            var cutFace = cutGo.AddComponent<MeshRenderer>();
            cutFace.sharedMaterial = _cutMat; cutFace.enabled = false;
            cutFace.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cutFace.receiveShadows = false;
            cutFace.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; cutFace.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            cutGo.transform.localScale = new Vector3(h * 3f, h * 3f, 1f);

            Wire(rig, "_height", h, "_visualRoot", visual, "_bones", bones, "_body", smr, "_face", face, "_eyeL", eyeL, "_eyeR", eyeR, "_cutFace", cutFace, "_voice", voice,
                "_eyeBaseScale", first.eyeScale, "_eyeLBase", first.eyeL, "_eyeRBase", first.eyeR, "_shadow", shadow, "_designs", _designAssets, "_design", 0);
            return SavePrefab(root, "Partner");
        }

        /// <summary>
        /// Scenes/CharacterGallery.unity: the partner seen from four sides, for looking at it in the Scene view (it is not
        /// part of any build). Each one is the Partner prefab.
        /// </summary>
        static void BuildGalleryScene(GameObject partnerPrefab, List<string> problems)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = Node("CharacterGallery", null).transform;
            (string name, float yaw)[] views = { ("Front", 0f), ("Three-quarter", 38f), ("Side", 90f), ("Back", 180f) };
            int n = views.Length;
            float gap = 0.098f, y0 = 1.0f;

            var camGo = Node("GalleryCamera", root);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.97f, 0.94f, 0.88f, 1);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 10f; cam.fieldOfView = 9f;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            camGo.transform.position = new Vector3(0, y0 + 0.085f, -1.75f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, y0 + 0.052f, 0) - camGo.transform.position);

            var floor = Fx("Floor", root, 2, new Color(0.93f, 0.88f, 0.80f, 1f), new Vector4(0.2f, 0, 0, 0), -20, new Color(0.90f, 0.84f, 0.75f, 1f));
            floor.transform.SetPositionAndRotation(new Vector3(0, y0 - 0.0005f, 0), Quaternion.Euler(90, 0, 0));
            floor.transform.localScale = new Vector3(0.70f, 0.30f, 1);

            for (int i = 0; i < n; i++)
            {
                var rig = Instance<PartnerRig>(partnerPrefab, root, "Partner " + views[i].name);
                rig.transform.position = new Vector3((i - (n - 1) * 0.5f) * gap, y0, 0);
                Vector3 toCam = camGo.transform.position - rig.transform.position; toCam.y = 0;
                rig.transform.rotation = Quaternion.LookRotation(toCam) * Quaternion.Euler(0, views[i].yaw, 0);
                var label = Label("Name " + views[i].name, root, views[i].name, 0.0105f, Ui.Pal.Ink);
                label.transform.position = new Vector3(rig.transform.position.x, y0 - 0.014f, -0.045f);
                label.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            }
            // a specimen sheet of the project's own typeface, off to the side with its own camera
            var sheet = Node("FontSpecimen", root).transform;
            sheet.position = new Vector3(3f, y0, 0);
            var sheetCamGo = Node("SpecimenCamera", sheet);
            var sheetCam = sheetCamGo.AddComponent<Camera>();
            sheetCam.clearFlags = CameraClearFlags.SolidColor; sheetCam.backgroundColor = cam.backgroundColor;
            sheetCam.orthographic = true; sheetCam.orthographicSize = 0.105f; sheetCam.nearClipPlane = 0.02f; sheetCam.farClipPlane = 5f;
            sheetCamGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            sheetCamGo.transform.localPosition = new Vector3(0, 0, -1f);
            var paper = Panel("Paper", sheet, 0.34f, 0.19f, 0.012f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 0, 0.0016f);
            paper.transform.localPosition = new Vector3(0, 0, 0.002f);
            string[] lines =
            {
                "LPStory - drawn for Yonder",
                "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                "abcdefghijklmnopqrstuvwxyz",
                "0123456789   .,:;!?'\"()[]{}",
                "+-=/\\|*#%&@$^~<>_`  · • … – — ‘ ’ “ ” → × ° ✓",
                "Nilo climbed out of the screen and sat down on the desk.",
                "ABCDEFGHIJKLM abcdefghijklm 0123456789",
                "Pair   Calibrate   Return   Quiet   04:59",
            };
            bool[] boldLine = { true, false, false, false, false, false, true, true };
            for (int i = 0; i < lines.Length; i++)
            {
                var l = Label("Line " + i, sheet, lines[i], i == 0 ? 0.017f : 0.0135f, i == 0 ? Ui.Pal.Text : Ui.Pal.Ink, TextAnchor.MiddleCenter, boldLine[i]);
                l.transform.localPosition = new Vector3(0, 0.073f - i * 0.0205f, 0);
            }

            ValidateOpenScene("CharacterGallery.unity", problems);
            EditorSceneManager.SaveScene(scene, GalleryScene);
        }

        static Transform FacePart(string name, Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, List<MeshRenderer> into)
        {
            var go = Node(name, parent);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = _faceMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            into.Add(r);
            return go.transform;
        }

        // ------------------------------------------------------------------ prefabs: portal and sparkles
        static GameObject BuildPortalFx()
        {
            var root = Node("PortalFx", null);
            var fx = root.AddComponent<PortalFx>();
            var t = root.transform;
            var ring = Hidden(Fx("Ring", t, 1, Ui.Pal.Orange, new Vector4(0.62f, 0.040f, 0, 0), 2, Ui.Pal.Pink));
            var dash = Hidden(Fx("Dash", t, 7, Ui.Pal.Violet, new Vector4(0.80f, 0.018f, 16f, 0.55f), 2, Ui.Pal.Cyan));
            var dots = Hidden(Fx("Dots", t, 7, Ui.Pal.Orange, new Vector4(0.93f, 0.014f, 40f, 0.22f), 2, Ui.Pal.Pink));
            foreach (var q in new[] { ring, dash, dots }) q.transform.localScale = Vector3.one * 0.16f;
            var ripples = new FxQuad[3];
            for (int i = 0; i < ripples.Length; i++) { ripples[i] = Hidden(Fx("Ripple " + i, t, 6, Ui.Pal.Cyan, new Vector4(0.62f, 0, 2f, 0), 1, Ui.Pal.Violet)); ripples[i].transform.localScale = Vector3.one * 0.25f; }
            var sparks = new FxQuad[10];
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i] = Hidden(Fx("Spark " + i, t, 5, Color.white, new Vector4(1.4f, 0, 0, 0), 3, i % 2 == 0 ? Ui.Pal.Cyan : Ui.Pal.Pink));
                float a = i / (float)sparks.Length * Mathf.PI * 2f;
                sparks[i].transform.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.064f;
                sparks[i].transform.localScale = Vector3.one * 0.016f;
            }
            Wire(fx, "_ring", ring, "_dashOuter", dash, "_dots", dots, "_ripples", ripples, "_sparks", sparks);
            return SavePrefab(root, "PortalFx");
        }

        static GameObject BuildSparkFx()
        {
            var root = Node("SparkFx", null);
            var fx = root.AddComponent<SparkFx>();
            var quads = new FxQuad[36];
            for (int i = 0; i < quads.Length; i++)
            {
                quads[i] = Hidden(Fx("Spark " + i, root.transform, 5, Color.white, new Vector4(1.3f, 0, 0, 0), 5, i % 3 == 0 ? Ui.Pal.Cyan : i % 3 == 1 ? Ui.Pal.Pink : Ui.Pal.Orange));
                quads[i].transform.localScale = Vector3.one * 0.012f;
            }
            Wire(fx, "_quads", quads);
            return SavePrefab(root, "SparkFx");
        }

        // ------------------------------------------------------------------ panels
        static UiPanel MakePanel(string name, Transform parent, float w, float h, Color accent, string title, string body)
        {
            var root = Node(name, parent).transform;
            var p = root.gameObject.AddComponent<UiPanel>();
            p.width = w; p.height = h;
            p.glow = Fx("Shadow", root, 8, Ui.Pal.Shadow, new Vector4(2.6f, 0, 0, 0), -2, Ui.Pal.Shadow);
            p.glow.transform.localScale = new Vector3(w * 1.22f, h * 1.34f, 1); p.glow.transform.localPosition = new Vector3(0.002f, -0.006f, 0.003f);
            p.bg = Panel("Bg", root, w, h, Mathf.Min(w, h) * 0.20f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 0, 0.0016f);
            p.bar = Panel("Bar", root, w * 0.30f, 0.0024f, 0.0012f, accent, Ui.Pal.Pink, 1);
            p.bar.transform.localPosition = new Vector3(0, h * 0.5f - 0.0215f, -0.0008f);
            p.title = Label("Title", root, title, 0.0105f, Ui.Pal.Text, TextAnchor.UpperCenter, true);
            p.title.transform.localPosition = new Vector3(0, h * 0.5f - 0.0065f, -0.001f);
            p.body = Label("Body", root, body, 0.0082f, Ui.Pal.TextDim, TextAnchor.UpperLeft);
            p.body.Mesh.alignment = TextAlignment.Left;
            p.body.transform.localPosition = new Vector3(-w * 0.5f + 0.008f, h * 0.5f - 0.027f, -0.001f);
            return p;
        }

        /// <summary>A picture of the interface art (Art/Ui/name.png, drawn by tools/ui-art/build_ui_art.py).</summary>
        static Texture2D Art(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(UiArtDir + "/" + name + ".png");
            if (t == null) throw new Exception("interface art missing: " + name + ".png (run tools/ui-art/build_ui_art.py)");
            return t;
        }

        /// <summary>A quad showing one picture of the interface art; width and height in metres.</summary>
        static UiSprite Sprite(string name, Transform parent, string art, float width, float height, int order)
        {
            var go = Node(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _spriteMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var sp = go.AddComponent<UiSprite>();
            sp.texture = Art(art); sp.order = order;
            sp.Bind(r);
            go.transform.localScale = new Vector3(width, height, 1);
            return sp;
        }

        /// <summary>
        /// The menu. Two things are authored here:
        ///  - MenuFan: what the user owns as stickers WITHOUT tiles on a piece of a cylinder curved towards the user
        ///    (four columns by three rows; the numbers are MenuBoardView's), each with a soft glow behind it, its name on
        ///    a strip of paper under it, marks for "on the desk", "on" and "new", and a thin warm ray to it from one
        ///    point at the bottom - the open left palm it unfolds from. Header: a ribbon with the page's name, a round
        ///    arrow at each side of it (the pages run Bag 1..n, Nilo, Tools), a dot per page, the user's stars, and a
        ///    round button that closes the fan (only shown while it stands in the room). Under the stickers one line
        ///    that says in words what a press did. It is at HAND scale: about 30 cm wide, the stickers 5 cm.
        ///  - PalmChip: the round "Stop" sticker the open palm carries during a game.
        /// Everything is authored as real objects; the app fills and drives them (MrMenuBoard).
        /// (History: a carousel that rose from the palm; a flat board of twelve paper tiles; a fan that stood in the
        /// room, opened by a button over the wrist or a bag on the desk, with a Help page. The button, the bag and the
        /// Help page are gone: "the bag is not needed", "no Help window" - the user, 2026-11-17.)
        /// </summary>
        const int MenuOrder = 200;      // the draw order of the menu's pictures (its lettering: 300); the pointing ray and its cursor are above that
        static MenuBoardView BuildMenuBoard(Transform panels)
        {
            const float k = MenuBoardView.K;      // (what was chosen for the fan as a board in the room, times k: hand scale)
            Color honey = new Color(1f, 0.84f, 0.42f), warm = new Color(1f, 0.93f, 0.74f);
            var root = Node("MenuFan", panels).transform;
            root.localPosition = new Vector3(-0.42f, 0.38f, 0);
            var p = root.gameObject.AddComponent<UiPanel>();
            p.width = MenuBoardView.FanWidth; p.height = MenuBoardView.FanHeight;
            // (a fan has no sheet of paper behind it: the parts of a panel it does not use are authored switched off)
            p.bg = Hidden(Fx("Unused Bg", root, 0, Color.clear, Vector4.zero, -9));
            p.glow = Hidden(Fx("Unused Glow", root, 0, Color.clear, Vector4.zero, -9));
            p.bar = Hidden(Fx("Unused Bar", root, 0, Color.clear, Vector4.zero, -9));
            p.title = Label("Unused Title", root, "", 0.008f, Ui.Pal.Text, TextAnchor.UpperCenter, true);
            p.body = Label("Unused Body", root, "", 0.008f, Ui.Pal.TextDim, TextAnchor.UpperLeft);
            var v = root.gameObject.AddComponent<MenuBoardView>();
            v.panel = p;

            // ---- the point everything unfolds from (just above the palm), and a ray from it to every sticker
            v.rootGlow = Fx("Root Glow", root, 8, new Color(1f, 0.90f, 0.60f, 0.85f), new Vector4(3.4f, 0, 0, 0), 1, new Color(1f, 0.97f, 0.86f, 0.85f));
            v.rootGlow.transform.localPosition = MenuBoardView.FanPoint(0, MenuBoardView.RootY); v.rootGlow.transform.localScale = Vector3.one * (0.075f * k);
            v.rootSpark = Sprite("Root Spark", root, "spark", 0.030f * k, 0.030f * k, 2);
            v.rootSpark.transform.localPosition = MenuBoardView.FanPoint(0, MenuBoardView.RootY, 0.002f);
            int n = MenuBoardView.Tiles;
            v.rays = new FxQuad[n];
            var rayRoot = Node("Rays", root).transform;
            for (int i = 0; i < n; i++)
            {
                v.rays[i] = Fx("Ray " + i, rayRoot, 2, new Color(warm.r, warm.g, warm.b, 0.0f), new Vector4(1f, 0, 0.03f, 0.95f), 0, new Color(1f, 0.97f, 0.86f, 0.75f));
                v.rays[i].transform.localScale = new Vector3(0.009f * k, 0.2f * k, 1f);
            }

            // ---- header: the ribbon with the page's name, an arrow at each side, the stars, the button that closes the fan, the page dots
            // (the header pops up as one: its parts are placed from the middle of the ribbon)
            v.header = Node("Header", root).transform;
            float hy = MenuBoardView.HeaderY;
            Vector3 hOrigin = MenuBoardView.FanPoint(0, hy);
            v.header.localPosition = hOrigin;
            Vector3 H(float s, float y, float lift = 0f) => MenuBoardView.FanPoint(s, y, lift) - hOrigin;
            const float ribbonW = 0.215f * k, ribbonH = ribbonW * 150f / 460f;
            v.ribbon = Sprite("Ribbon", v.header, "menu-ribbon", ribbonW, ribbonH, 8);
            v.ribbon.transform.localPosition = H(0, hy - 0.0075f * k);      // (the ribbon itself is a little above the middle of its picture: its shadow is below it)
            v.title = Label("Title", v.header, "Bag", 0.0160f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.title.transform.localPosition = H(0, hy, 0.002f);
            UiButton HeaderBtn(string name, string text, float s, float size, float textHeight, Color hue)
            {
                var b = AddPill(p, name, text, Vector3.zero, size, size, textHeight, hue);
                b.transform.SetParent(v.header, false);
                b.transform.localPosition = H(s, hy); b.transform.localRotation = MenuBoardView.FanTurn(s);
                b.radius = size * 0.62f; b.half = Vector2.zero;      // round: pressed anywhere on it, and a little round it
                return b;
            }
            // (the arrows are pressed with a fingertip: not smaller than 3 cm)
            v.prev = HeaderBtn("Btn Prev", "<", -0.090f, 0.030f, 0.0170f, Purple);
            v.next = HeaderBtn("Btn Next", ">", 0.090f, 0.030f, 0.0170f, Purple);
            v.close = HeaderBtn("Btn Close", "\u00d7", 0.132f, 0.028f, 0.0170f, Rust);
            var starsRoot = Node("Stars", v.header).transform;
            starsRoot.localPosition = H(-0.136f, hy); starsRoot.localRotation = MenuBoardView.FanTurn(-0.136f);
            v.starsBg = Panel("StarsBg", starsRoot, 0.050f, 0.0180f, 0.0090f, new Color(1f, 0.93f, 0.70f, 0.98f), new Color(1f, 0.86f, 0.52f, 0.98f), 3, 0.0010f);
            v.starIcon = Sprite("Star", starsRoot, "star", 0.020f, 0.020f, 8);
            v.starIcon.transform.localPosition = new Vector3(-0.0170f, 0.0006f, -0.002f);
            v.stars = Label("Stars", starsRoot, "300", 0.0108f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.stars.transform.localPosition = new Vector3(0.0060f, 0, -0.002f);
            v.dots = new FxQuad[8];
            for (int i = 0; i < v.dots.Length; i++)
            {
                v.dots[i] = Panel("PageDot " + i, v.header, 0.0085f * k, 0.0085f * k, 0.00425f * k, Ui.Pal.PanelBottom, Ui.Pal.PanelBottom, 3, 0.0009f);
                v.dots[i].transform.localPosition = H((i - 3.5f) * 0.017f * k, MenuBoardView.DotsY);
            }

            // ---- the stickers: four by three (the app lays the ones in use out every frame: MrMenuBoard.FanLayout)
            const float st = MenuBoardView.Sticker, artY = MenuBoardView.ArtY, nameY = MenuBoardView.NameY;
            const float badge = st * 0.94f * 288f / 208f;      // a badge picture is 288 px with the badge itself 208 px inside it
            v.tiles = new UiButton[n]; v.tileNameBg = new FxQuad[n]; v.tileDot = new FxQuad[n]; v.tileRing = new FxQuad[n];
            v.tileNewBg = new FxQuad[n]; v.tileNew = new StoryLabel[n]; v.tilePlace = new FxQuad[n]; v.tileHills = new UiSprite[n];
            for (int i = 0; i < n; i++)
            {
                var t = Node("Sticker " + i, root).transform;
                float s = (i % MenuBoardView.Cols - (MenuBoardView.Cols - 1) * 0.5f) * MenuBoardView.PitchX, y = MenuBoardView.GridY + (1 - i / MenuBoardView.Cols) * MenuBoardView.PitchY;
                t.localPosition = MenuBoardView.FanPoint(s, y); t.localRotation = MenuBoardView.FanTurn(s);
                var b = t.gameObject.AddComponent<UiButton>();
                b.radius = 0.030f;
                b.half = new Vector2(0.0330f, 0.0310f);      // the sticker and its name strip are the button (a little less than the step from one to the next)
                b.hoverGrow = 0.07f;
                Color glow = new Color(1f, 0.94f, 0.76f, 0.55f);
                b.color = glow; b.color2 = glow;
                b.bg = Fx("Glow", t, 8, glow, new Vector4(4.4f, 0, 0, 0), 1, glow);      // NO tile: only this soft round glow (it has faded to nothing well inside its quad)
                b.bg.transform.localScale = Vector3.one * (st * 2.0f); b.bg.transform.localPosition = new Vector3(0, artY, 0.002f);
                v.tileRing[i] = Hidden(Fx("OnRing", t, 1, new Color(1f, 0.72f, 0.22f, 1f), new Vector4(0.90f, 0.075f, 0, 0), 5, new Color(1f, 0.86f, 0.45f, 1f)));
                v.tileRing[i].transform.localPosition = new Vector3(0, artY, -0.0010f); v.tileRing[i].transform.localScale = Vector3.one * (st * 1.20f);
                v.tilePlace[i] = Hidden(Panel("Place", t, st * 0.92f, st * 0.92f, st * 0.46f, Ui.Pal.Cyan, Ui.Pal.Orange, 4, 0.0011f));
                v.tilePlace[i].transform.localPosition = new Vector3(0, artY, -0.0020f);
                v.tileHills[i] = Sprite("PlaceHills", t, "place-hills", st * 0.92f, st * 0.92f, 5);
                v.tileHills[i].transform.localPosition = new Vector3(0, artY, -0.0024f);
                v.tileHills[i].enabled = false; v.tileHills[i].Renderer.enabled = false;
                b.art = Sprite("Art", t, "item-any", badge, badge, 6);
                b.art.transform.localPosition = new Vector3(0, artY, -0.0030f);
                v.tileNameBg[i] = Panel("NameBg", t, 0.044f, 0.0145f, 0.0054f, new Color(1f, 0.975f, 0.91f, 0.98f), new Color(0.99f, 0.93f, 0.80f, 0.98f), 7, 0.0009f);
                v.tileNameBg[i].transform.localPosition = new Vector3(0, nameY, -0.0040f);
                b.label = Label("Name", t, "Chair", 0.0102f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                b.label.transform.localPosition = new Vector3(0, nameY + 0.0002f, -0.0050f);
                v.tileDot[i] = Hidden(Panel("OnDesk", t, 0.0095f, 0.0095f, 0.00475f, new Color(0.56f, 0.82f, 0.52f, 1f), new Color(0.42f, 0.72f, 0.42f, 1f), 8, 0.0010f));
                v.tileDot[i].transform.localPosition = new Vector3(0.0330f * k, artY + 0.0300f * k, -0.0046f);
                v.tileNewBg[i] = Hidden(Panel("NewBg", t, 0.0205f, 0.0098f, 0.0049f, new Color(1f, 0.80f, 0.80f, 1f), new Color(0.98f, 0.70f, 0.72f, 1f), 8, 0.0008f));
                v.tileNewBg[i].transform.localPosition = new Vector3(-0.0300f * k, artY + 0.0320f * k, -0.0046f);
                v.tileNew[i] = Label("New", t, "new", 0.0072f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                v.tileNew[i].transform.localPosition = new Vector3(-0.0300f * k, artY + 0.0322f * k, -0.0054f);
                p.buttons = new List<UiButton>(p.buttons) { b }.ToArray();
                v.tiles[i] = b;
            }
            // ---- the line that says what a press did, on its own strip of paper (there is no sheet behind it)
            v.statusBg = Panel("StatusBg", root, 0.19f, 0.0170f, 0.0085f, new Color(1f, 0.985f, 0.945f, 0.96f), new Color(0.985f, 0.945f, 0.875f, 0.96f), 3, 0.0009f);
            v.statusBg.transform.localPosition = MenuBoardView.FanPoint(0, MenuBoardView.StatusY, -0.001f);
            v.status = Label("Status", root, "", 0.0100f, new Color(0.25f, 0.45f, 0.28f), TextAnchor.MiddleCenter, true);
            v.status.transform.localPosition = MenuBoardView.FanPoint(0, MenuBoardView.StatusY, 0.002f);

            // ---- the pictures
            v.texItem = Art("item-any"); v.texSkill = Art("skill-any"); v.texDance = Art("tile-i-dance"); v.texMusic = Art("tile-i-music");
            v.texGame = Art("tile-game"); v.texDemonGate = Art("game-demon-guard"); v.texKeeper = Art("Local/item-ball"); v.texSurvival = Art("tile-survival");
            v.texTennis = Art("tile-i-tennis"); v.texPop = Art("tile-i-pop"); v.texRps = Art("tile-i-rps"); v.texRedLight = Art("tile-i-redlight");
            v.texGoIn = Art("tile-i-goin"); v.texComeOut = Art("tile-i-comeout"); v.texHere = Art("tile-i-here"); v.texWave = Art("tile-i-wave");
            v.texLook = Art("tile-i-look"); v.texCheer = Art("tile-i-cheer"); v.texMarket = Art("tile-i-market"); v.texScreen = Art("tile-i-screen");
            v.texFind = Art("tile-i-find"); v.texQuiet = Art("tile-i-quiet"); v.texRain = Art("tile-i-rain"); v.texCancel = Art("tile-i-cancel");

            // ---- the Stop chip: one round sticker on the open palm during a game (a panel of its own)
            var lr = Node("PalmChip", panels).transform;
            lr.localPosition = new Vector3(-0.42f, 0.06f, 0);
            var lp = lr.gameObject.AddComponent<UiPanel>();
            lp.width = 0.07f; lp.height = 0.07f;
            lp.bg = Fx("Glow", lr, 8, new Color(honey.r, honey.g, honey.b, 0.36f), new Vector4(3.0f, 0, 0, 0), -6, new Color(1f, 0.95f, 0.75f, 0.36f));
            lp.bg.transform.localScale = Vector3.one * 0.105f; lp.bg.transform.localPosition = new Vector3(0, 0, 0.002f);
            lp.glow = Hidden(Fx("Unused Glow", lr, 0, Color.clear, Vector4.zero, -9));
            lp.bar = Hidden(Fx("Unused Bar", lr, 0, Color.clear, Vector4.zero, -9));
            lp.title = Label("Title", lr, "", 0.008f, Ui.Pal.Text, TextAnchor.UpperCenter, true);
            lp.body = Label("Body", lr, "", 0.008f, Ui.Pal.TextDim, TextAnchor.UpperLeft);
            v.chipPanel = lp;
            const float chip = 0.045f;      // the sticker itself: 4.5 cm
            v.chip = Tile(lp, lr, "Btn Stop", "tile-stop", Vector3.zero, chip * 288f / 208f, 0.030f, honey);      // (it is touched: the fingertip coming within 3 cm of its middle)
            v.chip.hoverGrow = 0.08f;

            // The fan (and the chip on the palm) is the nearest thing to the user: it is drawn over everything else - the
            // things on the desk, the partner's speech bubble, the other boards - whatever their own draw order. (Text is
            // drawn at 100 everywhere else: a bubble behind the menu was written across it.)
            foreach (var part in new[] { root, lr })
            {
                foreach (var q in part.GetComponentsInChildren<FxQuad>(true)) { q.order += MenuOrder; q.Apply(); }
                foreach (var sp in part.GetComponentsInChildren<UiSprite>(true)) { sp.order += MenuOrder; sp.Apply(); }
                foreach (var tm in part.GetComponentsInChildren<TextMesh>(true)) tm.GetComponent<MeshRenderer>().sortingOrder = MenuOrder + 100;
            }
            return v;
        }

        /// <summary>A tile button: a picture (icon and name drawn in it) over a glow of its own colour that brightens under a finger.</summary>
        static UiButton Tile(UiPanel p, Transform parent, string objectName, string art, Vector3 local, float quadSize, float radius, Color hue)
        {
            var t = Node(objectName, parent).transform;
            t.localPosition = local;
            var b = t.gameObject.AddComponent<UiButton>();
            b.radius = radius;
            Color glow = hue; glow.a = 0.16f;
            b.color = glow; b.color2 = glow;
            b.bg = Fx("Glow", t, 8, glow, new Vector4(3.0f, 0, 0, 0), 5, glow);
            b.bg.transform.localScale = Vector3.one * quadSize * 1.25f; b.bg.transform.localPosition = new Vector3(0, quadSize * 0.03f, -0.001f);
            b.art = Sprite("Art", t, art, quadSize, quadSize, 6);
            b.art.transform.localPosition = new Vector3(0, 0, -0.003f);
            b.label = Label("Label", t, "", 0.006f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            var list = new List<UiButton>(p.buttons) { b };
            p.buttons = list.ToArray();
            return b;
        }

        static UiButton PageBtn(UiPanel p, Transform page, string objectName, string text, Vector3 local, float size, Color hue)
        {
            var b = AddBtn(p, objectName, text, local, size, hue);
            b.transform.SetParent(page, false);
            b.transform.localPosition = local;
            return b;
        }

        static UiButton AddBtn(UiPanel p, string objectName, string text, Vector3 local, float size, Color hueFrom)
        {
            // every button is a soft pastel pill: a touch lighter at the top, a thin darker outline, ink lettering
            Color top = Color.Lerp(hueFrom, Color.white, 0.18f), bottom = hueFrom * 0.96f;
            top.a = bottom.a = 0.98f;
            var t = Node(objectName, p.transform).transform;
            t.localPosition = local;
            var b = t.gameObject.AddComponent<UiButton>();
            b.radius = size * 0.5f; b.color = top; b.color2 = bottom;
            float bw = size, bh = size * 0.60f;
            b.bg = Panel("Bg", t, bw, bh, bh * 0.5f, top, bottom, 2, 0.0012f);
            b.bg.transform.localPosition = new Vector3(0, 0, -0.0015f);
            b.label = Label("Label", t, text, size * 0.21f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            b.label.transform.localPosition = new Vector3(0, 0, -0.003f);
            var list = new List<UiButton>(p.buttons) { b };
            p.buttons = list.ToArray();
            return b;
        }

        /// <summary>A pill button of a given width and height (AddBtn's are all the same shape).</summary>
        static UiButton AddPill(UiPanel p, string objectName, string text, Vector3 local, float w, float h, float textHeight, Color hueFrom)
        {
            Color top = Color.Lerp(hueFrom, Color.white, 0.18f), bottom = hueFrom * 0.96f;
            top.a = bottom.a = 0.98f;
            var t = Node(objectName, p.transform).transform;
            t.localPosition = local;
            var b = t.gameObject.AddComponent<UiButton>();
            b.radius = Mathf.Max(h * 0.75f, Mathf.Min(w, h * 2.2f) * 0.5f); b.color = top; b.color2 = bottom;
            b.half = new Vector2(w * 0.5f, h * 0.5f);      // a pill is pressed anywhere on it, not only inside a circle round its middle
            b.bg = Panel("Bg", t, w, h, h * 0.5f, top, bottom, 4, 0.0012f);
            b.bg.transform.localPosition = new Vector3(0, 0, -0.0015f);
            b.label = Label("Label", t, text, textHeight, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            b.label.transform.localPosition = new Vector3(0, 0, -0.003f);
            var list = new List<UiButton>(p.buttons) { b };
            p.buttons = list.ToArray();
            return b;
        }

        /// <summary>
        /// The marketplace as a board in the room: a sheet of paper under a ribbon; the user's stars; a row of tabs;
        /// eight product cards (four by two) and, to their right, the picked one large with one button. The app fills
        /// it from the catalogue the PC sends (MrMarket.UpdateShop).
        /// </summary>
        static MarketBoardView BuildMarketBoard(Transform panels)
        {
            const float W = 0.62f, H = 0.40f;
            var p = MakePanel("MarketBoard", panels, W, H, Ui.Pal.Orange, "", "");
            p.transform.localPosition = new Vector3(0.42f, 0.74f, 0);
            Ui.SizePanel(p.bg, W, H, 0.030f, 0.0022f);
            Hidden(p.bar);
            var root = p.transform;
            var v = p.gameObject.AddComponent<MarketBoardView>();
            v.panel = p;
            Color honey = new Color(1f, 0.84f, 0.42f);
            v.ribbon = Sprite("Ribbon", root, "market-ribbon", 0.215f, 0.215f * 150f / 460f, 8);
            v.ribbon.transform.localPosition = new Vector3(-0.185f, H * 0.5f - 0.010f, -0.004f);
            // the stars the user has
            var starsBg = Panel("StarsBg", root, 0.092f, 0.030f, 0.015f, new Color(1f, 0.93f, 0.70f, 0.98f), new Color(1f, 0.86f, 0.52f, 0.98f), 3, 0.0014f);
            starsBg.transform.localPosition = new Vector3(0.152f, H * 0.5f - 0.036f, -0.002f);
            v.starIcon = Sprite("Star", root, "star", 0.034f, 0.034f, 8);
            v.starIcon.transform.localPosition = new Vector3(0.118f, H * 0.5f - 0.035f, -0.004f);
            v.stars = Label("Stars", root, "300", 0.0190f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.stars.transform.localPosition = new Vector3(0.160f, H * 0.5f - 0.036f, -0.004f);
            v.nickname = Label("Nickname", root, "", 0.0175f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.nickname.transform.localPosition = new Vector3(0.012f, H * 0.5f - 0.036f, -0.004f);
            v.close = AddPill(p, "Btn Close", "Close", new Vector3(0.250f, H * 0.5f - 0.036f, 0), 0.070f, 0.032f, 0.0150f, Rust);
            // tabs
            string[] tabNames = { "All", "Things", "Skills", "Games", "Places", "Powers", "Mine" };
            v.tabs = new UiButton[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
                v.tabs[i] = AddPill(p, "Tab " + tabNames[i], tabNames[i], new Vector3(-0.270f + i * 0.0585f, H * 0.5f - 0.082f, 0), 0.0550f, 0.029f, 0.0128f, i == 6 ? Ui.Pal.Orange : Ui.Pal.PanelBottom);
            // cards: four by two
            const float cw = 0.090f, ch = 0.114f;
            v.cards = new UiButton[8]; v.cardPrice = new StoryLabel[8]; v.cardDisc = new FxQuad[8];
            for (int i = 0; i < 8; i++)
            {
                var t = Node("Card " + i, root).transform;
                t.localPosition = new Vector3(-0.249f + (i % 4) * 0.098f, 0.034f - (i / 4) * 0.124f, 0);
                var b = t.gameObject.AddComponent<UiButton>();
                b.radius = 0.043f; b.color = Ui.Pal.PanelTop; b.color2 = Ui.Pal.PanelBottom;
                b.half = new Vector2(cw * 0.5f, ch * 0.5f);      // the whole card is the button
                b.bg = Panel("Paper", t, cw, ch, 0.014f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 2, 0.0016f);
                b.bg.transform.localPosition = new Vector3(0, 0, -0.0015f);
                v.cardDisc[i] = Panel("Disc", t, 0.066f, 0.066f, 0.033f, new Color(1f, 0.89f, 0.72f), new Color(1f, 0.89f, 0.72f), 3, 0.0013f);
                v.cardDisc[i].transform.localPosition = new Vector3(0, 0.017f, -0.0025f);
                b.art = Sprite("Art", t, "item-any", 0.078f, 0.078f, 6);
                b.art.transform.localPosition = new Vector3(0, 0.017f, -0.0035f);
                b.label = Label("Name", t, "Chair", 0.0150f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
                b.label.transform.localPosition = new Vector3(0, -0.0285f, -0.004f);
                v.cardPrice[i] = Label("Price", t, "Free", 0.0122f, Ui.Pal.TextDim, TextAnchor.MiddleCenter);
                v.cardPrice[i].transform.localPosition = new Vector3(0, -0.0445f, -0.004f);
                var list = new List<UiButton>(p.buttons) { b };
                p.buttons = list.ToArray();
                v.cards[i] = b;
            }
            // under the cards: the page, and what just happened
            v.prev = AddPill(p, "Btn Prev", "<", new Vector3(-0.268f, -H * 0.5f + 0.030f, 0), 0.042f, 0.029f, 0.0170f, Purple);
            v.pageLabel = Label("Page", root, "1 / 1", 0.0145f, Ui.Pal.TextDim, TextAnchor.MiddleCenter, true);
            v.pageLabel.transform.localPosition = new Vector3(-0.222f, -H * 0.5f + 0.030f, -0.003f);
            v.next = AddPill(p, "Btn Next", ">", new Vector3(-0.176f, -H * 0.5f + 0.030f, 0), 0.042f, 0.029f, 0.0170f, Purple);
            v.notice = Label("Notice", root, "", 0.0150f, new Color(0.25f, 0.50f, 0.30f), TextAnchor.MiddleLeft, true);
            v.notice.transform.localPosition = new Vector3(-0.140f, -H * 0.5f + 0.030f, -0.003f);
            // the picked card, large
            const float dx = 0.213f, dw = 0.176f, dh = 0.262f, dy = -0.028f;
            v.detailBg = Panel("Detail", root, dw, dh, 0.018f, new Color(1f, 0.975f, 0.90f, 0.98f), new Color(1f, 0.94f, 0.80f, 0.98f), 1, 0.0018f);
            v.detailBg.transform.localPosition = new Vector3(dx, dy, -0.001f);
            v.detailDisc = Panel("DetailDisc", root, 0.098f, 0.098f, 0.049f, new Color(1f, 0.89f, 0.72f), new Color(1f, 0.89f, 0.72f), 3, 0.0015f);
            v.detailDisc.transform.localPosition = new Vector3(dx, dy + 0.074f, -0.0025f);
            v.detailIcon = Sprite("DetailIcon", root, "item-any", 0.112f, 0.112f, 7);
            v.detailIcon.transform.localPosition = new Vector3(dx, dy + 0.074f, -0.0045f);
            v.detailName = Label("DetailName", root, "Chair", 0.0190f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.detailName.transform.localPosition = new Vector3(dx, dy + 0.011f, -0.004f);
            v.detailBy = Label("DetailBy", root, "Furniture  -  by Nilo Studio", 0.0112f, Ui.Pal.TextDim, TextAnchor.MiddleCenter);
            v.detailBy.transform.localPosition = new Vector3(dx, dy - 0.006f, -0.004f);
            v.detailText = Label("DetailText", root, "Nilo can sit down - and study,\nwhen there is a desk.", 0.0120f, Ui.Pal.Text, TextAnchor.UpperCenter);
            v.detailText.transform.localPosition = new Vector3(dx, dy - 0.019f, -0.004f);
            v.action = AddPill(p, "Btn Action", "Get  -  Free", new Vector3(dx, dy - dh * 0.5f + 0.024f, 0), 0.156f, 0.034f, 0.0150f, Green);
            // for the user's own things: the price, one step down / up, between the text and the button
            v.minus = AddPill(p, "Btn Minus", "-", new Vector3(dx - 0.058f, dy - dh * 0.5f + 0.063f, 0), 0.036f, 0.028f, 0.0180f, Purple);
            v.plus = AddPill(p, "Btn Plus", "+", new Vector3(dx + 0.058f, dy - dh * 0.5f + 0.063f, 0), 0.036f, 0.028f, 0.0180f, Purple);
            v.priceLabel = Label("PriceLabel", root, "10 stars", 0.0140f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.priceLabel.transform.localPosition = new Vector3(dx, dy - dh * 0.5f + 0.063f, -0.004f);
            v.texItem = Art("item-any"); v.texSkill = Art("skill-any"); v.texDance = Art("skill-dance"); v.texMusic = Art("skill-music");
            v.texAmbience = Art("tile-kind-ambience"); v.texAbility = Art("tile-kind-ability"); v.texGame = Art("tile-game"); v.texDemonGate = Art("game-demon-guard"); v.texKeeper = Art("Local/item-ball"); v.texSurvival = Art("tile-survival");
            v.texTennis = Art("tile-tennis"); v.texRps = Art("tile-rps"); v.texPop = Art("tile-pop"); v.texRedLight = Art("tile-redlight");
            return v;
        }

        /// <summary>A display of the PC brought into the room: its picture in a paper frame, a line under it, a button to send it back.</summary>
        static ScreenBoardView BuildScreenBoard(Transform panels)
        {
            var p = MakePanel("ScreenBoard", panels, 0.49f, 0.33f, Ui.Pal.Cyan, "", "");
            p.transform.localPosition = new Vector3(-0.30f, 0.70f, 0);
            Hidden(p.bar);
            var root = p.transform;
            var v = p.gameObject.AddComponent<ScreenBoardView>();
            v.panel = p;
            v.placeholder = Art("tile-screen");
            v.picture = Sprite("Picture", root, "tile-screen", 0.12f, 0.12f, 6);
            v.picture.transform.localPosition = new Vector3(0, 0.02f, -0.003f);
            v.captionBg = Panel("CaptionBg", root, 0.46f, 0.03f, 0.008f, new Color(1f, 0.975f, 0.90f, 0.98f), new Color(1f, 0.94f, 0.80f, 0.98f), 1, 0.0010f);
            v.captionBg.transform.localPosition = new Vector3(0, -0.14f, -0.002f);
            v.caption = Label("Caption", root, "Nilo is looking ...", 0.0150f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.caption.transform.localPosition = new Vector3(0, -0.14f, -0.004f);
            v.close = AddPill(p, "Btn Close", "Send back", new Vector3(0.20f, 0.16f, -0.006f), 0.096f, 0.032f, 0.0140f, Rust);
            return v;
        }

        /// <summary>
        /// "Nilo made this!": the card that comes up when the partner has finished making something - a sheet of
        /// paper like the shop's detail card with the thing's picture, its name, the question whether to put it up in
        /// the marketplace, a price to step down / up, and two buttons. The app fills and places it (MrCraft).
        /// </summary>
        static MadeCardView BuildMadeCard(Transform panels)
        {
            const float W = 0.30f, H = 0.27f;
            var p = MakePanel("MadeCard", panels, W, H, Ui.Pal.Orange, "", "");
            p.transform.localPosition = new Vector3(0.84f, 0.30f, 0);
            Ui.SizePanel(p.bg, W, H, 0.030f, 0.0022f);
            Hidden(p.bar);
            var root = p.transform;
            var v = p.gameObject.AddComponent<MadeCardView>();
            v.panel = p;
            // the title on a honey ribbon that sits on the top edge of the card
            var ribbon = Panel("TitleRibbon", root, 0.200f, 0.036f, 0.018f, new Color(1f, 0.90f, 0.62f, 0.98f), new Color(1f, 0.84f, 0.50f, 0.98f), 3, 0.0016f);
            ribbon.transform.localPosition = new Vector3(0, H * 0.5f - 0.004f, -0.003f);
            Object.DestroyImmediate(p.title.gameObject);
            p.title = Label("Title", root, "Nilo made this!", 0.0180f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            p.title.transform.localPosition = new Vector3(0, H * 0.5f - 0.004f, -0.005f);
            // the thing: its picture (on a pastel disc when it is a picture of its own), its name, what kind of thing it is
            v.disc = Panel("Disc", root, 0.098f, 0.098f, 0.049f, new Color(1f, 0.89f, 0.72f), new Color(1f, 0.89f, 0.72f), 3, 0.0015f);
            v.disc.transform.localPosition = new Vector3(0, 0.056f, -0.0025f);
            v.icon = Sprite("Icon", root, "item-any", 0.112f, 0.112f, 7);
            v.icon.transform.localPosition = new Vector3(0, 0.056f, -0.0045f);
            v.thingName = Label("Name", root, "Chair", 0.0190f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.thingName.transform.localPosition = new Vector3(0, -0.006f, -0.004f);
            v.kind = Label("Kind", root, "Furniture", 0.0112f, Ui.Pal.TextDim, TextAnchor.MiddleCenter);
            v.kind.transform.localPosition = new Vector3(0, -0.024f, -0.004f);
            var ask = Label("Ask", root, "Put it up in the marketplace?", 0.0135f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            ask.transform.localPosition = new Vector3(0, -0.042f, -0.004f);
            // the price, one step down / up
            v.minus = AddPill(p, "Btn Minus", "-", new Vector3(-0.058f, -0.068f, 0), 0.036f, 0.028f, 0.0180f, Purple);
            v.plus = AddPill(p, "Btn Plus", "+", new Vector3(0.058f, -0.068f, 0), 0.036f, 0.028f, 0.0180f, Purple);
            v.priceLabel = Label("PriceLabel", root, "10 stars", 0.0140f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            v.priceLabel.transform.localPosition = new Vector3(0, -0.068f, -0.004f);
            v.sell = AddPill(p, "Btn Sell", "Sell it", new Vector3(-0.066f, -0.106f, 0), 0.118f, 0.034f, 0.0150f, Green);
            v.keep = AddPill(p, "Btn Keep", "Keep it", new Vector3(0.066f, -0.106f, 0), 0.118f, 0.034f, 0.0150f, Ui.Pal.PanelBottom);
            return v;
        }

        static readonly Color Teal = Ui.Pal.Cyan, Purple = Ui.Pal.Violet, Rust = Ui.Pal.Pink, Green = Ui.Pal.Mint;      // sky, lavender, rose, sage

        static GameObject BuildTimerPanel()
        {
            var p = MakePanel("TimerPanel", null, 0.15f, 0.10f, Ui.Pal.Mint, "Timer · v1", "             25:00\n             ready");
            var v = p.gameObject.AddComponent<TimerPanelView>();
            v.panel = p;
            v.ring = Fx("Ring", p.transform, 4, Ui.Pal.Cyan, new Vector4(0.78f, 0.17f, 1f, 0), 3, Ui.Pal.Pink);
            v.ring.transform.localScale = Vector3.one * 0.046f; v.ring.transform.localPosition = new Vector3(-0.045f, 0.008f, -0.002f);
            v.toggle = AddBtn(p, "Btn Toggle", "Go", new Vector3(-0.054f, -0.034f, 0), 0.034f, Green);
            v.reset = AddBtn(p, "Btn Reset", "Reset", new Vector3(-0.018f, -0.034f, 0), 0.034f, Purple);
            v.plus = AddBtn(p, "Btn Plus5", "+5m", new Vector3(0.018f, -0.034f, 0), 0.034f, Purple);
            v.minus = AddBtn(p, "Btn Minus5", "-5m", new Vector3(0.054f, -0.034f, 0), 0.034f, Purple);
            v.display = AddBtn(p, "Btn Display", "View", new Vector3(0.054f, 0.034f, 0), 0.03f, Purple);
            return SavePrefab(p.gameObject, "TimerPanel");
        }

        // ------------------------------------------------------------------ prefab: MrCore (shared by Quest and the simulator)
        static GameObject BuildMrCore(GameObject partnerPrefab, GameObject portalPrefab, GameObject sparkPrefab, GameObject timerPrefab)
        {
            var root = Node("MrCore", null);
            var app = root.AddComponent<MrApp>();
            Transform t = root.transform;

            var rig = Instance<PartnerRig>(partnerPrefab, t);
            rig.transform.localPosition = new Vector3(0, 0.75f, 0.35f);       // authoring pose only; the app places it on the desk

            var portalRoot = Node("Portal", t).transform;
            var portal = Instance<PortalFx>(portalPrefab, portalRoot);
            var sparks = Instance<SparkFx>(sparkPrefab, portalRoot);
            var ring = Hidden(Fx("PortalRing", portalRoot, 1, new Color(0.99f, 0.83f, 0.56f, 0.9f), new Vector4(0.88f, 0.045f, 0, 0), 2));

            var pointing = Node("Pointing", t).transform;
            var ptr = Hidden(Fx("Pointer", pointing, 8, Color.white, new Vector4(5f, 0, 0, 0), 4, Ui.Pal.Cyan));
            ptr.transform.localScale = Vector3.one * 0.026f;
            var marker = Hidden(Fx("TargetMarker", pointing, 7, Ui.Pal.Cyan, new Vector4(0.84f, 0.075f, 10f, 0.62f), 3, Ui.Pal.Pink));
            marker.transform.localScale = Vector3.one * 0.12f;
            var markerLabel = Label("MarkerLabel", pointing, "", 0.014f, Color.white);

            var captions = Node("Captions", t).transform;
            var captionBg = Hidden(Panel("CaptionBg", captions, 0.1f, 0.03f, 0.01f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 6, 0.0010f));
            var caption = Label("Caption", captions, "", 0.021f, Ui.Pal.Ink);
            var dbg = Label("Debug", captions, "", 0.010f, new Color(1f, 1f, 0.3f), TextAnchor.UpperLeft);
            // what the partner heard the user say, for a few seconds: a small strip of paper under its speech bubble
            var heardBg = Hidden(Panel("HeardBg", captions, 0.1f, 0.022f, 0.009f, new Color(0.97f, 0.95f, 0.90f, 0.96f), new Color(0.94f, 0.91f, 0.85f, 0.96f), 6, 0.0010f));
            var heard = Label("Heard", captions, "", 0.0175f, Ui.Pal.TextDim);

            // Pressing from a distance, the way the headset's own menus are pressed: a thin soft ray from the hand to the
            // board it points at, and a round cursor on the board; a pinch presses what is under the cursor (MrUiRay).
            // One of each per hand; drawn over the boards and their lettering.
            var rayRoot = Node("UiRay", t).transform;
            var uiRays = new FxQuad[2]; var uiCursors = new FxQuad[2];
            for (int i = 0; i < 2; i++)
            {
                uiRays[i] = Hidden(Panel(i == 0 ? "Ray R" : "Ray L", rayRoot, 0.0034f, 0.30f, 0.0017f, new Color(1f, 0.97f, 0.88f, 0.85f), new Color(1f, 0.97f, 0.88f, 0.0f), MenuOrder + 219));
                uiRays[i].transform.localPosition = new Vector3(i == 0 ? 0.2f : -0.2f, 1.0f, 0.2f);
                uiCursors[i] = Hidden(Panel(i == 0 ? "Cursor R" : "Cursor L", rayRoot, 0.015f, 0.015f, 0.0075f, new Color(1f, 0.99f, 0.95f, 0.96f), new Color(1f, 0.95f, 0.84f, 0.96f), MenuOrder + 220, 0.0016f));
                uiCursors[i].transform.localPosition = new Vector3(i == 0 ? 0.2f : -0.2f, 1.15f, 0.2f);
            }

            var monitors = Node("Monitors", t).transform;
            var monPool = new MonView[6];
            for (int i = 0; i < monPool.Length; i++)
            {
                var m = Node("Monitor " + i, monitors);
                m.transform.localPosition = new Vector3((i - 2.5f) * 0.62f, 1.02f, 0.62f);
                monPool[i] = m.AddComponent<MonView>();
                monPool[i].outline = Fx("Outline", m.transform, 3, new Color(0.99f, 0.83f, 0.56f, 0.55f), new Vector4(0, 0.012f, 0, 0), 1);
                monPool[i].outline.transform.localScale = new Vector3(0.53f, 0.30f, 1);
                m.SetActive(false);
            }

            var rain = Node("Rain", t).transform;
            rain.localPosition = new Vector3(0, 0.95f, 0.35f);                // authoring pose only: a little cloud above the desk
            var dropsRoot = Node("Drops", rain).transform;
            var drops = new FxQuad[110];
            for (int i = 0; i < drops.Length; i++)
            {
                // rain streaks: thin soft lines, a little different in length and strength so it does not look stamped (LPFx mode 9)
                float vary = 0.75f + 0.5f * (((i * 61) % 17) / 16f);
                drops[i] = Hidden(Fx("Drop " + i, dropsRoot, 9, new Color(Water.r, Water.g, Water.b, Water.a * (0.55f + 0.45f * (((i * 23) % 11) / 10f))), Vector4.zero, 6));
                drops[i].transform.localScale = new Vector3(DropSize.x, DropSize.y * vary, 1f);
                drops[i].transform.localPosition = new Vector3(((i * 37) % 110 - 55) * 0.005f, ((i * 53) % 110) * 0.0026f, ((i * 29) % 110 - 55) * 0.004f);
            }
            var water = BuildWater(rain);

            // panels: laid out in a row for the Scene view; at runtime each one is placed in front of the user when shown
            var panels = Node("Panels", t).transform;
            panels.localPosition = new Vector3(0, 1.25f, 0.5f);

            var menu = BuildMenuBoard(panels);
            AuthorU1Menu(menu);                        // the menu is a carousel: the arrows under the ring, the ribbon over it (LPSceneU1.cs)

            var pairP = MakePanel("Pairing", panels, 0.20f, 0.20f, Ui.Pal.Violet, "Enter the 6-digit code from the PC tray", "Code: ······");
            pairP.transform.localPosition = new Vector3(-0.12f, 0, 0);
            var pair = pairP.gameObject.AddComponent<PairingView>();
            pair.panel = pairP;
            pair.digits = new UiButton[10];
            for (int digit = 0; digit < 10; digit++)
            {
                int idx = digit == 0 ? 10 : digit - 1;
                pair.digits[digit] = AddBtn(pairP, "Btn " + digit, digit.ToString(), new Vector3(-0.05f + (idx % 3) * 0.05f, 0.03f - (idx / 3) * 0.036f, 0), 0.044f, Teal);
            }
            pair.erase = AddBtn(pairP, "Btn Erase", "Del", new Vector3(-0.05f, -0.078f, 0), 0.044f, Rust);
            pair.connect = AddBtn(pairP, "Btn Connect", "OK", new Vector3(0.05f, -0.078f, 0), 0.044f, Green);

            var calibP = MakePanel("Calibration", panels, 0.24f, 0.11f, Ui.Pal.Pink, "Calibrate screen", "Touch the lit corner pixel with your\nindex fingertip and pinch.");
            calibP.transform.localPosition = new Vector3(0.14f, 0, 0);
            var calib = calibP.gameObject.AddComponent<CalibrationView>();
            calib.panel = calibP;
            calib.skip = AddBtn(calibP, "Btn Skip", "Skip", new Vector3(-0.06f, -0.034f, 0), 0.05f, Purple);
            calib.finish = AddBtn(calibP, "Btn Finish", "Finish", new Vector3(0.06f, -0.034f, 0), 0.05f, Rust);

            var shop = BuildMarketBoard(panels);
            var screenBoard = BuildScreenBoard(panels);
            var madeCard = BuildMadeCard(panels);

            var taskP = MakePanel("TaskPanel", panels, 0.26f, 0.13f, Ui.Pal.Orange, "Result", "Changed: -\nBranch: -\nTests: -");
            taskP.transform.localPosition = new Vector3(0.42f, 0, 0);
            var task = taskP.gameObject.AddComponent<TaskPanelView>();
            task.panel = taskP;
            task.approve = AddBtn(taskP, "Btn Approve", "Approve", new Vector3(-0.07f, -0.046f, 0), 0.055f, Green);
            task.preview = AddBtn(taskP, "Btn Preview", "Preview", new Vector3(0.0f, -0.046f, 0), 0.055f, Teal);
            task.close = AddBtn(taskP, "Btn Close", "Close", new Vector3(0.07f, -0.046f, 0), 0.055f, Rust);

            var timers = Node("Timers", t).transform;
            timers.localPosition = new Vector3(0, 1.05f, 0.5f);
            var timerPool = new TimerPanelView[4];
            for (int i = 0; i < timerPool.Length; i++)
            {
                timerPool[i] = Instance<TimerPanelView>(timerPrefab, timers, "Timer " + i);
                timerPool[i].transform.localPosition = new Vector3((i - 1.5f) * 0.17f, 0, 0);
            }

            // tennis, the room half: the ball where it is in front of the glass, its shadow on the desk, a ring on each
            // palm (the racket) and the score above the screen. Switched on while a game runs.
            var tennis = Node("Tennis", t);
            tennis.transform.localPosition = new Vector3(0, 0.95f, 0.30f);        // authoring pose only
            var tnBall = Ball("Ball", tennis.transform);
            var tnShadow = Hidden(Fx("BallShadow", tennis.transform, 0, new Color(0.18f, 0.14f, 0.16f, 0.36f), new Vector4(2.2f, 0, 0, 0), 1));
            tnShadow.transform.localPosition = new Vector3(0, -0.15f, 0); tnShadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            tnShadow.transform.localScale = Vector3.one * Tennis.Radius * 3f;
            var tnPaddles = new FxQuad[2];
            for (int i = 0; i < 2; i++)
            {
                tnPaddles[i] = Hidden(Fx(i == 0 ? "Racket R" : "Racket L", tennis.transform, 1, new Color(Ui.Pal.Mint.r, Ui.Pal.Mint.g, Ui.Pal.Mint.b, 0.95f), new Vector4(0.90f, 0.07f, 0, 0), 4, Ui.Pal.Cyan));
                tnPaddles[i].transform.localPosition = new Vector3(i == 0 ? 0.15f : -0.15f, -0.05f, -0.1f);
                tnPaddles[i].transform.localScale = Vector3.one * Tennis.PaddleRadius * 2f;
            }
            var tnScoreBg = Panel("ScoreBoard", tennis.transform, 0.24f, 0.05f, 0.014f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 5, 0.0014f);
            tnScoreBg.transform.localPosition = new Vector3(0, 0.30f, 0.30f);
            var tnScore = Label("Score", tennis.transform, "You  0 : 0  Nilo", 0.019f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            tnScore.transform.localPosition = new Vector3(0, 0.30f, 0.298f);
            var tnRipple = Hidden(Fx("GlassRipple", tennis.transform, 1, new Color(1f, 0.90f, 0.55f, 1f), new Vector4(0.88f, 0.09f, 0, 0), 7, new Color(1f, 0.98f, 0.9f, 1f)));
            var tnMat = Hidden(Panel("CourtMat", tennis.transform, 0.40f, 0.30f, 0.02f, new Color(0.71f, 0.86f, 0.72f, 0.80f), new Color(0.62f, 0.80f, 0.66f, 0.80f), 0, 0.004f));
            tnMat.transform.localRotation = Quaternion.Euler(90, 0, 0); tnMat.transform.localPosition = new Vector3(0, -0.25f, 0.15f);
            var tnNet = Hidden(Panel("Net", tennis.transform, 0.42f, Tennis.NetHeight, 0.006f, new Color(1f, 0.985f, 0.945f, 0.85f), new Color(0.985f, 0.945f, 0.875f, 0.85f), 2, 0.003f));
            tnNet.transform.localPosition = new Vector3(0, -0.23f, 0);
            var roomGo = Node("RoomCourt", t);       // the frame of a court on the desk (z = 0 is the net); only its pose is used
            var tnRoom = roomGo.AddComponent<MonView>();
            tnRoom.outline = Hidden(Fx("Outline", roomGo.transform, 3, Color.clear, new Vector4(0, 0.012f, 0, 0), 1));
            tennis.SetActive(false);
            var games = Node("Games", t);
            games.transform.localPosition = new Vector3(0.3f, 1.0f, 0.3f);         // authoring pose only
            var gameBg = Panel("Board", games.transform, 0.22f, 0.06f, 0.014f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 5, 0.0014f);
            var gameLabel = Label("Line", games.transform, "You  0 : 0  Nilo\nRock, paper, scissors!", 0.0145f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            gameLabel.transform.localPosition = new Vector3(0, 0, -0.002f);
            var gameSign = Label("Sign", games.transform, "ROCK", 0.030f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            gameSign.transform.localPosition = new Vector3(0, -0.06f, -0.002f);
            var rpsYou = Sprite("Rps You", games.transform, "rps-rock", 0.074f, 0.074f, 9);
            var rpsIt = Sprite("Rps Nilo", games.transform, "rps-rock", 0.074f, 0.074f, 9);
            rpsYou.enabled = false; rpsYou.Renderer.enabled = false; rpsIt.enabled = false; rpsIt.Renderer.enabled = false;
            var rpsTex = new[] { Art("rps-rock"), Art("rps-paper"), Art("rps-scissors") };
            var popStars = new UiSprite[8];
            for (int i = 0; i < popStars.Length; i++) { popStars[i] = Sprite("Pop Star " + i, games.transform, "star", 0.046f, 0.046f, 9); popStars[i].enabled = false; popStars[i].Renderer.enabled = false; }
            // (the creatures, gems and star pulse of the old survival game are gone: the glass games have their own stage, AuthorGlass)
            games.SetActive(false);

            // (Nothing is authored for the user's hands: in passthrough they see their own. The lights on the fingertips
            // and the rings on the palms are gone - "too much design on the hands".)

            // Sound effects: six voices that are put where something happens in the room (a hammer blow, a landing, a
            // pop), and one that is not anywhere - for the interface. The sounds themselves are made at run time
            // (SfxSynth); MrSound plays them.
            var soundRoot = Node("Sound", t).transform;
            var sfx = new AudioSource[6];
            for (int i = 0; i < sfx.Length; i++)
            {
                sfx[i] = Node("Sfx " + i, soundRoot).AddComponent<AudioSource>();
                sfx[i].playOnAwake = false; sfx[i].loop = false; sfx[i].spatialBlend = 1f; sfx[i].dopplerLevel = 0f;
                sfx[i].rolloffMode = AudioRolloffMode.Linear; sfx[i].minDistance = 0.3f; sfx[i].maxDistance = 6f;      // full within arm's reach, gone across the room
            }
            var sfx2d = Node("Sfx Interface", soundRoot).AddComponent<AudioSource>();
            sfx2d.playOnAwake = false; sfx2d.loop = false; sfx2d.spatialBlend = 0f;

            // the partner's things: a pool of objects that are given a shape, a size and a place on the desk
            var thingsRoot = Node("Things", t).transform;
            var props = new PropView[10];
            for (int i = 0; i < props.Length; i++)
            {
                var go = Node("Thing " + i, thingsRoot);
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = _ballMesh;        // a stand-in until it is given its shape
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _propMat; mr.enabled = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                go.transform.localPosition = new Vector3((i - 4.5f) * 0.12f, 0.95f, 0.3f); go.transform.localScale = Vector3.one * 0.08f;
                props[i] = go.AddComponent<PropView>();
                props[i].Bind(mf, mr);
                props[i].shadow = Hidden(Fx("Shadow " + i, thingsRoot, 0, new Color(0.18f, 0.14f, 0.16f, 0.30f), new Vector4(2.2f, 0, 0, 0), 1));
                props[i].shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
                props[i].progress = Hidden(Fx("Progress " + i, thingsRoot, 4, new Color(1f, 0.84f, 0.42f, 1f), new Vector4(0.78f, 0.20f, 0.1f, 0), 12, new Color(1f, 0.96f, 0.86f, 1f)));
            }

            // the surroundings: a large sphere round the head (LPAmbience leaves the work area alone)
            var domeGo = Node("Ambience", t);
            domeGo.AddComponent<MeshFilter>().sharedMesh = _ballMesh;
            var domeR = domeGo.AddComponent<MeshRenderer>();
            domeR.sharedMaterial = _ambienceMat; domeR.enabled = false;
            domeR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; domeR.receiveShadows = false;
            domeR.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; domeR.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            domeGo.transform.localScale = Vector3.one * 14f;
            var ambience = domeGo.AddComponent<AmbienceView>();
            ambience.Bind(domeR);

            // the tune (its notes are turned into sound at run time)
            var music = Node("Music", t).AddComponent<AudioSource>();
            music.playOnAwake = false; music.loop = true; music.spatialBlend = 0f; music.volume = 0f;

            // the first seconds: the word mark, a turning ring, a line that says what is happening
            var introRoot = Node("Intro", t).transform;
            introRoot.localPosition = new Vector3(0, 1.3f, 0.6f);
            var introRing = Hidden(Fx("Ring", introRoot, 7, new Color(1f, 0.84f, 0.42f, 0.9f), new Vector4(0.90f, 0.035f, 18f, 0.5f), 38, new Color(1f, 0.96f, 0.86f, 0.9f)));
            introRing.transform.localScale = Vector3.one * 0.34f;
            var introLogo = Sprite("Logo", introRoot, "logo", 0.43f, 0.146f, 40);
            introLogo.enabled = false; introLogo.Renderer.enabled = false;
            var introBg = Hidden(Panel("LineBg", introRoot, 0.2f, 0.03f, 0.012f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 39, 0.0014f));
            introBg.transform.localPosition = new Vector3(0, -0.075f, 0.002f);
            var introLine = Label("Line", introRoot, "", 0.022f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            introLine.transform.localPosition = new Vector3(0, -0.075f, 0);

            var introGlow = Hidden(Fx("Glow", introRoot, 8, new Color(1f, 0.88f, 0.56f, 0.6f), new Vector4(2.2f, 0, 0, 0), 34, new Color(1f, 0.97f, 0.86f, 0.6f)));
            introGlow.transform.localScale = new Vector3(1.2f, 0.66f, 1f); introGlow.transform.localPosition = new Vector3(0, 0, 0.035f);
            var introWave = Hidden(Fx("Wave", introRoot, 1, new Color(1f, 0.84f, 0.42f, 0.85f), new Vector4(0.93f, 0.035f, 0, 0), 36, new Color(1f, 0.97f, 0.86f, 0.85f)));
            introWave.transform.localScale = Vector3.one * 0.6f;
            string[] orbitArt = { "tile-tennis", "skill-dance", "tile-market", "tile-pop", "skill-music", "tile-rps", "tile-look", "tile-survival" };
            var introOrbit = new UiSprite[orbitArt.Length];
            for (int i = 0; i < introOrbit.Length; i++)
            {
                introOrbit[i] = Sprite("Badge " + i, introRoot, orbitArt[i], 0.07f, 0.07f, 42);
                introOrbit[i].transform.localPosition = new Vector3(Mathf.Cos(i * 0.785f) * 0.5f, Mathf.Sin(i * 0.785f) * 0.225f, 0);
                introOrbit[i].enabled = false; introOrbit[i].Renderer.enabled = false;
            }
            var introStars = new UiSprite[16];
            for (int i = 0; i < introStars.Length; i++)
            {
                introStars[i] = Sprite("Star " + i, introRoot, i % 3 == 0 ? "spark" : "star", 0.026f, 0.026f, 37);
                introStars[i].transform.localPosition = new Vector3(Mathf.Cos(i * 2.399f) * 0.4f, Mathf.Sin(i * 2.399f) * 0.2f, 0.012f);
                introStars[i].enabled = false; introStars[i].Renderer.enabled = false;
            }

            // the shooting star a pressed badge sends to the partner (it crosses the room, so it is not part of the menu)
            var comet = Sprite("Comet", t, "glow", 0.05f, 0.05f, MenuOrder + 130);      // (it starts on a board: over the board, and over its lettering)
            comet.enabled = false; comet.Renderer.enabled = false;
            // ... and the smaller one that carries a thing the partner has made from its card into the bag (the left palm)
            var bagComet = Sprite("BagComet", t, "glow", 0.032f, 0.032f, MenuOrder + 130);
            bagComet.enabled = false; bagComet.Renderer.enabled = false;

            // Making (MrCraft): light gathers over the partner while it makes something - a pool of motes that stream in,
            // the ball of light they gather into with its halo, a tiny strip of paper under it that says what is being
            // done - and the sticker of what was made, which flies from the burst into the bag. (A workbench with a
            // parcel and a hammer stood here: "only an effect of light gathering is needed" - the user, 2026-11-17.)
            var makingRoot = Node("Making", t).transform;
            makingRoot.localPosition = new Vector3(0.12f, 0.95f, 0.35f);       // authoring pose only
            var lightMotes = new FxQuad[30];
            for (int i = 0; i < lightMotes.Length; i++)
            {
                lightMotes[i] = Hidden(Fx("Mote " + i, makingRoot, 8, new Color(1f, 0.86f, 0.50f, 0.9f), new Vector4(3.0f, 0, 0, 0), 21, new Color(1f, 0.98f, 0.88f, 0.9f)));
                lightMotes[i].transform.localPosition = new Vector3(Mathf.Cos(i * 2.399f) * 0.12f, Mathf.Sin(i * 2.399f) * 0.08f, 0);
                lightMotes[i].transform.localScale = Vector3.one * 0.02f;
            }
            var lightHalo = Hidden(Fx("Halo", makingRoot, 8, new Color(1f, 0.84f, 0.45f, 0.4f), new Vector4(3.2f, 0, 0, 0), 19, new Color(1f, 0.95f, 0.78f, 0.4f)));      // (it has faded to nothing well inside its quad)
            lightHalo.transform.localScale = Vector3.one * 0.16f; lightHalo.transform.localPosition = new Vector3(0, 0, 0.004f);
            var lightBall = Hidden(Fx("Ball", makingRoot, 8, new Color(1f, 0.90f, 0.58f, 0.95f), new Vector4(3.6f, 0, 0, 0), 20, new Color(1f, 0.99f, 0.93f, 0.95f)));
            lightBall.transform.localScale = Vector3.one * 0.08f;
            var lightStripBg = Hidden(Panel("StripBg", makingRoot, 0.07f, 0.014f, 0.005f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 22, 0.0009f));
            lightStripBg.transform.localPosition = new Vector3(0, -0.045f, 0.002f);
            var lightStrip = Ordered(Label("Strip", makingRoot, "", 0.0092f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true), 123);
            lightStrip.transform.localPosition = new Vector3(0, -0.045f, 0);
            var madeSticker = HiddenSprite(Sprite("MadeSticker", makingRoot, "item-any", 0.07f, 0.07f, MenuOrder + 131));      // (it ends on the palm, where the fan is: over the fan)

            Wire(app, "_rig", rig, "_portalFx", portal, "_sparks", sparks, "_ring", ring,
                "_tnRoot", tennis, "_tnBall", tnBall, "_tnBallShadow", tnShadow, "_tnPaddles", tnPaddles, "_tnScoreBg", tnScoreBg, "_tnScore", tnScore, "_tnRoomView", tnRoom, "_tnMat", tnMat, "_tnNet", tnNet, "_tnRipple", tnRipple,
                "_gameRoot", games, "_gameBg", gameBg, "_gameLabel", gameLabel, "_gameSign", gameSign, "_comet", comet,
                "_rpsYou", rpsYou, "_rpsIt", rpsIt, "_rpsTextures", rpsTex, "_popStars", popStars, "_sfx", sfx, "_sfx2d", sfx2d,
                "_props", props, "_ambience", ambience, "_music", music, "_introLogo", introLogo, "_introRing", introRing, "_introBg", introBg, "_introLine", introLine,
                "_ptrDot", ptr, "_marker", marker, "_markerLabel", markerLabel,
                "_captionBg", captionBg, "_caption", caption, "_dbg", dbg, "_monitorPool", monPool,
                "_menu", menu, "_pair", pair, "_calib", calib, "_task", task, "_uiRays", uiRays, "_uiCursors", uiCursors, "_heardBg", heardBg, "_heard", heard,
                "_shop", shop, "_screenBoard", screenBoard, "_made", madeCard, "_bagComet", bagComet,
                "_lightMotes", lightMotes, "_lightBall", lightBall, "_lightHalo", lightHalo, "_lightStripBg", lightStripBg, "_lightStrip", lightStrip, "_madeSticker", madeSticker,
                "_introGlow", introGlow, "_introWave", introWave, "_introOrbit", introOrbit, "_introStars", introStars,
                "_timerPool", timerPool, "_drops", drops, "_water", water);
            AuthorDocs(app, t, panels);                // the boards of the documents the partner makes (LPSceneDocs.cs) - before AuthorRoom, which lists every panel
            AuthorRoomScript(app, t, panels);          // the little objects, labels, glows and the strip of buttons of the things' scripts (LPSceneRoomScript.cs)
            AuthorLocal(app, t, panels);               // playing without the PC: the card with "Play now", the hint of the first steps, the local stickers (LPSceneLocal.cs)
            AuthorSwarm(app, t, panels);               // Screen Scamps: the monsters that come out of the screen, their bolts, cards and sign (LPSceneSwarm.cs) - before AuthorRoom, which lists every panel
            AuthorU1Work(app, t, panels);              // the work queue's strip: a handle and three cards (LPSceneU1.cs) - before AuthorRoom, which lists every panel
            AuthorRoom(app, t, panels, pointing);      // pressing things, the record picker, the pointer ring and ray, the shows (LPSceneRoom.cs)
            AuthorGlass(app, t);                       // the stage of a glass game on the desk (LPSceneGlass.cs)
            AuthorCraftBench(app, t);                  // the bench the partner hammers at while it makes something, its card with the percentage (LPSceneU1Craft.cs)
            AuthorU1Ball(app, t);                      // what a glass game sends out of the screen: real balls, their shadows and trails (LPSceneU1Ball.cs)
            AuthorEyes(app, t);                        // the sign "Nilo is looking" while it looks at what the user holds up (LPSceneEyes.cs)
            AuthorTvRally(app, t);
            return SavePrefab(root, "MrCore");
        }

        // ------------------------------------------------------------------ toon water (rain splashes, hand film)
        static Color Water => Ui.Pal.Rain;                                    // clear water: a cool white, never a saturated blue
        static readonly Vector2 DropSize = new Vector2(0.0030f, 0.052f);      // a streak about 3 mm wide (soft edges) and 5 cm long

        /// <summary>MrCore/Rain/Water: pools of splash crowns, beads and glints, plus one anchor with a water film per hand.</summary>
        static WaterFx BuildWater(Transform rain)
        {
            var root = Node("Water", rain);
            var fx = root.AddComponent<WaterFx>();

            var crownRoot = Node("Crowns", root.transform).transform;
            var crowns = new FxQuad[40];
            for (int i = 0; i < crowns.Length; i++)
            {
                crowns[i] = Hidden(Fx("Crown " + i, crownRoot, 10, Water, new Vector4(7f, 0, 0.35f, i * 0.37f), 7));
                crowns[i].transform.localScale = Vector3.one * 0.034f;
                crowns[i].transform.localRotation = Quaternion.Euler(90, 0, 0);
                crowns[i].transform.localPosition = new Vector3((i % 8 - 3.5f) * 0.04f, -0.17f, (i / 8 - 2f) * 0.04f);
            }

            var beadRoot = Node("Beads", root.transform).transform;
            var beads = new FxQuad[60];
            for (int i = 0; i < beads.Length; i++)
            {
                beads[i] = Hidden(Fx("Bead " + i, beadRoot, 12, Water, Vector4.zero, 8));
                beads[i].transform.localScale = Vector3.one * 0.008f;
                beads[i].transform.localPosition = new Vector3((i % 10 - 4.5f) * 0.012f, -0.13f + (i / 10) * 0.012f, 0);
            }

            var glintRoot = Node("Glints", root.transform).transform;
            var glints = new FxQuad[10];
            for (int i = 0; i < glints.Length; i++)
            {
                glints[i] = Hidden(Fx("Glint " + i, glintRoot, 5, Color.white, new Vector4(1.4f, 0, 0, 0), 9, Color.white));
                glints[i].transform.localScale = Vector3.one * 0.016f;
            }

            // the top of each hand: the MR app places these every frame; a wet hand shows the film and drips from its rim
            var anchors = new Transform[2]; var films = new FxQuad[2];
            for (int h = 0; h < 2; h++)
            {
                anchors[h] = Node(h == 0 ? "HandAnchor R" : "HandAnchor L", root.transform).transform;
                anchors[h].localPosition = new Vector3(h == 0 ? 0.12f : -0.12f, -0.02f, 0);
                anchors[h].localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                films[h] = Hidden(Fx("Film", anchors[h], 11, new Color(Water.r, Water.g, Water.b, 0.95f), new Vector4(0, 0, 0.8f, 0), 6));
                films[h].transform.localScale = new Vector3(0.105f, 0.170f, 1f);
                films[h].transform.localPosition = new Vector3(0, 0, -0.001f);
            }

            Wire(fx, "_crowns", crowns, "_beads", beads, "_glints", glints, "_handAnchors", anchors, "_handFilms", films);
            return fx;
        }

        // ------------------------------------------------------------------ scenes
        static void BuildMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Node("LivingPartner", null).AddComponent<AppBootstrap>();
            EditorSceneManager.SaveScene(scene, MainScene);
        }

        static void BuildPcScene(GameObject partnerPrefab, GameObject portalPrefab, GameObject sparkPrefab, List<string> problems)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = Node("PcCharacter", null);
            var app = root.AddComponent<PcApp>();
            Transform t = root.transform;

            // renders only the character and its effects into the transparent layered overlay window
            var camGo = Node("OverlayCamera", t);
            camGo.AddComponent<AudioListener>();      // the partner's voice on the PC speakers
            camGo.transform.localPosition = new Vector3(0, 0, -0.6f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer; cam.allowHDR = false; cam.allowMSAA = true;
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 6f; cam.useOcclusionCulling = false; cam.fieldOfView = 30f;
            cam.enabled = false;                                       // rendered explicitly by PcApp (the player window is hidden)
            var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = false; extra.renderShadows = false; extra.antialiasing = AntialiasingMode.None;

            var rig = Instance<PartnerRig>(partnerPrefab, t);
            rig.transform.localPosition = new Vector3(0, -0.05f, PlanBuilder.ResidentDepth(PartnerHeight));
            rig.transform.localRotation = Quaternion.Euler(0, 180f, 0);

            var portalRoot = Node("Portal", t).transform;
            var portal = Instance<PortalFx>(portalPrefab, portalRoot);
            var sparks = Instance<SparkFx>(sparkPrefab, portalRoot);
            var ring = Hidden(Fx("PortalRing", portalRoot, 1, new Color(0.99f, 0.83f, 0.56f, 0.9f), new Vector4(0.88f, 0.045f, 0, 0), 0));

            var status = Node("Status", t).transform;
            var pill = Hidden(Panel("StatusPill", status, 0.03f, 0.012f, 0.006f, Color.white, Color.white, 1, 0.0008f));
            var statusLabel = Label("StatusLabel", status, "", 0.0072f, Ui.Pal.Ink);

            var say = Node("Caption", t).transform;
            var sayBg = Hidden(Panel("SayBubble", say, 0.1f, 0.03f, 0.01f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, 6, 0.0009f));
            var sayLabel = Label("SayLabel", say, "", 0.0078f, Ui.Pal.Ink);

            var debug = Node("Debug", t).transform;
            var floor = Hidden(Fx("DebugFloor", debug, 2, new Color(1f, 0.3f, 0.9f, 0.8f), new Vector4(0.05f, 0, 0, 0), 2));
            var cross = Hidden(Fx("DebugCross", debug, 3, new Color(1f, 1f, 0.2f, 0.9f), new Vector4(0, 0.08f, 0, 0), 3));
            var dbgLabel = Label("DebugLabel", debug, "", 0.009f, new Color(1f, 1f, 0.3f), TextAnchor.UpperLeft);

            // tennis, the PC half: a court in a box behind the glass. Authored for a 0.60 x 0.34 m screen; the app sizes the
            // box to the real display when a game begins. Soft picture-book colours: a sage court, sky walls, cream lines.
            var tennis = Node("Tennis", t);
            float cw = 0.60f, chh = 0.34f, cd = 0.50f;
            var flat = Quaternion.Euler(90, 0, 0);
            FxQuad Wall(string name, Vector3 pos, Quaternion rot, float w, float h, Color far, Color near, int order)
            {
                var q = Panel(name, tennis.transform, w, h, 0f, far, near, order);
                q.transform.localPosition = pos; q.transform.localRotation = rot;
                return q;
            }
            var walls = new[]
            {
                Wall("Floor", new Vector3(0, -chh * 0.5f, cd * 0.5f), flat, cw, cd, new Color(0.62f, 0.80f, 0.64f, 1f), new Color(0.74f, 0.88f, 0.73f, 1f), -33),
                Wall("Ceiling", new Vector3(0, chh * 0.5f, cd * 0.5f), flat, cw, cd, new Color(0.93f, 0.95f, 0.96f, 1f), new Color(0.99f, 0.97f, 0.92f, 1f), -33),
                Wall("BackWall", new Vector3(0, 0, cd), Quaternion.identity, cw, chh, new Color(0.72f, 0.86f, 0.95f, 1f), new Color(0.86f, 0.93f, 0.96f, 1f), -34),
                Wall("LeftWall", new Vector3(-cw * 0.5f, 0, cd * 0.5f), Quaternion.Euler(0, 90, 0), cd, chh, new Color(0.78f, 0.88f, 0.94f, 1f), new Color(0.90f, 0.94f, 0.94f, 1f), -32),
                Wall("RightWall", new Vector3(cw * 0.5f, 0, cd * 0.5f), Quaternion.Euler(0, -90, 0), cd, chh, new Color(0.82f, 0.90f, 0.95f, 1f), new Color(0.93f, 0.95f, 0.94f, 1f), -32),
            };
            Color chalk = new Color(1.0f, 0.985f, 0.94f, 0.96f);
            float fy = -chh * 0.5f + 0.0008f, inset = 0.03f, lw = 0.0045f;
            var lines = new[]
            {
                Wall("Line Left", new Vector3(-cw * 0.5f + inset, fy, cd * 0.5f), flat, lw, cd - 2 * inset, chalk, chalk, -28),
                Wall("Line Right", new Vector3(cw * 0.5f - inset, fy, cd * 0.5f), flat, lw, cd - 2 * inset, chalk, chalk, -28),
                Wall("Line Base", new Vector3(0, fy, cd - inset), flat, cw - 2 * inset, lw, chalk, chalk, -28),
                Wall("Line Glass", new Vector3(0, fy, inset), flat, cw - 2 * inset, lw, chalk, chalk, -28),
                Wall("Line Centre", new Vector3(0, fy, cd * 0.5f), flat, lw * 0.8f, cd - 2 * inset, chalk, chalk, -28),
            };
            var tnShadow = Hidden(Fx("BallShadow", tennis.transform, 0, new Color(0.16f, 0.24f, 0.18f, 0.40f), new Vector4(2.2f, 0, 0, 0), -27));
            tnShadow.transform.localPosition = new Vector3(0.05f, fy, 0.2f); tnShadow.transform.localRotation = flat;
            tnShadow.transform.localScale = Vector3.one * Tennis.Radius * 3f;
            var tnBall = Ball("Ball", tennis.transform);
            tnBall.transform.localPosition = new Vector3(0.05f, -0.05f, 0.2f);
            var tnScoreBg = Panel("ScoreBoard", tennis.transform, 0.27f, 0.070f, 0.014f, Ui.Pal.PanelTop, Ui.Pal.PanelBottom, -26, 0.0016f);
            tnScoreBg.transform.localPosition = new Vector3(0, chh * 0.5f - 0.050f, cd - 0.003f);
            var tnScore = Label("Score", tennis.transform, "You  0 : 0  Nilo", 0.024f, Ui.Pal.Ink, TextAnchor.MiddleCenter, true);
            tnScore.transform.localPosition = new Vector3(0, chh * 0.5f - 0.041f, cd - 0.006f);
            var tnNote = Label("Note", tennis.transform, "First to 5", 0.0125f, Ui.Pal.TextDim);
            tnNote.transform.localPosition = new Vector3(0, chh * 0.5f - 0.067f, cd - 0.006f);
            // the partner's racket: the app holds it in the right hand (origin at the fingertips, +y along the arm)
            var racket = Node("Racket", tennis.transform).transform;
            racket.localPosition = new Vector3(-0.06f, -chh * 0.5f + 0.06f, Tennis.PartnerDepth);
            var grip = Panel("Grip", racket, 0.0042f, 0.016f, 0.002f, new Color(0.72f, 0.55f, 0.45f, 1f), new Color(0.62f, 0.46f, 0.38f, 1f), 1);
            grip.transform.localPosition = new Vector3(0, 0.006f, 0);
            var strings = Fx("Strings", racket, 0, new Color(1f, 0.98f, 0.93f, 0.55f), new Vector4(4.4f, 0, 0, 0), 1);
            strings.transform.localPosition = new Vector3(0, 0.034f, 0); strings.transform.localScale = Vector3.one * 0.040f;
            var frame = Fx("Frame", racket, 1, Ui.Pal.Pink, new Vector4(0.80f, 0.17f, 0, 0), 2, new Color(0.93f, 0.60f, 0.62f, 1f));
            frame.transform.localPosition = new Vector3(0, 0.034f, 0); frame.transform.localScale = Vector3.one * 0.048f;
            tennis.SetActive(false);
            Wire(app, "_tnRoot", tennis, "_tnWalls", walls, "_tnLines", lines, "_tnBall", tnBall, "_tnBallShadow", tnShadow, "_tnRacket", racket,
                "_tnScoreBg", tnScoreBg, "_tnScore", tnScore, "_tnNote", tnNote);

            Wire(app, "_rig", rig, "_cam", cam, "_encodeMaterial", _encodeMat, "_portalFx", portal, "_sparks", sparks, "_ring", ring,
                "_statusDot", pill, "_statusLabel", statusLabel, "_sayBg", sayBg, "_sayLabel", sayLabel, "_sayLabelRenderer", sayLabel.GetComponent<MeshRenderer>(),
                "_floorLine", floor, "_dbgCross", cross, "_dbgLabel", dbgLabel);
            AuthorPcGlass(app, t);      // hands' shadows, glass games, shows (LPScenePc.cs)
            AuthorPcDocs(app, t);       // the file the partner holds up, to be clicked (LPScenePcDocs.cs)
            AuthorPcU2(app, t);         // the work queue panel, the operate show, the file's thumbnail (LPSceneU2.cs)
            AuthorPcSwarm(app, t);      // Screen Scamps: the monsters behind the glass of a monitor (LPSceneSwarm.cs)
            ValidateOpenScene("PcCharacter.unity", problems);
            EditorSceneManager.SaveScene(scene, PcScene);
        }

        static void BuildSimScene(GameObject mrCorePrefab, List<string> problems)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = Node("MrSimulator", null);
            var sim = root.AddComponent<MrSim>();
            Transform t = root.transform;

            var camGo = Node("SimHead", t);
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.56f, 0.62f, 0.70f, 1);
            cam.nearClipPlane = 0.03f; cam.farClipPlane = 20f; cam.fieldOfView = 62f;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            camGo.transform.position = new Vector3(0.03f, 1.34f, -0.42f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0.98f, 0.55f) - camGo.transform.position);

            var room = Node("SimRoom", t).transform;
            var desk = Fx("SimDesk", room, 2, new Color(0.80f, 0.68f, 0.55f, 1f), new Vector4(0.02f, 0, 0, 0), -20, new Color(0.74f, 0.61f, 0.48f, 1f));
            desk.transform.SetPositionAndRotation(new Vector3(0, 0.78f, 0.42f), Quaternion.Euler(90, 0, 0));
            desk.transform.localScale = new Vector3(1.9f, 0.9f, 1);
            var hand = Fx("SimHand", room, 2, new Color(0.95f, 0.78f, 0.66f, 0.85f), new Vector4(0.3f, 0, 0, 0), 0, null, Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
            hand.transform.localPosition = new Vector3(0.2f, 0.9f, 0.3f);
            hand.transform.localScale = new Vector3(0.085f, 0.012f, 0.16f);
            // (the scripted right index finger at the menu on the left palm: a short rod that ends at the fingertip)
            var finger = Fx("SimFinger", room, 2, new Color(0.93f, 0.72f, 0.60f, 0.95f), new Vector4(0.3f, 0, 0, 0), MenuOrder + 150, null, Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
            finger.transform.localPosition = new Vector3(0.3f, 0.9f, 0.3f);
            finger.transform.localScale = new Vector3(0.016f, 0.016f, 0.07f);
            var screens = new FxQuad[6];
            for (int i = 0; i < screens.Length; i++)
            {
                screens[i] = Fx("Screen " + i, room, 2, new Color(0.22f, 0.25f, 0.33f, 1f), new Vector4(0.02f, 0, 0, 0), -10, new Color(0.17f, 0.19f, 0.26f, 1f));
                screens[i].transform.localPosition = new Vector3((i - 2.5f) * 0.62f, 1.02f, 0.622f);
                screens[i].transform.localScale = new Vector3(0.53f, 0.30f, 1);
                screens[i].gameObject.SetActive(false);
            }

            var app = Instance<MrApp>(mrCorePrefab, null);
            Wire(sim, "_cam", cam, "_app", app, "_handVis", hand, "_fingerVis", finger, "_deskVis", desk, "_screenPool", screens);
            ValidateOpenScene("MrSimulator.unity", problems);
            EditorSceneManager.SaveScene(scene, SimScene);
        }
    }
}
