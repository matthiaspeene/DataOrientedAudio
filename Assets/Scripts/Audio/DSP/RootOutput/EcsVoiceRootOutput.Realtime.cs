using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.DSP.RootOutput
{
    public static partial class EcsVoiceRootOutput
    {
        [BurstCompile(CompileSynchronously = true)]
        public struct Realtime : RootOutputInstance.IRealtime
        {
            #region State

            internal NativeArray<ArchetypeMeta> Archetypes;   // length = maxArchetypes
            internal NativeArray<byte> ActiveFlags;           // 0 = inactive, 1 = active
            internal NativeArray<int> PlaybackPositions;      // Current sample index in clip per voice

            // Mixing Data
            internal NativeArray<float> GainsL;               // length = totalVoices
            internal NativeArray<float> GainsR;               // length = totalVoices

            // Output Buffer
            internal NativeArray<float> MixBuffer;            // interleaved or planar backing buffer
            internal NativeArray<float> TempBuffers;          // temp buffer for archetype mixing (maxArchetypes * bufferLength)

            // Config
            internal AudioFormat Format;

            // Job Handles
            JobHandle voicesJobHandle;
            JobHandle mixJobHandle;

            #endregion

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
                        // TODO: Support multi-channel audio
                        if (gainMsg.ChannelIndex == 0)
                        {
                            GainsL[gainMsg.GlobalVoiceIndex] = gainMsg.Value;
                        }
                        else
                        {
                            GainsR[gainMsg.GlobalVoiceIndex] = gainMsg.Value;
                        }
                    }

                    if (element.TryGetData(out SetVoiceActiveMessage activeMsg))
                    {
                        ActiveFlags[activeMsg.GlobalVoiceIndex] = activeMsg.IsActive ? (byte)1 : (byte)0;
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
                // Clear MixBuffer
                MixBuffer.Fill(0);

                int bufferLength = MixBuffer.Length;
                int archetypeCount = Archetypes.Length;

                // Clear the pre-allocated temp buffers
                // Layout: [Archetype0_Samples] [Archetype1_Samples] ...
                TempBuffers.Fill(0);

                var handles = new NativeList<JobHandle>(archetypeCount, Allocator.Temp);

                // 1. Have each archetype run a per-archetype SIMD job that writes into that archetypes buffer.
                for (int i = 0; i < archetypeCount; i++)
                {
                    var meta = Archetypes[i];

                    // Skip if empty or invalid
                    if (meta.Count == 0 || !meta.Blob.IsCreated)
                        continue;

                    var job = new ArchetypeVoiceJob
                    {
                        Meta = meta,
                        ActiveFlags = ActiveFlags,
                        PlaybackPositions = PlaybackPositions,
                        GainsL = GainsL,
                        GainsR = GainsR,
                        OutputBuffer = TempBuffers.GetSubArray(i * bufferLength, bufferLength),
                        Format = Format
                    };

                    handles.Add(job.Schedule(input));
                }

                voicesJobHandle = JobHandle.CombineDependencies(handles.AsArray());
                handles.Dispose();

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
                [ReadOnly] public NativeArray<byte> ActiveFlags;
                [ReadOnly] public NativeArray<float> GainsL;
                [ReadOnly] public NativeArray<float> GainsR;
                [ReadOnly] public AudioFormat Format;

                [NativeDisableParallelForRestriction]
                public NativeArray<int> PlaybackPositions;

                public NativeArray<float> OutputBuffer;

                public void Execute()
                {
                    // Clear the output buffer for this archetype
                    OutputBuffer.Fill(0);

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

                    // Iterate through all voices in this archetype
                    for (int i = 0; i < Meta.Count; i++)
                    {
                        int globalIndex = Meta.Start + i;

                        // Check if voice is active
                        if (ActiveFlags[globalIndex] == 0) continue;

                        int position = PlaybackPositions[globalIndex];
                        float gainL = GainsL[globalIndex];
                        float gainR = GainsR[globalIndex];

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

                                // Apply gain (assuming simple stereo mapping)
                                float gain = (ch == 0) ? gainL : gainR;

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
                    float sum = 0;
                    for (int i = 0; i < ArchetypeCount; i++)
                    {
                        sum += ArchetypeBuffers[i * BufferLength + index];
                    }
                    MixBuffer[index] += sum;
                }
            }

            public void EndProcessing(in RealtimeContext context, Pipe pipe, ChannelBuffer output)
            {
                mixJobHandle.Complete();

                var temp = new ChannelBuffer(MixBuffer, Format.channelCount);

                // Assumes layout matches.
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
    }
}
