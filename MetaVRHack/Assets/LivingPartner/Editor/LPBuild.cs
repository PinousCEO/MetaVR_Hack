using System;
using System.IO;
using System.Linq;
using LivingPartner.Character;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LivingPartner.EditorTools
{
    /// <summary>Build entry points for the two clients produced from the one shared project.</summary>
    public static class LPBuild
    {
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Distribution => Path.GetFullPath(Environment.GetEnvironmentVariable("YONDER_BUILD_DIR") ?? Path.Combine(RepoRoot, "dist"));
        public static string LastResult = "";

        // One scene per client, all authored in the hierarchy (see LPSceneBuilder):
        //   Windows build: Main (role switch) + PcCharacter + MrSimulator.   Quest build: QuestMR only.
        static readonly string[] WindowsScenes = { LPSceneBuilder.MainScene, LPSceneBuilder.PcScene, LPSceneBuilder.SimScene };
        static readonly string[] QuestScenes = { LPSceneBuilder.QuestScene };

        static bool EnsureScenes(string[] scenes)
        {
            if (scenes.Any(s => !File.Exists(s))) LPSceneBuilder.BuildAll();
            var missing = scenes.Where(s => !File.Exists(s)).ToArray();
            if (missing.Length == 0) return true;
            LastResult = "build aborted: scenes missing: " + string.Join(", ", missing);
            Debug.LogError("[LPBuild] " + LastResult);
            return false;
        }
        static void CommonSettings()
        {
            // The companion keeps a live adb reverse tunnel to the headset while players are built.
            EditorPrefs.SetBool("AndroidADBKillServerOnExit", false);
            PlayerSettings.companyName = "LivingAIPartner";
            PlayerSettings.productName = "Yonder";
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        [MenuItem("LivingPartner/Build Windows Character Client")]
        public static void BuildWindows() => BuildWindowsTo(Path.Combine(Distribution, "LivingAIPartner-Windows"));

        public static void BuildWindowsTo(string outDir)
        {
            if (!EnsureScenes(WindowsScenes)) return;
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Standalone;
            CommonSettings();
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 480; PlayerSettings.defaultScreenHeight = 270;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.forceSingleInstance = false;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.livingaipartner.desktop");
            Directory.CreateDirectory(outDir);
            var opts = new BuildPlayerOptions
            {
                scenes = WindowsScenes,
                locationPathName = Path.Combine(outDir, "LivingAIPartner.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            Report(BuildPipeline.BuildPlayer(opts), "windows");
        }

        // Leaves Android as the active build target. Switch back to Windows in a SEPARATE editor step: doing it in the same
        // scripted command as this build froze the Editor (see IMPLEMENTATION_STATUS notes; that is also why this is not called BuildQuest any more).
        [MenuItem("LivingPartner/Build Quest APK")]
        public static void BuildQuestApk()
        {
            if (!EnsureScenes(QuestScenes)) return;
            // The OpenXR validation callback reads the selected group before BuildPlayer switches targets.
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
            CommonSettings();
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.livingaipartner.quest");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            string outDir = Path.Combine(Distribution, "LivingAIPartner-Quest");
            Directory.CreateDirectory(outDir);
            var opts = new BuildPlayerOptions
            {
                scenes = QuestScenes,
                locationPathName = Path.Combine(outDir, "LivingAIPartner.apk"),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            Report(BuildPipeline.BuildPlayer(opts), "quest");
        }

        static void Report(BuildReport report, string name)
        {
            var s = report.summary;
            var errors = report.steps.SelectMany(st => st.messages).Where(m => m.type == LogType.Error).Select(m => m.content).Take(12).ToArray();
            LastResult = $"{name}: {s.result} size={s.totalSize / 1024 / 1024}MB time={s.totalTime.TotalSeconds:0}s errors={s.totalErrors} out={s.outputPath}\n" + string.Join("\n", errors);
            Debug.Log("[LPBuild] " + LastResult);
            try { File.WriteAllText(Path.Combine(RepoRoot, "artifacts", $"build-{name}.txt"), DateTime.Now.ToString("s") + "\n" + LastResult); } catch { }
        }
    }
}
