using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;
using DataOrientedAudio.Voice.Runtime;
using NUnit.Framework.Internal.Commands;

namespace DataOrientedAudio.DSP.RootOutput
{
    public static partial class EcsVoiceRootOutput
    {
        [BurstCompile(CompileSynchronously = true)]
        public struct Realtime : RootOutputInstance.IRealtime
        {
            #region State

            internal NativeArray<ArchetypeMeta> Archetypes;   // length = maxArchetypes
            internal NativeArray<byte> VoiceActiveFlags;           // 0 = inactive, 1 = active
            //internal NativeArray<int> ArchetypeActiveCounts;          // DEPRECATED: Replaced by ArchetypeActiveVoices
            internal NativeArray<NativeList<int>> ArchetypeActiveVoices; // List of active global indices per archetype
            internal NativeArray<int> PlaybackPositions;      // Current sample index in clip per voice

            // Mixing Data
            internal NativeArray<float> Gains;                // length = totalVoices * channelCount (interleaved)

            // Output Buffer
            internal NativeArray<float> MixBuffer;            // interleaved or planar backing buffer
            internal NativeArray<float> TempBuffers;          // temp buffer for archetype mixing (maxArchetypes * bufferLength)

            // Config
            internal AudioFormat Format;

            // Job Handles
            JobHandle voicesJobHandle;
            JobHandle mixJobHandle;

            internal NativeArray<JobHandle> Handles;

            #endregion

            public Realtime(int maxArchetypes, int totalVoices, int dspBufferSize, AudioSpeakerMode speakerMode) : this()
            {
                Archetypes = new NativeArray<ArchetypeMeta>(maxArchetypes, Allocator.Persistent);
                VoiceActiveFlags = new NativeArray<byte>(totalVoices, Allocator.Persistent);
                // ArchetypeActiveCounts = new NativeArray<int>(maxArchetypes, Allocator.Persistent);
                ArchetypeActiveVoices = new NativeArray<NativeList<int>>(maxArchetypes, Allocator.Persistent);
                for (int i = 0; i < maxArchetypes; i++)
                {
                    ArchetypeActiveVoices[i] = new NativeList<int>(Allocator.Persistent);
                }
                PlaybackPositions = new NativeArray<int>(totalVoices, Allocator.Persistent);

                int speakerChannels;
                switch (speakerMode)
                {
                    case AudioSpeakerMode.Mono: speakerChannels = 1; break;
                    case AudioSpeakerMode.Stereo: speakerChannels = 2; break;
                    case AudioSpeakerMode.Quad: speakerChannels = 4; break;
                    case AudioSpeakerMode.Surround: speakerChannels = 5; break;
                    case AudioSpeakerMode.Mode5point1: speakerChannels = 6; break;
                    case AudioSpeakerMode.Mode7point1: speakerChannels = 8; break;
                    case AudioSpeakerMode.Prologic: speakerChannels = 2; break;
                    default: speakerChannels = 2; break;
                }



                Gains = new NativeArray<float>(totalVoices * speakerChannels, Allocator.Persistent);
                for (int i = 0; i < Gains.Length; i++)
                {
                    Gains[i] = 1f;
                }

                int bufferSamples = dspBufferSize * speakerChannels;
                MixBuffer = new NativeArray<float>(bufferSamples, Allocator.Persistent);
                TempBuffers = new NativeArray<float>(bufferSamples * maxArchetypes, Allocator.Persistent);
                Handles = new NativeArray<JobHandle>(maxArchetypes, Allocator.Persistent);

                Format = new AudioFormat(speakerMode, AudioSettings.outputSampleRate, dspBufferSize);
            }

            #region Realtime lifecycle

            public void Update(UpdatedDataContext context, Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (element.TryGetData(out RegisterArchetypeMessage regMsg))
                    {
                        if (regMsg.ArchetypeIndex >= 0 && regMsg.ArchetypeIndex < Archetypes.Length)
                        {
                            Archetypes[regMsg.ArchetypeIndex] = new ArchetypeMeta
                            {
                                Blob = regMsg.Blob,
                                Start = regMsg.Start,
                                Count = regMsg.Count
                            };
                        }
                    }

                    if (element.TryGetData(out SetVoiceGainMessage gainMsg))
                    {
                        // Support multi-channel audio
                        int channelCount = Format.channelCount;
                        if (gainMsg.ChannelIndex < channelCount)
                        {
                            Gains[gainMsg.GlobalVoiceIndex * channelCount + gainMsg.ChannelIndex] = gainMsg.Value;
                        }
                    }

                    if (element.TryGetData(out SetVoiceActiveMessage activeMsg))
                    {
                        byte prevState = VoiceActiveFlags[activeMsg.GlobalVoiceIndex];
                        byte newState = activeMsg.IsActive ? (byte)1 : (byte)0;

                        if (prevState != newState)
                        {
                            VoiceActiveFlags[activeMsg.GlobalVoiceIndex] = newState;

                            if (activeMsg.IsActive)
                            {
                                ArchetypeActiveVoices[activeMsg.ArchetypeIndex].Add(activeMsg.GlobalVoiceIndex);
                            }
                            else
                            {
                                var list = ArchetypeActiveVoices[activeMsg.ArchetypeIndex];
                                for (int i = 0; i < list.Length; i++)
                                {
                                    if (list[i] == activeMsg.GlobalVoiceIndex)
                                    {
                                        list.RemoveAtSwapBack(i);
                                        break;
                                    }
                                }
                            }
                        }
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
                MixBuffer.Fill(0);
                TempBuffers.Fill(0);

                int bufferLength = MixBuffer.Length;
                int archetypeCount = Archetypes.Length;

                for (int i = 0; i < archetypeCount; i++)
                {
                    var meta = Archetypes[i];

                    // Skip if empty or invalid
                    if (meta.Count == 0 || !meta.Blob.IsCreated)
                    {
                        Handles[i] = default;
                        continue;
                    }

                    // Skip if archetype is not active
                    // Skip if archetype is not active
                    if (ArchetypeActiveVoices[i].IsEmpty)
                    {
                        Handles[i] = default;
                        continue;
                    }

                    var job = new ArchetypeVoiceJob
                    {
                        Meta = meta,
                        ActiveIndices = ArchetypeActiveVoices[i].AsArray(),
                        PlaybackPositions = PlaybackPositions,
                        Gains = Gains.GetSubArray(meta.Start * Format.channelCount, meta.Count * Format.channelCount),
                        OutputBuffer = TempBuffers.GetSubArray(i * bufferLength, bufferLength),
                        Format = Format
                    };

                    Handles[i] = job.Schedule(input);
                }

                voicesJobHandle = JobHandle.CombineDependencies(Handles);

                // 2. Mix all temp voice buffers into the MixBuffer.
                var mixJob = new MixJob
                {
                    ArchetypeBuffers = TempBuffers,
                    MixBuffer = MixBuffer,
                    ArchetypeCount = archetypeCount,
                    BufferLength = bufferLength
                };

                // Schedule the mix job
                mixJobHandle = mixJob.Schedule(bufferLength, 64, voicesJobHandle);
            }

            [BurstCompile]
            struct ArchetypeVoiceJob : IJob
            {
                [ReadOnly] public ArchetypeMeta Meta;
                [ReadOnly] public NativeArray<int> ActiveIndices;
                // [ReadOnly] public NativeArray<byte> ActiveFlags; // No longer needed
                [ReadOnly] public NativeSlice<float> Gains;
                [ReadOnly] public AudioFormat Format;

                [NativeDisableParallelForRestriction]
                public NativeArray<int> PlaybackPositions;

                public NativeSlice<float> OutputBuffer;

                public void Execute()
                {
                    ref var blob = ref Meta.Blob.Value;
                    if (blob.Clips.Length == 0) return;

                    // "For now just play clip one"
                    ref var clip = ref blob.Clips[0];
                    if (clip.Samples.Length == 0) return;

                    ref var samples = ref clip.Samples;
                    int clipSampleCount = clip.SampleCount;
                    int clipChannels = clip.ChannelCount;
                    int outputChannels = Format.channelCount;
                    int bufferFrames = OutputBuffer.Length / outputChannels;

                    // Iterate through all active voices in this archetype
                    for (int i = 0; i < ActiveIndices.Length; i++)
                    {
                        int globalIndex = ActiveIndices[i];
                        int localIndex = globalIndex - Meta.Start;

                        // Calculate gain index base using local index
                        int gainIndexBase = localIndex * outputChannels;

                        int position = PlaybackPositions[globalIndex];

                        // Debug
                        //Debug.Log($"Processing voice {globalIndex} with {outputChannels} channels and {clipChannels} channels and {bufferFrames} frames and {position} position and {clipSampleCount} sample count");

                        // Read samples
                        for (int f = 0; f < bufferFrames; f++)
                        {
                            // Loop processing. For now, just stop if we hit the end of the clip.
                            // User said: "We will impliment clip randomization later and what clip to play etc. later."
                            if (position >= clipSampleCount)
                            {
                                // Simple loop for now to avoid silence? Or stop? 
                                // "Just play clip one". Usually implies one-shot, but for testing looping is often better.
                                // I will loop it to ensure constant playback for testing.
                                position = 0;
                            }

                            for (int ch = 0; ch < outputChannels; ch++)
                            {
                                // Determine source channel mapping
                                // If source is mono (1 ch), read ch 0. If stereo, read matches.
                                int srcCh = (ch < clipChannels) ? ch : 0;

                                int sampleIndex = position * clipChannels + srcCh;
                                float sample = samples[sampleIndex];

                                // Apply gain
                                float gain = Gains[gainIndexBase + ch];

                                OutputBuffer[f * outputChannels + ch] += sample * gain;
                            }

                            position++;
                        }

                        PlaybackPositions[globalIndex] = position;
                    }
                }
            }

            [BurstCompile]
            struct MixJob : IJobParallelFor
            {
                [ReadOnly] public NativeArray<float> ArchetypeBuffers;
                public NativeArray<float> MixBuffer;
                public int ArchetypeCount;
                public int BufferLength;

                public void Execute(int index)
                {
                    float sum = 0f;
                    int baseOffset = index;

                    for (int i = 0; i < ArchetypeCount; i++)
                        sum += ArchetypeBuffers[i * BufferLength + baseOffset];

                    MixBuffer[index] = sum; // you already cleared MixBuffer, so = is fine
                }
            }

            public void EndProcessing(in RealtimeContext context, Pipe pipe, ChannelBuffer output)
            {
                mixJobHandle.Complete();

                int frameCount = output.frameCount;
                int channelCount = output.channelCount;

                for (int frame = 0; frame < frameCount; frame++)
                {
                    int baseIndex = frame * channelCount;
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        output[ch, frame] = MixBuffer[baseIndex + ch];
                    }
                }
            }

            public void RemovedFromProcessing()
            {
                // Buffers are owned and disposed by the Control side (Dispose / reconfigure).
                // Dispose internal lists
                if (ArchetypeActiveVoices.IsCreated)
                {
                    for (int i = 0; i < ArchetypeActiveVoices.Length; i++)
                    {
                        if (ArchetypeActiveVoices[i].IsCreated)
                            ArchetypeActiveVoices[i].Dispose();
                    }
                    ArchetypeActiveVoices.Dispose();
                }
            }

            #endregion
        }
    }
}
