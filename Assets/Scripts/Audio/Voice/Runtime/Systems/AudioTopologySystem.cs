using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Voice.Runtime.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class AudioTopologySystem : SystemBase
    {
        private NativeList<AudioTopologyArchetype> _archetypes;
        private bool _isInitialized;

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
            // We only need to run this when the topology changes (e.g. initial bake or scene load).
            // For now, we'll run it once and then only if we detect changes (which we won't for now).
            // Actually, since baking happens in edit mode/subscene, the entities are there on start.

            if (_isInitialized) return;

            // 1. Find all unique voice archetypes (VoiceBlobReference)
            // We want to group by VoiceBlobReference.

            // Query all entities with VoiceBlobReference
            var query = SystemAPI.QueryBuilder()
                .WithAll<VoiceBlobReference, VoiceLocalIndex>()
                .Build();

            if (query.IsEmpty) return;

            // Collect all unique blobs and their counts
            // Since we can't easily "Group By" in a simple query, we'll iterate.
            // But wait, we need to assign ArchetypeIndex to the entities too.

            // Let's gather all unique VoiceBlobReferences.
            // A simple way is to use a NativeHashMap to count voices per blob.

            var uniqueBlobs = new NativeHashMap<BlobAssetReference<VoiceBlob>, int>(16, Allocator.Temp);
            var blobToFirstEntity = new NativeHashMap<BlobAssetReference<VoiceBlob>, Entity>(16, Allocator.Temp);

            foreach (var (blobRef, entity) in SystemAPI.Query<RefRO<VoiceBlobReference>>().WithEntityAccess())
            {
                if (!uniqueBlobs.ContainsKey(blobRef.ValueRO.Value))
                {
                    uniqueBlobs.Add(blobRef.ValueRO.Value, 0);
                    blobToFirstEntity.Add(blobRef.ValueRO.Value, entity);
                }
                uniqueBlobs[blobRef.ValueRO.Value]++;
            }

            // Now build the topology
            _archetypes.Clear();
            int currentStart = 0;
            int archIndex = 0;

            // We need a stable order. The hashmap iteration order is not guaranteed stable across runs if hashes collide differently,
            // but for a small number of blobs it might be okay. To be deterministic, we should sort.
            // Sorting BlobAssetReferences is hard.
            // Alternative: Sort by the TypeID we baked? VoiceTypeID.

            // Let's redo the gathering using VoiceTypeID which is an int.
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

            // Get keys and sort them for stability
            var keys = uniqueTypes.GetKeyArray(Allocator.Temp);
            keys.Sort();

            for (int i = 0; i < keys.Length; i++)
            {
                int typeId = keys[i];
                var blob = uniqueTypes[typeId];
                int count = typeCounts[typeId];

                _archetypes.Add(new AudioTopologyArchetype
                {
                    ArchetypeIndex = archIndex,
                    Blob = blob,
                    Start = currentStart,
                    Count = count
                });

                // Assign VoiceArchetypeIndex to all entities of this type
                // We can do this with a query and a shared component filter, or just iterate and check.
                // Iterating all is fine for init.
                foreach (var (archIdx, entity) in SystemAPI.Query<RefRW<VoiceArchetypeIndex>>().WithAll<VoiceTypeID>().WithEntityAccess())
                {
                    var tID = EntityManager.GetSharedComponent<VoiceTypeID>(entity);
                    if (tID.Value == typeId)
                    {
                        archIdx.ValueRW.Value = archIndex;
                    }
                }

                currentStart += count;
                archIndex++;
            }

            // Create/Update Singleton
            var singleton = new AudioTopologySingleton
            {
                MaxArchetypes = _archetypes.Length,
                TotalVoices = currentStart
            };

            if (SystemAPI.HasSingleton<AudioTopologySingleton>())
                SystemAPI.SetSingleton(singleton);
            else
            {
                var e = EntityManager.CreateEntity(typeof(AudioTopologySingleton));
                EntityManager.SetComponentData(e, singleton);
            }

            _isInitialized = true;

            keys.Dispose();
            uniqueBlobs.Dispose();
            blobToFirstEntity.Dispose();
            uniqueTypes.Dispose();
            typeCounts.Dispose();
        }

        public AudioTopologyData GetTopologyData()
        {
            if (!_isInitialized && _archetypes.Length == 0)
            {
                // Force update if accessed before first update?
                // Or just return empty.
                return new AudioTopologyData();
            }

            int total = 0;
            if (_archetypes.Length > 0)
            {
                var last = _archetypes[_archetypes.Length - 1];
                total = last.Start + last.Count;
            }

            return new AudioTopologyData
            {
                MaxArchetypes = _archetypes.Length,
                TotalVoices = total,
                Archetypes = _archetypes.AsArray()
            };
        }
    }
}
