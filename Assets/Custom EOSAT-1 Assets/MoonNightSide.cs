using UnityEngine;

/// <summary>
/// The Moon's day and night sides, from the real Sun direction in the telemetry (or the real clock without telemetry).
///
///  - The Moon's material is swapped for the CUBE15/DayNight shader, so the lit side and the line between day and night follow
///    the Sun and Moon positions exactly, regardless of the scene's lights and ambient light.
///  - The night side is not left pure black: it shows the Moon's surface faintly, tinted blue-grey, as earthshine (sunlight
///    bounced off the Earth). It is brightest when the Earth is full in the Moon's sky (a new Moon) and fades to nothing at full Moon.
///  - The Moon keeps one face toward the Earth.
///
/// SpaceWorld adds this to itself; nothing to place by hand. The numbers can be changed live in Play mode.
/// </summary>
public class MoonNightSide : MonoBehaviour
{
    [Tooltip("Brightness of the night side when the Earth is full in the Moon's sky. 0 = pure black.")]
    [SerializeField, Range(0f, 0.5f)] float earthshine = 0.12f;
    [Tooltip("Brightness of the night side even when the Earth is a thin crescent in the Moon's sky.")]
    [SerializeField, Range(0f, 0.2f)] float minimumNightSide = 0.02f;
    [SerializeField, Range(0f, 2f)] float dayBrightness = 1f;
    [SerializeField, Range(0.001f, 0.5f)] float terminatorSoftness = 0.04f;
    [Tooltip("Keep the Moon's near side toward the Earth.")]
    [SerializeField] bool faceEarth = true;
    [Tooltip("Turn the Moon about its own axis if the dark seas are on the wrong side. 0, 90, 180 or 270.")]
    [SerializeField] float nearSideYawDegrees = 0f;

    SpaceWorld world;
    Transform moon;
    Material mat;
    static readonly int SunDirId = Shader.PropertyToID("_SunDir");

    public void Init(SpaceWorld w, Transform moonTransform)
    {
        world = w; moon = moonTransform;
        if (moon == null) return;
        var r = moon.GetComponentInChildren<Renderer>();
        if (r == null) { Debug.LogWarning("[MoonNightSide] the Moon has no renderer"); return; }
        var shader = Resources.Load<Shader>("CubeDayNight");
        if (shader == null) { Debug.LogWarning("[MoonNightSide] CubeDayNight shader not found in a Resources folder."); return; }

        var src = r.sharedMaterial;
        Texture surface = src != null ? (src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture) : null;
        mat = new Material(shader) { name = "Moon DayNight" };
        if (surface != null) mat.SetTexture("_DayMap", surface);
        mat.SetColor("_NightFloorTint", new Color(0.62f, 0.72f, 1f, 1f));
        r.material = mat;
    }

    void LateUpdate()
    {
        if (moon == null || mat == null || world == null) return;

        Vector3 toMoon = moon.localPosition;                        // Earth -> Moon, in the space world's frame
        if (toMoon.sqrMagnitude < 1e-6f) return;
        Vector3 dirToMoon = toMoon.normalized;

        // The Earth seen from the Moon is full when the Moon is between the Earth and the Sun.
        float full = Mathf.Clamp01((1f + Vector3.Dot(world.SunDir, dirToMoon)) * 0.5f);
        mat.SetVector(SunDirId, world.transform.TransformDirection(world.SunDir).normalized);
        mat.SetFloat("_NightFloor", minimumNightSide + earthshine * full);
        mat.SetFloat("_DayBrightness", dayBrightness);
        mat.SetFloat("_Soft", terminatorSoftness);

        if (faceEarth)
        {
            Quaternion look = Quaternion.LookRotation(-dirToMoon, Vector3.up);
            moon.localRotation = look * Quaternion.Euler(0f, nearSideYawDegrees, 0f);
        }
    }

    void OnDestroy() { if (mat != null) Destroy(mat); }
}
