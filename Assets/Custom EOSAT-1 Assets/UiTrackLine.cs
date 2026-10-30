using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One point of the ground track. Position is normalised: x = (lon+180)/360, y = (lat+90)/180.</summary>
public struct TrackPoint
{
    public Vector2 pos;
    public bool eclipse;        // spacecraft was in Earth's shadow here
    public bool breakBefore;    // do NOT join to the previous point (the track wrapped around the map edge)
    public double time;         // simulation time in seconds when recorded
}

/// <summary>
/// A UI element that draws a polyline, so the ground track lives inside the canvas like any other
/// UI graphic (it is clipped, scaled and sorted with the panel, and works with the raycaster).
///
/// Put it on a child of the map image, anchors stretched to fill it (all offsets 0).
/// GroundTrackMap feeds it points; you don't call anything on it yourself.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UITrackLine : MaskableGraphic
{
    [Tooltip("Line width in canvas pixels. Thin lines shimmer in a headset, so 4 or more.")]
    [SerializeField] float thickness = 4f;
    [SerializeField] Color sunlitColor  = new Color(0.85f, 0.65f, 0.18f);
    [SerializeField] Color eclipseColor = new Color(0.31f, 0.55f, 0.95f);
    [Tooltip("Opacity of the oldest part of the track. 1 = no fade.")]
    [Range(0f, 1f)]
    [SerializeField] float oldestAlpha = 0.25f;

    List<TrackPoint> points;   // owned by GroundTrackMap

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;   // a line must never block clicks on things behind it
    }

    public void SetPoints(List<TrackPoint> list)
    {
        points = list;
        SetVerticesDirty();
    }

    /// <summary>Call after the list changed so the mesh is rebuilt.</summary>
    public void Refresh() => SetVerticesDirty();

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points == null || points.Count < 2) return;

        Rect r = rectTransform.rect;
        float half = thickness * 0.5f;
        int n = points.Count;

        for (int i = 1; i < n; i++)
        {
            TrackPoint b = points[i];
            if (b.breakBefore) continue;   // don't draw a line across the whole map at the wrap-around

            Vector2 p0 = ToRect(points[i - 1].pos, r);
            Vector2 p1 = ToRect(b.pos, r);
            Vector2 d = p1 - p0;
            float len = d.magnitude;
            if (len < 0.001f) continue;

            // Sideways offset so the line has thickness.
            Vector2 side = new Vector2(-d.y, d.x) / len * half;

            Color c = b.eclipse ? eclipseColor : sunlitColor;
            c.a *= Mathf.Lerp(oldestAlpha, 1f, (float)i / (n - 1));
            Color32 col = c;

            int idx = vh.currentVertCount;
            vh.AddVert(p0 - side, col, Vector2.zero);
            vh.AddVert(p0 + side, col, Vector2.zero);
            vh.AddVert(p1 + side, col, Vector2.zero);
            vh.AddVert(p1 - side, col, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }
    }

    static Vector2 ToRect(Vector2 normalised, Rect r) =>
        new Vector2(r.xMin + normalised.x * r.width, r.yMin + normalised.y * r.height);
}