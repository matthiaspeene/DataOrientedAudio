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

                    // Disable this trigger logic so it doesn't fire again for this entity
                    // The entity remains alive for movement, but the audio triggering is handed off.
                    // If we want it to trigger exactly once, we can just set NextTriggerTime to infinity or remove the component if possible.
                    // For now, setting time to infinity effectively stops it.
                    voice.ValueRW.NextTriggerTime = float.MaxValue;
                }
            }
        }
    }
}