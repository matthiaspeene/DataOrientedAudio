using Unity.Entities;
using Unity.Burst;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct VoiceRepeatSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            double currentTime = SystemAPI.Time.ElapsedTime;

            foreach (var (triggerRepeat, voiceActive, startRequest, stopRequest) in
                     SystemAPI.Query<RefRW<TriggerRepeat>, EnabledRefRW<VoiceActive>, EnabledRefRW<StartVoiceRequest>, EnabledRefRW<StopVoiceRequest>>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                if (triggerRepeat.ValueRO.IsWaitingForRepeat)
                {
                    if (currentTime >= triggerRepeat.ValueRO.NextRepetitionTime)
                    {
                        // Time to play!
                        triggerRepeat.ValueRW.IsWaitingForRepeat = false;

                        // Re-enable and start
                        voiceActive.ValueRW = true;
                        startRequest.ValueRW = true;

                        // Ensure stop request is off
                        stopRequest.ValueRW = false; // Should be already, but just in case
                    }
                }
            }
        }
    }
}
