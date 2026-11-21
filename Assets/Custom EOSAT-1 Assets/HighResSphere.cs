using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Swaps a sphere's mesh for a much smoother one. Unity's built-in sphere only has 24 x 16 segments, which shows as
/// flat facets on the outline in VR.
///
/// The new mesh is made by splitting every triangle of Unity's own sphere into four, again and again, and pushing the new
/// points out onto the sphere. Because it starts from Unity's sphere, the texture mapping is identical, so the Earth, the
/// clouds and the longitudes stay exactly where they were.
///
/// Add it to any object that uses the built-in sphere (Earth, Clouds, Atmosphere, the hub's GlobeModel). SpaceWorld adds
/// it to the space view's spheres by itself.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class HighResSphere : MonoBehaviour
{
    [Range(0, 4)]
    [Tooltip("How many times the triangles are split. 2 = about 12k triangles, 3 = about 49k (smooth in VR), 4 = about 200k.")]
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

        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh src = tmp.GetComponent<MeshFilter>().sharedMesh;
        var verts = new List<Vector3>(src.vertices);
        var uvs = new List<Vector2>(src.uv);
        var tris = new List<int>(src.triangles);
        Destroy(tmp);

        for (int level = 0; level < levels; level++)
        {
            var edgeMid = new Dictionary<long, int>();
            var next = new List<int>(tris.Count * 4);

            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (edgeMid.TryGetValue(key, out int found)) return found;
                Vector3 p = ((verts[a] + verts[b]) * 0.5f).normalized * 0.5f;     // back onto the sphere
                verts.Add(p);
                uvs.Add((uvs[a] + uvs[b]) * 0.5f);
                int idx = verts.Count - 1;
                edgeMid[key] = idx;
                return idx;
            }

            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.Add(a);  next.Add(ab); next.Add(ca);
                next.Add(b);  next.Add(bc); next.Add(ab);
                next.Add(c);  next.Add(ca); next.Add(bc);
                next.Add(ab); next.Add(bc); next.Add(ca);
            }
            tris = next;
        }

        var normals = new Vector3[verts.Count];
        for (int i = 0; i < normals.Length; i++) normals[i] = verts[i].normalized;

        var mesh = new Mesh { name = "HighResSphere" + levels };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();       // needed for the Earth's normal map
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);        // free the CPU copy
        Cache[levels] = mesh;
        return mesh;
    }
}
