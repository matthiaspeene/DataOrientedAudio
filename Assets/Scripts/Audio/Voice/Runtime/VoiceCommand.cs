using Unity.Entities;

namespace DataOrientedAudio.Voice.Runtime
{
    public enum VoiceCommandType : byte
    {
        SetGain,
        SetActive,
        // Add more when needed (e.g., SetPitch, SetPosition)
    }

    public struct VoiceCommand
    {
        public VoiceCommandType Type;
        public int ArchetypeIndex;
        public int LocalVoiceIndex;
        public float Value;   // Gain or 0/1 for active
    }
}
