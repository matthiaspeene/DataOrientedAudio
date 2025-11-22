using Unity.Entities;
using Unity.Transforms;
using Unity.Burst;
using DataOrientedAudio.Common.Runtime;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [BurstCompile]
    public partial struct VoicePositioningSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // Iterate over all active voices that have spatialization data
            // Iterate over all active voices that are following an entity
            foreach (var (transform, follow, offset) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<VoiceFollowsEntity>, RefRO<VoicePositionOffset>>()
                         .WithAll<VoiceActive>())
            {
                Entity target = follow.ValueRO.Target;

                // Check if target exists and has transform
                if (SystemAPI.HasComponent<LocalTransform>(target))
                {
                    var targetTransform = SystemAPI.GetComponent<LocalTransform>(target);

                    // Copy position and rotation
                    // Apply offset
                    transform.ValueRW.Position = targetTransform.Position + offset.ValueRO.Value;
                    transform.ValueRW.Rotation = targetTransform.Rotation;
                }
            }
        }
    }
}
