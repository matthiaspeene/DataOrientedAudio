using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.Voice.Runtime
{
    /// <summary>
    /// Audio listener singleton entity component.
    /// Tracks listener position, orientation, and velocity for spatialization and future Doppler effects.
    /// </summary>
    public struct AudioListener : IComponentData
    {
        public float3 Position;
        public float3 Forward;
        public float3 Right;
        public float3 Up;
        public float3 Velocity;
        public float3 PreviousPosition;
    }

    /// <summary>
    /// Tag component to identify the audio listener singleton entity.
    /// </summary>
    public struct AudioListenerTag : IComponentData { }
}
