using Unity.Entities;

namespace DataOrientedAudio.Voice.Runtime
{
    /// <summary>
    /// Baked component: The index of this voice within its archetype (0 to VoiceCount-1).
    /// </summary>
    public struct VoiceLocalIndex : IComponentData
    {
        public int Value;
    }

    /// <summary>
    /// Runtime component: The index of the archetype this voice belongs to.
    /// Assigned by the AudioTopologySystem.
    /// </summary>
    public struct VoiceArchetypeIndex : IComponentData
    {
        public int Value;
    }
}
