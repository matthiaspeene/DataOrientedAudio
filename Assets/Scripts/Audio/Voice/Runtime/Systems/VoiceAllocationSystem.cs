
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
    [UpdateInGroup(typeof(AudioVoiceLifecycleGroup))]
    [BurstCompile]
    public partial struct VoiceAllocationSystem : ISystem
    {
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
            Entity selectedVoice = FindInactiveVoice(ref state, candidates);

            if (selectedVoice != Entity.Null)
            {
                ActivateVoice(ref state, selectedVoice);
                ResetVoiceAge(ref state, selectedVoice);
                ApplySpatializationSettings(ref state, selectedVoice, evt);
            }

            candidates.Dispose();
        }

        private EntityQuery CreateVoiceQuery(ref SystemState state, int voiceTypeHash)
        {
            var query = state.GetEntityQuery(
                ComponentType.ReadWrite<VoiceActive>(),
                ComponentType.ReadWrite<StartVoiceRequest>(),
                ComponentType.ReadOnly<VoiceTypeID>()
            );

            query.SetSharedComponentFilter(new VoiceTypeID { Value = voiceTypeHash });
            return query;
        }

        private Entity FindInactiveVoice(ref SystemState state, NativeArray<Entity> candidates)
        {
            // TODO: Maintain a NativeList of free voices per TypeID for better performance
            foreach (var candidate in candidates)
            {
                if (!state.EntityManager.IsComponentEnabled<VoiceActive>(candidate))
                {
                    return candidate;
                }
            }

            // TODO: Handle voice stealing if no inactive voices are found
            return Entity.Null;
        }

        private readonly void ActivateVoice(ref SystemState state, Entity voice)
        {
            state.EntityManager.SetComponentEnabled<VoiceActive>(voice, true);
            state.EntityManager.SetComponentEnabled<StartVoiceRequest>(voice, true);
        }

        private readonly void ResetVoiceAge(ref SystemState state, Entity voice)
        {
            var voiceActive = state.EntityManager.GetComponentData<VoiceActive>(voice);
            voiceActive.Age = 0;
            state.EntityManager.SetComponentData(voice, voiceActive);
        }

        private readonly void ApplySpatializationSettings(ref SystemState state, Entity voice, AudioEvent evt)
        {
            // TODO: Query Per Archetype. This branching is technically not needed. We can use the archetype to determine behavior beforehand.
            if (state.EntityManager.HasComponent<VoiceFollowsEntity>(voice))
            {
                // Attached 3D
                state.EntityManager.SetComponentData(voice, new VoiceFollowsEntity { Target = evt.AttachTo });
                state.EntityManager.SetComponentData(voice, new VoicePositionOffset { Value = evt.Position });
            }
            else if (state.EntityManager.HasComponent<LocalTransform>(voice))
            {
                // World 3D
                var transform = state.EntityManager.GetComponentData<LocalTransform>(voice);
                transform.Position = evt.Position;
                state.EntityManager.SetComponentData(voice, transform);
            }
        }
    }
}