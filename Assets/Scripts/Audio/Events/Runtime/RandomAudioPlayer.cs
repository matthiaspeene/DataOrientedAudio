using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.Events.Runtime
{
    public struct RandomAudioPlayer : IComponentData
    {
        public float Timer;
        public float MinInterval;
        public float MaxInterval;
        public int VoiceTypeHash;
        
        // We use a small seed to keep the random state per entity
        public Random Random;
    }
}
