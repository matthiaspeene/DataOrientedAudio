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
    [UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
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
                float impulse = math.abs(details.EstimatedImpulse);
                float3 contactPosition = details.EstimatedContactPointPositions.Length > 0
                    ? details.AverageContactPointPosition
                    : float3.zero;

                if (playA)
                    TryEmit(collisionEvent.EntityA, impulse, contactPosition);
                if (playB)
                    TryEmit(collisionEvent.EntityB, impulse, contactPosition);

                details.EstimatedContactPointPositions.Dispose();
            }

            private bool CanPlay(Entity entity)
            {
                return SettingsLookup.HasComponent(entity) && EventBufferLookup.HasBuffer(entity);
            }

            private void TryEmit(Entity entity, float impulse, float3 contactPosition)
            {
                PlayAudioOnCollision settings = SettingsLookup[entity];
                if (CurrentTime < settings.NextAllowedTime || impulse < settings.MinimumImpulse)
                    return;

                bool attached = settings.Space == AudioEventSpace.Attached3D;
                EventBufferLookup[entity].Add(new AudioEvent
                {
                    VoiceTypeHash = settings.VoiceTypeHash,
                    Position = attached ? float3.zero : contactPosition,
                    AttachTo = attached ? entity : Entity.Null
                });

                settings.NextAllowedTime = CurrentTime + settings.CooldownSeconds;
                SettingsLookup[entity] = settings;
            }
        }
    }
}
