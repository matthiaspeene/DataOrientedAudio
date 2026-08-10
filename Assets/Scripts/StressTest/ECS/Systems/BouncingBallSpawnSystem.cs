using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace DataOrientedAudio.StressTest.Systems
{
    /// <summary>Instantiates ECS physics ball prefabs and assigns their initial velocities.</summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [BurstCompile]
    public partial struct BouncingBallSpawnSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BouncingBallSpawner>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            double currentTime = SystemAPI.Time.ElapsedTime;
            var commandBuffer = new EntityCommandBuffer(Allocator.Temp);

            foreach (RefRW<BouncingBallSpawner> spawnerRef in
                     SystemAPI.Query<RefRW<BouncingBallSpawner>>())
            {
                BouncingBallSpawner spawner = spawnerRef.ValueRO;
                int countToSpawn = GetSpawnCount(ref spawner, currentTime);
                if (countToSpawn <= 0)
                {
                    spawnerRef.ValueRW = spawner;
                    continue;
                }

                var random = new Random(math.max(1u, spawner.RandomState));
                for (int i = 0; i < countToSpawn; i++)
                {
                    Entity ball = commandBuffer.Instantiate(spawner.BallPrefab);
                    float3 position = spawner.SpawnCenter + random.NextFloat3(-spawner.SpawnExtents, spawner.SpawnExtents);
                    float3 direction = math.normalizesafe(
                        random.NextFloat3(new float3(-1f, 0.2f, -1f), new float3(1f, 1f, 1f)),
                        math.up());
                    float speed = random.NextFloat(spawner.MinimumSpeed, spawner.MaximumSpeed);

                    commandBuffer.SetComponent(ball, LocalTransform.FromPositionRotationScale(
                        position,
                        random.NextQuaternionRotation(),
                        1f));
                    commandBuffer.SetComponent(ball, new PhysicsVelocity
                    {
                        Linear = direction * speed,
                        Angular = random.NextFloat3(-4f, 4f)
                    });
                }

                spawner.SpawnedBallCount += countToSpawn;
                spawner.RandomState = random.NextUInt();
                if (spawner.RandomState == 0)
                    spawner.RandomState = 1;
                spawnerRef.ValueRW = spawner;
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }

        private static int GetSpawnCount(ref BouncingBallSpawner spawner, double currentTime)
        {
            int remaining = spawner.MaximumBallCount - spawner.SpawnedBallCount;
            if (remaining <= 0)
                return 0;

            if (!spawner.InitialSpawnComplete)
            {
                spawner.InitialSpawnComplete = true;
                spawner.NextSpawnTime = currentTime + spawner.SpawnInterval;
                return math.min(spawner.InitialBallCount, remaining);
            }

            if (spawner.SpawnInterval <= 0f || spawner.BallsPerInterval <= 0 || currentTime < spawner.NextSpawnTime)
                return 0;

            spawner.NextSpawnTime = currentTime + spawner.SpawnInterval;
            return math.min(spawner.BallsPerInterval, remaining);
        }
    }
}
