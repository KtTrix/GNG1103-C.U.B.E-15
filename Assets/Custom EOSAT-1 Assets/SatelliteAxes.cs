using UnityEngine;

/// <summary>
/// The satellite's body axes drawn on the satellite in the space view: X red, Y green, Z blue, each with a letter at the tip.
/// They turn with the satellite, so its attitude can be read at a glance instead of from quaternion numbers.
/// Up close they are the satellite's size; zoomed out they grow so they stay readable around the satellite's dot.
///
/// SpaceWorld adds this to the Satellite object (next to SatelliteModel); nothing to place by hand.
/// Sizes and colours: Assets/Resources/SpaceMarkerSettings.asset (CUBE15 > Select Space Marker Settings).
/// Switched on and off in the menu (Options > Display > Satellite axes, the setting "display.axes").
///
/// The model is built in real metres and the whole object is scaled by SpaceWorld, so the lengths are in model metres.
/// Body X is local x, body Y is local z, body Z (the imager) is local y (see SatelliteModel).
/// </summary>
public class SatelliteAxes : MonoBehaviour
{
    public const string Key = "display.axes";

    // For the first few frames after the space scene loads the camera has not been moved to the satellite yet, so it
    // is hundreds of units away and the distance-scaled arrows would flash huge. They stay hidden until things settle.
    const int WarmUpFrames = 4;

    GameObject root;
    int stamp;
    bool on = true;
    int born;

    void Awake()
    {
        on = CubeSettings.GetBool(Key, true);
        born = Time.frameCount;
        CubeSettings.Changed += OnChanged;
        Rebuild();
    }

    void OnDestroy() { CubeSettings.Changed -= OnChanged; }
    void OnChanged(string key) { if (key == Key) on = CubeSettings.GetBool(Key, true); }

    int seenJumps; float holdUntil;
    readonly System.Collections.Generic.Dictionary<Camera, float> lastK = new System.Collections.Generic.Dictionary<Camera, float>();

    void Rebuild()
    {
        var S = SpaceMarkerSettings.Instance;
        if (root != null) Destroy(root);
        root = AxesGizmo.Build(transform, new[] { Vector3.right, Vector3.forward, Vector3.up },
                               S.satLength, S.satThickness, S.satLabelSize, S.AxisColors, S.SatWords);
        root.SetActive(false);
        stamp = S.GeometryStamp();
    }

    void OnEnable() { SpaceCam.PreRender += LateUpdate; }
    void OnDisable() { SpaceCam.PreRender -= LateUpdate; }

    // Grow with distance: up close the axes are the satellite's size; far away they are scaled up so they keep a readable size.
    void LateUpdate()
    {
        var S = SpaceMarkerSettings.Instance;
        if (S.GeometryStamp() != stamp) Rebuild();        // a value was edited: redraw
        if (root == null) return;

        bool covered = WarningMonitor.Instance != null && WarningMonitor.Instance.AxesCovered;     // spinning too fast to read them
        bool visible = on && !covered && Time.frameCount - born >= WarmUpFrames;
        if (root.activeSelf != visible) root.SetActive(visible);
        if (!visible) return;

        var cam = SpaceCam.Current;
        float k = 1f;
        float s = transform.lossyScale.x;
        if (cam != null && s > 1e-7f)
        {
            float natural = S.satLength * s;                                               // the axes' length in the world at natural size
            float d = Vector3.Distance(cam.transform.position, transform.position);
            float length = natural;
            if (S.satMinAngleDeg > 0f) length = Mathf.Max(length, d * Mathf.Tan(S.satMinAngleDeg * Mathf.Deg2Rad));   // grow when far
            length = Mathf.Min(length, d * S.satMaxFractionOfDistance);                                              // never reach toward you when close
            k = length / natural;
        }
        // Time was moved (a click on the bar): the satellite jumps while the camera is still catching up, so the distance is
        // briefly huge. For a moment each camera keeps the growth it had just before, so the arrows never balloon.
        // Otherwise the growth is exactly what the distance says (remembered per camera, since views have different distances).
        if (seenJumps != Timeline.Jumps) { seenJumps = Timeline.Jumps; holdUntil = Time.unscaledTime + 0.7f; }
        if (cam != null)
        {
            if (Time.unscaledTime < holdUntil && lastK.TryGetValue(cam, out float held)) k = held;
            else lastK[cam] = k;
        }
        root.transform.localScale = Vector3.one * k;
    }
}
