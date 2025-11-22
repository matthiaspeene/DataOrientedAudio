// Blob asset for voice data
// Contains immutable voice-level parameters and all clip data shared by all voice entities of the same type
using Unity.Entities;

namespace DataOrientedAudio.Voice.Runtime
{
    /// <summary>
    /// Individual clip data stored within the VoiceBlob.
    /// Each clip contains its own sample data and metadata.
    /// </summary>
    public struct ClipData
    {
        public BlobArray<float> Samples;  // Interleaved audio samples (e.g., L,R,L,R for stereo)
        public int ChannelCount;          // Number of audio channels (1=mono, 2=stereo, etc.)
        public int SampleRate;            // Sample rate in Hz (e.g., 44100, 48000)
        public int SampleCount;           // Number of samples per channel
    }

    /// <summary>
    /// Voice blob asset containing all immutable voice data.
    /// This entire structure is shared by all voice entities of the same type.
    /// </summary>
    public struct VoiceBlob
    {
        public BlobArray<ClipData> Clips; // Array of all clips for random selection
        public float GainMin;             // Minimum gain multiplier
        public float GainMax;             // Maximum gain multiplier
        public float PlaybackSpeedMin;    // Minimum playback speed multiplier
        public float PlaybackSpeedMax;    // Maximum playback speed multiplier

        // TBA: Add other params as they come (e.g., trigger mode, repeat delay range, etc.)
    }
}
