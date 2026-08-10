using Unity.Entities;
using Unity.Mathematics;

namespace DataOrientedAudio.StressTest
{
    /// <summary>Runtime state and configuration for one continuously growing ball population.</summary>
    public struct BouncingBallSpawner : IComponentData
    {
        public Entity BallPrefab;
        public float3 SpawnCenter;
        public float3 SpawnExtents;
        public int InitialBallCount;
        public int BallsPerInterval;
        public int MaximumBallCount;
        public float SpawnInterval;
        public float MinimumSpeed;
        public float MaximumSpeed;
        public int SpawnedBallCount;
        public double NextSpawnTime;
        public uint RandomState;
        public bool InitialSpawnComplete;
    }
}
