using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;
using DataOrientedAudio.Events.Runtime;

namespace DataOrientedAudio.Events.Runtime.Systems
{
    [BurstCompile]
    public partial struct RandomAudioPlayerSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (player, eventBuffer, localTransform, entity) in SystemAPI.Query<RefRW<RandomAudioPlayer>, DynamicBuffer<AudioEvent>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                player.ValueRW.Timer -= dt;

                if (player.ValueRW.Timer <= 0)
                {
                    // Trigger Audio Event
                    eventBuffer.Add(new AudioEvent
                    {
                        VoiceTypeHash = player.ValueRO.VoiceTypeHash,
                        Position = localTransform.ValueRO.Position,
                        AttachTo = Entity.Null
                    });

                    //UnityEngine.Debug.Log($"RandomAudioPlayerSystem: Triggered Audio Event for {entity}");

                    // Reset Timer
                    float newInterval = player.ValueRW.Random.NextFloat(player.ValueRO.MinInterval, player.ValueRO.MaxInterval);
                    player.ValueRW.Timer = newInterval;
                }
            }
        }
    }
}
