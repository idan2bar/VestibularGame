using UnityEngine;

namespace VestibularGame.Game
{
    /// <summary>
    /// Hollow circle around the target (the star) that fills clockwise from 12 o'clock while the child keeps
    /// looking, following <see cref="GameSession.StepProgress"/>. It empties when the child looks away.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GazeProgressRing : MonoBehaviour
    {
        [Tooltip("Session to read progress from. Found in the scene if left empty.")]
        [SerializeField] GameSession session;
        [Tooltip("The star. The ring follows its position every frame.")]
        [SerializeField] Transform target;
        [Tooltip("World units at the target's starting scale.")]
        [SerializeField] float radius = 1.2f;
        [Tooltip("Scale the radius with the target so the ring stays around a growing star.")]
        [SerializeField] bool scaleWithTarget = true;
        [SerializeField] float lineWidth = 0.08f;
        [Tooltip("Points per full circle.")]
        [Range(16, 256)] [SerializeField] int segments = 96;
        [SerializeField] Color fillColor = new Color(1f, 0.85f, 0.2f, 1f);
        [Tooltip("Faint full background circle.")]
        [SerializeField] Color trackColor = new Color(1f, 1f, 1f, 0.2f);
        [Tooltip("Exponential smoothing speed of the shown fill toward the real progress.")]
        [SerializeField] float fillSmoothing = 12f;
        [SerializeField] bool hideWhenNotRunning = true;
        [Tooltip("Optional. When empty, a Sprites/Default material is created.")]
        [SerializeField] Material material;
        [SerializeField] string sortingLayerName = "Default";
        [SerializeField] int sortingOrder = 10;

        LineRenderer track;
        LineRenderer fill;
        Vector3[] scratch;
        float[] cos;
        float[] sin;
        int builtSegments;
        Material createdMaterial;
        float shownFill;
        float baseLossyScale = 1f;

        void OnEnable()
        {
            if (session == null) session = FindAnyObjectByType<GameSession>();
            CaptureBaseScale();
            shownFill = 0f;
            Rebuild();
        }

        void OnDisable() => DestroyRenderers();

        void OnDestroy()
        {
            if (createdMaterial == null) return;
            if (Application.isPlaying) Destroy(createdMaterial);
            else DestroyImmediate(createdMaterial);
            createdMaterial = null;
        }

        void CaptureBaseScale()
        {
            float s = target != null ? target.lossyScale.x : 1f;
            baseLossyScale = Mathf.Abs(s) > 0.0001f ? s : 1f;
        }

        void LateUpdate()
        {
            if (builtSegments != Mathf.Max(segments, 8) || track == null || fill == null) Rebuild();

            bool show = target != null && (!hideWhenNotRunning || (session != null && session.IsRunning));
            if (!show)
            {
                track.enabled = false;
                fill.enabled = false;
                shownFill = 0f;
                return;
            }

            float goal = session != null ? Mathf.Clamp01(session.StepProgress) : 0f;
            if (goal < shownFill - 0.5f) shownFill = goal;
            else shownFill += (goal - shownFill) * (1f - Mathf.Exp(-fillSmoothing * Time.unscaledDeltaTime));
            shownFill = Mathf.Clamp01(shownFill);

            Vector3 center = target.position;
            float r = radius;
            if (scaleWithTarget) r *= target.lossyScale.x / baseLossyScale;

            track.enabled = true;
            track.widthMultiplier = lineWidth;
            track.startColor = trackColor;
            track.endColor = trackColor;
            track.positionCount = builtSegments;
            for (int i = 0; i < builtSegments; i++)
                scratch[i] = new Vector3(center.x + cos[i] * r, center.y + sin[i] * r, center.z);
            track.SetPositions(scratch);

            if (shownFill < 0.001f)
            {
                fill.enabled = false;
                return;
            }

            int count = Mathf.Max(2, Mathf.RoundToInt(builtSegments * shownFill) + 1);
            fill.enabled = true;
            fill.widthMultiplier = lineWidth;
            fill.startColor = fillColor;
            fill.endColor = fillColor;
            fill.positionCount = count;
            for (int i = 0; i < count; i++)
                scratch[i] = new Vector3(center.x + cos[i] * r, center.y + sin[i] * r, center.z);
            fill.SetPositions(scratch);
        }

        void Rebuild()
        {
            DestroyRenderers();

            builtSegments = Mathf.Max(segments, 8);
            // Index k is the angle 90 degrees minus k/segments of a turn: clockwise from 12 o'clock.
            cos = new float[builtSegments + 1];
            sin = new float[builtSegments + 1];
            scratch = new Vector3[builtSegments + 1];
            for (int k = 0; k <= builtSegments; k++)
            {
                float a = Mathf.PI * 0.5f - k / (float)builtSegments * Mathf.PI * 2f;
                cos[k] = Mathf.Cos(a);
                sin[k] = Mathf.Sin(a);
            }

            Material mat = material;
            if (mat == null)
            {
                if (createdMaterial == null) createdMaterial = new Material(Shader.Find("Sprites/Default"));
                mat = createdMaterial;
            }

            track = CreateRenderer("Track", mat, true);
            fill = CreateRenderer("Fill", mat, false);
            // Fill draws above the track.
            fill.sortingOrder = sortingOrder + 1;
        }

        LineRenderer CreateRenderer(string childName, Material mat, bool loop)
        {
            var go = new GameObject(childName) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = loop ? builtSegments : 2;
            lr.alignment = LineAlignment.View;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = mat;
            lr.sortingLayerName = sortingLayerName;
            lr.sortingOrder = sortingOrder;
            lr.enabled = false;
            return lr;
        }

        void DestroyRenderers()
        {
            DestroyChild(ref track);
            DestroyChild(ref fill);
        }

        static void DestroyChild(ref LineRenderer lr)
        {
            if (lr != null)
            {
                GameObject go = lr.gameObject;
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
            lr = null;
        }
    }
}
