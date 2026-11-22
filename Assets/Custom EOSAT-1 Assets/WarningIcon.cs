using UnityEngine;

/// <summary>A warning triangle with an exclamation mark, drawn in code so there is no image file to import.</summary>
public static class WarningIcon
{
    static Texture2D tex;
    static Sprite sprite;

    public static Texture2D Texture
    {
        get { if (tex == null) tex = Make(); return tex; }
    }

    public static Sprite Sprite
    {
        get
        {
            if (sprite == null) sprite = UnityEngine.Sprite.Create(Texture, new Rect(0, 0, Texture.width, Texture.height), new Vector2(0.5f, 0.5f), 100f);
            return sprite;
        }
    }

    static float Edge(float d, float soft) => Mathf.Clamp01(0.5f - d / soft);   // 1 inside, 0 outside, soft edge

    static Texture2D Make()
    {
        const int n = 128;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        Color amber = new Color(1f, 0.72f, 0.08f), dark = new Color(0.12f, 0.06f, 0.02f), rim = Color.white;

        // triangle with its tip up; the signed distance to its three sides
        Vector2 a = new Vector2(0.5f, 0.93f), b = new Vector2(0.05f, 0.14f), c = new Vector2(0.95f, 0.14f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                float d = Mathf.Max(Side(p, a, b), Mathf.Max(Side(p, b, c), Side(p, c, a)));   // < 0 inside, in uv units
                float px1 = d * n;                                                              // in pixels
                float inside = Edge(px1, 1.5f);
                float core = Edge(px1 + 7f, 1.5f);                                              // inside the white border

                // the exclamation mark: a bar and a dot
                float bar = Edge(Mathf.Max(Mathf.Abs(p.x - 0.5f) - (0.045f + 0.03f * Mathf.InverseLerp(0.62f, 0.34f, p.y)), Mathf.Max(p.y - 0.62f, 0.34f - p.y)) * n, 1.5f);
                float dot = Edge((Vector2.Distance(p, new Vector2(0.5f, 0.26f)) - 0.05f) * n, 1.5f);
                float mark = Mathf.Max(bar, dot);

                Color col = Color.Lerp(rim, amber, core);
                col = Color.Lerp(col, dark, mark * core);
                px[y * n + x] = new Color(col.r, col.g, col.b, inside);
            }
        t.SetPixels32(px);
        t.Apply(true, true);
        return t;
    }

    // signed distance (uv units) from p to the edge a->b, negative on the left of a counter-clockwise triangle's inside
    static float Side(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 e = b - a;
        Vector2 n = new Vector2(e.y, -e.x).normalized;      // outward normal for the winding a (top), b (bottom-left), c (bottom-right)
        return Vector2.Dot(p - a, n);
    }
}
