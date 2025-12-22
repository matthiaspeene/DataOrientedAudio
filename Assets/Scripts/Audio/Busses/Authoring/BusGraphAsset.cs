#if UNITY_EDITOR
using System;
using UnityEngine;

namespace DataOrientedAudio.Busses.Authoring
{
    // Authoring asset; UI shows dB, asset stores linear for runtime bake.
    [CreateAssetMenu(menuName = "Audio/Bus Graph", fileName = "BusGraph")]
    public sealed class BusGraphAsset : ScriptableObject
    {
        public BusDef[] buses;                 // tree rows (each has parentGuid)
        public CategoryRouteDef[] routes;      // optional category → bus mapping

        [Serializable]
        public struct BusDef
        {
            public string name;                // display name
            public string guid;                // stable string GUID
            public string parentGuid;          // "" -> Master
            public float outGain;              // linear (stored), editor shows dB
            public float lpfCutoffHz;          // Hz
            // public BusSendDef[] sends;
        }

        // [Serializable] public struct BusSendDef { public string targetGuid; public float gain; public bool preFader; }
        [Serializable] public struct CategoryRouteDef { public string category; public string busGuid; }
    }
}
#endif
