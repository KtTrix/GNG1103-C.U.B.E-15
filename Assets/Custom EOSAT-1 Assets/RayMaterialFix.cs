using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes the controller rays show in mixed reality. The template's ray material leaves the frame's alpha near 0 on the
/// empty passthrough background, so the headset treats the ray as "not there" (it only shows over panels, which are
/// solid). This swaps the ray lines' material for one that writes alpha properly. Creates itself; nothing to place.
/// </summary>
public class RayMaterialFix : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<RayMaterialFix>() != null) return;
        var go = new GameObject("RayMaterialFix");
        DontDestroyOnLoad(go);
        go.AddComponent<RayMaterialFix>();
    }

    Material mat;
    readonly HashSet<LineRenderer> done = new HashSet<LineRenderer>();
    float next;

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 1f;

        if (mat == null)
        {
            var sh = Shader.Find("CUBE15/RayUnlit");
            if (sh == null) { enabled = false; return; }
            mat = new Material(sh);
        }

        foreach (var lr in FindObjectsByType<LineRenderer>(FindObjectsInactive.Include))
        {
            if (lr == null || done.Contains(lr)) continue;
            if (!IsRay(lr.transform)) continue;
            lr.sharedMaterial = mat;
            done.Add(lr);
        }
        done.RemoveWhere(l => l == null);
    }

    // a line that belongs to a controller's interactor (named like "Near-Far Interactor", "Left Controller", "LineVisual")
    static bool IsRay(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            string n = p.name;
            if (n.IndexOf("Interactor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("LineVisual", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }
}
