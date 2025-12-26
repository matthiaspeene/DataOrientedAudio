using Unity.Entities;

namespace DataOrientedAudio.Voice.Runtime
{
    #region Voice Commands

    public enum VoiceCommandType : byte
    {
        SetGain,
        SetActive,
        SetPlaybackSpeed
    }

    public struct VoiceCommand
    {
        public VoiceCommandType Type;
        public int ArchetypeIndex;
        public int LocalVoiceIndex;
        public int ChannelIndex; // To support multi-channel values. Set to -1 for shared values.
        public float Value;   // 0/1 for active
        public int PlaybackPosition; // In samples
    }

    #endregion

    #region Voice Identification

    public struct VoiceTypeID : ISharedComponentData
    {
        public int Value;
    }

    #endregion

    #region Voice Blob Reference

    /// <summary>
    /// Component that holds a reference to the VoiceBlob asset.
    /// All voices of the same type share the same blob asset.
    /// </summary>
    public struct VoiceBlobReference : IComponentData
    {
        public BlobAssetReference<VoiceBlob> Value;
    }

    #endregion
}
