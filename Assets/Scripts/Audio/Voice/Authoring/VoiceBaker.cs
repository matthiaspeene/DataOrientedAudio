using Unity.Entities;
using UnityEngine;
using DataOrientedAudio.Common.Runtime;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Voice.Authoring
{
    public class VoiceBaker : Baker<VoiceAuthoring>
    {
        public override void Bake(VoiceAuthoring authoring)
        {
            VoiceDataScriptable voiceData = authoring.voiceData;
            // Change TransformUsageFlags if you tie this voice entity to world position.
            var entity = GetEntity(TransformUsageFlags.None);

            #region Gain
            // Allocate channel gain buffer and init to 1.0f.
            var gains = AddBuffer<OutChannelGain>(entity);

            // Harcode to stereo for now; extend later as needed. TBA::Author the master output count.
            int outputChannelCount = 2;
            for (int i = 0; i < outputChannelCount; i++)
            {
                gains.Add(new OutChannelGain { Value = 1f });
            }

            AddComponent(entity, new MixGainMod
            {
                BusIndex = -1, // TBA: for future use.
                Value = 1f
            });

            if (voiceData.UseRandomGain)
            {
                AddComponent(entity, new RandomGainMod
                {
                    Range = voiceData.GainRange,
                    Result = 1f
                });
            }
            #endregion

            #region PlaybackSpeed

            RandomRange playbackSpeedRange = voiceData.GetPitchAsPlaybackSpeedRange();
            AddComponent(entity, new OutPlaybackSpeed
            {
                Value = playbackSpeedRange.Max
            });

            if (voiceData.UseRandomPitch)
            {
                AddComponent(entity, new RandomPlaybackSpeedMod
                {
                    Range = playbackSpeedRange,
                    Result = 0f
                });
            }
            #endregion

            #region State Components
            // These are toggled by systems; start disabled.
            AddComponent(entity, new VoiceActive { Age = 0f });
            SetComponentEnabled<VoiceActive>(entity, false);

            AddComponent<StartVoiceRequest>(entity);
            SetComponentEnabled<StartVoiceRequest>(entity, false);

            AddComponent<StopVoiceRequest>(entity);
            SetComponentEnabled<StopVoiceRequest>(entity, false);
            #endregion
        }
    }
}
