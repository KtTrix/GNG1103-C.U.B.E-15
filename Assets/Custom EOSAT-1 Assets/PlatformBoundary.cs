using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// An invisible wall around the edge of the hub platform, so you cannot walk or glide off it into the void.
/// Starts itself when the app starts: it looks down from where you stand, finds the platform you are standing on, and builds
/// the wall around its edge (a ring for a round platform, four sides for a square one). The wall is kept under HubContent, so
/// it goes away with the hub when you are in the space view.
/// </summary>
public class PlatformBoundary : MonoBehaviour
{
    const float WallHeight = 3f;        // metres above the floor
    const float WallThickness = 0.3f;
    const float Inset = 0.10f;          // how far inside the platform's edge the wall's inner face sits

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<PlatformBoundary>() != null) return;
        new GameObject("PlatformBoundary").AddComponent<PlatformBoundary>();
    }

    IEnumerator Start()
    {
        yield return PanelManager.WaitForHead();            // the rig is in place
        var rig = FindAnyObjectByType<XROrigin>();
        Vector3 from = (rig != null ? rig.Origin.transform.position : Vector3.zero) + Vector3.up * 1f;
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning("[PlatformBoundary] Found no floor under the player, so no boundary was built.");
            yield break;
        }
        Build(hit.collider);
    }

    void Build(Collider floor)
    {
        var rend = floor.GetComponent<Renderer>();
        Bounds b = rend != null ? rend.bounds : floor.bounds;           // the platform as you see it
        var mf = floor.GetComponent<MeshFilter>();
        bool round = mf != null && mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Cylinder");

        var root = new GameObject("Platform Boundary").transform;
        var hub = GameObject.Find("HubContent");
        if (hub != null) root.SetParent(hub.transform, true);

        float top = b.max.y;
        Vector3 c = new Vector3(b.center.x, top, b.center.z);

        if (round)
        {
            float r = Mathf.Min(b.extents.x, b.extents.z);
            int n = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * r / 0.8f), 24, 96);
            float len = 2f * Mathf.PI * r / n * 1.2f;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 pos = c + radial * (r - Inset + WallThickness * 0.5f) + Vector3.up * (WallHeight * 0.5f);
                Wall(root, pos, Quaternion.LookRotation(radial), new Vector3(len, WallHeight, WallThickness));
            }
            Debug.Log("[PlatformBoundary] round wall, radius " + r.ToString("F2") + " m, around " + floor.name);
        }
        else
        {
            Vector3 e = b.extents;
            float y = WallHeight * 0.5f;
            float t = WallThickness, o = t * 0.5f - Inset;
            Wall(root, c + new Vector3(0f, y, e.z + o), Quaternion.identity, new Vector3(e.x * 2f + t * 2f, WallHeight, t));
            Wall(root, c + new Vector3(0f, y, -e.z - o), Quaternion.identity, new Vector3(e.x * 2f + t * 2f, WallHeight, t));
            Wall(root, c + new Vector3(e.x + o, y, 0f), Quaternion.identity, new Vector3(t, WallHeight, e.z * 2f + t * 2f));
            Wall(root, c + new Vector3(-e.x - o, y, 0f), Quaternion.identity, new Vector3(t, WallHeight, e.z * 2f + t * 2f));
            Debug.Log("[PlatformBoundary] square wall around " + floor.name);
        }
    }

    static void Wall(Transform parent, Vector3 pos, Quaternion rot, Vector3 size)
    {
        var go = new GameObject("Wall");
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, rot);
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
    }
}
