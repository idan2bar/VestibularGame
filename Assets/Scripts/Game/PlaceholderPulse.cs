using UnityEngine;

namespace VestibularGame.Game
{
    /// <summary>Placeholder animation: gently pulses localScale (unscaled time) around the scale it had on enable.</summary>
    public sealed class PlaceholderPulse : MonoBehaviour
    {
        [SerializeField] float amplitude = 0.12f;
        [SerializeField] float frequency = 1.2f;

        Vector3 baseScale;

        void OnEnable() => baseScale = transform.localScale;

        void OnDisable() => transform.localScale = baseScale;

        void Update()
        {
            float s = 1f + amplitude * Mathf.Sin(Time.unscaledTime * frequency * 2f * Mathf.PI);
            transform.localScale = baseScale * s;
        }
    }
}
