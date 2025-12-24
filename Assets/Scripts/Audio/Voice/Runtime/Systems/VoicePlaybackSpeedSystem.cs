using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    /// <summary>
    /// Calculates the final OutPlaybackSpeed for each voice.
    /// Currently just takes RandomPlaybackSpeedMod.Result or defaults to 1.0.
    /// </summary>
    [UpdateInGroup(typeof(AudioVoiceFinalizationGroup))]
    [BurstCompile]
    public partial struct VoicePlaybackSpeedSystem : ISystem
    {
        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<OutPlaybackSpeed>()
                .WithAny<RandomPlaybackSpeedMod>()
                .Build(ref state);

            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new VoicePlaybackSpeedJob
            {
                RandomSpeedLookup = SystemAPI.GetComponentLookup<RandomPlaybackSpeedMod>(true),
                LastSystemVersion = state.LastSystemVersion
            };

            state.Dependency = job.ScheduleParallel(_query, state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct VoicePlaybackSpeedJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<RandomPlaybackSpeedMod> RandomSpeedLookup;
        public uint LastSystemVersion;

        public void Execute(Entity entity, ref OutPlaybackSpeed outSpeed)
        {
            // Efficiency check: only proceed if any relevant component has changed.
            if (!RandomSpeedLookup.DidChange(entity, LastSystemVersion))
                return;

            float speed = 1.0f;

            if (RandomSpeedLookup.HasComponent(entity))
            {
                speed *= RandomSpeedLookup[entity].Result;
            }

            outSpeed.Value = speed;
        }
    }
}
