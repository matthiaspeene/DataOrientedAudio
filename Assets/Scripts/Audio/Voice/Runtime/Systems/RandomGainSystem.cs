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

            foreach (var (randomGain, blobRef) in SystemAPI.Query<RefRW<RandomGainMod>, RefRO<VoiceBlobReference>>()
                         .WithAll<StartVoiceRequest>())
            {
                ref var blob = ref blobRef.ValueRO.Value.Value;

                // Advance random state for each entity to avoid identical values in the same frame
                float t = random.NextFloat();
                float min = blob.GainMin;
                float max = blob.GainMax;

                randomGain.ValueRW.Result = math.lerp(min, max, t);
            }
        }
    }
}
