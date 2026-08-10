using Unity.Collections;
using Unity.Entities;
using DataOrientedAudio.Voice.Runtime;
using DataOrientedAudio.Busses.Generated;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class AudioTopologySystem : SystemBase
    {
        #region Fields

        private NativeList<AudioTopologyArchetype> _archetypes;
        private int _busCount;
        private bool _isInitialized;

        #endregion

        #region Lifecycle

        protected override void OnCreate()
        {
            base.OnCreate();

            _archetypes = new NativeList<AudioTopologyArchetype>(Allocator.Persistent);

            // Don't let this system start running until at least one baked voice exists.
            // This effectively waits for the SubScene that contains voices to be loaded.
            RequireForUpdate<VoiceBlobReference>();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            //UnityEngine.Debug.Log("[AudioTopologySystem] OnStartRunning – building topology");
            BuildTopology();
        }

        protected override void OnUpdate()
        {
            // Topology is static for now, nothing to do per frame.
        }

        protected override void OnDestroy()
        {
            // Stop the realtime side before releasing the topology list and the
            // ECS-owned blob assets referenced by that topology.
            AudioShutdownState.RequestShutdown();

            if (_archetypes.IsCreated)
                _archetypes.Dispose();

            base.OnDestroy();
        }

        #endregion

        #region Public API

        public AudioTopologyData GetTopologyData()
        {
            // In case we're being queried from the audio side before OnStartRunning,
            // this gives us a second chance to build once entities exist.
            if (!_isInitialized)
            {
                UnityEngine.Debug.Log("[AudioTopologySystem] Lazy topology build from GetTopologyData");
                BuildTopology();
            }

            if (!_isInitialized || _archetypes.Length == 0)
            {
                UnityEngine.Debug.Log("[AudioTopologySystem] Topology not ready or no archetypes – returning empty");
                return new AudioTopologyData
                {
                    MaxArchetypes = 0,
                    TotalVoices = 0,
                    MaxBuses = 0,
                    Archetypes = default
                };
            }

            var totalVoiceCount = 0;
            if (_archetypes.Length > 0)
            {
                var lastArchetype = _archetypes[_archetypes.Length - 1];
                totalVoiceCount = lastArchetype.Start + lastArchetype.Count;
            }

            //UnityEngine.Debug.Log($"[AudioTopologySystem] Returning topology with {totalVoiceCount} voices " +
            //                      $"across {_archetypes.Length} archetypes");

            return new AudioTopologyData
            {
                MaxArchetypes = _archetypes.Length,
                TotalVoices = totalVoiceCount,
                MaxBuses = System.Enum.GetValues(typeof(BusId)).Length, // TODO: This is not optimal, we need to count the number of "real" buses in the topology wich will be the "pruned" list of buses
                Archetypes = _archetypes.AsArray()
            };
        }

        #endregion

        #region Topology Building

        private void BuildTopology()
        {
            if (_isInitialized)
                return;

            var query = SystemAPI.QueryBuilder()
                .WithAll<VoiceBlobReference, VoiceLocalIndex>()
                .Build();

            if (query.IsEmpty)
            {
                // This can happen if we're called before subscenes finish loading.
                UnityEngine.Debug.Log("[AudioTopologySystem] BuildTopology – no voices found yet");
                return;
            }

            _archetypes.Clear();

            var voiceTypeData = GatherVoiceTypes();
            BuildTopologyArchetypes(voiceTypeData);
            AssignArchetypeIndices(voiceTypeData.SortedTypeIds);
            CreateOrUpdateSingleton();


            _isInitialized = true;

            DisposeTemporaryCollections(voiceTypeData);
        }

        private VoiceTypeData GatherVoiceTypes()
        {
            var uniqueTypes = new NativeHashMap<int, BlobAssetReference<VoiceBlob>>(16, Allocator.Temp);
            var typeCounts = new NativeHashMap<int, int>(16, Allocator.Temp);

            foreach (var (blobRef, entity) in SystemAPI
                         .Query<RefRO<VoiceBlobReference>>()
                         .WithAll<VoiceTypeID>()
                         .WithEntityAccess())
            {
                var type = EntityManager.GetSharedComponent<VoiceTypeID>(entity);
                var typeId = type.Value;

                if (!uniqueTypes.ContainsKey(typeId))
                {
                    uniqueTypes.Add(typeId, blobRef.ValueRO.Value);
                    typeCounts.Add(typeId, 0);
                }

                typeCounts[typeId] = typeCounts[typeId] + 1;
            }

            var sortedTypeIds = uniqueTypes.GetKeyArray(Allocator.Temp);
            sortedTypeIds.Sort();

            return new VoiceTypeData
            {
                UniqueTypes = uniqueTypes,
                TypeCounts = typeCounts,
                SortedTypeIds = sortedTypeIds
            };
        }

        private void BuildTopologyArchetypes(VoiceTypeData voiceTypeData)
        {
            var currentStartIndex = 0;
            var currentArchetypeIndex = 0;

            for (var i = 0; i < voiceTypeData.SortedTypeIds.Length; i++)
            {
                var typeId = voiceTypeData.SortedTypeIds[i];
                var voiceBlob = voiceTypeData.UniqueTypes[typeId];
                var voiceCount = voiceTypeData.TypeCounts[typeId];

                _archetypes.Add(new AudioTopologyArchetype
                {
                    ArchetypeIndex = currentArchetypeIndex,
                    Blob = voiceBlob,
                    Start = currentStartIndex,
                    Count = voiceCount
                });

                currentStartIndex += voiceCount;
                currentArchetypeIndex++;
            }
        }

        private void AssignArchetypeIndices(NativeArray<int> sortedTypeIds)
        {
            for (var i = 0; i < sortedTypeIds.Length; i++)
            {
                var typeId = sortedTypeIds[i];
                var archetypeIndex = i;

                foreach (var (archIdx, entity) in SystemAPI
                             .Query<RefRW<VoiceArchetypeIndex>>()
                             .WithAll<VoiceTypeID>()
                             .WithEntityAccess())
                {
                    var entityTypeId = EntityManager.GetSharedComponent<VoiceTypeID>(entity).Value;
                    if (entityTypeId == typeId)
                    {
                        archIdx.ValueRW.Value = archetypeIndex;
                    }
                }
            }
        }

        private void CreateOrUpdateSingleton()
        {
            var totalVoiceCount = 0;
            if (_archetypes.Length > 0)
            {
                var lastArchetype = _archetypes[_archetypes.Length - 1];
                totalVoiceCount = lastArchetype.Start + lastArchetype.Count;
            }

            var singleton = new AudioTopologySingleton
            {
                MaxArchetypes = _archetypes.Length,
                TotalVoices = totalVoiceCount
            };

            if (SystemAPI.HasSingleton<AudioTopologySingleton>())
            {
                SystemAPI.SetSingleton(singleton);
            }
            else
            {
                var entity = EntityManager.CreateEntity(typeof(AudioTopologySingleton));
                EntityManager.SetComponentData(entity, singleton);
            }
        }

        private void DisposeTemporaryCollections(VoiceTypeData voiceTypeData)
        {
            voiceTypeData.SortedTypeIds.Dispose();
            voiceTypeData.UniqueTypes.Dispose();
            voiceTypeData.TypeCounts.Dispose();
        }

        #endregion

        #region Helper Structures

        private struct VoiceTypeData
        {
            public NativeHashMap<int, BlobAssetReference<VoiceBlob>> UniqueTypes;
            public NativeHashMap<int, int> TypeCounts;
            public NativeArray<int> SortedTypeIds;
        }

        #endregion
    }
}
