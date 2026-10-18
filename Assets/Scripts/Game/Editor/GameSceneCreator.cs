using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VestibularGame.FaceTracking;

namespace VestibularGame.Game.Editor
{
    /// <summary>
    /// Builds the game scene out of placeholder GameObjects (sprites, UI) that are meant to be swapped for real art,
    /// and registers it as the first build scene.
    /// </summary>
    public static class GameSceneCreator
    {
        const string SceneFolder = "Assets/Scenes";
        const string ScenePath = SceneFolder + "/Game.unity";
        const string PlaceholderFolder = "Assets/Art/Placeholders";
        const string CirclePath = PlaceholderFolder + "/PlaceholderCircle.png";
        const string SquarePath = PlaceholderFolder + "/PlaceholderSquare.png";

        const int CircleSize = 256;
        const int SquareSize = 8;

        [MenuItem("Tools/Game/Create Game Scene")]
        public static void CreateGameScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var circle = EnsurePlaceholderSprite(CirclePath, CircleSize, true);
            var square = EnsurePlaceholderSprite(SquarePath, SquareSize, false);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildCamera();

            // Face tracking + detector.
            var trackingGo = new GameObject("FaceTracking");
            var controller = trackingGo.AddComponent<FaceTrackingController>();
            var detector = trackingGo.AddComponent<CenterGazeDetector>();
            Assign(detector, "controller", controller);

            // Steps (all at the world origin = screen centre).
            var stepsRoot = new GameObject("Steps");
            var centreStep = BuildStep(stepsRoot.transform, "Step_CentreCalibration");
            var headStep = BuildStep(stepsRoot.transform, "Step_HeadCalibration");
            var playStep = BuildStep(stepsRoot.transform, "Step_Play");

            var calibrationTarget = BuildSpriteChild(centreStep.transform, "CalibrationTarget", circle, 0.5f,
                new Color(1f, 0.8f, 0.2f, 1f));
            calibrationTarget.AddComponent<PlaceholderPulse>();

            var headTarget = BuildSpriteChild(headStep.transform, "HeadCalibrationTarget", circle, 0.5f,
                new Color(0.2f, 0.9f, 1f, 1f));
            headTarget.AddComponent<PlaceholderPulse>();

            var lookTarget = BuildSpriteChild(playStep.transform, "LookTarget", circle, 1f, Color.white);
            var feedback = lookTarget.AddComponent<GazeTargetFeedback>();
            Assign(feedback, "detector", detector);

            headStep.SetActive(false);
            playStep.SetActive(false);

            // Game flow.
            var flowGo = new GameObject("GameFlow");
            var flow = flowGo.AddComponent<GameFlowController>();
            Assign(flow, "controller", controller);
            Assign(flow, "detector", detector);
            Assign(flow, "centreCalibrationStep", centreStep);
            Assign(flow, "headCalibrationStep", headStep);
            Assign(flow, "playStep", playStep);

            BuildCalibrationUi(flow, square);
            BuildEventSystem();

            if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddAsFirstBuildScene(ScenePath);
            Debug.Log("Game scene created at " + ScenePath);
        }

        // ---------------------------------------------------------------- scene objects

        static void BuildCamera()
        {
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
            cameraGo.AddComponent<AudioListener>();
        }

        static GameObject BuildStep(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            return go;
        }

        static GameObject BuildSpriteChild(Transform parent, string name, Sprite sprite, float scale, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            return go;
        }

        static void BuildCalibrationUi(GameFlowController flow, Sprite square)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("CalibrationUI", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var canvasRt = (RectTransform)canvasGo.transform;

            // Instruction text: top-centre, 120 px from the top.
            var instruction = NewText("InstructionText", canvasRt, font, 40, FontStyle.Bold, Color.white,
                TextAnchor.MiddleCenter);
            SetRect(instruction.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -120f), new Vector2(1400f, 160f));
            instruction.text = "Look at the dot and hold still";

            // Progress bar: below centre.
            var progressRt = NewRect("Progress", canvasRt);
            SetRect(progressRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -170f), new Vector2(520f, 18f));
            var progressBg = progressRt.gameObject.AddComponent<Image>();
            progressBg.color = new Color(0.12f, 0.13f, 0.17f, 0.9f);
            progressBg.raycastTarget = false;

            var fillRt = NewRect("Fill", progressRt);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = square;
            fill.color = new Color(0.85f, 0.92f, 1f, 1f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;

            // Result text: same spot as the progress bar, starts inactive.
            var result = NewText("ResultText", canvasRt, font, 36, FontStyle.Bold, Color.white,
                TextAnchor.MiddleCenter);
            SetRect(result.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -170f), new Vector2(1200f, 80f));
            result.gameObject.SetActive(false);

            // Recalibrate button: bottom-right corner with 40 px margins.
            var buttonRt = NewRect("RecalibrateButton", canvasRt);
            SetRect(buttonRt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-40f, 40f), new Vector2(280f, 76f));
            var buttonImage = buttonRt.gameObject.AddComponent<Image>();
            buttonImage.sprite = square;
            buttonImage.color = new Color(0.22f, 0.24f, 0.3f, 1f);
            var button = buttonRt.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            var label = NewText("Text", buttonRt, font, 30, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.text = "Recalibrate";

            var hud = canvasGo.AddComponent<CalibrationHud>();
            Assign(hud, "flow", flow);
            Assign(hud, "instructionText", instruction);
            Assign(hud, "progressRoot", progressRt.gameObject);
            Assign(hud, "progressFill", fill);
            Assign(hud, "resultText", result);
            Assign(hud, "recalibrateButton", button);
        }

        static void BuildEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        // ---------------------------------------------------------------- UI helpers

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

        // ---------------------------------------------------------------- serialization

        static void Assign(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Debug.LogError("GameSceneCreator: property '" + property + "' not found on " + target.GetType().Name + ".");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- placeholder sprites

        static Sprite EnsurePlaceholderSprite(string assetPath, int size, bool disc)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(PlaceholderFolder)) AssetDatabase.CreateFolder("Assets/Art", "Placeholders");

            if (!File.Exists(assetPath))
            {
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                float centre = (size - 1) * 0.5f;
                float radius = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float a = 1f;
                        if (disc)
                        {
                            float dx = x - centre, dy = y - centre;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy);
                            a = Mathf.Clamp01(radius - dist);
                        }
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(assetPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = size;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = disc ? FilterMode.Bilinear : FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
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
