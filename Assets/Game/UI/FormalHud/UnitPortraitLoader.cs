using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArknoNights.UI
{
    /// <summary>Adapts raw Texture2D unit portraits to cached Sprites for UGUI Image consumers.</summary>
    public static class UnitPortraitLoader
    {
        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Sprite Load(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            if (Cache.TryGetValue(resourcePath, out var cached))
            {
                return cached;
            }

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                Cache[resourcePath] = null;
                return null;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = texture.name;
            Cache[resourcePath] = sprite;
            return sprite;
        }
    }
}
