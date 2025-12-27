using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using UnityEngine;
using DataOrientedAudio.Common;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Voice.Authoring
{
    public class VoiceBaker : Baker<VoiceAuthoring>
    {
        // Equal-power center pan gain (sqrt(2)/2)
        private const float CenterPanGain = 0.707f;

        public override void Bake(VoiceAuthoring authoring)
        {
            VoiceDataScriptable voiceData = authoring.VoiceData;

            // Early exit if no voice data is assigned
            if (voiceData == null)
            {
                Debug.LogWarning($"VoiceAuthoring on '{authoring.gameObject.name}' has no VoiceData assigned. Skipping bake.", authoring);
                return;
            }

            // Create the VoiceBlob asset once for all voices of this type
            // This blob contains ALL clip data for this voice type
            BlobAssetReference<VoiceBlob> voiceBlobRef = CreateVoiceBlob(voiceData);

            // Determine transformation flags based on the voice type's spatial setting.
            // Spatial voices require transform components to be processed by the audio system.
            TransformUsageFlags transformUsageFlags = authoring.VoiceData.Space == AudioEventSpace.Stereo2D
                ? TransformUsageFlags.None
                : TransformUsageFlags.Dynamic;

            // Pool Loop
            for (int v = 0; v < voiceData.MaxVoices; v++)
            {
                // Create entity (first one is 'entity', others are additional)
                Entity voiceEntity = (v == 0)
                    ? GetEntity(transformUsageFlags)
                    : CreateAdditionalEntity(transformUsageFlags, false, voiceBlobRef.ToString());

                // 1. Core Data
                int typeId = voiceData.name.GetHashCode();
                AddSharedComponent(voiceEntity, new VoiceTypeID { Value = typeId });

                // 1b. Add VoiceBlob reference (shared immutable data containing all clips)
                AddBlobAsset(ref voiceBlobRef, out _);
                AddComponent(voiceEntity, new VoiceBlobReference { Value = voiceBlobRef });

                // 2. Spatialization (Archetypes)
                switch (voiceData.Space)
                {
                    case AudioEventSpace.World3D:
                        // The LocalTransform is already added by TransformUsageFlags.Dynamic.
                        // We set/overwrite it here to ensure the pool entity has a clean default.
                        AddComponent(voiceEntity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1f });

                        // Add Spatialization Components
                        var spatialGains = AddBuffer<SpatializationChannelGains>(voiceEntity);
                        spatialGains.Add(new SpatializationChannelGains { Value = CenterPanGain }); // Left channel (center pan)
                        spatialGains.Add(new SpatializationChannelGains { Value = CenterPanGain }); // Right channel (center pan)
                        break;

                    case AudioEventSpace.Attached3D:
                        // The LocalTransform is already added by TransformUsageFlags.Dynamic.
                        AddComponent(voiceEntity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1f });

                        // Add Follow Components
                        AddComponent(voiceEntity, new VoiceFollowsEntity { Target = Entity.Null });
                        AddComponent(voiceEntity, new VoicePositionOffset { Value = float3.zero });

                        // Add Spatialization Components
                        var attachedSpatialGains = AddBuffer<SpatializationChannelGains>(voiceEntity);
                        attachedSpatialGains.Add(new SpatializationChannelGains { Value = CenterPanGain }); // Left channel (center pan)
                        attachedSpatialGains.Add(new SpatializationChannelGains { Value = CenterPanGain }); // Right channel (center pan)
                        break;

                    case AudioEventSpace.Stereo2D:
                    default:
                        // No local transform or spatial components for 2D.
                        break;
                }

                // 3. Gain
                var gains = AddBuffer<OutChannelGain>(voiceEntity);
                int outputChannelCount = 2; // Hardcoded stereo
                for (int i = 0; i < outputChannelCount; i++)
                {
                    gains.Add(new OutChannelGain { Value = 1f });
                }

                AddComponent(voiceEntity, new MixGainMod { BusIndex = voiceData.MixBusIndex, Value = 1f });

                if (voiceData.UseRandomGain)
                {
                    AddComponent(voiceEntity, new RandomGainMod { Result = 1f });
                    AddSharedComponent(voiceEntity, new VoiceRandomGainRange
                    {
                        Min = voiceData.GainRange.Min,
                        Max = voiceData.GainRange.Max
                    });
                }

                // 3a. Distance Attenuation
                // We extract the linear falloff range from the start and end of the curve:
                // Start key time = MinDistance, End key time = MaxDistance.
                if (voiceData.DistanceAttenuation != null && voiceData.DistanceAttenuation.length >= 2 && voiceData.Space != AudioEventSpace.Stereo2D)
                {
                    Keyframe firstKey = voiceData.DistanceAttenuation[0];
                    Keyframe lastKey = voiceData.DistanceAttenuation[voiceData.DistanceAttenuation.length - 1];

                    // Add settings
                    AddSharedComponent(voiceEntity, new DistanceAttenuationSettings
                    {
                        MinDistance = firstKey.time,
                        MaxDistance = lastKey.time
                    });

                    // Add runtime gain modifier (default to 1.0 until system updates it)
                    AddComponent(voiceEntity, new DistanceAttenuationGainMod { Value = 1f });
                }

                // 3b. Playback Position
                RandomRange randomPlaybackPositionRange = voiceData.GetPlaybackPositionRangeInSamples();
                AddComponent(voiceEntity, new OutPlaybackStartPosition { Value = (int)randomPlaybackPositionRange.Min });
                if (voiceData.UseRandomPlaybackPosition)
                {
                    AddComponent(voiceEntity, new RandomPlaybackPositionMod { Result = (int)randomPlaybackPositionRange.Min });
                    AddSharedComponent(voiceEntity, new VoiceRandomPlaybackPositionRange
                    {
                        Min = (int)randomPlaybackPositionRange.Min,
                        Max = (int)randomPlaybackPositionRange.Max
                    });
                    //Debug.Log("Added RandomPlaybackPositionMod " + voiceData.name + " " + randomPlaybackPositionRange.Min + " " + randomPlaybackPositionRange.Max);
                }

                // 4. Playback Speed
                RandomRange playbackSpeedRange = voiceData.GetPitchAsPlaybackSpeedRange();
                AddComponent(voiceEntity, new OutPlaybackSpeed { Value = playbackSpeedRange.Max });

                if (voiceData.UseRandomPitch)
                {
                    AddComponent(voiceEntity, new RandomPlaybackSpeedMod { Result = 0f });
                    AddSharedComponent(voiceEntity, new VoiceRandomPlaybackSpeedRange
                    {
                        Min = playbackSpeedRange.Min,
                        Max = playbackSpeedRange.Max
                    });
                }

                // 5. State
                AddComponent(voiceEntity, new VoiceActive { Age = 0f });
                SetComponentEnabled<VoiceActive>(voiceEntity, false);

                AddComponent<StartVoiceRequest>(voiceEntity);
                SetComponentEnabled<StartVoiceRequest>(voiceEntity, false);

                AddComponent<StopVoiceRequest>(voiceEntity);
                SetComponentEnabled<StopVoiceRequest>(voiceEntity, false);

                // 6. Identity & Topology
                AddComponent(voiceEntity, new VoiceLocalIndex { Value = v });
                AddComponent(voiceEntity, new VoiceArchetypeIndex { Value = -1 }); // Assigned at runtime

                // 7. Triggering
                if (voiceData.TriggerMode == Triggermode.Repeat)
                {
                    AddComponent(voiceEntity, new TriggerRepeat
                    {
                        DelayMin = voiceData.RepeatDelayRange.x,
                        DelayMax = voiceData.RepeatDelayRange.y,
                        IsWaitingForRepeat = false,
                        NextRepetitionTime = 0
                    });
                }
            }
        }

        /// <summary>
        /// Creates a VoiceBlob asset from VoiceDataScriptable.
        /// This blob contains all immutable voice data including all audio clips.
        /// The entire blob is shared by all voice entities of this type.
        /// </summary>
        private BlobAssetReference<VoiceBlob> CreateVoiceBlob(VoiceDataScriptable voiceData)
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref VoiceBlob voiceBlob = ref builder.ConstructRoot<VoiceBlob>();

            // Set bus index
            voiceBlob.OutputBusIndex = voiceData.MixBusIndex;

            // Set voice-level parameters
            voiceBlob.Loop = voiceData.TriggerMode == Triggermode.Loop; // TBA: Add other trigger modes

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
