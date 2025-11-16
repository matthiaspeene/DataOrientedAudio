using Unity.Entities;

namespace DataOrientedAudio.Core.Runtime
{
    // ECS singleton for global audio output configuration.
    public struct AudioOutputConfig : IComponentData
    {
        public int ChannelCount;
        // public int SampleRate;
        // public int BufferSize;
    }
}
