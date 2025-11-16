using DataOrientedAudio.Common.Runtime;
using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.Voice.Runtime
{
    internal class VoiceSpatializationComponents
    {
        // Resolved spatialization data for a single voice entity.
        public struct VoiceSpatialization : IComponentData
        {
            public AudioEventSpace Space;

            public float3 Position;   // For World3D; can also be used as an offset.
            public Entity AttachTo;   // For Attached3D; the entity whose transform we follow.
        }

        // Optional: link a voice back to its logical owner / emitter.
        public struct VoiceOwner : IComponentData
        {
            public Entity Emitter;
        }
    }
}
