using UnityEngine;

/// <summary>
/// The Earth's axes drawn at its centre in the space view: Z (blue) through the north pole, X (red) towards the vernal
/// equinox and Y (green) 90 degrees east of it, the inertial (ECI) frame the telemetry uses. They stay fixed in space
/// while the Earth turns inside them.
///
/// SpaceWorld adds this to itself and gives it the Earth's radius; nothing to place by hand.
/// Sizes and colours: Assets/Resources/SpaceMarkerSettings.asset (CUBE15 > Select Space Marker Settings).
/// Switched on and off in the menu (Options > Display > Earth axes, the setting "display.earthaxes").
/// The space world maps ECI (x, y, z) to Unity (x, z, y), so the arrows go along Unity's x, z and y.
/// </summary>
public class EarthAxes : MonoBehaviour
{
    public const string Key = "display.earthaxes";

    GameObject root;
    float radius;
    int stamp;
    bool on = true;
    int born;

    /// <summary>radius is the Earth's radius in this object's units.</summary>
    public void Init(float earthRadius)
    {
        if (radius > 0f) return;
        radius = earthRadius;
        on = CubeSettings.GetBool(Key, true);
        born = Time.frameCount;
        CubeSettings.Changed += OnChanged;
        Rebuild();
    }

    void OnDestroy() { CubeSettings.Changed -= OnChanged; }
    void OnChanged(string key) { if (key == Key) on = CubeSettings.GetBool(Key, true); }

    void Rebuild()
    {
        var S = SpaceMarkerSettings.Instance;
        if (root != null) Destroy(root);
        root = AxesGizmo.Build(transform, new[] { Vector3.right, Vector3.forward, Vector3.up },
                               radius * S.earthLengthRadii, radius * S.earthThicknessRadii, radius * S.earthLabelRadii, S.AxisColors, S.EarthWords);
        root.SetActive(false);
        stamp = S.GeometryStamp();
    }

    void LateUpdate()
    {
        if (radius <= 0f) return;
        if (SpaceMarkerSettings.Instance.GeometryStamp() != stamp) Rebuild();     // a value was edited: redraw
        bool visible = on && Time.frameCount - born >= 4;                          // wait a few frames after the scene loads
        if (root != null && root.activeSelf != visible) root.SetActive(visible);
    }
}
