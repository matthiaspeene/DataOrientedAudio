using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using DataOrientedAudio.Common;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    #region Follow System

    [UpdateInGroup(typeof(AudioVoiceLifecycleGroup))]
    [BurstCompile]
    public partial struct VoiceFollowSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VoiceFollowsEntity>();
            state.RequireForUpdate<VoiceActive>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new VoiceFollowJob
            {
                TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                OffsetLookup = SystemAPI.GetComponentLookup<VoicePositionOffset>(true)
            };

            job.ScheduleParallel();
        }
    }

    #endregion

    #region Follow Job

    [BurstCompile]
    [WithAll(typeof(VoiceActive))]
    public partial struct VoiceFollowJob : IJobEntity
    {
        #region Lookups

        [ReadOnly]
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;

        [ReadOnly]
        public ComponentLookup<VoicePositionOffset> OffsetLookup;

        #endregion

        #region Execute

        private void Execute(
            Entity entity,
            ref LocalTransform transform,
            in VoiceFollowsEntity follow)
        {
            if (!TransformLookup.TryGetComponent(follow.Target, out var targetTransform))
                return;

            float3 position = targetTransform.Position;

            if (OffsetLookup.HasComponent(entity))
            {
                position += OffsetLookup[entity].Value;
            }

            transform.Position = position;
            transform.Rotation = targetTransform.Rotation;
        }

        #endregion
    }

    #endregion
}
