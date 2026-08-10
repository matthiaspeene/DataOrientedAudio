using DataOrientedAudio.Common;
using Unity.Entities;

namespace DataOrientedAudio.Events.Runtime
{
    /// <summary>
    /// Requests an audio event when the entity participates in a Unity Physics collision.
    /// The entity must also have an AudioEvent buffer and a collider which raises contact events.
    /// </summary>
    public struct PlayAudioOnCollision : IComponentData
    {
        public int VoiceTypeHash;
        public AudioEventSpace Space;
        public float CooldownSeconds;
        public float MinimumImpactSpeed;
        public float LoudImpactSpeed;
        public float QuietImpactGain;
        public float LoudImpactGain;
        public double NextAllowedTime;
    }
}
