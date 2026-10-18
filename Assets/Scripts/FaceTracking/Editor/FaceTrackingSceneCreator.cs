using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VestibularGame.FaceTracking.DebugUI;

namespace VestibularGame.FaceTracking.Editor
{
    /// <summary>Creates the face-tracking debug scene and registers it as the first build scene.</summary>
    public static class FaceTrackingSceneCreator
    {
        const string SceneFolder = "Assets/Scenes";
        const string ScenePath = SceneFolder + "/FaceTrackingDebug.unity";

        [MenuItem("Tools/Face Tracking/Create Debug Scene")]
        public static void CreateDebugScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
            cameraGo.AddComponent<AudioListener>();

            var go = new GameObject("FaceTracking");
            go.AddComponent<FaceTrackingController>();
            go.AddComponent<FaceTrackingDebugView>();

            if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddAsFirstBuildScene(ScenePath);
            Debug.Log("Face tracking debug scene created at " + ScenePath);
        }

        static void AddAsFirstBuildScene(string path)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(path, true) };
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path != path) scenes.Add(s);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
