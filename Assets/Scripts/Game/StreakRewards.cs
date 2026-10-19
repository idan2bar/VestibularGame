using UnityEngine;
using UnityEngine.Events;

namespace VestibularGame.Game
{
    /// <summary>
    /// Makes gaze streaks feel good: grows a target, bursts particles and layers in music as the level rises.
    /// Everything resets on look-away and fades out when the session ends.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreakRewards : MonoBehaviour
    {
        const float PunchDuration = 0.3f;
        const double ScheduleDelay = 0.1;

        [SerializeField, Tooltip("Session to listen to. Found automatically if left empty.")]
        GameSession session;

        [Header("Grow")]
        [SerializeField, Tooltip("Use a parent of the animated star. An Animator that animates scale on the same object would override this.")]
        Transform growTarget;
        [SerializeField, Tooltip("Extra scale (fraction of the base scale) gained per level.")]
        float growPerLevel = 0.15f;
        [SerializeField, Tooltip("Exponential smoothing speed toward the target scale (unscaled time).")]
        float growSpeed = 6f;
        [SerializeField, Tooltip("Extra scale added on each level up that decays over about 0.3 s.")]
        float levelUpPunch = 0.12f;

        [Header("Particles")]
        [SerializeField, Tooltip("Played on every level up.")]
        ParticleSystem[] burstOnLevel;
        [SerializeField, Tooltip("If above 0, level ups Emit this many particles instead of restarting the system.")]
        int burstCount;
        [SerializeField, Tooltip("Optional. Start at level 1 and stop emitting when the streak resets.")]
        ParticleSystem[] loopWhileStreaking;

        [Header("Music")]
        [SerializeField, Tooltip("Layer 0 is the base track. Layer i fades in when the level reaches i.")]
        AudioSource[] musicLayers;
        [SerializeField, Range(0f, 1f), Tooltip("Volume of the base layer and of layers that are faded in.")]
        float layerVolume = 1f;
        [SerializeField, Tooltip("Fade speed in volume per second.")]
        float fadeSpeed = 1.5f;

        [Header("One shots")]
        [SerializeField, Tooltip("Optional source for the level up and reset sounds.")]
        AudioSource sfx;
        [SerializeField] AudioClip levelUpClip;
        [SerializeField] AudioClip resetClip;

        [Header("Events")]
        public UnityEvent<int> onRewardLevel;
        public UnityEvent onRewardReset;

        Vector3 baseScale = Vector3.one;
        bool baseCaptured;
        int level;
        float growFactor;
        float punchTimer;
        bool musicActive;
        bool musicEnding;
        bool subscribed;

        void OnEnable()
        {
            if (growTarget != null)
            {
                baseScale = growTarget.localScale;
                baseCaptured = true;
            }
            level = 0;
            growFactor = 0f;
            punchTimer = 0f;

            if (session == null)
                session = FindAnyObjectByType<GameSession>();
            if (session == null)
                return;

            session.SessionStarted += OnSessionStarted;
            session.LevelReached += OnLevelReached;
            session.StreakReset += OnStreakReset;
            session.SessionEnded += OnSessionEnded;
            subscribed = true;

            if (session.IsRunning)
            {
                StartMusic();
                if (session.Level > 0)
                    level = session.Level;
            }
        }

        void OnDisable()
        {
            if (subscribed && session != null)
            {
                session.SessionStarted -= OnSessionStarted;
                session.LevelReached -= OnLevelReached;
                session.StreakReset -= OnStreakReset;
                session.SessionEnded -= OnSessionEnded;
            }
            subscribed = false;

            StopMusicNow();
            StopLoopParticles();
            if (baseCaptured && growTarget != null)
                growTarget.localScale = baseScale;
            baseCaptured = false;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateGrow(dt);
            UpdateMusic(dt);
        }

        void UpdateGrow(float dt)
        {
            if (growTarget == null || !baseCaptured)
                return;

            float goal = growPerLevel * level;
            growFactor += (goal - growFactor) * (1f - Mathf.Exp(-growSpeed * dt));

            float punch = 0f;
            if (punchTimer > 0f)
            {
                punchTimer = Mathf.Max(0f, punchTimer - dt);
                float k = punchTimer / PunchDuration;
                punch = levelUpPunch * k * k;
            }

            growTarget.localScale = baseScale * (1f + growFactor + punch);
        }

        void UpdateMusic(float dt)
        {
            if (!musicActive || musicLayers == null)
                return;

            float step = fadeSpeed * dt;
            bool anyAudible = false;
            for (int i = 0; i < musicLayers.Length; i++)
            {
                AudioSource src = musicLayers[i];
                if (!IsUsable(src))
                    continue;

                float target = 0f;
                if (!musicEnding && (i == 0 || level >= i))
                    target = layerVolume;

                src.volume = Mathf.MoveTowards(src.volume, target, step);
                if (src.volume > 0f)
                    anyAudible = true;
            }

            if (musicEnding && !anyAudible)
                StopMusicNow();
        }

        static bool IsUsable(AudioSource src) => src != null && src.clip != null;

        void StartMusic()
        {
            musicEnding = false;
            if (musicLayers == null)
                return;

            double startTime = AudioSettings.dspTime + ScheduleDelay;
            for (int i = 0; i < musicLayers.Length; i++)
            {
                AudioSource src = musicLayers[i];
                if (!IsUsable(src))
                    continue;

                src.loop = true;
                src.volume = i == 0 ? layerVolume : 0f;
                src.Stop();
                src.PlayScheduled(startTime);
                musicActive = true;
            }
        }

        void StopMusicNow()
        {
            if (musicLayers != null)
            {
                for (int i = 0; i < musicLayers.Length; i++)
                {
                    AudioSource src = musicLayers[i];
                    if (IsUsable(src))
                        src.Stop();
                }
            }
            musicActive = false;
            musicEnding = false;
        }

        void OnSessionStarted()
        {
            level = 0;
            growFactor = 0f;
            punchTimer = 0f;
            StopLoopParticles();
            StartMusic();
        }

        void OnLevelReached(int newLevel)
        {
            level = newLevel;
            punchTimer = levelUpPunch > 0f ? PunchDuration : 0f;

            if (burstOnLevel != null)
            {
                for (int i = 0; i < burstOnLevel.Length; i++)
                    Burst(burstOnLevel[i]);
            }

            if (loopWhileStreaking != null && newLevel >= 1)
            {
                for (int i = 0; i < loopWhileStreaking.Length; i++)
                {
                    ParticleSystem ps = loopWhileStreaking[i];
                    if (ps != null && !ps.isEmitting)
                        ps.Play(true);
                }
            }

            if (sfx != null && levelUpClip != null)
                sfx.PlayOneShot(levelUpClip);

            onRewardLevel?.Invoke(newLevel);
        }

        void OnStreakReset()
        {
            level = 0;
            punchTimer = 0f;
            StopLoopParticles();

            if (sfx != null && resetClip != null)
                sfx.PlayOneShot(resetClip);

            onRewardReset?.Invoke();
        }

        void OnSessionEnded(SessionStats stats)
        {
            level = 0;
            StopLoopParticles();
            if (musicActive)
                musicEnding = true;
        }

        void Burst(ParticleSystem ps)
        {
            if (ps == null)
                return;

            if (burstCount > 0)
            {
                if (!ps.isPlaying)
                    ps.Play(true);
                ps.Emit(burstCount);
            }
            else
            {
                if (ps.isPlaying)
                    ps.Clear(true);
                ps.Play(true);
            }
        }

        void StopLoopParticles()
        {
            if (loopWhileStreaking == null)
                return;
            for (int i = 0; i < loopWhileStreaking.Length; i++)
            {
                ParticleSystem ps = loopWhileStreaking[i];
                if (ps != null)
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
}
