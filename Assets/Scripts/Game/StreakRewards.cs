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
        const float MaxGrowDt = 0.1f;
        const float GrowStep = 1f / 120f;
        const float MinScale = 0.2f;
        const double ScheduleDelay = 0.1;

        [SerializeField, Tooltip("Session to listen to. Found automatically if left empty.")]
        GameSession session;

        [Header("Grow")]
        [SerializeField, Tooltip("Use a parent of the animated star. An Animator that animates scale on the same object would override this.")]
        Transform growTarget;
        [SerializeField, Tooltip("Extra scale (fraction of the base scale) gained per level.")]
        float growPerLevel = 0.15f;
        [SerializeField, Tooltip("Spring oscillation frequency in Hz.")]
        float springFrequency = 3f;
        [SerializeField, Range(0.05f, 1f), Tooltip("Damping ratio of the grow spring. Lower means more bounce; 1 means no overshoot.")]
        float springDamping = 0.3f;
        [SerializeField, Range(0.05f, 1f), Tooltip("Damping ratio used while shrinking back after a reset, so the return is soft.")]
        float resetDamping = 0.6f;
        [SerializeField, Tooltip("Extra spring velocity (scale fraction per second) added on each level up, for the pop.")]
        float levelUpKick = 1.2f;
        [SerializeField, Tooltip("Jelly deformation driven by the spring velocity. 0 turns it off.")]
        float squashStretch = 0.06f;

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
        float growVelocity;
        bool shrinking;
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
            growVelocity = 0f;
            shrinking = false;

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
            float omega = 2f * Mathf.PI * springFrequency;
            float zeta = shrinking ? resetDamping : springDamping;
            float stiffness = omega * omega;
            float damping = 2f * zeta * omega;

            float remaining = Mathf.Min(dt, MaxGrowDt);
            while (remaining > 0f)
            {
                float h = Mathf.Min(remaining, GrowStep);
                remaining -= h;
                float a = stiffness * (goal - growFactor) - damping * growVelocity;
                growVelocity += a * h;
                growFactor += growVelocity * h;
            }

            if (growFactor < MinScale - 1f)
            {
                growFactor = MinScale - 1f;
                if (growVelocity < 0f)
                    growVelocity = 0f;
            }

            float s = Mathf.Clamp(growVelocity * squashStretch, -0.25f, 0.25f);
            Vector3 k = baseScale * (1f + growFactor);
            growTarget.localScale = new Vector3(k.x * (1f + s), k.y * (1f - s * 0.8f), k.z);
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
            growVelocity = 0f;
            StopLoopParticles();
            StartMusic();
        }

        void OnLevelReached(int newLevel)
        {
            level = newLevel;
            growVelocity += levelUpKick;
            shrinking = false;

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
            shrinking = true;
            StopLoopParticles();

            if (sfx != null && resetClip != null)
                sfx.PlayOneShot(resetClip);

            onRewardReset?.Invoke();
        }

        void OnSessionEnded(SessionStats stats)
        {
            level = 0;
            shrinking = true;
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
