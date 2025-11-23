using Unity.Entities;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using DataOrientedAudio.Common;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    #region System

    /// <summary>
    /// Updates voice positions to follow target entities with optional offset.
    /// </summary>
    [UpdateInGroup(typeof(AudioVoiceUpdateGroup))]
    [BurstCompile]
    public partial struct VoicePositioningSystem : ISystem
    {
        [BurstCompile]
        public readonly void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VoiceFollowsEntity>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new VoicePositioningJob
            {
                TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true)
            }.ScheduleParallel();
        }
    }

    #endregion

    #region Job

    /// <summary>
    /// Updates voice transforms to follow target entities.
    /// </summary>
    [BurstCompile]
    public partial struct VoicePositioningJob : IJobEntity
    {
        #region Variables

        [ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        #endregion

        #region Voice Positioning

        /// <summary>
        /// SIMD-optimized: Uses float3 vector operations.
        /// </summary>
        private void Execute(
            ref LocalTransform transform,
            in VoiceFollowsEntity follow,
            in VoicePositionOffset offset)
        {
            if (TransformLookup.TryGetComponent(follow.Target, out LocalTransform targetTransform))
            {
                transform.Position = targetTransform.Position + offset.Value;
                transform.Rotation = targetTransform.Rotation;
            }
        }

        #endregion
    }

    #endregion
}
