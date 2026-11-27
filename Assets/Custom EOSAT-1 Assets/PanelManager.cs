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
            var xr = Rig;
            return xr != null && xr.Origin != null ? xr.Origin.transform.up : Vector3.up;
        }
    }

    static XROrigin rigCache;
    static XROrigin Rig
    {
        get
        {
            if (rigCache == null) rigCache = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            return rigCache;
        }
    }

    // ── carrying panels with the rig ──────────────────────────────────────────
    // In the space view the rig is moved every frame to follow the satellite. Panels live in the world, so without this
    // they would be left behind. While FollowRig is on (ViewManager sets it for the space view) every panel moves with
    // the rig, keeping its place relative to you. In the hub it is off: walking around leaves panels where they are.
    public static bool FollowRig { get; set; }

    /// <summary>Raised when panels were carried along; the grabber moves its own remembered points by the same amount.</summary>
    public static event Action<Matrix4x4> RigMoved;

    static Matrix4x4 lastRig;
    static bool haveRig;

    /// <summary>Record the rig's current pose without carrying anything (used after panels were placed by hand).</summary>
    public static void MarkRig()
    {
        var xr = Rig;
        if (xr == null || xr.Origin == null) return;
        Transform t = xr.Origin.transform;
        lastRig = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        haveRig = true;
    }

    // ── a separate set of open panels for each view (hub, space) ───────────────
    // Leaving a view closes its panels and remembers which were open and where. Coming back puts them back exactly.
    // The menu is not part of any view: it stays open and comes with you.
    class Snap { public bool active, unlocked; public Vector3 pos, lpos; public Quaternion rot, lrot; }
    static readonly Dictionary<string, Dictionary<GrabbablePanel, Snap>> viewSnaps = new Dictionary<string, Dictionary<GrabbablePanel, Snap>>();

    static bool PerView(GrabbablePanel p) => p != null && p.PanelId != "menu";

    /// <summary>Remember this view's panels (open or not, and where) and close them. Space panels are remembered relative to the rig.</summary>
    public static void StoreView(string view)
    {
        Refresh();
        Transform rt = Rig != null && Rig.Origin != null ? Rig.Origin.transform : null;
        var d = new Dictionary<GrabbablePanel, Snap>();
        foreach (var p in new List<GrabbablePanel>(panels))
        {
            if (!PerView(p)) continue;
            Transform t = p.transform;
            var s = new Snap { active = p.gameObject.activeSelf, unlocked = p.Unlocked, pos = t.position, rot = t.rotation };
            if (rt != null) { s.lpos = rt.InverseTransformPoint(t.position); s.lrot = Quaternion.Inverse(rt.rotation) * t.rotation; }
            d[p] = s;
            p.gameObject.SetActive(false);
        }
        viewSnaps[view] = d;
        Changed?.Invoke();
    }

    /// <summary>Bring back the panels this view had open. A view that never had any starts with none. localToRig = true for space.</summary>
    public static void RestoreView(string view, bool localToRig)
    {
        Refresh();
        Transform rt = Rig != null && Rig.Origin != null ? Rig.Origin.transform : null;
        viewSnaps.TryGetValue(view, out var d);
        foreach (var p in new List<GrabbablePanel>(panels))
        {
            if (!PerView(p)) continue;
            if (d != null && d.TryGetValue(p, out Snap s))
            {
                if (localToRig && rt != null) p.transform.SetPositionAndRotation(rt.TransformPoint(s.lpos), rt.rotation * s.lrot);
                else p.transform.SetPositionAndRotation(s.pos, s.rot);
                p.Unlocked = s.unlocked;
                p.gameObject.SetActive(s.active);
            }
            else p.gameObject.SetActive(false);
        }
        MarkRig();                      // we placed them ourselves, so don't carry them again
        Physics.SyncTransforms();
        Changed?.Invoke();
    }

    /// <summary>Notice how far the rig moved since the last call and carry the panels by that much. Cheap; called often.</summary>
    public static void SyncRig()
    {
        var xr = Rig;
        if (xr == null || xr.Origin == null) return;
        Transform t = xr.Origin.transform;
        Matrix4x4 now = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

        if (haveRig && FollowRig)
        {
            Matrix4x4 d = now * lastRig.inverse;
            Quaternion dr = d.rotation;
            Vector3 dp = d.GetColumn(3);
            if (dp.sqrMagnitude > 1e-12f || Quaternion.Angle(dr, Quaternion.identity) > 0.0005f)
            {
                foreach (var p in panels)
                {
                    if (p == null) continue;
                    Transform pt = p.transform;
                    pt.SetPositionAndRotation(d.MultiplyPoint3x4(pt.position), dr * pt.rotation);
                }
                RigMoved?.Invoke(d);
                Physics.SyncTransforms();      // so the grab rays see the panels where they now are
            }
        }
        lastRig = now;
        haveRig = true;
    }

    /// <summary>Wait until the headset is tracking (a real head height that has stopped jumping), up to 10 s.</summary>
    public static IEnumerator WaitForHead()
    {
        while (Head == null) yield return null;

        // Turn a backwards start round as early as possible: as soon as the headset's real pose arrives (the position moves off its
        // first, default reading), not after the longer settle wait below.
        Vector3 first = Head.position;
        float early = 0f;
        while (early < 2f && (Head.position - first).sqrMagnitude < 0.0001f) { yield return null; early += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.15f);
        if (Head.localPosition.y > 0.3f) AlignStartFacing();

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
        AlignStartFacing();
    }

    static bool startAligned;

    /// <summary>
    /// Once, when the app starts: on the headset the tracking origin can point the opposite way to the scene (the log showed the head
    /// facing 180 degrees from the rig), so turn the rig around the player's head until they face the way the scene faces (the globe).
    /// </summary>
    static void AlignStartFacing()
    {
        if (startAligned) return;
        startAligned = true;
        var xr = Rig;
        if (xr == null || xr.Camera == null) return;
        Vector3 up = xr.Origin.transform.up;
        Vector3 want = Vector3.ProjectOnPlane(xr.Origin.transform.forward, up);
        Vector3 have = Vector3.ProjectOnPlane(xr.Camera.transform.forward, up);
        if (want.sqrMagnitude < 1e-4f || have.sqrMagnitude < 1e-4f) return;
        float angle = Vector3.SignedAngle(have, want, up);
        // Only fix a start that is clearly backwards. A smaller angle is just the player looking around, and must not be "corrected".
        if (Mathf.Abs(angle) < 120f) { Debug.Log("[PanelManager] start facing fine (" + angle.ToString("F0") + " degrees off), not turning"); return; }
        angle = 180f;
        Debug.Log("[PanelManager] start facing: turning the rig " + angle.ToString("F0") + " degrees");
        xr.RotateAroundCameraUsingOriginUp(angle);
        MarkRig();
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

    /// <summary>True while the welcome panel is on screen. The view / mixed-reality / next-view buttons do nothing then, so the player can try the controllers.</summary>
    public static bool WelcomeOpen
    {
        get { var p = Get("welcome"); return p != null && p.gameObject.activeInHierarchy; }
    }

    public static GrabbablePanel Get(string id)
    {
        foreach (var p in panels) if (p != null && p.PanelId == id) return p;
        Refresh();
        foreach (var p in panels) if (p != null && p.PanelId == id) return p;
        return null;
    }

    /// <summary>Tell the Panels page (and anything else listening) that something about the panels changed.</summary>
    public static void NotifyChanged() => Changed?.Invoke();

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
    /// Reset all (menu button): every panel in the default layout is opened and put back in its start place for this
    /// view, even if it was open somewhere else. Other panels are left alone.
    /// </summary>
    public static void ResetLayout()
    {
        Refresh();
        foreach (var p in new List<GrabbablePanel>(panels))
        {
            if (p == null || !PerView(p)) continue;
            bool use = p.InDefaultLayout || p.PanelId == "position" || p.PanelId == "map";
            if (use) ResetPanel(p);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Reset one panel (the Reset button on its row): it is opened and put back in its start place for this view, with
    /// its track unlocked and attached to the world again. Panels that aren't in the default layout go in front of you.
    /// </summary>
    public static void ResetPanel(GrabbablePanel p)
    {
        if (p == null) return;
        float yaw = p.LayoutYaw;
        bool use = p.InDefaultLayout;
        if (!use && p.PanelId == "position") { use = true; yaw = -32f; }
        if (!use && p.PanelId == "map") { use = true; yaw = 32f; }

        p.Unlocked = false;
        p.Attach = GrabbablePanel.AttachMode.World;
        p.ResetScale();                      // the L-corner scaling goes back to normal size too
        p.gameObject.SetActive(true);

        bool beside = use && PanelSettings.Get().layoutBesideGlobe &&
                      PlaceBesideGlobe(p, yaw, PanelSettings.Get().layoutGlobeGap);
        if (!beside)
        {
            if (use) PlaceInFront(p, p.LayoutDistance, yaw, p.LayoutHeight);
            else PlaceInFront(p, 1.5f, 0f, -0.05f);
        }
        Changed?.Invoke();
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
