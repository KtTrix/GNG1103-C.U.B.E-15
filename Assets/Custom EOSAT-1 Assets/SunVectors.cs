using UnityEngine;

/// <summary>
/// Two yellow arrows that point at the Sun: one from the Earth's centre and one from the satellite. They keep pointing the
/// same way in space (they do not turn with the Earth or the satellite). Each is switched on and off in the menu
/// (Options > Display > Earth sun vector / Satellite sun vector: settings "display.sunearth" and "display.sunsat").
///
/// SpaceWorld adds this to itself; nothing to place by hand. Colour, length and thickness:
/// Assets/Resources/SpaceMarkerSettings.asset (CUBE15 > Select Space Marker Settings), under "Sun vectors".
/// The satellite's arrow grows as you zoom out (like its axes) so it stays readable, and never reaches toward your face.
/// </summary>
public class SunVectors : MonoBehaviour
{
    public const string EarthKey = "display.sunearth";
    public const string SatKey = "display.sunsat";
    const int WarmUpFrames = 4;

    // Tell the menu rows the defaults from the settings asset as soon as the app starts, so they show the right state
    // even before the space view has loaded.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterDefaults()
    {
        var S = SpaceMarkerSettings.Instance;
        CubeSettings.SetDefault(EarthKey, S.sunEarthOnByDefault ? 1 : 0);
        CubeSettings.SetDefault(SatKey, S.sunSatOnByDefault ? 1 : 0);
    }

    SpaceWorld world;
    Transform satellite;
    float earthRadius;
    GameObject earthArrow, satArrow;
    int stamp, born;
    int seenJumps; float holdUntil, kSmooth = -1f;

    public void Init(SpaceWorld w, Transform sat, float radius)
    {
        if (world != null) return;
        world = w; satellite = sat; earthRadius = radius;
        born = Time.frameCount;
        Rebuild();
    }

    // Both arrows are built one unit long and then scaled to length, so thickness stays in proportion.
    void Rebuild()
    {
        var S = SpaceMarkerSettings.Instance;
        if (earthArrow != null) Destroy(earthArrow);
        if (satArrow != null) Destroy(satArrow);
        earthArrow = AxesGizmo.BuildArrow(transform, "Sun vector (Earth)", 1f,
            S.sunEarthThicknessRadii / Mathf.Max(0.01f, S.sunEarthLengthRadii), S.sunEarthLabelFraction, S.sunEarthColor, S.sunEarthWord);
        satArrow = AxesGizmo.BuildArrow(transform, "Sun vector (satellite)", 1f,
            S.sunSatThickness / Mathf.Max(0.01f, S.sunSatLength), S.sunSatLabelFraction, S.sunSatColor, S.sunSatWord);
        earthArrow.SetActive(false); satArrow.SetActive(false);
        stamp = S.GeometryStamp();
    }

    void OnEnable() { SpaceCam.PreRender += LateUpdate; }
    void OnDisable() { SpaceCam.PreRender -= LateUpdate; }

    void LateUpdate()
    {
        if (world == null) return;
        var S = SpaceMarkerSettings.Instance;
        if (S.GeometryStamp() != stamp) Rebuild();

        // The player's own choice (Options > Display) wins; until they make one, the tick box in the settings asset decides.
        // The menu rows are told the same default, so they show the right state before the player touches them.
        CubeSettings.SetDefault(EarthKey, S.sunEarthOnByDefault ? 1 : 0);
        CubeSettings.SetDefault(SatKey, S.sunSatOnByDefault ? 1 : 0);
        bool earthOn = CubeSettings.GetBool(EarthKey, S.sunEarthOnByDefault);
        bool satOn = CubeSettings.GetBool(SatKey, S.sunSatOnByDefault);

        Vector3 sun = world.SunDir;
        bool ready = Time.frameCount - born >= WarmUpFrames && world.HasData && sun.sqrMagnitude > 1e-6f;
        Quaternion aim = ready ? Quaternion.FromToRotation(Vector3.up, sun.normalized) : Quaternion.identity;

        // from the Earth's centre
        bool showEarth = ready && earthOn;
        if (earthArrow.activeSelf != showEarth) earthArrow.SetActive(showEarth);
        if (showEarth)
        {
            earthArrow.transform.localPosition = Vector3.zero;
            earthArrow.transform.localRotation = aim;
            earthArrow.transform.localScale = Vector3.one * (earthRadius * S.sunEarthLengthRadii);
        }

        // from the satellite
        bool showSat = ready && satOn && world.HasSatellite && satellite != null && satellite.gameObject.activeInHierarchy;
        if (satArrow.activeSelf != showSat) satArrow.SetActive(showSat);
        if (!showSat) return;

        float lossy = Mathf.Max(1e-7f, transform.lossyScale.x);
        // Scale exactly like the satellite's axes (same growth with distance), so the sun arrow always stays in proportion to them.
        float satScale = Mathf.Max(1e-7f, satellite.lossyScale.x);
        float k = 1f;
        var cam = SpaceCam.Current;
        if (cam != null)
        {
            float axesNatural = S.satLength * satScale;
            float d = Vector3.Distance(cam.transform.position, satellite.position);
            float axesLen = axesNatural;
            if (S.satMinAngleDeg > 0f) axesLen = Mathf.Max(axesLen, d * Mathf.Tan(S.satMinAngleDeg * Mathf.Deg2Rad));
            axesLen = Mathf.Min(axesLen, d * S.satMaxFractionOfDistance);
            k = axesLen / Mathf.Max(1e-7f, axesNatural);
        }
        // time was moved (a click on the bar): the satellite jumps while the camera is still catching up, so the distance
        // is briefly huge. Keep the old growth for a moment, then ease to the new one, so the arrows never balloon.
        if (seenJumps != Timeline.Jumps) { seenJumps = Timeline.Jumps; holdUntil = Time.unscaledTime + 0.7f; }
        if (kSmooth < 0f) kSmooth = k;
        else if (Time.unscaledTime >= holdUntil) kSmooth = Mathf.Lerp(kSmooth, k, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
        k = kSmooth;
        float length = S.sunSatLength * satScale * k;
        satArrow.transform.localPosition = transform.InverseTransformPoint(satellite.position);
        satArrow.transform.localRotation = aim;
        satArrow.transform.localScale = Vector3.one * (length / lossy);
    }
}
