
using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using DataOrientedAudio.Events.Runtime;
using DataOrientedAudio.Voice.Runtime;
using DataOrientedAudio.Common;
using Unity.Transforms;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct VoiceAllocationSystem : ISystem
    {
        #region Voice Allocation

        public void OnUpdate(ref SystemState state)
        {
            foreach (var (emitter, eventBuffer) in SystemAPI.Query<RefRW<AudioEventEmitter>, DynamicBuffer<AudioEvent>>())
            {
                if (eventBuffer.IsEmpty)
                    continue;

                ProcessAudioEvents(ref state, eventBuffer);
                eventBuffer.Clear();
            }
        }

        private void ProcessAudioEvents(ref SystemState state, DynamicBuffer<AudioEvent> eventBuffer)
        {
            for (int i = 0; i < eventBuffer.Length; i++)
            {
                AllocateVoice(ref state, eventBuffer[i]);
            }
        }

        private void AllocateVoice(ref SystemState state, AudioEvent evt)
        {
            EntityQuery query = CreateVoiceQuery(ref state, evt.VoiceTypeHash);
            NativeArray<Entity> candidates = query.ToEntityArray(Allocator.Temp);
            Entity selectedVoice = FindInactiveVoice(candidates);

            if (selectedVoice != Entity.Null)
            {
                ActivateVoice(selectedVoice);
                ApplyVoiceParameters(selectedVoice, evt);
                ApplySpatializationSettings(selectedVoice, evt);
            }

            candidates.Dispose();
        }

        #endregion

        #region Voice Selection

        private EntityQuery CreateVoiceQuery(ref SystemState state, int voiceTypeHash)
        {
            var query = state.GetEntityQuery(
                ComponentType.ReadWrite<VoiceActive>(),
                ComponentType.ReadWrite<StartVoiceRequest>(),
                ComponentType.ReadWrite<OutChannelGain>(),
                ComponentType.ReadWrite<OutPlaybackSpeed>(),
                ComponentType.ReadOnly<VoiceTypeID>()
            );

            query.SetSharedComponentFilter(new VoiceTypeID { Value = voiceTypeHash });
            return query;
        }

        private readonly Entity FindInactiveVoice(NativeArray<Entity> candidates)
        {
            // TODO: Maintain a NativeList of free voices per TypeID for better performance
            foreach (var candidate in candidates)
            {
                if (!SystemAPI.IsComponentEnabled<VoiceActive>(candidate))
                {
                    return candidate;
                }
            }

            return Entity.Null;
        }

        #endregion

        #region Voice Activation

        private readonly void ActivateVoice(Entity voice)
        {
            SystemAPI.SetComponentEnabled<VoiceActive>(voice, true);
            SystemAPI.SetComponentEnabled<StartVoiceRequest>(voice, true);
        }

        private void ApplyVoiceParameters(Entity voice, AudioEvent evt)
        {
            ApplyGain(voice, evt.Gain);
            ApplyPlaybackSpeed(voice, evt.PlaybackSpeed);
            ResetVoiceAge(voice);
        }

        #endregion

        #region Voice Parameters

        private void ApplyGain(Entity voice, float gain)
        {
            var gains = SystemAPI.GetBuffer<OutChannelGain>(voice);
            for (int k = 0; k < gains.Length; ++k)
            {
                gains[k] = new OutChannelGain { Value = gain };
            }
        }

        private void ApplyPlaybackSpeed(Entity voice, float playbackSpeed)
        {
            SystemAPI.SetComponent(voice, new OutPlaybackSpeed { Value = playbackSpeed });
        }

        private readonly void ResetVoiceAge(Entity voice)
        {
            var voiceActive = SystemAPI.GetComponent<VoiceActive>(voice);
            voiceActive.Age = 0;
            SystemAPI.SetComponent(voice, voiceActive);
        }

        #endregion

        #region Spatialization

        private void ApplySpatializationSettings(Entity voice, AudioEvent evt)
        {
            // TODO: Query Per Archetype. This branching is technically not needed. We can use the archetype to determine behavior beforehand.
            if (SystemAPI.HasComponent<VoiceFollowsEntity>(voice))
            {
                // Attached 3D
                SystemAPI.SetComponent(voice, new VoiceFollowsEntity { Target = evt.AttachTo });
                SystemAPI.SetComponent(voice, new VoicePositionOffset { Value = evt.Position });
            }
            else if (SystemAPI.HasComponent<LocalTransform>(voice))
            {
                // World 3D
                var transform = SystemAPI.GetComponent<LocalTransform>(voice);
                transform.Position = evt.Position;
                SystemAPI.SetComponent(voice, transform);
            }
            //else
            //{
            //    2D
            //}
        }

        #endregion
    }
}
