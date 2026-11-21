using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Places you in the space scene. There is no walking: each VIEW is a viewpoint tied to something
/// (the whole system, the satellite, the Sun, a ground station). The script moves the XR rig, you look
/// around with your head, and the LEFT STICK zooms in and out along the view's axis.
///
/// Controls (Quest):
///   RIGHT stick up/down     zoom in / out
///   RIGHT stick left/right  turn around the view (smooth); it springs back when you change view
///   A (right)               previous view          B (right)   next view
///   LEFT stick left/right   (ground station view only) previous / next station, with a smooth glide
///   X (left)                VR / MR (MixedRealityMode)     Y (left)   hub / space (ViewManager)
/// Every view starts at its default spot, looking at its subject. Which views are in the roster is set per view
/// with the "Include" tick boxes below.
///
/// Views (A / B, or keys 1-6):
///   1 Overview        the Earth, orbit, Moon and Sun; look around to find them.
///   2 Orbit follow    behind and above the satellite; the horizon stays level.
///   3 Inertial follow follows the satellite, but your orientation stays fixed against the stars.
///   4 Body fixed      in the satellite's own frame, looking along the imager (nadir) axis, backed off.
///   5 Sun view        behind the satellite on the Sun-satellite line, looking at the Sun. When the
///                     satellite enters eclipse the Earth slides across the Sun.
///   6 Ground station  above a ground station, looking at the satellite. Left stick left/right changes
///                     station (a smooth glide, and only when you ask).
///
/// View changes are instant (gliding causes motion sickness). Only changing between ground stations
/// glides. Zoom is proportional: it moves a fixed PERCENTAGE of the distance per second, so it is slow
/// and precise when you are close and quick when you are far. Push the stick gently for fine control.
///
/// Keyboard for testing in the editor: 1-6 views, Q / E previous / next view, W/S zoom, A/D turn around the view,
/// left/right arrows change station, R recentre.
/// </summary>
public class SpaceViewController : MonoBehaviour
{
    public enum ViewKind { Overview, OrbitFollow, InertialFollow, BodyFixed, Sun, GroundStation }

    [Serializable] public struct ViewTuning
    {
        [Tooltip("Tick to have this view in the roster (A / B step through the ticked views).")] public bool include;
        [Tooltip("Closest zoom (Unity units).")] public float min;
        [Tooltip("Farthest zoom (Unity units).")] public float max;
        [Tooltip("Where this view starts.")] public float start;
    }

    [Serializable] public struct Station { public string name; public float lat, lon; }

    [SerializeField] SpaceWorld world;
    [SerializeField] ViewKind startView = ViewKind.Overview;

    [Header("Comfort")]
    [Tooltip("Changing from one ground station to another glides instead of jumping.")]
    [SerializeField] bool glideBetweenStations = true;
    [Tooltip("Also glide when entering or leaving the ground station view. Off = instant, like the other views.")]
    [SerializeField] bool glideIntoGroundView = false;
    [Tooltip("Seconds a glide takes.")]
    [SerializeField] float glideSeconds = 1.0f;
    [Tooltip("How fast the left stick zooms, as a fraction of the distance per second at full push.")]
    [SerializeField] float zoomSpeed = 1.5f;
    [Tooltip("Body fixed view only: smooths the satellite's rotation so the world does not whip around. 0 = rigid.")]
    [SerializeField] float bodyFixedSmoothing = 4f;
    [Tooltip("How fast the right stick (left/right) turns you around the view, in degrees per second at full push.")]
    [SerializeField] float turnSpeedDeg = 70f;
    [Tooltip("Closest the camera can draw (metres) while in space, so you can get right up to the satellite.")]
    [SerializeField] float nearClip = 0.01f;

    [Header("Zoom reach (applied on top of the per-view ranges below)")]
    [Tooltip("Multiplies how CLOSE you can get in the follow, body, Sun and ground views. Smaller = closer. " +
             "(The overview keeps its minimum so you stay outside the Earth.)")]
    [SerializeField] float closestZoomMultiplier = 0.05f;
    [Tooltip("Multiplies how FAR you can zoom out in every view. Bigger = farther.")]
    [SerializeField] float farthestZoomMultiplier = 5f;
    [Tooltip("The headset cannot focus on anything closer than about half a metre, so it looks blurry and double. " +
             "Your eyes are never brought closer than this; zooming in further makes the satellite grow instead.")]
    [SerializeField] float comfortableDistance = 0.5f;
    [Tooltip("When you zoom in past the comfortable distance, the whole space world (Earth, Moon...) is scaled up around the " +
             "satellite so everything keeps its proportions. This is the most it is scaled. If you need more, only the satellite grows.")]
    [SerializeField] float maxWorldScale = 60f;
    [Tooltip("The camera's far clip plane while in space. The scaled-up world is big, so it must be far.")]
    [SerializeField] float farClip = 5000f;

    [Header("Per-view zoom range (Unity units)")]
    [SerializeField] ViewTuning overview = new ViewTuning { include = true, min = 6f, max = 30f, start = 14f };
    [SerializeField] ViewTuning orbitFollow = new ViewTuning { include = true, min = 0.06f, max = 4f, start = 0.4f };
    [SerializeField] ViewTuning inertialFollow = new ViewTuning { include = true, min = 0.06f, max = 4f, start = 0.4f };
    [SerializeField] ViewTuning bodyFixed = new ViewTuning { include = true, min = 0.06f, max = 3f, start = 0.35f };
    [SerializeField] ViewTuning sunView = new ViewTuning { include = true, min = 0.06f, max = 8f, start = 0.5f };
    [Tooltip("For the ground station view this is the height above the ground.")]
    [SerializeField] ViewTuning groundView = new ViewTuning { include = true, min = 0.02f, max = 1.5f, start = 0.05f };

    [Header("Ground stations (from the stub)")]
    [SerializeField] Station[] stations =
    {
        new Station { name = "Iqaluit",   lat = 63.747f,  lon = -68.518f },
        new Station { name = "Troll",     lat = -72.012f, lon = 2.535f },
        new Station { name = "Svalbard",  lat = 78.2297f, lon = 15.3975f },
        new Station { name = "Inuvik",    lat = 68.3195f, lon = -133.5495f },
        new Station { name = "O'Higgins", lat = -63.3211f, lon = -57.9008f },
    };

    public ViewKind Current { get; private set; }
    public string CurrentLabel => Current == ViewKind.GroundStation && stations.Length > 0
        ? "Ground station: " + stations[stationIndex].name : Current.ToString();
    public event Action<ViewKind> ViewChanged;

    XROrigin origin;
    InputAction zoomAction, flickAction, prevAction, nextAction;
    readonly float[] distances = new float[6];
    int stationIndex;
    bool stationLatched;
    float viewYaw;                         // degrees turned around the current view; reset on every view change
    Vector3 poseFocus; bool poseOrbits;    // what the turn pivots around (set by TryGetPose)
    float savedNearClip = -1f, savedFarClip = -1f;

    // Where the head sits in the rig when we last centred (so leaning still works).
    Vector3 captureHeadLocal;
    float captureYaw;
    bool captured;

    // Gliding between ground stations
    bool hasLast, blending;
    float blendT;
    Vector3 lastPos, fromPos;
    Quaternion lastRot = Quaternion.identity, fromRot = Quaternion.identity;

    // Per-view state
    bool inertialCaptured;
    Vector3 inertialOffsetDir;
    Quaternion inertialRot;
    Quaternion smoothedBody = Quaternion.identity;
    bool bodyInit;

    // Overview camera sits slightly above the Earth's equator, looking at its centre.
    static readonly Vector3 OverviewDirection = new Vector3(0f, 0.26f, -0.97f).normalized;

    static readonly Key[] NumberKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };

    // ── setup ────────────────────────────────────────────────────────────────
    void Start()
    {
        if (world == null) world = FindAnyObjectByType<SpaceWorld>();
        origin = FindAnyObjectByType<XROrigin>();

        if (origin != null && origin.Camera != null)
        {
            savedNearClip = origin.Camera.nearClipPlane;
            origin.Camera.nearClipPlane = nearClip;
            savedFarClip = origin.Camera.farClipPlane;
            if (farClip > savedFarClip) origin.Camera.farClipPlane = farClip;
        }

        for (int i = 0; i < distances.Length; i++) distances[i] = Tuning((ViewKind)i).start;

        // Left stick: only left/right is used (ground station view: change station).
        zoomAction = new InputAction("LeftStick", InputActionType.Value, expectedControlType: "Vector2");
        zoomAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        zoomAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        zoomAction.Enable();

        // Right stick: up/down zoom, left/right turn around the view.
        flickAction = new InputAction("RightStick", InputActionType.Value, expectedControlType: "Vector2");
        flickAction.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        flickAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        flickAction.Enable();

        // A = previous view, B = next view (right controller).
        prevAction = new InputAction("PreviousView", InputActionType.Button);
        prevAction.AddBinding("<XRController>{RightHand}/primaryButton");
        prevAction.AddBinding("<Keyboard>/q");
        prevAction.Enable();
        nextAction = new InputAction("NextView", InputActionType.Button);
        nextAction.AddBinding("<XRController>{RightHand}/secondaryButton");
        nextAction.AddBinding("<Keyboard>/e");
        nextAction.Enable();

        // The starting view must be in the roster.
        if (!IsIncluded(startView)) startView = FirstIncluded();
        Current = startView;
    }

    void OnDestroy()
    {
        if (zoomAction != null) { zoomAction.Disable(); zoomAction.Dispose(); }
        if (flickAction != null) { flickAction.Disable(); flickAction.Dispose(); }
        if (prevAction != null) { prevAction.Disable(); prevAction.Dispose(); }
        if (nextAction != null) { nextAction.Disable(); nextAction.Dispose(); }
        if (savedNearClip > 0f && origin != null && origin.Camera != null)
            origin.Camera.nearClipPlane = savedNearClip;
        if (savedFarClip > 0f && origin != null && origin.Camera != null)
            origin.Camera.farClipPlane = savedFarClip;
        if (world != null) world.SetWorldScale(1f);
    }

    void ZoomRange(ViewKind v, out float min, out float max)
    {
        ViewTuning t = Tuning(v);
        min = v == ViewKind.Overview ? t.min : t.min * Mathf.Max(0.0001f, closestZoomMultiplier);
        // Satellite views: do not let the zoom number go inside the satellite itself.
        bool satView = v == ViewKind.OrbitFollow || v == ViewKind.InertialFollow || v == ViewKind.BodyFixed || v == ViewKind.Sun;
        if (satView && world != null) min = Mathf.Max(min, 1.2f * world.SatelliteLengthUnits);
        max = t.max * Mathf.Max(1f, farthestZoomMultiplier);
        if (max < min) max = min;
    }

    ViewTuning Tuning(ViewKind v)
    {
        switch (v)
        {
            case ViewKind.Overview: return overview;
            case ViewKind.OrbitFollow: return orbitFollow;
            case ViewKind.InertialFollow: return inertialFollow;
            case ViewKind.BodyFixed: return bodyFixed;
            case ViewKind.Sun: return sunView;
            default: return groundView;
        }
    }

    // ── public controls ──────────────────────────────────────────────────────
    public void SetView(ViewKind v)
    {
        if (v == Current && hasLast) return;
        ViewKind previous = Current;
        Current = v;
        inertialCaptured = false;
        bodyInit = false;
        viewYaw = 0f;                      // every view starts at its default spot...
        distances[(int)v] = Tuning(v).start;   // ...and its default zoom

        if (v == ViewKind.GroundStation && world != null && world.State != null && world.State.InContact)
            PickStationByName(world.State.GroundStation);

        bool glide = glideIntoGroundView && (v == ViewKind.GroundStation || previous == ViewKind.GroundStation);
        if (glide) StartBlend(); else blending = false;

        Debug.Log("[SpaceView] view = " + CurrentLabel);
        ViewChanged?.Invoke(Current);
    }

    public void SetViewByIndex(int index) => SetView((ViewKind)(((index % distances.Length) + distances.Length) % distances.Length));
    public int ViewCount => distances.Length;

    /// <summary>Whether a view is ticked "Include" in the inspector. If none are ticked, all count.</summary>
    public bool IsIncluded(ViewKind v)
    {
        bool any = false;
        for (int i = 0; i < distances.Length; i++) if (Tuning((ViewKind)i).include) { any = true; break; }
        return !any || Tuning(v).include;
    }

    ViewKind FirstIncluded()
    {
        for (int i = 0; i < distances.Length; i++) if (IsIncluded((ViewKind)i)) return (ViewKind)i;
        return ViewKind.Overview;
    }

    void Step(int dir)
    {
        int n = distances.Length;
        int i = (int)Current;
        for (int k = 0; k < n; k++)
        {
            i = (i + dir + n) % n;
            if (IsIncluded((ViewKind)i)) { SetView((ViewKind)i); return; }
        }
    }

    public void NextView() => Step(1);
    public void PreviousView() => Step(-1);

    public void NextStation() => ChangeStation(1);
    public void PreviousStation() => ChangeStation(-1);

    /// <summary>Make where you are looking right now the "forward" of the view.</summary>
    public void Recenter() => CaptureHead();

    void ChangeStation(int step)
    {
        if (stations.Length == 0) return;
        stationIndex = (stationIndex + step + stations.Length) % stations.Length;
        if (glideBetweenStations) StartBlend(); else blending = false;
        Debug.Log("[SpaceView] view = " + CurrentLabel);
    }

    void PickStationByName(string stationName)
    {
        for (int i = 0; i < stations.Length; i++)
            if (stations[i].name == stationName) { stationIndex = i; return; }
    }

    void StartBlend()
    {
        if (!hasLast) return;
        fromPos = lastPos;
        fromRot = lastRot;
        blendT = 0f;
        blending = true;
    }

    void CaptureHead()
    {
        if (origin == null || origin.Camera == null) return;
        Transform o = origin.Origin.transform;
        captureHeadLocal = origin.CameraInOriginSpacePos;
        Vector3 f = o.InverseTransformDirection(origin.Camera.transform.forward);
        f.y = 0f;
        captureYaw = f.sqrMagnitude < 1e-4f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        captured = true;
    }

    // ── per frame ────────────────────────────────────────────────────────────
    void LateUpdate()
    {
        if (world == null || origin == null || origin.Camera == null || !world.HasData) return;
        if (!captured) CaptureHead();

        float dt = Time.unscaledDeltaTime;
        ReadInput(dt);

        ViewTuning tune = Tuning(Current);
        int vi = (int)Current;
        ZoomRange(Current, out float zMin, out float zMax);
        distances[vi] = Mathf.Clamp(distances[vi], zMin, zMax);

        // Close-ups: keep the eyes at a comfortable distance and scale the satellite up instead of moving closer.
        float physical = distances[vi];
        float satScale = 1f;
        bool satView = Current == ViewKind.OrbitFollow || Current == ViewKind.InertialFollow ||
                       Current == ViewKind.BodyFixed || Current == ViewKind.Sun;
        if (satView && world.HasSatellite && physical < comfortableDistance)
        {
            satScale = comfortableDistance / physical;
            physical = comfortableDistance;
        }
        else if (Current == ViewKind.GroundStation)
            physical = Mathf.Max(physical, comfortableDistance);
        // Scale the whole world about the satellite (so proportions stay right); only if that is not enough, grow the satellite.
        float worldScale = Mathf.Min(satScale, Mathf.Max(1f, maxWorldScale));
        world.SetWorldScale(worldScale);
        world.SatelliteZoomScale = satScale / worldScale;

        if (!TryGetPose(physical, dt, out Vector3 pos, out Quaternion rot)) return;

        if (blending)
        {
            blendT += dt / Mathf.Max(0.05f, glideSeconds);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blendT));
            pos = Vector3.Lerp(fromPos, pos, s);
            rot = Quaternion.Slerp(fromRot, rot, s);
            if (blendT >= 1f) blending = false;
        }

        // Turn around the view (right stick left/right). The pivot is the subject (Earth, satellite); in the ground
        // station view you turn on the spot.
        if (Mathf.Abs(viewYaw) > 0.01f)
        {
            Quaternion q = Quaternion.AngleAxis(viewYaw, rot * Vector3.up);
            if (poseOrbits) pos = poseFocus + q * (pos - poseFocus);
            rot = q * rot;
        }

        Apply(pos, rot);
    }

    void ReadInput(float dt)
    {
        Vector2 left = zoomAction.ReadValue<Vector2>();
        Vector2 right = flickAction.ReadValue<Vector2>();

        // Zoom (right stick up = closer). Proportional: the same fraction of the distance per second, so it
        // is slow when close. A dead zone and a squared curve make small pushes very gentle.
        float zy = right.y;
        float mag = Mathf.Abs(zy);
        if (mag > 0.12f)
        {
            float t = (mag - 0.12f) / 0.88f;                    // 0..1 beyond the dead zone
            float push = Mathf.Sign(zy) * t * t;                // squared: gentle at first
            distances[(int)Current] *= Mathf.Exp(-push * zoomSpeed * dt);
        }

        // Turn around the view (right stick left/right), smooth and continuous.
        float turn = right.x;
        float turnMag = Mathf.Abs(turn);
        if (turnMag > 0.15f)
        {
            float t = (turnMag - 0.15f) / 0.85f;
            viewYaw += Mathf.Sign(turn) * t * turnSpeedDeg * dt;
        }

        // Ground station view: left stick left/right changes station (a flick).
        if (!stationLatched)
        {
            if (Current == ViewKind.GroundStation)
            {
                if (left.x > 0.7f) { NextStation(); stationLatched = true; }
                else if (left.x < -0.7f) { PreviousStation(); stationLatched = true; }
            }
        }
        else if (Mathf.Abs(left.x) < 0.3f) stationLatched = false;

        // A = previous view, B = next view
        if (prevAction.WasPressedThisFrame()) PreviousView();
        if (nextAction.WasPressedThisFrame()) NextView();

        var kb = Keyboard.current;
        if (kb != null)
        {
            for (int i = 0; i < NumberKeys.Length; i++)
                if (kb[NumberKeys[i]].wasPressedThisFrame && IsIncluded((ViewKind)i)) SetView((ViewKind)i);
            if (kb[Key.R].wasPressedThisFrame) Recenter();
        }

        if (!hasLast) Debug.Log("[SpaceView] view = " + CurrentLabel);
    }

    // ── where the head should be, for each view (world space) ───────────────
    bool TryGetPose(float dist, float dt, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        Transform w = world.transform;
        Vector3 up = Vector3.up;
        Vector3 earthCentre = w.position;
        Vector3 sat = w.TransformPoint(world.SatPosition);
        Vector3 nadir = w.TransformDirection(world.SatNadir);
        Vector3 zenith = -nadir;
        bool haveSat = world.HasSatellite;

        // No telemetry yet (the stub is not running): the views that follow the satellite have nothing to
        // follow, so show the overview instead. The Earth, Sun and Moon are still there to look at.
        if (!haveSat && Current != ViewKind.Overview && Current != ViewKind.GroundStation)
        {
            ZoomRange(ViewKind.Overview, out float oMin, out float oMax);
            float od = Mathf.Clamp(distances[(int)ViewKind.Overview], oMin, oMax);
            pos = earthCentre + OverviewDirection * od;
            rot = Quaternion.LookRotation(earthCentre - pos, up);
            poseFocus = earthCentre; poseOrbits = true;
            return true;
        }

        switch (Current)
        {
            case ViewKind.Overview:
            {
                pos = earthCentre + OverviewDirection * dist;
                rot = Quaternion.LookRotation(earthCentre - pos, up);
                poseFocus = earthCentre; poseOrbits = true;
                return true;
            }

            case ViewKind.OrbitFollow:
            {
                pos = OrbitFollowPosition(sat, zenith, dist);
                rot = Quaternion.LookRotation(sat - pos, zenith);
                poseFocus = sat; poseOrbits = true;
                return true;
            }

            case ViewKind.InertialFollow:
            {
                if (!inertialCaptured)
                {
                    Vector3 p0 = OrbitFollowPosition(sat, zenith, dist);
                    inertialOffsetDir = (p0 - sat).normalized;
                    inertialRot = Quaternion.LookRotation(-inertialOffsetDir, up);
                    inertialCaptured = true;
                }
                pos = sat + inertialOffsetDir * dist;
                rot = inertialRot;                        // never re-aimed: the stars stay put
                poseFocus = sat; poseOrbits = true;
                return true;
            }

            case ViewKind.BodyFixed:
            {
                // Body +Z (imager / nadir) is Unity local +y. Up on screen = body +X (along-track, local +x).
                Quaternion target = w.rotation * world.SatRotation *
                                    Quaternion.LookRotation(Vector3.up, Vector3.right);
                if (!bodyInit) { smoothedBody = target; bodyInit = true; }
                smoothedBody = bodyFixedSmoothing <= 0f
                    ? target
                    : Quaternion.Slerp(smoothedBody, target, 1f - Mathf.Exp(-bodyFixedSmoothing * dt));
                rot = smoothedBody;
                pos = sat + rot * (Vector3.back * dist);     // backed off, behind the viewing direction
                poseFocus = sat; poseOrbits = true;
                return true;
            }

            case ViewKind.Sun:
            {
                Vector3 sunDir = w.TransformDirection(world.SunDir);
                pos = sat - sunDir * dist;                   // satellite is between you and the Sun
                Vector3 upRef = Mathf.Abs(Vector3.Dot(sunDir, up)) > 0.98f ? Vector3.forward : up;
                rot = Quaternion.LookRotation(sunDir, upRef);
                poseFocus = sat; poseOrbits = true;
                return true;
            }

            case ViewKind.GroundStation:
            {
                if (stations.Length == 0) return false;
                Station st = stations[stationIndex];
                Vector3 dir = w.TransformDirection(world.StationDirection(st.lat, st.lon));
                Vector3 surface = earthCentre + dir * world.EarthRadiusUnits;
                pos = surface + dir * dist;
                // With no satellite yet, look along the horizon instead.
                Vector3 toSat = haveSat ? sat - pos : Vector3.ProjectOnPlane(Vector3.forward, dir);
                if (!haveSat && toSat.sqrMagnitude < 1e-6f) toSat = Vector3.right;
                rot = toSat.sqrMagnitude < 1e-6f ? Quaternion.LookRotation(Vector3.forward, dir)
                                                 : Quaternion.LookRotation(toSat, dir);
                poseOrbits = false;                        // turn on the spot, about the local vertical
                return true;
            }
        }
        return false;
    }

    Vector3 OrbitFollowPosition(Vector3 sat, Vector3 zenith, float dist)
    {
        Vector3 track = world.transform.TransformDirection(world.TrackDir);
        track = (track - Vector3.Project(track, zenith));
        if (track.sqrMagnitude < 1e-6f) track = Vector3.forward;
        track.Normalize();
        return sat - track * (dist * 0.8f) + zenith * (dist * 0.5f);
    }

    // ── moving the rig ───────────────────────────────────────────────────────
    void Apply(Vector3 headPos, Quaternion viewRot)
    {
        // Turn the rig so the way you were facing when you centred becomes the view's forward,
        // then place it so your head's resting position lands on the target. Leaning still works.
        Quaternion rigRot = viewRot * Quaternion.Euler(0f, -captureYaw, 0f);
        Transform o = origin.Origin.transform;
        o.rotation = rigRot;
        o.position = headPos - rigRot * captureHeadLocal;

        lastPos = headPos;
        lastRot = viewRot;
        hasLast = true;
    }
}
