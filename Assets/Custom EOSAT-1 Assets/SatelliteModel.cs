using UnityEngine;

/// <summary>
/// Builds the EOSAT-1 satellite from simple shapes and animates it from telemetry:
///   * a body (226 x 100 x 366 mm in X, Y, Z),
///   * two solar flaps (209 x 342 mm), one on each of the wide +Y and -Y faces. Stowed, each lies flat on its big face;
///     it swings out 90 degrees about a hinge on the face's -Z edge until it is level with the -Z (top) deck, then stays there.
///     Hinge angle = 90 degrees x fraction (0 = stowed, 1 = deployed). The cells face -Z once out.
///   * four antenna rods on the -Z end that swing out when the antenna deploys,
///   * a camera lens on the +Z end (the imager / nadir axis).
///
/// Axes: body X -> local x, body Y -> local z, body Z -> local y (+Z imager = local +y; flaps: +Y = local +z side, -Y = local -z side).
/// The model is built in real METRES; SpaceWorld scales the whole object. Everything is built in code at start-up.
/// </summary>
public class SatelliteModel : MonoBehaviour
{
    [SerializeField] SpaceFrame frame;

    [Header("Size (metres, body axes)")]
    [SerializeField] Vector3 bodySize = new Vector3(0.226f, 0.100f, 0.366f);   // X, Y, Z (the wide faces are +Y and -Y, where the wings are mounted)
    [Tooltip("How far each flap reaches from its hinge (342 mm: it covers the big face when stowed and sticks out this far when deployed).")]
    [SerializeField] float panelLength = 0.342f;
    [Tooltip("Flap size along the body's X axis (209 mm).")]
    [SerializeField] float panelWidth = 0.209f;
    [Tooltip("The stub's geometry notes suggest four UHF tape-spring elements of about 0.5 m on the -Z corners.")]
    [SerializeField] float antennaLength = 0.25f;

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

        // Imager camera on the +Z (nadir) end (local +y): a round housing ring, a dark lens and a small glint.
        float camY = hy;
        Cyl("CameraRing", transform, new Vector3(0f, camY + 0.006f, 0f), new Vector3(0.088f, 0.006f, 0.088f), antennaMat);
        Cyl("CameraBarrel", transform, new Vector3(0f, camY + 0.016f, 0f), new Vector3(0.066f, 0.006f, 0.066f), bodyMat);
        Cyl("CameraLens", transform, new Vector3(0f, camY + 0.0235f, 0f), new Vector3(0.050f, 0.0015f, 0.050f), lensMat);
        Cyl("CameraGlint", transform, new Vector3(0.010f, camY + 0.0255f, 0.010f), new Vector3(0.012f, 0.0006f, 0.012f), antennaMat);

        // Solar flaps: one on each wide face (+Y / -Y), hinged along the face's -Z edge (parallel to body X = local x).
        // Stowed, the flap lies over the face; it swings out 90 degrees to lie level with the top deck and stays there.
        for (int w = 0; w < 2; w++)
        {
            float side = w == 0 ? 1f : -1f;                       // +Y flap, -Y flap
            var hinge = new GameObject(w == 0 ? "Wing+Y" : "Wing-Y").transform;
            hinge.SetParent(transform, false);
            hinge.localPosition = new Vector3(0f, -hy, side * (hz + Gap));
            wingHinge[w] = hinge;
            BuildPanel(hinge, side);
        }

        // Four antenna elements on the -Z end, one at each corner (assumed: the stub notes put them there; they splay clear of the lens). They are short stubs when stowed and
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

                // The rods do not fold out: they push out in a straight line, so the direction is the same stowed and deployed
                // (only the length grows). They point away from the -Z face (local -y), splayed outward a little.
                Vector3 dir = new Vector3(sx * 0.45f, -0.85f, sz * 0.3f).normalized;
                antennaOut[n] = Quaternion.FromToRotation(Vector3.up, dir);
                antennaStowed[n] = antennaOut[n];
                pivot.localRotation = antennaStowed[n];
                antennaPivot[n] = pivot;
                n++;
            }

        ApplyPose(0f, 0f, 0f);
    }

    // One solar flap, standing up (local +y) from its hinge, panelWidth wide along x and panelLength long. Its thickness sits on
    // the outer side of the hinge (local +side z). The cells are on that outer face: they face outward while stowed and turn to
    // face +Z (zenith) when the flap has swung out.
    void BuildPanel(Transform hinge, float side)
    {
        float t = 0.006f;     // thickness (along local z)
        var holder = new GameObject("Panel").transform;
        holder.SetParent(hinge, false);
        holder.localPosition = new Vector3(0f, panelLength * 0.5f, side * t * 0.5f);

        // ONE mesh for the whole flap: the grey back and sides are one material, the outer face (the solar cells) is another.
        // (It used to be a thin "cells" box laid 0.5 mm over a grey box. Two surfaces that close together share almost the same
        // depth, so the graphics card could not decide which was in front and the wings flickered and showed through themselves.)
        var go = new GameObject("Wing", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(holder, false);
        go.GetComponent<MeshFilter>().sharedMesh = WingMesh(panelWidth, panelLength, t, side);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterials = new[] { backMat, cellMat };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // A flat box (w along x, l along y, t along z). Submesh 0 = back and sides, submesh 1 = the outer face (+z if side > 0, else -z).
    static Mesh WingMesh(float w, float l, float t, float side)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var norms = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tri0 = new System.Collections.Generic.List<int>();
        var tri1 = new System.Collections.Generic.List<int>();
        float hx = w * 0.5f, hy = l * 0.5f, hz = t * 0.5f;

        void Face(System.Collections.Generic.List<int> tris, Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            // corners in order around the face; flip them if the winding would face inward
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var tmp = b; b = d; d = tmp; }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int k = 0; k < 4; k++) norms.Add(n);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        float so = side >= 0f ? 1f : -1f;
        // outer face (cells)
        Face(tri1, new Vector3(0, 0, so), new Vector3(-hx, -hy, so * hz), new Vector3(hx, -hy, so * hz), new Vector3(hx, hy, so * hz), new Vector3(-hx, hy, so * hz));
        // inner face and the four sides (grey)
        Face(tri0, new Vector3(0, 0, -so), new Vector3(-hx, -hy, -so * hz), new Vector3(hx, -hy, -so * hz), new Vector3(hx, hy, -so * hz), new Vector3(-hx, hy, -so * hz));
        Face(tri0, new Vector3(1, 0, 0), new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz), new Vector3(hx, -hy, hz));
        Face(tri0, new Vector3(-1, 0, 0), new Vector3(-hx, -hy, -hz), new Vector3(-hx, hy, -hz), new Vector3(-hx, hy, hz), new Vector3(-hx, -hy, hz));
        Face(tri0, new Vector3(0, 1, 0), new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz));
        Face(tri0, new Vector3(0, -1, 0), new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, -hy, hz), new Vector3(-hx, -hy, hz));

        var m = new Mesh { name = "Solar flap" };
        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetUVs(0, uvs);
        m.subMeshCount = 2;
        m.SetTriangles(tri0, 0);
        m.SetTriangles(tri1, 1);
        m.RecalculateBounds();
        return m;
    }

    // ── updating ─────────────────────────────────────────────────────────────
    int seenJumps; float snapUntil;

    void Update()
    {
        if (!built) return;
        if (seenJumps != Timeline.Jumps) { seenJumps = Timeline.Jumps; snapUntil = Time.unscaledTime + 0.3f; }   // time moved: show the new state at once
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
        if (Time.unscaledTime < snapUntil) { wingFrac0 = target0; wingFrac1 = target1; antennaFrac = antennaOutNow ? 1f : 0f; }
        ApplyPose(wingFrac0, wingFrac1, antennaFrac);
    }

    void ApplyPose(float f0, float f1, float ant)
    {
        PoseWing(0, f0, 1f);
        PoseWing(1, f1, -1f);
        float e = Mathf.SmoothStep(0f, 1f, ant);
        float len = Mathf.Lerp(0.03f, antennaLength, e);
        for (int i = 0; i < 4; i++)
        {
            if (antennaPivot[i] == null) continue;
            antennaPivot[i].localRotation = Quaternion.Slerp(antennaStowed[i], antennaOut[i], e);
            antennaRod[i].localPosition = new Vector3(0f, len * 0.5f, 0f);
            antennaRod[i].localScale = new Vector3(0.006f, len * 0.5f, 0.006f);
        }
    }

    // fraction 0 = stowed (flap down over the big face); 1 = swung out 90 degrees, level with the top deck.
    // Hinge angle = 90 degrees x fraction. Rotation is about the hinge line (local x).
    void PoseWing(int w, float f, float side)
    {
        if (wingHinge[w] == null) return;
        f = Mathf.Clamp01(f);
        wingHinge[w].localRotation = Quaternion.Euler(side * 90f * f, 0f, 0f);
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
                bool border = x < 3 || x >= w - 3 || y < 3 || y >= h - 3;
                bool line = border || (x % 32 < 2) || (y % 32 < 2);
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
