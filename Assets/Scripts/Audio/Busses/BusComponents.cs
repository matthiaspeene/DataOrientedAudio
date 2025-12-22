// Assets/Scripts/Audio/Runtime/Components/BusComponents.cs
using Unity.Entities;

namespace DataOrientedAudio.Busses
{
    /// <summary>
    /// Bus tag + ids (authoring/baker should set these).
    /// </summary>
    public struct Bus : IComponentData
    {
        public ushort Id;        // stable index into bus arrays
        public short ParentId;   // -1 for Master
    }

    /// <summary>
    /// Bus gain per channel (buffer element).
    /// </summary>
    public struct BusGain : IBufferElementData
    {
        public float Linear;        // linear 0..∞
    }

    /// <summary>
    /// Tag for the singleton entity that owns the BusGain buffer.
    /// </summary>
    public struct BusGainsSingleton : IComponentData { }

    /// <summary>
    /// Optional per-bus sends (kept POD-only; not used by the current mixer path).
    /// </summary>
    public struct BusSend : IBufferElementData
    {
        public short TargetBusId;   // destination bus
        public float Gain;          // linear
        public byte PreFader;       // 0 = post, 1 = pre
    }

    /// <summary>
    /// Readonly baked graph of buses (authoring-time → runtime blob).
    /// </summary>
    public struct BusGraphBlob
    {
        // Topology
        public BlobArray<short> Parent;        // length = busCount, Parent[i] = parent id (-1 for master)
        public BlobArray<ushort> PostOrder;    // children→...→root order (for accumulation)

        // Reference/defaults (optional)
        public BlobArray<BusRow> Buses;        // readonly defaults for reference
        public BlobArray<CategoryRoute> Routes;// optional: category → bus mapping
    }

    /// <summary>
    /// Compact per-bus row in the baked blob.
    /// </summary>
    public struct BusRow
    {
        public ushort BusId;
        public short OutBusId;          // -1 -> device
        public float OutGainDefault;    // linear
        public float LpfCutoffDefault;  // Hz
    }

    /// <summary>
    /// Optional mapping from content category hash → default bus.
    /// </summary>
    public struct CategoryRoute
    {
        public int CategoryHash; // hash of category string/enum
        public ushort BusId;
    }

    /// <summary>
    /// Singleton handle to the baked BusGraphBlob.
    /// </summary>
    public struct BusGraphRef : IComponentData
    {
        public BlobAssetReference<BusGraphBlob> Blob;
    }
}
