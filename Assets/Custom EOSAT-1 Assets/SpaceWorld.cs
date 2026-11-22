using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lives in the SpaceView scene. Each frame it places the Earth, Moon, Sun and satellite from the
/// shared SpaceState (see SpaceFrame).
///
/// The layout is NOT true scale for the planets: directions are always true, but the Moon is drawn
/// much closer and the Sun is a disc at a fixed distance from the viewer. The satellite is the opposite:
/// it is drawn tiny (close to its real proportions as far as Unity can handle), so you zoom in to see it.
/// A small marker (optional) keeps it findable from far away.
///
/// If no telemetry has arrived (the stub is not running) the Earth, Moon and Sun are still shown, for
/// today's real date. The satellite, its orbit line and trail appear when data starts.
///
/// All km values stay available as real numbers in SpaceState for the data panels.
/// </summary>
public class SpaceWorld : MonoBehaviour
{
    [SerializeField] SpaceFrame frame;

    [Header("Things in this scene")]
    [SerializeField] Transform earth;
    [SerializeField] Transform moon;
    [SerializeField] Transform satellite;
    [Tooltip("The directional light. It is turned to shine from the Sun's direction.")]
    [SerializeField] Light sunLight;
    [Tooltip("A bright sphere that marks the Sun. Kept at a fixed distance from the viewer.")]
    [SerializeField] Transform sunDisc;
    [Tooltip("Shadows are switched off on the Sun light: they cost a lot on the headset and are not needed.")]
    [SerializeField] bool disableSunShadows = true;

    [Header("Orbit lines")]
    [Tooltip("Optional: an empty object with a LineRenderer. Draws the WHOLE orbit as a ring, worked out from " +
             "the satellite's position and velocity, so it is always complete and never has gaps or kinks.")]
    [SerializeField] LineRenderer orbitRing;
    [Tooltip("Draw the full orbit circle. Off by default: it is one fixed circle in space, which is why it looks " +
             "like 'a single ring'. The trail shows where the satellite has actually been over the Earth.")]
    [SerializeField] bool showOrbitRing = false;
    [SerializeField] int ringSegments = 180;
    [Tooltip("Optional: an empty object with a LineRenderer. Draws the path the satellite has actually flown, " +
             "from the history that SpaceFrame keeps, so it is unbroken even after visiting the hub.")]
    [SerializeField] LineRenderer trail;
    [Tooltip("Draw the path the satellite has flown. Off for now: the space view shows no lines at all.")]
    [SerializeField] bool showTrail = false;
    [Tooltip("How many of the most recent history points to draw.")]
    [SerializeField] int maxTrailPoints = 1600;

    [Header("Optional Earth extras")]
    [Tooltip("A second sphere just above the surface with the cloud texture. Slowly drifts.")]
    [SerializeField] Transform clouds;
    [SerializeField] float cloudScale = 1.012f;
    [SerializeField] float cloudDriftDegPerMinute = 2f;
    [Tooltip("A sphere a little larger than the Earth with the AtmosphereRim material (blue glow at the edge).")]
    [SerializeField] Transform atmosphere;
    [SerializeField] float atmosphereScale = 1.035f;

    [Header("Sizes and distances (planets readable, not true scale)")]
    [Tooltip("Earth's radius in Unity units (metres). Everything else is sized from this.")]
    [SerializeField] float earthRadiusUnits = 3f;
    [Tooltip("1 = the satellite's true height above the surface. Keep at 1 if you want the eclipse in the " +
             "Sun view to match the stub exactly.")]
    [SerializeField] float altitudeExaggeration = 1f;
    [Tooltip("Moon radius as a fraction of Earth's. The true value is 0.27.")]
    [SerializeField] float moonRadiusInEarthRadii = 0.27f;
    [Tooltip("How many Earth radii away the Moon is drawn. The true value is about 60.")]
    [SerializeField] float moonDistanceInEarthRadii = 14f;
    [Tooltip("How far from the viewer the Sun disc is drawn (keep below the camera's far clip plane).")]
    [SerializeField] float sunDistanceUnits = 400f;
    [Tooltip("How big the Sun looks, in degrees across. Its true size is only 0.5, which is a speck.")]
    [SerializeField] float sunApparentDiameterDeg = 3f;

    [Header("Satellite")]
    [Tooltip("How long the satellite is drawn, in Unity units. Its real length is 0.366 m; at this " +
             "Earth size a true-scale satellite would be invisible, so it is exaggerated, but only a little.")]
    [SerializeField] float satelliteLengthUnits = 0.04f;
    [Tooltip("Multiplies the size above. 0.25 draws the satellite a quarter as long (still much larger than true scale, " +
             "which would be invisible), so you zoom in closer to see it.")]
    [SerializeField] float satelliteSizeMultiplier = 0.25f;
    Vector3 origin;
    bool originSet;

    /// <summary>Where the space world sits when it is not zoomed. ViewManager calls this when the scene loads.</summary>
    public void SetOrigin(Vector3 position)
    {
        origin = position; originSet = true;
        transform.position = position;
    }

    /// <summary>
    /// Scales the WHOLE space world (Earth, Moon, orbit, satellite) about the satellite, so zooming in on the satellite
    /// keeps everything in proportion instead of making the satellite alone look huge. The satellite stays where it is.
    /// </summary>
    public void SetWorldScale(float s)
    {
        if (!originSet) { origin = transform.position; originSet = true; }
        s = Mathf.Max(0.01f, s);
        transform.localScale = Vector3.one * s;
        transform.position = origin + (HasSatellite ? SatPosition * (1f - s) : Vector3.zero);
    }

    /// <summary>Set by SpaceViewController: grows the satellite when you zoom in closer than is comfortable for the eyes.</summary>
    public float SatelliteZoomScale { get; set; } = 1f;
    float SatLength => satelliteLengthUnits * Mathf.Max(0.001f, satelliteSizeMultiplier);
    [Tooltip("Optional: a small bright sphere or ring that stays the same apparent size, so you can find " +
             "the satellite from far away. Hides itself when you are close.")]
    [SerializeField] Transform marker;
    [SerializeField] float markerAngleDeg = 1.2f;
    [SerializeField] float markerHideDistance = 1.0f;

    // ── what the views read ──────────────────────────────────────────────────
    /// <summary>True once the sky is ready (from telemetry, or from the clock if there is none).</summary>
    public bool HasData { get; private set; }
    /// <summary>True once telemetry has given us the satellite.</summary>
    public bool HasSatellite => State != null && State.HasSatellite;
    public SpaceState State => frame != null ? frame.State : null;
    public float EarthRadiusUnits => earthRadiusUnits;
    public SpaceFrame Frame => frame;
    /// <summary>Where a position in km (Unity axes) sits in this world; the same mapping the satellite uses.</summary>
    public Vector3 MapKm(Vector3 km, float earthRadiusKm) => MapSatellite(km, earthRadiusKm);
    public float SatelliteLengthUnits => SatLength;

    // In this object's local space:
    public Vector3 SatPosition { get; private set; }
    public Quaternion SatRotation { get; private set; } = Quaternion.identity;
    public Vector3 TrackDir { get; private set; } = Vector3.forward;   // direction of travel
    public Vector3 SunDir { get; private set; } = Vector3.forward;     // from Earth towards the Sun
    public Vector3 SatNadir => SatPosition.sqrMagnitude > 1e-6f ? (-SatPosition).normalized : Vector3.down;

    // Satellite body size in metres, in BODY axes: X 0.100, Y 0.226, Z 0.366.
    // In Unity local axes body (X, Y, Z) appears as (x, z, y), so the cube is scaled (0.100, 0.366, 0.226).
    static readonly Vector3 BusUnityLocal = new Vector3(0.100f, 0.366f, 0.226f);

    Vector3[] trailBuffer = new Vector3[0];
    Vector3[] ringBuffer = new Vector3[0];
    int lastHistoryVersion = -1;

    void Awake()
    {
        if (trail != null) trail.useWorldSpace = false;
        if (orbitRing != null) { orbitRing.useWorldSpace = false; orbitRing.loop = true; }
        if (disableSunShadows && sunLight != null) sunLight.shadows = LightShadows.None;

        // If the Atmosphere object was never dropped into the slot above, look for it by name in this scene.
        if (atmosphere == null) atmosphere = FindInScene("Atmosphere");
        if (clouds == null) clouds = FindInScene("Clouds");

        // Smooth spheres instead of Unity's faceted default (very visible in VR)
        MakeSmooth(earth); MakeSmooth(clouds); MakeSmooth(atmosphere);

        // The detailed satellite model builds itself on the Satellite object; no manual setup needed.
        if (satellite != null && satellite.GetComponent<SatelliteModel>() == null)
            satellite.gameObject.AddComponent<SatelliteModel>();

        // Body axes drawn on the satellite (X red, Y green, Z blue); switched in Options > Display.
        if (satellite != null && satellite.GetComponent<SatelliteAxes>() == null)
            satellite.gameObject.AddComponent<SatelliteAxes>();

        // Red warning sphere with an icon, shown in every view while the satellite spins too fast.
        if (satellite != null && satellite.GetComponent<SpinWarningSphere>() == null)
            satellite.gameObject.AddComponent<SpinWarningSphere>();

        // The Earth's axes (inertial frame) at the Earth's centre, fixed while the Earth turns.
        var earthAxes = GetComponent<EarthAxes>();
        if (earthAxes == null) earthAxes = gameObject.AddComponent<EarthAxes>();
        earthAxes.Init(earthRadiusUnits);

        // Yellow arrows pointing at the Sun, from the Earth and from the satellite; switched in Options > Display.
        var sunVectors = GetComponent<SunVectors>();
        if (sunVectors == null) sunVectors = gameObject.AddComponent<SunVectors>();
        sunVectors.Init(this, satellite, earthRadiusUnits);

        // The Moon's night side (faint earthshine, so the dark part of the disc is still seen) and its near side toward the Earth.
        var moonNight = GetComponent<MoonNightSide>();
        if (moonNight == null) moonNight = gameObject.AddComponent<MoonNightSide>();
        moonNight.Init(this, moon);

        // The Earth's night side: dark, with the city lights, from the real Sun direction.
        var earthNight = GetComponent<EarthNightSide>();
        if (earthNight == null) earthNight = gameObject.AddComponent<EarthNightSide>();
        earthNight.Init(this, earth);

        // A pulsing dot and a trail that keep the satellite findable from far away; they fade out as you come close.
        var beacon = GetComponent<SatelliteBeacon>();
        if (beacon == null) beacon = gameObject.AddComponent<SatelliteBeacon>();
        beacon.Init(this, satellite);
    }

    Transform FindInScene(string objectName)
    {
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == objectName) return t;
        return null;
    }

    static void MakeSmooth(Transform t)
    {
        if (t != null && t.GetComponent<MeshFilter>() != null && t.GetComponent<HighResSphere>() == null)
            t.gameObject.AddComponent<HighResSphere>();
    }

    /// <summary>Unit direction from the Earth's centre to a ground point (local space, follows the Earth's spin).</summary>
    public Vector3 StationDirection(double latDeg, double lonDeg)
    {
        double gmst = State != null ? State.GmstDeg : 0.0;
        double lat = latDeg * Math.PI / 180.0;
        double th = (lonDeg + gmst) * Math.PI / 180.0;            // angle around the north axis, from ECI x
        return new Vector3((float)(Math.Cos(lat) * Math.Cos(th)),  // ECI (x, y, z) -> Unity (x, z, y)
                           (float)Math.Sin(lat),
                           (float)(Math.Cos(lat) * Math.Sin(th)));
    }

    Vector3 MapSatellite(Vector3 km, float earthRadiusKm)
    {
        float r = km.magnitude;
        if (r < 1f) return Vector3.zero;
        float ratio = r / earthRadiusKm;
        float scaled = earthRadiusUnits * (1f + (ratio - 1f) * altitudeExaggeration);
        return km / r * scaled;
    }

    static void SetActiveIfChanged(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    void Update()
    {
        if (frame == null)
        {
            frame = FindAnyObjectByType<SpaceFrame>();
            if (frame == null) return;
        }
        var s = frame.State;
        if (!s.Valid) return;
        HasData = true;

        float eKm = (float)s.EarthRadiusKm;
        bool sat = s.HasSatellite;

        SunDir = s.SunDir;
        if (sat)
        {
            SatPosition = MapSatellite(s.SatPosKm, eKm);
            SatRotation = s.SatRotation;
            if (s.SatVelKmS.sqrMagnitude > 1e-9f) TrackDir = s.SatVelKmS.normalized;
        }

        float earthDiameter = 2f * earthRadiusUnits;     // Unity's sphere is 1 across

        if (earth != null)
        {
            earth.localPosition = Vector3.zero;
            earth.localScale = Vector3.one * earthDiameter;
            earth.localRotation = s.EarthRotation;
        }

        if (clouds != null)
        {
            clouds.localPosition = Vector3.zero;
            clouds.localScale = Vector3.one * (earthDiameter * cloudScale);
            clouds.localRotation = s.EarthRotation * Quaternion.Euler(0f, Time.time * cloudDriftDegPerMinute / 60f, 0f);
        }

        if (atmosphere != null)
        {
            atmosphere.localPosition = Vector3.zero;
            atmosphere.localScale = Vector3.one * (earthDiameter * atmosphereScale);
        }

        if (moon != null)
        {
            moon.localPosition = s.MoonPosKm.normalized * (moonDistanceInEarthRadii * earthRadiusUnits);
            moon.localScale = Vector3.one * (2f * moonRadiusInEarthRadii * earthRadiusUnits);
        }

        if (satellite != null)
        {
            SetActiveIfChanged(satellite.gameObject, sat);
            if (sat)
            {
                satellite.localPosition = SatPosition;
                satellite.localRotation = SatRotation;
                satellite.localScale = Vector3.one * (SatLength * SatelliteZoomScale / 0.366f);   // the model is built in real metres
            }
        }

        Camera cam = Camera.main;
        Vector3 viewer = cam != null ? cam.transform.position : transform.position;

        if (marker != null)
        {
            bool show = false;
            float d = 0f;
            if (sat)
            {
                d = Vector3.Distance(viewer, transform.TransformPoint(SatPosition));
                show = d > markerHideDistance;
            }
            SetActiveIfChanged(marker.gameObject, show);
            if (show)
            {
                marker.localPosition = SatPosition;
                marker.localScale = Vector3.one * (2f * d * Mathf.Tan(markerAngleDeg * 0.5f * Mathf.Deg2Rad));
            }
        }

        if (sunLight != null)
            sunLight.transform.localRotation = Quaternion.LookRotation(-SunDir, Vector3.up);

        if (sunDisc != null)
        {
            sunDisc.position = viewer + transform.TransformDirection(SunDir) * sunDistanceUnits;
            sunDisc.localScale = Vector3.one *
                (2f * sunDistanceUnits * Mathf.Tan(sunApparentDiameterDeg * 0.5f * Mathf.Deg2Rad)) / Mathf.Max(0.01f, transform.lossyScale.x);
        }

        UpdateRing(s, eKm);
        UpdateTrail(s, eKm);
    }

    /// <summary>The whole orbit as a circle through the satellite, in the plane of its position and velocity.</summary>
    void UpdateRing(SpaceState s, float eKm)
    {
        if (orbitRing == null) return;

        bool ok = showOrbitRing && s.HasSatellite && s.SatVelKmS.sqrMagnitude > 1e-9f && s.SatPosKm.sqrMagnitude > 1f;
        if (orbitRing.enabled != ok) orbitRing.enabled = ok;
        if (!ok) return;

        Vector3 u = s.SatPosKm.normalized;
        Vector3 v = s.SatVelKmS - Vector3.Dot(s.SatVelKmS, u) * u;     // the part of the velocity across the radius
        if (v.sqrMagnitude < 1e-9f) return;
        v.Normalize();
        float r = s.SatPosKm.magnitude;

        int n = Mathf.Max(16, ringSegments);
        if (ringBuffer.Length != n)
        {
            ringBuffer = new Vector3[n];
            orbitRing.positionCount = n;
        }
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * (2f * Mathf.PI);
            ringBuffer[i] = MapSatellite((Mathf.Cos(a) * u + Mathf.Sin(a) * v) * r, eKm);
        }
        orbitRing.SetPositions(ringBuffer);
    }

    void UpdateTrail(SpaceState s, float eKm)
    {
        if (trail == null) return;

        bool sat = showTrail && s.HasSatellite && frame != null;
        if (trail.enabled != sat) trail.enabled = sat;
        if (!sat) return;

        var h = frame.History;
        int count = Mathf.Min(h.Count, Mathf.Max(2, maxTrailPoints));
        // The trail is drawn in the EARTH's frame and turns with the Earth, like the hub's ground track. In
        // inertial space the orbit is one fixed circle; seen from the ground each lap is shifted west, which is
        // what this shows. (Otherwise every lap would be drawn on top of the last one: "a single ring".)
        float gNow = (float)s.GmstDeg;
        trail.transform.localPosition = Vector3.zero;
        trail.transform.localRotation = Quaternion.Euler(0f, -gNow, 0f);
        Vector3 head = Quaternion.Euler(0f, gNow, 0f) * MapSatellite(s.SatPosKm, eKm);   // the line always ends on the satellite

        if (frame.HistoryVersion != lastHistoryVersion || trailBuffer.Length != count + 1)
        {
            lastHistoryVersion = frame.HistoryVersion;
            if (trailBuffer.Length != count + 1) trailBuffer = new Vector3[count + 1];
            int start = h.Count - count;
            for (int i = 0; i < count; i++)
            {
                Vector3 e = h[start + i].Eci;                       // ECI km as sent
                Vector3 inertial = MapSatellite(SpaceMath.EciToUnity(e.x, e.y, e.z), eKm);
                trailBuffer[i] = Quaternion.Euler(0f, h[start + i].GmstDeg, 0f) * inertial;   // fix it to the Earth
            }
            trailBuffer[count] = head;
            trail.positionCount = count + 1;
            trail.SetPositions(trailBuffer);
        }
        else
        {
            trail.SetPosition(count, head);                         // between history points, just move the end
        }
    }
}
