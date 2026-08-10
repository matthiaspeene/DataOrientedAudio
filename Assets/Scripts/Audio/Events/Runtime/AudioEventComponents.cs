// File: Audio/Events/Runtime/AudioEventComponents.cs
using Unity.Entities;
using Unity.Mathematics;
using DataOrientedAudio.Common;

namespace DataOrientedAudio.Events.Runtime
{
    // One play request, stored in a DynamicBuffer on the emitter.
    public struct AudioEvent : IBufferElementData
    {
        public int VoiceTypeHash;      // Hash of the VoiceData name (replaces Entity VoiceDef)

        public float3 Position;        // Used if Space == World3D, Behaves as an offset if Space == Attached3D
        public Entity AttachTo;        // Used if Space == Attached3D

        // Optional per-play gain. The explicit flag keeps existing/default-created events at unity gain.
        public float GainMultiplier;
        public bool UseGainMultiplier;
    }

    // Marks an entity as a producer of AudioEvents.
    public struct AudioEventEmitter : IComponentData
    {
        public Entity DefaultVoiceDef;    // Optional default sound to use
    }
}
