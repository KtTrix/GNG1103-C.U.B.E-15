using UnityEngine;

/// <summary>
/// Gives the hub globe a real day side and night side that follow the data (and the real clock when no telemetry has arrived).
/// The globe's material is swapped for the CUBE15/DayNight shader, which shades it from the Sun's direction alone, worked out in
/// the globe's own frame, so it stays right while you spin or tilt the globe and while the Earth turns. It ignores the scene's
/// lights, ambient light and baked lighting, so the night side is genuinely dark, with the city lights picture showing on it.
///
/// Starts itself; nothing to place. The settings below can be changed live in Play mode.
/// </summary>
public class HubGlobeSun : MonoBehaviour
{
    [SerializeField, Range(0f, 1f), Tooltip("How bright the night side is (0 = black).")] float nightSideBrightness = 0.04f;
    [SerializeField, Range(0f, 4f), Tooltip("Strength of the city lights on the night side. 0 = off.")] float cityLights = 1.5f;
    [SerializeField, Range(0f, 2f), Tooltip("Brightness of the day side.")] float dayBrightness = 1f;
    [SerializeField, Range(0.001f, 0.5f), Tooltip("How gradual the line between day and night is.")] float terminatorSoftness = 0.1f;

    SatelliteMarker marker;
    SpaceFrame frame;
    Material mat;
    float nextFind;
    static readonly int SunDirId = Shader.PropertyToID("_SunDir");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("HubGlobeSun");
        DontDestroyOnLoad(go);
        go.AddComponent<HubGlobeSun>();
    }

    void LateUpdate()
    {
        if ((marker == null || frame == null || mat == null) && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 1f;
            if (frame == null) frame = FindAnyObjectByType<SpaceFrame>();
            if (marker == null) marker = FindAnyObjectByType<SatelliteMarker>();
            if (marker != null && mat == null) SetUp();
        }
        if (marker == null || frame == null || mat == null) return;

        var S = frame.State;
        Vector3 d = S.SunDir;                                      // Earth -> Sun, ECI in Unity axes (x, z, y)
        if (d.sqrMagnitude < 0.5f) return;

        // ECI -> Earth-fixed (turn by the Earth's angle) -> the globe's local axes (x = ECEF X, y = ECEF Z, z = ECEF Y),
        // the same mapping the satellite dot uses.
        double g = S.GmstDeg * System.Math.PI / 180.0;
        double c = System.Math.Cos(g), sn = System.Math.Sin(g);
        double X = d.x, Y = d.z, Z = d.y;
        var local = new Vector3((float)(c * X + sn * Y), (float)Z, (float)(-sn * X + c * Y));

        Transform pivot = marker.transform.parent;
        Vector3 world = pivot != null ? pivot.TransformDirection(local) : local;
        if (world.sqrMagnitude < 1e-6f) return;

        mat.SetVector(SunDirId, world.normalized);
        mat.SetFloat("_NightFloor", nightSideBrightness);
        mat.SetFloat("_NightStrength", cityLights);
        mat.SetFloat("_DayBrightness", dayBrightness);
        mat.SetFloat("_Soft", terminatorSoftness);
    }

    // Find the globe's own renderer (not the satellite dot) and swap in the day/night material, keeping its pictures.
    void SetUp()
    {
        var shader = Resources.Load<Shader>("CubeDayNight");
        if (shader == null) { Debug.LogWarning("[HubGlobeSun] CubeDayNight shader not found in a Resources folder."); nextFind = float.MaxValue; return; }
        Transform pivot = marker.transform.parent;
        if (pivot == null) return;

        Renderer globe = null;
        foreach (var r in pivot.GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform == marker.transform || r.sharedMaterial == null) continue;
            if (r.gameObject.name == "GlobeModel") { globe = r; break; }
            if (globe == null && r.sharedMaterial.HasProperty("_BaseMap") && r.sharedMaterial.GetTexture("_BaseMap") != null) globe = r;
        }
        if (globe == null) return;

        var src = globe.sharedMaterial;
        Texture day = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
        Texture night = src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap") : null;
        mat = new Material(shader) { name = "HubGlobe DayNight" };
        if (day != null) mat.SetTexture("_DayMap", day);
        if (night != null) mat.SetTexture("_NightMap", night);
        globe.material = mat;
    }

    void OnDestroy() { if (mat != null) Destroy(mat); }
}
