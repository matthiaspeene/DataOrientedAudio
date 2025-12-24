using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;
using Unity.Mathematics;
using DataOrientedAudio.Voice.Runtime;
using NUnit.Framework.Internal.Commands;

namespace DataOrientedAudio.DSP.RootOutput
{
    [BurstCompile]
    public static partial class EcsVoiceRootOutput
    {
        [BurstCompile(CompileSynchronously = true)]
        public struct Realtime : RootOutputInstance.IRealtime
        {
            #region State

            internal AudioFormat Format;
            internal int MaxArchetypes;
            internal int MaxVoices;
            internal int MaxBuses;

            // Bus data
            internal NativeArray<BusMeta> BusMeta;
            internal NativeArray<NativeList<int>> BusActiveArchetypes;
            internal NativeArray<NativeList<int>> BusActiveVoices;
            internal NativeArray<float> BusBuffers;

            // Archetype data
            internal NativeArray<ArchetypeMeta> Archetypes;

            // Voice data
            internal NativeArray<bool> VoiceActiveFlags;
            internal NativeArray<float> Gains; // globalVoiceIndex * channels
            internal NativeArray<float> PreviousGains; // globalVoiceIndex * channels
            internal NativeArray<float> PlaybackSpeeds;
            internal NativeArray<float> PlaybackPositions;
            internal NativeQueue<int> FinishedVoiceIndices;

            // Job handles
            internal NativeArray<JobHandle> BusJobHandles;
            JobHandle mixJobHandle;

            #endregion

            public Realtime(int maxArchetypes, int totalVoices, int dspBufferSize, AudioSpeakerMode speakerMode, int maxBuses) : this()
            {
                MaxArchetypes = maxArchetypes;
                MaxVoices = totalVoices;
                MaxBuses = maxBuses;
                Format = new AudioFormat(speakerMode, AudioSettings.outputSampleRate, dspBufferSize);
                Archetypes = new NativeArray<ArchetypeMeta>(maxArchetypes, Allocator.Persistent);
                BusMeta = new NativeArray<BusMeta>(maxBuses, Allocator.Persistent);

                for (int i = 0; i < maxBuses; i++)
                {
                    BusMeta[i] = new BusMeta
                    {
                        Start = i * dspBufferSize,
                        Size = dspBufferSize,
                        ChannelCount = Format.channelCount
                    };
                }

                PlaybackPositions = new NativeArray<float>(totalVoices, Allocator.Persistent);
                PlaybackSpeeds = new NativeArray<float>(totalVoices, Allocator.Persistent);
                for (int i = 0; i < totalVoices; i++) PlaybackSpeeds[i] = 1f;

                FinishedVoiceIndices = new NativeQueue<int>(Allocator.Persistent);
                VoiceActiveFlags = new NativeArray<bool>(totalVoices, Allocator.Persistent);

                BusActiveVoices = new NativeArray<NativeList<int>>(maxBuses, Allocator.Persistent);
                BusActiveArchetypes = new NativeArray<NativeList<int>>(maxBuses, Allocator.Persistent);

                for (int i = 0; i < maxBuses; i++)
                {
                    BusActiveVoices[i] = new NativeList<int>(Allocator.Persistent);
                    BusActiveArchetypes[i] = new NativeList<int>(Allocator.Persistent);
                }

                int speakerChannels;
                switch (speakerMode)
                {
                    case AudioSpeakerMode.Mono:
                        speakerChannels = 1;
                        break;
                    case AudioSpeakerMode.Stereo:
                        speakerChannels = 2;
                        break;
                    case AudioSpeakerMode.Quad:
                        speakerChannels = 4;
                        break;
                    case AudioSpeakerMode.Surround:
                        speakerChannels = 5;
                        break;
                    case AudioSpeakerMode.Mode5point1:
                        speakerChannels = 6;
                        break;
                    case AudioSpeakerMode.Mode7point1:
                        speakerChannels = 8;
                        break;
                    case AudioSpeakerMode.Prologic:
                        speakerChannels = 2;
                        break;
                    default:
                        speakerChannels = 2;
                        break;
                }

                Gains = new NativeArray<float>(totalVoices * speakerChannels, Allocator.Persistent);
                PreviousGains = new NativeArray<float>(totalVoices * speakerChannels, Allocator.Persistent);

                for (int i = 0; i < Gains.Length; i++)
                {
                    Gains[i] = 1f;
                    PreviousGains[i] = 1f;
                }

                int bufferSamples = dspBufferSize * speakerChannels;
                BusBuffers = new NativeArray<float>(bufferSamples * maxBuses, Allocator.Persistent);

                for (int i = 0; i < maxBuses; i++)
                {
                    BusMeta[i] = new BusMeta
                    {
                        Start = i * bufferSamples,
                        Size = bufferSamples,
                        ChannelCount = speakerChannels
                    }; // TODO: Support bus-specific channel counts 
                }

                BusJobHandles = new NativeArray<JobHandle>(maxBuses, Allocator.Persistent);
                Format = new AudioFormat(speakerMode, AudioSettings.outputSampleRate, dspBufferSize);
            }

            #region Update (messages)

            public void Update(UpdatedDataContext context, Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (element.TryGetData(out RegisterArchetypeMessage reg))
                    {
                        Archetypes[reg.ArchetypeIndex] = new ArchetypeMeta
                        {
                            Blob = reg.Blob,
                            Start = reg.Start,
                            Count = reg.Count
                        };
                    }

                    if (element.TryGetData(out SetVoiceGainMessage gain))
                    {
                        int idx = gain.GlobalVoiceIndex * Format.channelCount + gain.ChannelIndex;
                        Gains[idx] = gain.Value;
                    }

                    if (element.TryGetData(out SetVoicePlaybackSpeedMessage speedMsg))
                    {
                        PlaybackSpeeds[speedMsg.GlobalVoiceIndex] = speedMsg.Value;
                    }

                    if (element.TryGetData(out SetVoiceActiveMessage active))
                    {
                        bool newState = active.IsActive;
                        bool oldState = VoiceActiveFlags[active.GlobalVoiceIndex];
                        if (oldState == newState) continue;

                        VoiceActiveFlags[active.GlobalVoiceIndex] = newState;

                        // If voice is becoming active, initialize previous gains to current (target) gains
                        // to avoid smoothing from zero/stale values.
                        if (newState)
                        {
                            int baseIdx = active.GlobalVoiceIndex * Format.channelCount;
                            for (int ch = 0; ch < Format.channelCount; ch++)
                            {
                                PreviousGains[baseIdx + ch] = Gains[baseIdx + ch];
                            }
                        }

                        // Safety check: if the archetype isn't registered yet, we can't determine the bus.
                        // This can happen during startup before the bootstrap message arrives.
                        var arch = Archetypes[active.ArchetypeIndex];
                        if (!arch.Blob.IsCreated)
                        {
                            UnityEngine.Debug.LogWarning($"[EcsVoiceRootOutput] Voice activated for archetype {active.ArchetypeIndex} but it's not registered yet. Defaulting to Bus 0.");
                        }

                        int busIndex = arch.Blob.IsCreated ? arch.Blob.Value.OutputBusIndex : 0;
                        var busVoiceList = BusActiveVoices[busIndex];
                        var busArchetypeList = BusActiveArchetypes[busIndex];

                        if (newState)
                        {
                            busVoiceList.Add(active.GlobalVoiceIndex);
                            busArchetypeList.Add(active.ArchetypeIndex);

                            // Starting position: 0 for forward, end for reverse
                            float speed = PlaybackSpeeds[active.GlobalVoiceIndex];
                            if (speed < 0 && arch.Blob.IsCreated && arch.Blob.Value.Clips.Length > 0)
                            {
                                PlaybackPositions[active.GlobalVoiceIndex] = arch.Blob.Value.Clips[0].SampleCount - 0.001f;
                            }
                            else
                            {
                                PlaybackPositions[active.GlobalVoiceIndex] = 0f;
                            }
                        }
                        else
                        {
                            int listIdx = busVoiceList.IndexOf(active.GlobalVoiceIndex);
                            if (listIdx != -1)
                            {
                                busVoiceList.RemoveAtSwapBack(listIdx);
                                busArchetypeList.RemoveAtSwapBack(listIdx);
                            }
                        }

                        // Write back modified list structs to the array
                        BusActiveVoices[busIndex] = busVoiceList;
                        BusActiveArchetypes[busIndex] = busArchetypeList;
                    }
                }
            }

            #endregion

            #region Process

            public void Process(in RealtimeContext context, Pipe pipe, JobHandle input)
            {
                BusBuffers.Fill(0f);

                for (int bus = 0; bus < BusMeta.Length; bus++)
                {
                    if (BusActiveArchetypes[bus].IsEmpty)
                    {
                        BusJobHandles[bus] = default;
                        continue;
                    }

                    var job = new BusMixJob
                    {
                        BusMeta = BusMeta[bus],
                        Archetypes = Archetypes,
                        ActiveArchetypes = BusActiveArchetypes[bus].AsArray(),
                        ActiveVoices = BusActiveVoices[bus].AsArray(),
                        PlaybackPositions = PlaybackPositions,
                        PlaybackSpeeds = PlaybackSpeeds,
                        Gains = Gains,
                        PreviousGains = PreviousGains,
                        Format = Format,
                        OutputBuffer = BusBuffers,
                        FinishedVoices = FinishedVoiceIndices.AsParallelWriter()
                    };

                    BusJobHandles[bus] = job.Schedule(input);
                }

                var combinedHandle = JobHandle.CombineDependencies(BusJobHandles);

                mixJobHandle = new MixJob
                {
                    Buffers = BusBuffers,
                    BusMeta = BusMeta,
                    BufferLength = BusMeta[0].Size
                }.Schedule(combinedHandle);
            }

            #endregion

            #region Jobs

            [BurstCompile]
            struct BusMixJob : IJob
            {
                [ReadOnly] public NativeArray<ArchetypeMeta> Archetypes;
                [ReadOnly] public NativeArray<int> ActiveArchetypes;
                [ReadOnly] public NativeArray<int> ActiveVoices;
                [ReadOnly] public NativeArray<float> Gains;
                [NativeDisableContainerSafetyRestriction] public NativeArray<float> PreviousGains;
                [ReadOnly] public AudioFormat Format;

                [NativeDisableContainerSafetyRestriction] public NativeArray<float> PlaybackSpeeds;
                [NativeDisableContainerSafetyRestriction] public NativeArray<float> PlaybackPositions;
                [NativeDisableContainerSafetyRestriction] public NativeArray<float> OutputBuffer;
                [NativeDisableContainerSafetyRestriction] public NativeQueue<int>.ParallelWriter FinishedVoices;

                public BusMeta BusMeta;

                [BurstCompile]
                public void Execute()
                {
                    var busBuffer = OutputBuffer.Slice(BusMeta.Start, BusMeta.Size);
                    int channels = Format.channelCount;
                    int frames = busBuffer.Length / channels;

                    int currentArchetypeIndex = -1;
                    BlobAssetReference<VoiceBlob> currentBlob = default;
                    int currentClipChannelCount = 0;
                    int currentClipSampleCount = 0;

                    for (int v = 0; v < ActiveVoices.Length; v++)
                    {
                        int global = ActiveVoices[v];
                        int archetypeIndex = ActiveArchetypes[v];

                        // Cache-previous optimization
                        if (archetypeIndex != currentArchetypeIndex)
                        {
                            currentArchetypeIndex = archetypeIndex;
                            var meta = Archetypes[archetypeIndex];
                            currentBlob = meta.Blob;
                            ref var blobRef = ref currentBlob.Value;

                            if (blobRef.Clips.Length == 0)
                            {
                                currentArchetypeIndex = -1; // Mark invalid
                                continue;
                            }

                            ref var clip = ref blobRef.Clips[0];
                            currentClipChannelCount = clip.ChannelCount;
                            currentClipSampleCount = clip.SampleCount;
                        }

                        if (currentArchetypeIndex == -1) continue;

                        ref var samples = ref currentBlob.Value.Clips[0].Samples;
                        float pos = PlaybackPositions[global];
                        float speed = PlaybackSpeeds[global];
                        bool loop = currentBlob.Value.Loop;

                        for (int f = 0; f < frames; f++)
                        {
                            if (pos >= currentClipSampleCount || pos < 0)
                            {
                                if (!loop)
                                {
                                    FinishedVoices.Enqueue(global);
                                    break;
                                }
                                pos -= (float)currentClipSampleCount * math.floor(pos / currentClipSampleCount);
                            }

                            int floorPos = (int)math.floor(pos);
                            int ceilPos = floorPos + 1;
                            float frac = pos - floorPos;

                            int gainBase = global * channels;
                            float t = (float)f / frames;
                            int dstBase = f * channels;

                            for (int ch = 0; ch < channels; ch++)
                            {
                                int srcCh = ch < currentClipChannelCount ? ch : 0;

                                // Linear interpolation for playback speed
                                float s0 = samples[floorPos * currentClipChannelCount + srcCh];
                                float s1 = 0f;
                                if (ceilPos < currentClipSampleCount)
                                {
                                    s1 = samples[ceilPos * currentClipChannelCount + srcCh];
                                }
                                else if (loop)
                                {
                                    s1 = samples[srcCh];
                                }

                                float sample = math.lerp(s0, s1, frac);
                                float smoothedGain = math.lerp(PreviousGains[gainBase + ch], Gains[gainBase + ch], t);

                                busBuffer[dstBase + ch] += sample * smoothedGain;
                            }

                            pos += speed;
                        }

                        // Update previous gains for next block
                        int voiceGainBase = global * channels;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            PreviousGains[voiceGainBase + ch] = Gains[voiceGainBase + ch];
                        }

                        PlaybackPositions[global] = pos;
                    }
                }
            }


            [BurstCompile]
            struct MixJob : IJob
            {
                public NativeArray<float> Buffers;
                public NativeArray<BusMeta> BusMeta;
                public int BufferLength;

                public void Execute()
                {
                    var master = Buffers.Slice(0, BufferLength);
                    // No zeroing here, master bus (index 0) is populated by BusMixJob or remains empty if nothing plays.

                    for (int i = 1; i < BusMeta.Length; i++)
                    {
                        var src = Buffers.Slice(BusMeta[i].Start, BusMeta[i].Size);
                        for (int j = 0; j < src.Length; j++)
                            master[j] += src[j];
                    }
                }
            }

            #endregion

            #region EndProcessing

            public void EndProcessing(in RealtimeContext context, Pipe pipe, ChannelBuffer output)
            {
                mixJobHandle.Complete();

                int channels = output.channelCount;
                for (int f = 0; f < output.frameCount; f++)
                {
                    int baseIdx = f * channels;
                    for (int ch = 0; ch < channels; ch++)
                        output[ch, f] = BusBuffers[baseIdx + ch];
                }

                while (FinishedVoiceIndices.TryDequeue(out int idx))
                    pipe.SendData(context, new VoiceFinishedMessage { GlobalVoiceIndex = idx });
            }

            #endregion

            public JobHandle EarlyProcessing(in RealtimeContext context, Pipe pipe)
            {
                // Not used in this sketch, but this is another place where you // could read messages if you wanted them as close to Process as possible. return default;
                return default;
            }

            public void RemovedFromProcessing()
            {
                // Ensure any pending jobs are completed
                if (mixJobHandle.IsCompleted == false)
                    mixJobHandle.Complete();

                for (int i = 0; i < BusJobHandles.Length; i++)
                {
                    if (BusJobHandles[i].IsCompleted == false)
                        BusJobHandles[i].Complete();
                }

                if (VoiceActiveFlags.IsCreated) VoiceActiveFlags.Dispose();
                if (Gains.IsCreated) Gains.Dispose();
                if (PreviousGains.IsCreated) PreviousGains.Dispose();
                if (PlaybackSpeeds.IsCreated) PlaybackSpeeds.Dispose();
                if (PlaybackPositions.IsCreated) PlaybackPositions.Dispose();
                if (FinishedVoiceIndices.IsCreated) FinishedVoiceIndices.Dispose();

                if (Archetypes.IsCreated) Archetypes.Dispose();

                if (BusMeta.IsCreated) BusMeta.Dispose();
                if (BusBuffers.IsCreated) BusBuffers.Dispose();
                if (BusJobHandles.IsCreated) BusJobHandles.Dispose();

                if (BusActiveArchetypes.IsCreated)
                {
                    for (int i = 0; i < BusActiveArchetypes.Length; i++)
                    {
                        if (BusActiveArchetypes[i].IsCreated)
                            BusActiveArchetypes[i].Dispose();
                    }
                    BusActiveArchetypes.Dispose();
                }

                if (BusActiveVoices.IsCreated)
                {
                    for (int i = 0; i < BusActiveVoices.Length; i++)
                    {
                        if (BusActiveVoices[i].IsCreated)
                            BusActiveVoices[i].Dispose();
                    }
                    BusActiveVoices.Dispose();
                }
            }
        }
    }
}
