// File: Audio/Voice/Authoring/VoiceAuthoring.cs
using UnityEngine;
using DataOrientedAudio.Common;

namespace DataOrientedAudio.Voice.Authoring
{
    public class VoiceAuthoring : MonoBehaviour
    {
        public VoiceDataScriptable VoiceData;

        private void OnValidate()
        {
            if (VoiceData == null)
            {
                Debug.LogWarning($"VoiceAuthoring on '{gameObject.name}' has no VoiceData assigned. Skipping bake.", this);
            }

            this.gameObject.name = $"Voice_{VoiceData.name}";
        }
    }
}
