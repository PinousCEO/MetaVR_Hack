using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace LivingPartner.EditorTools
{
    /// <summary>Configures XR Plug-in Management / OpenXR for the Quest (Android) build of the shared project.</summary>
    public static class LPQuestSetup
    {
        public static string LastReport = "";

        // OpenXR features the Quest client relies on (matched by feature type name so minor package updates keep working)
        static readonly string[] Wanted =
        {
            "MetaQuestFeature",        // Meta Quest Support
            "HandTracking",            // XR Hands: Hand Tracking Subsystem
            "MetaHandTrackingAim",     // Meta Hand Tracking Aim
            "ARSessionFeature",        // Meta Quest: Session
            "ARCameraFeature",         // Meta Quest: Camera (Passthrough)
            "DisplayUtilitiesFeature", // Meta Quest: Display Utilities
            // Composition Layers Support. REQUIRED for passthrough: Meta OpenXR 2.x submits the passthrough picture as a
            // composition layer, and without this feature that layer is never handed to the compositor - the room is
            // black and only the app's own objects show (seen on a Quest 3, 2026-11-16). It is part of the package's
            // "Meta Quest" feature set; enabling the features one by one here had left it out.
            "OpenXRCompositionLayersFeature",
            "ARRaycastFeature",        // Meta Quest: Raycasts - rays against the real room, to find the desk the partner stands on
        };

        [MenuItem("LivingPartner/Configure Quest XR")]
        public static void Configure()
        {
            var sb = new StringBuilder();
            const BuildTargetGroup group = BuildTargetGroup.Android;

            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            var manager = perTarget.ManagerSettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            bool assigned = XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            sb.AppendLine($"OpenXR loader assigned for Android: {assigned} (active loaders: {string.Join(", ", manager.activeLoaders.Select(l => l.name))})");

            FeatureHelpers.RefreshFeatures(group);
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (oxr == null) { sb.AppendLine("OpenXR settings for Android not found"); LastReport = sb.ToString(); Debug.LogError(LastReport); return; }
            oxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            foreach (var f in oxr.GetFeatures())
            {
                string n = f.GetType().Name;
                if (Wanted.Contains(n)) { f.enabled = true; EditorUtility.SetDirty(f); }
                if (n == "ARCameraFeature")
                {
                    // reading the camera's pictures (to find the displays) is a separate switch from showing passthrough
                    var fso = new SerializedObject(f);
                    var prop = fso.FindProperty("m_CameraImageSupport");
                    if (prop != null) { prop.boolValue = true; fso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(f); sb.AppendLine("  camera image support: on"); }
                    else sb.AppendLine("  camera image support: SETTING NOT FOUND");
                }
                sb.AppendLine($"  feature {n} ({f.GetType().Namespace}) enabled={f.enabled}");
            }
            EditorUtility.SetDirty(oxr); EditorUtility.SetDirty(perTarget); EditorUtility.SetDirty(manager); EditorUtility.SetDirty(general);

            // Player settings required by OpenXR on Quest
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Passthrough is drawn BEHIND the app's picture, so the picture's alpha has to reach the compositor. The Meta
            // OpenXR package lists what that needs from URP: no HDR and no terrain holes in the pipeline asset the headset
            // uses, no post-processing in its renderer, intermediate texture "Auto". With HDR left on (the template's
            // default) the room was black in the headset (first run on a Quest 3, 2026-11-16).
            sb.AppendLine(PassthroughGraphics("Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/Mobile_Renderer.asset"));
            AssetDatabase.SaveAssets();
            sb.AppendLine("Player: IL2CPP, ARM64, minSdk 32, Vulkan, linear colour, internet permission");
            LastReport = sb.ToString();
            Debug.Log("[LPQuestSetup]\n" + LastReport);
        }

        /// <summary>Sets the URP asset and renderer the headset uses to what passthrough needs. Returns a line for the report.</summary>
        public static string PassthroughGraphics(string pipelineAssetPath, string rendererPath)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(pipelineAssetPath);
            var renderer = AssetDatabase.LoadMainAssetAtPath(rendererPath);
            if (asset == null || renderer == null) return "URP assets for the headset not found: " + pipelineAssetPath + ", " + rendererPath;
            var a = new SerializedObject(asset);
            a.FindProperty("m_SupportsHDR").boolValue = false;
            a.FindProperty("m_SupportsTerrainHoles").boolValue = false;
            a.FindProperty("m_RenderScale").floatValue = 1f;
            a.ApplyModifiedPropertiesWithoutUndo();
            var r = new SerializedObject(renderer);
            r.FindProperty("postProcessData").objectReferenceValue = null;
            r.FindProperty("m_IntermediateTextureMode").intValue = 0;      // Auto
            r.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset); EditorUtility.SetDirty(renderer);
            return "URP for the headset (" + Path.GetFileName(pipelineAssetPath) + "): HDR off, terrain holes off, render scale 1, post-processing off, intermediate texture Auto";
        }
    }

    /// <summary>
    /// Adds the permissions the product needs to the generated Android manifest: reading passthrough camera frames
    /// (Passthrough Camera API), and receiving the companion's LAN discovery beacon. Passthrough display and hand tracking
    /// entries are added by the OpenXR features themselves.
    /// </summary>
    public sealed class LPAndroidManifestPatch : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 9999;

        static readonly string[] Permissions =
        {
            "horizonos.permission.HEADSET_CAMERA",
            "android.permission.CAMERA",
            "android.permission.INTERNET",
            "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.ACCESS_WIFI_STATE",
            "android.permission.CHANGE_WIFI_MULTICAST_STATE",
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) { Debug.LogWarning("[LPAndroidManifestPatch] manifest not found at " + manifest); return; }
            string xml = File.ReadAllText(manifest);
            var add = new StringBuilder();
            foreach (var p in Permissions)
                if (!xml.Contains("\"" + p + "\"")) add.AppendLine($"  <uses-permission android:name=\"{p}\" />");
            int i = xml.IndexOf("<application");
            if (i < 0) return;
            bool changed = add.Length > 0;
            if (changed) xml = xml.Insert(i, add.ToString());
            // Nothing is asked for when the app starts (Unity would ask for every dangerous permission of the manifest at
            // once, before anything is seen): the app asks itself, each at its moment - spatial data to find the desk, the
            // microphone when a PC is there to listen, the headset's camera when the PC's screens are looked for.
            const string Skip = "unityplayer.SkipPermissionsDialog";
            if (!xml.Contains(Skip))
            {
                int app = xml.IndexOf("<application"), close = app >= 0 ? xml.IndexOf('>', app) : -1;
                if (close > 0 && xml[close - 1] != '/')
                {
                    xml = xml.Insert(close + 1, "\n    <meta-data android:name=\"" + Skip + "\" android:value=\"true\" />");
                    changed = true;
                    Debug.Log("[LPAndroidManifestPatch] added " + Skip + " = true (no permission dialog at launch)");
                }
                else Debug.LogWarning("[LPAndroidManifestPatch] no <application ...> element to put " + Skip + " into");
            }
            if (!changed) return;
            File.WriteAllText(manifest, xml);
            if (add.Length > 0) Debug.Log("[LPAndroidManifestPatch] added permissions:\n" + add);
        }
    }
}
