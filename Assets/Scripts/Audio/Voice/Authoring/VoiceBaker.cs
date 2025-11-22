using Unity.Entities;
using UnityEngine;
using DataOrientedAudio.Common;
using DataOrientedAudio.Voice.Runtime;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

namespace DataOrientedAudio.Voice.Authoring
{
    public class VoiceBaker : Baker<VoiceAuthoring>
    {
        public override void Bake(VoiceAuthoring authoring)
        {
            VoiceDataScriptable voiceData = authoring.VoiceData;

            // Create the VoiceBlob asset once for all voices of this type
            // This blob contains ALL clip data for this voice type
            BlobAssetReference<VoiceBlob> voiceBlobRef = CreateVoiceBlob(voiceData);

            // Pool Loop
            for (int v = 0; v < voiceData.MaxVoices; v++)
            {
                // Create entity (first one is 'entity', others are additional)
                var voiceEntity = (v == 0) ? GetEntity(TransformUsageFlags.None) : CreateAdditionalEntity(TransformUsageFlags.None);

                // 1. Core Data
                int typeId = voiceData.name.GetHashCode();
                AddSharedComponent(voiceEntity, new VoiceTypeID { Value = typeId });

                // 1b. Add VoiceBlob reference (shared immutable data containing all clips)
                AddBlobAsset(ref voiceBlobRef, out var hash);
                AddComponent(voiceEntity, new VoiceBlobReference { Value = voiceBlobRef });

                // Note: We no longer need a SampleDataBlob buffer - all clip data is in the VoiceBlob

                // 2. Spatialization (Conditional)
                if (voiceData.Is3D)
                {
                    // Add Transform
                    AddComponent(voiceEntity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1f });

                    // Add Spatial Components
                    AddComponent(voiceEntity, new VoiceFollowsEntity { Target = Entity.Null });
                    SetComponentEnabled<VoiceFollowsEntity>(voiceEntity, false);

                    AddComponent(voiceEntity, new VoicePositionOffset { Value = float3.zero });
                }

                // 3. Gain
                var gains = AddBuffer<OutChannelGain>(voiceEntity);
                int outputChannelCount = 2; // Hardcoded stereo
                for (int i = 0; i < outputChannelCount; i++)
                {
                    gains.Add(new OutChannelGain { Value = 1f });
                }

                AddComponent(voiceEntity, new MixGainMod { BusIndex = -1, Value = 1f });

                if (voiceData.UseRandomGain)
                {
                    AddComponent(voiceEntity, new RandomGainMod { Range = voiceData.GainRange, Result = 1f });
                }

                // 4. Playback Speed
                RandomRange playbackSpeedRange = voiceData.GetPitchAsPlaybackSpeedRange();
                AddComponent(voiceEntity, new OutPlaybackSpeed { Value = playbackSpeedRange.Max });

                if (voiceData.UseRandomPitch)
                {
                    AddComponent(voiceEntity, new RandomPlaybackSpeedMod { Range = playbackSpeedRange, Result = 0f });
                }

                // 5. State
                AddComponent(voiceEntity, new VoiceActive { Age = 0f });
                SetComponentEnabled<VoiceActive>(voiceEntity, false);

                AddComponent<StartVoiceRequest>(voiceEntity);
                SetComponentEnabled<StartVoiceRequest>(voiceEntity, false);

                AddComponent<StopVoiceRequest>(voiceEntity);
                SetComponentEnabled<StopVoiceRequest>(voiceEntity, false);
            }
        }

        /// <summary>
        /// Creates a VoiceBlob asset from VoiceDataScriptable.
        /// This blob contains all immutable voice data including all audio clips.
        /// The entire blob is shared by all voice entities of this type.
        /// </summary>
        private BlobAssetReference<VoiceBlob> CreateVoiceBlob(VoiceDataScriptable voiceData)
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref VoiceBlob voiceBlob = ref builder.ConstructRoot<VoiceBlob>();

                // Set voice-level parameters
                var gainRange = voiceData.GainRange;
                voiceBlob.GainMin = gainRange.Min;
                voiceBlob.GainMax = gainRange.Max;

                var playbackSpeedRange = voiceData.GetPitchAsPlaybackSpeedRange();
                voiceBlob.PlaybackSpeedMin = playbackSpeedRange.Min;
                voiceBlob.PlaybackSpeedMax = playbackSpeedRange.Max;

                // Count valid clips
                int validClipCount = 0;
                for (int i = 0; i < voiceData.Clips.Length; i++)
                {
                    if (voiceData.Clips[i] != null)
                        validClipCount++;
                }

                // Allocate clips array
                var clipsArray = builder.Allocate(ref voiceBlob.Clips, validClipCount);

                // Build each clip's data
                int clipIndex = 0;
                for (int i = 0; i < voiceData.Clips.Length; i++)
                {
                    AudioClip clip = voiceData.Clips[i];
                    if (clip == null)
                        continue;

                    ref ClipData clipData = ref clipsArray[clipIndex];

                    // Get audio data from Unity AudioClip
                    float[] samples = new float[clip.samples * clip.channels];
                    clip.GetData(samples, 0);

                    // Allocate and copy sample data into blob array
                    var samplesArray = builder.Allocate(ref clipData.Samples, samples.Length);
                    for (int s = 0; s < samples.Length; s++)
                    {
                        samplesArray[s] = samples[s];
                    }

                    // Set clip metadata
                    clipData.ChannelCount = clip.channels;
                    clipData.SampleRate = clip.frequency;
                    clipData.SampleCount = clip.samples;

                    clipIndex++;
                }

                return builder.CreateBlobAssetReference<VoiceBlob>(Allocator.Persistent);
            }
        }
    }
}
