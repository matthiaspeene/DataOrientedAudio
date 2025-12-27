using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
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
        // Toggle for scheduling mode comparison
        public static readonly SharedStatic<bool> UseParallelScheduling = SharedStatic<bool>.GetOrCreate<Realtime, ParallelSchedulingKey>();
        private class ParallelSchedulingKey { }

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

            // Job handles - Restored for optional parallel execution
            internal NativeArray<JobHandle> BusJobHandles;
            JobHandle mixJobHandle;

            // Profiler markers
            static readonly ProfilerMarker s_UpdateMarker = new ProfilerMarker(ProfilerCategory.Audio, "EcsVoiceRootOutput.Realtime.Update");
            static readonly ProfilerMarker s_ProcessMarker = new ProfilerMarker(ProfilerCategory.Audio, "EcsVoiceRootOutput.Realtime.Process");
            static readonly ProfilerMarker s_EndProcessingMarker = new ProfilerMarker(ProfilerCategory.Audio, "EcsVoiceRootOutput.Realtime.EndProcessing");

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
                using (s_UpdateMarker.Auto())
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

                                // Set playback position from the message
                                PlaybackPositions[active.GlobalVoiceIndex] = active.PlaybackPosition;
                                //UnityEngine.Debug.Log("PlaybackPositions[" + active.GlobalVoiceIndex + "] = " + active.PlaybackPosition);
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
            }

            #endregion

            #region Process

            public void Process(in RealtimeContext context, Pipe pipe, JobHandle input)
            {
                using (s_ProcessMarker.Auto())
                {
                    BusBuffers.Fill(0f);

                    for (int bus = 0; bus < BusMeta.Length; bus++)
                    {
                        if (BusActiveArchetypes[bus].IsEmpty)
                        {
                            if (EcsVoiceRootOutput.UseParallelScheduling.Data)
                            {
                                BusJobHandles[bus] = default;
                            }
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

                        if (EcsVoiceRootOutput.UseParallelScheduling.Data)
                        {
                            BusJobHandles[bus] = job.Schedule(input);
                        }
                        else
                        {
                            job.Run();
                        }
                    }

                    var mixJob = new MixJob
                    {
                        Buffers = BusBuffers,
                        BusMeta = BusMeta,
                        BufferLength = BusMeta[0].Size
                    };

                    if (EcsVoiceRootOutput.UseParallelScheduling.Data)
                    {
                        var combined = JobHandle.CombineDependencies(BusJobHandles);
                        mixJobHandle = mixJob.Schedule(combined);
                    }
                    else
                    {
                        mixJob.Run();
                    }
                }
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
                public unsafe void Execute()
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
                            if (currentBlob.IsCreated)
                            {
                                ref var blobRef = ref currentBlob.Value;
                                if (blobRef.Clips.Length > 0)
                                {
                                    ref var clip = ref blobRef.Clips[0];
                                    currentClipChannelCount = clip.ChannelCount;
                                    currentClipSampleCount = clip.SampleCount;
                                }
                                else
                                {
                                    currentArchetypeIndex = -1;
                                }
                            }
                            else
                            {
                                currentArchetypeIndex = -1;
                            }
                        }

                        if (currentArchetypeIndex == -1) continue;

                        ref var samples = ref currentBlob.Value.Clips[0].Samples;
                        float pos = PlaybackPositions[global];
                        float speed = PlaybackSpeeds[global];
                        bool loop = currentBlob.Value.Loop;
                        int gainBase = global * channels;

                        // Pointers for faster access
                        float* outPtr = (float*)OutputBuffer.GetUnsafePtr() + BusMeta.Start;
                        float* samplePtr = (float*)samples.GetUnsafePtr();

                        // Resampling and Mixing
                        if (channels == 2 && currentClipChannelCount == 1)
                        {
                            // Stereo Output, Mono Source
                            float2 targetGain = *(float2*)((float*)Gains.GetUnsafeReadOnlyPtr() + gainBase);
                            float2 prevGain = *(float2*)((float*)PreviousGains.GetUnsafePtr() + gainBase);
                            float2 gainStep = (targetGain - prevGain) / frames;
                            float2 currentGain = prevGain;

                            for (int f = 0; f < frames; f++)
                            {
                                if (pos >= currentClipSampleCount || pos < 0)
                                {
                                    if (!loop) { FinishedVoices.Enqueue(global); break; }
                                    pos -= (float)currentClipSampleCount * math.floor(pos / currentClipSampleCount);
                                }

                                int floorPos = (int)math.floor(pos);
                                int ceilPos = (loop && floorPos == currentClipSampleCount - 1) ? 0 : floorPos + 1;
                                float frac = pos - floorPos;

                                float s0 = samplePtr[floorPos];
                                float s1 = (ceilPos < currentClipSampleCount) ? samplePtr[ceilPos] : 0f;
                                float sample = math.lerp(s0, s1, frac);

                                float2* dst = (float2*)(outPtr + f * 2);
                                *dst += sample * currentGain;

                                currentGain += gainStep;
                                pos += speed;
                            }
                            *(float2*)((float*)PreviousGains.GetUnsafePtr() + gainBase) = targetGain;
                        }
                        else if (channels == 2 && currentClipChannelCount == 2)
                        {
                            // Stereo Output, Stereo Source
                            float2 targetGain = *(float2*)((float*)Gains.GetUnsafeReadOnlyPtr() + gainBase);
                            float2 prevGain = *(float2*)((float*)PreviousGains.GetUnsafePtr() + gainBase);
                            float2 gainStep = (targetGain - prevGain) / frames;
                            float2 currentGain = prevGain;

                            for (int f = 0; f < frames; f++)
                            {
                                if (pos >= currentClipSampleCount || pos < 0)
                                {
                                    if (!loop) { FinishedVoices.Enqueue(global); break; }
                                    pos -= (float)currentClipSampleCount * math.floor(pos / currentClipSampleCount);
                                }

                                int floorPos = (int)math.floor(pos);
                                int ceilPos = (loop && floorPos == currentClipSampleCount - 1) ? 0 : floorPos + 1;
                                float frac = pos - floorPos;

                                float2 s0 = *(float2*)(samplePtr + floorPos * 2);
                                float2 s1 = (ceilPos < currentClipSampleCount) ? *(float2*)(samplePtr + ceilPos * 2) : 0f;
                                float2 sample = math.lerp(s0, s1, frac);

                                float2* dst = (float2*)(outPtr + f * 2);
                                *dst += sample * currentGain;

                                currentGain += gainStep;
                                pos += speed;
                            }
                            *(float2*)((float*)PreviousGains.GetUnsafePtr() + gainBase) = targetGain;
                        }
                        else if (channels == 1 && currentClipChannelCount == 1)
                        {
                            // Mono Output, Mono Source
                            float targetGain = Gains[gainBase];
                            float prevGain = PreviousGains[gainBase];
                            float gainStep = (targetGain - prevGain) / frames;
                            float currentGain = prevGain;

                            for (int f = 0; f < frames; f++)
                            {
                                if (pos >= currentClipSampleCount || pos < 0)
                                {
                                    if (!loop) { FinishedVoices.Enqueue(global); break; }
                                    pos -= (float)currentClipSampleCount * math.floor(pos / currentClipSampleCount);
                                }

                                int floorPos = (int)math.floor(pos);
                                int ceilPos = (loop && floorPos == currentClipSampleCount - 1) ? 0 : floorPos + 1;
                                float frac = pos - floorPos;

                                float s0 = samplePtr[floorPos];
                                float s1 = (ceilPos < currentClipSampleCount) ? samplePtr[ceilPos] : 0f;
                                float sample = math.lerp(s0, s1, frac);

                                outPtr[f] += sample * currentGain;

                                currentGain += gainStep;
                                pos += speed;
                            }
                            PreviousGains[gainBase] = targetGain;
                        }
                        else
                        {
                            // Generic Fallback
                            for (int f = 0; f < frames; f++)
                            {
                                if (pos >= currentClipSampleCount || pos < 0)
                                {
                                    if (!loop) { FinishedVoices.Enqueue(global); break; }
                                    pos -= (float)currentClipSampleCount * math.floor(pos / currentClipSampleCount);
                                }

                                int floorPos = (int)math.floor(pos);
                                int ceilPos = (loop && floorPos == currentClipSampleCount - 1) ? 0 : floorPos + 1;
                                float frac = pos - floorPos;

                                float smoothedT = (float)f / frames;
                                int dstBase = f * channels;

                                for (int ch = 0; ch < channels; ch++)
                                {
                                    int srcCh = ch < currentClipChannelCount ? ch : 0;
                                    float s0 = samplePtr[floorPos * currentClipChannelCount + srcCh];
                                    float s1 = (ceilPos < currentClipSampleCount) ? samplePtr[ceilPos * currentClipChannelCount + srcCh] : 0f;
                                    float sample = math.lerp(s0, s1, frac);
                                    float smoothedGain = math.lerp(PreviousGains[gainBase + ch], Gains[gainBase + ch], smoothedT);

                                    outPtr[dstBase + ch] += sample * smoothedGain;
                                }
                                pos += speed;
                            }

                            for (int ch = 0; ch < channels; ch++)
                                PreviousGains[gainBase + ch] = Gains[gainBase + ch];
                        }

                        PlaybackPositions[global] = pos;
                    }
                }
            }


            [BurstCompile]
            struct MixJob : IJob
            {
                public NativeArray<float> Buffers;
                [ReadOnly] public NativeArray<BusMeta> BusMeta;
                public int BufferLength;

                [BurstCompile]
                public unsafe void Execute()
                {
                    float* bufferPtr = (float*)Buffers.GetUnsafePtr();
                    float* masterPtr = bufferPtr; // Bus 0 is master

                    for (int i = 1; i < BusMeta.Length; i++)
                    {
                        var meta = BusMeta[i];
                        float* srcPtr = bufferPtr + meta.Start;
                        int size = meta.Size;

                        int vectorSegments = size / 4;
                        int remainder = size % 4;

                        float4* masterVec = (float4*)masterPtr;
                        float4* srcVec = (float4*)srcPtr;

                        for (int v = 0; v < vectorSegments; v++)
                        {
                            masterVec[v] += srcVec[v];
                        }

                        for (int r = size - remainder; r < size; r++)
                        {
                            masterPtr[r] += srcPtr[r];
                        }
                    }
                }
            }

            #endregion

            #region EndProcessing

            public void EndProcessing(in RealtimeContext context, Pipe pipe, ChannelBuffer output)
            {
                using (s_EndProcessingMarker.Auto())
                {
                    if (EcsVoiceRootOutput.UseParallelScheduling.Data)
                    {
                        mixJobHandle.Complete();
                    }

                    int channels = output.channelCount;
                    for (int f = 0; f < output.frameCount; f++)
                    {
                        int baseIdx = f * channels;
                        for (int ch = 0; ch < channels; ch++)
                            output[ch, f] = BusBuffers[baseIdx + ch];
                    }

                    while (FinishedVoiceIndices.TryDequeue(out int idx))
                    {
                        pipe.SendData(context, new VoiceFinishedMessage { GlobalVoiceIndex = idx });
                    }
                }
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
                if (mixJobHandle.IsCompleted == false) mixJobHandle.Complete();
                for (int i = 0; i < BusJobHandles.Length; i++) { if (BusJobHandles[i].IsCompleted == false) BusJobHandles[i].Complete(); }

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
