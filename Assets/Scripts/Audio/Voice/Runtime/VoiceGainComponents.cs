using Unity.Entities;
using DataOrientedAudio.Common.Runtime;

namespace DataOrientedAudio.Voice.Runtime
{
    public struct OutChannelGain : IBufferElementData
    {
        public float Value;
    }

    // Random gain applied once when the voice is spawned.
    public struct RandomGainMod : IComponentData
    {
        public RandomRange Range;
        public float Result; // Actual random gain chosen within Range.
    }

    // Gain coming from a mix group / bus.
    public struct MixGainMod : IComponentData
    {
        public int BusIndex; // TBA: for future use.
        public float Value;
    }

    /* TBA
    // Example of future modulator hookup.
    public struct GainModulatorMod : IComponentData
    {
        public Entity Modulator;
        public float Amount;
        public float Value;
    }
    */
}
