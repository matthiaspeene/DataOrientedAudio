using Unity.Entities;
using UnityEngine;
using DataOrientedAudio.Events.Runtime;
using DataOrientedAudio.Voice.Authoring;

namespace DataOrientedAudio.Events.Authoring
{
    public class RandomAudioPlayerAuthoring : MonoBehaviour
    {
        public VoiceDataScriptable VoiceData;
        public float MinInterval = 1f;
        public float MaxInterval = 3f;

        public class Baker : Baker<RandomAudioPlayerAuthoring>
        {
            public override void Bake(RandomAudioPlayerAuthoring authoring)
            {
                if (authoring.VoiceData == null)
                {
                    Debug.LogWarning($"RandomAudioPlayerAuthoring on '{authoring.name}' has no VoiceData assigned.", authoring);
                    return;
                }

                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // Add the Player component
                AddComponent(entity, new RandomAudioPlayer
                {
                    Timer = 0f, // Start immediately
                    MinInterval = authoring.MinInterval,
                    MaxInterval = authoring.MaxInterval,
                    VoiceTypeHash = authoring.VoiceData.name.GetHashCode(),
                    Random = new Unity.Mathematics.Random((uint)entity.Index + 1) // Simple seed
                });

                // Add requirements for VoiceAllocationSystem
                AddComponent(entity, new AudioEventEmitter
                {
                    DefaultVoiceDef = Entity.Null
                });

                AddBuffer<AudioEvent>(entity);
            }
        }
    }
}
