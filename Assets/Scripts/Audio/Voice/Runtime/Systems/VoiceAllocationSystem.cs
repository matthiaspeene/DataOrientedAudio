using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using DataOrientedAudio.Events.Runtime;
using DataOrientedAudio.Voice.Runtime;
using DataOrientedAudio.Common.Runtime;
using Unity.Transforms;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct VoiceAllocationSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // Query for all emitters that have events
            foreach (var (emitter, eventBuffer) in SystemAPI.Query<RefRW<AudioEventEmitter>, DynamicBuffer<AudioEvent>>())
            {
                if (eventBuffer.IsEmpty)
                    continue;

                for (int i = 0; i < eventBuffer.Length; i++)
                {
                    AudioEvent evt = eventBuffer[i];
                    AllocateVoice(ref state, evt);
                }

                eventBuffer.Clear();
            }
        }

        private void AllocateVoice(ref SystemState state, AudioEvent evt)
        {
            // Create a query for inactive voices of the requested type
            var query = state.GetEntityQuery(
                ComponentType.ReadWrite<VoiceActive>(),
                ComponentType.ReadWrite<StartVoiceRequest>(),
                ComponentType.ReadWrite<OutChannelGain>(),
                ComponentType.ReadWrite<OutPlaybackSpeed>(),
                ComponentType.ReadOnly<VoiceTypeID>()
            );

            // Filter by the specific VoiceTypeID
            query.SetSharedComponentFilter(new VoiceTypeID { Value = evt.VoiceTypeHash });

            // Find candidates that are NOT active
            // Note: We can't easily filter by "Enabled=false" in the query setup with SharedFilters efficiently in a loop 
            // without creating new queries or using Enableable components logic.
            // A better approach for high performance:
            // Maintain a NativeList of free voices per TypeID? 
            // For now, let's iterate candidates.

            var candidates = query.ToEntityArray(Allocator.Temp);
            Entity selectedVoice = Entity.Null;

            foreach (var candidate in candidates)
            {
                if (!SystemAPI.IsComponentEnabled<VoiceActive>(candidate))
                {
                    selectedVoice = candidate;
                    break;
                }
            }

            if (selectedVoice != Entity.Null)
            {
                // Activate
                SystemAPI.SetComponentEnabled<VoiceActive>(selectedVoice, true);
                SystemAPI.SetComponentEnabled<StartVoiceRequest>(selectedVoice, true);

                // Apply Event parameters
                // Gain
                var gains = SystemAPI.GetBuffer<OutChannelGain>(selectedVoice);
                for (int k = 0; k < gains.Length; ++k)
                {
                    gains[k] = new OutChannelGain { Value = evt.Gain };
                }

                // PlaybackSpeed
                SystemAPI.SetComponent(selectedVoice, new OutPlaybackSpeed { Value = evt.PlaybackSpeed });

                // Reset Age
                var voiceActive = SystemAPI.GetComponent<VoiceActive>(selectedVoice);
                voiceActive.Age = 0;
                SystemAPI.SetComponent(selectedVoice, voiceActive);

                // Spatialization
                if (evt.Space == AudioEventSpace.Stereo2D)
                {
                    SystemAPI.SetComponentEnabled<VoiceIsSpatial>(selectedVoice, false);
                    SystemAPI.SetComponentEnabled<VoiceFollowsEntity>(selectedVoice, false);
                }
                else if (evt.Space == AudioEventSpace.World3D)
                {
                    SystemAPI.SetComponentEnabled<VoiceIsSpatial>(selectedVoice, true);
                    SystemAPI.SetComponentEnabled<VoiceFollowsEntity>(selectedVoice, false);

                    // Set position directly
                    var transform = SystemAPI.GetComponent<LocalTransform>(selectedVoice);
                    transform.Position = evt.Position;
                    SystemAPI.SetComponent(selectedVoice, transform);
                }
                else if (evt.Space == AudioEventSpace.Attached3D)
                {
                    SystemAPI.SetComponentEnabled<VoiceIsSpatial>(selectedVoice, true);
                    SystemAPI.SetComponentEnabled<VoiceFollowsEntity>(selectedVoice, true);
                    SystemAPI.SetComponent(selectedVoice, new VoiceFollowsEntity { Target = evt.AttachTo });

                    // Set offset
                    SystemAPI.SetComponent(selectedVoice, new VoicePositionOffset { Value = evt.Position });
                }
            }

            candidates.Dispose();
        }
    }
}
