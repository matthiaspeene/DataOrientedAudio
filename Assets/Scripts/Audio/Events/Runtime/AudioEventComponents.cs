// File: Audio/Events/Runtime/AudioEventComponents.cs
using Unity.Entities;
using Unity.Mathematics;
using DataOrientedAudio.Common.Runtime;

namespace DataOrientedAudio.Events.Runtime
{
    // One play request, stored in a DynamicBuffer on the emitter.
    public struct AudioEvent : IBufferElementData
    {
        public int VoiceTypeHash;      // Hash of the VoiceData name (replaces Entity VoiceDef)

        public AudioEventSpace Space;  // Stereo2D / World3D / Attached3D

        public float Gain;             // Override gain (1.0 = default)
        public float PlaybackSpeed;    // Override playback speed (1.0 = default speed)

        public float3 Position;        // Used if Space == World3D, Behaves as an offset if Space == Attached3D
        public Entity AttachTo;        // Used if Space == Attached3D
    }

    // Marks an entity as a producer of AudioEvents.
    public struct AudioEventEmitter : IComponentData
    {
        public Entity DefaultVoiceDef;    // Optional default sound to use
        public AudioEventSpace DefaultSpace;
    }
}
