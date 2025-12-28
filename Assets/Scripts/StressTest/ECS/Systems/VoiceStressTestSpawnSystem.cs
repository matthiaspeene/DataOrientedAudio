using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using DataOrientedAudio.Events.Runtime;
using Unity.Collections;

namespace DataOrientedAudio.StressTest.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class VoiceStressTestSpawnSystem : SystemBase
    {
        /// <summary>
        /// Public method to increase stress level programmatically.
        /// </summary>
        public void IncreaseStressLevel()
        {
            if (SystemAPI.HasSingleton<VoiceStressTest>())
            {
                var entity = SystemAPI.GetSingletonEntity<VoiceStressTest>();
                if (EntityManager.HasBuffer<VoiceStressConfig>(entity))
                {
                    var stressTest = SystemAPI.GetComponentRW<VoiceStressTest>(entity);
                    var configBuffer = SystemAPI.GetBuffer<VoiceStressConfig>(entity);
                    IncreaseStressLevelInternal(ref stressTest.ValueRW, configBuffer);
                }
            }
        }

        protected override void OnUpdate()
        {
            //IncreaseStressLevel();
        }

        private void IncreaseStressLevelInternal(ref VoiceStressTest stressTest, DynamicBuffer<VoiceStressConfig> configBuffer)
        {
            // Increment step
            stressTest.CurrentStressStep++;

            int totalSpawnedSoFar = stressTest.TotalVoicesSpawned;

            /* ECS has no cap. Cap is defined by the audio engine.
            if (totalSpawnedSoFar >= 255)
            {
                Debug.LogWarning("VoiceStressTestSpawnSystem: Reached cap of 255 voices. Cannot increase stress further.");
                return;
            }
*/

            // Create random number generator with seed
            var random = new Unity.Mathematics.Random(stressTest.RandomSeed);

            // Advance random state based on current step to get different results each time
            for (int i = 0; i < stressTest.CurrentStressStep; i++)
            {
                random.NextUInt();
            }

            // Spawn voices for each config
            // Copy to NativeArray to avoid invalidation during structural changes
            using var localConfigs = configBuffer.ToNativeArray(Allocator.Temp);
            for (int i = 0; i < localConfigs.Length; i++)
            {
                var config = localConfigs[i];
                int voicesToAddThisStep = config.AmountRatio;

                // Enforce global 255 cap
                int maxAllowedMore = 255 - totalSpawnedSoFar;

                if (voicesToAddThisStep > maxAllowedMore)
                {
                    voicesToAddThisStep = maxAllowedMore;
                }

                SpawnVoicesOfType(ref random, config, voicesToAddThisStep, stressTest.MovementBounds);

                totalSpawnedSoFar += voicesToAddThisStep;

                if (totalSpawnedSoFar >= 255)
                {
                    Debug.LogWarning("VoiceStressTestSpawnSystem: Reached cap of 255 voices. Cannot increase stress further.");
                    break;
                }
            }

            stressTest.TotalVoicesSpawned = totalSpawnedSoFar;

            // Update seed for next iteration
            stressTest.RandomSeed = random.NextUInt();

            Debug.Log($"VoiceStressTestSpawnSystem: Increased stress level to step {stressTest.CurrentStressStep}. Currently spawned voices: {totalSpawnedSoFar}.");
        }

        private void SpawnVoicesOfType(ref Unity.Mathematics.Random random, VoiceStressConfig config, int count, float3 bounds)
        {
            for (int i = 0; i < count; i++)
            {
                Entity voiceEntity = EntityManager.CreateEntity();

                // Set position
                float3 startPosition = config.Spatialized
                    ? GetRandomPositionInBounds(ref random, bounds)
                    : float3.zero;

                EntityManager.AddComponentData(voiceEntity, LocalTransform.FromPosition(startPosition));

                // Set voice data
                EntityManager.AddComponentData(voiceEntity, new StressTestVoice
                {
                    VoiceTypeHash = config.VoiceTypeHash,
                    NextTriggerTime = (float)SystemAPI.Time.ElapsedTime + random.NextFloat(0f, config.MaxStartDelay),
                    RepeatDelayMin = 0.5f,  // Could be exposed in config
                    RepeatDelayMax = 2f
                });

                // Set movement if needed
                if (config.Moving && config.Spatialized)
                {
                    EntityManager.AddComponentData(voiceEntity, new StressTestMovement
                    {
                        Direction = math.normalize(random.NextFloat3(-1f, 1f)),
                        Speed = random.NextFloat(config.MinSpeed, config.MaxSpeed)
                    });
                }

                // Add audio event emitter
                EntityManager.AddComponentData(voiceEntity, new AudioEventEmitter { DefaultVoiceDef = Entity.Null });
                EntityManager.AddBuffer<AudioEvent>(voiceEntity);
            }
        }

        private float3 GetRandomPositionInBounds(ref Unity.Mathematics.Random random, float3 bounds)
        {
            return new float3(
                random.NextFloat(-bounds.x, bounds.x),
                random.NextFloat(-bounds.y, bounds.y),
                random.NextFloat(-bounds.z, bounds.z)
            );
        }
    }
}
