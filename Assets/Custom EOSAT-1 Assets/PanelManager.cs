using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// The list of every panel in the app (welcome, position, map, menu ...) and what you can do with them:
/// find one by id, show/hide it, and drop it in front of the player.
/// </summary>
public static class PanelManager
{
    static readonly List<GrabbablePanel> panels = new List<GrabbablePanel>();
    static Transform head;

    public static IReadOnlyList<GrabbablePanel> All => panels;

    /// <summary>Fires whenever a panel is added, removed, shown or hidden (the Panels menu page listens to this).</summary>
    public static event Action Changed;

    /// <summary>The player's head: the XR Origin's camera (falls back to the main camera).</summary>
    public static Transform Head
    {
        get
        {
            if (head == null)
            {
                var xr = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
                if (xr != null && xr.Camera != null) head = xr.Camera.transform;
                else if (Camera.main != null) head = Camera.main.transform;
            }
            return head;
        }
    }

    /// <summary>
    /// "Up" for the player: the XR rig's own up direction. In the space view the rig is turned (and tilted) to look at
    /// things, so world-up is not the player's up there. Panels are levelled and placed using this.
    /// </summary>
    public static Vector3 Up
    {
        get
        {
            var xr = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            return xr != null && xr.Origin != null ? xr.Origin.transform.up : Vector3.up;
        }
    }

    /// <summary>Wait until the headset is tracking (a real head height that has stopped jumping), up to 10 s.</summary>
    public static IEnumerator WaitForHead()
    {
        while (Head == null) yield return null;

        float waited = 0f, steady = 0f;
        Vector3 last = Head.position;
        while (waited < 10f && steady < 1f)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
            bool tracked = Head.localPosition.y > 0.3f;
            bool still = (Head.position - last).sqrMagnitude < 0.0004f;
            steady = (tracked && still) ? steady + Time.unscaledDeltaTime : 0f;
            last = Head.position;
        }
    }

    public static void Register(GrabbablePanel p)
    {
        if (p != null && !panels.Contains(p)) { panels.Add(p); Changed?.Invoke(); }
    }

    public static void Unregister(GrabbablePanel p)
    {
        if (panels.Remove(p)) Changed?.Invoke();
    }

    /// <summary>Panels that start switched off in the scene never run Awake, so look for them by hand.</summary>
    public static void Refresh()
    {
        foreach (var p in UnityEngine.Object.FindObjectsByType<GrabbablePanel>(FindObjectsInactive.Include))
            Register(p);
    }

    public static GrabbablePanel Get(string id)
    {
        foreach (var p in panels) if (p != null && p.PanelId == id) return p;
        Refresh();
        foreach (var p in panels) if (p != null && p.PanelId == id) return p;
        return null;
    }

    public static bool IsVisible(string id)
    {
        var p = Get(id);
        return p != null && p.gameObject.activeSelf;
    }

    public static void SetVisible(string id, bool visible)
    {
        var p = Get(id);
        if (p == null || p.gameObject.activeSelf == visible) return;
        p.gameObject.SetActive(visible);
        Changed?.Invoke();
    }

    public static void Toggle(string id) => SetVisible(id, !IsVisible(id));

    /// <summary>Place a panel in front of the player at eye height, facing them. yawOffsetDegrees turns left (-) or right (+).</summary>
    public static void PlaceInFront(GrabbablePanel p, float distance = 1.4f, float yawOffsetDegrees = 0f, float heightOffset = -0.05f)
    {
        var h = Head;
        if (p == null || h == null) return;

        Vector3 up = Up;
        Vector3 flat = Vector3.ProjectOnPlane(h.forward, up);
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(h.up, up);        // looking straight up or down
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(Vector3.forward, up);
        Vector3 dir = Quaternion.AngleAxis(yawOffsetDegrees, up) * flat.normalized;

        Vector3 pos = h.position + dir * distance + up * heightOffset;
        p.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, up));
        Debug.Log("[Panels] placed '" + p.PanelId + "' at " + pos + " (head at " + h.position + ")");
    }

    static bool FindGlobe(out Vector3 center, out float radius)
    {
        center = Vector3.zero; radius = 0.25f;
        var gm = GameObject.Find("GlobeModel");
        if (gm == null)
        {
            var g = GameObject.Find("Globe");
            if (g == null) return false;
            center = g.transform.position;
            return true;
        }
        var r = gm.GetComponent<Renderer>();
        if (r != null)
        {
            center = r.bounds.center;
            Vector3 e = r.bounds.extents;
            radius = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
        }
        else { center = gm.transform.position; radius = 0.25f * gm.transform.lossyScale.x; }
        return true;
    }

    /// <summary>Place a panel beside the globe (side -1 = to the left as seen from the player, +1 = right), facing the player.</summary>
    public static bool PlaceBesideGlobe(GrabbablePanel p, float side, float gap)
    {
        var h = Head;
        if (p == null || h == null || !FindGlobe(out Vector3 center, out float radius)) return false;

        Vector3 toGlobe = center - h.position;
        toGlobe.y = 0f;
        if (toGlobe.sqrMagnitude < 0.01f) return false;
        Vector3 forward = toGlobe.normalized;
        Vector3 right = Quaternion.Euler(0f, 90f, 0f) * forward;

        float offset = radius + gap + p.WidthMetres * 0.5f;
        Vector3 pos = center + right * (Mathf.Sign(side) * offset);

        Vector3 away = pos - h.position;
        away.y = 0f;
        p.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(away.normalized, Vector3.up));
        Debug.Log("[Panels] placed '" + p.PanelId + "' beside the globe at " + pos);
        return true;
    }

    /// <summary>
    /// The layout after the welcome panel. Every panel with "In Default Layout" ticked is shown and placed using its own
    /// layout numbers. Panels with the ids "position" and "map" are also used (left and right) if they are not ticked.
    /// Panels that are already showing are not moved, so this is safe to call again.
    /// </summary>
    public static void ApplyDefaultLayout()
    {
        Refresh();
        foreach (var p in new List<GrabbablePanel>(panels))
        {
            if (p == null) continue;
            float yaw = p.LayoutYaw;
            bool use = p.InDefaultLayout;
            if (!use && p.PanelId == "position") { use = true; yaw = -32f; }
            if (!use && p.PanelId == "map") { use = true; yaw = 32f; }
            if (!use) continue;
            if (p.gameObject.activeInHierarchy) continue;      // already showing: leave it where the player put it
            p.gameObject.SetActive(true);
            bool beside = PanelSettings.Get().layoutBesideGlobe &&
                          PlaceBesideGlobe(p, yaw, PanelSettings.Get().layoutGlobeGap);
            if (!beside) PlaceInFront(p, p.LayoutDistance, yaw, p.LayoutHeight);
        }
        Changed?.Invoke();
    }
}
