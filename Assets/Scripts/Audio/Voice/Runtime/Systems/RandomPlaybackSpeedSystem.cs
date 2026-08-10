using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(AudioVoiceUpdateGroup))]
    [BurstCompile]
    public partial struct RandomPlaybackSpeedSystem : ISystem
    {
        private Random _random;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Keep one advancing random stream. 
            _random = Random.CreateFromIndex(5678u);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (randomSpeed, entity) in SystemAPI.Query<RefRW<RandomPlaybackSpeedMod>>()
                         .WithEntityAccess()
                         .WithAll<VoiceRandomPlaybackSpeedRange, StartVoiceRequest>())
            {
                var range = state.EntityManager.GetSharedComponent<VoiceRandomPlaybackSpeedRange>(entity);

                float t = _random.NextFloat();
                float min = range.Min;
                float max = range.Max;

                randomSpeed.ValueRW.Result = math.lerp(min, max, t);
            }
        }
    }
}

//TODO: *Optional improvements*
/*
Unify random systems
Gain and playback speed logic are basically identical. We could:

Have one RandomizeVoiceParamsSystem that writes gain, speed, pan, filter, etc. using the same RNG stream per voice.

Or use a shared helper that takes min/max from the blob and returns random values.

Parallelize with jobs
When/if your voice count gets big, I’d refactor to something like:

IJobEntity or IJobChunk scheduled from the system.

NativeArray<Random> of length equal to the batch or worker count.

Each job index uses its own Random instance (classic DOTS pattern).

Deterministic modes
If you ever want “same scene = same randomization” for debugging, the global singleton seed and hashed system seeds make it trivial: just give the global RNG a fixed seed when running in a “deterministic debug” mode.
*/
