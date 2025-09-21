using System;
using UnityEngine;

namespace Bitsets
{
    [Serializable]
    public struct TagMask
    {
        [SerializeField] private ulong[] bits;

        public ReadOnlySpan<ulong> Bits => bits;
        public bool HasIndex(int index) => BitSet64.Get(bits, index);

#if UNITY_EDITOR
        public void SetByIndex(int index, bool value = true) => BitSet64.Set(ref bits, index, value);
        public void Clear() => BitSet64.ClearAll(ref bits);
        public void SetTag(string tag, TagRegistry registry, bool value = true)
        {
            if (registry == null || string.IsNullOrWhiteSpace(tag)) return;
            int idx = registry.TryGetOrAdd(tag.Trim());
            if (idx >= 0) SetByIndex(idx, value);
        }
#endif

        public bool HasTag(string tag, TagRegistry registry)
            => registry != null && registry.TryGetIndex(tag, out int idx) && HasIndex(idx);
    }
}
