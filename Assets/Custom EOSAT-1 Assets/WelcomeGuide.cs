using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// The controllers picture on the welcome panel: a real render of the headset's controllers (made from the Touch / Touch
/// Plus / Touch Pro models), with a pointer from every button to what it does. The words follow the player's own button
/// choices (Options > Controls > Button mapping), and a button's dot lights up while you press it on the real controller.
/// Builds itself; the panel only needs this component with the references below (made by CUBE15 > Panels > New Welcome Panel).
/// The pictures are in Assets/Resources/WelcomeControllers (quest2, quest3, questpro).
/// </summary>
public class WelcomeGuide : MonoBehaviour
{
    public RectTransform diagram;       // empty area the picture and its labels are drawn into
    public TMP_Text stepsText;          // optional: a "first steps" list (not used at the moment)
    public TMP_Text headsetText;        // small line above the picture: which controllers were found

    static readonly Color Idle = Color.white;
    static readonly Color Ring = new Color(0.07f, 0.08f, 0.10f);
    static readonly Color Lit = CubeTheme.Accent;

    // Where each button is on each picture (0..1 across, 0..1 down), measured from the renders.
    static readonly Dictionary<string, Dictionary<string, Vector2>> Anchors = new Dictionary<string, Dictionary<string, Vector2>>
    {
        { "quest2", new Dictionary<string, Vector2> { { "L.thumbstick", new Vector2(0.1595f, 0.3478f) }, { "L.y_button", new Vector2(0.2445f, 0.3946f) }, { "L.x_button", new Vector2(0.2017f, 0.4126f) }, { "L.menu", new Vector2(0.1256f, 0.4103f) }, { "L.trigger", new Vector2(0.2299f, 0.4852f) }, { "L.squeeze", new Vector2(0.1922f, 0.6056f) }, { "R.thumbstick", new Vector2(0.84f, 0.348f) }, { "R.b_button", new Vector2(0.755f, 0.3944f) }, { "R.a_button", new Vector2(0.798f, 0.4125f) }, { "R.menu", new Vector2(0.8728f, 0.4098f) }, { "R.trigger", new Vector2(0.7692f, 0.4852f) }, { "R.squeeze", new Vector2(0.8073f, 0.6055f) } } },
        { "quest3", new Dictionary<string, Vector2> { { "L.thumbstick", new Vector2(0.1334f, 0.0739f) }, { "L.y_button", new Vector2(0.2156f, 0.1159f) }, { "L.x_button", new Vector2(0.165f, 0.1542f) }, { "L.menu", new Vector2(0.0736f, 0.1622f) }, { "L.trigger", new Vector2(0.2168f, 0.2551f) }, { "L.squeeze", new Vector2(0.1773f, 0.4555f) }, { "R.thumbstick", new Vector2(0.8652f, 0.0736f) }, { "R.b_button", new Vector2(0.7831f, 0.1164f) }, { "R.a_button", new Vector2(0.8338f, 0.1543f) }, { "R.menu", new Vector2(0.9236f, 0.1605f) }, { "R.trigger", new Vector2(0.7819f, 0.2555f) }, { "R.squeeze", new Vector2(0.8221f, 0.4557f) } } },
        { "questpro", new Dictionary<string, Vector2> { { "L.thumbstick", new Vector2(0.1393f, 0.0773f) }, { "L.y_button", new Vector2(0.2157f, 0.1119f) }, { "L.x_button", new Vector2(0.1667f, 0.153f) }, { "L.menu", new Vector2(0.0772f, 0.1674f) }, { "L.trigger", new Vector2(0.2176f, 0.2725f) }, { "L.squeeze", new Vector2(0.1775f, 0.4584f) }, { "R.thumbstick", new Vector2(0.8607f, 0.0773f) }, { "R.b_button", new Vector2(0.7843f, 0.1119f) }, { "R.a_button", new Vector2(0.8333f, 0.153f) }, { "R.menu", new Vector2(0.9211f, 0.1662f) }, { "R.trigger", new Vector2(0.7824f, 0.2725f) }, { "R.squeeze", new Vector2(0.8225f, 0.4584f) } } }
    };

    // (key, title, anchor name, fixed text)
    static readonly string[][] LeftParts =
    {
        new[] { "L.stick", "Thumbstick", "L.thumbstick", "Walk / Stations" },
        new[] { "L.y", "Y button", "L.y_button", null },
        new[] { "L.menu", "Menu button", "L.menu", null },
        new[] { "L.x", "X button", "L.x_button", null },
        new[] { "L.trig", "Trigger", "L.trigger", "Select (on the back)" },
        new[] { "L.grip", "Grip", "L.squeeze", null },
    };
    static readonly string[][] RightParts =
    {
        new[] { "R.stick", "Thumbstick", "R.thumbstick", "Turn / Zoom" },
        new[] { "R.b", "B button", "R.b_button", null },
        new[] { "R.meta", "Meta button", "R.menu", "System (fixed)" },
        new[] { "R.a", "A button", "R.a_button", null },
        new[] { "R.trig", "Trigger", "R.trigger", "Select (on the back)" },
        new[] { "R.grip", "Grip", "R.squeeze", null },
    };

    [System.Serializable]
    public class Part
    {
        public string key, title, fixedText;
        public Image dot;
        public TMP_Text label;
    }

    [Tooltip("Filled in by right-click > Build editable layout. If it holds anything, the guide uses it as it is (move, resize and restyle the objects under Diagram freely).")]
    [SerializeField] List<Part> parts = new List<Part>();
    [Tooltip("Which controllers the editable layout is drawn for: quest2, quest3 or questpro.")]
    [SerializeField] string layoutFor = "quest2";
    readonly Dictionary<string, Part> byKey = new Dictionary<string, Part>();
    bool built;
    string profile = "";
    static Sprite circleSprite, outlineSprite;

    // a thin ring, so the real button stays visible inside it
    static Sprite Outline(float thickness)
    {
        if (outlineSprite != null && thickness == 0.14f) return outlineSprite;
        const int n = 96;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        float outer = n / 2f, inner = outer * (1f - thickness) ;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
            px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner));
        }
        t.SetPixels32(px); t.Apply(true, true);
        var sp = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        if (thickness == 0.14f) outlineSprite = sp;
        return sp;
    }

    // ── set-up ───────────────────────────────────────────────────────────────
    void OnEnable()
    {
        Build();
        Refresh();
        CubeSettings.Changed += OnChanged;
    }

    void OnDisable() { CubeSettings.Changed -= OnChanged; }

    void OnChanged(string key) { if (key != null && key.StartsWith("bind.")) Refresh(); }

    static Sprite Circle()
    {
        if (circleSprite != null) return circleSprite;
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
            px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d));
        }
        t.SetPixels32(px); t.Apply(true, true);
        return circleSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    Image Dot(string name, Vector2 pos, float size, Color c, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(diagram, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(size, size);
        var im = go.GetComponent<Image>();
        im.sprite = sprite != null ? sprite : Circle(); im.color = c; im.raycastTarget = false;
        return im;
    }

    void Line(Vector2 a, Vector2 b, Color c)
    {
        var go = new GameObject("Pointer", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(diagram, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = a;
        Vector2 d = b - a;
        rt.sizeDelta = new Vector2(d.magnitude, 2.5f);
        rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        var im = go.GetComponent<Image>(); im.color = c; im.raycastTarget = false;
    }

    // a dashed curve (quadratic) from a to b bending towards c: the path of a button that is hidden behind the controller
    void Curve(Vector2 a, Vector2 c, Vector2 b, Color col)
    {
        const int n = 28;
        Vector2 prev = a;
        for (int i = 1; i <= n; i++)
        {
            float t = i / (float)n, u = 1f - t;
            Vector2 p = u * u * a + 2f * u * t * c + t * t * b;
            if (i % 2 == 1) Line(prev, p, col);
            prev = p;
        }
    }

    TMP_Text Label(Vector2 centre, Vector2 size, TextAlignmentOptions align)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(diagram, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = centre; rt.sizeDelta = size;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.alignment = align; t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.enableAutoSizing = true; t.fontSizeMin = 16; t.fontSizeMax = 25;
        t.overflowMode = TextOverflowModes.Truncate;
        return t;
    }

    // An older welcome panel still has the "First steps" card: hide it and give its room to the overview.
    void TidyOldPanel()
    {
        var root = GetComponentInParent<Canvas>();
        if (root == null) return;
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt.name == "Steps Card") rt.gameObject.SetActive(false);
            else if (rt.name == "Overview Card") rt.anchorMax = new Vector2(0.97f, rt.anchorMax.y);
        }
    }

    static string DetectProfile()
    {
        var d = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!d.isValid) return "quest2";
        string low = (d.name + " " + d.manufacturer).ToLowerInvariant();
        if (low.Contains("touch pro") || low.Contains("quest pro")) return "questpro";
        if (low.Contains("touch plus") || low.Contains("quest 3")) return "quest3";
        return "quest2";
    }

    void Build()
    {
        if (built || diagram == null) return;
        built = true;
        TidyOldPanel();
        if (parts.Count > 0)                    // a layout saved in the scene: use it exactly as you left it
        {
            foreach (var p in parts) if (p != null && !string.IsNullOrEmpty(p.key)) byKey[p.key] = p;
            profile = string.IsNullOrEmpty(layoutFor) ? "quest2" : layoutFor;
            // the ring pictures are made in code and can't be saved in the scene, so give them back
            foreach (var p in parts) if (p != null && p.dot != null) p.dot.sprite = Outline(0.14f);
            foreach (var im in diagram.GetComponentsInChildren<Image>(true))
                if (im.name.EndsWith(" Ring")) im.sprite = Outline(0.2f);
            return;
        }
        BuildLayout();
    }

    /// <summary>Right-click this component's header > "Build editable layout": makes the picture, labels, dots and pointer
    /// lines as ordinary objects under the diagram, so you can move, resize and restyle them yourself.</summary>
    [ContextMenu("Build editable layout")]
    void BuildEditableLayout()
    {
        if (diagram == null) { Debug.LogWarning("[Welcome] Set the Diagram field first."); return; }
        for (int i = diagram.childCount - 1; i >= 0; i--) DestroyImmediate(diagram.GetChild(i).gameObject);
        parts.Clear(); byKey.Clear();
        BuildLayout();
        Refresh();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        Debug.Log("[Welcome] Editable layout built under '" + diagram.name + "'. Move things in the Scene/Inspector, then save the scene.");
    }

    /// <summary>Removes the saved layout, so the guide draws its own at run time again.</summary>
    [ContextMenu("Clear editable layout")]
    void ClearEditableLayout()
    {
        if (diagram == null) return;
        for (int i = diagram.childCount - 1; i >= 0; i--) DestroyImmediate(diagram.GetChild(i).gameObject);
        parts.Clear(); byKey.Clear();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    void BuildLayout()
    {
        Canvas.ForceUpdateCanvases();
        Vector2 size = diagram.rect.size;
        if (size.x < 200f || size.y < 120f) size = new Vector2(1222f, 500f);
        float W = size.x, H = size.y;

        profile = Application.isPlaying ? DetectProfile() : layoutFor;
        var tex = Resources.Load<Texture2D>("WelcomeControllers/" + profile);
        if (tex == null) { profile = "quest2"; tex = Resources.Load<Texture2D>("WelcomeControllers/quest2"); }
        if (tex == null) { Debug.LogWarning("[Welcome] No controller pictures in Assets/Resources/WelcomeControllers."); return; }

        var anchors = Anchors[profile];

        // Show only the band of the picture that holds the buttons, so they come out as large as the space allows.
        float yMin = 1f, yMax = 0f;
        foreach (var a in anchors.Values) { yMin = Mathf.Min(yMin, a.y); yMax = Mathf.Max(yMax, a.y); }
        float cropTop = 0f, cropBot = 1f, band = 1f;                      // whole picture: the rings reach well above the buttons
        float aspect = tex.width / (tex.height * band);                 // width / height of the cropped picture
        const float minColumn = 215f;                                   // room kept for the words on each side
        float picW = Mathf.Min(W - 2f * minColumn, H * 0.96f * aspect);
        float picH = picW / aspect;
        Vector2 origin = new Vector2((W - picW) * 0.5f, (H - picH) * 0.5f);
        var go = new GameObject("Controllers Image", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(diagram, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        rt.anchoredPosition = origin; rt.sizeDelta = new Vector2(picW, picH);
        var raw = go.GetComponent<RawImage>(); raw.texture = tex; raw.raycastTarget = false;
        raw.uvRect = new Rect(0f, 1f - cropBot, 1f, band);

        float colW = origin.x - 24f;
        var pointer = new Color(CubeTheme.Dim.r, CubeTheme.Dim.g, CubeTheme.Dim.b, 0.9f);

        foreach (bool left in new[] { true, false })
        {
            var list = left ? LeftParts : RightParts;
            var pts = new List<(string[] def, Vector2 at)>();
            foreach (var def in list)
            {
                Vector2 a = anchors[def[2]];
                pts.Add((def, origin + new Vector2(a.x * picW, (1f - (a.y - cropTop) / band) * picH)));
            }
            pts.Sort((p, q) => q.at.y.CompareTo(p.at.y));

            float gapY = Mathf.Min(72f, (H - 6f) / 6f);
            var ys = new float[pts.Count];
            float prev = H - gapY * 0.5f + gapY;
            for (int i = 0; i < pts.Count; i++)
            {
                float y = Mathf.Min(pts[i].at.y, prev - gapY);
                ys[i] = y; prev = y;
            }
            float floor = gapY * 0.5f;
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                float lowest = floor + (pts.Count - 1 - i) * gapY;
                if (ys[i] < lowest) ys[i] = lowest;
            }

            for (int i = 0; i < pts.Count; i++)
            {
                var def = pts[i].def;
                string side = left ? "Left " : "Right ";
                var part = new Part { key = def[0], title = def[1], fixedText = def[3] };

                Vector2 centre = new Vector2(left ? (colW - 6f) * 0.5f : W - (colW - 6f) * 0.5f, ys[i]);
                part.label = Label(centre, new Vector2(colW - 18f, gapY - 4f), left ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
                part.label.name = side + def[1] + " Label";

                var lineGo = new GameObject(side + def[1] + " Pointer", typeof(RectTransform), typeof(WelcomeLine));
                lineGo.transform.SetParent(diagram, false);
                var ring = Dot(side + def[1] + " Ring", pts[i].at, 30f, Ring, Outline(0.2f));
                part.dot = Dot(side + def[1] + " Dot", pts[i].at, 22f, Idle, Outline(0.14f));

                var wl = lineGo.GetComponent<WelcomeLine>();
                wl.from = part.label.rectTransform; wl.fromEdge = left ? 1 : -1;
                wl.to = ring.rectTransform; wl.color = pointer;
                // the trigger is under the handle, out of sight from here: its pointer bends round the back, dashed
                if (def[0].EndsWith(".trig")) wl.bend = new Vector2((left ? -1f : 1f) * 0.12f * picW, -0.30f * picH);
                wl.Apply();

                parts.Add(part); byKey[part.key] = part;
            }
        }
    }


    // ── what each button does (follows the player's own choices) ─────────────
    static void ParsePath(string path, out string hand, out string ctrl)
    {
        hand = path.Contains("{LeftHand}") ? "L" : path.Contains("{RightHand}") ? "R" : "";
        int i = path.LastIndexOf('/');
        ctrl = (i >= 0 ? path.Substring(i + 1) : path).Trim('{', '}').ToLowerInvariant();
        if (ctrl == "menubutton") ctrl = "menu";
        if (ctrl == "joystickclicked" || ctrl == "primary2daxisclick") ctrl = "thumbstickclicked";
    }

    static IEnumerable<string> KeysFor(string hand, string ctrl)
    {
        string[] hands = hand == "" ? new[] { "L", "R" } : new[] { hand };
        foreach (var h in hands)
            switch (ctrl)
            {
                case "primarybutton": yield return h == "L" ? "L.x" : "R.a"; break;
                case "secondarybutton": yield return h == "L" ? "L.y" : "R.b"; break;
                case "menu": if (h == "L") yield return "L.menu"; break;
                case "grippressed": case "grip": yield return h + ".grip"; break;
                case "triggerpressed": case "trigger": yield return h + ".trig"; break;
                case "thumbstickclicked": yield return h + ".stick"; break;
            }
    }

    void Refresh()
    {
        if (parts.Count == 0) return;
        var uses = new Dictionary<string, List<string>>();
        foreach (var e in ControlMap.Entries)
        {
            string path = ControlMap.Get(e.id);
            if (ControlMap.IsNone(path)) continue;
            ParsePath(path, out string hand, out string ctrl);
            foreach (var k in KeysFor(hand, ctrl))
            {
                if (!uses.TryGetValue(k, out var l)) uses[k] = l = new List<string>();
                l.Add(e.label);
            }
        }

        string dim = CubeTheme.HexString(CubeTheme.Dim), acc = CubeTheme.HexString(CubeTheme.Accent);
        foreach (var p in parts)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(p.fixedText)) lines.Add(p.fixedText);
            if (uses.TryGetValue(p.key, out var l)) lines.AddRange(l);
            string fn = lines.Count > 0 ? string.Join(" / ", lines) : "Not used";
            p.label.text = "<b><color=#" + acc + ">" + p.title + "</color></b>\n<color=#" + dim + ">" + fn + "</color>";
        }

        if (stepsText != null) stepsText.text = "";

        if (headsetText != null)
        {
            string kind = profile == "questpro" ? "Meta Quest Pro" : profile == "quest3" ? "Meta Quest 3 / 3S" : "Meta Quest 2";
            bool found = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).isValid;
            headsetText.text = "<cspace=0.12em><b>YOUR CONTROLLERS</b></cspace>   <color=#" + dim + ">" +
                (found ? kind + ". Press a button and its dot lights up."
                       : "No headset found: showing the " + kind + " controllers.") + "</color>";
        }
    }

    // ── lighting up what you press ───────────────────────────────────────────
    void Update()
    {
        if (!built) return;
        Light(XRNode.LeftHand, "L");
        Light(XRNode.RightHand, "R");
    }

    void Light(XRNode node, string h)
    {
        var d = InputDevices.GetDeviceAtXRNode(node);
        bool a = false, b = false, click = false, menu = false; float trig = 0, grip = 0; Vector2 axis = Vector2.zero;
        if (d.isValid)
        {
            d.TryGetFeatureValue(CommonUsages.primaryButton, out a);
            d.TryGetFeatureValue(CommonUsages.secondaryButton, out b);
            d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out click);
            d.TryGetFeatureValue(CommonUsages.menuButton, out menu);
            d.TryGetFeatureValue(CommonUsages.trigger, out trig);
            d.TryGetFeatureValue(CommonUsages.grip, out grip);
            d.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);
        }
        bool left = h == "L";
        Set(h + (left ? ".x" : ".a"), a ? 1f : 0f);
        Set(h + (left ? ".y" : ".b"), b ? 1f : 0f);
        Set(h + ".stick", click || axis.sqrMagnitude > 0.04f ? 1f : 0f);
        Set(h + ".trig", trig);
        Set(h + ".grip", grip);
        if (left) Set("L.menu", menu ? 1f : 0f);
    }

    void Set(string key, float amount)
    {
        if (!byKey.TryGetValue(key, out var p) || p.dot == null) return;
        p.dot.color = Color.Lerp(Idle, Lit, Mathf.Clamp01(amount));
        p.dot.sprite = amount > 0.4f ? Circle() : Outline(0.14f);      // a ring until pressed, then filled in
    }
}
