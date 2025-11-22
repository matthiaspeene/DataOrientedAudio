using Unity.Entities;
using UnityEngine;
using DataOrientedAudio.Common.Runtime;
using DataOrientedAudio.Voice.Runtime;
using Unity.Mathematics;
using Unity.Transforms;

namespace DataOrientedAudio.Voice.Authoring
{
    public class VoiceBaker : Baker<VoiceAuthoring>
    {
        public override void Bake(VoiceAuthoring authoring)
        {
            VoiceDataScriptable voiceData = authoring.VoiceData;
            // Pool Loop
            for (int v = 0; v < voiceData.MaxVoices; v++)
            {
                // Create entity (first one is 'entity', others are additional)
                var voiceEntity = (v == 0) ? GetEntity(TransformUsageFlags.None) : CreateAdditionalEntity(TransformUsageFlags.None);

                // 1. Core Data
                int typeId = voiceData.name.GetHashCode();
                AddSharedComponent(voiceEntity, new VoiceTypeID { Value = typeId });

                // 2. Spatialization (Conditional)
                if (voiceData.Is3D)
                {
                    // Add Transform
                    AddComponent(voiceEntity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1f });

                    // Add Spatial Components
                    // Note: We don't need VoiceIsSpatial anymore as presence of Transform implies 3D capability in this design,
                    // OR we keep it for logic. The plan said remove it.
                    // We DO need VoiceFollowsEntity for attachment.
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
    }
}
