using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VestibularGame.Game
{
    /// <summary>
    /// Drives the already-built session UI (timer, level, step progress bar and end panel) from
    /// <see cref="GameSession"/> state. It never creates UI; every element is a serialized reference.
    /// Strings are only rebuilt when their content changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionHud : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The session this HUD mirrors. Found automatically when left empty.")]
        [SerializeField] GameSession session;
        [Tooltip("Active only while the session is running.")]
        [SerializeField] GameObject hudRoot;
        [Tooltip("Remaining time as m:ss.")]
        [SerializeField] Text timerText;
        [Tooltip("Current streak level, e.g. \"★ 3\".")]
        [SerializeField] Text levelText;
        [Tooltip("Filled image (horizontal) whose fillAmount follows the progress toward the next level.")]
        [SerializeField] Image stepFill;

        [Header("End panel")]
        [Tooltip("Shown when the session ends, hidden when it starts.")]
        [SerializeField] GameObject endPanel;
        [SerializeField] Text endTitleText;
        [SerializeField] Text endStatsText;
        [Tooltip("Restarts the session without recalibrating.")]
        [SerializeField] Button playAgainButton;
        [Tooltip("Title shown on the end panel.")]
        [SerializeField] string endTitle = "Great job!";

        readonly StringBuilder sb = new StringBuilder(64);
        int shownSecond = -1;
        int shownLevel = -1;
        bool subscribed;

        void Awake()
        {
            if (session == null) session = FindAnyObjectByType<GameSession>();
        }

        void OnEnable()
        {
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (session == null) return;

            session.SessionStarted += OnSessionStarted;
            session.SessionEnded += OnSessionEnded;
            subscribed = true;
            if (playAgainButton != null) playAgainButton.onClick.AddListener(OnPlayAgainClicked);

            if (endPanel != null && !session.IsFinished) SetActive(endPanel, false);
            Refresh(true);
        }

        void OnDisable()
        {
            if (subscribed && session != null)
            {
                session.SessionStarted -= OnSessionStarted;
                session.SessionEnded -= OnSessionEnded;
            }
            subscribed = false;
            if (playAgainButton != null) playAgainButton.onClick.RemoveListener(OnPlayAgainClicked);
        }

        void Update()
        {
            if (session == null) return;
            Refresh(false);
        }

        void OnDestroy()
        {
            // Nothing else to release; subscriptions are removed in OnDisable.
        }

        void OnSessionStarted()
        {
            if (endPanel != null) SetActive(endPanel, false);
            Refresh(true);
        }

        void OnSessionEnded(SessionStats stats)
        {
            if (hudRoot != null) SetActive(hudRoot, false);
            if (endTitleText != null) endTitleText.text = endTitle;
            if (endStatsText != null)
            {
                sb.Clear();
                sb.Append("Best: ★ ").Append(stats.BestLevel.ToString(CultureInfo.InvariantCulture))
                    .Append("\nLooked: ").Append(Mathf.RoundToInt(stats.LookingSeconds).ToString(CultureInfo.InvariantCulture))
                    .Append(" s");
                endStatsText.text = sb.ToString();
            }
            if (endPanel != null) SetActive(endPanel, true);
        }

        void OnPlayAgainClicked()
        {
            if (session != null) session.Restart();
        }

        void Refresh(bool force)
        {
            if (hudRoot != null) SetActive(hudRoot, session.IsRunning);
            if (!session.IsRunning && !force) return;

            if (stepFill != null) stepFill.fillAmount = Mathf.Clamp01(session.StepProgress);

            if (timerText != null)
            {
                int seconds = Mathf.CeilToInt(session.RemainingSeconds);
                if (force || seconds != shownSecond)
                {
                    shownSecond = seconds;
                    sb.Clear();
                    sb.Append(seconds / 60).Append(':');
                    int s = seconds % 60;
                    if (s < 10) sb.Append('0');
                    sb.Append(s);
                    timerText.text = sb.ToString();
                }
            }

            if (levelText != null)
            {
                int level = session.Level;
                if (force || level != shownLevel)
                {
                    shownLevel = level;
                    sb.Clear();
                    sb.Append("★ ").Append(level);
                    levelText.text = sb.ToString();
                }
            }
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) go.SetActive(active);
        }
    }
}
