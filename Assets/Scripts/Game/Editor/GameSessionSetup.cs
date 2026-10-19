using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VestibularGame.Game.Editor
{
    /// <summary>
    /// Adds the timed session (GameSession, reactions, rewards and the session HUD) to the open Game scene.
    /// Idempotent and non-destructive: existing objects are never deleted or moved.
    /// </summary>
    public static class GameSessionSetup
    {
        const string SquarePath = "Assets/Art/Placeholders/PlaceholderSquare.png";

        [MenuItem("Tools/Game/Add Session To Scene")]
        public static void AddSessionToScene()
        {
            var flow = Object.FindAnyObjectByType<GameFlowController>();
            var detector = Object.FindAnyObjectByType<CenterGazeDetector>();
            if (flow == null || detector == null)
            {
                EditorUtility.DisplayDialog("Add Session To Scene",
                    "The open scene needs a GameFlowController and a CenterGazeDetector. Open Game.unity first.", "OK");
                return;
            }

            var added = new List<string>();

            // GameSession on the flow's GameObject.
            var session = Object.FindAnyObjectByType<GameSession>();
            if (session == null)
            {
                session = Undo.AddComponent<GameSession>(flow.gameObject);
                Assign(session, "flow", flow);
                Assign(session, "detector", detector);
                added.Add("GameSession (on " + flow.gameObject.name + ")");
            }

            // Reactions and rewards under the play step.
            Transform playStep = null;
            var flowSo = new SerializedObject(flow);
            var playProp = flowSo.FindProperty("playStep");
            if (playProp != null && playProp.objectReferenceValue is GameObject playGo) playStep = playGo.transform;
            if (playStep == null) playStep = flow.transform;

            if (Object.FindAnyObjectByType<CharacterReactions>() == null)
            {
                var reactions = NewChild("CharacterReactions", playStep).AddComponent<CharacterReactions>();
                Undo.RegisterCreatedObjectUndo(reactions.gameObject, "Add CharacterReactions");
                Assign(reactions, "session", session);
                added.Add("CharacterReactions");
            }

            if (Object.FindAnyObjectByType<StreakRewards>() == null)
            {
                var rewards = NewChild("StreakRewards", playStep).AddComponent<StreakRewards>();
                Undo.RegisterCreatedObjectUndo(rewards.gameObject, "Add StreakRewards");
                Assign(rewards, "session", session);
                added.Add("StreakRewards");
            }

            if (Object.FindAnyObjectByType<SessionHud>() == null)
            {
                BuildSessionUi(session);
                added.Add("SessionUI canvas with SessionHud");
            }

            if (!Object.FindAnyObjectByType<EventSystem>())
            {
                var go = new GameObject("EventSystem");
                go.AddComponent<EventSystem>();
                var module = go.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
                Undo.RegisterCreatedObjectUndo(go, "Add EventSystem");
                added.Add("EventSystem");
            }

            EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);

            string summary = added.Count == 0
                ? "Game session already set up; nothing added."
                : "Added: " + string.Join(", ", added) + ".";
            Debug.Log("Add Session To Scene: " + summary +
                      "\nStill to assign manually: CharacterReactions animators, StreakRewards growTarget, burstOnLevel particles and musicLayers.");
        }

        static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        // ---------------------------------------------------------------- UI

        static void BuildSessionUi(GameSession session)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var square = AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);

            var canvasGo = new GameObject("SessionUI", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasGo, "Add SessionUI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var canvasRt = (RectTransform)canvasGo.transform;

            // In-game HUD (active while running).
            var hudRt = NewRect("HudRoot", canvasRt);
            hudRt.anchorMin = Vector2.zero;
            hudRt.anchorMax = Vector2.one;
            hudRt.offsetMin = hudRt.offsetMax = Vector2.zero;

            var timer = NewText("TimerText", hudRt, font, 56, FontStyle.Bold, Color.white, TextAnchor.UpperRight);
            SetRect(timer.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-40f, -30f), new Vector2(300f, 90f));
            timer.text = "2:00";

            var level = NewText("LevelText", hudRt, font, 56, FontStyle.Bold, new Color(1f, 0.85f, 0.3f, 1f),
                TextAnchor.UpperLeft);
            SetRect(level.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(40f, -30f), new Vector2(300f, 90f));
            level.text = "★ 0";

            var barRt = NewRect("StepBar", hudRt);
            SetRect(barRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(720f, 14f));
            var barBg = barRt.gameObject.AddComponent<Image>();
            barBg.sprite = square;
            barBg.color = new Color(0.12f, 0.13f, 0.17f, 0.8f);
            barBg.raycastTarget = false;

            var fillRt = NewRect("Fill", barRt);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = square;
            fill.color = new Color(1f, 0.85f, 0.3f, 1f);
            // Filled mode needs a sprite; without one fall back to Simple (fillAmount then has no visual effect).
            fill.type = square != null ? Image.Type.Filled : Image.Type.Simple;
            if (square != null)
            {
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
            fill.fillAmount = 0f;
            fill.raycastTarget = false;

            // End panel (inactive).
            var panelRt = NewRect("EndPanel", canvasRt);
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
            var backdrop = panelRt.gameObject.AddComponent<Image>();
            backdrop.sprite = square;
            backdrop.color = new Color(0f, 0f, 0f, 0.6f);

            var boxRt = NewRect("Box", panelRt);
            SetRect(boxRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(800f, 560f));
            var boxImage = boxRt.gameObject.AddComponent<Image>();
            boxImage.sprite = square;
            boxImage.color = new Color(0.12f, 0.13f, 0.18f, 0.95f);
            boxImage.raycastTarget = false;

            var title = NewText("EndTitleText", boxRt, font, 72, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -50f), new Vector2(700f, 110f));
            title.text = "Great job!";

            var stats = NewText("EndStatsText", boxRt, font, 48, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
            SetRect(stats.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 10f), new Vector2(700f, 200f));
            stats.text = "Best: ★ 0\nLooked: 0 s";

            var buttonRt = NewRect("PlayAgainButton", boxRt);
            SetRect(buttonRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(360f, 90f));
            var buttonImage = buttonRt.gameObject.AddComponent<Image>();
            buttonImage.sprite = square;
            buttonImage.color = new Color(0.22f, 0.45f, 0.3f, 1f);
            var button = buttonRt.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            var label = NewText("Text", buttonRt, font, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.text = "Play again";

            panelRt.gameObject.SetActive(false);

            var hud = Undo.AddComponent<SessionHud>(canvasGo);
            Assign(hud, "session", session);
            Assign(hud, "hudRoot", hudRt.gameObject);
            Assign(hud, "timerText", timer);
            Assign(hud, "levelText", level);
            Assign(hud, "stepFill", fill);
            Assign(hud, "endPanel", panelRt.gameObject);
            Assign(hud, "endTitleText", title);
            Assign(hud, "endStatsText", stats);
            Assign(hud, "playAgainButton", button);
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Text NewText(string name, Transform parent, Font font, int size, FontStyle style, Color color,
            TextAnchor alignment)
        {
            var rt = NewRect(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos,
            Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Assign(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Debug.LogError("GameSessionSetup: property '" + property + "' not found on " + target.GetType().Name + ".");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
