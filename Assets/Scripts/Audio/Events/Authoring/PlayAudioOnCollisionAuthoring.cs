using DataOrientedAudio.Events.Runtime;
using DataOrientedAudio.Voice.Authoring;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

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
        [Tooltip("Minimum time between sounds from this entity, even if it touches several colliders at once.")]
        [SerializeField] private float cooldownSeconds = 0.08f;

        [Header("Impact Velocity Volume")]
        [Min(0f)]
        [Tooltip("Minimum relative speed toward the contact point required to play. At this speed, the quiet impact gain is used.")]
        [FormerlySerializedAs("quietImpactSpeed")]
        [SerializeField] private float minimumImpactSpeed = 1f;

        [Min(0f)]
        [Tooltip("Relative speed toward the contact point which produces the loud impact gain. Faster impacts are clamped to that gain.")]
        [SerializeField] private float loudImpactSpeed = 12f;

        [Range(0f, 2f)]
        [Tooltip("Linear volume multiplier at the minimum impact speed.")]
        [SerializeField] private float quietImpactGain = 0.08f;

        [Range(0f, 2f)]
        [Tooltip("Linear volume multiplier at or above the loud impact speed.")]
        [SerializeField] private float loudImpactGain = 1f;

        private void Reset()
        {
            EnableContactEvents();
        }

        private void OnValidate()
        {
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
            loudImpactSpeed = Mathf.Max(minimumImpactSpeed + 0.01f, loudImpactSpeed);
            quietImpactGain = Mathf.Max(0f, quietImpactGain);
            loudImpactGain = Mathf.Max(0f, loudImpactGain);
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
                    CooldownSeconds = authoring.cooldownSeconds,
                    MinimumImpactSpeed = authoring.minimumImpactSpeed,
                    LoudImpactSpeed = authoring.loudImpactSpeed,
                    QuietImpactGain = authoring.quietImpactGain,
                    LoudImpactGain = authoring.loudImpactGain,
                    NextAllowedTime = 0d
                });

                AddComponent(entity, new AudioEventEmitter { DefaultVoiceDef = Entity.Null });
                AddBuffer<AudioEvent>(entity);
            }
        }
    }
}
