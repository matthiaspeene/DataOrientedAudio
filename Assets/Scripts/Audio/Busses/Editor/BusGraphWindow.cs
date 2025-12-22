#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using DataOrientedAudio.Common;


namespace DataOrientedAudio.Busses.Editor
{
    // UI for bus hierarchy authoring; runtime stays linear in bakers.
    public sealed class BusGraphWindow : EditorWindow
    {
        [Serializable]
        private class BusView
        {
            public string name = "NewBus";
            public int parentIndex = -1;     // -1 = Master (UI sentinel)
            public float uiGainDb = 0f;      // UI only (stored as linear in asset)
            public float uiLpfCutoffHz = 22000f;
            public string guid;              // stable id
        }

        [SerializeField] private UnityEngine.Object _asset;
        [SerializeField] private List<BusView> _buses = new();
        [SerializeField] private Vector2 _scroll;

        private ReorderableList _list;

        [MenuItem("Window/Audio/Bus Graph")]
        public static void Open()
        {
            var w = GetWindow<BusGraphWindow>("Bus Graph");
            w.minSize = new Vector2(640, 380);
            w.Show();
        }

        private void OnEnable()
        {
            BuildList();
            EnsureMaster();
        }

        private void BuildList()
        {
            _list = new ReorderableList(_buses, typeof(BusView), true, true, true, true);
            _list.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "Buses (top-down). Master is implicit; Parent = Master for roots.");
            };
            _list.onAddCallback = _ =>
            {
                _buses.Add(new BusView
                {
                    name = UniqueName("NewBus"),
                    parentIndex = -1,
                    uiGainDb = 0f,
                    uiLpfCutoffHz = 22000f,
                    guid = Guid.NewGuid().ToString("N")
                });
                EnsureMaster();
            };
            _list.onRemoveCallback = l =>
            {
                if (l.index <= 0) return; // Master not deletable
                if (l.index < 0 || l.index >= _buses.Count) return;

                int removedParent = _buses[l.index].parentIndex;
                for (int i = 0; i < _buses.Count; i++)
                {
                    if (_buses[i].parentIndex == l.index) _buses[i].parentIndex = removedParent;
                    else if (_buses[i].parentIndex > l.index) _buses[i].parentIndex--;
                }
                _buses.RemoveAt(l.index);
                EnsureMaster();
            };
            _list.onReorderCallback = _ => EnsureMaster();

            _list.elementHeight = EditorGUIUtility.singleLineHeight * 2f + 8f;

            _list.drawElementCallback = (rect, index, active, focused) =>
            {
                var item = _buses[index];
                float line = EditorGUIUtility.singleLineHeight;
                float pad = 2f;
                rect.height = line;
                float x = rect.x;
                float w = rect.width;

                // Row 1: Name | Parent | Gain (dB)
                var rName = new Rect(x, rect.y + pad, w * 0.34f, line);
                var rParent = new Rect(rName.xMax + 6, rect.y + pad, w * 0.28f, line);
                var rGain = new Rect(rParent.xMax + 6, rect.y + pad, w * 0.24f, line);

                if (index == 0)
                {
                    // Master: name locked, no parent
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUI.TextField(rName, "Master");
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUI.Popup(rParent, 0, new[] { "Master" });
                    item.name = "Master";
                    item.parentIndex = -1;
                }
                else
                {
                    item.name = EditorGUI.TextField(rName, item.name);

                    int parent = Mathf.Clamp(item.parentIndex, -1, _buses.Count - 1);
                    var choices = BuildParentNames(index);
                    parent = EditorGUI.Popup(rParent, parent + 1, choices) - 1;
                    if (parent == index) parent = -1;
                    if (CreatesCycle(index, parent)) parent = -1;
                    item.parentIndex = parent;
                }

                // Use DbMath range (no more hardcoded -60..12); min maps to linear=0 on save.
                item.uiGainDb = EditorGUI.Slider(
                    rGain,
                    new GUIContent("Gain (dB)"),
                    item.uiGainDb,
                    DbMath.DbMin,
                    DbMath.DbMax
                );

                // Row 2: LPF | GUID (readonly)
                var rLPF = new Rect(x, rName.yMax + pad, w * 0.24f, line);
                item.uiLpfCutoffHz = EditorGUI.FloatField(rLPF, new GUIContent("LPF Hz"), item.uiLpfCutoffHz <= 0 ? 10f : item.uiLpfCutoffHz);

                var rGuid = new Rect(rLPF.xMax + 6, rLPF.y, w - (rLPF.width + 6), line);
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(rGuid, "GUID", string.IsNullOrEmpty(item.guid) ? "(auto)" : item.guid);

                _buses[index] = item;
            };
        }

        private void OnGUI()
        {
            EnsureMaster();

            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    _asset = EditorGUILayout.ObjectField("Bus Graph Asset", _asset, typeof(ScriptableObject), false);
                    if (GUILayout.Button("New", GUILayout.Width(60))) CreateNewAsset();
                    if (GUILayout.Button("Load", GUILayout.Width(60))) LoadFromAsset();
                    if (GUILayout.Button("Save", GUILayout.Width(60))) SaveToAsset();
                }

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Master (implicit root). All buses ultimately route here.", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Add Bus", GUILayout.Width(90)))
                    {
                        _buses.Add(new BusView { name = UniqueName("NewBus"), parentIndex = -1, uiGainDb = 0f, uiLpfCutoffHz = 22000f, guid = Guid.NewGuid().ToString("N") });
                        EnsureMaster();
                    }
                }

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                _list.DoLayoutList();
                EditorGUILayout.EndScrollView();

                DrawPreviewTree();
            }
        }

        private void CreateNewAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create BusGraphAsset", "BusGraph", "asset", "");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance("BusGraphAsset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            _asset = asset;
            _buses.Clear();
            EnsureMaster();
            EditorUtility.SetDirty(this);
        }

        private void LoadFromAsset()
        {
            if (_asset == null) return;

            var so = new SerializedObject(_asset);
            var propBuses = so.FindProperty("buses");
            _buses.Clear();

            if (propBuses != null && propBuses.isArray)
            {
                var tmp = new List<(string name, string guid, string parentGuid, float outGainLinear, float lpfHz)>();
                for (int i = 0; i < propBuses.arraySize; i++)
                {
                    var el = propBuses.GetArrayElementAtIndex(i);
                    tmp.Add((
                        name: el.FindPropertyRelative("name")?.stringValue ?? "Unnamed",
                        guid: el.FindPropertyRelative("guid")?.stringValue ?? string.Empty,
                        parentGuid: el.FindPropertyRelative("parentGuid")?.stringValue ?? string.Empty,
                        outGainLinear: el.FindPropertyRelative("outGain")?.floatValue ?? 1f,
                        lpfHz: el.FindPropertyRelative("lpfCutoffHz")?.floatValue ?? 22000f
                    ));
                }

                // Normalize GUIDs and map
                var guidToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < tmp.Count; i++)
                {
                    var g = string.IsNullOrEmpty(tmp[i].guid) ? Guid.NewGuid().ToString("N") : tmp[i].guid;
                    guidToIndex[g] = i;
                    tmp[i] = (tmp[i].name, g, tmp[i].parentGuid, tmp[i].outGainLinear, tmp[i].lpfHz);
                }

                // Detect master guid
                string masterGuid = tmp.Count > 0 ? tmp[0].guid : "";
                for (int i = 0; i < tmp.Count; i++)
                    if (string.Equals(tmp[i].name, "Master", StringComparison.Ordinal) && string.IsNullOrEmpty(tmp[i].parentGuid))
                        masterGuid = tmp[i].guid;

                for (int i = 0; i < tmp.Count; i++)
                {
                    // Map parentGuid to UI index (-1 => Master)
                    int parentIdx = -1;
                    if (!string.IsNullOrEmpty(tmp[i].parentGuid))
                    {
                        if (tmp[i].parentGuid == masterGuid) parentIdx = -1;
                        else if (guidToIndex.TryGetValue(tmp[i].parentGuid, out var pi)) parentIdx = pi;
                    }

                    _buses.Add(new BusView
                    {
                        name = i == 0 ? (string.Equals(tmp[i].name, "Master", StringComparison.Ordinal) ? "Master" : tmp[i].name) : tmp[i].name,
                        parentIndex = i == 0 ? -1 : parentIdx,
                        uiGainDb = DbMath.LinearToDb(tmp[i].outGainLinear), // editor shows dB; 0 → DbMin
                        uiLpfCutoffHz = tmp[i].lpfHz <= 0 ? 22000f : tmp[i].lpfHz,
                        guid = tmp[i].guid
                    });
                }
            }

            EnsureMaster();
            BuildList();
            Repaint();
        }

        private void SaveToAsset()
        {
            if (_asset == null) return;

            EnsureMaster();

            // Ensure GUIDs (stable ids)
            for (int i = 0; i < _buses.Count; i++)
                if (string.IsNullOrEmpty(_buses[i].guid))
                    _buses[i].guid = Guid.NewGuid().ToString("N");

            var indexToGuid = _buses.Select(b => b.guid).ToArray();
            string masterGuid = indexToGuid.Length > 0 ? indexToGuid[0] : "";

            var so = new SerializedObject(_asset);
            var propBuses = so.FindProperty("buses");
            if (propBuses == null || !propBuses.isArray)
            {
                Debug.LogError("BusGraphAsset.buses field not found/array.");
                return;
            }

            propBuses.ClearArray();
            propBuses.arraySize = _buses.Count;
            for (int i = 0; i < _buses.Count; i++)
            {
                var el = propBuses.GetArrayElementAtIndex(i);

                // Master row invariants
                if (i == 0)
                {
                    _buses[0].name = "Master";
                    _buses[0].parentIndex = -1;
                }

                // parentGuid rules:
                //  - Master (i==0): empty => routes to device
                //  - Others: if UI parent == Master (-1) => masterGuid; else parent bus guid
                string parentGuid =
                    (i == 0) ? string.Empty :
                    (_buses[i].parentIndex == -1 ? masterGuid :
                     (_buses[i].parentIndex >= 0 && _buses[i].parentIndex < indexToGuid.Length ? indexToGuid[_buses[i].parentIndex] : masterGuid));

                SetString(el, "name", i == 0 ? "Master" : _buses[i].name);
                SetString(el, "guid", _buses[i].guid);
                SetString(el, "parentGuid", parentGuid);

                // Use DbMath for linear conversion; DbMin maps to EXACT 0.
                SetFloat(el, "outGain", DbMath.DbToLinear(_buses[i].uiGainDb));
                SetFloat(el, "lpfCutoffHz", Mathf.Max(10f, _buses[i].uiLpfCutoffHz));

                var sends = el.FindPropertyRelative("sends");
                if (sends != null && sends.isArray) { sends.ClearArray(); }
            }
            // TBA: Validate no cycles, all GUIDs unique, names nonempty/unique?

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
        }

        private string UniqueName(string baseName)
        {
            string n = baseName;
            int i = 1;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in _buses) used.Add(b.name);
            while (used.Contains(n)) n = $"{baseName}{i++}";
            return n;
        }

        private string[] BuildParentNames(int selfIndex)
        {
            var names = new List<string> { "Master" };
            for (int i = 0; i < _buses.Count; i++)
                names.Add(i == selfIndex ? "(self)" : _buses[i].name);
            return names.ToArray();
        }

        private bool CreatesCycle(int index, int newParent)
        {
            int cur = newParent;
            while (cur >= 0)
            {
                if (cur == index) return true;
                cur = _buses[cur].parentIndex;
            }
            return false;
        }

        private void EnsureMaster()
        {
            if (_buses.Count == 0)
            {
                _buses.Add(new BusView { name = "Master", parentIndex = -1, uiGainDb = 0f, uiLpfCutoffHz = 22000f, guid = Guid.NewGuid().ToString("N") });
                return;
            }

            int idx = _buses.FindIndex(b => string.Equals(b.name, "Master", StringComparison.Ordinal) && b.parentIndex == -1);
            if (idx == -1)
            {
                _buses.Insert(0, new BusView { name = "Master", parentIndex = -1, uiGainDb = 0f, uiLpfCutoffHz = 22000f, guid = Guid.NewGuid().ToString("N") });
            }
            else if (idx != 0)
            {
                var m = _buses[idx];
                _buses.RemoveAt(idx);
                _buses.Insert(0, m);
            }

            _buses[0].name = "Master";
            _buses[0].parentIndex = -1;
        }

        private void DrawPreviewTree()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                void DrawNode(string label, int idx, int depth)
                {
                    var indent = new string(' ', depth * 2);
                    EditorGUILayout.LabelField($"{indent}- {label}");
                    for (int i = 0; i < _buses.Count; i++)
                        if (_buses[i].parentIndex == idx)
                            DrawNode(_buses[i].name, i, depth + 1);
                }
                DrawNode("Master", -1, 0);
            }
        }

        // --- SerializedProperty helpers (editor-only) ---
        private static void SetString(SerializedProperty el, string name, string v)
        {
            var p = el.FindPropertyRelative(name);
            if (p != null) p.stringValue = v;
        }
        private static void SetFloat(SerializedProperty el, string name, float v)
        {
            var p = el.FindPropertyRelative(name);
            if (p != null) p.floatValue = v;
        }
    }
}
#endif
