using Unity.Entities;
using Unity.Transforms;

namespace DataOrientedAudio.Voice.Runtime
{
    // Base audio group: runs inside Simulation and after transforms
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class AudioPipelineGroup : ComponentSystemGroup { }

    // ─────────────────────────────────────────────
    // Stage 1: Allocate & manage voice lifetime
    // ─────────────────────────────────────────────
    [UpdateInGroup(typeof(AudioPipelineGroup))]
    public partial class AudioVoiceLifecycleGroup : ComponentSystemGroup { }
    // Systems: start requests, stop requests, voice pooling, allocation.

    // ─────────────────────────────────────────────
    // Stage 2: Update per-voice dynamic parameters
    // ─────────────────────────────────────────────
    [UpdateInGroup(typeof(AudioPipelineGroup))]
    [UpdateAfter(typeof(AudioVoiceLifecycleGroup))]
    public partial class AudioVoiceUpdateGroup : ComponentSystemGroup { }
    // Systems: spatialization, doppler, envelopes, modulator updates.

    // ─────────────────────────────────────────────
    // Stage 3: Sum all modulated values → final per-voice outs
    // ─────────────────────────────────────────────
    [UpdateInGroup(typeof(AudioPipelineGroup))]
    [UpdateAfter(typeof(AudioVoiceUpdateGroup))]
    public partial class AudioVoiceFinalizationGroup : ComponentSystemGroup { }
    // Systems: accumulate all calculated parameters into GainOut, SpeedOut, etc.

    // ─────────────────────────────────────────────
    // Stage 4: Convert ECS voice state into control-side commands
    // ─────────────────────────────────────────────
    [UpdateInGroup(typeof(AudioPipelineGroup))]
    [UpdateAfter(typeof(AudioVoiceFinalizationGroup))]
    public partial class AudioVoiceCommandGroup : ComponentSystemGroup { }
    // Systems: writes VoiceCommand buffers consumed by audio Control.Update
}
