using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(AudioVoiceCommandGroup))]
    public partial class AudioVoiceCommandSystem : SystemBase
    {
        private NativeList<VoiceCommand> _commands;

        // We need to track previous state to detect changes.
        // We can use IJobChunk or Entities.ForEach with ChangeFilter?
        // ChangeFilter works for components.

        // For Gain, we have OutChannelGain (buffer) or MixGainMod (component).
        // The RootOutput expects a single float Gain. Let's assume it's the final calculated gain.
        // But we don't have a "FinalGain" component yet. 
        // Let's assume we are tracking `MixGainMod` for now, or we should add a `FinalGain` component 
        // that the DSP system writes to?

        // The user prompt said: "Detect parameter changes vs previous frame (gain, active, etc.)"
        // Let's assume we track `MixGainMod` and `VoiceActive`.

        // To detect changes, we can use `IJobEntity` with `ChangeFilter`.
        // But `ChangeFilter` only tells us *that* it changed, not the previous value.
        // However, for the command buffer, we just need to send the *new* value if it changed.
        // The receiver (DSP) will update its state.

        protected override void OnCreate()
        {
            // No local list needed, we use the Bridge's write buffer
            EcsAudioBridge.Initialize();
        }

        protected override void OnDestroy()
        {
            EcsAudioBridge.Shutdown();
        }

        protected override void OnUpdate()
        {
            // Get the shared command list
            var commands = EcsAudioBridge.GetCommandList();
            commands.Clear();

            // Detect Gain Changes
            // Detect Gain Changes
            foreach (var (archIdx, localIdx, gain) in SystemAPI.Query<RefRO<VoiceArchetypeIndex>, RefRO<VoiceLocalIndex>, RefRO<MixGainMod>>()
                         .WithChangeFilter<MixGainMod>())
            {
                commands.Add(new VoiceCommand
                {
                    Type = VoiceCommandType.SetGain,
                    ArchetypeIndex = archIdx.ValueRO.Value,
                    LocalVoiceIndex = localIdx.ValueRO.Value,
                    Value = gain.ValueRO.Value
                });
            }

            // 2. Active State Changes
            // 2. Active State Changes
            foreach (var (archIdx, localIdx, active) in SystemAPI.Query<RefRO<VoiceArchetypeIndex>, RefRO<VoiceLocalIndex>, RefRO<VoiceActive>>()
                         .WithChangeFilter<VoiceActive>())
            {
                // Placeholder for active state logic
            }

            // Complete dependency so the list is ready for main thread
            this.Dependency.Complete();
        }

        public void ClearCommands()
        {
            // Deprecated
        }
    }
}
