
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
        private NativeHashMap<int, NativeList<Entity>> _freeVoices; // VoiceTypeID -> List of free voices
        private bool _isInitialized;

        public void OnCreate(ref SystemState state)
        {
            _freeVoices = new NativeHashMap<int, NativeList<Entity>>(16, Allocator.Persistent);
            _isInitialized = false;

            // Wait for voices to be loaded
            state.RequireForUpdate<VoiceActive>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_freeVoices.IsCreated)
            {
                foreach (var kvp in _freeVoices)
                {
                    kvp.Value.Dispose();
                }
                _freeVoices.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            if (EcsAudioBridge.IsShuttingDown)
                return;

            if (!_isInitialized)
            {
                InitializeFreeVoices(ref state);
                _isInitialized = true;
            }

            ProcessReclaimedVoices(ref state);

            foreach (var (emitter, eventBuffer) in SystemAPI.Query<RefRW<AudioEventEmitter>, DynamicBuffer<AudioEvent>>())
            {
                if (eventBuffer.IsEmpty)
                    continue;

                ProcessAudioEvents(ref state, eventBuffer);
                eventBuffer.Clear();
            }
        }

        private void InitializeFreeVoices(ref SystemState state)
        {
            var query = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<VoiceActive>(),
                    ComponentType.ReadOnly<VoiceTypeID>()
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            var entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var typeId = state.EntityManager.GetSharedComponent<VoiceTypeID>(entity).Value;

                if (!_freeVoices.ContainsKey(typeId))
                {
                    _freeVoices.Add(typeId, new NativeList<Entity>(Allocator.Persistent));
                }

                _freeVoices[typeId].Add(entity);
            }

            entities.Dispose();
        }


        private void ProcessAudioEvents(ref SystemState state, DynamicBuffer<AudioEvent> eventBuffer)
        {
            // UnityEngine.Debug.Log($"VoiceAllocationSystem: Processing {eventBuffer.Length} Audio Events");
            for (int i = 0; i < eventBuffer.Length; i++)
            {
                AllocateVoice(ref state, eventBuffer[i]);
            }
        }

        private void ProcessReclaimedVoices(ref SystemState state)
        {
            var reclaimQueue = EcsAudioBridge.GetReclaimQueue();

            while (reclaimQueue.TryDequeue(out Entity entity))
            {
                if (!state.EntityManager.Exists(entity)) continue;

                if (state.EntityManager.HasComponent<VoiceTypeID>(entity))
                {
                    int typeId = state.EntityManager.GetSharedComponent<VoiceTypeID>(entity).Value;

                    if (_freeVoices.ContainsKey(typeId))
                    {
                        _freeVoices[typeId].Add(entity);
                    }
                }
            }
        }

        private void AllocateVoice(ref SystemState state, AudioEvent evt)
        {
            int hash = evt.VoiceTypeHash;

            if (!_freeVoices.ContainsKey(hash))
            {
                UnityEngine.Debug.LogWarning($"VoiceAllocationSystem: No voice pool found for hash {hash}");
                return;
            }

            NativeList<Entity> pool = _freeVoices[hash];

            if (pool.Length == 0)
            {
                UnityEngine.Debug.LogWarning($"VoiceAllocationSystem: No free voices available for hash {hash}");
                return;
            }

            // Pop the last entity (efficient)
            int lastIndex = pool.Length - 1;
            Entity selectedVoice = pool[lastIndex];
            pool.RemoveAtSwapBack(lastIndex);

            // UnityEngine.Debug.Log($"VoiceAllocationSystem: Allocated Voice {selectedVoice} for hash {hash}");

            if (selectedVoice != Entity.Null)
            {
                ActivateVoice(ref state, selectedVoice);
                ResetVoiceAge(ref state, selectedVoice);
                ApplySpatializationSettings(ref state, selectedVoice, evt);
            }
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
