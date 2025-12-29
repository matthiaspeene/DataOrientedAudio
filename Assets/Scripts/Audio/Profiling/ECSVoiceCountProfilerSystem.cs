using Unity.Entities;
using Unity.Profiling;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Profiling
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ECSVoiceCountProfilerSystem : SystemBase
    {
        private EntityQuery _voiceQuery;

        private static readonly ProfilerCounter<int> ECSVoiceCount =
            new ProfilerCounter<int>(
                ProfilerCategory.Scripts,
                "ECS Voice Count",
                ProfilerMarkerDataUnit.Count
            );


        protected override void OnCreate()
        {
            _voiceQuery = GetEntityQuery(
                ComponentType.ReadOnly<VoiceActive>()
            );
        }

        protected override void OnUpdate()
        {
            int count = _voiceQuery.CalculateEntityCount();
            ECSVoiceCount.Sample(count);
        }
    }
}
