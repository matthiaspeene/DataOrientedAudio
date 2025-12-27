using Unity.Entities;
using Unity.Burst;
using Unity.Transforms;
using DataOrientedAudio.Events.Runtime;

namespace DataOrientedAudio.StressTest.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct StressTestVoiceTriggerSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float currentTime = (float)SystemAPI.Time.ElapsedTime;

            foreach (var (voice, eventBuffer, transform, entity) in
            SystemAPI.Query<RefRW<StressTestVoice>, DynamicBuffer<AudioEvent>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                if (currentTime >= voice.ValueRO.NextTriggerTime)

                    if (SystemAPI.HasComponent<StressTestMovement>(entity))
                    {
                        eventBuffer.Add(new AudioEvent
                        {
                            VoiceTypeHash = voice.ValueRO.VoiceTypeHash,
                            Position = transform.ValueRO.Position,
                            AttachTo = entity
                        });
                    }
                    else
                    {
                        eventBuffer.Add(new AudioEvent
                        {
                            VoiceTypeHash = voice.ValueRO.VoiceTypeHash,
                            Position = transform.ValueRO.Position,
                            AttachTo = Entity.Null
                        });
                    }

                voice.ValueRW.NextTriggerTime = float.MaxValue;
            }
        }
    }
}