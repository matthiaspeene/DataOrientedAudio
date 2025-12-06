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
    /// or its blob/range mapping changes.
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
    /// Optional: mark a voice active/inactive.
    /// You can fold this into your own flags array if you prefer.
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
    public static class EcsVoiceRootOutput
    {
        #region Realtime

        [BurstCompile(CompileSynchronously = true)]
        public struct Realtime : RootOutputInstance.IRealtime
        {
            // Realtime state – all unmanaged.
            internal NativeArray<ArchetypeMeta> Archetypes;   // length = maxArchetypes
            // TODO: In the future this will be a vector of gains to support multi-channel audio. For now we just use left and right. *This will be a challenge becouse it would mean an array of arrays. See if there's a better soltuion.
            internal NativeArray<float> GainsL;                // length = totalVoices
            internal NativeArray<float> GainsR;                // length = totalVoices
            internal NativeArray<byte> ActiveFlags;           // 0 = inactive, 1 = active
            internal NativeArray<float> MixBuffer;            // interleaved or planar backing buffer

            internal AudioFormat Format;
            JobHandle m_MixJob;

            #region Mixing job

            [BurstCompile]
            struct MixVoicesJob : IJob
            {
                [ReadOnly] public NativeArray<ArchetypeMeta> Archetypes;
                [ReadOnly] public NativeArray<float> GainsL;
                [ReadOnly] public NativeArray<float> GainsR;
                [ReadOnly] public NativeArray<byte> ActiveFlags;

                public AudioFormat Format;

                // Backing buffer for the final mix (per frame × channel).
                public NativeArray<float> MixBuffer;

                public void Execute()
                {
                    // For this sketch we just clear the buffer and pretend each active voice
                    // contributes its gain as a DC signal. Replace this with your real DSP.
                    MixBuffer.Fill(0f);

                    var buffer = new ChannelBuffer(MixBuffer, Format.channelCount);

                    var frameCount = buffer.frameCount;
                    var channelCount = buffer.channelCount;

                    // Super simple example: sum gains of all active voices into all channels.
                    float totalGain = 0f;
                    var voiceCount = GainsL.Length;

                    for (int i = 0; i < voiceCount; i++)
                    {
                        if (ActiveFlags[i] == 0)
                            continue;

                        totalGain += GainsL[i];
                    }

                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        for (int ch = 0; ch < channelCount; ch++)
                        {
                            buffer[ch, frame] = totalGain; // Replace with real mixing later.
                        }
                    }
                }
            }

            #endregion

            #region Realtime lifecycle

            public void Update(UpdatedDataContext context, Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (element.TryGetData(out RegisterArchetypeMessage regMsg))
                    {
                        Debug.Log($"RegisterArchetypeMessage in realtime: {regMsg}");
                    }

                    if (element.TryGetData(out SetVoiceGainMessage gainMsg))
                    {
                        Debug.Log($"SetVoiceGainMessage in realtime: {gainMsg}");

                    }

                    if (element.TryGetData(out SetVoiceActiveMessage activeMsg))
                    {
                        Debug.Log($"SetVoiceActiveMessage in realtime: {activeMsg}");
                    }
                }
            }

            public JobHandle EarlyProcessing(in RealtimeContext context, Pipe pipe)
            {
                // Not used in this sketch, but this is another place where you
                // could read messages if you wanted them as close to Process as possible.
                return default;
            }

            public void Process(in RealtimeContext context, Pipe pipe, JobHandle input)
            {
                // Kick the mixing job. In your real implementation this will:
                // - For each archetype, run a per-archetype SIMD job that writes into MixBuffer.
                // - Possibly split by channel layout / mixer targets.

                /*
                var job = new MixVoicesJob
                {
                    Archetypes = Archetypes,
                    GainsL = GainsL,
                    GainsR = GainsR,
                    ActiveFlags = ActiveFlags,
                    Format = Format,
                    MixBuffer = MixBuffer,
                };

                m_MixJob = job.Schedule(input);
                */
            }

            public void EndProcessing(in RealtimeContext context, Pipe pipe, ChannelBuffer output)
            {
                /*
                // Fence on mixing and copy to Unity's output buffer.
                m_MixJob.Complete();

                var temp = new ChannelBuffer(MixBuffer, Format.channelCount);

                // Assumes layout matches. If not, convert/mix appropriately here.
                var frameCount = output.frameCount;
                var channelCount = output.channelCount;

                for (int frame = 0; frame < frameCount; frame++)
                {
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        output[ch, frame] = temp[ch, frame];
                    }
                }
                */
            }

            public void RemovedFromProcessing()
            {
                // Buffers are owned and disposed by the Control side (Dispose / reconfigure).
            }

            #endregion
        }

        #endregion

        #region Control

        /// <summary>
        /// Control side for the ECS-driven root output.
        /// It owns allocation/disposal and translates ECS commands → pipe messages.
        /// </summary>
        public struct Control : RootOutputInstance.IControl<Realtime>
        {
            private int _maxArchetypes;
            private int _totalVoices;
            private bool _bootstrapSent;
            private AudioTopologyData _topology; // Cache topology to access Blob references

            public Control(int maxArchetypes, int totalVoices)
            {
                _maxArchetypes = maxArchetypes;
                _totalVoices = totalVoices;
                _bootstrapSent = false;
                _topology = default;
            }

            #region Lifecycle

            public JobHandle Configure(ControlContext context, ref Realtime realtime, in AudioFormat format)
            {
                var topology = DataOrientedAudio.Voice.Runtime.EcsAudioBridge.GetTopology();

                // If topology is ready, use it. Otherwise fall back or wait.
                if (topology.MaxArchetypes > 0)
                {
                    _maxArchetypes = topology.MaxArchetypes;
                    _totalVoices = topology.TotalVoices;
                    _topology = topology;
                }

                // (Re)allocate native memory on the realtime side.
                realtime.Format = format;

                // Helper to safely reallocate if needed
                void EnsureArray<T>(ref NativeArray<T> array, int length) where T : struct
                {
                    if (array.IsCreated && array.Length == length)
                        return;

                    if (array.IsCreated)
                        array.Dispose();

                    array = new NativeArray<T>(length, Allocator.Persistent);
                }

                if (_maxArchetypes <= 0 || _totalVoices <= 0)
                {
                    _maxArchetypes = 1;
                    _totalVoices = 1;
                }

                EnsureArray(ref realtime.Archetypes, _maxArchetypes);
                EnsureArray(ref realtime.GainsL, _totalVoices);
                EnsureArray(ref realtime.GainsR, _totalVoices);
                EnsureArray(ref realtime.ActiveFlags, _totalVoices);

                // Mix buffer: one sample per channel per frame.
                var bufferSamples = format.bufferFrameCount * format.channelCount;
                EnsureArray(ref realtime.MixBuffer, bufferSamples);

                // Reset bootstrap flag so we send messages in the first Update
                _bootstrapSent = false;

                return default;
            }

            public void Dispose(ControlContext context, ref Realtime realtime)
            {
                if (realtime.Archetypes.IsCreated)
                {
                    realtime.Archetypes.Dispose();
                }

                if (realtime.GainsL.IsCreated)
                {
                    realtime.GainsL.Dispose();
                }

                if (realtime.GainsR.IsCreated)
                {
                    realtime.GainsR.Dispose();
                }

                if (realtime.ActiveFlags.IsCreated)
                {
                    realtime.ActiveFlags.Dispose();
                }

                if (realtime.MixBuffer.IsCreated)
                {
                    realtime.MixBuffer.Dispose();
                }
            }

            public void Update(ControlContext context, Pipe pipe)
            {
                // 2. For each archetype, if anything changed, send RegisterArchetypeMessage.
                // We do this once on bootstrap for now.
                if (!_bootstrapSent && _topology.MaxArchetypes > 0 && _topology.Archetypes.IsCreated)
                {
                    for (int i = 0; i < _topology.Archetypes.Length; i++)
                    {
                        var a = _topology.Archetypes[i];
                        var msg = new RegisterArchetypeMessage
                        {
                            ArchetypeIndex = a.ArchetypeIndex,
                            Blob = a.Blob,
                            Start = a.Start,
                            Count = a.Count,
                        };
                        pipe.SendData(context, msg);
                    }
                    _bootstrapSent = true;
                }

                // 3. Read ECS audio command buffer with per-voice gain/active changes.
                // We use a local buffer to copy commands safely from the bridge.
                var commands = new NativeList<DataOrientedAudio.Voice.Runtime.VoiceCommand>(Allocator.Temp);
                DataOrientedAudio.Voice.Runtime.EcsAudioBridge.GetCommands(commands);

                if (commands.IsCreated && commands.Length > 0)
                {
                    for (int i = 0; i < commands.Length; i++)
                    {
                        var cmd = commands[i];

                        // Get topology for this archetype to compute global index.
                        // We need to look up the archetype info.
                        // We cached _topology.
                        if (cmd.ArchetypeIndex >= 0 && cmd.ArchetypeIndex < _topology.Archetypes.Length)
                        {
                            var topo = _topology.Archetypes[cmd.ArchetypeIndex];
                            int globalIndex = topo.Start + cmd.LocalVoiceIndex;

                            switch (cmd.Type)
                            {
                                case DataOrientedAudio.Voice.Runtime.VoiceCommandType.SetGain:
                                    pipe.SendData(context, new SetVoiceGainMessage
                                    {
                                        GlobalVoiceIndex = globalIndex,
                                        ChannelIndex = cmd.ChannelIndex,
                                        Value = cmd.Value
                                    });
                                    break;

                                case DataOrientedAudio.Voice.Runtime.VoiceCommandType.SetActive:
                                    pipe.SendData(context, new SetVoiceActiveMessage
                                    {
                                        GlobalVoiceIndex = globalIndex,
                                        IsActive = cmd.Value != 0f,
                                    });
                                    UnityEngine.Debug.Log($"Processed active request for global voice: {globalIndex}");
                                    break;
                            }
                        }
                    }

                    // No need to clear bridge commands, the swap handled it.
                }
                commands.Dispose();
            }

            public Response OnMessage(ControlContext context, Pipe pipe, Message message)
            {
                // Optional: handle messages from the realtime side back to ECS.
                return Response.Unhandled;
            }

            #endregion
        }

        #endregion
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
