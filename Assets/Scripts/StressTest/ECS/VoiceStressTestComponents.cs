using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.StressTest
{
    /// <summary>
    /// Singleton component controlling the stress test state.
    /// </summary>
    public struct VoiceStressTest : IComponentData
    {
        public int CurrentStressStep;
        public int TotalVoicesSpawned;
        public float3 MovementBounds;
        public uint RandomSeed;
    }

    /// <summary>
    /// Configuration for one voice type in the stress test (from VoiceStressTestConfig).
    /// </summary>
    public struct VoiceStressConfig : IBufferElementData
    {
        public int VoiceTypeHash;        // Hash of VoiceData name
        public int AmountRatio;          // How many to spawn per step
        public bool Spatialized;
        public bool Moving;
        public float MinSpeed;
        public float MaxSpeed;
        public float MaxStartDelay;
    }

    /// <summary>
    /// Component for a single stress test voice entity.
    /// </summary>
    public struct StressTestVoice : IComponentData
    {
        public int VoiceTypeHash;
        public float NextTriggerTime;    // When to trigger next audio event
        public float RepeatDelayMin;     // For Repeat trigger mode
        public float RepeatDelayMax;
    }

    /// <summary>
    /// Movement data for moving voices.
    /// </summary>
    public struct StressTestMovement : IComponentData
    {
        public float3 Direction;
        public float Speed;
    }
}
