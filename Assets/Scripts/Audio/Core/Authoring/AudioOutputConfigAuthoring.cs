using Unity.Entities;
using UnityEngine;
using DataOrientedAudio.Core.Runtime;

namespace DataOrientedAudio.Core.Authoring
{
    // TODO: make this system listen to the AudioSettings changes at runtime.
    public class AudioOutputConfigAuthoring : MonoBehaviour
    {
        [Header("Output")]
        [Min(1)]
        [SerializeField] private int channelCount = 2;
        // [SerializeField] private int sampleRate = 48000;
        // [SerializeField] private int bufferSize = 1024;

        public int ChannelCount => channelCount;
        // public int SampleRate => sampleRate;
        // public int BufferSize => bufferSize;

        private void OnValidate()
        {
            if (channelCount < 1)
                channelCount = 1;
        }
    }

    public class AudioOutputConfigBaker : Baker<AudioOutputConfigAuthoring>
    {
        public override void Bake(AudioOutputConfigAuthoring authoring)
        {
            // Single config entity for the whole audio world.
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new AudioOutputConfig
            {
                ChannelCount = authoring.ChannelCount,
                // SampleRate = authoring.SampleRate,
                // BufferSize = authoring.BufferSize
            });
        }
    }
}
