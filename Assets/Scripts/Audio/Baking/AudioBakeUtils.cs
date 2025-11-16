// File: Audio/Baking/AudioBakeUtils.cs
using Unity.Entities;
using UnityEngine;
using DataOrientedAudio.Core.Runtime;
using System;

namespace DataOrientedAudio.Audio.Baking
{
    public static class AudioBakeUtils
    {
        // ---------------------------
        //  KEY TYPES
        // ---------------------------

        struct SampleBlobKey : IEquatable<SampleBlobKey>
        {
            public UnityEngine.Object Clip;

            public bool Equals(SampleBlobKey other) => Clip == other.Clip;
            public override int GetHashCode() => Clip ? Clip.GetHashCode() : 0;
        }

        struct VoiceBlobKey : IEquatable<VoiceBlobKey>
        {
            public UnityEngine.Object VoiceDef;

            public bool Equals(VoiceBlobKey other) => VoiceDef == other.VoiceDef;
            public override int GetHashCode() => VoiceDef ? VoiceDef.GetHashCode() : 0;
        }

        // ---------------------------
        //  EXTENSION: SAMPLE BLOB
        // ---------------------------

        public static BlobAssetReference<SampleDataBlob> GetSampleDataBlob(
            this Baker baker,
            AudioClip clip)
        {
            var key = new SampleBlobKey { Clip = clip };

            return baker.GetBlobAssetReference(key, builder =>
            {
                ref var root = ref builder.ConstructRoot<SampleDataBlob>();

                int count = clip.samples * clip.channels;
                float[] data = new float[count];
                clip.GetData(data, 0);

                var samples = builder.Allocate(ref root.Samples, count);
                for (int i = 0; i < count; i++)
                    samples[i] = data[i];

                root.ChannelCount = clip.channels;
                root.SampleRate = clip.frequency;
                root.SampleCount = clip.samples;
            });
        }

        // ---------------------------
        //  EXTENSION: VOICE BLOB
        // ---------------------------

        public static BlobAssetReference<VoiceDataBlob> GetVoiceDefinitionBlob(
            this Baker baker,
            VoiceDefinition def)
        {
            var key = new VoiceBlobKey { VoiceDef = def };

            return baker.GetBlobAssetReference(key, builder =>
            {
                ref var root = ref builder.ConstructRoot<VoiceDataBlob>();

                // Allocate variation clips
                var clips = builder.Allocate(ref root.Clips, def.Clips.Length);
                for (int i = 0; i < def.Clips.Length; i++)
                    clips[i] = baker.GetSampleDataBlob(def.Clips[i]);

                root.DefaultGain = def.DefaultGain;
                root.DefaultPitch = def.DefaultPitch;

                // If you have spatialization blobs, bake them here:
                // root.Spatialization = baker.GetSpatializationBlob(def.Spatialization);
            });
        }
    }
}
