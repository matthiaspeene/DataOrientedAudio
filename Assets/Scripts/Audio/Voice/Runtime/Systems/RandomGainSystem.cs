using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(AudioVoiceUpdateGroup))]
    [BurstCompile]
    public partial struct RandomGainSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            uint seed = (uint)(SystemAPI.Time.ElapsedTime * 10000.0) + 1;
            var random = new Random(seed);

            foreach (var (randomGain, entity) in SystemAPI.Query<RefRW<RandomGainMod>>()
                         .WithEntityAccess()
                         .WithAll<VoiceRandomGainRange, StartVoiceRequest>())
            {
                var range = state.EntityManager.GetSharedComponent<VoiceRandomGainRange>(entity);

                // Advance random state for each entity to avoid identical values in the same frame
                float t = random.NextFloat();
                float min = range.Min;
                float max = range.Max;

                randomGain.ValueRW.Result = math.lerp(min, max, t);
            }
        }
    }
}
