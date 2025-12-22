#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using DataOrientedAudio.Busses;
using DataOrientedAudio.Busses.Generated;
using DataOrientedAudio.Busses.Authoring;
using DataOrientedAudio.Common;

namespace DataOrientedAudio.Busses.Editor
{
    /// <summary>
    /// Bake BusGraphAsset → runtime:
    /// - One Bus entity per row (Id = serialized order, ParentId from parentGuid).
    /// - Add OutGain (linear) to each Bus.
    /// - Build BusGraphBlob (Parent[], PostOrder[], defaults, optional routes) and publish via BusGraphRef.
    /// </summary>
    public sealed class BusGraphBaker : Baker<BusGraphAuthoring>
    {
        public override void Bake(BusGraphAuthoring authoring)
        {
            var asset = authoring.graph;
            if (!asset || asset.buses == null || asset.buses.Length == 0) return;

            var buses = asset.buses;
            int count = buses.Length;

            // GUID → index (serialized order is canonical and matches enum generation)
            var guidToIndex = new Dictionary<string, int>(count);
            for (int i = 0; i < count; i++)
            {
                var g = string.IsNullOrEmpty(buses[i].guid) ? "" : buses[i].guid;
                if (!guidToIndex.ContainsKey(g)) guidToIndex.Add(g, i);
            }

            // Build rows + parent links
            var busRows = new NativeList<BusRow>(Allocator.Temp);
            var parent = new short[count];
            for (int i = 0; i < count; i++)
            {
                short p = -1;
                var pg = buses[i].parentGuid ?? string.Empty;
                if (!string.IsNullOrEmpty(pg) && guidToIndex.TryGetValue(pg, out var pIdx))
                    p = (short)pIdx;

                parent[i] = p;

                busRows.Add(new BusRow
                {
                    BusId = (ushort)i,
                    OutBusId = p,
                    OutGainDefault = buses[i].outGain,
                    LpfCutoffDefault = buses[i].lpfCutoffHz
                });
            }

            // Build children→...→root post-order once (baked)
            var postOrder = BuildPostOrder(parent, count);

            // Build blob
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<BusGraphBlob>();

                // Parent[]
                var arrParent = builder.Allocate(ref root.Parent, count);
                for (int i = 0; i < count; i++) arrParent[i] = parent[i];

                // PostOrder[]
                var arrPost = builder.Allocate(ref root.PostOrder, postOrder.Count);
                for (int i = 0; i < postOrder.Count; i++) arrPost[i] = (ushort)postOrder[i];

                // Buses[]
                var arrBuses = builder.Allocate(ref root.Buses, busRows.Length);
                for (int i = 0; i < busRows.Length; i++) arrBuses[i] = busRows[i];

                // Routes[]
                var routesSrc = asset.routes ?? System.Array.Empty<BusGraphAsset.CategoryRouteDef>();
                var arrRoutes = builder.Allocate(ref root.Routes, routesSrc.Length);
                for (int i = 0; i < routesSrc.Length; i++)
                {
                    arrRoutes[i] = new CategoryRoute
                    {
                        CategoryHash = StringHash.StableHash32(routesSrc[i].category),
                        BusId = (ushort)(guidToIndex.TryGetValue(routesSrc[i].busGuid, out var rid) ? rid : 0)
                    };
                }

                var blobRef = builder.CreateBlobAssetReference<BusGraphBlob>(Allocator.Persistent);
                AddBlobAsset(ref blobRef, out Unity.Entities.Hash128 _);

                var graphEntity = GetEntity(TransformUsageFlags.None);
                AddComponent(graphEntity, new BusGraphRef { Blob = blobRef });
            }

            // Create runtime Bus entities + OutGain
            for (int i = 0; i < count; i++)
            {
                var e = CreateAdditionalEntity(TransformUsageFlags.None);
                AddComponent(e, new Bus
                {
                    Id = (ushort)i,
                    ParentId = parent[i]
                });
                var gains = AddBuffer<BusGain>(e);
                float val = Mathf.Clamp(buses[i].outGain, 0f, 8f);
                gains.Add(new BusGain { Linear = val });
                gains.Add(new BusGain { Linear = val });
            }

            busRows.Dispose();
            DependsOn(asset);
        }

        // Deterministic children→...→root order with robustness for cycles/unreachable nodes
        static List<int> BuildPostOrder(short[] parent, int count)
        {
            var childCount = new int[count];
            for (int i = 0; i < count; i++)
            {
                int p = parent[i];
                if (p >= 0) childCount[p]++;
            }

            var order = new List<int>(count);
            var queue = new Stack<int>(count);
            for (int i = 0; i < count; i++) if (childCount[i] == 0) queue.Push(i);

            while (queue.Count > 0)
            {
                int b = queue.Pop();
                order.Add(b);
                int p = parent[b];
                if (p >= 0 && --childCount[p] == 0) queue.Push(p);
            }

            // Robustness: if we didn't visit all nodes (cycles/unreachable), append remaining deterministically
            if (order.Count < count)
            {
                var visited = new bool[count];
                for (int i = 0; i < order.Count; i++)
                    visited[order[i]] = true;

                // Append remaining indices in ascending order
                for (int i = 0; i < count; i++)
                {
                    if (!visited[i])
                        order.Add(i);
                }

                // Log warning about malformed graph
                UnityEngine.Debug.LogWarning($"BusGraphBaker: Detected cycles or unreachable nodes in bus graph. " +
                    $"Processed {order.Count - (count - order.Count)} nodes normally, appended {count - order.Count} remaining nodes deterministically.");
            }

            return order;
        }
    }
}
#endif
