using UnityEngine;
using DataOrientedAudio.Busses;

namespace DataOrientedAudio.Busses.Authoring
{
    // Scene hook to reference a BusGraphAsset; baker reads this at edit time.
    public sealed class BusGraphAuthoring : MonoBehaviour
    {
        public BusGraphAsset graph;
    }
}
