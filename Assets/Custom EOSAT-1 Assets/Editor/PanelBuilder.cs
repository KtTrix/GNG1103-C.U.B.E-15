#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Builds panels as ordinary objects in the open scene (and saves each as a prefab), so everything can be
/// edited in the Hierarchy and Inspector: text, colours, sizes, buttons.
/// Menu:  CUBE15 > Panels > New Welcome Panel   /   New Blank Panel
/// Open HubScene first, so the panels live in the scene that never unloads.
/// </summary>
public static class PanelBuilder
{
    const string PrefabFolder = "Assets/Custom EOSAT-1 Assets/Panels";
    const float TitleHeight = 44f;

    [MenuItem("CUBE15/Panels/New Blank Panel")]
    static void Blank() => Build("panel", "New Panel", "", 1000f, 700f, out _, out _);

    [MenuItem("CUBE15/Panels/New Welcome Panel")]
    static void Welcome()
    {
        var root = Build("welcome", "Welcome", "EOSAT-1 VR SIMULATOR", 1100f, 760f, out RectTransform content, out GrabbablePanel gp);

        var so = new SerializedObject(gp);
        so.FindProperty("placeInFrontOnStart").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        string dim = CubeTheme.HexString(CubeTheme.Dim);

        var body = Text("Body", content, new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.97f), 32,
                        TextAlignmentOptions.TopLeft, CubeTheme.Text);
        body.text =
            "<b>Welcome aboard.</b> This is a live view of the EOSAT-1 satellite simulator.\n\n" +
            "<color=#" + dim + ">HUB</color>   The globe shows where the satellite is. Panels give its position and ground track.\n" +
            "<color=#" + dim + ">SPACE</color>   Fly alongside the satellite and look at it from the Sun, Earth and more.";

        var btn = Button("Got it", content, new Vector2(0.62f, 0.04f), new Vector2(0.96f, 0.15f), 34, CubeTheme.ButtonAccent);
        var actions = root.AddComponent<PanelActions>();
        UnityEventTools.AddStringPersistentListener(btn.onClick, actions.HidePanel, "welcome");

        Save(root);
    }


    // ── the menu ─────────────────────────────────────────────────────────────
    [MenuItem("CUBE15/Panels/Build Menu")]
    static void BuildMenu()
    {
        var root = Build("menu", "Menu", "EOSAT-1", 760f, 840f, out RectTransform content, out GrabbablePanel gp);
        var mc = root.AddComponent<MenuController>();
        var mso = new SerializedObject(mc);
        mso.FindProperty("pagesRoot").objectReferenceValue = content;
        mso.ApplyModifiedPropertiesWithoutUndo();

        // Page_Main
        var main = Page("Main", content, true);
        Nav(main, mc, 0, "Resume", null, CubeTheme.ButtonAccent, close: true);
        Nav(main, mc, 1, "Options", "Options");
        Nav(main, mc, 2, "Credits", "Credits");
        Nav(main, mc, 3, "Quit", "Quit");

        // Page_Options
        var opt = Page("Options", content, false);
        Nav(opt, mc, 0, "Controls", "Controls");
        Nav(opt, mc, 1, "Panels", "Panels");
        Nav(opt, mc, 2, "Satellite Data", "SatelliteData");
        Nav(opt, mc, 3, "Audio", "Audio");
        Nav(opt, mc, 4, "Accessibility", "Accessibility");
        BackButton(opt, mc);

        // placeholder pages, filled in later
        foreach (var n in new[] { "Controls", "Panels", "SatelliteData", "Audio", "Accessibility" })
        {
            var pg = Page(n, content, false);
            var t = Text("Heading", pg, new Vector2(0.08f, 0.55f), new Vector2(0.92f, 0.9f), 40, TextAlignmentOptions.Center, CubeTheme.Dim);
            t.text = CubeTheme.Title(n == "SatelliteData" ? "Satellite Data" : n) + "\n\n<size=70%>coming soon</size>";
            BackButton(pg, mc);
        }

        // Page_Credits
        var cr = Page("Credits", content, false);
        var ct = Text("Credits Text", cr, new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.97f), 28, TextAlignmentOptions.TopLeft, CubeTheme.Text);
        ct.enableAutoSizing = true;                         // shrinks to fit the box
        ct.fontSizeMin = 16; ct.fontSizeMax = 28;
        ct.overflowMode = TextOverflowModes.Truncate;       // anything that still doesn't fit is cut, never drawn outside
        var cc = cr.gameObject.AddComponent<CreditsText>();
        cc.target = ct;
        cc.file = MakeCreditsFile();
        BackButton(cr, mc);

        // Page_Quit
        var q = Page("Quit", content, false);
        var qt = Text("Question", q, new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.93f), 44, TextAlignmentOptions.Center, CubeTheme.Text);
        qt.text = "Quit the simulator?";
        var yes = Button("Yes, quit", q, new Vector2(0.1f, 0.48f), new Vector2(0.9f, 0.60f), 36, Color.Lerp(CubeTheme.Bad, CubeTheme.Panel, 0.55f));
        UnityEventTools.AddPersistentListener(yes.onClick, mc.Quit);
        var no = Button("No", q, new Vector2(0.1f, 0.32f), new Vector2(0.9f, 0.44f), 36, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(no.onClick, mc.Back);

        root.SetActive(false);          // closed until the menu button opens it
        Save(root);
    }

    static RectTransform Page(string name, RectTransform content, bool active)
    {
        var r = Rect("Page_" + name, content, Vector2.zero, Vector2.one);
        r.gameObject.SetActive(active);
        return r;
    }

    // Button rows from the top: row 0, 1, 2 ...
    static Vector2 Row(int i, bool top = false)
    {
        float yTop = 0.93f - i * 0.14f;
        return top ? new Vector2(0.9f, yTop) : new Vector2(0.1f, yTop - 0.11f);
    }

    static void Nav(RectTransform page, MenuController mc, int row, string label, string target,
                    Color? color = null, bool close = false)
    {
        var b = Button(label, page, Row(row), Row(row, true), 36, color ?? CubeTheme.Button);
        if (close) UnityEventTools.AddPersistentListener(b.onClick, mc.Close);
        else UnityEventTools.AddStringPersistentListener(b.onClick, mc.ShowPage, target);
    }

    static void BackButton(RectTransform page, MenuController mc)
    {
        var b = Button("Back", page, new Vector2(0.1f, 0.04f), new Vector2(0.9f, 0.15f), 34, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(b.onClick, mc.Back);
    }

    static TextAsset MakeCreditsFile()
    {
        const string path = "Assets/Custom EOSAT-1 Assets/Credits.txt";
        if (!File.Exists(path))
        {
            File.WriteAllText(path,
                "<b>EOSAT-1 VR SIMULATOR</b>\n" +
                "Team C.U.B.E-15 - GNG1103, University of Ottawa\n\n" +
                "<b>Team</b>\n" +
                "(add names and roles here)\n\n" +
                "<b>Credits</b>\n" +
                "Earth, Moon and Sun textures: Solar System Scope (CC BY 4.0)\n" +
                "Quest controller models: WebXR Input Profiles (MIT, Copyright (c) 2019 Amazon)\n");
            AssetDatabase.ImportAsset(path);
        }
        return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
    }


    // ── the position panel ───────────────────────────────────────────────────
    [MenuItem("CUBE15/Panels/New Position Panel")]
    static void Position()
    {
        var root = Build("position", "Position", "LIVE TELEMETRY", 700f, 700f, out RectTransform content, out GrabbablePanel gp);

        var so = new SerializedObject(gp);
        so.FindProperty("inDefaultLayout").boolValue = true;
        so.FindProperty("layoutYawDegrees").floatValue = -32f;      // to your left
        so.ApplyModifiedPropertiesWithoutUndo();

        var link = Text("Link Text", content, new Vector2(0.55f, 0.91f), new Vector2(0.96f, 0.99f), 34,
                        TextAlignmentOptions.MidlineRight, CubeTheme.Ok);
        link.text = "";
        var values = Text("Values Text", content, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.90f), 34,
                          TextAlignmentOptions.TopLeft, CubeTheme.Text);
        values.text = "Waiting for data...";

        var tp = root.AddComponent<TelemetryPanel>();
        var tso = new SerializedObject(tp);
        tso.FindProperty("valuesText").objectReferenceValue = values;
        tso.FindProperty("linkText").objectReferenceValue = link;
        tso.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);          // the default layout shows it (on your left) when the app starts
        Save(root);
    }


    // ── the ground-track map panel ───────────────────────────────────────────
    [MenuItem("CUBE15/Panels/New Map Panel")]
    static void MapPanel()
    {
        const float MapW = 1100f, MapH = 550f;
        var root = Build("map", "Ground Track", "EQUIRECTANGULAR", MapW + 80f, MapH + 110f, out RectTransform content, out GrabbablePanel gp);

        var so = new SerializedObject(gp);
        so.FindProperty("inDefaultLayout").boolValue = true;
        so.FindProperty("layoutYawDegrees").floatValue = 32f;       // to your right
        so.ApplyModifiedPropertiesWithoutUndo();

        // the map picture (a standard 2:1 equirectangular image, longitude -180 at the left edge)
        var map = Rect("Map", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        map.sizeDelta = new Vector2(MapW, MapH);
        var raw = map.gameObject.AddComponent<RawImage>();
        raw.texture = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Custom EOSAT-1 Assets/flat_earth03.jpg");
        raw.raycastTarget = true;
        if (raw.texture == null) Debug.LogWarning("Map picture not found at Assets/Custom EOSAT-1 Assets/flat_earth03.jpg - drag your map image onto the Raw Image's Texture slot.");

        // the track line, drawn over the map
        var track = Rect("Track", map, Vector2.zero, Vector2.one);
        var line = track.gameObject.AddComponent<UITrackLine>();
        var lso = new SerializedObject(line);
        lso.FindProperty("thickness").floatValue = 4f;
        lso.FindProperty("sunlitColor").colorValue = new Color(1f, 0f, 0.8428f);
        lso.FindProperty("eclipseColor").colorValue = new Color(0.6895f, 0.8038f, 0.3139f);
        lso.FindProperty("oldestAlpha").floatValue = 0.25f;
        lso.ApplyModifiedPropertiesWithoutUndo();

        // the spacecraft marker
        var markerRt = Rect("Marker", map, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        markerRt.sizeDelta = new Vector2(16f, 16f);
        var markerImg = markerRt.gameObject.AddComponent<Image>();
        var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        markerImg.sprite = knob;
        markerImg.color = new Color(1f, 0.2392f, 0.7804f);
        markerImg.raycastTarget = false;

        // the script that fills it with live data
        var gtm = map.gameObject.AddComponent<GroundTrackMap>();
        var gso = new SerializedObject(gtm);
        gso.FindProperty("trackLine").objectReferenceValue = line;
        gso.FindProperty("marker").objectReferenceValue = markerRt;
        gso.FindProperty("dotSprite").objectReferenceValue = knob;
        gso.FindProperty("orbitsToKeep").floatValue = 2f;
        gso.FindProperty("maxPoints").intValue = 1500;
        gso.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);          // the default layout shows it (on your right) when the app starts
        Save(root);
    }

    // ── builders ─────────────────────────────────────────────────────────────
    static GameObject Build(string id, string title, string subtitle, float w, float h,
                            out RectTransform content, out GrabbablePanel gp)
    {
        var root = new GameObject("Panel_" + char.ToUpperInvariant(id[0]) + id.Substring(1));
        Undo.RegisterCreatedObjectUndo(root, "Create panel");
        root.transform.position = new Vector3(0f, 1.5f, 1.5f);

        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(root.transform, false);
        canvasGo.transform.localScale = Vector3.one * 0.001f;     // 1 canvas unit = 1 mm
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
        var crt = (RectTransform)canvasGo.transform;
        crt.sizeDelta = new Vector2(w, h);

        // background + hairline border
        var bg = Rect("Background", crt, Vector2.zero, Vector2.one);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = CubeTheme.Panel;
        bgImg.raycastTarget = true;       // must be true, or the VR pointer passes through the panel
        var outline = bg.gameObject.AddComponent<Outline>();
        outline.effectColor = CubeTheme.Border;
        outline.effectDistance = new Vector2(2f, 2f);

        // title strip
        var strip = Rect("Title Strip", crt, new Vector2(0f, 1f), new Vector2(1f, 1f));
        strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(0f, TitleHeight);
        strip.anchoredPosition = Vector2.zero;

        var div = Rect("Divider", strip, new Vector2(0f, 0f), new Vector2(1f, 0f));
        div.sizeDelta = new Vector2(0f, 2f);
        var dImg = div.gameObject.AddComponent<Image>();
        dImg.color = CubeTheme.Border;
        dImg.raycastTarget = false;

        var t = Text("Title", strip, Vector2.zero, Vector2.one, 26, TextAlignmentOptions.MidlineLeft, CubeTheme.Dim);
        t.text = CubeTheme.Title(title);
        t.rectTransform.offsetMin = new Vector2(18f, 0f);
        var st = Text("Subtitle", strip, Vector2.zero, Vector2.one, 26, TextAlignmentOptions.MidlineRight, CubeTheme.Dim);
        st.text = CubeTheme.Title(subtitle);
        st.rectTransform.offsetMax = new Vector2(-18f, 0f);

        // content area (put your own objects in here)
        content = Rect("Content", crt, Vector2.zero, Vector2.one);
        content.offsetMax = new Vector2(0f, -TitleHeight);

        // grab handle
        float hw = w * 0.3f;
        var handle = Rect("Grab Handle", crt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        handle.pivot = new Vector2(0.5f, 1f);
        handle.anchoredPosition = new Vector2(0f, -14f);
        handle.sizeDelta = new Vector2(hw, 14f);
        var hImg = handle.gameObject.AddComponent<Image>();
        hImg.color = CubeTheme.Dim;
        hImg.raycastTarget = false;
        var col = handle.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(hw + 60f, 84f, 10f);
        col.center = new Vector3(0f, -35f, 0f);
        var ph = handle.gameObject.AddComponent<PanelHandle>();

        gp = root.AddComponent<GrabbablePanel>();
        ph.panel = gp;
        var so = new SerializedObject(gp);
        so.FindProperty("panelId").stringValue = id;
        so.FindProperty("displayName").stringValue = title;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        return root;
    }

    static void Save(GameObject root)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets/Custom EOSAT-1 Assets", "Panels");
        string path = PrefabFolder + "/" + root.name + ".prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.UserAction);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("Created " + root.name + " in the scene and saved the prefab at " + path);
    }

    static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    static TMP_Text Text(string name, Transform parent, Vector2 aMin, Vector2 aMax, int size,
                         TextAlignmentOptions align, Color color)
    {
        var rt = Rect(name, parent, aMin, aMax);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.alignment = align; t.color = color;
        t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;      // long lines wrap at the edge of the box
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    static UnityEngine.UI.Button Button(string label, Transform parent, Vector2 aMin, Vector2 aMax, int size, Color baseColor)
    {
        var rt = Rect("Btn " + label, parent, aMin, aMax);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;
        var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = baseColor;
        cb.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
        cb.selectedColor = baseColor;
        cb.pressedColor = Color.Lerp(baseColor, Color.white, 0.45f);
        btn.colors = cb;
        var t = Text("Label", rt, Vector2.zero, Vector2.one, size, TextAlignmentOptions.Center, CubeTheme.Text);
        t.text = "<cspace=0.08em><b>" + label.ToUpperInvariant() + "</b></cspace>";
        return btn;
    }
}
#endif
