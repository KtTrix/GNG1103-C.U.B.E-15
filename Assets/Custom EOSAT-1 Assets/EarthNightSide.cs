using UnityEngine;

/// <summary>
/// Gives the space-view Earth a real day side and night side from the Sun direction in the data (or the real clock without
/// telemetry), the same way HubGlobeSun does for the hub globe. The Earth's material is swapped for the CUBE15/DayNight shader,
/// keeping its day picture and its night-lights picture, so the night side is dark with the city lights showing regardless of
/// the scene's lights and ambient light. The clouds and atmosphere layers are left as they are.
///
/// SpaceWorld adds this to itself; nothing to place by hand. The numbers can be changed live in Play mode.
/// </summary>
public class EarthNightSide : MonoBehaviour
{
    [SerializeField, Range(0f, 1f), Tooltip("How bright the night side is (0 = black).")] float nightSideBrightness = 0.04f;
    [SerializeField, Range(0f, 4f), Tooltip("Strength of the city lights on the night side. 0 = off.")] float cityLights = 1.5f;
    [SerializeField, Range(0f, 2f), Tooltip("Brightness of the day side.")] float dayBrightness = 1f;
    [SerializeField, Range(0.001f, 0.5f), Tooltip("How gradual the line between day and night is.")] float terminatorSoftness = 0.1f;

    SpaceWorld world;
    Material mat;
    static readonly int SunDirId = Shader.PropertyToID("_SunDir");

    public void Init(SpaceWorld w, Transform earth)
    {
        world = w;
        if (earth == null) return;
        var shader = Resources.Load<Shader>("CubeDayNight");
        if (shader == null) { Debug.LogWarning("[EarthNightSide] CubeDayNight shader not found in a Resources folder."); return; }

        // the Earth's own surface: the renderer whose material has a night-lights picture; failing that, the one on the Earth object itself
        Renderer surface = null, own = earth.GetComponent<Renderer>();
        foreach (var r in earth.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.sharedMaterial;
            if (m != null && m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null) { surface = r; break; }
        }
        if (surface == null) surface = own;
        if (surface == null || surface.sharedMaterial == null) { Debug.LogWarning("[EarthNightSide] could not find the Earth's renderer"); return; }

        var src = surface.sharedMaterial;
        Texture day = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
        Texture night = src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap") : null;
        mat = new Material(shader) { name = "Earth DayNight" };
        if (day != null) mat.SetTexture("_DayMap", day);
        if (night != null) mat.SetTexture("_NightMap", night);
        surface.material = mat;
    }

    void LateUpdate()
    {
        if (mat == null || world == null) return;
        Vector3 sun = world.transform.TransformDirection(world.SunDir);
        if (sun.sqrMagnitude < 1e-6f) return;
        mat.SetVector(SunDirId, sun.normalized);
        mat.SetFloat("_NightFloor", nightSideBrightness);
        mat.SetFloat("_NightStrength", cityLights);
        mat.SetFloat("_DayBrightness", dayBrightness);
        mat.SetFloat("_Soft", terminatorSoftness);
    }

    void OnDestroy() { if (mat != null) Destroy(mat); }
}
