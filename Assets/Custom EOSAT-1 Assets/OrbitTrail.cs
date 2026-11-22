using System;
using UnityEngine;

/// <summary>
/// Draws a fading line behind the satellite dot.
///
/// Scene setup: make this a CHILD of the Globe pivot (a sibling of GlobeModel and
/// SatelliteDot). The Line Renderer is added automatically.
///
/// Why not a TrailRenderer? A TrailRenderer stores points in WORLD space, so when the
/// user spins the globe the old trail would stay behind and smear. This script stores
/// points in the globe's local space instead, so the trail turns with the globe and
/// stays glued to the map.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class OrbitTrail : MonoBehaviour
{
    [Tooltip("The SatelliteDot object that moves around the globe.")]
    [SerializeField] Transform dot;

    [Header("Look")]
    [SerializeField] Color trailColor = new Color(1f, 0.24f, 0.78f); // hot pink / magenta
    [Tooltip("Line width in metres. Thin lines shimmer in a headset, so don't go much below 0.003.")]
    [SerializeField] float width = 0.004f;
    [Tooltip("Opacity of the oldest end of the trail (0 = fades out completely).")]
    [Range(0f, 1f)]
    [SerializeField] float tailAlpha = 0f;

    [Header("Length")]
    [Tooltip("Max points kept. About 500 is roughly one orbit at the default globe size.")]
    [SerializeField] int maxPoints = 600;
    [Tooltip("Show about this many orbits of trail. 1 = one lap round the globe, so lines never pile up.")]
    [SerializeField] float orbitsToShow = 0.85f;
    [Tooltip("A new point is added after the dot moves this far (metres).")]
    [SerializeField] float minSegment = 0.004f;
    [Tooltip("If the dot jumps farther than this in one step, the trail restarts.")]
    [SerializeField] float resetJumpDistance = 0.3f;

    [Header("History")]
    [Tooltip("When the hub comes back (after the space view), redraw the trail from the history that " +
             "SpaceFrame kept, so there is no gap or straight jump.")]
    [SerializeField] bool rebuildFromHistory = true;

    LineRenderer line;
    Vector3[] points;
    int count;
    SpaceFrame frame;
    SatelliteMarker marker;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = false;                 // points live in this object's local space
        line.alignment = LineAlignment.View;
        line.numCapVertices = 4;
        line.numCornerVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;

        points = new Vector3[Mathf.Max(2, maxPoints)];
        ApplyLook();
    }

    // Keep only the last orbitsToShow laps, measured by the real LENGTH of the line. (Counting points does not work:
    // at a high stub speed the dot jumps further between frames, so the same number of points covers many laps.)
    void TrimToCap()
    {
        if (dot == null || orbitsToShow <= 0f || count < 3) return;
        float r = Mathf.Max(0.01f, transform.InverseTransformPoint(dot.position).magnitude);
        float maxLength = orbitsToShow * 2f * Mathf.PI * r;

        float total = 0f;
        for (int i = 1; i < count; i++) total += (points[i] - points[i - 1]).magnitude;
        if (total <= maxLength) return;

        int drop = 0;
        while (drop < count - 2 && total > maxLength)
        {
            total -= (points[drop + 1] - points[drop]).magnitude;
            drop++;
        }
        if (drop <= 0) return;
        Array.Copy(points, drop, points, 0, count - drop);
        count -= drop;
    }

    void OnEnable()
    {
        TelemetryClient.SessionReset += Clear;
        if (rebuildFromHistory && points != null) RebuildFromHistory();
    }

    void OnDisable() { TelemetryClient.SessionReset -= Clear; }

    /// <summary>Redraw the whole trail from the recorded path (kept by SpaceFrame, which is never switched off).</summary>
    void RebuildFromHistory()
    {
        if (frame == null) frame = FindAnyObjectByType<SpaceFrame>();
        if (marker == null && dot != null) marker = dot.GetComponent<SatelliteMarker>();
        if (frame == null || marker == null) return;
        if (frame.History.Count == 0) { Clear(); return; }        // a new link wiped the history while the hub was off

        var h = frame.History;
        Clear();
        for (int i = 0; i < h.Count; i++)
        {
            // globe-local -> world -> this trail's own space (they may not be the same object or scale)
            Vector3 gl = marker.LocalFor(h[i].Eci.x, h[i].Eci.y, h[i].Eci.z, h[i].GmstDeg);
            Vector3 p = transform.InverseTransformPoint(marker.transform.parent != null
                ? marker.transform.parent.TransformPoint(gl) : gl);
            if (count > 0 && (p - points[count - 1]).magnitude < minSegment) continue;

            if (count == points.Length)
            {
                Array.Copy(points, 1, points, 0, count - 1);
                count--;
            }
            points[count++] = p;
        }
        TrimToCap();
        line.positionCount = count;
        line.SetPositions(points);
    }

    void OnValidate()
    {
        // Lets you tweak colour/width live in Play mode.
        if (Application.isPlaying && line != null) ApplyLook();
    }

    void ApplyLook()
    {
        line.widthMultiplier = width;

        // Index 0 is the OLDEST point, so the gradient runs transparent -> opaque.
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
            new[] { new GradientAlphaKey(tailAlpha, 0f), new GradientAlphaKey(1f, 1f) });
        line.colorGradient = g;
    }

    // LateUpdate runs after SatelliteMarker has moved the dot this frame.
    void LateUpdate()
    {
        if (dot == null) return;

        Vector3 p = transform.InverseTransformPoint(dot.position);

        // Before the first telemetry frame the dot sits at the globe's centre; ignore that.
        if (p.sqrMagnitude < 0.0001f) return;

        if (count > 0)
        {
            float moved = (p - points[count - 1]).magnitude;
            if (moved > resetJumpDistance) Clear();
            else if (moved < minSegment) return;
        }

        if (count == points.Length)
        {
            // Full: drop the oldest point by shifting everything down one slot.
            Array.Copy(points, 1, points, 0, count - 1);
            count--;
        }

        points[count++] = p;
        TrimToCap();
        line.positionCount = count;
        line.SetPositions(points);
    }

    [ContextMenu("Clear Trail")]
    public void Clear()
    {
        count = 0;
        if (line != null) line.positionCount = 0;
    }
}
