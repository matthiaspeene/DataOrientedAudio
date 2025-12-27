using Unity.Entities;
using Unity.Burst;
using Unity.Transforms;
using Unity.Mathematics;

namespace DataOrientedAudio.StressTest.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct StressTestMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VoiceStressTest>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Get singleton bounds
            float3 bounds = float3.zero;
            foreach (var stressTest in SystemAPI.Query<RefRO<VoiceStressTest>>())
            {
                bounds = stressTest.ValueRO.MovementBounds;
                break; // Only one singleton
            }

            float dt = SystemAPI.Time.DeltaTime;

            // Process all moving voices
            foreach (var (transform, movement) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<StressTestMovement>>())
            {
                float3 position = transform.ValueRO.Position;
                float3 direction = movement.ValueRO.Direction;
                float speed = movement.ValueRO.Speed;

                // Move
                position += direction * speed * dt;

                // Bounce on X
                if (position.x > bounds.x)
                {
                    position.x = bounds.x;
                    direction.x = -direction.x;
                }
                else if (position.x < -bounds.x)
                {
                    position.x = -bounds.x;
                    direction.x = -direction.x;
                }

                // Bounce on Y
                if (position.y > bounds.y)
                {
                    position.y = bounds.y;
                    direction.y = -direction.y;
                }
                else if (position.y < -bounds.y)
                {
                    position.y = -bounds.y;
                    direction.y = -direction.y;
                }

                // Bounce on Z
                if (position.z > bounds.z)
                {
                    position.z = bounds.z;
                    direction.z = -direction.z;
                }
                else if (position.z < -bounds.z)
                {
                    position.z = -bounds.z;
                    direction.z = -direction.z;
                }

                // Update transform and direction
                transform.ValueRW.Position = position;
                movement.ValueRW.Direction = direction;
            }
        }
    }
}
