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
    [Tooltip("A new point is added after the dot moves this far (metres).")]
    [SerializeField] float minSegment = 0.004f;
    [Tooltip("If the dot jumps farther than this in one step, the trail restarts.")]
    [SerializeField] float resetJumpDistance = 0.3f;

    LineRenderer line;
    Vector3[] points;
    int count;

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