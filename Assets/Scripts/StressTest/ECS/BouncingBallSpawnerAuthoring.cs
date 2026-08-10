using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DataOrientedAudio.StressTest
{
    /// <summary>Spawns an initial set of ECS physics balls and adds more at a fixed interval.</summary>
    public class BouncingBallSpawnerAuthoring : MonoBehaviour
    {
        [Header("Prefab")]
        [Tooltip("A prefab with MeshRenderer, SphereCollider, Rigidbody, and PlayAudioOnCollisionAuthoring.")]
        [SerializeField] private GameObject ballPrefab;

        [Header("Population")]
        [Min(0)] [SerializeField] private int initialBallCount = 12;
        [Min(0)] [SerializeField] private int ballsPerInterval = 1;
        [Min(1)] [SerializeField] private int maximumBallCount = 100;
        [Min(0f)] [SerializeField] private float spawnInterval = 1.5f;

        [Header("Spawn Volume")]
        [SerializeField] private Vector3 spawnExtents = new Vector3(5f, 3f, 5f);
        [Min(0f)] [SerializeField] private float minimumLaunchSpeed = 4f;
        [Min(0f)] [SerializeField] private float maximumLaunchSpeed = 9f;
        [SerializeField] private uint randomSeed = 1;

        private void OnValidate()
        {
            initialBallCount = Mathf.Max(0, initialBallCount);
            ballsPerInterval = Mathf.Max(0, ballsPerInterval);
            maximumBallCount = Mathf.Max(1, maximumBallCount);
            spawnInterval = Mathf.Max(0f, spawnInterval);
            minimumLaunchSpeed = Mathf.Max(0f, minimumLaunchSpeed);
            maximumLaunchSpeed = Mathf.Max(minimumLaunchSpeed, maximumLaunchSpeed);
            spawnExtents = new Vector3(
                Mathf.Max(0f, spawnExtents.x),
                Mathf.Max(0f, spawnExtents.y),
                Mathf.Max(0f, spawnExtents.z));
            randomSeed = math.max(1u, randomSeed);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(transform.position, spawnExtents * 2f);
        }

        private sealed class Baker : Baker<BouncingBallSpawnerAuthoring>
        {
            public override void Bake(BouncingBallSpawnerAuthoring authoring)
            {
                if (authoring.ballPrefab == null)
                {
                    Debug.LogWarning(
                        $"{nameof(BouncingBallSpawnerAuthoring)} on '{authoring.name}' has no ball prefab. Skipping bake.",
                        authoring);
                    return;
                }

                if (!authoring.ballPrefab.TryGetComponent(out Rigidbody _))
                {
                    Debug.LogWarning(
                        $"Ball prefab '{authoring.ballPrefab.name}' needs a Rigidbody so it bakes as a dynamic ECS physics body.",
                        authoring.ballPrefab);
                }

                Entity entity = GetEntity(TransformUsageFlags.None);
                Entity prefabEntity = GetEntity(authoring.ballPrefab, TransformUsageFlags.Dynamic);
                AddComponent(entity, new BouncingBallSpawner
                {
                    BallPrefab = prefabEntity,
                    SpawnCenter = authoring.transform.position,
                    SpawnExtents = authoring.spawnExtents,
                    InitialBallCount = math.min(authoring.initialBallCount, authoring.maximumBallCount),
                    BallsPerInterval = authoring.ballsPerInterval,
                    MaximumBallCount = authoring.maximumBallCount,
                    SpawnInterval = authoring.spawnInterval,
                    MinimumSpeed = authoring.minimumLaunchSpeed,
                    MaximumSpeed = authoring.maximumLaunchSpeed,
                    SpawnedBallCount = 0,
                    NextSpawnTime = 0d,
                    RandomState = math.max(1u, authoring.randomSeed),
                    InitialSpawnComplete = false
                });
            }
        }
    }
}
