using DataOrientedAudio.Events.Runtime;
using DataOrientedAudio.Voice.Authoring;
using Unity.Entities;
using UnityEngine;

namespace DataOrientedAudio.Events.Authoring
{
    /// <summary>
    /// Reusable collision-to-audio bridge for entities baked with Unity Physics.
    /// Add it to the same GameObject as the Collider (and Rigidbody for a dynamic body).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayAudioOnCollisionAuthoring : MonoBehaviour
    {
        [SerializeField] private VoiceDataScriptable voiceData;

        [Min(0f)]
        [Tooltip("Contacts below this estimated solver impulse do not make a sound. This prevents resting contacts from chattering.")]
        [SerializeField] private float minimumImpulse = 0.5f;

        [Min(0f)]
        [Tooltip("Minimum time between sounds from this entity, even if it touches several colliders at once.")]
        [SerializeField] private float cooldownSeconds = 0.08f;

        private void Reset()
        {
            EnableContactEvents();
        }

        private void OnValidate()
        {
            minimumImpulse = Mathf.Max(0f, minimumImpulse);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            EnableContactEvents();
        }

        private void EnableContactEvents()
        {
            if (TryGetComponent(out Collider attachedCollider))
            {
                // Unity Physics 6.5 converts this to CollideRaiseCollisionEvents.
                attachedCollider.providesContacts = true;
            }
        }

        private sealed class Baker : Baker<PlayAudioOnCollisionAuthoring>
        {
            public override void Bake(PlayAudioOnCollisionAuthoring authoring)
            {
                if (authoring.voiceData == null)
                {
                    Debug.LogWarning(
                        $"{nameof(PlayAudioOnCollisionAuthoring)} on '{authoring.name}' has no Voice Data assigned. Skipping bake.",
                        authoring);
                    return;
                }

                Collider attachedCollider = GetComponent<Collider>();
                if (attachedCollider == null)
                {
                    Debug.LogWarning(
                        $"{nameof(PlayAudioOnCollisionAuthoring)} on '{authoring.name}' needs a Collider on the same GameObject. Skipping bake.",
                        authoring);
                    return;
                }

                if (!attachedCollider.providesContacts)
                {
                    Debug.LogWarning(
                        $"The Collider on '{authoring.name}' is not providing contacts. Enable 'Provides Contacts' to receive collision audio events.",
                        attachedCollider);
                }

                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new PlayAudioOnCollision
                {
                    VoiceTypeHash = authoring.voiceData.name.GetHashCode(),
                    Space = authoring.voiceData.Space,
                    MinimumImpulse = authoring.minimumImpulse,
                    CooldownSeconds = authoring.cooldownSeconds,
                    NextAllowedTime = 0d
                });

                AddComponent(entity, new AudioEventEmitter { DefaultVoiceDef = Entity.Null });
                AddBuffer<AudioEvent>(entity);
            }
        }
    }
}
