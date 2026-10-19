using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace VestibularGame.Game.Editor
{
    /// <summary>
    /// Adds the gaze progress ring around the star in the open Game scene.
    /// Idempotent and non-destructive: existing objects are never deleted or moved.
    /// </summary>
    public static class GazeProgressRingSetup
    {
        [MenuItem("Tools/Game/Add Gaze Progress Ring")]
        public static void AddRing()
        {
            var flow = Object.FindAnyObjectByType<GameFlowController>();

            Transform playStep = null;
            if (flow != null)
            {
                var flowSo = new SerializedObject(flow);
                var playProp = flowSo.FindProperty("playStep");
                if (playProp != null && playProp.objectReferenceValue is GameObject playGo) playStep = playGo.transform;
            }

            Transform star = null;
            if (playStep != null)
            {
                foreach (var t in playStep.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Star") { star = t; break; }
                }
            }
            if (star == null) star = Selection.activeTransform;
            if (star == null)
            {
                EditorUtility.DisplayDialog("Add Gaze Progress Ring",
                    "Could not find the star. Select it in the Hierarchy (or name it \"Star\" under the play step) and try again.",
                    "OK");
                return;
            }
            if (playStep == null) playStep = star.parent != null ? star.parent : star;

            var log = new List<string>();
            var session = Object.FindAnyObjectByType<GameSession>();

            if (Object.FindAnyObjectByType<GazeProgressRing>() == null)
            {
                var go = new GameObject("GazeProgressRing");
                go.transform.SetParent(playStep, false);
                Undo.RegisterCreatedObjectUndo(go, "Add GazeProgressRing");
                var ring = go.AddComponent<GazeProgressRing>();
                Assign(ring, "session", session);
                Assign(ring, "target", star);

                float radius = 1.2f;
                var renderers = star.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    float extent = Mathf.Max(b.extents.x, b.extents.y);
                    if (extent > 0.01f) radius = extent * 1.1f;
                }
                var so = new SerializedObject(ring);
                var radiusProp = so.FindProperty("radius");
                if (radiusProp != null)
                {
                    radiusProp.floatValue = radius;
                    so.ApplyModifiedProperties();
                }
                log.Add("created GazeProgressRing under " + playStep.name + " (target " + star.name + ", radius " +
                        radius.ToString("0.00") + ")");
            }

            var rewards = Object.FindAnyObjectByType<StreakRewards>();
            if (rewards != null)
            {
                var so = new SerializedObject(rewards);
                var grow = so.FindProperty("growTarget");
                if (grow != null && grow.objectReferenceValue == null)
                {
                    Undo.RecordObject(rewards, "Assign growTarget");
                    grow.objectReferenceValue = star;
                    so.ApplyModifiedProperties();
                    log.Add("assigned StreakRewards.growTarget = " + star.name);
                }
            }

            var hud = Object.FindAnyObjectByType<SessionHud>();
            if (hud != null)
            {
                var so = new SerializedObject(hud);
                var stepFill = so.FindProperty("stepFill");
                if (stepFill != null && stepFill.objectReferenceValue is Image img)
                {
                    Transform bar = img.transform.parent;
                    if (bar != null && bar.gameObject.activeSelf)
                    {
                        Undo.RecordObject(bar.gameObject, "Hide StepBar");
                        bar.gameObject.SetActive(false);
                    }
                    Undo.RecordObject(hud, "Clear stepFill");
                    stepFill.objectReferenceValue = null;
                    so.ApplyModifiedProperties();
                    log.Add("deactivated old step bar '" + (bar != null ? bar.name : "?") + "' and cleared SessionHud.stepFill");
                }
            }

            EditorSceneManager.MarkSceneDirty(star.gameObject.scene);
            Debug.Log("Add Gaze Progress Ring: " +
                      (log.Count == 0 ? "already set up; nothing changed." : string.Join("; ", log) + "."));
        }

        static void Assign(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Debug.LogError("GazeProgressRingSetup: property '" + property + "' not found on " + target.GetType().Name + ".");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
