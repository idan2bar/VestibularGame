using UnityEngine;

namespace VestibularGame.Game
{
    /// <summary>
    /// Example consumer of <see cref="CenterGazeDetector"/> for the centre object in the play step.
    /// PLACEHOLDER: replace it with an <see cref="Animator"/> that has the two triggers (default "LookStart" and
    /// "LookStop"). When an Animator is assigned, only the triggers are fired; otherwise a scale/colour lerp is used.
    /// </summary>
    public sealed class GazeTargetFeedback : MonoBehaviour
    {
        [SerializeField] CenterGazeDetector detector;
        [Tooltip("Optional. When assigned, the look events fire its triggers and the placeholder reaction is skipped.")]
        [SerializeField] Animator animator;
        [SerializeField] string lookStartTrigger = "LookStart";
        [SerializeField] string lookStopTrigger = "LookStop";

        [Header("Placeholder reaction (no Animator)")]
        [SerializeField] float lookingScale = 1.25f;
        [SerializeField] Color lookingColor = new Color(0.45f, 1f, 0.55f, 1f);
        [SerializeField] Color idleColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        [SerializeField] float speed = 8f;

        SpriteRenderer sprite;
        Vector3 baseScale;
        float blend;

        void OnEnable()
        {
            if (detector == null) detector = FindAnyObjectByType<CenterGazeDetector>();
            if (sprite == null) sprite = GetComponent<SpriteRenderer>();
            baseScale = transform.localScale;

            if (detector == null) return;
            detector.LookStarted += OnLookStarted;
            detector.LookStopped += OnLookStopped;

            blend = detector.IsLooking ? 1f : 0f;
            if (animator != null) { if (detector.IsLooking) OnLookStarted(); }
            else Apply();
        }

        void OnDisable()
        {
            if (detector != null)
            {
                detector.LookStarted -= OnLookStarted;
                detector.LookStopped -= OnLookStopped;
            }
            if (animator == null)
            {
                transform.localScale = baseScale;
                if (sprite != null) sprite.color = idleColor;
            }
        }

        void OnLookStarted()
        {
            if (animator == null) return;
            animator.ResetTrigger(lookStopTrigger);
            animator.SetTrigger(lookStartTrigger);
        }

        void OnLookStopped()
        {
            if (animator == null) return;
            animator.ResetTrigger(lookStartTrigger);
            animator.SetTrigger(lookStopTrigger);
        }

        void Update()
        {
            if (animator != null || detector == null) return;
            float target = detector.IsLooking ? 1f : 0f;
            blend = Mathf.Lerp(blend, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            Apply();
        }

        void Apply()
        {
            transform.localScale = baseScale * Mathf.Lerp(1f, lookingScale, blend);
            if (sprite != null) sprite.color = Color.Lerp(idleColor, lookingColor, blend);
        }
    }
}
