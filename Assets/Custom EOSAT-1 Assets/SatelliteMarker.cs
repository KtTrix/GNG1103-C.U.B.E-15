using System;
using UnityEngine;

/// <summary>
/// Places this object at the satellite's position around the globe.
///
/// Scene setup: make this a CHILD of the Globe pivot (the object with GlobeSpinner),
/// next to GlobeModel. Because it's a child, it turns with the globe when the user
/// spins it, so the dot always stays over the right spot on the map.
///
/// Frame conventions (this is the important part):
///   * The stub sends ECI (inertial) coordinates. Earth rotates underneath them.
///   * We rotate ECI into ECEF (Earth-fixed) using earth.gmst_deg, so the position
///     is expressed in the same frame as the Earth texture.
///   * ECEF is right-handed with Z = north pole; Unity is left-handed with Y = up.
///     Swapping Y and Z fixes both at once:  Unity = (ECEF X, ECEF Z, ECEF Y).
///     With that mapping, longitude 0 points along +X, 90 degrees East points along +Z.
/// </summary>
public class SatelliteMarker : MonoBehaviour
{
    [SerializeField] TelemetryClient telemetry;

    [Header("Scale")]
    [Tooltip("Radius of the globe sphere in the Globe pivot's space. For a default Unity sphere, " +
             "that is the sphere's scale / 2 (scale 0.5 -> 0.25).")]
    [SerializeField] float globeRadius = 0.25f;
    [Tooltip("1 = true scale (a 500 km orbit hugs the surface). Higher values lift the dot " +
             "away from the globe for visibility. Visual only.")]
    [SerializeField] float altitudeExaggeration = 3f;

    [Header("Motion")]
    [Tooltip("How quickly the dot catches up to each new frame. Higher = snappier.")]
    [SerializeField] float smoothing = 12f;
    [SerializeField] bool hideUntilData = true;

    [Header("Test mode (no server needed)")]
    [Tooltip("Pin the dot to a fixed latitude/longitude. Use it to calibrate the globe texture.")]
    [SerializeField] bool useTestPosition = false;
    [SerializeField] float testLatDeg = 45.42f;   // Ottawa
    [SerializeField] float testLonDeg = -75.70f;
    [SerializeField] float testAltKm = 500f;

    const double DefaultEarthRadiusKm = 6371.0;

    Renderer rend;
    Vector3 target;
    bool haveTarget;
    bool snapNext = true;
    int lastFrameCount = -1;

    void Awake()
    {
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        if (useTestPosition)
        {
            double lat = testLatDeg * Math.PI / 180.0;
            double lon = testLonDeg * Math.PI / 180.0;
            double r = DefaultEarthRadiusKm + testAltKm;
            target = EcefToLocal(r * Math.Cos(lat) * Math.Cos(lon),
                                 r * Math.Cos(lat) * Math.Sin(lon),
                                 r * Math.Sin(lat),
                                 DefaultEarthRadiusKm);
            haveTarget = true;
        }
        else if (telemetry != null && telemetry.FramesReceived != lastFrameCount && telemetry.Latest != null)
        {
            lastFrameCount = telemetry.FramesReceived;
            target = EciToLocal(telemetry.Latest);
            haveTarget = true;
        }

        if (rend != null) rend.enabled = haveTarget || !hideUntilData;
        if (!haveTarget) return;

        if (snapNext || smoothing <= 0f)
        {
            transform.localPosition = target;
            snapNext = false;
        }
        else
        {
            // Frame-rate independent smoothing; hides the jumps between 1-10 Hz frames.
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, k);
        }
    }

    /// <summary>ECI km -> ECEF km (rotate about Z by -GMST) -> position in the globe's local space.</summary>
    Vector3 EciToLocal(StateFrame f)
    {
        double[] p = f.orbit.pos_eci_km;
        double g = f.earth.gmst_deg * Math.PI / 180.0;
        double c = Math.Cos(g), s = Math.Sin(g);

        // Same formula as eci_to_ecef() in the Python stub.
        double xe = c * p[0] + s * p[1];
        double ye = -s * p[0] + c * p[1];
        double ze = p[2];

        double earthR = f.earth.radius_km > 0 ? f.earth.radius_km : DefaultEarthRadiusKm;
        return EcefToLocal(xe, ye, ze, earthR);
    }

    Vector3 EcefToLocal(double xe, double ye, double ze, double earthRadiusKm)
    {
        double r = Math.Sqrt(xe * xe + ye * ye + ze * ze);
        if (r < 1.0) return Vector3.zero;

        // Scale km -> metres of globe, optionally exaggerating the height above the surface.
        float altFraction = (float)((r - earthRadiusKm) / earthRadiusKm);
        float displayRadius = globeRadius * (1f + altitudeExaggeration * altFraction);

        // Unity axes: x = ECEF X, y = ECEF Z (north), z = ECEF Y.
        var dir = new Vector3((float)(xe / r), (float)(ze / r), (float)(ye / r));
        return dir * displayRadius;
    }
}