using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the ground-track panel: a flat (equirectangular) world map with the spacecraft marker,
/// its recent track, and the five ground stations from the client's planner.
///
/// Put this on the map image object (the RawImage that shows the world map). That object's
/// RectTransform IS the map, so latitude/longitude are converted to positions inside it:
///     x = (lon + 180) / 360        y = (lat + 90) / 180
/// The map texture must be a standard 2:1 equirectangular image, longitude -180 at the left edge.
/// </summary>
public class GroundTrackMap : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] TelemetryClient telemetry;
    [Tooltip("The map image's RectTransform. Leave empty to use this object.")]
    [SerializeField] RectTransform mapRect;
    [SerializeField] UITrackLine trackLine;
    [Tooltip("A small round Image, child of the map, pivot in the centre.")]
    [SerializeField] RectTransform marker;

    [Header("Track history")]
    [Tooltip("How much past track to keep, in simulation seconds. One orbit is about 5670 s.")]
    [SerializeField] float historySeconds = 5670f;
    [Tooltip("Add a track point every this many simulation seconds. Lower = smoother but heavier.")]
    [SerializeField] float sampleEverySimSeconds = 15f;

    [Header("Ground stations")]
    [SerializeField] bool showStations = true;
    [SerializeField] bool showStationLabels = true;
    [Tooltip("A round sprite for the dots (the built-in 'Knob' works). Empty = square dots.")]
    [SerializeField] Sprite dotSprite;
    [SerializeField] Color stationColor = new Color(0.34f, 0.67f, 1f);
    [SerializeField] Color stationLabelColor = new Color(0.62f, 0.70f, 0.78f);
    [SerializeField] float stationDotSize = 12f;
    [SerializeField] float labelFontSize = 18f;

    // Same five stations (and positions) as the simulator's stub.
    static readonly (string name, float lat, float lon)[] Stations =
    {
        ("Iqaluit",   63.747f,  -68.518f),
        ("Troll",    -72.012f,    2.535f),
        ("Svalbard",  78.2297f,  15.3975f),
        ("Inuvik",    68.3195f, -133.5495f),
        ("O'Higgins", -63.3211f, -57.9008f),
    };

    readonly List<TrackPoint> points = new List<TrackPoint>();
    int lastFrameCount = -1;
    double lastTime = double.NegativeInfinity;
    double lastSampleTime = double.NegativeInfinity;
    float lastSampleLon;

    void Start()
    {
        if (mapRect == null) mapRect = GetComponent<RectTransform>();
        if (telemetry == null) telemetry = FindFirstObjectByType<TelemetryClient>();

        if (trackLine != null) trackLine.SetPoints(points);
        if (marker != null) marker.gameObject.SetActive(false);   // hidden until the first frame arrives
        if (showStations) BuildStations();
    }

    void Update()
    {
        if (telemetry == null || telemetry.Latest == null) return;
        if (telemetry.FramesReceived == lastFrameCount) return;
        lastFrameCount = telemetry.FramesReceived;
        OnFrame(telemetry.Latest);
    }

    void OnFrame(StateFrame f)
    {
        float lat = (float)f.orbit.lat_deg;
        float lon = (float)f.orbit.lon_deg;
        Vector2 n = ToNormalised(lat, lon);

        double t = GetSimSeconds(f);

        // The instructor can reload a saved point (clock goes backwards) or jump far ahead.
        // The old track would then be wrong, so start it again.
        if (t < lastTime - 1.0 || t - lastTime > historySeconds)
            ClearTrack();
        lastTime = t;

        if (points.Count == 0 || t - lastSampleTime >= sampleEverySimSeconds)
        {
            bool wrapped = points.Count == 0 || Mathf.Abs(lon - lastSampleLon) > 180f;
            points.Add(new TrackPoint
            {
                pos = n,
                eclipse = f.sun != null && f.sun.in_eclipse,
                breakBefore = wrapped,
                time = t,
            });
            lastSampleTime = t;
            lastSampleLon = lon;

            while (points.Count > 1 && t - points[0].time > historySeconds)
                points.RemoveAt(0);

            if (trackLine != null) trackLine.Refresh();
        }

        if (marker != null)
        {
            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
            marker.anchorMin = marker.anchorMax = n;
            marker.anchoredPosition = Vector2.zero;
        }
    }

    void ClearTrack()
    {
        points.Clear();
        lastSampleTime = double.NegativeInfinity;
        if (trackLine != null) trackLine.Refresh();
    }

    /// <summary>Simulation time in seconds, from the frame's sim_time (falls back to met_s).</summary>
    static double GetSimSeconds(StateFrame f)
    {
        if (!string.IsNullOrEmpty(f.sim_time) &&
            DateTime.TryParse(f.sim_time, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime dt))
            return dt.Ticks / (double)TimeSpan.TicksPerSecond;

        return f.met_s;
    }

    static Vector2 ToNormalised(float latDeg, float lonDeg) =>
        new Vector2(Mathf.Clamp01((lonDeg + 180f) / 360f), Mathf.Clamp01((latDeg + 90f) / 180f));

    // ── ground stations, created at start so there is nothing to place by hand ──
    void BuildStations()
    {
        foreach (var s in Stations)
        {
            var go = new GameObject("Station_" + s.name, typeof(RectTransform), typeof(Image));
            go.layer = mapRect.gameObject.layer;

            var rt = (RectTransform)go.transform;
            rt.SetParent(mapRect, false);
            rt.anchorMin = rt.anchorMax = ToNormalised(s.lat, s.lon);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(stationDotSize, stationDotSize);

            var img = go.GetComponent<Image>();
            img.sprite = dotSprite;
            img.color = stationColor;
            img.raycastTarget = false;

            if (!showStationLabels) continue;

            var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lgo.layer = go.layer;
            var lrt = (RectTransform)lgo.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(stationDotSize * 0.8f, 0f);
            lrt.sizeDelta = new Vector2(200f, labelFontSize * 1.4f);

            var text = lgo.GetComponent<TextMeshProUGUI>();
            text.text = s.name;
            text.fontSize = labelFontSize;
            text.color = stationLabelColor;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
        }
    }
}