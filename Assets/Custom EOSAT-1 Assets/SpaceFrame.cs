using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Everything the space views need from one telemetry frame, already converted to Unity.
///
/// Units: positions are in KILOMETRES, with the Earth's centre at the origin. The view scripts
/// decide how to scale km into Unity units; nothing here is scaled.
///
/// Axes: the stub uses ECI (x, y, z with z = north pole). Unity has y = up, so
///   Unity (x, y, z) = ECI (x, z, y)        and a quaternion (x, y, z, w) becomes (-x, -z, -y, w).
/// Both were checked against the stub's own body-frame vectors in the sample file.
/// </summary>
public class SpaceState
{
    public bool Valid;
    /// <summary>False while no telemetry has arrived: only the sky (Earth spin, Sun, Moon) is real, from the clock.</summary>
    public bool HasSatellite;
    public int Seq;
    public DateTime UtcTime;
    public string AocsMode = "", Phase = "", GroundStation = "";
    public bool InContact, InEclipse;
    public float SunFraction = 1f;               // 1 = full sun, 0 = full shadow

    // Deployment (the stub's solar-array rotation angle is deliberately NOT used)
    public string WingPlusYState = "STOWED", WingMinusYState = "STOWED", AntennaState = "STOWED";
    public float WingPlusYFraction, WingMinusYFraction;   // 0 = folded, 1 = fully out
    public bool AntennaDeployed;

    // Positions (km, Unity axes, Earth centre = origin)
    public Vector3 SatPosKm, SatVelKmS, SunPosKm, MoonPosKm;
    public Vector3 SunDir;                       // unit vector, Earth -> Sun (from the stub)

    // Orientation
    public Quaternion SatRotation = Quaternion.identity;  // body -> Unity world
    public Quaternion EarthRotation = Quaternion.identity; // turn the Earth mesh by this

    // Earth / orbit
    public double GmstDeg, EarthRadiusKm = 6371.0, AltKm;
    public double SubLatDeg, SubLonDeg;          // worked out from the position (for checking)

    // Distances (km)
    public double SatToEarthKm, EarthToSunKm, SatToSunKm, EarthToMoonKm, SatToMoonKm;

    // Directions seen from the satellite, written in BODY axes (X, Y, Z), as the stub does.
    // Body +Z is the imager/nadir axis: EarthDirBody is (0,0,1) when nadir pointing.
    public Vector3 EarthDirBody, SunDirBody, MoonDirBody;
}

public static class SpaceMath
{
    public const double AU_KM = 149597870.7;
    const double Rad = Math.PI / 180.0;

    public static Vector3 EciToUnity(double x, double y, double z) =>
        new Vector3((float)x, (float)z, (float)y);

    public static Vector3 EciToUnity(double[] v) => EciToUnity(v[0], v[1], v[2]);

    /// <summary>Stub quaternion [x, y, z, w] -> Unity quaternion for the converted axes.</summary>
    public static Quaternion QuatToUnity(double[] q)
    {
        double x = -q[0], y = -q[2], z = -q[1], w = q[3];
        double n = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (n < 1e-9) return Quaternion.identity;
        return new Quaternion((float)(x / n), (float)(y / n), (float)(z / n), (float)(w / n));
    }

    /// <summary>A direction in Unity world axes -> the same direction in BODY axes (X, Y, Z).</summary>
    public static Vector3 WorldToBody(Quaternion satRotation, Vector3 worldDir)
    {
        Vector3 local = Quaternion.Inverse(satRotation) * worldDir;   // Unity local axes
        return new Vector3(local.x, local.z, local.y);                // back to body X, Y, Z
    }

    static double JulianDate(DateTime utc) =>
        (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays + 2440587.5;

    /// <summary>
    /// Moon position, km, in ECI axes (equatorial, about half a degree accurate for years near
    /// now). Low-precision series from the Astronomical Almanac.
    /// </summary>
    public static void MoonEciKm(DateTime utc, out double x, out double y, out double z)
    {
        double T = (JulianDate(utc) - 2451545.0) / 36525.0;
        double lam = 218.32 + 481267.881 * T
            + 6.29 * Math.Sin((135.0 + 477198.87 * T) * Rad)
            - 1.27 * Math.Sin((259.3 - 413335.36 * T) * Rad)
            + 0.66 * Math.Sin((235.7 + 890534.22 * T) * Rad)
            + 0.21 * Math.Sin((269.9 + 954397.74 * T) * Rad)
            - 0.19 * Math.Sin((357.5 + 35999.05 * T) * Rad)
            - 0.11 * Math.Sin((186.5 + 966404.03 * T) * Rad);
        double bet = 5.13 * Math.Sin((93.3 + 483202.02 * T) * Rad)
            + 0.28 * Math.Sin((228.2 + 960400.89 * T) * Rad)
            - 0.28 * Math.Sin((318.3 + 6003.15 * T) * Rad)
            - 0.17 * Math.Sin((217.6 - 407332.21 * T) * Rad);
        double par = 0.9508
            + 0.0518 * Math.Cos((135.0 + 477198.87 * T) * Rad)
            + 0.0095 * Math.Cos((259.3 - 413335.36 * T) * Rad)
            + 0.0078 * Math.Cos((235.7 + 890534.22 * T) * Rad)
            + 0.0028 * Math.Cos((269.9 + 954397.74 * T) * Rad);
        double r = 6378.137 / Math.Sin(par * Rad);

        double l = lam * Rad, b = bet * Rad;
        double cx = Math.Cos(b) * Math.Cos(l), cy = Math.Cos(b) * Math.Sin(l), cz = Math.Sin(b);
        double eps = (23.439291 - 0.0130042 * T) * Rad;                 // tilt of the ecliptic
        x = r * cx;
        y = r * (cy * Math.Cos(eps) - cz * Math.Sin(eps));
        z = r * (cy * Math.Sin(eps) + cz * Math.Cos(eps));
    }

    /// <summary>How far the Earth has turned (degrees) at a date. Matches the stub's earth.gmst_deg.</summary>
    public static double GmstDeg(DateTime utc)
    {
        double d = JulianDate(utc) - 2451545.0;
        double g = 280.46061837 + 360.98564736629 * d;
        return ((g % 360.0) + 360.0) % 360.0;
    }

    /// <summary>Unit vector from the Earth towards the Sun, in ECI axes, for a date. Matches the stub's sun.dir_eci.</summary>
    public static void SunDirEci(DateTime utc, out double x, out double y, out double z)
    {
        double d = JulianDate(utc) - 2451545.0;
        double L = 280.460 + 0.9856474 * d;
        double g = (357.528 + 0.9856003 * d) * Rad;
        double lam = (L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Rad;
        double eps = (23.439 - 0.0000004 * d) * Rad;
        x = Math.Cos(lam);
        y = Math.Cos(eps) * Math.Sin(lam);
        z = Math.Sin(eps) * Math.Sin(lam);
    }

    /// <summary>Earth-Sun distance in km for a date (the stub only gives the direction).</summary>
    public static double SunDistanceKm(DateTime utc)
    {
        double d = JulianDate(utc) - 2451545.0;
        double g = (357.529 + 0.98560028 * d) * Rad;
        return AU_KM * (1.00014 - 0.01671 * Math.Cos(g) - 0.00014 * Math.Cos(2 * g));
    }
}

/// <summary>
/// Reads the latest frame from TelemetryClient and publishes a SpaceState that every space view
/// (zoomed-out, close-up, data panels) reads from. Put it on one empty object.
/// </summary>
[DefaultExecutionOrder(-100)]   // run before the views, so they always read this frame's smoothed state
public class SpaceFrame : MonoBehaviour
{
    [SerializeField] TelemetryClient telemetry;

    [Tooltip("Turns the Earth mesh so its prime meridian lines up. Leave 0, then if the satellite is " +
             "not above the right spot on the Earth, try 90, 180 or 270.")]
    [SerializeField] float earthRotationOffsetDeg = 0f;

    [Header("Optional on-screen check (3D TMP text)")]
    [SerializeField] TMP_Text debugText;

    [Header("Orbit history (recorded all the time, even while the hub or space view is switched off)")]
    [Tooltip("A new history point is kept after the satellite moves this far (km).")]
    [SerializeField] float historyStepKm = 40f;
    [Tooltip("How many points are kept. 3000 points is a few orbits.")]
    [SerializeField] int historyMaxSamples = 3000;

    [Header("Smoothing")]
    [Tooltip("Telemetry arrives about 10 times a second, so the satellite would move in steps. With this on, the " +
             "position and attitude are carried forward smoothly between frames every rendered frame.")]
    [SerializeField] bool smoothBetweenFrames = true;
    [Tooltip("Never carry forward longer than this (real seconds), e.g. if the stub pauses.")]
    [SerializeField] float maxExtrapolateSeconds = 0.5f;

    /// <summary>One remembered position: ECI km as the stub sent it, the Earth's turn, and when/eclipse.</summary>
    public struct TrailSample { public Vector3 Eci; public float GmstDeg; public double SimSeconds; public bool Eclipse; }

    /// <summary>The path flown so far, oldest first. Both the hub's trail and the space view's trail draw from it.</summary>
    public List<TrailSample> History { get; } = new List<TrailSample>();
    /// <summary>Changes whenever History changes, so a drawer knows when to redraw.</summary>
    public int HistoryVersion { get; private set; }
    int lastHistorySeq = -1;

    public SpaceState State { get; } = new SpaceState();
    public event Action<SpaceState> Updated;

    StateFrame lastFrame;
    float nextDebug;

    // last real frame, kept so the state can be carried forward between frames
    Vector3 rawPos, rawVel; Quaternion rawRot = Quaternion.identity, prevRot = Quaternion.identity;
    double rawGmst; DateTime rawUtc, prevUtc; float rawReal, prevReal; bool havePrev;
    double simPerFrame = 0, frameInterval = 0.1;

    void Start()
    {
        if (telemetry == null) telemetry = FindAnyObjectByType<TelemetryClient>();
    }

    void Update()
    {
        var f = telemetry != null ? telemetry.Latest : null;
        if (f != null && !ReferenceEquals(f, lastFrame))
        {
            lastFrame = f;
            if (Compute(f, earthRotationOffsetDeg, State))
            {
                NoteRawFrame();
                Updated?.Invoke(State);
            }
            RecordHistory(f);
        }
        else if (lastFrame != null && smoothBetweenFrames && State.HasSatellite)
        {
            Extrapolate();
        }
        else if (lastFrame == null)
        {
            // No telemetry has ever arrived (the stub is not running): show today's real sky anyway,
            // so the Earth, Moon and Sun are still there. The satellite is hidden until data comes.
            ComputeSkyOnly(DateTime.UtcNow, earthRotationOffsetDeg, State);
        }
        if (debugText != null && Time.unscaledTime >= nextDebug)
        {
            nextDebug = Time.unscaledTime + 0.25f;
            debugText.text = DebugString();
        }
    }

    void NoteRawFrame()
    {
        float now = Time.unscaledTime;
        prevRot = havePrev ? rawRot : State.SatRotation;
        if (havePrev)
        {
            double dReal = now - rawReal;
            double dSim = (State.UtcTime - rawUtc).TotalSeconds;
            if (dReal > 0.0005 && dReal < 2.0) frameInterval = Mathf.Lerp((float)frameInterval, (float)dReal, 0.1f);
            if (dSim >= 0 && dSim < 3600) simPerFrame = simPerFrame <= 0 ? dSim : simPerFrame + (dSim - simPerFrame) * 0.1;
        }
        havePrev = true;
        rawPos = State.SatPosKm; rawVel = State.SatVelKmS; rawRot = State.SatRotation;
        rawGmst = State.GmstDeg; rawUtc = State.UtcTime; rawReal = now;
    }

    /// <summary>Carry the last real frame forward by the real time since it arrived (scaled by the stub's speed).</summary>
    void Extrapolate()
    {
        var s = State;
        float elapsed = Mathf.Min(Time.unscaledTime - rawReal, maxExtrapolateSeconds);
        if (elapsed <= 0f || frameInterval <= 0.0005) return;
        double simAdv = simPerFrame / frameInterval * elapsed;      // simulation seconds since the last frame

        // position: turn about the orbit's axis, so a fast stub speed follows the curve instead of a chord
        float r = rawPos.magnitude;
        Vector3 axis = Vector3.Cross(rawPos, rawVel);
        if (r > 1f && axis.sqrMagnitude > 1e-6f)
        {
            float ang = (float)(rawVel.magnitude / r * simAdv * 180.0 / Math.PI);
            var q = Quaternion.AngleAxis(ang, axis.normalized);
            s.SatPosKm = q * rawPos;
            s.SatVelKmS = q * rawVel;
        }

        // attitude: keep turning at the rate seen between the last two frames
        float t = 1f + (float)(elapsed / frameInterval);
        s.SatRotation = Quaternion.SlerpUnclamped(prevRot, rawRot, t);

        s.GmstDeg = rawGmst + 0.00417807462 * simAdv;
        s.EarthRotation = Quaternion.Euler(0f, (float)(-s.GmstDeg) + earthRotationOffsetDeg, 0f);
        s.UtcTime = rawUtc.AddSeconds(simAdv);

        double px = s.SatPosKm.x, py = s.SatPosKm.z, pz = s.SatPosKm.y;   // back to ECI for lat/lon
        s.SubLatDeg = Math.Atan2(pz, Math.Sqrt(px * px + py * py)) * 180.0 / Math.PI;
        double lon = Math.Atan2(py, px) * 180.0 / Math.PI - s.GmstDeg;
        s.SubLonDeg = ((lon + 540.0) % 360.0) - 180.0;
        s.SatToEarthKm = s.SatPosKm.magnitude;
        s.SatToSunKm = (s.SunPosKm - s.SatPosKm).magnitude;
        s.SatToMoonKm = (s.MoonPosKm - s.SatPosKm).magnitude;
        s.EarthDirBody = SpaceMath.WorldToBody(s.SatRotation, (-s.SatPosKm).normalized);
        s.SunDirBody = SpaceMath.WorldToBody(s.SatRotation, (s.SunPosKm - s.SatPosKm).normalized);
        s.MoonDirBody = SpaceMath.WorldToBody(s.SatRotation, (s.MoonPosKm - s.SatPosKm).normalized);
    }

    void RecordHistory(StateFrame f)
    {
        if (f.orbit == null || f.orbit.pos_eci_km == null || f.orbit.pos_eci_km.Length < 3) return;

        // The stub restarted (its numbering began again): the old path no longer applies.
        if (f.seq > 0 && f.seq < lastHistorySeq - 20) { History.Clear(); HistoryVersion++; }
        lastHistorySeq = f.seq;

        var p = new Vector3((float)f.orbit.pos_eci_km[0], (float)f.orbit.pos_eci_km[1], (float)f.orbit.pos_eci_km[2]);
        if (History.Count > 0 && (p - History[History.Count - 1].Eci).magnitude < historyStepKm) return;

        History.Add(new TrailSample
        {
            Eci = p,
            GmstDeg = f.earth != null ? (float)f.earth.gmst_deg : 0f,
            SimSeconds = State.UtcTime.Ticks / (double)TimeSpan.TicksPerSecond,
            Eclipse = f.sun != null && f.sun.in_eclipse,
        });
        if (History.Count > historyMaxSamples) History.RemoveAt(0);
        HistoryVersion++;
    }

    /// <summary>The sky only, from a date: Earth's spin, the Sun and the Moon. No satellite.</summary>
    public static void ComputeSkyOnly(DateTime utc, float earthOffsetDeg, SpaceState s)
    {
        s.UtcTime = utc;
        s.HasSatellite = false;
        s.Seq = 0;

        s.GmstDeg = SpaceMath.GmstDeg(utc);
        s.EarthRadiusKm = 6371.0;
        s.EarthRotation = Quaternion.Euler(0f, (float)(-s.GmstDeg) + earthOffsetDeg, 0f);

        SpaceMath.SunDirEci(utc, out double sx, out double sy, out double sz);
        s.SunDir = SpaceMath.EciToUnity(sx, sy, sz).normalized;
        double sunDist = SpaceMath.SunDistanceKm(utc);
        s.SunPosKm = s.SunDir * (float)sunDist;
        s.EarthToSunKm = sunDist;

        SpaceMath.MoonEciKm(utc, out double mx, out double my, out double mz);
        s.MoonPosKm = SpaceMath.EciToUnity(mx, my, mz);
        s.EarthToMoonKm = Math.Sqrt(mx * mx + my * my + mz * mz);

        s.SatPosKm = Vector3.zero;
        s.SatVelKmS = Vector3.zero;
        s.SatRotation = Quaternion.identity;
        s.InEclipse = false;
        s.SunFraction = 1f;
        s.Valid = true;
    }

    /// <summary>Fill a SpaceState from a frame. Returns false if the frame is unusable.</summary>
    public static bool Compute(StateFrame f, float earthOffsetDeg, SpaceState s)
    {
        if (f == null || f.orbit == null || f.orbit.pos_eci_km == null || f.orbit.pos_eci_km.Length < 3)
            return false;

        s.Seq = f.seq;
        s.HasSatellite = true;
        s.UtcTime = ParseTime(f.sim_time, s.UtcTime);

        // Orbit
        double px = f.orbit.pos_eci_km[0], py = f.orbit.pos_eci_km[1], pz = f.orbit.pos_eci_km[2];
        s.SatPosKm = SpaceMath.EciToUnity(px, py, pz);
        s.SatVelKmS = (f.orbit.vel_eci_km_s != null && f.orbit.vel_eci_km_s.Length >= 3)
            ? SpaceMath.EciToUnity(f.orbit.vel_eci_km_s) : Vector3.zero;
        s.AltKm = f.orbit.alt_km;
        s.SatToEarthKm = Math.Sqrt(px * px + py * py + pz * pz);

        // Attitude
        s.SatRotation = (f.attitude != null && f.attitude.q_body_to_eci != null && f.attitude.q_body_to_eci.Length >= 4)
            ? SpaceMath.QuatToUnity(f.attitude.q_body_to_eci) : Quaternion.identity;
        s.AocsMode = f.attitude != null && f.attitude.aocs_mode != null ? f.attitude.aocs_mode : "";

        // Earth: turning about the north axis by gmst. The sign flips because Unity is left-handed.
        s.GmstDeg = f.earth != null ? f.earth.gmst_deg : 0.0;
        s.EarthRadiusKm = (f.earth != null && f.earth.radius_km > 0) ? f.earth.radius_km : 6371.0;
        s.EarthRotation = Quaternion.Euler(0f, (float)(-s.GmstDeg) + earthOffsetDeg, 0f);

        // Where the satellite is over the Earth, worked out from the position (to check against the stub's lat/lon).
        s.SubLatDeg = Math.Atan2(pz, Math.Sqrt(px * px + py * py)) * 180.0 / Math.PI;
        double lon = Math.Atan2(py, px) * 180.0 / Math.PI - s.GmstDeg;
        s.SubLonDeg = ((lon + 540.0) % 360.0) - 180.0;

        // Sun
        double sunDist = SpaceMath.SunDistanceKm(s.UtcTime);
        if (f.sun != null && f.sun.dir_eci != null && f.sun.dir_eci.Length >= 3)
            s.SunDir = SpaceMath.EciToUnity(f.sun.dir_eci).normalized;
        s.InEclipse = f.sun != null && f.sun.in_eclipse;
        s.SunPosKm = s.SunDir * (float)sunDist;
        s.EarthToSunKm = sunDist;
        s.SatToSunKm = (s.SunPosKm - s.SatPosKm).magnitude;

        // Moon (from the date)
        SpaceMath.MoonEciKm(s.UtcTime, out double mx, out double my, out double mz);
        s.MoonPosKm = SpaceMath.EciToUnity(mx, my, mz);
        s.EarthToMoonKm = Math.Sqrt(mx * mx + my * my + mz * mz);
        s.SatToMoonKm = (s.MoonPosKm - s.SatPosKm).magnitude;

        // Directions seen from the satellite, in body axes
        s.EarthDirBody = SpaceMath.WorldToBody(s.SatRotation, (-s.SatPosKm).normalized);
        s.SunDirBody = SpaceMath.WorldToBody(s.SatRotation, (s.SunPosKm - s.SatPosKm).normalized);
        s.MoonDirBody = SpaceMath.WorldToBody(s.SatRotation, (s.MoonPosKm - s.SatPosKm).normalized);

        if (f.sun != null) s.SunFraction = (float)f.sun.sun_fraction;

        // Deployment. The wing rotation angle (sada) is ignored on purpose.
        if (f.deploy != null)
        {
            var wingPlus = f.deploy.wing_py; var wingMinus = f.deploy.wing_my; var ant = f.deploy.antenna;
            s.WingPlusYState = wingPlus != null && wingPlus.state != null ? wingPlus.state : "STOWED";
            s.WingMinusYState = wingMinus != null && wingMinus.state != null ? wingMinus.state : "STOWED";
            s.WingPlusYFraction = wingPlus != null ? Mathf.Clamp01((float)wingPlus.fraction) : 0f;
            s.WingMinusYFraction = wingMinus != null ? Mathf.Clamp01((float)wingMinus.fraction) : 0f;
            s.AntennaDeployed = ant != null && ant.deployed;
            s.AntennaState = ant != null && ant.state != null ? ant.state : "STOWED";
        }

        // Mission info
        if (f.mission != null)
        {
            s.Phase = f.mission.phase ?? "";
            s.InContact = f.mission.in_contact;
            s.GroundStation = f.mission.ground_station ?? "";
        }

        s.Valid = true;
        return true;
    }

    static DateTime ParseTime(string text, DateTime fallback)
    {
        if (!string.IsNullOrEmpty(text) &&
            DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
            return DateTime.SpecifyKind(t, DateTimeKind.Utc);
        return fallback == default ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : fallback;
    }

    string DebugString()
    {
        var s = State;
        if (!s.Valid) return "SpaceFrame: waiting for telemetry";
        var c = CultureInfo.InvariantCulture;
        if (!s.HasSatellite)
            return "NO TELEMETRY (showing today's sky)\n" +
                   "Earth-Sun " + s.EarthToSunKm.ToString("N0", c) + " km\n" +
                   "Earth-Moon " + s.EarthToMoonKm.ToString("N0", c) + " km";
        return
            "sim " + s.UtcTime.ToString("yyyy-MM-dd HH:mm:ss", c) + "\n" +
            "over lat " + s.SubLatDeg.ToString("F1", c) + "  lon " + s.SubLonDeg.ToString("F1", c) + "\n" +
            "Sat-Earth " + s.SatToEarthKm.ToString("N0", c) + " km\n" +
            "Earth-Sun " + s.EarthToSunKm.ToString("N0", c) + " km\n" +
            "Earth-Moon " + s.EarthToMoonKm.ToString("N0", c) + " km\n" +
            "Earth dir body " + s.EarthDirBody.ToString("F3") + "\n" +
            "Sun dir body " + s.SunDirBody.ToString("F3") + "\n" +
            "Moon dir body " + s.MoonDirBody.ToString("F3") + "\n" +
            "wings +Y " + s.WingPlusYState + " " + s.WingPlusYFraction.ToString("F2", c) +
            "  -Y " + s.WingMinusYState + " " + s.WingMinusYFraction.ToString("F2", c) + "\n" +
            "antenna " + s.AntennaState + "\n" +
            (s.InEclipse ? "ECLIPSE" : "in sunlight");
    }
}
