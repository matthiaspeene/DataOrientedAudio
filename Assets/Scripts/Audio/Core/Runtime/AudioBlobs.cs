using Unity.Entities;

namespace DataOrientedAudio.Core.Runtime
{
    // Raw sample data blob
    public readonly struct SampleDataBlob
    {
        public readonly BlobArray<float> Samples; // Interleaved for multiple channels
        public readonly int ChannelCount;
        public readonly int SampleRate;
        public readonly int SampleCount;
    }

    // Voice default data – can reference SampleDataBlob or be used as a config container.
    // (You can refactor this later depending on how you want to build blobs.)
    public struct VoiceDataBlob
    {
        public BlobArray<BlobAssetReference<SampleDataBlob>> Clips; // shared reference
        public float DefaultGain;
        public float DefaultPlaybackSpeed;
    }
}
