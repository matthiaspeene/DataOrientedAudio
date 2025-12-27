using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    /// <summary>
    /// Sums (multiplies) all gain modifiers into the final OutChannelGain buffer.
    /// Uses change filters to only process entities when a gain modifier has changed.
    /// </summary>
    [UpdateInGroup(typeof(AudioVoiceFinalizationGroup))]
    [BurstCompile]
    public partial struct VoiceGainSystem : ISystem
    {
        private EntityQuery _query;


        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // We want to process entities that have OutChannelGain AND at least one of the modifiers.
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<OutChannelGain>()
                .WithAny<MixGainMod, RandomGainMod, SpatializationChannelGains, DistanceAttenuationGainMod>()
                .Build(ref state);

            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new VoiceGainJob
            {
                MixGainLookup = SystemAPI.GetComponentLookup<MixGainMod>(true),
                RandomGainLookup = SystemAPI.GetComponentLookup<RandomGainMod>(true),
                DistanceAttenLookup = SystemAPI.GetComponentLookup<DistanceAttenuationGainMod>(true),
                SpatialGainsLookup = SystemAPI.GetBufferLookup<SpatializationChannelGains>(true),
                LastSystemVersion = state.LastSystemVersion
            };

            state.Dependency = job.ScheduleParallel(_query, state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct VoiceGainJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<MixGainMod> MixGainLookup;
        [ReadOnly] public ComponentLookup<RandomGainMod> RandomGainLookup;
        [ReadOnly] public ComponentLookup<DistanceAttenuationGainMod> DistanceAttenLookup;
        [ReadOnly] public BufferLookup<SpatializationChannelGains> SpatialGainsLookup;
        public uint LastSystemVersion;

        public void Execute(Entity entity, DynamicBuffer<OutChannelGain> outGains)
        {
            // Efficiency check: only proceed if any relevant component has changed.
            bool changed = false;

            if (MixGainLookup.HasComponent(entity) && MixGainLookup.DidChange(entity, LastSystemVersion))
                changed = true;
            else if (RandomGainLookup.HasComponent(entity) && RandomGainLookup.DidChange(entity, LastSystemVersion))
                changed = true;
            else if (DistanceAttenLookup.HasComponent(entity) && DistanceAttenLookup.DidChange(entity, LastSystemVersion))
                changed = true;
            else if (SpatialGainsLookup.HasBuffer(entity) && SpatialGainsLookup.DidChange(entity, LastSystemVersion))
                changed = true;

            if (!changed)
                return;

            // 1. Calculate base gain from global modifiers
            float baseGain = 1.0f;

            if (MixGainLookup.HasComponent(entity))
            {
                baseGain *= MixGainLookup[entity].Value;
            }

            if (RandomGainLookup.HasComponent(entity))
            {
                baseGain *= RandomGainLookup[entity].Result;
            }

            if (DistanceAttenLookup.HasComponent(entity))
            {
                baseGain *= DistanceAttenLookup[entity].Value;
            }

            // 2. Apply spatialization gains or just base gain to all channels
            if (SpatialGainsLookup.HasBuffer(entity))
            {
                var sGains = SpatialGainsLookup[entity];
                int count = math.min(outGains.Length, sGains.Length);

                for (int i = 0; i < count; i++)
                {
                    outGains[i] = new OutChannelGain { Value = baseGain * sGains[i].Value };
                }
            }
            else
            {
                for (int i = 0; i < outGains.Length; i++)
                {
                    outGains[i] = new OutChannelGain { Value = baseGain };
                }
            }
        }
    }
}
