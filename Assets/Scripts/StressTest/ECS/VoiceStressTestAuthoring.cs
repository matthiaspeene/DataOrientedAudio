using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using DataOrientedAudio.Events.Runtime;

namespace DataOrientedAudio.StressTest
{
    public class VoiceStressTestAuthoring : MonoBehaviour
    {
        [Header("Test Configuration")]
        [SerializeField] private VoiceStressTestConfig testConfig;

        class Baker : Baker<VoiceStressTestAuthoring>
        {
            public override void Bake(VoiceStressTestAuthoring authoring)
            {
                if (authoring.testConfig == null)
                {
                    Debug.LogWarning("VoiceStressTestAuthoring: No testConfig assigned. Skipping bake.", authoring);
                    return;
                }

                // Create singleton entity with Dynamic transform usage
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // Add singleton component
                AddComponent(entity, new VoiceStressTest
                {
                    CurrentStressStep = 0,
                    TotalVoicesSpawned = 0,
                    MovementBounds = new float3(
                        authoring.testConfig.movementBounds.x,
                        authoring.testConfig.movementBounds.y,
                        authoring.testConfig.movementBounds.z
                    ),
                    RandomSeed = (uint)(authoring.GetInstanceID() ^ 0x9E3779B9)
                });

                // Add audio event components
                AddComponent(entity, new AudioEventEmitter { DefaultVoiceDef = Entity.Null });
                AddBuffer<AudioEvent>(entity);

                // Convert voice configs to buffer
                var configBuffer = AddBuffer<VoiceStressConfig>(entity);
                foreach (var voiceConfig in authoring.testConfig.voiceConfigs)
                {
                    if (voiceConfig.voiceData == null)
                    {
                        Debug.LogWarning("VoiceStressTestAuthoring: VoiceSpawnConfig has null voiceData. Skipping.", authoring);
                        continue;
                    }

                    configBuffer.Add(new VoiceStressConfig
                    {
                        VoiceTypeHash = voiceConfig.voiceData.name.GetHashCode(),
                        AmountRatio = voiceConfig.amountRatio,
                        Spatialized = voiceConfig.spatialized,
                        Moving = voiceConfig.moving,
                        MinSpeed = voiceConfig.minSpeed,
                        MaxSpeed = voiceConfig.maxSpeed,
                        MaxStartDelay = voiceConfig.maxStartDelay
                    });
                }
            }
        }
    }
}
