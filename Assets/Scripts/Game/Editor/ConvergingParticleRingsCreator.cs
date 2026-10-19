using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VestibularGame.Game.Editor
{
    /// <summary>
    /// Creates an endless "rings of particles closing in to one point" effect from a plain ParticleSystem,
    /// so it can be tweaked in the Inspector afterwards.
    /// </summary>
    public static class ConvergingParticleRingsCreator
    {
        const float Radius = 3f;
        const float RingTravelTime = 2.5f;
        const int RingsOnScreen = 4;
        const int ParticlesPerRing = 48;
        const float ParticleSize = 0.12f;

        const string UrpParticleMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";
        const string EffectsFolder = "Assets/Art/Effects";
        const string FallbackMaterialPath = EffectsFolder + "/ConvergingParticle.mat";

        [MenuItem("Tools/Effects/Create Converging Particle Rings")]
        public static void Create()
        {
            var go = new GameObject("ConvergingParticleRings");
            if (Selection.activeTransform != null) go.transform.SetParent(Selection.activeTransform, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            Configure(ps, go.GetComponent<ParticleSystemRenderer>());

            Undo.RegisterCreatedObjectUndo(go, "Create Converging Particle Rings");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);

            ps.Play();
        }

        static void Configure(ParticleSystem ps, ParticleSystemRenderer renderer)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float interval = RingTravelTime / RingsOnScreen;

            var main = ps.main;
            main.duration = interval;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = RingTravelTime;
            main.startSpeed = -(Radius / RingTravelTime);
            main.startSize = ParticleSize;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.useUnscaledTime = true;
            main.maxParticles = ParticlesPerRing * (RingsOnScreen + 2);
            main.playOnAwake = true;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)ParticlesPerRing) });

            // Circle shape lies in the local XY plane when rotation is zero, which faces a 2D camera.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Radius;
            shape.radiusThickness = 0f;
            shape.arc = 360f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            shape.arcSpread = 0f;
            shape.rotation = Vector3.zero;

            var colorModule = ps.colorOverLifetime;
            colorModule.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                    new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f)
                });
            colorModule.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeModule = ps.sizeOverLifetime;
            sizeModule.enabled = true;
            sizeModule.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 5;
            renderer.sharedMaterial = GetParticleMaterial();
        }

        static Material GetParticleMaterial()
        {
            var urp = AssetDatabase.LoadAssetAtPath<Material>(UrpParticleMaterialPath);
            if (urp != null) return urp;

            var existing = AssetDatabase.LoadAssetAtPath<Material>(FallbackMaterialPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(EffectsFolder)) AssetDatabase.CreateFolder("Assets/Art", "Effects");

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var material = new Material(shader) { name = "ConvergingParticle" };
            var texture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            else material.mainTexture = texture;

            // Transparent surface so the alpha fade works with the URP particle shader.
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            AssetDatabase.CreateAsset(material, FallbackMaterialPath);
            return material;
        }
    }
}
