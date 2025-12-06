using Unity.Entities;
using Unity.Collections;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(AudioVoiceCommandGroup))]
    public partial class AudioVoiceCommandSystem : SystemBase
    {
        #region Lifecycle

        protected override void OnCreate()
        {
            EcsAudioBridge.Initialize();
        }

        protected override void OnDestroy()
        {
            EcsAudioBridge.Shutdown();
        }

        protected override void OnUpdate()
        {
            var commands = EcsAudioBridge.GetCommandList();
            commands.Clear();

            ProcessGainChanges(commands);
            ProcessPlaybackSpeedChanges(commands);
            ProcessStartRequests(commands);
            ProcessStopRequests(commands);
        }

        #endregion

        #region Command Processing

        private void ProcessGainChanges(NativeList<VoiceCommand> commands)
        {
            foreach (var (archIdx, localIdx, gainBuffer) in
                     SystemAPI.Query<RefRO<VoiceArchetypeIndex>, RefRO<VoiceLocalIndex>, DynamicBuffer<OutChannelGain>>()
                         .WithChangeFilter<OutChannelGain>())
            {
                for (int i = 0; i < gainBuffer.Length; i++)
                {
                    commands.Add(new VoiceCommand
                    {
                        Type = VoiceCommandType.SetGain,
                        ArchetypeIndex = archIdx.ValueRO.Value,
                        LocalVoiceIndex = localIdx.ValueRO.Value,
                        ChannelIndex = i,
                        Value = gainBuffer[i].Value
                    });
                }
            }
        }

        private void ProcessPlaybackSpeedChanges(NativeList<VoiceCommand> commands)
        {
            foreach (var (archIdx, localIdx, speed) in
                     SystemAPI.Query<RefRO<VoiceArchetypeIndex>, RefRO<VoiceLocalIndex>, RefRO<OutPlaybackSpeed>>()
                         .WithChangeFilter<OutPlaybackSpeed>())
            {
                commands.Add(new VoiceCommand
                {
                    Type = VoiceCommandType.SetPlaybackSpeed,
                    ArchetypeIndex = archIdx.ValueRO.Value,
                    LocalVoiceIndex = localIdx.ValueRO.Value,
                    Value = speed.ValueRO.Value
                });
            }
        }

        private void ProcessStartRequests(NativeList<VoiceCommand> commands)
        {
            foreach (var (archIdx, localIdx, startEnabled) in
                     SystemAPI.Query<RefRO<VoiceArchetypeIndex>, RefRO<VoiceLocalIndex>, EnabledRefRW<StartVoiceRequest>>())
            {
                commands.Add(new VoiceCommand
                {
                    Type = VoiceCommandType.SetActive,
                    ArchetypeIndex = archIdx.ValueRO.Value,
                    LocalVoiceIndex = localIdx.ValueRO.Value,
                    Value = 1.0f
                });

                startEnabled.ValueRW = false;
            }
        }

        private void ProcessStopRequests(NativeList<VoiceCommand> commands)
        {
            foreach (var (archIdx, localIdx, stopEnabled) in
                     SystemAPI.Query<RefRO<VoiceArchetypeIndex>, RefRO<VoiceLocalIndex>, EnabledRefRW<StopVoiceRequest>>())
            {
                commands.Add(new VoiceCommand
                {
                    Type = VoiceCommandType.SetActive,
                    ArchetypeIndex = archIdx.ValueRO.Value,
                    LocalVoiceIndex = localIdx.ValueRO.Value,
                    Value = 0.0f
                });

                stopEnabled.ValueRW = false;
            }
        }
        #endregion
    }
}
