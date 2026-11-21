using UnityEngine;

/// <summary>
/// Builds the EOSAT-1 satellite from simple shapes and animates it from telemetry:
///   * a body (226 x 100 x 366 mm in X, Y, Z),
///   * two solar wings (2 panels each, 209 x 342 mm) that unfold from the wide +Y and -Y faces (fraction 0 = folded, 1 = fully out),
///   * four antenna rods on the -Z end that swing out when the antenna deploys,
///   * a camera lens on the +Z end (the imager / nadir axis).
///
/// Put this on the "Satellite" object in the SpaceView scene (the one SpaceWorld moves). It builds everything
/// in code at start-up, so there is nothing to place by hand. The old cube's MeshRenderer is switched off.
/// The model is built in real METRES; SpaceWorld scales the whole object (uniformly) to the size you want.
///
/// Axes: this object's local axes are Unity's, with body X -> x, body Y -> z, body Z -> y.
///   body +Z (imager, points at Earth when nadir pointing) = local +y
///   wings are on the wide faces: body +Y wing = local +z side, body -Y wing = local -z side
///
/// Solar cells face body +X (the stub's cell normal when the array angle is 0). The wing rotation angle (sada) from the
/// stub is ignored on purpose, as agreed with the team.
/// </summary>
public class SatelliteModel : MonoBehaviour
{
    [SerializeField] SpaceFrame frame;

    [Header("Size (metres, body axes)")]
    [SerializeField] Vector3 bodySize = new Vector3(0.226f, 0.100f, 0.366f);   // X, Y, Z (the wide faces are +Y and -Y, where the wings are mounted)
    [Tooltip("Length of each solar panel segment, measured outward from the body (stub spec: 2 panels of 209 x 342 mm).")]
    [SerializeField] float panelLength = 0.209f;
    [Tooltip("Panel size along the body's Z axis (342 mm in the stub's spec).")]
    [SerializeField] float panelWidth = 0.342f;
    [SerializeField] int segmentsPerWing = 2;
    [Tooltip("The stub's geometry notes suggest four UHF tape-spring elements of about 0.5 m on the -Z corners.")]
    [SerializeField] float antennaLength = 0.5f;

    [Header("Look")]
    [SerializeField] Color bodyColor = new Color(0.78f, 0.74f, 0.62f);
    [SerializeField] Color cellColor = new Color(0.10f, 0.16f, 0.42f);
    [SerializeField] Color backColor = new Color(0.55f, 0.55f, 0.58f);
    [SerializeField] Color antennaColor = new Color(0.85f, 0.85f, 0.88f);
    [SerializeField] Color lensColor = new Color(0.04f, 0.04f, 0.06f);

    [Header("Animation")]
    [Tooltip("How long (real seconds) the antenna takes to swing out or back.")]
    [SerializeField] float antennaSeconds = 1.5f;
    [Tooltip("Smooths the wing fraction so it never jumps between telemetry frames.")]
    [SerializeField] float wingSmoothing = 8f;
    [Tooltip("Show the wings and antenna fully out when no telemetry is flowing (for looking at the model).")]
    [SerializeField] bool previewDeployedWithoutData = true;

    Transform[] wingHinge = new Transform[2];       // 0 = +Y wing, 1 = -Y wing
    Transform[][] wingSegments = new Transform[2][];
    Transform[] antennaPivot = new Transform[4];
    Transform[] antennaRod = new Transform[4];
    Quaternion[] antennaStowed = new Quaternion[4];
    Quaternion[] antennaOut = new Quaternion[4];
    float wingFrac0, wingFrac1, antennaFrac;
    Material baseMat, bodyMat, cellMat, backMat, antennaMat, lensMat;
    Texture2D cellTexture;
    bool built;

    const float Gap = 0.010f;   // space between folded panels so they do not flicker against each other

    void Awake()
    {
        Build();
    }

    void Build()
    {
        if (built) return;
        built = true;
        if (frame == null) frame = FindAnyObjectByType<SpaceFrame>();

        // Borrow the old cube's material as the template (so we use the same shader the project already has).
        var oldRend = GetComponent<MeshRenderer>();
        Material template = oldRend != null ? oldRend.sharedMaterial : null;
        if (oldRend != null) oldRend.enabled = false;
        MakeMaterials(template);

        float bx = bodySize.x, by = bodySize.y, bz = bodySize.z;   // body X, Y, Z lengths
        // Local (Unity) half sizes: x = body X, y = body Z, z = body Y
        float hx = bx * 0.5f, hy = bz * 0.5f, hz = by * 0.5f;

        // Body
        Box("Body", transform, Vector3.zero, new Vector3(bx, bz, by), bodyMat);

        // Imager camera on the +Z end (local +y): a round housing ring, a dark lens and a small glint,
        // like the nadir camera on a 6U cubesat.
        float camY = hy;
        Cyl("CameraRing", transform, new Vector3(0f, camY + 0.006f, 0f), new Vector3(0.088f, 0.006f, 0.088f), antennaMat);
        Cyl("CameraBarrel", transform, new Vector3(0f, camY + 0.016f, 0f), new Vector3(0.066f, 0.006f, 0.066f), bodyMat);
        Cyl("CameraLens", transform, new Vector3(0f, camY + 0.0235f, 0f), new Vector3(0.050f, 0.0015f, 0.050f), lensMat);
        Cyl("CameraGlint", transform, new Vector3(0.010f, camY + 0.0255f, 0.010f), new Vector3(0.012f, 0.0006f, 0.012f), antennaMat);

        // Solar wings, as in the stub's notes: mounted on the body's +Y and -Y faces (the wide 226 x 366 mm faces),
        // each wing 2 folding panels of 209 x 342 mm, folded flat against the face when stowed. The hinge runs along
        // the face's -X edge (parallel to body Z = local y); the panels open outward along +/-Y (local z).
        for (int w = 0; w < 2; w++)
        {
            float side = w == 0 ? 1f : -1f;                       // +Y wing, -Y wing
            var hinge = new GameObject(w == 0 ? "Wing+Y" : "Wing-Y").transform;
            hinge.SetParent(transform, false);
            hinge.localPosition = new Vector3(-hx, 0f, side * (hz + Gap));
            wingHinge[w] = hinge;

            wingSegments[w] = new Transform[Mathf.Max(1, segmentsPerWing)];
            Transform parent = hinge;
            for (int i = 0; i < wingSegments[w].Length; i++)
            {
                var seg = new GameObject("Segment" + i).transform;
                seg.SetParent(parent, false);
                seg.localPosition = i == 0 ? Vector3.zero : new Vector3(panelLength, 0f, 0f);
                wingSegments[w][i] = seg;
                BuildPanel(seg, side, i);
                parent = seg;
            }
        }

        // Four antenna elements on the -Z end, one at each corner. They are short stubs when stowed and
        // grow to full length as they swing out.
        int n = 0;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var pivot = new GameObject("Antenna" + n).transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = new Vector3(sx * (hx - 0.01f), -hy - 0.004f, sz * (hz - 0.01f));
                var rod = Cyl("Rod", pivot, Vector3.zero, new Vector3(0.006f, 0.03f, 0.006f), antennaMat);
                antennaRod[n] = rod;

                // Stowed: lying flat along the -Z face, pointing toward the middle of the long side (local x).
                antennaStowed[n] = Quaternion.FromToRotation(Vector3.up, new Vector3(-sx, 0f, 0f));
                // Deployed: pointing away from the -Z face (local -y), splayed outward.
                Vector3 dir = new Vector3(sx * 0.45f, -0.85f, sz * 0.3f).normalized;
                antennaOut[n] = Quaternion.FromToRotation(Vector3.up, dir);
                pivot.localRotation = antennaStowed[n];
                antennaPivot[n] = pivot;
                n++;
            }

        ApplyPose(0f, 0f, 0f);
    }

    // One solar panel: a thin box running along local +x from the segment's origin (209 mm), 342 mm along local y.
    // Folded, it lies on the face (thickness along local z). Once the wing is out, its flat faces point along body
    // +/-X, and the cells face body +X (the stub's cell normal at a sun-tracking angle of 0).
    void BuildPanel(Transform seg, float side, int index)
    {
        float t = 0.006f;     // thickness (along local z)
        var holder = new GameObject("Panel").transform;
        holder.SetParent(seg, false);
        holder.localPosition = new Vector3(panelLength * 0.5f, 0f, side * t * 0.5f);

        Box("Back", holder, Vector3.zero, new Vector3(panelLength, panelWidth, t), backMat);
        float cellOffset = -side * (t * 0.5f + 0.0005f);
        Box("Cells", holder, new Vector3(0f, 0f, cellOffset), new Vector3(panelLength * 0.94f, panelWidth * 0.97f, 0.001f), cellMat);
    }

    // ── updating ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (!built) return;
        float target0, target1; bool antennaOutNow;
        var s = frame != null ? frame.State : null;
        if (s != null && s.HasSatellite)
        {
            target0 = s.WingPlusYFraction;
            target1 = s.WingMinusYFraction;
            antennaOutNow = s.AntennaDeployed;
        }
        else
        {
            float p = previewDeployedWithoutData ? 1f : 0f;
            target0 = target1 = p;
            antennaOutNow = previewDeployedWithoutData;
        }

        float k = 1f - Mathf.Exp(-wingSmoothing * Time.deltaTime);
        wingFrac0 = Mathf.Lerp(wingFrac0, target0, k);
        wingFrac1 = Mathf.Lerp(wingFrac1, target1, k);
        antennaFrac = Mathf.MoveTowards(antennaFrac, antennaOutNow ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, antennaSeconds));
        ApplyPose(wingFrac0, wingFrac1, antennaFrac);
    }

    void ApplyPose(float f0, float f1, float ant)
    {
        PoseWing(0, f0, 1f);
        PoseWing(1, f1, -1f);
        float e = Mathf.SmoothStep(0f, 1f, ant);
        float len = Mathf.Lerp(0.06f, antennaLength, e);
        for (int i = 0; i < 4; i++)
        {
            if (antennaPivot[i] == null) continue;
            antennaPivot[i].localRotation = Quaternion.Slerp(antennaStowed[i], antennaOut[i], e);
            antennaRod[i].localPosition = new Vector3(0f, len * 0.5f, 0f);
            antennaRod[i].localScale = new Vector3(0.006f, len * 0.5f, 0.006f);
        }
    }

    // fraction 0 = every panel folded flat against the face; 1 = wing straight out along +/-Y.
    void PoseWing(int w, float f, float side)
    {
        if (wingHinge[w] == null) return;
        f = Mathf.Clamp01(f);
        // First panel swings out 90 degrees about the hinge (parallel to local y).
        wingSegments[w][0].localRotation = Quaternion.Euler(0f, -side * 90f * f, 0f);
        // Each later panel starts folded back (180 degrees) and opens to straight (0).
        for (int i = 1; i < wingSegments[w].Length; i++)
        {
            float sign = (i % 2 == 1) ? 1f : -1f;
            wingSegments[w][i].localRotation = Quaternion.Euler(0f, -side * sign * 180f * (1f - f), 0f);
        }
    }

    // ── building blocks ──────────────────────────────────────────────────────
    static Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Prepare(go, name, parent, pos, size, m);
        return go.transform;
    }

    static Transform Cyl(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        // Unity's cylinder is 2 units tall and 1 wide, so "size" here is (width, half-height, width).
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Prepare(go, name, parent, pos, size, m);
        return go.transform;
    }

    static void Prepare(GameObject go, string name, Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    void MakeMaterials(Material template)
    {
        if (template == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            template = new Material(sh);
        }
        bodyMat = Make(template, bodyColor, 0.85f, 0.55f);
        backMat = Make(template, backColor, 0.6f, 0.4f);
        antennaMat = Make(template, antennaColor, 0.9f, 0.6f);
        lensMat = Make(template, lensColor, 0.2f, 0.9f);

        cellMat = Make(template, Color.white, 0.4f, 0.8f);
        cellTexture = MakeCellTexture();
        SetTex(cellMat, cellTexture);
        cellMat.color = cellColor * 1.8f;
    }

    static Material Make(Material template, Color c, float metallic, float smooth)
    {
        var m = new Material(template);
        m.color = c;                                   // sets _BaseColor / _Color
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        return m;
    }

    static void SetTex(Material m, Texture2D t)
    {
        if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", Vector2.one); }
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
    }

    // A grid of dark cells separated by light lines, made in code (no image file needed).
    static Texture2D MakeCellTexture()
    {
        const int w = 64, h = 192;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool line = (x % 32 < 2) || (y % 32 < 2);
                px[y * w + x] = line ? new Color32(190, 195, 205, 255) : new Color32(255, 255, 255, 255);
            }
        t.SetPixels32(px);
        t.Apply(true);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    void OnDestroy()
    {
        if (cellTexture != null) Destroy(cellTexture);
    }
}
