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
                // Consume all messages from the control side.
                // NOTE: In your project you might want to use UpdateIfDataIsAvailable
                // as the update setting for cheap polling.

                var input = pipe.GetAvailableData(context);

                // Register archetypes / update blob+ranges.
                // TODO: Fix TryRead - AvailableData does not contain TryRead
                /*
                while (input.TryRead(out RegisterArchetypeMessage reg))
                {
                    if ((uint)reg.ArchetypeIndex >= (uint)Archetypes.Length)
                        continue; // Safety guard

                    var meta = Archetypes[reg.ArchetypeIndex];
                    meta.Blob = reg.Blob;
                    meta.Start = reg.Start;
                    meta.Count = reg.Count;
                    Archetypes[reg.ArchetypeIndex] = meta;
                }

                // Voice gain updates.
                while (input.TryRead(out SetVoiceGainMessage gainMsg))
                {
                    if ((uint)gainMsg.GlobalVoiceIndex >= (uint)GainsL.Length)
                        continue;

                    if (gainMsg.ChannelIndex == 0)
                        GainsL[gainMsg.GlobalVoiceIndex] = gainMsg.Value;
                    else if (gainMsg.ChannelIndex == 1)
                        GainsR[gainMsg.GlobalVoiceIndex] = gainMsg.Value;
                }

                // Voice active flags.
                while (input.TryRead(out SetVoiceActiveMessage activeMsg))
                {
                    if ((uint)activeMsg.GlobalVoiceIndex >= (uint)ActiveFlags.Length)
                        continue;

                    ActiveFlags[activeMsg.GlobalVoiceIndex] = activeMsg.IsActive ? (byte)1 : (byte)0;
                }
                */
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
            }

            public void EndProcessing(in RealtimeContext context, Pipe pipe, ChannelBuffer output)
            {
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
                // 1. Read ECS singleton/state that describes archetype → (blob, start, count).
                // Note: This runs on the main thread (usually), so we can access managed bridge.
                // If it runs on a worker thread, we might have issues accessing managed systems.
                // Assuming Control.Configure runs on Main Thread or has access to World.

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

                // Dispose previous allocations if needed.
                if (realtime.Archetypes.IsCreated)
                {
                    realtime.Archetypes.Dispose();
                    realtime.GainsL.Dispose();
                    realtime.GainsR.Dispose();
                    realtime.ActiveFlags.Dispose();
                    realtime.MixBuffer.Dispose();
                }

                if (_maxArchetypes <= 0 || _totalVoices <= 0)
                {
                    _maxArchetypes = 1;
                    _totalVoices = 1;
                }

                realtime.Archetypes = new NativeArray<ArchetypeMeta>(_maxArchetypes, Allocator.Persistent);
                realtime.GainsL = new NativeArray<float>(_totalVoices, Allocator.Persistent);
                realtime.GainsR = new NativeArray<float>(_totalVoices, Allocator.Persistent);
                realtime.ActiveFlags = new NativeArray<byte>(_totalVoices, Allocator.Persistent);

                // Mix buffer: one sample per channel per frame.
                var bufferSamples = format.bufferFrameCount * format.channelCount;
                realtime.MixBuffer = new NativeArray<float>(bufferSamples, Allocator.Persistent);

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
