using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DataOrientedAudio.Voice.Runtime
{
    /// <summary>
    /// MonoBehaviour that syncs a Transform (typically Camera.main) to the ECS AudioListener singleton.
    /// This creates a bridge between Unity's GameObject/Transform system and the ECS audio listener.
    /// </summary>
    public class AudioListenerSync : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The transform to sync. If null, will use Camera.main on Start.")]
        private Transform _targetTransform;

        private Entity _listenerEntity;
        private EntityManager _entityManager;
        private EntityQuery _listenerQuery;
        private bool _isInitialized;

        private void Start()
        {
            // Find Camera.main if no explicit transform is assigned
            // Note: Camera.main is used for convenience and backwards compatibility.
            // For better performance, explicitly assign a target transform.
            if (_targetTransform == null)
            {
                if (Camera.main != null)
                {
                    _targetTransform = Camera.main.transform;
                }
                else
                {
                    Debug.LogWarning("AudioListenerSync: No target transform assigned and Camera.main is null. AudioListener will not sync.");
                    return;
                }
            }

            // Get or create the AudioListener singleton entity
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            _listenerQuery = _entityManager.CreateEntityQuery(typeof(AudioListenerTag));
            CreateOrFindListenerEntity();

            _isInitialized = true;
        }

        private void OnDestroy()
        {
            if (_listenerQuery.IsCreated)
            {
                _listenerQuery.Dispose();
            }
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _targetTransform == null)
                return;

            // Ensure entity is still valid
            if (!_entityManager.Exists(_listenerEntity))
            {
                CreateOrFindListenerEntity();
            }

            // Get current listener data
            var listener = _entityManager.GetComponentData<AudioListener>(_listenerEntity);

            // Calculate velocity from position delta using previous frame's stored position
            float3 currentPosition = _targetTransform.position;
            float deltaTime = Time.deltaTime;
            
            if (deltaTime > 0f)
            {
                listener.Velocity = (currentPosition - listener.PreviousPosition) / deltaTime;
            }
            else
            {
                listener.Velocity = float3.zero;
            }

            // Sync position and rotation data
            listener.Position = currentPosition;
            listener.Forward = _targetTransform.forward;
            listener.Right = _targetTransform.right;
            listener.Up = _targetTransform.up;
            
            // Store current position as previous for next frame's velocity calculation
            listener.PreviousPosition = currentPosition;

            // Write back to ECS
            _entityManager.SetComponentData(_listenerEntity, listener);
        }

        private void CreateOrFindListenerEntity()
        {
            // Try to find existing listener entity
            if (_listenerQuery.CalculateEntityCount() > 0)
            {
                _listenerEntity = _listenerQuery.GetSingletonEntity();
            }
            else
            {
                // Create new listener entity
                _listenerEntity = _entityManager.CreateEntity();
                _entityManager.AddComponentData(_listenerEntity, new AudioListenerTag());
                _entityManager.AddComponentData(_listenerEntity, new AudioListener
                {
                    Position = float3.zero,
                    Forward = new float3(0, 0, 1),
                    Right = new float3(1, 0, 0),
                    Up = new float3(0, 1, 0),
                    Velocity = float3.zero,
                    PreviousPosition = float3.zero
                });
            }
        }
    }
}
