using DataOrientedAudio.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

namespace DataOrientedAudio.Events.Runtime.Systems
{
    /// <summary>
    /// Converts Unity Physics collision events into AudioEvents. If both participants have
    /// PlayAudioOnCollision, each participant can emit its own corresponding sound.
    /// </summary>
    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PhysicsSimulationGroup))]
    [UpdateBefore(typeof(ExportPhysicsWorld))]
    [BurstCompile]
    public partial struct PlayAudioOnCollisionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayAudioOnCollision>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<SimulationSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new CollisionAudioJob
            {
                CurrentTime = SystemAPI.Time.ElapsedTime,
                PhysicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld,
                VelocityLookup = SystemAPI.GetComponentLookup<PhysicsVelocity>(true),
                SettingsLookup = SystemAPI.GetComponentLookup<PlayAudioOnCollision>(false),
                EventBufferLookup = SystemAPI.GetBufferLookup<AudioEvent>(false)
            };

            state.Dependency = job.Schedule(
                SystemAPI.GetSingleton<SimulationSingleton>(),
                state.Dependency);
        }

        [BurstCompile]
        private struct CollisionAudioJob : ICollisionEventsJob
        {
            public double CurrentTime;

            [ReadOnly]
            public PhysicsWorld PhysicsWorld;

            [ReadOnly]
            public ComponentLookup<PhysicsVelocity> VelocityLookup;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<PlayAudioOnCollision> SettingsLookup;

            [NativeDisableParallelForRestriction]
            public BufferLookup<AudioEvent> EventBufferLookup;

            public void Execute(CollisionEvent collisionEvent)
            {
                bool playA = CanPlay(collisionEvent.EntityA);
                bool playB = CanPlay(collisionEvent.EntityB);
                if (!playA && !playB)
                    return;

                CollisionEvent.Details details = collisionEvent.CalculateDetails(ref PhysicsWorld);
                if (details.EstimatedContactPointPositions.Length == 0)
                {
                    details.EstimatedContactPointPositions.Dispose();
                    return;
                }

                float3 contactPosition = details.AverageContactPointPosition;

                if (playA)
                {
                    float impactSpeed = GetImpactSpeed(
                        collisionEvent.EntityA,
                        collisionEvent.EntityB,
                        collisionEvent.BodyIndexA,
                        contactPosition);
                    TryEmit(collisionEvent.EntityA, impactSpeed, contactPosition);
                }

                if (playB)
                {
                    float impactSpeed = GetImpactSpeed(
                        collisionEvent.EntityB,
                        collisionEvent.EntityA,
                        collisionEvent.BodyIndexB,
                        contactPosition);
                    TryEmit(collisionEvent.EntityB, impactSpeed, contactPosition);
                }

                details.EstimatedContactPointPositions.Dispose();
            }

            private bool CanPlay(Entity entity)
            {
                return SettingsLookup.HasComponent(entity) && EventBufferLookup.HasBuffer(entity);
            }

            private float GetImpactSpeed(
                Entity entity,
                Entity otherEntity,
                int bodyIndex,
                float3 contactPosition)
            {
                float3 velocity = VelocityLookup.TryGetComponent(entity, out PhysicsVelocity physicsVelocity)
                    ? physicsVelocity.Linear
                    : float3.zero;
                float3 otherVelocity = VelocityLookup.TryGetComponent(otherEntity, out PhysicsVelocity otherPhysicsVelocity)
                    ? otherPhysicsVelocity.Linear
                    : float3.zero;

                // Only linear motion toward this body's contact point is impact motion.
                // Tangential travel (including the translation of a rolling body) contributes nothing.
                float3 bodyPosition = PhysicsWorld.Bodies[bodyIndex].WorldFromBody.pos;
                float3 directionToContact = math.normalizesafe(contactPosition - bodyPosition);
                return math.max(0f, math.dot(velocity - otherVelocity, directionToContact));
            }

            private void TryEmit(Entity entity, float impactSpeed, float3 contactPosition)
            {
                PlayAudioOnCollision settings = SettingsLookup[entity];
                if (CurrentTime < settings.NextAllowedTime ||
                    impactSpeed < settings.MinimumImpactSpeed)
                    return;

                bool attached = settings.Space == AudioEventSpace.Attached3D;
                float speedRange = settings.LoudImpactSpeed - settings.MinimumImpactSpeed;
                float volumeT = speedRange > math.EPSILON
                    ? math.saturate((impactSpeed - settings.MinimumImpactSpeed) / speedRange)
                    : 1f;
                volumeT = math.smoothstep(0f, 1f, volumeT);
                float gain = math.lerp(settings.QuietImpactGain, settings.LoudImpactGain, volumeT);

                EventBufferLookup[entity].Add(new AudioEvent
                {
                    VoiceTypeHash = settings.VoiceTypeHash,
                    Position = attached ? float3.zero : contactPosition,
                    AttachTo = attached ? entity : Entity.Null,
                    GainMultiplier = gain,
                    UseGainMultiplier = true
                });

                settings.NextAllowedTime = CurrentTime + settings.CooldownSeconds;
                SettingsLookup[entity] = settings;
            }
        }
    }
}
