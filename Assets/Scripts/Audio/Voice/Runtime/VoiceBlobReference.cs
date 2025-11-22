using Unity.Entities;

namespace DataOrientedAudio.Voice.Runtime
{
    /// <summary>
    /// Component that holds a reference to the VoiceBlob asset.
    /// All voices of the same type share the same blob asset.
    /// </summary>
    public struct VoiceBlobReference : IComponentData
    {
        public BlobAssetReference<VoiceBlob> Value;
    }
}
