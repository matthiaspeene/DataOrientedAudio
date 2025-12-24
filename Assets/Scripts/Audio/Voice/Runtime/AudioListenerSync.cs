using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DataOrientedAudio.Voice.Runtime
{
    /// <summary>
    /// MonoBehaviour bridge that syncs the Main Camera to the ECS AudioListener singleton.
    /// Attach this to your Main Camera GameObject (the one with Unity's AudioListener).
    /// This does NOT need to be baked - it runs as a MonoBehaviour in the main scene.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AudioListenerSync : MonoBehaviour
    {
        private Entity _listenerEntity;
        private EntityManager _entityManager;
        private bool _isInitialized;

        private void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogWarning("AudioListenerSync: No default world found.");
                return;
            }

            _entityManager = world.EntityManager;

            // Find or create the singleton entity
            var query = _entityManager.CreateEntityQuery(typeof(AudioListenerTag));

            if (query.IsEmpty)
            {
                // Create singleton
                _listenerEntity = _entityManager.CreateEntity(
                    typeof(AudioListenerTag),
                    typeof(AudioListener));

                _entityManager.SetName(_listenerEntity, "AudioListener");
            }
            else
            {
                _listenerEntity = query.GetSingletonEntity();
            }

            query.Dispose();
            _isInitialized = true;
        }

        private void Update() // Use Update, NOT LateUpdate
        {
            if (!_isInitialized || !_entityManager.Exists(_listenerEntity))
                return;

            var t = transform;

            // Get previous position for velocity calculation
            var listener = _entityManager.GetComponentData<AudioListener>(_listenerEntity);

            float3 currentPosition = t.position;
            float deltaTime = Time.deltaTime;

            if (deltaTime > 0f)
            {
                listener.Velocity = (currentPosition - listener.PreviousPosition) / deltaTime;
            }
            else
            {
                listener.Velocity = float3.zero;
            }

            // Update all listener data
            listener.Position = currentPosition;
            listener.Forward = t.forward;
            listener.Right = t.right;
            listener.Up = t.up;
            listener.PreviousPosition = currentPosition;

            Debug.Log("AudioListenerSync: " + listener.Position);

            _entityManager.SetComponentData(_listenerEntity, listener);
        }

        private void OnDestroy()
        {
            // Don't destroy the singleton - other systems might need it
            // Just disconnect this MonoBehaviour
            _isInitialized = false;
        }
    }
}
