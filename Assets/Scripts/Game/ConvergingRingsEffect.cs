using System.Collections.Generic;
using UnityEngine;

namespace VestibularGame.Game
{
    /// <summary>
    /// Endless "rings closing in to one point" effect. Evenly staggered circle outlines shrink from
    /// <see cref="startRadius"/> to <see cref="endRadius"/> around this object, then reappear at the outer radius.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ConvergingRingsEffect : MonoBehaviour
    {
        [Range(1, 16)] [SerializeField] int ringCount = 4;
        [Tooltip("Seconds for one ring to travel from the outer radius to the centre.")]
        [SerializeField] float period = 2.5f;
        [Tooltip("World units.")]
        [SerializeField] float startRadius = 3f;
        [SerializeField] float endRadius = 0f;
        [SerializeField] float lineWidth = 0.06f;
        [Tooltip("Points per circle.")]
        [Range(16, 256)] [SerializeField] int segments = 64;
        [Tooltip("Maps progress 0..1 to the blend from start to end radius.")]
        [SerializeField] AnimationCurve radiusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Ring colour over its life: fades in at the edge, fades out as it vanishes into the point.")]
        [SerializeField] Gradient colorOverLife = DefaultGradient();
        [Tooltip("Rings get thinner as they close in.")]
        [SerializeField] bool shrinkWidth = true;
        [SerializeField] bool useUnscaledTime = true;
        [Tooltip("Optional. When empty, a Sprites/Default material is created.")]
        [SerializeField] Material material;
        [SerializeField] string sortingLayerName = "Default";
        [SerializeField] int sortingOrder = 0;

        public bool Playing { get; set; } = true;

        readonly List<LineRenderer> rings = new List<LineRenderer>();
        Vector3[] unitPoints;
        Vector3[] scratch;
        int builtSegments;
        Material createdMaterial;
        float phase;

        static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f)
                });
            return g;
        }

        public void Restart() => phase = 0f;

        void OnEnable() => Rebuild();

        void OnDisable() => DestroyRings();

        void OnDestroy()
        {
            if (createdMaterial == null) return;
            if (Application.isPlaying) Destroy(createdMaterial);
            else DestroyImmediate(createdMaterial);
            createdMaterial = null;
        }

        void Update()
        {
            if (rings.Count != ringCount || builtSegments != segments) Rebuild();

            if (!Playing)
            {
                for (int i = 0; i < rings.Count; i++) rings[i].enabled = false;
                return;
            }

            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            phase = Mathf.Repeat(phase + dt / Mathf.Max(period, 0.01f), 1f);

            int count = rings.Count;
            for (int i = 0; i < count; i++)
            {
                LineRenderer lr = rings[i];
                float p = Mathf.Repeat(phase + i / (float)count, 1f);
                float radius = Mathf.LerpUnclamped(startRadius, endRadius, radiusCurve.Evaluate(p));

                for (int s = 0; s < builtSegments; s++) scratch[s] = unitPoints[s] * radius;
                lr.SetPositions(scratch);

                float width = lineWidth;
                if (shrinkWidth)
                {
                    float k = Mathf.Abs(startRadius) > 0.0001f ? Mathf.Abs(radius / startRadius) : 1f;
                    width *= Mathf.Clamp(k, 0.25f, 1f);
                }
                lr.widthMultiplier = width;

                Color c = colorOverLife.Evaluate(p);
                lr.startColor = c;
                lr.endColor = c;
                lr.enabled = true;
            }
        }

        void Rebuild()
        {
            DestroyRings();

            builtSegments = Mathf.Max(segments, 3);
            unitPoints = new Vector3[builtSegments];
            scratch = new Vector3[builtSegments];
            for (int s = 0; s < builtSegments; s++)
            {
                float a = s / (float)builtSegments * Mathf.PI * 2f;
                unitPoints[s] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            }

            Material mat = material;
            if (mat == null)
            {
                if (createdMaterial == null) createdMaterial = new Material(Shader.Find("Sprites/Default"));
                mat = createdMaterial;
            }

            for (int i = 0; i < ringCount; i++)
            {
                var go = new GameObject("Ring " + i) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = builtSegments;
                lr.alignment = LineAlignment.View;
                lr.numCornerVertices = 0;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = mat;
                lr.sortingLayerName = sortingLayerName;
                lr.sortingOrder = sortingOrder;
                lr.enabled = false;
                rings.Add(lr);
            }
        }

        void DestroyRings()
        {
            for (int i = 0; i < rings.Count; i++)
            {
                if (rings[i] == null) continue;
                GameObject go = rings[i].gameObject;
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
            rings.Clear();
        }
    }
}
