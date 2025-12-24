using Unity.Entities;
using Unity.Collections;

namespace DataOrientedAudio.Voice.Runtime
{
    /// <summary>
    /// Describes a single voice archetype in the global audio topology.
    /// </summary>
    public struct AudioTopologyArchetype
    {
        public BlobAssetReference<VoiceBlob> Blob;
        public int ArchetypeIndex;
        public int Start;
        public int Count;
    }

    /// <summary>
    /// Singleton component holding the global audio topology summary.
    /// </summary>
    public struct AudioTopologySingleton : IComponentData
    {
        public int MaxArchetypes;
        public int TotalVoices;
        // The actual list of archetypes is stored in a NativeList managed by the system
        // or accessed via the Bridge.
    }

    /// <summary>
    /// Managed wrapper for passing topology data to the control thread.
    /// </summary>
    public struct AudioTopologyData
    {
        public int MaxArchetypes;
        public int TotalVoices;
        public int MaxBuses;
        public NativeArray<AudioTopologyArchetype> Archetypes;
    }
}
