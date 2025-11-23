// TODO: This system might be replaced by updating the topology onValidate.

using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using DataOrientedAudio.Voice.Runtime;
using System.Diagnostics;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class AudioTopologySystem : SystemBase
    {
        #region Fields

        private NativeList<AudioTopologyArchetype> _archetypes;
        private bool _isInitialized;

        #endregion

        #region Lifecycle

        protected override void OnCreate()
        {
            _archetypes = new NativeList<AudioTopologyArchetype>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (_archetypes.IsCreated)
                _archetypes.Dispose();
        }

        protected override void OnUpdate()
        {
            if (_isInitialized) return;

            var query = SystemAPI.QueryBuilder()
                .WithAll<VoiceBlobReference, VoiceLocalIndex>()
                .Build();

            if (query.IsEmpty) return;

            _archetypes.Clear();

            var voiceTypeData = GatherVoiceTypes();
            BuildTopologyArchetypes(voiceTypeData);
            AssignArchetypeIndices(voiceTypeData.SortedTypeIds);
            CreateOrUpdateSingleton();

            _isInitialized = true;

            DisposeTemporaryCollections(voiceTypeData);
        }

        #endregion

        #region Topology Building

        private VoiceTypeData GatherVoiceTypes()
        {
            var uniqueTypes = new NativeHashMap<int, BlobAssetReference<VoiceBlob>>(16, Allocator.Temp);
            var typeCounts = new NativeHashMap<int, int>(16, Allocator.Temp);

            foreach (var (blobRef, entity) in SystemAPI.Query<RefRO<VoiceBlobReference>>().WithAll<VoiceTypeID>().WithEntityAccess())
            {
                var typeID = EntityManager.GetSharedComponent<VoiceTypeID>(entity);
                if (!uniqueTypes.ContainsKey(typeID.Value))
                {
                    uniqueTypes.Add(typeID.Value, blobRef.ValueRO.Value);
                    typeCounts.Add(typeID.Value, 0);
                }
                typeCounts[typeID.Value]++;
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
            int currentStartIndex = 0;
            int currentArchetypeIndex = 0;

            for (int i = 0; i < voiceTypeData.SortedTypeIds.Length; i++)
            {
                int typeId = voiceTypeData.SortedTypeIds[i];
                var voiceBlob = voiceTypeData.UniqueTypes[typeId];
                int voiceCount = voiceTypeData.TypeCounts[typeId];

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
            for (int i = 0; i < sortedTypeIds.Length; i++)
            {
                int typeId = sortedTypeIds[i];
                int archetypeIndex = i;

                foreach (var (archIdx, entity) in SystemAPI.Query<RefRW<VoiceArchetypeIndex>>().WithAll<VoiceTypeID>().WithEntityAccess())
                {
                    var entityTypeID = EntityManager.GetSharedComponent<VoiceTypeID>(entity);
                    if (entityTypeID.Value == typeId)
                    {
                        archIdx.ValueRW.Value = archetypeIndex;
                    }
                }
            }
        }

        private void CreateOrUpdateSingleton()
        {
            int totalVoiceCount = 0;
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
                SystemAPI.SetSingleton(singleton);
            else
            {
                var singletonEntity = EntityManager.CreateEntity(typeof(AudioTopologySingleton));
                EntityManager.SetComponentData(singletonEntity, singleton);
            }
        }

        private void DisposeTemporaryCollections(VoiceTypeData voiceTypeData)
        {
            voiceTypeData.SortedTypeIds.Dispose();
            voiceTypeData.UniqueTypes.Dispose();
            voiceTypeData.TypeCounts.Dispose();
        }

        #endregion

        #region Public API

        public AudioTopologyData GetTopologyData()
        {
            if (!_isInitialized && _archetypes.Length == 0)
            {
                return new AudioTopologyData();
            }

            int totalVoiceCount = 0;
            if (_archetypes.Length > 0)
            {
                var lastArchetype = _archetypes[_archetypes.Length - 1];
                totalVoiceCount = lastArchetype.Start + lastArchetype.Count;
            }

            return new AudioTopologyData
            {
                MaxArchetypes = _archetypes.Length,
                TotalVoices = totalVoiceCount,
                Archetypes = _archetypes.AsArray()
            };
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
