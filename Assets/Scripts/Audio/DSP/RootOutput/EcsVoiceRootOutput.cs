using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities; // BlobAssetReference<T>
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;
using DataOrientedAudio.Voice.Runtime;

// NOTE: This is a sketch. It focuses on data layout and pipe messaging.
// The actual DSP (reading samples, spatialization, etc.) is intentionally minimal.

namespace DataOrientedAudio.DSP.RootOutput
{
    /// <summary>
    /// Shared, per-archetype metadata used by the realtime side.
    /// Archetype i owns voices in the range [Start, Start + Count).
    /// </summary>
    internal struct ArchetypeMeta
    {
        public BlobAssetReference<VoiceBlob> Blob;
        public int Start;
        public int Count;
    }

    #region Pipe messages


    /// <summary>
    /// Sent from Control → Realtime when an archetype becomes known
    /// or its blob/range mapping changes. This currently only happens on bootstrap.
    /// </summary>
    public struct RegisterArchetypeMessage
    {
        public int ArchetypeIndex;
        public BlobAssetReference<VoiceBlob> Blob;
        public int Start;
        public int Count;
    }


    /// <summary>
    /// Sent from Control → Realtime to update a single voice gain.
    /// GlobalVoiceIndex = ArchetypeMeta.Start + localVoiceIndex.
    /// </summary>
    public struct SetVoiceGainMessage
    {
        public int GlobalVoiceIndex;
        public int ChannelIndex;
        public float Value;
    }

    /// <summary>
    /// Mark a voice active / inactive.
    /// </summary>
    public struct SetVoiceActiveMessage
    {
        public int GlobalVoiceIndex;
        public bool IsActive;
    }

    #endregion

    /// <summary>
    /// Root output that mixes ECS-driven voices using a global SoA layout and
    /// per-archetype contiguous ranges.
    /// </summary>
    public static partial class EcsVoiceRootOutput
    {
    }

    #region Small NativeArray helpers

    internal static class NativeArrayExtensions
    {
        public static void Fill(this NativeArray<float> array, float value)
        {
            for (int i = 0; i < array.Length; i++)
                array[i] = value;
        }
    }

    #endregion
}
