using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace VestibularGame.Game
{
    /// <summary>State names to play on one <see cref="Animator"/> for each reaction. An empty name skips that reaction.</summary>
    [Serializable]
    public sealed class AnimatorStates
    {
        public Animator animator;
        [Tooltip("State played on look-back and session start (e.g. body: \"Glowing&scale 0\", eyes/mouth: \"Happy\").")]
        public string happy;
        [Tooltip("State played on look-away, sad pick (e.g. eyes/mouth: \"Sad\").")]
        public string sad;
        [Tooltip("State played on look-away, wave pick (e.g. body: \"Waving\").")]
        public string wave;
        [Tooltip("State played when the session ends (e.g. body: \"wining flip\").")]
        public string win;
        public int layer = 0;
        [Tooltip("Cross-fade time in seconds. 0 or less plays the state immediately.")]
        public float crossFade = 0.15f;
    }

    /// <summary>
    /// Makes the character (star body, eyes, mouth) react to <see cref="GameSession"/> events by playing
    /// named Animator states: happy on look-back / session start, sad or wave (random) on look-away, win on session end.
    /// </summary>
    public sealed class CharacterReactions : MonoBehaviour
    {
        [SerializeField] GameSession session;
        [Tooltip("One entry per Animator (e.g. star body, eyes, mouth). All of them react together.")]
        [SerializeField] AnimatorStates[] animators;
        [Tooltip("Chance that a look-away plays Wave instead of Sad.")]
        [Range(0f, 1f)] [SerializeField] float waveChance = 0.5f;

        /// <summary>Raised after each random look-away pick. True = wave, false = sad.</summary>
        public event Action<bool> LookAwayReaction;

        readonly Dictionary<string, int> hashes = new Dictionary<string, int>();
        readonly HashSet<string> warned = new HashSet<string>();

        void OnEnable()
        {
            CacheHashes();
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (session == null) return;
            session.SessionStarted += PlayHappy;
            session.LookBack += PlayHappy;
            session.LookAway += PlayLookAway;
            session.SessionEnded += OnSessionEnded;
        }

        void OnDisable()
        {
            if (session == null) return;
            session.SessionStarted -= PlayHappy;
            session.LookBack -= PlayHappy;
            session.LookAway -= PlayLookAway;
            session.SessionEnded -= OnSessionEnded;
        }

        void OnDestroy() => LookAwayReaction = null;

        void OnSessionEnded(SessionStats stats) => PlayWin();

        public void PlayHappy() => PlayAll(a => a.happy);
        public void PlaySad() => PlayAll(a => a.sad);
        public void PlayWave() => PlayAll(a => a.wave);
        public void PlayWin() => PlayAll(a => a.win);

        /// <summary>Picks Wave (with <c>waveChance</c>) or Sad at random and plays it on all animators.</summary>
        public void PlayLookAway()
        {
            bool wave = Random.value < waveChance;
            if (wave) PlayWave(); else PlaySad();
            LookAwayReaction?.Invoke(wave);
        }

        void CacheHashes()
        {
            hashes.Clear();
            if (animators == null) return;
            foreach (var a in animators)
            {
                if (a == null) continue;
                Cache(a.happy); Cache(a.sad); Cache(a.wave); Cache(a.win);
            }
        }

        void Cache(string state)
        {
            if (!string.IsNullOrEmpty(state)) hashes[state] = Animator.StringToHash(state);
        }

        void PlayAll(Func<AnimatorStates, string> pick)
        {
            if (animators == null) return;
            foreach (var a in animators)
            {
                if (a == null || a.animator == null) continue;
                Play(a, pick(a));
            }
        }

        void Play(AnimatorStates a, string state)
        {
            if (string.IsNullOrEmpty(state)) return;
            if (!hashes.TryGetValue(state, out int hash))
            {
                hash = Animator.StringToHash(state);
                hashes[state] = hash;
            }
            if (!a.animator.HasState(a.layer, hash))
            {
                if (warned.Add(state))
                    Debug.LogWarning($"CharacterReactions: state \"{state}\" not found on layer {a.layer} of {a.animator.name}.", this);
                return;
            }
            if (a.crossFade > 0f) a.animator.CrossFadeInFixedTime(hash, a.crossFade, a.layer);
            else a.animator.Play(hash, a.layer);
        }
    }
}
