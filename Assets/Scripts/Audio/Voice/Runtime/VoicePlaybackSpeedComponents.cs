using Unity.Entities;
using DataOrientedAudio.Common.Runtime;

namespace DataOrientedAudio.Voice.Runtime
{
    // Final playback speed factor used in DSP.
    public struct OutPlaybackSpeed : IComponentData
    {
        public float Value;
    }

    public struct RandomPlaybackSpeedMod : IComponentData
    {
        public RandomRange Range;
        public float Result; // Raw playback speed factor.
    }
}
