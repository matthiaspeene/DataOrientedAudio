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

            ProcessFinishedVoices();

            ProcessGainChanges(commands);
            ProcessPlaybackSpeedChanges(commands);
            ProcessStartRequests(commands);
            ProcessStopRequests(commands);
        }

        private void ProcessFinishedVoices()
        {
            var finishedVoices = EcsAudioBridge.GetFinishedCommandList();

            if (finishedVoices.Length == 0) return;

            var reclaimQueue = EcsAudioBridge.GetReclaimQueue();

            // Iterate over all active voices to find matches
            // We use EntityQuery to filter only active voices
            foreach (var (archIdx, localIdx, entity) in
                     SystemAPI.Query<RefRO<VoiceArchetypeIndex>, RefRO<VoiceLocalIndex>>()
                         .WithEntityAccess())
            {
                for (int i = 0; i < finishedVoices.Length; i++)
                {
                    var cmd = finishedVoices[i];

                    if (cmd.ArchetypeIndex == archIdx.ValueRO.Value && cmd.LocalVoiceIndex == localIdx.ValueRO.Value)
                    {
                        // Match found
                        SystemAPI.SetComponentEnabled<VoiceActive>(entity, false);
                        SystemAPI.SetComponentEnabled<StopVoiceRequest>(entity, true);

                        // Add to reclaim queue
                        reclaimQueue.Enqueue(entity);
                    }
                }
            }

            finishedVoices.Clear();
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
