using UnityEngine;

/// <summary>
/// Makes the satellite easy to find when you are zoomed out in the space view: a slowly pulsing glowing dot on the satellite
/// and a fading trail of where it has been (the same Earth-fixed track the hub draws, so each lap lands west of the last).
/// Both keep the same size on screen whatever the distance, so they never shrink to nothing.
///
/// As you come in close the trail dims and the dot fades out completely once the satellite itself is big enough to make out.
/// (The satellite's own axes grow with distance too; see SatelliteAxes.)
///
/// Every number (sizes, colours, pulse, fade distances) is in Assets/Resources/SpaceMarkerSettings.asset.
/// SpaceWorld adds this to itself. Switched on and off in the menu (Options > Display > Satellite marker, setting "display.beacon").
/// Uses the CUBE15/BeaconUnlit shader from the Resources folder.
/// </summary>
public class SatelliteBeacon : MonoBehaviour
{
    public const string Key = "display.beacon";

    // All the numbers live in Assets/Resources/SpaceMarkerSettings.asset.
    static SpaceMarkerSettings S => SpaceMarkerSettings.Instance;

    SpaceWorld world;
    Transform sat;
    GameObject root;
    Transform dot;
    Material dotMat, lineMat;
    LineRenderer line;
    Texture2D glow;
    bool enabledSetting = true;
    int born;

    Vector3[] inertial = new Vector3[0];     // each history point, in the space world (Earth turning not applied)
    float[] gmst = new float[0];
    Vector3[] pts = new Vector3[0];
    Vector3[] loc = new Vector3[0];
    int lastVersion = -1;
    float lastTrailStrength = -1f, lastTail = -1f;
    Color lastTrailColor;

    public void Init(SpaceWorld w, Transform satellite)
    {
        if (root != null) return;
        world = w;
        sat = satellite;

        var shader = Shader.Find("CUBE15/BeaconUnlit");
        if (shader == null)
        {
            Debug.LogWarning("[Beacon] Shader 'CUBE15/BeaconUnlit' not found (it should be in Assets/Resources). The satellite marker is off.");
            enabled = false;
            return;
        }

        root = new GameObject("Satellite Beacon");
        root.transform.SetParent(transform, false);

        glow = MakeGlow();
        dotMat = new Material(shader) { mainTexture = glow };
        lineMat = new Material(shader);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Dot";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(root.transform, false);
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = dotMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        quad.AddComponent<FaceViewer>();
        dot = quad.transform;

        var lineGo = new GameObject("Trail");
        lineGo.transform.SetParent(root.transform, false);
        line = lineGo.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 3;
        line.numCornerVertices = 2;
        line.sharedMaterial = lineMat;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;

        enabledSetting = CubeSettings.GetBool(Key, true);
        born = Time.frameCount;
        CubeSettings.Changed += OnChanged;
    }

    void OnDestroy()
    {
        CubeSettings.Changed -= OnChanged;
        if (glow != null) Destroy(glow);
        if (dotMat != null) Destroy(dotMat);
        if (lineMat != null) Destroy(lineMat);
    }

    void OnChanged(string key) { if (key == Key) enabledSetting = CubeSettings.GetBool(Key, true); }

    // a soft round glow: bright core, wide faint halo
    static Texture2D MakeGlow()
    {
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Exp(-(r * r) / 0.035f) + 0.35f * Mathf.Exp(-(r * r) / 0.22f);
                a *= Mathf.Clamp01((1f - r) * 4f);                 // nothing at the quad's edge
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    void OnEnable() { SpaceCam.PreRender += LateUpdate; }
    void OnDisable() { SpaceCam.PreRender -= LateUpdate; }

    void LateUpdate()
    {
        if (root == null) return;

        var cam = SpaceCam.Current;
        // Wait a few frames after the scene loads: until then the camera has not been moved to the satellite and the
        // distance-scaled dot would flash huge.
        bool show = enabledSetting && Time.frameCount - born >= 4 && cam != null && world != null && world.HasSatellite && sat != null && sat.gameObject.activeInHierarchy;
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) return;

        Vector3 viewer = cam.transform.position;
        Vector3 satPos = sat.position;
        float d = Mathf.Max(1e-4f, Vector3.Distance(viewer, satPos));

        // how big the satellite itself looks (its model is 0.366 m long)
        float satLen = sat.lossyScale.x * 0.366f;
        float sizeDeg = 2f * Mathf.Atan(0.5f * satLen / d) * Mathf.Rad2Deg;

        float dotVis = 1f - Smooth(S.fullUntilSizeDeg, S.dotGoneAtSizeDeg, sizeDeg);
        float trailVis = Mathf.Lerp(1f, S.trailLowestStrength, Smooth(S.fullUntilSizeDeg, S.trailDimmestAtSizeDeg, sizeDeg));

        UpdateDot(d, satPos, dotVis);
        UpdateTrail(d, trailVis);
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-4f, b - a));
        return t * t * (3f - 2f * t);
    }

    float WorldSize(float distance, float angleDeg) => 2f * distance * Mathf.Tan(angleDeg * 0.5f * Mathf.Deg2Rad);

    void UpdateDot(float d, Vector3 satPos, float vis)
    {
        bool on = vis > 0.01f;
        if (dot.gameObject.activeSelf != on) dot.gameObject.SetActive(on);
        if (!on) return;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f * Mathf.PI / Mathf.Max(0.5f, S.pulseSeconds));
        float size = WorldSize(d, S.dotSizeDeg) * (1f - S.pulseSizeDepth * 0.5f + S.pulseSizeDepth * pulse);
        dot.position = satPos;
        dot.localScale = Vector3.one * (size / Mathf.Max(1e-6f, transform.lossyScale.x));

        var c = S.dotColor;
        c.a = vis * (1f - S.pulseBrightnessDepth + S.pulseBrightnessDepth * pulse);
        dotMat.color = c;
    }

    void UpdateTrail(float d, float strength)
    {
        var frame = world.Frame;
        var s = world.State;
        if (frame == null || s == null || !s.Valid) { line.positionCount = 0; return; }

        var h = frame.History;
        int count = Mathf.Min(h.Count, Mathf.Max(2, S.maxTrailPoints));
        if (count < 2) { line.positionCount = 0; return; }

        float eKm = (float)s.EarthRadiusKm;
        float gNow = (float)s.GmstDeg;

        if (frame.HistoryVersion != lastVersion || inertial.Length != count)
        {
            lastVersion = frame.HistoryVersion;
            if (inertial.Length != count) { inertial = new Vector3[count]; gmst = new float[count]; pts = new Vector3[count + 1]; loc = new Vector3[count + 1]; }
            int start = h.Count - count;
            for (int i = 0; i < count; i++)
            {
                Vector3 e = h[start + i].Eci;
                inertial[i] = world.MapKm(SpaceMath.EciToUnity(e.x, e.y, e.z), eKm);
                gmst[i] = h[start + i].GmstDeg;
            }
        }

        // The track is fixed to the Earth: turn each point by how far the Earth has turned since it was recorded.
        for (int i = 0; i < count; i++)
            loc[i] = Quaternion.Euler(0f, gmst[i] - gNow, 0f) * inertial[i];
        Vector3 head = world.SatPosition;
        loc[count] = head;                                              // the line always ends on the satellite

        // Only the stretch behind the satellite that is within the chosen distance is drawn (walk back from the satellite).
        int first = 0;
        if (S.trailLengthDeg > 0f)
        {
            float limit = S.trailLengthDeg * Mathf.Deg2Rad * head.magnitude;      // along the orbit, in the world's own units
            float run = 0f;
            first = count;
            while (first > 0)
            {
                run += Vector3.Distance(loc[first], loc[first - 1]);
                if (run > limit) break;
                first--;
            }
        }

        int n = count + 1 - first;
        for (int i = 0; i < n; i++) pts[i] = transform.TransformPoint(loc[first + i]);

        if (line.positionCount != n) line.positionCount = n;
        line.SetPositions(pts);
        line.widthMultiplier = WorldSize(d, S.trailWidthDeg);

        if (Mathf.Abs(strength - lastTrailStrength) > 0.01f || S.trailTailAlpha != lastTail || S.trailColor != lastTrailColor)
        {
            lastTrailStrength = strength;
            lastTail = S.trailTailAlpha;
            lastTrailColor = S.trailColor;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(S.trailColor, 0f), new GradientColorKey(S.trailColor, 1f) },
                      new[] { new GradientAlphaKey(S.trailTailAlpha * strength, 0f), new GradientAlphaKey(Mathf.Lerp(S.trailTailAlpha, 1f, 0.5f) * strength, 0.6f), new GradientAlphaKey(strength, 1f) });
            line.colorGradient = g;
        }
    }
}
