using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(AudioVoiceUpdateGroup))]
    [BurstCompile]
    public partial struct RandomPlaybackPositionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            uint seed = (uint)(SystemAPI.Time.ElapsedTime * 10000.0) + 9012;
            var random = new Random(seed);

            foreach (var (randomPosition, outPosition, entity) in SystemAPI.Query<RefRW<RandomPlaybackPositionMod>, RefRW<OutPlaybackStartPosition>>()
                         .WithEntityAccess()
                         .WithAll<VoiceRandomPlaybackPositionRange, StartVoiceRequest>())
            {
                var range = state.EntityManager.GetSharedComponent<VoiceRandomPlaybackPositionRange>(entity);

                float t = random.NextFloat();
                int min = range.Min;
                int max = range.Max;

                int randomPos = (int)math.lerp(min, max, t);
                randomPosition.ValueRW.Result = randomPos;
                outPosition.ValueRW.Value = randomPos;

                //UnityEngine.Debug.Log("RandomPlaybackPositionSystem: " + entity + " " + randomPos);
            }
        }
    }
}
