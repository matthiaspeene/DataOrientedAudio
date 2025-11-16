using Unity.Entities;

namespace DataOrientedAudio.Voice.Runtime
{
    // Voice is logically active; used as a toggled-on component.
    public struct VoiceActive : IComponentData, IEnableableComponent
    {
        public float Age;
    }

    // Flag to start a voice.
    public struct StartVoiceRequest : IComponentData, IEnableableComponent
    {
        // Add fade/delay data later if you like.
        // public float Delay;
        // public float FadeInTime;
    }

    // Flag to stop a voice.
    public struct StopVoiceRequest : IComponentData, IEnableableComponent
    {
        // public float FadeOutTime;
    }
}
