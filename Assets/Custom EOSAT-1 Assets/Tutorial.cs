using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// The guided tutorial: a small card (step text + Next / End buttons) that sits off to one side of your view, and a bobbing
/// arrow pointing at whatever the step is about (a controller, a panel, a menu button). Steps move on by themselves when
/// you do the thing; Next is always there if you'd rather skip a step.
/// Every button named in the text is read from the player's own mapping (ControlMap), so remapped buttons are shown correctly.
/// Start it with  Tutorial.Begin()  (the welcome panel's Start tutorial button and Options > Tutorial do). Creates itself.
/// To add or change steps, edit BuildSteps() below.
/// </summary>
public class Tutorial : MonoBehaviour
{
    /// <summary>When true, the welcome panel does not pop up at launch.</summary>
    public const string SkipKey = "tutorial.skipwelcome";

    /// <summary>When true, the welcome panel (with its Start tutorial button) appears at every launch, even after Skip forever.</summary>
    public const string AutoKey = "tutorial.autostart";

    static Tutorial inst;
    public static bool Running => inst != null && inst.running;

    public static void Begin()
    {
        if (inst == null)
        {
            var go = new GameObject("Tutorial");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<Tutorial>();
        }
        inst.Run();
    }

    public static void Stop() { if (inst != null && inst.running) inst.Finish(false); }

    // ── steps ────────────────────────────────────────────────────────────────
    class Step
    {
        public string title;
        public string lead;               // "open the menu": shown as "Nice, now open the menu" when the step before it is done
        public Func<string> text;
        public Func<Vector3?> target;     // where the arrow points (null = no arrow)
        public Func<bool> done;           // true once the player has done it (null = only the Next button moves on)
        public Action enter;
    }

    // a button name from the player's own mapping, e.g. "Left Y"
    static string B(string id)
    {
        string p = ControlMap.Get(id);
        if (ControlMap.IsNone(p)) return "<color=#" + CubeTheme.HexString(CubeTheme.Warn) + "><b>(no button set)</b></color>";
        return "<color=#" + CubeTheme.HexString(CubeTheme.Accent) + "><b>" + ControlMap.Friendly(p).Replace(" (both)", "") + "</b></color>";
    }
    static string Hi(string s) => "<color=#" + CubeTheme.HexString(CubeTheme.Accent) + "><b>" + s + "</b></color>";

    SpaceViewController svc;
    int startKind = -1;

    // what the Space View edit steps compare against
    ViewSlot eSlot; float eDist, eYaw, ePitch; SpaceViewController.ViewKind eKind;
    void CaptureEdit()
    {
        eSlot = WorkspaceViews.Exists ? WorkspaceViews.Instance.Editing : null;
        if (eSlot == null) return;
        eDist = eSlot.Dist; eYaw = eSlot.Yaw; ePitch = eSlot.Pitch; eKind = eSlot.Kind;
    }
    ViewSlot Editing => WorkspaceViews.Exists ? WorkspaceViews.Instance.Editing : null;

    List<Step> BuildSteps()
    {
        return new List<Step>
        {
            new Step {
                title = "Open the menu", lead = "open the menu",
                text = () => "Press " + B("menu") + " to open the menu.",
                target = () => HandPos("menu"),
                done = () => MenuOpen() },
            new Step {
                title = "Open the Panels page", lead = "open the Panels page",
                text = () => "The menu controls everything. Aim at " + Hi("Panels") + " and pull the trigger to choose which panels are open and where they sit.",
                target = () => MenuItemTop("Btn Panels"),
                done = () => PageOpen("Page_Panels") },
            new Step {
                title = "Show and hide panels", lead = "try showing or hiding a panel",
                text = () => Hi("On / Off") + " opens and closes a panel. " + Hi("Attach") + " keeps it on your hand or head, " + Hi("Pin") + " stops it being moved by accident, and " + Hi("Reset") + " puts it back. Try switching one On or Off.",
                target = () => MenuItemTop("Btn Show"),
                enter = () => changeStamp = -10f,
                done = () => changeStamp > enteredAt + 0.6f },
            new Step {
                title = "Close the menu", lead = "close the menu",
                text = () => "Press " + B("menu") + " again, or choose " + Hi("Resume") + ", to close the menu.",
                target = () => HandPos("menu"),
                done = () => !MenuOpen() },
            new Step {
                title = "Move a panel", lead = "try moving a panel",
                text = () => "Point at any panel and hold " + B("grab") + " to pick it up and move it. Drag one of its corner brackets to make it bigger or smaller.",
                target = NearestPanelTop,
                done = () => AnyGrabbed() },
            new Step {
                title = "Fly out to the satellite", lead = "fly out to the satellite",
                text = () => "Press " + B("view") + " to switch between the hub and space view. Try it now: it takes you out to EOSAT-1.",
                target = () => HandPos("view"),
                done = () => InSpace() },
            new Step {
                title = "Try the views", lead = "try the views",
                text = () => "Space view has six viewpoints: Overview, Orbit, Inertial, Body-fixed, Sun and Ground station. Press " + B("nextview") + " for the next view and " + B("prevview") + " for the previous one.",
                target = () => HandPos("nextview"),
                enter = () => { svc = null; startKind = -1; },
                done = () => ViewChanged() },
            new Step {
                title = "Back to the hub", lead = "head back to the hub",
                text = () => "Press " + B("view") + " again to come back to the hub.",
                target = () => HandPos("view"),
                done = () => InHub() },
            new Step {
                title = "Open the space view panel", lead = "open the space view panel",
                text = () => "The " + Hi("space view panel") + " keeps live views of the satellite in the hub, so you never have to leave. Open the menu, go to Panels and switch " + Hi(SpacePanel() != null ? SpacePanel().DisplayName : "Space View") + " On.",
                target = WorkspaceOpenTarget,
                done = () => WorkspaceOpen() },
            new Step {
                title = "Close the menu", lead = "close the menu",
                text = () => "Press " + B("menu") + " again, or choose " + Hi("Resume") + ", to close the menu and see the space view panel.",
                target = () => HandPos("menu"),
                done = () => !MenuOpen() && WorkspaceOpen() },
            new Step {
                title = "Edit a view", lead = "edit a view",
                text = () => "Each view has its own buttons. Point at a view and press " + Hi("Edit") + " to change where it looks and what it shows.",
                target = () => WorkspaceItemTop("Btn Edit"),
                done = () => Editing != null },
            new Step {
                title = "Steer the view", lead = "steer it",
                text = () => "While editing: the " + Hi("right stick") + " zooms in and out and turns the view, and the " + Hi("left stick") + " tilts it. Give it a go.",
                target = () => WorkspaceItemTop("Btn Edit"),
                enter = () => CaptureEdit(),
                done = () => { var e = Editing; if (e == null) return false; if (e != eSlot) { CaptureEdit(); return false; }
                               return Mathf.Abs(e.Dist - eDist) > 0.02f * Mathf.Max(1f, eDist) || Mathf.Abs(Mathf.DeltaAngle(e.Yaw, eYaw)) > 8f || Mathf.Abs(e.Pitch - ePitch) > 8f; } },
            new Step {
                title = "Change what it shows", lead = "change what it shows",
                text = () => "Still editing? Press " + B("nextview") + " or " + B("prevview") + " to change which view this is: Overview, Orbit, Sun, Ground station and so on.",
                target = () => HandPos("nextview"),
                enter = () => CaptureEdit(),
                done = () => { var e = Editing; if (e == null) return false; if (e != eSlot) { CaptureEdit(); return false; } return e.Kind != eKind; } },
            new Step {
                title = "Finish editing", lead = "finish editing",
                text = () => "Press " + Hi("Done") + " on the view to finish. " + Hi("Pop out") + " turns a view into its own panel, " + Hi("+ Add view") + " adds more (up to four), and " + Hi("X") + " removes one.",
                target = () => WorkspaceItemTop("Btn Edit"),
                done = () => WorkspaceOpen() && Editing == null },
            new Step {
                title = "You're ready", lead = null,
                text = () => "Open the menu any time with " + B("menu") + ". The " + Hi("Mixed reality") + " button there switches passthrough on and off, and " + Hi("Options") + " has everything else. You can replay this tutorial from Options > Tutorial.",
                target = null,
                done = null },
        };
    }

    // the main space view panel (found by its component, so it works whatever id the panel was given)
    static GrabbablePanel spacePanel;
    static float nextSpaceFind;
    static GrabbablePanel SpacePanel()
    {
        if (spacePanel != null) return spacePanel;
        if (Time.unscaledTime < nextSpaceFind) return null;
        nextSpaceFind = Time.unscaledTime + 1f;
        PanelManager.Refresh();
        foreach (var p in PanelManager.All)
        {
            if (p == null) continue;
            var w = p.GetComponent<WorkspacePanel>();
            if (w != null && !w.IsPopOut) { spacePanel = p; break; }
        }
        return spacePanel;
    }
    static bool WorkspaceOpen() { var w = SpacePanel(); return w != null && w.gameObject.activeInHierarchy; }

    // menu -> Panels page -> the Space View row's On/Off button
    Vector3? WorkspaceOpenTarget()
    {
        if (!MenuOpen()) return HandPos("menu");
        if (!PageOpen("Page_Panels")) return MenuItemTop("Btn Panels");
        var m = PanelManager.Get("menu");
        var w = SpacePanel();
        if (w == null) return null;
        string want = "Row " + w.DisplayName;
        foreach (var row in m.GetComponentsInChildren<RectTransform>(false))
        {
            if (row.name != want) continue;
            var show = row.Find("Btn Show") as RectTransform;
            if (show == null) return null;
            var c = new Vector3[4];
            show.GetWorldCorners(c);
            return (c[1] + c[2]) * 0.5f;
        }
        return null;
    }

    // the first button with this name on the open Space View panel (falls back to the top of the panel)
    static Vector3? WorkspaceItemTop(string objName)
    {
        var w = SpacePanel();
        if (w == null || !w.gameObject.activeInHierarchy) return null;
        foreach (var t in w.GetComponentsInChildren<RectTransform>(false))
        {
            if (t.name != objName) continue;
            var c = new Vector3[4];
            t.GetWorldCorners(c);
            return (c[1] + c[2]) * 0.5f;
        }
        var col = w.GetComponentInChildren<Collider>();
        return col == null ? (Vector3?)null : new Vector3(col.bounds.center.x, col.bounds.max.y, col.bounds.center.z);
    }

    // ── checks ───────────────────────────────────────────────────────────────
    static bool AnyGrabbed()
    {
        foreach (var p in PanelManager.All) if (p != null && p.IsGrabbed) return true;
        return false;
    }
    static bool MenuOpen() { var m = PanelManager.Get("menu"); return m != null && m.gameObject.activeInHierarchy; }
    static bool PageOpen(string page)
    {
        var m = PanelManager.Get("menu");
        if (m == null || !m.gameObject.activeInHierarchy) return false;
        foreach (var t in m.GetComponentsInChildren<Transform>(false)) if (t.name == page) return true;
        return false;
    }
    static bool InSpace() => ViewManager.Instance != null && ViewManager.Instance.Current == ViewManager.View.Space;
    static bool InHub() => ViewManager.Instance != null && ViewManager.Instance.Current == ViewManager.View.Hub;
    bool ViewChanged()
    {
        if (svc == null) svc = FindFirstObjectByType<SpaceViewController>();
        if (svc == null) return false;
        if (startKind < 0) { startKind = (int)svc.Current; return false; }
        return (int)svc.Current != startKind;
    }

    // ── where the arrow points ───────────────────────────────────────────────
    Vector3? NearestPanelTop()
    {
        var head = PanelManager.Head;
        if (head == null) return null;
        GrabbablePanel best = null; float bd = float.MaxValue; Bounds bb = default;
        foreach (var p in PanelManager.All)
        {
            if (p == null || !p.gameObject.activeInHierarchy || p.PanelId == "menu" || p.PanelId == "welcome") continue;
            bool any = false; Bounds b = default;
            foreach (var c in p.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any) continue;
            float d = (b.center - head.position).sqrMagnitude;
            if (d < bd) { bd = d; best = p; bb = b; }
        }
        if (best == null) return null;
        return new Vector3(bb.center.x, bb.max.y, bb.center.z);
    }

    static Vector3? MenuItemTop(string objName)
    {
        var m = PanelManager.Get("menu");
        if (m == null || !m.gameObject.activeInHierarchy) return null;
        foreach (var t in m.GetComponentsInChildren<RectTransform>(false))
        {
            if (t.name != objName) continue;
            var c = new Vector3[4];
            t.GetWorldCorners(c);
            return (c[1] + c[2]) * 0.5f;
        }
        return null;
    }

    Vector3? HandPos(string controlId)
    {
        bool left = !ControlMap.Get(controlId).Contains("{RightHand}");
        var h = FindHand(left);
        return h == null ? (Vector3?)null : h.position + PanelManager.Up * 0.05f;
    }

    readonly Transform[] hands = new Transform[2];
    float nextFind;
    Transform FindHand(bool left)
    {
        var c = hands[left ? 0 : 1];
        if (c != null) return c;
        var head = PanelManager.Head;
        if (head == null || Time.unscaledTime < nextFind) return null;
        nextFind = Time.unscaledTime + 1f;
        string side = left ? "left" : "right";
        Transform best = null; int bestDepth = int.MaxValue;
        foreach (var t in head.root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!n.Contains(side) || !n.Contains("controller")) continue;
            if (n.Contains("model") || n.Contains("visual") || n.Contains("ray") || n.Contains("teleport") || n.Contains("poke") || n.Contains("grab")) continue;
            int depth = 0; for (var p = t; p != null; p = p.parent) depth++;
            if (depth < bestDepth) { best = t; bestDepth = depth; }
        }
        hands[left ? 0 : 1] = best;
        return best;
    }

    // ── running ──────────────────────────────────────────────────────────────
    List<Step> steps;
    int index;
    bool running;
    float enteredAt, doneAt = -1f, changeStamp = -10f, nextText;
    bool cardPlaced;

    void Run()
    {
        if (cardRt == null) BuildUI();
        PanelManager.SetVisible("welcome", false);
        PanelManager.SetVisible("menu", false);
        PanelManager.Changed -= OnPanels;
        PanelManager.Changed += OnPanels;
        PanelManager.RigMoved -= OnRigMoved;
        PanelManager.RigMoved += OnRigMoved;
        steps = BuildSteps();
        running = true;
        cardPlaced = false;
        cardRt.gameObject.SetActive(true);
        Go(0);
    }

    // In space view the rig follows the satellite every frame; carry the card along with it, like the other panels
    void OnRigMoved(Matrix4x4 d)
    {
        if (cardRt == null || !running) return;
        cardRt.SetPositionAndRotation(d.MultiplyPoint3x4(cardRt.position), d.rotation * cardRt.rotation);
    }

    void OnPanels() { changeStamp = Time.unscaledTime; }

    void Go(int i)
    {
        if (i >= steps.Count) { Finish(true); return; }
        index = i;
        cardPlaced = false;           // each new step: the card reappears in front of you, wherever you are looking now
        doneAt = -1f;
        cardPlaced = false;                 // each new step brings the card back in front of you, wherever you are looking now
        enteredAt = Time.unscaledTime;
        var s = steps[i];
        s.enter?.Invoke();
        hdr.text = "<cspace=0.12em><b>STEP " + (i + 1) + " OF " + steps.Count + "</b></cspace>";
        title.text = "<b>" + s.title + "</b>";
        body.text = s.text();
        status.text = "";
        nextLabel.text = "<cspace=0.08em><b>" + (i == steps.Count - 1 ? "FINISH" : "NEXT") + "</b></cspace>";
    }

    void Finish(bool completed)
    {
        running = false;
        PanelManager.Changed -= OnPanels;
        PanelManager.RigMoved -= OnRigMoved;
        if (cardRt != null) cardRt.gameObject.SetActive(false);
        if (arrowRt != null) arrowRt.gameObject.SetActive(false);
        if (completed) CubeSettings.SetBool(SkipKey, true);      // seen it all: no need to greet them at every launch
    }

    void OnNext() { if (running) Go(index + 1); }
    void OnEnd() { Finish(false); }

    void Update()
    {
        if (!running) return;
        var s = steps[index];
        if (Time.unscaledTime >= nextText) { nextText = Time.unscaledTime + 0.05f; body.text = s.text(); }
        if (doneAt < 0f && s.done != null && Time.unscaledTime - enteredAt > 0.15f && s.done())
        {
            doneAt = Time.unscaledTime;
            string next = index + 1 < steps.Count ? steps[index + 1].lead : null;
            status.text = "<color=#" + CubeTheme.HexString(CubeTheme.Ok) + ">" + (next == null ? "Nice, that's everything." : "Nice, now " + next) + "</color>";
        }
        if (doneAt >= 0f && Time.unscaledTime - doneAt > 0.8f) Go(index + 1);
    }

    void LateUpdate()
    {
        if (!running) return;
        var head = PanelManager.Head;
        if (head == null) return;
        Vector3 up = PanelManager.Up;

        if (cardCanvas.worldCamera == null) cardCanvas.worldCamera = head.GetComponent<Camera>();

        // card: appears off to the right of where you were looking, then stays put in the world so you can look at it.
        // If you end up far from it (the view switched to space, or you walked off), it reappears in front of you.
        if (!cardPlaced || (cardRt.position - head.position).sqrMagnitude > 9f) PlaceCard(head, up);

        // arrow: above the target, pointing down at it, bobbing
        var tgt = steps[index].target?.Invoke();
        if (tgt.HasValue)
        {
            if (!arrowRt.gameObject.activeSelf) arrowRt.gameObject.SetActive(true);
            Vector3 pos = tgt.Value + up * (0.04f + 0.012f * Mathf.Sin(Time.unscaledTime * 5f));
            arrowRt.position = pos;
            Vector3 away = pos - head.position;
            if (away.sqrMagnitude > 1e-6f) arrowRt.rotation = Quaternion.LookRotation(away, up);
        }
        else if (arrowRt.gameObject.activeSelf) arrowRt.gameObject.SetActive(false);
    }

    void PlaceCard(Transform head, Vector3 up)
    {
        Vector3 refFwd = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = refFwd;
        Vector3 dir = Quaternion.AngleAxis(26f, up) * fwd.normalized;
        Vector3 pos = head.position + dir * 0.9f - up * 0.10f;
        cardRt.position = pos;
        cardRt.rotation = Quaternion.LookRotation(pos - head.position, up);
        cardPlaced = true;
    }

    // ── the card and the arrow ───────────────────────────────────────────────
    RectTransform cardRt, arrowRt;
    Canvas cardCanvas;
    TMP_Text hdr, title, body, status, nextLabel;

    void BuildUI()
    {
        // card
        var cg = new GameObject("Tutorial Card", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        cardCanvas = cg.AddComponent<Canvas>();
        cardCanvas.renderMode = RenderMode.WorldSpace;
        cg.AddComponent<TrackedDeviceGraphicRaycaster>();
        cardRt = (RectTransform)cg.transform;
        cardRt.sizeDelta = new Vector2(760f, 380f);
        cardRt.localScale = Vector3.one * 0.00065f;       // 0.76 m card x 0.65 = about 0.5 m wide

        var bg = UIKit.Rect("Background", cardRt, Vector2.zero, Vector2.one);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = CubeTheme.Panel; bgImg.raycastTarget = true;       // true: so the pointer doesn't pass through
        var ol = bg.gameObject.AddComponent<Outline>();
        ol.effectColor = CubeTheme.Border; ol.effectDistance = new Vector2(2f, 2f);

        var bar = UIKit.Rect("Accent", cardRt, new Vector2(0f, 0f), new Vector2(0.012f, 1f));
        var barImg = bar.gameObject.AddComponent<Image>();
        barImg.color = CubeTheme.Info; barImg.raycastTarget = false;

        hdr = UIKit.Text("Step", cardRt, new Vector2(0.05f, 0.85f), new Vector2(0.4f, 0.97f), 22, TextAlignmentOptions.MidlineLeft, CubeTheme.Dim);
        status = UIKit.Text("Status", cardRt, new Vector2(0.4f, 0.85f), new Vector2(0.95f, 0.97f), 22, TextAlignmentOptions.MidlineRight, CubeTheme.Ok);
        title = UIKit.Text("Title", cardRt, new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.85f), 40, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        body = UIKit.Text("Body", cardRt, new Vector2(0.05f, 0.25f), new Vector2(0.95f, 0.68f), 29, TextAlignmentOptions.TopLeft, CubeTheme.Text);
        body.enableAutoSizing = true; body.fontSizeMin = 18; body.fontSizeMax = 29;
        body.overflowMode = TextOverflowModes.Truncate;
        body.lineSpacing = 4f;

        var end = UIKit.Button("End tutorial", cardRt, new Vector2(0.05f, 0.05f), new Vector2(0.38f, 0.20f), 22, CubeTheme.Button, OnEnd);
        var next = UIKit.Button("Next", cardRt, new Vector2(0.62f, 0.05f), new Vector2(0.95f, 0.20f), 28, CubeTheme.ButtonAccent, OnNext);
        nextLabel = next.GetComponentInChildren<TMP_Text>();

        // arrow
        var ag = new GameObject("Tutorial Arrow", typeof(RectTransform));
        ag.transform.SetParent(transform, false);
        var ac = ag.AddComponent<Canvas>();
        ac.renderMode = RenderMode.WorldSpace;
        arrowRt = (RectTransform)ag.transform;
        arrowRt.sizeDelta = new Vector2(120f, 120f);
        arrowRt.localScale = Vector3.one * (0.07f / 120f);
        var ai = UIKit.Rect("Arrow", arrowRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
        ai.sprite = MakeArrow();
        ai.color = CubeTheme.Accent;
        ai.raycastTarget = false;
        var m = new Material(Canvas.GetDefaultCanvasMaterial()) { renderQueue = 4000 };
        m.SetInt("unity_GUIZTestMode", 8);                 // always on top, so a panel never hides it
        ai.material = m;

        cg.SetActive(false);
        ag.SetActive(false);
    }

    // a block arrow pointing down, drawn into a texture
    static Sprite MakeArrow()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N;           // v = 0 at the bottom (the tip)
                float half = v < 0.5f ? (v / 0.5f) * 0.46f : 0.16f;       // head widens up to v = 0.5, then the stem
                if (v > 0.96f) half = 0f;
                float a = Mathf.Clamp01((half - Mathf.Abs(u - 0.5f)) * N * 0.5f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }
}
