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
        var root = Build("welcome", "Welcome", "EOSAT-1 VR SIMULATOR", 1300f, 1100f, out RectTransform content, out GrabbablePanel gp);

        var so = new SerializedObject(gp);
        so.FindProperty("placeInFrontOnStart").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        string dim = CubeTheme.HexString(CubeTheme.Dim);

        // title
        var title = Text("Welcome Title", content, new Vector2(0.03f, 0.915f), new Vector2(0.97f, 0.985f), 46,
                         TextAlignmentOptions.Center, CubeTheme.Text);
        title.text = "<b>Welcome to C.U.B.E.15's EOSAT-1 Simulator!</b>";
        var sub = Text("Welcome Subtitle", content, new Vector2(0.03f, 0.875f), new Vector2(0.97f, 0.915f), 26,
                       TextAlignmentOptions.Center, CubeTheme.Dim);
        sub.text = "A live, in-headset view of the EOSAT-1 satellite, driven by the client's simulator.";

        // what it does
        var overview = WelcomeCard(content, "Overview Card", "WHAT IT DOES", new Vector2(0.03f, 0.64f), new Vector2(0.97f, 0.87f));
        overview.text =
            "<b>Live telemetry</b>   EOSAT-1's position, attitude, Sun and eclipse state, streamed from the simulator.\n" +
            "<b>Hub</b>   A globe, data panels and a ground-track map around you. Grab, pin and arrange them.\n" +
            "<b>Space view</b>   Fly out to the satellite: Overview, Orbit, Inertial, Body-fixed, Sun and Ground-station views.\n" +
            "<b>Warnings</b>   A popup and a red sphere appear on a high spin or bad data.\n" +
            "<b>Mixed reality</b>   Switch on passthrough to use it in your room.";

        // the controllers picture
        var headset = Text("Controllers Heading", content, new Vector2(0.03f, 0.595f), new Vector2(0.97f, 0.635f), 22,
                           TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        headset.text = "<cspace=0.12em><b>YOUR CONTROLLERS</b></cspace>";      // filled in by the Welcome Guide
        var pic = Rect("Controllers Picture", content, new Vector2(0.03f, 0.095f), new Vector2(0.97f, 0.59f));
        var picBg = pic.gameObject.AddComponent<Image>();
        picBg.color = CubeTheme.Inset; picBg.raycastTarget = false;
        var diagram = Rect("Diagram", pic, Vector2.zero, Vector2.one);

        var guide = root.AddComponent<WelcomeGuide>();
        var gso = new SerializedObject(guide);
        gso.FindProperty("diagram").objectReferenceValue = diagram;
        gso.FindProperty("headsetText").objectReferenceValue = headset;
        gso.ApplyModifiedPropertiesWithoutUndo();

        // bottom row
        var hint = Text("Reopen Hint", content, new Vector2(0.03f, 0.02f), new Vector2(0.68f, 0.085f), 22,
                        TextAlignmentOptions.MidlineLeft, CubeTheme.Dim);
        hint.text = "You can open this again any time from Options > Panels.";
        var btn = Button("Got it", content, new Vector2(0.72f, 0.02f), new Vector2(0.97f, 0.085f), 34, CubeTheme.ButtonAccent);
        var actions = root.AddComponent<PanelActions>();
        UnityEventTools.AddStringPersistentListener(btn.onClick, actions.HidePanel, "welcome");

        Save(root);
    }

    // a rounded-looking card with a small heading and an auto-sizing body; returns the body text
    static TMP_Text WelcomeCard(RectTransform parent, string name, string heading, Vector2 aMin, Vector2 aMax)
    {
        var card = Rect(name, parent, aMin, aMax);
        var img = card.gameObject.AddComponent<Image>();
        img.color = CubeTheme.Card; img.raycastTarget = false;
        var h = Text("Heading", card, new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.99f), 22, TextAlignmentOptions.MidlineLeft, CubeTheme.Dim);
        h.text = "<cspace=0.12em><b>" + heading + "</b></cspace>";
        var body = Text("Body", card, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.89f), 25, TextAlignmentOptions.TopLeft, CubeTheme.Text);
        body.enableAutoSizing = true; body.fontSizeMin = 16; body.fontSizeMax = 25;
        body.overflowMode = TextOverflowModes.Truncate;
        body.lineSpacing = 4f;
        return body;
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
        // six entries, a little tighter than the main menu's rows
        string[,] items = { { "Controls", "Controls" }, { "Panels", "Panels" }, { "Display", "Display" },
                            { "Satellite Data", "SatelliteData" }, { "Audio", "Audio" }, { "Accessibility", "Accessibility" } };
        for (int i = 0; i < items.GetLength(0); i++)
        {
            float top = 0.95f - i * 0.125f;
            Nav(opt, mc, items[i, 0], items[i, 1], 32, new Vector2(0.1f, top - 0.10f), new Vector2(0.9f, top));
        }
        BackButton(opt, mc);

        // placeholder pages, filled in later
        foreach (var n in new[] { "Audio" })
        {
            var pg = Page(n, content, false);
            var t = Text("Heading", pg, new Vector2(0.08f, 0.55f), new Vector2(0.92f, 0.9f), 40, TextAlignmentOptions.Center, CubeTheme.Dim);
            t.text = CubeTheme.Title(n == "SatelliteData" ? "Satellite Data" : n) + "\n\n<size=70%>coming soon</size>";
            BackButton(pg, mc);
        }

        SatellitePage(content, mc);
        PanelsPageBuild(content, mc);
        PanelFeelPage(content, mc);
        ControlsPage(content, mc);
        AccessibilityPageBuild(content, mc);
        DisplayPageBuild(content, mc);
        ButtonMapPageBuild(content, mc);

        // Page_ConnectionHelp
        var help = Page("ConnectionHelp", content, false);
        var ht = Text("Help Text", help, new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.97f), 26, TextAlignmentOptions.TopLeft, CubeTheme.Text);
        ht.enableAutoSizing = true; ht.fontSizeMin = 16; ht.fontSizeMax = 26;
        ht.overflowMode = TextOverflowModes.Truncate;
        ht.text = HelpText();
        BackButton(help, mc);

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



    // ── Panels page: one row per panel (show, attach, pin), reset, and a link to Panel feel ──
    static void PanelsPageBuild(RectTransform content, MenuController mc)
    {
        var pg = Page("Panels", content, false);
        var pp = pg.gameObject.AddComponent<PanelsPage>();

        // column headings, aligned with the row template below
        var hdr = Rect("Header", pg, new Vector2(0.04f, 0.87f), new Vector2(0.96f, 0.93f));
        Heading(hdr, "PANEL", 0f, 0.28f, TextAlignmentOptions.MidlineLeft);
        Heading(hdr, "SHOW", 0.30f, 0.46f, TextAlignmentOptions.Midline);
        Heading(hdr, "ATTACH", 0.48f, 0.66f, TextAlignmentOptions.Midline);
        Heading(hdr, "PIN", 0.68f, 0.83f, TextAlignmentOptions.Midline);
        Heading(hdr, "RESET", 0.85f, 1f, TextAlignmentOptions.Midline);

        var list = Rect("List", pg, new Vector2(0.04f, 0.27f), new Vector2(0.96f, 0.86f));

        // the hidden example row (PanelsPage makes one copy per panel)
        var row = Rect("Row Template", list, new Vector2(0f, 0.83f), new Vector2(1f, 1f));
        var rn = Text("Name", row, new Vector2(0f, 0f), new Vector2(0.28f, 1f), 26, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        rn.text = "Panel name";
        rn.enableAutoSizing = true; rn.fontSizeMin = 16; rn.fontSizeMax = 26;       // long names shrink instead of running into the buttons
        Button("Show", row, new Vector2(0.30f, 0.08f), new Vector2(0.46f, 0.92f), 19, CubeTheme.Button);
        Button("Attach", row, new Vector2(0.48f, 0.08f), new Vector2(0.66f, 0.92f), 19, CubeTheme.Button);
        Button("Pin", row, new Vector2(0.68f, 0.08f), new Vector2(0.83f, 0.92f), 19, CubeTheme.Button);
        Button("Reset", row, new Vector2(0.85f, 0.08f), new Vector2(1f, 0.92f), 19, CubeTheme.Button);
        row.gameObject.SetActive(false);

        var reset = Button("Reset all", pg, new Vector2(0.06f, 0.165f), new Vector2(0.47f, 0.255f), 24, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(reset.onClick, pp.ResetPanels);
        Nav(pg, mc, "Panel feel", "PanelFeel", 24, new Vector2(0.53f, 0.165f), new Vector2(0.94f, 0.255f));

        var so = new SerializedObject(pp);
        so.FindProperty("listRoot").objectReferenceValue = list;
        so.FindProperty("rowTemplate").objectReferenceValue = row.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();
        BackButton(pg, mc);
    }

    static void Heading(RectTransform parent, string text, float x0, float x1, TextAlignmentOptions align)
    {
        var t = Text("H " + text, parent, new Vector2(x0, 0f), new Vector2(x1, 1f), 18, align, CubeTheme.Dim);
        t.text = "<cspace=0.12em>" + text + "</cspace>";
    }

    // ── Button mapping page: scrolling list, tap then press a button, hold to unassign, Apply ──
    static void ButtonMapPageBuild(RectTransform content, MenuController mc)
    {
        var pg = Page("ButtonMapping", content, false);
        var bp = pg.gameObject.AddComponent<ButtonMapPage>();
        var clear = new Color(0f, 0f, 0f, 0f);

        var status = Text("Status", pg, new Vector2(0.04f, 0.895f), new Vector2(0.96f, 0.995f), 19, TextAlignmentOptions.TopLeft, CubeTheme.Warn);
        status.text = "";

        var hdr = Rect("Header", pg, new Vector2(0.04f, 0.835f), new Vector2(0.92f, 0.89f));
        Heading(hdr, "ACTION", 0f, 0.44f, TextAlignmentOptions.MidlineLeft);
        Heading(hdr, "BUTTON (HOLD TO CLEAR)", 0.44f, 0.80f, TextAlignmentOptions.Midline);
        Heading(hdr, "RESET", 0.82f, 1f, TextAlignmentOptions.Midline);

        // the scrolling list: drag it with the laser, or use the bar on the right
        var view = Rect("Viewport", pg, new Vector2(0.04f, 0.27f), new Vector2(0.92f, 0.83f));
        view.gameObject.AddComponent<Image>().color = clear;           // so dragging empty space scrolls too
        view.gameObject.AddComponent<RectMask2D>();
        var list = Rect("Content", view, new Vector2(0f, 1f), new Vector2(1f, 1f));
        list.pivot = new Vector2(0.5f, 1f);
        list.gameObject.AddComponent<Image>().color = clear;

        var row = Rect("Row Template", list, new Vector2(0f, 1f), new Vector2(1f, 1f));
        var rn = Text("Name", row, new Vector2(0f, 0f), new Vector2(0.44f, 1f), 24, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        rn.text = "Action";
        rn.enableAutoSizing = true; rn.fontSizeMin = 16; rn.fontSizeMax = 24;
        Button("Change", row, new Vector2(0.46f, 0.04f), new Vector2(0.80f, 0.96f), 20, CubeTheme.Button);
        Button("Reset", row, new Vector2(0.82f, 0.04f), new Vector2(1f, 0.96f), 19, CubeTheme.Button);
        row.gameObject.SetActive(false);

        var bar = Rect("Scrollbar", pg, new Vector2(0.935f, 0.27f), new Vector2(0.96f, 0.83f));
        bar.gameObject.AddComponent<Image>().color = CubeTheme.Inset;
        var sb = bar.gameObject.AddComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        var slide = Rect("Sliding Area", bar, Vector2.zero, Vector2.one);
        var handle = Rect("Handle", slide, Vector2.zero, Vector2.one);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.color = CubeTheme.Dim;
        sb.handleRect = handle;
        sb.targetGraphic = handleImg;

        var sr = view.gameObject.AddComponent<ScrollRect>();
        sr.content = list;
        sr.viewport = view;
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;
        sr.verticalScrollbar = sb;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        var apply = Button("Apply", pg, new Vector2(0.04f, 0.165f), new Vector2(0.48f, 0.245f), 26, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(apply.onClick, bp.Apply);
        var rb = Button("Restore defaults", pg, new Vector2(0.52f, 0.165f), new Vector2(0.96f, 0.245f), 22, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(rb.onClick, bp.RestoreDefaults);

        var so = new SerializedObject(bp);
        so.FindProperty("listRoot").objectReferenceValue = list;
        so.FindProperty("rowTemplate").objectReferenceValue = row.gameObject;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("applyButton").objectReferenceValue = apply;
        so.ApplyModifiedPropertiesWithoutUndo();
        BackButton(pg, mc);
    }

    // ── Display page: what is drawn in the space view ──
    static void DisplayPageBuild(RectTransform content, MenuController mc)
    {
        var pg = Page("Display", content, false);
        var act = pg.gameObject.AddComponent<SettingsActions>();

        ChoiceRow(pg, "Satellite axes", "display.axes", 1, new[] { "Off", "On" }, 0.98f);
        ChoiceRow(pg, "Earth axes", "display.earthaxes", 1, new[] { "Off", "On" }, 0.84f);
        ChoiceRow(pg, "Satellite marker", "display.beacon", 1, new[] { "Off", "On" }, 0.70f);
        var h = Text("Display hint", pg, new Vector2(0.08f, 0.30f), new Vector2(0.92f, 0.55f), 19, TextAlignmentOptions.TopLeft, CubeTheme.Dim);
        h.text = "Axes: X red, Y green, Z blue. The satellite's axes turn with it. The Earth's show the inertial frame (ECI): Z north pole, X vernal equinox.\n\nThe marker is a pulsing dot and a trail that keep the satellite easy to find when zoomed out. They fade as you come close.";

        var rb = Button("Restore defaults", pg, new Vector2(0.1f, 0.165f), new Vector2(0.9f, 0.235f), 24, CubeTheme.Button);
        UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, "display.axes");
        UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, "display.earthaxes");
        UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, "display.beacon");
        BackButton(pg, mc);
    }

    // ── Accessibility page: eye height and recenter ──
    static void AccessibilityPageBuild(RectTransform content, MenuController mc)
    {
        var pg = Page("Accessibility", content, false);
        var act = pg.gameObject.AddComponent<SettingsActions>();

        CompactSlider(pg, "Your height", "access.playerheight", 1f, 2.2f, 1.75f, 0.05f, "0.00", " m", 0.98f, true);
        CompactSlider(pg, "Raise / lower view", "access.height", -0.5f, 1f, 0f, 0.05f, "0.00", " m", 0.84f, true);
        ChoiceRow(pg, "Units", "access.units", 0, new[] { "Metres", "Feet", "Centimetres" }, 0.70f);

        var mb = Button("Match my height", pg, new Vector2(0.1f, 0.46f), new Vector2(0.9f, 0.55f), 26, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(mb.onClick, act.MatchHeight);
        var h1 = Text("Match hint", pg, new Vector2(0.08f, 0.395f), new Vector2(0.92f, 0.455f), 18, TextAlignmentOptions.TopLeft, CubeTheme.Dim);
        h1.text = "Sit or stand as you play, look straight ahead, then press.";

        var rc = Button("Recenter view", pg, new Vector2(0.1f, 0.30f), new Vector2(0.9f, 0.39f), 26, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(rc.onClick, act.Recenter);
        var h2 = Text("Recenter hint", pg, new Vector2(0.08f, 0.245f), new Vector2(0.92f, 0.295f), 18, TextAlignmentOptions.TopLeft, CubeTheme.Dim);
        h2.text = "Brings the hub or the space view back around you.";

        var rb = Button("Restore defaults", pg, new Vector2(0.1f, 0.165f), new Vector2(0.9f, 0.235f), 24, CubeTheme.Button);
        UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, "access.playerheight");
        UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, "access.height");
        UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, "access.units");
        BackButton(pg, mc);
    }

    // ── Controls page: turning, move speed, which button grabs panels ──
    static void ControlsPage(RectTransform content, MenuController mc)
    {
        var pg = Page("Controls", content, false);
        var act = pg.gameObject.AddComponent<SettingsActions>();

        ChoiceRow(pg, "Turning", "controls.turnstyle", 0, new[] { "Snap", "Smooth" }, 0.98f);
        CompactSlider(pg, "Snap turn angle", "controls.snapangle", 15f, 90f, 45f, 15f, "0", " deg", 0.83f);
        CompactSlider(pg, "Smooth turn speed", "controls.smoothspeed", 30f, 180f, 60f, 5f, "0", " deg/s", 0.68f);
        CompactSlider(pg, "Move speed", "controls.movespeed", 0.5f, 4f, 1.5f, 0.1f, "0.0", " m/s", 0.53f);
        Nav(pg, mc, "Button mapping", "ButtonMapping", 26, new Vector2(0.1f, 0.265f), new Vector2(0.9f, 0.365f));

        var rb = Button("Restore defaults", pg, new Vector2(0.1f, 0.165f), new Vector2(0.9f, 0.235f), 24, CubeTheme.Button);
        foreach (var k in new[] { "controls.turnstyle", "controls.snapangle", "controls.smoothspeed", "controls.movespeed" })
            UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, k);
        UnityEventTools.AddPersistentListener(rb.onClick, act.ResetBindings);
        BackButton(pg, mc);
    }

    static void ChoiceRow(RectTransform page, string label, string key, int def, string[] labels, float yTop)
    {
        var row = Rect("Setting " + label, page, new Vector2(0f, yTop - 0.14f), new Vector2(1f, yTop));
        var l = Text("Label", row, new Vector2(0.06f, 0f), new Vector2(0.50f, 1f), 28, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        l.text = label;
        var btn = Button(labels[0], row, new Vector2(0.52f, 0.1f), new Vector2(0.94f, 0.9f), 26, CubeTheme.Button);

        var sc = row.gameObject.AddComponent<SettingChoice>();
        var so = new SerializedObject(sc);
        so.FindProperty("key").stringValue = key;
        so.FindProperty("defaultValue").intValue = def;
        var arr = so.FindProperty("labels");
        arr.arraySize = labels.Length;
        for (int i = 0; i < labels.Length; i++) arr.GetArrayElementAtIndex(i).stringValue = labels[i];
        so.FindProperty("button").objectReferenceValue = btn;
        so.FindProperty("labelText").objectReferenceValue = btn.GetComponentInChildren<TMP_Text>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // a slider row without a description line, so several fit on one page
    static void CompactSlider(RectTransform page, string label, string key, float min, float max, float def, float step,
                              string format, string suffix, float yTop, bool lengthUnits = false)
    {
        var row = Rect("Setting " + label, page, new Vector2(0f, yTop - 0.14f), new Vector2(1f, yTop));
        var l = Text("Label", row, new Vector2(0.06f, 0.55f), new Vector2(0.62f, 1f), 26, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        l.text = label;
        var v = Text("Value", row, new Vector2(0.62f, 0.55f), new Vector2(0.94f, 1f), 26, TextAlignmentOptions.MidlineRight, CubeTheme.Accent);
        v.text = "";
        var slider = MakeSlider("Slider", row, new Vector2(0.06f, 0f), new Vector2(0.94f, 0.55f));

        var ss = row.gameObject.AddComponent<SettingSlider>();
        var so = new SerializedObject(ss);
        so.FindProperty("key").stringValue = key;
        so.FindProperty("defaultValue").floatValue = def;
        so.FindProperty("min").floatValue = min;
        so.FindProperty("max").floatValue = max;
        so.FindProperty("step").floatValue = step;
        so.FindProperty("displayScale").floatValue = 1f;
        so.FindProperty("format").stringValue = format;
        so.FindProperty("suffix").stringValue = suffix;
        so.FindProperty("lengthUnits").boolValue = lengthUnits;
        so.FindProperty("slider").objectReferenceValue = slider;
        so.FindProperty("valueText").objectReferenceValue = v;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── Panel feel page: sliders ──
    static void PanelFeelPage(RectTransform content, MenuController mc)
    {
        var pg = Page("PanelFeel", content, false);
        var act = pg.gameObject.AddComponent<SettingsActions>();

        SliderRow(pg, "Weight", "How heavy panels feel when you move them. Light follows your hand instantly.",
                  "panel.weight", 0f, 1f, 0.5f, 1f, "0.00", "", 0.90f);
        SliderRow(pg, "Break-free distance", "How far you raise or lower your hand before a panel comes off its floor track.",
                  "panel.breakfree", 0.01f, 0.10f, 0.02f, 100f, "0.0", " cm", 0.62f);

        var rb = Button("Restore defaults", pg, new Vector2(0.1f, 0.25f), new Vector2(0.9f, 0.36f), 28, CubeTheme.Button);
        UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, "panel.weight");
        UnityEventTools.AddStringPersistentListener(rb.onClick, act.ResetKey, "panel.breakfree");
        BackButton(pg, mc);
    }

    static void SliderRow(RectTransform page, string label, string description, string key,
                          float min, float max, float def, float displayScale, string format, string suffix, float yTop)
    {
        var row = Rect("Setting " + label, page, new Vector2(0f, yTop - 0.28f), new Vector2(1f, yTop));
        var l = Text("Label", row, new Vector2(0.06f, 0.78f), new Vector2(0.62f, 1f), 28, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        l.text = label;
        var v = Text("Value", row, new Vector2(0.62f, 0.78f), new Vector2(0.94f, 1f), 28, TextAlignmentOptions.MidlineRight, CubeTheme.Accent);
        v.text = "";
        var slider = MakeSlider("Slider", row, new Vector2(0.06f, 0.36f), new Vector2(0.94f, 0.74f));
        var d = Text("Description", row, new Vector2(0.06f, 0f), new Vector2(0.94f, 0.32f), 20, TextAlignmentOptions.TopLeft, CubeTheme.Dim);
        d.text = description;

        var ss = row.gameObject.AddComponent<SettingSlider>();
        var so = new SerializedObject(ss);
        so.FindProperty("key").stringValue = key;
        so.FindProperty("defaultValue").floatValue = def;
        so.FindProperty("min").floatValue = min;
        so.FindProperty("max").floatValue = max;
        so.FindProperty("displayScale").floatValue = displayScale;
        so.FindProperty("format").stringValue = format;
        so.FindProperty("suffix").stringValue = suffix;
        so.FindProperty("slider").objectReferenceValue = slider;
        so.FindProperty("valueText").objectReferenceValue = v;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // a slider made of ordinary objects: a thin track, a fill, and a big round-ish handle (the whole row is the hit area)
    static Slider MakeSlider(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var root = Rect(name, parent, aMin, aMax);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;                                    // so the pointer can grab it anywhere along the row

        var track = Rect("Track", root, new Vector2(0f, 0.42f), new Vector2(1f, 0.58f));
        track.offsetMin = new Vector2(14f, 0f); track.offsetMax = new Vector2(-14f, 0f);
        var ti = track.gameObject.AddComponent<Image>();
        ti.color = CubeTheme.Inset; ti.raycastTarget = false;

        var fillArea = Rect("Fill Area", root, new Vector2(0f, 0.42f), new Vector2(1f, 0.58f));
        fillArea.offsetMin = new Vector2(14f, 0f); fillArea.offsetMax = new Vector2(-14f, 0f);
        var fill = Rect("Fill", fillArea, Vector2.zero, Vector2.one);
        var fi = fill.gameObject.AddComponent<Image>();
        fi.color = CubeTheme.ButtonAccent; fi.raycastTarget = false;

        var area = Rect("Handle Slide Area", root, Vector2.zero, Vector2.one);
        area.offsetMin = new Vector2(30f, 0f); area.offsetMax = new Vector2(-30f, 0f);
        var handle = Rect("Handle", area, new Vector2(0f, 0.1f), new Vector2(0f, 0.9f));
        handle.sizeDelta = new Vector2(44f, 0f);
        var hi = handle.gameObject.AddComponent<Image>();
        hi.color = CubeTheme.Text; hi.raycastTarget = true;

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = hi;
        slider.direction = Slider.Direction.LeftToRight;
        var cb = slider.colors;
        cb.normalColor = CubeTheme.Text;
        cb.highlightedColor = CubeTheme.Accent;
        cb.pressedColor = CubeTheme.Accent;
        cb.selectedColor = CubeTheme.Text;
        slider.colors = cb;
        return slider;
    }

    // ── Satellite Data page: link status, IP + port, keypad ──────────────────
    static string HelpText()
    {
        string dm = CubeTheme.HexString(CubeTheme.Dim);
        return
            "<b>OVER WI-FI</b>\n" +
            "1. Put the PC and the headset on the same Wi-Fi.\n" +
            "2. On the PC open Command Prompt and run <b>ipconfig</b>. Note the IPv4 Address of the Wi-Fi adapter.\n" +
            "3. Type it on the Satellite Data page, keep port 8100, press <b>Connect</b>.\n" +
            "<color=#" + dm + ">If it won't link, allow TCP 8100 and 8101 through Windows Firewall.</color>\n\n" +
            "<b>OVER A USB CABLE</b>\n" +
            "1. Plug the headset into the PC and accept USB debugging inside the headset.\n" +
            "2. On the PC run: <b>adb reverse tcp:8100 tcp:8100</b>\n" +
            "3. Press <b>USB Cable</b> on the Satellite Data page.";
    }

    static void SatellitePage(RectTransform content, MenuController mc)
    {
        var pg = Page("SatelliteData", content, false);
        SatelliteBody(pg, mc, false);
        BackButton(pg, mc);
    }

    // The satellite data controls (status, IP + port, keypad, buttons). The menu page and the stand-alone panel share them.
    // In the menu, Instructions opens its own page; in a panel it covers the keypad with the instructions instead.
    static SatelliteDataPage SatelliteBody(RectTransform pg, MenuController mc, bool asPanel)
    {
        var sp = pg.gameObject.AddComponent<SatelliteDataPage>();
        var so = new SerializedObject(sp);

        var status = Text("Status", pg, new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.98f), 36, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        so.FindProperty("statusText").objectReferenceValue = status;

        // the two fields (tap one to choose which the keypad types into)
        var ipBox = Field("IP Box", pg, new Vector2(0.04f, 0.73f), new Vector2(0.62f, 0.85f), 44, out TMP_Text ipT);
        var portBox = Field("Port Box", pg, new Vector2(0.65f, 0.73f), new Vector2(0.96f, 0.85f), 44, out TMP_Text portT);
        UnityEventTools.AddPersistentListener(ipBox.GetComponent<UnityEngine.UI.Button>().onClick, sp.SelectIp);
        UnityEventTools.AddPersistentListener(portBox.GetComponent<UnityEngine.UI.Button>().onClick, sp.SelectPort);
        so.FindProperty("ipText").objectReferenceValue = ipT;
        so.FindProperty("portText").objectReferenceValue = portT;
        so.FindProperty("ipBox").objectReferenceValue = ipBox;
        so.FindProperty("portBox").objectReferenceValue = portBox;

        // keypad, 4 rows x 3 columns
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "DEL" };
        const float x0 = 0.04f, x1 = 0.62f, yTop = 0.70f, yBot = 0.18f;
        float cw = (x1 - x0) / 3f, ch = (yTop - yBot) / 4f;
        for (int i = 0; i < keys.Length; i++)
        {
            int row = i / 3, col = i % 3;
            var b = Button(keys[i], pg, new Vector2(x0 + col * cw + 0.006f, yTop - (row + 1) * ch + 0.008f),
                           new Vector2(x0 + (col + 1) * cw - 0.006f, yTop - row * ch - 0.008f), 40, CubeTheme.Button);
            if (keys[i] == "DEL") UnityEventTools.AddPersistentListener(b.onClick, sp.Backspace);
            else UnityEventTools.AddStringPersistentListener(b.onClick, sp.Press, keys[i]);
        }

        // right-hand buttons
        // five buttons, top to bottom: Connect, Disconnect, USB cable, Clear, Instructions
        const float bTop = 0.70f, bStep = 0.104f;
        Vector2 BMin(int i) => new Vector2(0.65f, bTop - (i + 1) * bStep + 0.006f);
        Vector2 BMax(int i) => new Vector2(0.96f, bTop - i * bStep - 0.006f);
        var conn = Button("Connect", pg, BMin(0), BMax(0), 30, CubeTheme.ButtonAccent);
        UnityEventTools.AddPersistentListener(conn.onClick, sp.Connect);
        var disc = Button("Disconnect", pg, BMin(1), BMax(1), 28, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(disc.onClick, sp.Disconnect);
        var usb = Button("USB cable", pg, BMin(2), BMax(2), 28, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(usb.onClick, sp.UseUsb);
        var clr = Button("Clear", pg, BMin(3), BMax(3), 30, CubeTheme.Button);
        UnityEventTools.AddPersistentListener(clr.onClick, sp.Clear);

        if (!asPanel)
        {
            Nav(pg, mc, "Instructions", "ConnectionHelp", 22, BMin(4), BMax(4));
        }
        else
        {
            var ins = Button("Instructions", pg, BMin(4), BMax(4), 22, CubeTheme.Button);
            UnityEventTools.AddPersistentListener(ins.onClick, sp.ToggleHelp);

            // the instructions, drawn over the keypad until closed
            var ov = Rect("Help Overlay", pg, new Vector2(0f, 0.165f), new Vector2(1f, 1f));
            var oi = ov.gameObject.AddComponent<Image>();
            oi.color = CubeTheme.Panel; oi.raycastTarget = true;
            var ot = Text("Help Text", ov, new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.97f), 26, TextAlignmentOptions.TopLeft, CubeTheme.Text);
            ot.enableAutoSizing = true; ot.fontSizeMin = 16; ot.fontSizeMax = 26;
            ot.overflowMode = TextOverflowModes.Truncate;
            ot.text = HelpText();
            var close = Button("Close", ov, new Vector2(0.1f, 0.02f), new Vector2(0.9f, 0.12f), 28, CubeTheme.Button);
            UnityEventTools.AddPersistentListener(close.onClick, sp.ToggleHelp);
            ov.gameObject.SetActive(false);
            so.FindProperty("helpOverlay").objectReferenceValue = ov.gameObject;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        return sp;
    }

    // a text box that is also a button; the page script tints its Image to show which one is selected
    static Image Field(string name, Transform parent, Vector2 aMin, Vector2 aMax, int size, out TMP_Text text)
    {
        var rt = Rect(name, parent, aMin, aMax);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = CubeTheme.Inset;
        img.raycastTarget = true;
        var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        text = Text("Text", rt, Vector2.zero, Vector2.one, size, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        text.rectTransform.offsetMin = new Vector2(16f, 0f);
        text.rectTransform.offsetMax = new Vector2(-8f, 0f);
        text.overflowMode = TextOverflowModes.Overflow;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return img;
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

    static void Nav(RectTransform page, MenuController mc, string label, string target, int size, Vector2 aMin, Vector2 aMax)
    {
        var b = Button(label, page, aMin, aMax, size, CubeTheme.Button);
        UnityEventTools.AddStringPersistentListener(b.onClick, mc.ShowPage, target);
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


    // ── the stand-alone Satellite Data panel (off by default; open it from Options > Panels) ──
    [MenuItem("CUBE15/Panels/New Satellite Data Panel")]
    static void SatellitePanel()
    {
        // 709 tall so the controls match the menu page: the menu page has a 15% strip for a Back button that a panel doesn't need
        var root = Build("satdata", "Satellite Data", "TELEMETRY LINK", 760f, 709f, out RectTransform content, out GrabbablePanel gp);
        var body = Rect("Body", content, Vector2.zero, Vector2.one);
        body.offsetMin = new Vector2(0f, -131f);
        SatelliteBody(body, null, true);
        root.SetActive(false);
        Save(root);
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
