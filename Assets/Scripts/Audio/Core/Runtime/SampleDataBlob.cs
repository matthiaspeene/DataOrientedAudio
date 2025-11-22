using Unity.Entities;

namespace DataOrientedAudio.Core.Runtime
{
    // Raw sample data blob
    public struct SampleDataBlob : IBufferElementData
    {
        public BlobArray<float> Samples; // Interleaved for multiple channels
        public int ChannelCount;
        public int SampleRate;
        public int SampleCount;
    }
}