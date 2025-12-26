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

            foreach (var (voice, eventBuffer, transform) in SystemAPI.Query<RefRW<StressTestVoice>, DynamicBuffer<AudioEvent>, RefRO<LocalTransform>>())
            {
                if (currentTime >= voice.ValueRO.NextTriggerTime)
                {
                    // Add audio event
                    eventBuffer.Add(new AudioEvent
                    {
                        VoiceTypeHash = voice.ValueRO.VoiceTypeHash,
                        Position = transform.ValueRO.Position,
                        AttachTo = Entity.Null
                    });

                    // Schedule next trigger
                    var random = Unity.Mathematics.Random.CreateFromIndex((uint)(voice.ValueRO.VoiceTypeHash ^ (int)(currentTime * 1000)));
                    float repeatDelay = random.NextFloat(voice.ValueRO.RepeatDelayMin, voice.ValueRO.RepeatDelayMax);
                    voice.ValueRW.NextTriggerTime = currentTime + repeatDelay;
                }
            }
        }
    }
}
