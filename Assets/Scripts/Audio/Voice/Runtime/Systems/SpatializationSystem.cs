using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    /// <summary>
    /// Calculates stereo panning gains for spatial voices based on their position relative to the audio listener.
    /// Uses equal-power panning with distance-based pan scaling.
    /// </summary>
    [UpdateInGroup(typeof(AudioVoiceUpdateGroup))]
    [BurstCompile]
    public partial struct SpatializationSystem : ISystem
    {
        // Configuration constants
        private const float MaxPanDistance = 10f;
        private const float MinDistance = 0.1f;
        
        private EntityQuery _listenerQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VoiceIsSpatial>();
            
            // Create listener query once
            _listenerQuery = SystemAPI.QueryBuilder().WithAll<AudioListenerTag, AudioListener>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Query the AudioListener singleton
            float3 listenerPosition = float3.zero;
            float3 listenerRight = new float3(1, 0, 0);
            
            // Try to get listener data from singleton
            if (_listenerQuery.CalculateEntityCount() > 0)
            {
                var listener = SystemAPI.GetSingleton<AudioListener>();
                listenerPosition = listener.Position;
                listenerRight = listener.Right;
            }

            // Schedule the spatialization job
            var job = new SpatializationJob
            {
                ListenerPosition = listenerPosition,
                ListenerRight = listenerRight,
                MaxPanDistance = MaxPanDistance,
                MinDistance = MinDistance
            };

            job.ScheduleParallel();
        }
    }

    /// <summary>
    /// Job that calculates stereo panning gains for each active spatial voice.
    /// Uses equal-power panning law: L = cos(θ), R = sin(θ) where θ = (pan + 1) * π/4
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(VoiceActive), typeof(VoiceIsSpatial))]
    public partial struct SpatializationJob : IJobEntity
    {
        [ReadOnly] public float3 ListenerPosition;
        [ReadOnly] public float3 ListenerRight;
        [ReadOnly] public float MaxPanDistance;
        [ReadOnly] public float MinDistance;

        private void Execute(
            in LocalTransform transform,
            DynamicBuffer<SpatializationChannelGains> gains)
        {
            // Calculate direction from listener to voice (horizontal plane only for stereo)
            float3 toVoice = transform.Position - ListenerPosition;
            
            // Project onto horizontal plane (ignore vertical component for stereo panning)
            toVoice.y = 0f;
            
            float distance = math.length(toVoice);
            
            // Avoid division by zero
            if (distance < MinDistance)
            {
                // Voice is very close to listener - center pan
                gains[0] = new SpatializationChannelGains { Value = 0.707f }; // Left
                gains[1] = new SpatializationChannelGains { Value = 0.707f }; // Right
                return;
            }

            // Normalize direction
            float3 direction = toVoice / distance;
            
            // Calculate pan value (-1 to +1) using dot product with listener's right vector
            float pan = math.dot(direction, ListenerRight);
            
            // Scale pan by distance factor (closer = more centered, further = more extreme pan)
            float distanceFactor = math.saturate(distance / MaxPanDistance);
            pan *= distanceFactor;
            
            // Convert pan to equal-power gains using circular law
            // θ = (pan + 1) * π/4 maps pan range [-1, 1] to angle range [0, π/2]
            float angle = (pan + 1f) * (math.PI / 4f);
            
            float leftGain = math.cos(angle);
            float rightGain = math.sin(angle);
            
            // Write gains to buffer
            gains[0] = new SpatializationChannelGains { Value = leftGain };
            gains[1] = new SpatializationChannelGains { Value = rightGain };
        }
    }
}
