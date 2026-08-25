using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates the handful of simple sprites the menu needs, so the main menu ships with no
/// image assets to import, wire or keep in sync. Everything is cached by size: the backdrop
/// spawns many circles but only ever pays for one texture per distinct resolution.
///
/// Only circles today. If the menu ever needs a rounded rect or a ring, add it here rather
/// than generating textures inline in a MonoBehaviour.
/// </summary>
public static class UISpriteFactory
{
    private static readonly Dictionary<int, Sprite> CircleCache = new Dictionary<int, Sprite>();

    /// <summary>
    /// A white filled circle, <paramref name="size"/> pixels across, with a one-pixel
    /// anti-aliased edge. Tint it through <see cref="UnityEngine.UI.Image.color"/>.
    /// </summary>
    public static Sprite Circle(int size = 256)
    {
        size = Mathf.Clamp(size, 8, 1024);

        if (CircleCache.TryGetValue(size, out Sprite cached) && cached != null)
        {
            return cached;
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = $"UISpriteFactory_Circle_{size}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            // The menu owns these for the lifetime of the play session; nothing else should
            // be able to unload them out from under the backdrop.
            hideFlags = HideFlags.HideAndDontSave
        };

        float centre = (size - 1) * 0.5f;
        float radius = centre;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                // Fade across the outermost pixel so the edge is not a staircase.
                float alpha = Mathf.Clamp01(radius - distance);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = texture.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;

        CircleCache[size] = sprite;
        return sprite;
    }
}
