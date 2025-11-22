using DataOrientedAudio.Common.Runtime;
using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.Voice.Runtime
{
    // Tag: If enabled, this voice is treated as 3D/Spatial.
    // If disabled, it is 2D (Stereo).
    public struct VoiceIsSpatial : IComponentData, IEnableableComponent { }

    // Data: If enabled, this voice follows a target entity.
    public struct VoiceFollowsEntity : IComponentData, IEnableableComponent
    {
        public Entity Target;
    }

    // Data: Optional offset. Can be used with or without following.
    public struct VoicePositionOffset : IComponentData
    {
        public float3 Value;
    }

    // Optional: link a voice back to its logical owner / emitter.
    public struct VoiceOwner : IComponentData
    {
        public Entity Emitter;
    }
}
