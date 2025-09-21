using System.Collections.Generic;
using UnityEngine;

namespace Bitsets
{
    public class TagRegistry : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField] private List<string> indexToName = new();  // 0..N-1，只追加
        [SerializeField] private bool ignoreCase = false;
        [SerializeField] private bool trimWhitespace = true;
        [SerializeField] private bool freezeAppend = false;         // 发版前锁表（可选）

        private Dictionary<string, int> nameToIndex;

        public IReadOnlyList<string> IndexToName => indexToName;
        public int Count => indexToName?.Count ?? 0;

        public bool TryGetIndex(string tag, out int idx)
        {
            if (!Normalize(ref tag)) { idx = -1; return false; }
            EnsureMap();
            return nameToIndex.TryGetValue(tag, out idx);
        }

#if UNITY_EDITOR
        public int TryGetOrAdd(string tag)
        {
            if (!Normalize(ref tag)) return -1;
            EnsureMap();
            if (nameToIndex.TryGetValue(tag, out var i)) return i;

            if (freezeAppend)
            {
                Debug.LogError($"[{name}] freezeAppend=true，禁止新增 tag：\"{tag}\"");
                return -1;
            }

            indexToName.Add(tag);
            int idx = indexToName.Count - 1;
            nameToIndex[tag] = idx;
            UnityEditor.EditorUtility.SetDirty(this);
            return idx;
        }
#endif

        private void EnsureMap()
        {
            if (nameToIndex != null) return;
            var cmp = ignoreCase ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;
            nameToIndex = new Dictionary<string, int>(cmp);
            for (int i = 0; i < indexToName.Count; i++)
            {
                var s = indexToName[i];
                if (Normalize(ref s) && !nameToIndex.ContainsKey(s)) nameToIndex.Add(s, i);
            }
        }

        private bool Normalize(ref string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (trimWhitespace) s = s.Trim();
            return !string.IsNullOrEmpty(s);
        }

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { nameToIndex = null; }
    }
}
