using Unity.Entities;
using Unity.Mathematics;
using DataOrientedAudio.Common;

namespace DataOrientedAudio.Voice.Runtime
{
    #region Gain Components

    public struct OutChannelGain : IBufferElementData
    {
        public float Value;
    }

    // Random gain applied once when the voice is spawned.
    public struct RandomGainMod : IComponentData
    {
        public float Result; // Actual random gain chosen within Range.
    }

    // Gain coming from a mix group / bus.
    public struct MixGainMod : IComponentData
    {
        public int BusIndex; // TODO:: for future use.
        public float Value;
    }

    /* TODO:
    // Example of future modulator hookup.
    public struct GainModulatorMod : IComponentData
    {
        public Entity Modulator;
        public float Amount;
        public float Value;
    }
    */

    #endregion

    #region Identity Components

    /// <summary>
    /// Baked component: The index of this voice within its archetype (0 to VoiceCount-1).
    /// </summary>
    public struct VoiceLocalIndex : IComponentData
    {
        public int Value;
    }

    /// <summary>
    /// Runtime component: The index of the archetype this voice belongs to.
    /// Assigned by the AudioTopologySystem.
    /// </summary>
    public struct VoiceArchetypeIndex : IComponentData
    {
        public int Value;
    }

    #endregion

    #region Playback Speed Components

    // Final playback speed factor used in DSP.
    public struct OutPlaybackSpeed : IComponentData
    {
        public float Value;
    }

    public struct RandomPlaybackSpeedMod : IComponentData
    {
        public float Result; // Raw playback speed factor.
    }

    #endregion

    #region Spatialization Components

    // Data: If enabled, this voice follows a target entity.
    public struct VoiceFollowsEntity : IComponentData, IEnableableComponent
    {
        public Entity Target;
    }

    // Data: Optional offset. Can be used with or without following.
    public struct VoicePositionOffset : IComponentData
    {
        public float3 Value;
    }

    // Optional: link a voice back to its logical owner / emitter.
    public struct VoiceOwner : IComponentData
    {
        public Entity Emitter;
    }

    public struct SpatializationChannelGains : IBufferElementData
    {
        public float Value;
    }

    #endregion

    #region State Components

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

    #endregion

    #region Shared Range Components

    public struct VoiceRandomGainRange : ISharedComponentData
    {
        public float Min;
        public float Max;
    }

    public struct VoiceRandomPlaybackSpeedRange : ISharedComponentData
    {
        public float Min;
        public float Max;
    }

    #endregion
}
