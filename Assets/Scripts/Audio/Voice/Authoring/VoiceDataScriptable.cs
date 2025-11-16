using UnityEngine;
using DataOrientedAudio.Common.Runtime;

namespace DataOrientedAudio.Voice.Authoring
{

    public enum Triggermode
    {
        Once,
        Loop,
        Repeat
    }

    [CreateAssetMenu(
        fileName = "VoiceData",
        menuName = "Audio/VoiceData",
        order = 0)]
    public class VoiceDataScriptable : ScriptableObject
    {
        [Header("Clips")]
        [SerializeField] private AudioClip[] _clips;

        [Header("Level & Pitch")]
        [Tooltip("Min/Max linear gain. X = min, Y = max.")]
        [SerializeField] private RandomRange _gainRange = new RandomRange(1f, 1f);

        [Tooltip("Min/Max pitch multiplier. X = min, Y = max.")]
        [SerializeField] private RandomRange _pitchRange = new RandomRange(0f, 0f);

        [Header("Triggering")]
        [SerializeField] private Triggermode _triggermode = Triggermode.Once;

        [Tooltip("Used when TriggerMode is Repeat. X = min delay, Y = max delay (seconds).")]
        [SerializeField] private Vector2 _repeatDelayRange = new Vector2(0f, 0f);

        // --- Accessors ---
        public AudioClip[] Clips => _clips;
        public RandomRange GainRange => _gainRange;
        public RandomRange PitchRange => _pitchRange;
        public Triggermode TriggerMode => _triggermode;
        public Vector2 RepeatDelayRange => _repeatDelayRange;

        public RandomRange GetPitchAsPlaybackSpeedRange()
        {
            return new RandomRange(
                PitchToPlaybackSpeed(_pitchRange.Min),
                PitchToPlaybackSpeed(_pitchRange.Max));
        }

        #region bake helpers
        public bool UseRandomGain => _gainRange.Min != _gainRange.Max;
        public bool UseRandomPitch => _pitchRange.Min != _pitchRange.Max;
        #endregion

        private void OnValidate()
        {
            _gainRange.Min = Mathf.Min(_gainRange.Min, _gainRange.Max);
            _pitchRange.Max = Mathf.Max(_pitchRange.Min, _pitchRange.Max);
            _repeatDelayRange.x = Mathf.Max(0f, _repeatDelayRange.x);
            _repeatDelayRange.y = Mathf.Max(_repeatDelayRange.x, _repeatDelayRange.y);
        }

        /// <summary>
        /// Returns a random AudioClip from the list or null if none assigned.
        /// </summary>
        public AudioClip GetRandomClip()
        {
            if (_clips == null || _clips.Length == 0)
                return null;

            int index = Random.Range(0, _clips.Length);
            return _clips[index];
        }

        /// <summary>
        /// Returns a random gain multiplier within the configured range.
        /// </summary>
        public float GetRandomGain() =>
            Random.Range(_gainRange.x, _gainRange.y);

        /// <summary>
        /// Returns a random pitch multiplier within the configured range.
        /// </summary>
        public float GetRandomPitch() =>
            Random.Range(_pitchRange.x, _pitchRange.y);

        /// <summary>
        /// Returns a random repeat delay in seconds.
        /// </summary>
        public float GetRandomRepeatDelay() =>
            Random.Range(_repeatDelayRange.x, _repeatDelayRange.y);

        private float PitchToPlaybackSpeed(float pitch)
        {
            return Unity.Mathematics.math.pow(2f, pitch / 12f);
        }

    }
}