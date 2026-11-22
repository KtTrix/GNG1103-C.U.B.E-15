using UnityEngine;

/// <summary>
/// The red warning sphere around the satellite, with a warning icon on it, shown in every space view while the spin is high.
/// Its look comes from WarningSettings (colour, opacity, edge, size). It never looks smaller than a minimum angle on screen,
/// and at very high spin it becomes dense so it hides the satellite's axes. Added to the satellite by SpaceWorld.
/// </summary>
public class SpinWarningSphere : MonoBehaviour
{
    GameObject sphere, icon;
    Material sphereMat, iconMat;
    float pulse;

    void Awake()
    {
        var shader = Shader.Find("CUBE15/WarningUnlit");
        if (shader == null) { Debug.LogWarning("[Warning] Shader CUBE15/WarningUnlit not found; the sphere is off."); enabled = false; return; }

        sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Spin Warning Sphere";
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(transform, false);
        sphereMat = new Material(shader);
        var sr = sphere.GetComponent<MeshRenderer>();
        sr.sharedMaterial = sphereMat; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sr.receiveShadows = false;

        icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
        icon.name = "Spin Warning Icon";
        Destroy(icon.GetComponent<Collider>());
        icon.transform.SetParent(transform, false);
        iconMat = new Material(shader) { mainTexture = WarningIcon.Texture };
        iconMat.SetColor("_Color", Color.white);
        iconMat.SetFloat("_Rim", 0f);
        iconMat.renderQueue = 3040;
        var ir = icon.GetComponent<MeshRenderer>();
        ir.sharedMaterial = iconMat; ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; ir.receiveShadows = false;

        sphere.SetActive(false); icon.SetActive(false);
    }

    void OnDestroy()
    {
        if (sphereMat != null) Destroy(sphereMat);
        if (iconMat != null) Destroy(iconMat);
    }

    void LateUpdate()
    {
        var m = WarningMonitor.Instance;
        float vis = m != null ? m.SphereVisibility : 0f;
        bool on = vis > 0.001f;
        if (sphere.activeSelf != on) { sphere.SetActive(on); icon.SetActive(on); }
        if (!on) return;

        var S = WarningSettings.Instance;
        float s = Mathf.Max(1e-6f, transform.lossyScale.x);                 // model metres -> world
        float radiusWorld = S.sphereRadius * s;
        var cam = Camera.main;
        Vector3 toCam = Vector3.up;
        if (cam != null)
        {
            Vector3 d = cam.transform.position - transform.position;
            float dist = Mathf.Max(0.0001f, d.magnitude);
            toCam = d / dist;
            float minRadius = dist * Mathf.Tan(S.sphereMinAngleDeg * 0.5f * Mathf.Deg2Rad);   // so it can be seen from far away
            radiusWorld = Mathf.Min(Mathf.Max(radiusWorld, minRadius), radiusWorld * Mathf.Max(1f, S.sphereMaxSizeMultiplier));
        }
        float local = radiusWorld / s;                                      // as a local scale on the satellite
        sphere.transform.localScale = Vector3.one * (local * 2f);
        sphere.transform.localPosition = Vector3.zero;

        pulse += Time.unscaledDeltaTime / Mathf.Max(0.1f, S.spherePulseSeconds);
        float wob = 1f - S.spherePulseDepth * 0.5f * (1f + Mathf.Sin(pulse * Mathf.PI * 2f));
        float opacity = Mathf.Lerp(S.sphereOpacity, S.sphereCoverOpacity, m.CoverAmount) * wob * vis;

        Color c = S.sphereColor; c.a = opacity;
        sphereMat.SetColor("_Color", c);
        sphereMat.SetFloat("_Rim", S.sphereRim);

        // the icon sits on the sphere's surface facing you, nudged up so it is not square on to the satellite
        if (cam != null)
        {
            Vector3 up = PanelManager.Up;
            Vector3 dirWorld = (toCam + up * 0.35f).normalized;
            icon.transform.position = transform.position + dirWorld * radiusWorld * 1.01f;
            icon.transform.rotation = cam.transform.rotation;
            // the parent is scaled, so the world size is set through the local scale
            float w = radiusWorld * 2f * S.iconSizeFraction;
            icon.transform.localScale = Vector3.one * (w / s);
            var ic = Color.white; ic.a = Mathf.Clamp01(vis * 1.2f);
            iconMat.SetColor("_Color", ic);
        }
    }
}
