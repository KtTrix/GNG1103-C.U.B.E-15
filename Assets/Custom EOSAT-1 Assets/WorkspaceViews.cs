using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using ViewKind = SpaceViewController.ViewKind;

/// <summary>One live view of space shown on a Space View panel: its own camera, its own picture, its own point of view.</summary>
public class ViewSlot
{
    public ViewKind Kind;
    public float Dist = 1f, Yaw, Pitch;
    public int Station;

    public Camera Cam;
    public RenderTexture Tex;
    public bool HasImage;

    int seenFrame = -100;
    /// <summary>The tile calls this every frame it is on screen and in front of the player.</summary>
    public void Seen() { seenFrame = Time.frameCount; }
    public bool Visible => Time.frameCount - seenFrame <= 2;

    internal bool inertialCaptured, bodyInit;
    internal Vector3 inertialOffsetDir;
    internal Quaternion inertialRot = Quaternion.identity, smoothedBody = Quaternion.identity;
    internal int seenJumps;
    internal float lastPose;
    internal bool pendingStart;      // the starting distance is set once the space scene is there to ask
    internal Skybox sky;
    internal Material skyMat;
}

/// <summary>
/// The live views for the Space View panels. Every view is a camera that draws the space world into a texture, so the player can
/// stay in the hub and still watch the satellite from several viewpoints.
///
///   * At most 4 views at once. With 3-4 on screen they draw smaller and less often; a view nobody is looking at stops drawing
///     (its last picture stays); the view being edited always draws every frame.
///   * Edit mode: point at a view and press the "Edit hovered view" button (Options > Controls > Button mapping; it starts
///     unassigned). Then: RIGHT stick up/down zooms, left/right turns around the subject; LEFT stick up/down tilts, left/right
///     (ground station view) changes station; A / B change which view it is. Press Done on the tile to stop.
///
/// Created on demand by the first Space View panel. The space scene is kept loaded in the background (by ViewManager) while a
/// Space View panel is open in the hub.
/// </summary>
public class WorkspaceViews : MonoBehaviour
{
    public const int MaxViews = 4;
    public const float Aspect = 1.6f;

    static WorkspaceViews inst;
    public static bool Exists => inst != null;
    public static WorkspaceViews Instance
    {
        get
        {
            if (inst == null)
            {
                var go = new GameObject("Space View Cameras");
                var vm = ViewManager.Instance;
                if (vm != null) go.transform.SetParent(vm.transform, false);
                inst = go.AddComponent<WorkspaceViews>();
            }
            return inst;
        }
    }

    readonly List<ViewSlot> slots = new List<ViewSlot>();
    readonly HashSet<Camera> viewCams = new HashSet<Camera>();
    SpaceWorld world;
    SpaceViewController ctl;
    float nextFind;
    int users;
    bool stationLatched;

    public int Count => slots.Count;
    public bool CanAdd => slots.Count < MaxViews;
    public ViewSlot Editing { get; private set; }
    public ViewSlot Hovered { get; set; }
    public bool SpaceReady { get; private set; }

    InputAction stickR, stickL, prevA, nextA;

    // ── life ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        inst = this;
        stickR = new InputAction("WsRight", InputActionType.Value, expectedControlType: "Vector2");
        stickR.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        stickL = new InputAction("WsLeft", InputActionType.Value, expectedControlType: "Vector2");
        stickL.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        prevA = new InputAction("WsPrev", InputActionType.Button);
        ControlMap.Apply(prevA, "prevview", null);
        nextA = new InputAction("WsNext", InputActionType.Button);
        ControlMap.Apply(nextA, "nextview", null);
        stickR.Enable(); stickL.Enable(); prevA.Enable(); nextA.Enable();
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
    }

    void OnDestroy()
    {
        PanelLocomotionLock.ForceHold = false;
        foreach (var a in new[] { stickR, stickL })
            if (a != null) { a.Disable(); a.Dispose(); }
        foreach (var a in new[] { prevA, nextA })
            if (a != null) { ControlMap.Unbind(a); a.Disable(); a.Dispose(); }
        foreach (var s in new List<ViewSlot>(slots)) Remove(s);
        if (inst == this) inst = null;
    }

    void OnBeginCamera(ScriptableRenderContext c, Camera cam) { if (viewCams.Contains(cam)) SpaceCam.RaisePreRender(cam); }
    void OnEndCamera(ScriptableRenderContext c, Camera cam) { if (viewCams.Contains(cam)) SpaceCam.EndRender(); }

    // ── panels ask for the space scene while they are open ───────────────────
    public void Retain()
    {
        users++;
        if (ViewManager.Instance != null) ViewManager.Instance.WantBackgroundSpace(true);
    }

    public void Release()
    {
        users = Mathf.Max(0, users - 1);
        if (users == 0 && ViewManager.Instance != null) ViewManager.Instance.WantBackgroundSpace(false);
    }

    // ── slots ────────────────────────────────────────────────────────────────
    public ViewSlot Add(ViewKind kind)
    {
        if (!CanAdd) return null;
        var s = new ViewSlot();
        slots.Add(s);
        SetKind(s, kind);
        return s;
    }

    public void Remove(ViewSlot s)
    {
        if (s == null) return;
        if (Editing == s) SetEditing(null);
        if (Hovered == s) Hovered = null;
        slots.Remove(s);
        if (s.Cam != null) { viewCams.Remove(s.Cam); Destroy(s.Cam.gameObject); }
        if (s.Tex != null) { s.Tex.Release(); Destroy(s.Tex); s.Tex = null; }
    }

    public void SetKind(ViewSlot s, ViewKind kind)
    {
        s.Kind = kind;
        s.Yaw = 0f; s.Pitch = 0f;
        s.inertialCaptured = false; s.bodyInit = false;
        if (ctl == null) { s.Dist = 1f; s.pendingStart = true; }
        else { Limits(kind, out _, out _, out float start); s.Dist = start; s.pendingStart = false; }
        if (kind == ViewKind.GroundStation && world != null && world.State != null && world.State.InContact && ctl != null)
            s.Station = Mathf.Max(0, ctl.FindStation(world.State.GroundStation));
    }

    /// <summary>The next view that is in the roster, in the given direction.</summary>
    public ViewKind NextKind(ViewKind from, int dir)
    {
        int n = Enum.GetValues(typeof(ViewKind)).Length;
        int i = (int)from;
        for (int k = 0; k < n; k++)
        {
            i = (i + dir + n) % n;
            if (ctl == null || ctl.IsIncluded((ViewKind)i)) return (ViewKind)i;
        }
        return from;
    }

    /// <summary>A view kind that is not on show yet (for the + button), if there is one.</summary>
    public ViewKind UnusedKind()
    {
        int n = Enum.GetValues(typeof(ViewKind)).Length;
        for (int i = 0; i < n; i++)
        {
            var k = (ViewKind)i;
            if (ctl != null && !ctl.IsIncluded(k)) continue;
            bool used = false;
            foreach (var s in slots) if (s.Kind == k) { used = true; break; }
            if (!used) return k;
        }
        return ViewKind.Overview;
    }

    public string LabelOf(ViewSlot s)
    {
        switch (s.Kind)
        {
            case ViewKind.Overview: return "Overview";
            case ViewKind.OrbitFollow: return "Orbit follow";
            case ViewKind.InertialFollow: return "Inertial follow";
            case ViewKind.BodyFixed: return "Body fixed";
            case ViewKind.Sun: return "Sun view";
            default: return ctl != null && ctl.StationCount > 0 ? "Ground: " + ctl.StationName(s.Station) : "Ground station";
        }
    }

    // ── editing ──────────────────────────────────────────────────────────────
    public void SetEditing(ViewSlot s)
    {
        Editing = s;
        PanelLocomotionLock.ForceHold = s != null;
        stationLatched = true;
    }

    public void ToggleEdit(ViewSlot s) => SetEditing(Editing == s ? null : s);

    /// <summary>A tile went away (its panel closed): stop editing it.</summary>
    public void TileGone(ViewSlot s)
    {
        if (Editing == s) SetEditing(null);
        if (Hovered == s) Hovered = null;
    }

    // ── per frame ────────────────────────────────────────────────────────────
    bool Ready()
    {
        var vm = ViewManager.Instance;
        if (vm == null || vm.Current != ViewManager.View.Hub || vm.Busy) return false;
        if ((world == null || ctl == null) && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 0.5f;
            if (world == null) world = FindAnyObjectByType<SpaceWorld>();
            if (ctl == null) ctl = FindAnyObjectByType<SpaceViewController>();
        }
        return world != null && world.HasData;
    }

    void LateUpdate()
    {
        SpaceReady = Ready();
        if (!SpaceReady)
        {
            if (Editing != null) SetEditing(null);
            foreach (var s in slots) if (s.Cam != null) s.Cam.enabled = false;
            return;
        }

        float dt = Time.unscaledDeltaTime;
        HandleEdit(dt);

        int visible = 0;
        foreach (var s in slots) if (s.Visible) visible++;
        int width = visible <= 2 ? 960 : 720;           // 3-4 views on screen: smaller pictures

        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            EnsureCamera(s);
            if (!s.Visible) { s.Cam.enabled = false; continue; }          // nobody is looking: keep the last picture

            int interval = s == Editing ? 1 : (visible <= 2 ? 2 : 3);
            bool due = !s.HasImage || s.Tex == null || (Time.frameCount + i) % interval == 0;
            if (!due) { s.Cam.enabled = false; continue; }

            EnsureTexture(s, s == Editing ? Mathf.Max(width, 960) : width);
            float pdt = Mathf.Clamp(Time.unscaledTime - s.lastPose, 0.001f, 0.25f);
            s.lastPose = Time.unscaledTime;
            UpdatePose(s, pdt);
            s.Cam.enabled = true;
            s.HasImage = true;
        }
    }

    void EnsureCamera(ViewSlot s)
    {
        if (s.Cam == null)
        {
            var go = new GameObject("View Camera");
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<Camera>();
            cam.cullingMask = SpaceCam.Mask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.002f;
            cam.farClipPlane = 12000f;      // the Sun disc is drawn far away
            cam.allowHDR = false;
            cam.allowMSAA = true;           // smooth edges on the thin trail lines
            cam.useOcclusionCulling = false;
            cam.depth = -50f;
            cam.enabled = false;
            s.Cam = cam;
            s.sky = go.AddComponent<Skybox>();
            viewCams.Add(cam);
        }
        var vm = ViewManager.Instance;
        Material sky = vm != null ? vm.SpaceSkybox : null;
        if (s.skyMat != sky)
        {
            s.skyMat = sky;
            s.sky.material = sky;
            s.Cam.clearFlags = sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        }
    }

    void EnsureTexture(ViewSlot s, int width)
    {
        int height = Mathf.RoundToInt(width / Aspect);
        if (s.Tex != null && s.Tex.width == width && s.Tex.height == height) return;
        if (s.Tex != null) { s.Cam.targetTexture = null; s.Tex.Release(); Destroy(s.Tex); }
        s.Tex = new RenderTexture(width, height, 24) { antiAliasing = 1, useMipMap = false, name = "Workspace View" };
        s.Cam.targetTexture = s.Tex;
    }

    // ── edit mode input ──────────────────────────────────────────────────────
    void HandleEdit(float dt)
    {
        var s = Editing;
        if (s == null || ControlMap.Busy) return;
        Limits(s.Kind, out float lo, out float hi, out _);

        Vector2 r = stickR.ReadValue<Vector2>();
        Vector2 l = stickL.ReadValue<Vector2>();

        float mag = Mathf.Abs(r.y);
        if (mag > 0.12f)
        {
            float t = (mag - 0.12f) / 0.88f;
            s.Dist *= Mathf.Exp(-Mathf.Sign(r.y) * t * t * 1.5f * dt);
            s.Dist = Mathf.Clamp(s.Dist, lo, hi);
        }
        float turn = Mathf.Abs(r.x);
        if (turn > 0.15f) s.Yaw += Mathf.Sign(r.x) * ((turn - 0.15f) / 0.85f) * 70f * dt;

        float tilt = Mathf.Abs(l.y);
        if (tilt > 0.15f) s.Pitch = Mathf.Clamp(s.Pitch + Mathf.Sign(l.y) * ((tilt - 0.15f) / 0.85f) * 50f * dt, -80f, 80f);

        if (s.Kind == ViewKind.GroundStation && ctl != null && ctl.StationCount > 0)
        {
            if (!stationLatched)
            {
                if (l.x > 0.7f) { s.Station = (s.Station + 1) % ctl.StationCount; stationLatched = true; }
                else if (l.x < -0.7f) { s.Station = (s.Station + ctl.StationCount - 1) % ctl.StationCount; stationLatched = true; }
            }
            else if (Mathf.Abs(l.x) < 0.3f) stationLatched = false;
        }

        if (prevA.WasPressedThisFrame()) { SetKind(s, NextKind(s.Kind, -1)); HandToast.Show(LabelOf(s), prevA.activeControl); }
        if (nextA.WasPressedThisFrame()) { SetKind(s, NextKind(s.Kind, 1)); HandToast.Show(LabelOf(s), nextA.activeControl); }
    }

    // ── where each camera sits (the same views as the space view) ────────────
    static readonly Vector3 OverviewDirection = new Vector3(0f, 0.26f, -0.97f).normalized;

    void Limits(ViewKind k, out float lo, out float hi, out float start)
    {
        if (ctl != null) { ctl.GetZoomLimits(k, out lo, out hi, out start); return; }
        lo = 0.02f; hi = 30f; start = 1f;
    }

    void UpdatePose(ViewSlot s, float dt)
    {
        Transform w = world.transform;
        Vector3 up = Vector3.up;
        Vector3 earth = w.position;
        Vector3 sat = w.TransformPoint(world.SatPosition);
        Vector3 zenith = -w.TransformDirection(world.SatNadir);
        bool haveSat = world.HasSatellite;

        if (s.pendingStart && ctl != null)
        {
            Limits(s.Kind, out _, out _, out float st0);
            s.Dist = st0;
            s.pendingStart = false;
        }

        ViewKind k = s.Kind;
        if (!haveSat && k != ViewKind.Overview && k != ViewKind.GroundStation) k = ViewKind.Overview;     // nothing to follow yet
        Limits(k, out float lo, out float hi, out _);
        float dist = Mathf.Clamp(s.Dist, lo, hi);
        if (k == s.Kind) s.Dist = dist;

        if (s.seenJumps != Timeline.Jumps) { s.seenJumps = Timeline.Jumps; s.bodyInit = false; }

        Vector3 pos = Vector3.zero, focus = earth;
        Quaternion rot = Quaternion.identity;
        bool orbits = true;

        switch (k)
        {
            case ViewKind.Overview:
                pos = earth + OverviewDirection * dist;
                rot = Quaternion.LookRotation(earth - pos, up);
                focus = earth;
                break;

            case ViewKind.OrbitFollow:
                pos = FollowPosition(sat, zenith, dist);
                rot = Quaternion.LookRotation(sat - pos, zenith);
                focus = sat;
                break;

            case ViewKind.InertialFollow:
                if (!s.inertialCaptured)
                {
                    Vector3 p0 = FollowPosition(sat, zenith, dist);
                    s.inertialOffsetDir = (p0 - sat).normalized;
                    s.inertialRot = Quaternion.LookRotation(-s.inertialOffsetDir, up);
                    s.inertialCaptured = true;
                }
                pos = sat + s.inertialOffsetDir * dist;
                rot = s.inertialRot;
                focus = sat;
                break;

            case ViewKind.BodyFixed:
            {
                // Body +Z (imager / nadir) is local +y; up on screen = body +X (local +x), as in the space view.
                Quaternion target = w.rotation * world.SatRotation * Quaternion.LookRotation(Vector3.up, Vector3.right);
                if (!s.bodyInit) { s.smoothedBody = target; s.bodyInit = true; }
                s.smoothedBody = Quaternion.Slerp(s.smoothedBody, target, 1f - Mathf.Exp(-4f * dt));
                rot = s.smoothedBody;
                pos = sat + rot * (Vector3.back * dist);
                focus = sat;
                break;
            }

            case ViewKind.Sun:
            {
                Vector3 sunDir = w.TransformDirection(world.SunDir);
                pos = sat - sunDir * dist;
                Vector3 upRef = Mathf.Abs(Vector3.Dot(sunDir, up)) > 0.98f ? Vector3.forward : up;
                rot = Quaternion.LookRotation(sunDir, upRef);
                focus = sat;
                break;
            }

            default:
            {
                int n = ctl != null ? ctl.StationCount : 0;
                if (n == 0) { pos = earth + OverviewDirection * dist; rot = Quaternion.LookRotation(earth - pos, up); break; }
                s.Station = ((s.Station % n) + n) % n;
                ctl.StationLatLon(s.Station, out float lat, out float lon);
                Vector3 dir = w.TransformDirection(world.StationDirection(lat, lon));
                pos = earth + dir * world.EarthRadiusUnits + dir * dist;
                Vector3 toSat = haveSat ? sat - pos : Vector3.ProjectOnPlane(Vector3.forward, dir);
                if (toSat.sqrMagnitude < 1e-8f) toSat = Vector3.right;
                rot = Quaternion.LookRotation(toSat, dir);
                orbits = false;
                break;
            }
        }

        if (Mathf.Abs(s.Yaw) > 0.01f || Mathf.Abs(s.Pitch) > 0.01f)
        {
            Quaternion q = Quaternion.AngleAxis(s.Yaw, rot * Vector3.up) * Quaternion.AngleAxis(s.Pitch, rot * Vector3.right);
            if (orbits) pos = focus + q * (pos - focus);
            rot = q * rot;
        }
        s.Cam.transform.SetPositionAndRotation(pos, rot);
    }

    Vector3 FollowPosition(Vector3 sat, Vector3 zenith, float dist)
    {
        Vector3 track = world.transform.TransformDirection(world.TrackDir);
        track -= Vector3.Project(track, zenith);
        if (track.sqrMagnitude < 1e-6f) track = Vector3.forward;
        track.Normalize();
        return sat - track * (dist * 0.8f) + zenith * (dist * 0.5f);
    }
}
