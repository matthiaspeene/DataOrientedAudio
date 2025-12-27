
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    /// <summary>
    /// Calculates distance attenuation for active voices relative to the audio listener.
    /// Uses a simple linear falloff based on the baked Min/Max distance settings.
    /// </summary>
    [UpdateInGroup(typeof(AudioVoiceUpdateGroup))]
    [BurstCompile]
    public partial struct DistanceAttenuationSystem : ISystem
    {
        private EntityQuery _query;

        public void OnCreate(ref SystemState state)
        {
            // We need an AudioListener to calculate distance
            state.RequireForUpdate<AudioListener>();

            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<VoiceActive>()
                .WithAll<DistanceAttenuationSettings>()
                .WithAll<DistanceAttenuationGainMod>()
                .WithAll<LocalTransform>()
                .Build(ref state);

            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<AudioListener>(out var listener))
                return;

            var job = new DistanceAttenuationJob
            {
                ListenerPosition = listener.Position,
                DistanceAttenSettingsHandle = state.GetSharedComponentTypeHandle<DistanceAttenuationSettings>(),
                DistanceAttenGainModHandle = SystemAPI.GetComponentTypeHandle<DistanceAttenuationGainMod>(false),
                LocalTransformHandle = SystemAPI.GetComponentTypeHandle<LocalTransform>(true)
            };

            state.Dependency = job.ScheduleParallel(_query, state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct DistanceAttenuationJob : IJobChunk
    {
        [ReadOnly] public float3 ListenerPosition;
        [ReadOnly] public SharedComponentTypeHandle<DistanceAttenuationSettings> DistanceAttenSettingsHandle;
        public ComponentTypeHandle<DistanceAttenuationGainMod> DistanceAttenGainModHandle;
        [ReadOnly] public ComponentTypeHandle<LocalTransform> LocalTransformHandle;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
        {
            var settings = chunk.GetSharedComponent(DistanceAttenSettingsHandle);
            var gainMods = chunk.GetNativeArray(ref DistanceAttenGainModHandle);
            var transforms = chunk.GetNativeArray(ref LocalTransformHandle);

            float range = settings.MaxDistance - settings.MinDistance;
            bool hasRange = range > math.EPSILON;

            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (enumerator.NextEntityIndex(out int i))
            {
                float distance = math.distance(transforms[i].Position, ListenerPosition);

                if (!hasRange)
                {
                    gainMods[i] = new DistanceAttenuationGainMod { Value = distance < settings.MaxDistance ? 1f : 0f };
                    continue;
                }

                float t = math.saturate((distance - settings.MinDistance) / range);
                gainMods[i] = new DistanceAttenuationGainMod { Value = 1f - t };
            }
        }
    }
}
