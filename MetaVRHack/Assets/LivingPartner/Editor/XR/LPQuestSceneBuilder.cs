using System.Collections.Generic;
using LivingPartner.MR;
using LivingPartner.Quest;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

namespace LivingPartner.EditorTools
{
    /// <summary>
    /// Authors Scenes/QuestMR.unity: the XR Origin with the tracked passthrough camera, the AR Session and the shared MrCore
    /// prefab are real objects in the hierarchy, and QuestApp gets them through serialized references.
    /// Lives in the XR editor assembly because it needs the XR packages; LPSceneBuilder.BuildAll calls it.
    /// </summary>
    public static class LPQuestSceneBuilder
    {
        [InitializeOnLoadMethod]
        static void Hook() { LPSceneBuilder.QuestSceneAuthor = Build; }

        static bool Build(List<string> problems)
        {
            var mrCore = AssetDatabase.LoadAssetAtPath<GameObject>(LPSceneBuilder.MrCorePrefab);
            if (mrCore == null) { problems.Add("QuestMR.unity: MrCore prefab missing"); return false; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var appGo = new GameObject("QuestMR");
            var quest = appGo.AddComponent<QuestApp>();

            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(offset.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.2f, 0);                                   // authoring pose; the headset drives it
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);   // alpha 0 = passthrough shows through
            cam.nearClipPlane = 0.04f; cam.farClipPlane = 30f; cam.allowHDR = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; data.renderShadows = false;
            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("HeadPosition", binding: "<XRHMD>/centerEyePosition"));
            tpd.rotationInput = new InputActionProperty(new InputAction("HeadRotation", binding: "<XRHMD>/centerEyeRotation"));
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            camGo.AddComponent<AudioListener>();                                                        // the user's ears: the partner's voice is spatial
            var camManager = camGo.AddComponent<ARCameraManager>();                                                      // Meta OpenXR: enables the passthrough layer behind the app
            var raycasts = originGo.AddComponent<ARRaycastManager>();                                   // rays against the real room (depth); QuestApp switches it on once spatial data is allowed
            raycasts.enabled = false;
            origin.Camera = cam; origin.CameraFloorOffsetObject = offset; origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            new GameObject("AR Session").AddComponent<ARSession>();

            var core = (GameObject)PrefabUtility.InstantiatePrefab(mrCore, scene);
            var app = core.GetComponent<MrApp>();

            var so = new SerializedObject(quest);
            so.FindProperty("_cam").objectReferenceValue = cam;
            so.FindProperty("_origin").objectReferenceValue = originGo.transform;
            so.FindProperty("_app").objectReferenceValue = app;
            so.FindProperty("_raycasts").objectReferenceValue = raycasts;
            so.FindProperty("_cameraManager").objectReferenceValue = camManager;
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var go in scene.GetRootGameObjects()) LPSceneBuilder.Validate(go, "QuestMR.unity", problems);
            EditorSceneManager.SaveScene(scene, LPSceneBuilder.QuestScene);
            return true;
        }
    }
}
