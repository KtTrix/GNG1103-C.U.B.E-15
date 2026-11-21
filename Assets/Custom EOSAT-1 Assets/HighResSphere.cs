using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Swaps a sphere's mesh for a much smoother one. Unity's built-in sphere only has 24 x 16 segments, which shows as
/// flat facets on the outline in VR, and its triangles at the poles pull the texture into wedges.
///
/// The new mesh is a clean latitude / longitude grid. Every texture coordinate is calculated from the point's own
/// latitude and longitude, so the poles are exact. Before building, it measures how Unity's own sphere maps its texture
/// (which way round, where the seam is) and copies that, so the Earth, the clouds and the longitudes stay exactly where
/// they were.
///
/// Add it to any object that uses the built-in sphere (Earth, Clouds, Atmosphere, the hub's GlobeModel). SpaceWorld adds
/// it to the space view's spheres by itself.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class HighResSphere : MonoBehaviour
{
    [Range(0, 4)]
    [Tooltip("Detail level. 2 = 96 x 48 grid (about 9k triangles), 3 = 192 x 96 (about 37k, smooth in VR), 4 = 384 x 192 (about 147k).")]
    [SerializeField] int subdivisions = 3;

    static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();

    void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        if (subdivisions > 0) mf.sharedMesh = GetMesh(subdivisions);
    }

    /// <summary>The smooth sphere (radius 0.5, like Unity's), made once per detail level and shared.</summary>
    public static Mesh GetMesh(int levels)
    {
        if (Cache.TryGetValue(levels, out Mesh cached) && cached != null) return cached;

        // Learn how Unity's own sphere maps its texture: u = s * atan2(z, x) / 2pi + c.
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh src = tmp.GetComponent<MeshFilter>().sharedMesh;
        Calibrate(src, out float s, out float c, out bool vUp);
        Destroy(tmp);

        int L = 24 << levels;          // segments around
        int S = L / 2;                 // rows from pole to pole
        int cols = L + 1;              // one extra column so the texture seam has its own vertices

        var verts = new Vector3[(S + 1) * cols];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];

        for (int i = 0; i <= S; i++)
        {
            float lat = Mathf.PI * (0.5f - (float)i / S);                 // +90 degrees at the top
            float cosLat = Mathf.Cos(lat), sinLat = Mathf.Sin(lat);
            float v = 0.5f + (vUp ? lat : -lat) / Mathf.PI;
            bool pole = i == 0 || i == S;

            for (int j = 0; j <= L; j++)
            {
                float u = (float)j / L;
                float a = s * (u - c) * 2f * Mathf.PI;                    // the angle that gives this u
                Vector3 dir = new Vector3(cosLat * Mathf.Cos(a), sinLat, cosLat * Mathf.Sin(a));
                int idx = i * cols + j;
                verts[idx] = dir * 0.5f;
                normals[idx] = dir;
                // at a pole every column has its own copy of the point, with the u of the middle of its segment
                uvs[idx] = new Vector2(pole ? (j + 0.5f) / L : u, v);
            }
        }

        var tris = new List<int>(L * S * 6);
        for (int i = 0; i < S; i++)
        {
            for (int j = 0; j < L; j++)
            {
                int a = i * cols + j, b = a + 1, cc = a + cols, d = cc + 1;
                if (i == 0) { tris.Add(a); tris.Add(d); tris.Add(cc); }           // top row: a single triangle per segment
                else if (i == S - 1) { tris.Add(a); tris.Add(b); tris.Add(cc); }   // bottom row likewise
                else
                {
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(cc);
                }
            }
        }

        // Make sure the triangles face outward (Unity draws the clockwise side): test one near the equator.
        {
            int i = S / 2, j = 0;
            int a = i * cols + j, b = a + 1, d = a + cols + 1;
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[d] - verts[a]);
            Vector3 centre = (verts[a] + verts[b] + verts[d]) / 3f;
            if (Vector3.Dot(n, centre) < 0f)
                for (int k = 0; k < tris.Count; k += 3) { int t = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = t; }
        }

        var mesh = new Mesh { name = "HighResSphere" + levels };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();       // needed for the Earth's normal map
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);        // free the CPU copy
        Cache[levels] = mesh;
        return mesh;
    }

    /// <summary>Find which way Unity's sphere wraps its texture: u = s * atan2(z, x) / 2pi + c (s is +1 or -1).</summary>
    static void Calibrate(Mesh src, out float s, out float c, out bool vUp)
    {
        var v = src.vertices;
        var uv = src.uv;

        // some vertices away from the poles
        var pick = new List<int>();
        for (int i = 0; i < v.Length; i++)
            if (Mathf.Abs(v[i].y) < 0.35f) pick.Add(i);

        s = 1f; c = 0f;
        float best = float.MaxValue;
        foreach (float sign in new[] { 1f, -1f })
        {
            int i0 = pick[0];
            float a0 = Mathf.Atan2(v[i0].z, v[i0].x);
            float cc = uv[i0].x - sign * a0 / (2f * Mathf.PI);

            float err = 0f;
            foreach (int i in pick)
            {
                float a = Mathf.Atan2(v[i].z, v[i].x);
                float d = uv[i].x - (sign * a / (2f * Mathf.PI) + cc);
                d -= Mathf.Round(d);               // the seam wraps by whole turns
                err += Mathf.Abs(d);
            }
            if (err < best) { best = err; s = sign; c = cc; }
        }

        // is v larger towards the north pole?
        vUp = true;
        for (int i = 0; i < v.Length; i++)
            if (v[i].y > 0.3f) { vUp = uv[i].y > 0.5f; break; }
    }
}
