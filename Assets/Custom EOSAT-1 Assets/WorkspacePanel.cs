using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ViewKind = SpaceViewController.ViewKind;

/// <summary>
/// A Space View panel: a grid of live views of space, so you can stay in the hub and watch the satellite like a control room.
/// Starts with two views. "+ Add view" adds more (up to 4 in total across all space view panels), "Pop out" moves a view to its
/// own panel, "X" closes it, "Edit" (or the Edit hovered view button) lets you move and change the view with the sticks.
///
/// Build the first one with  CUBE15 > Panels > New Space View Panel  (in HubScene). Pop-out panels are made in code.
/// </summary>
public class WorkspacePanel : MonoBehaviour
{
    [SerializeField] RectTransform body;

    const float CellW = 520f, CellH = CellW / WorkspaceViews.Aspect, Gap = 16f, BarH = 64f, TitleH = 44f;

    class Tile
    {
        public ViewSlot slot;
        public RectTransform rt;
        public Image frame;
        public RawImage raw;
        public TMP_Text label, status, hint;
        public TMP_Text editLabel;
        public RectTransform strip;
        public TMP_Text[] texts;
        public float[] baseSizes;
        public float lastFactor = 1f;
    }

    readonly List<Tile> tiles = new List<Tile>();
    TMP_Text addLabel, helpText;
    Button addButton;
    RectTransform canvasRt;
    GrabbablePanel panel;
    bool isPopOut;
    bool started;
    int popNumber;
    static int popCounter;

    public bool IsPopOut => isPopOut;

    // ── pop-out panels are built in code ─────────────────────────────────────
    public static WorkspacePanel SpawnPopOut(ViewSlot slot)
    {
        int n = ++popCounter;
        var canvas = UIKit.CreatePanel("spaceview_" + n, "View " + n, 560f, 420f, out GrabbablePanel gp, "SPACE VIEW");
        gp.gameObject.SetActive(false);                        // finish setting up before it comes alive
        var bodyRt = UIKit.Rect("Body", canvas, Vector2.zero, Vector2.one);
        bodyRt.offsetMax = new Vector2(0f, -TitleH);
        var wp = gp.gameObject.AddComponent<WorkspacePanel>();
        wp.body = bodyRt;
        wp.isPopOut = true;
        wp.popNumber = n;
        wp.AdoptSlot(slot);
        gp.gameObject.SetActive(true);
        PanelManager.PlaceInFront(gp, 1.4f, n % 2 == 0 ? 25f : -25f, -0.05f);
        return wp;
    }

    // ── life ─────────────────────────────────────────────────────────────────
    void OnEnable()
    {
        if (body == null) { Debug.LogWarning("[Space View] No Body assigned on the Space View panel."); return; }
        canvasRt = body.parent as RectTransform;
        BuildBar();
        if (!started)
        {
            started = true;
            if (!isPopOut && tiles.Count == 0) AddDefaultViews();
        }
        else foreach (var t in tiles) t.rt.gameObject.SetActive(true);
        Layout();
        WorkspaceViews.Instance.Retain();
        ControlsHint();
    }

    void OnDisable()
    {
        if (WorkspaceViews.Exists) WorkspaceViews.Instance.Release();
    }

    void OnDestroy()
    {
        if (!WorkspaceViews.Exists) return;
        foreach (var t in tiles) if (t.slot != null && isPopOut) WorkspaceViews.Instance.Remove(t.slot);
    }

    void AddDefaultViews()
    {
        var wv = WorkspaceViews.Instance;
        var first = wv.Add(ViewKind.Overview);
        var second = wv.Add(ViewKind.OrbitFollow);
        if (first != null) tiles.Add(MakeTile(first));
        if (second != null) tiles.Add(MakeTile(second));
    }

    void AdoptSlot(ViewSlot slot)
    {
        started = true;
        if (slot != null) tiles.Add(MakeTile(slot));
    }

    // ── the bar at the bottom: add button and hint ───────────────────────────
    void BuildBar()
    {
        if (addButton != null) return;
        var bar = UIKit.Rect("Bar", body, new Vector2(0f, 0f), new Vector2(1f, 0f));
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, BarH);
        bar.anchoredPosition = Vector2.zero;
        addButton = UIKit.Button("+ Add view", bar, new Vector2(0f, 0f), new Vector2(0f, 1f), 24, CubeTheme.ButtonAccent, AddView);
        var brt = (RectTransform)addButton.transform;
        brt.pivot = new Vector2(0f, 0.5f);
        brt.sizeDelta = new Vector2(240f, -2f * Gap * 0.5f);
        brt.anchoredPosition = new Vector2(Gap, 0f);
        addLabel = addButton.transform.Find("Label").GetComponent<TMP_Text>();
        helpText = UIKit.Text("Help", bar, new Vector2(0f, 0f), new Vector2(1f, 1f), 20, TextAlignmentOptions.MidlineRight, CubeTheme.Dim);
        helpText.rectTransform.offsetMin = new Vector2(Gap * 2f + 240f, 0f);
        helpText.rectTransform.offsetMax = new Vector2(-Gap, 0f);
    }

    void ControlsHint()
    {
        if (helpText == null) return;
        helpText.text = "<b>Edit</b> a view, then: right stick zoom / turn · left stick tilt · A / B change view";
    }

    /// <summary>The X in the title strip. The main Space View panel just closes (its views are kept for next time); a pop-out panel
    /// closes and its views go with it.</summary>
    public void CloseFromX()
    {
        if (isPopOut)
        {
            foreach (var t in tiles) if (t.slot != null) WorkspaceViews.Instance.Remove(t.slot);
            tiles.Clear();
            Destroy(gameObject);
            return;
        }
        var gp = GetComponent<GrabbablePanel>();
        if (gp != null) PanelManager.SetVisible(gp.PanelId, false); else gameObject.SetActive(false);
    }

    // ── tiles ────────────────────────────────────────────────────────────────
    Tile MakeTile(ViewSlot slot)
    {
        var t = new Tile { slot = slot };
        var rt = UIKit.Rect("Tile", body, Vector2.zero, Vector2.zero);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(CellW, CellH);
        t.rt = rt;
        var hover = rt.gameObject.AddComponent<WorkspaceTile>();
        hover.slot = slot;

        var frame = UIKit.Rect("Frame", rt, Vector2.zero, Vector2.one);
        t.frame = frame.gameObject.AddComponent<Image>();
        t.frame.raycastTarget = false;

        var view = UIKit.Rect("View", rt, Vector2.zero, Vector2.one);
        view.offsetMin = new Vector2(3f, 3f); view.offsetMax = new Vector2(-3f, -3f);
        t.raw = view.gameObject.AddComponent<RawImage>();
        t.raw.color = new Color(1f, 1f, 1f, 1f);
        t.raw.raycastTarget = true;

        t.status = UIKit.Text("Status", view, Vector2.zero, Vector2.one, 24, TextAlignmentOptions.Center, CubeTheme.Dim);
        t.status.text = "Loading space...";

        // strip along the top with the name and the buttons
        var strip = UIKit.Rect("Strip", view, new Vector2(0f, 1f), new Vector2(1f, 1f));
        strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(0f, 42f);
        strip.anchoredPosition = Vector2.zero;
        var sImg = strip.gameObject.AddComponent<Image>();
        sImg.color = new Color(0f, 0f, 0f, 0.55f);
        sImg.raycastTarget = false;

        t.strip = strip;
        t.label = UIKit.Text("Name", strip, new Vector2(0f, 0f), new Vector2(0.42f, 1f), 21, TextAlignmentOptions.MidlineLeft, CubeTheme.Text);
        t.label.rectTransform.offsetMin = new Vector2(10f, 0f);
        t.label.overflowMode = TextOverflowModes.Ellipsis;

        var edit = UIKit.Button("Edit", strip, new Vector2(0.43f, 0.08f), new Vector2(0.63f, 0.92f), 19, CubeTheme.Button, () => OnEdit(t));
        t.editLabel = edit.transform.Find("Label").GetComponent<TMP_Text>();
        var pop = UIKit.Button("Pop out", strip, new Vector2(0.64f, 0.08f), new Vector2(0.88f, 0.92f), 19, CubeTheme.Button, () => PopOut(t));
        var close = UIKit.Button("X", strip, new Vector2(0.89f, 0.08f), new Vector2(0.99f, 0.92f), 19, CubeTheme.Button, () => Close(t));
        t.texts = new[] { t.label, t.editLabel, pop.transform.Find("Label").GetComponent<TMP_Text>(), close.transform.Find("Label").GetComponent<TMP_Text>() };
        t.baseSizes = new float[t.texts.Length];
        for (int k = 0; k < t.texts.Length; k++) t.baseSizes[k] = t.texts[k].fontSize;

        // shown while editing
        t.hint = UIKit.Text("Edit hint", view, new Vector2(0f, 0f), new Vector2(1f, 0f), 17, TextAlignmentOptions.Center, CubeTheme.Accent);
        t.hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        t.hint.rectTransform.sizeDelta = new Vector2(0f, 34f);
        t.hint.text = "EDITING  ·  right stick: zoom / turn  ·  left stick: tilt  ·  A / B: change view";
        t.hint.gameObject.SetActive(false);
        return t;
    }

    void OnEdit(Tile t) => WorkspaceViews.Instance.ToggleEdit(t.slot);

    void AddView()
    {
        var wv = WorkspaceViews.Instance;
        if (!wv.CanAdd) return;
        var slot = wv.Add(wv.UnusedKind());
        if (slot == null) return;
        tiles.Add(MakeTile(slot));
        Layout();
    }

    void Close(Tile t)
    {
        tiles.Remove(t);
        Destroy(t.rt.gameObject);
        WorkspaceViews.Instance.Remove(t.slot);
        AfterTilesChanged();
    }

    void PopOut(Tile t)
    {
        // a view that is already alone on a pop-out panel has nowhere to go
        if (isPopOut && tiles.Count <= 1) return;
        tiles.Remove(t);
        WorkspaceViews.Instance.TileGone(t.slot);
        Destroy(t.rt.gameObject);
        SpawnPopOut(t.slot);
        AfterTilesChanged();
    }

    void AfterTilesChanged()
    {
        if (tiles.Count == 0)
        {
            // No views left: a pop-out panel goes away; the main Space View panel switches itself off (it starts with
            // its two default views again the next time it is opened).
            if (isPopOut) { Destroy(gameObject); return; }
            started = false;
            var gp = GetComponent<GrabbablePanel>();
            if (gp != null) PanelManager.SetVisible(gp.PanelId, false); else gameObject.SetActive(false);
            return;
        }
        Layout();
    }

    // ── layout: the panel grows with the number of views ─────────────────────
    void Layout()
    {
        if (canvasRt == null) return;
        int n = tiles.Count;
        int cols = n <= 1 ? 1 : 2;
        int rows = Mathf.Max(1, (n + cols - 1) / cols);
        float w = cols * CellW + (cols + 1) * Gap;
        float h = TitleH + rows * CellH + (rows + 1) * Gap + BarH;
        if (n == 0) h = TitleH + 2f * Gap + BarH;
        if (isPopOut && n <= 1) w = Mathf.Max(w, 560f);
        canvasRt.sizeDelta = new Vector2(w, h);

        for (int i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            tiles[i].rt.anchoredPosition = new Vector2(Gap + c * (CellW + Gap), -(Gap + r * (CellH + Gap)));
        }

        // the invisible box that lets the grip take hold anywhere on the panel follows the size too
        var bodyBox = canvasRt.GetComponent<BoxCollider>();
        if (bodyBox != null)
        {
            bodyBox.size = new Vector3(w, h, bodyBox.size.z);
            bodyBox.center = new Vector3(canvasRt.rect.center.x, canvasRt.rect.center.y, 0f);
        }

        // the grab handle is as wide as 30% of the panel
        var handle = canvasRt.Find("Grab Handle") as RectTransform;
        if (handle != null)
        {
            float hw = w * 0.3f;
            handle.sizeDelta = new Vector2(hw, handle.sizeDelta.y);
            var col = handle.GetComponent<BoxCollider>();
            if (col != null) col.size = new Vector3(hw + 60f, col.size.y, col.size.z);
        }
    }

    // ── each frame ───────────────────────────────────────────────────────────
    void Update()
    {
        var wv = WorkspaceViews.Instance;
        var head = PanelManager.Head;

        // The panel can be scaled up with the corner handles. The buttons on each view grow less than the panel does, so they
        // do not get abnormally large: at 2x size they are about 1.5x, at 2.5x about 1.7x.
        if (panel == null) panel = GetComponent<GrabbablePanel>();
        float sc = panel != null ? panel.Scale : 1f;
        float factor = sc > 1f ? Mathf.Pow(sc, -0.6f) : 1f;

        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            if (t.slot == null) continue;

            // a tile counts as watched when it is roughly in front of the player
            bool watched = true;
            if (head != null)
            {
                Vector3 centre = t.rt.TransformPoint(t.rt.rect.center);
                Vector3 to = centre - head.position;
                watched = to.sqrMagnitude < 36f && Vector3.Angle(head.forward, to) < 60f;
            }
            if (watched) t.slot.Seen();

            if (t.slot.Tex != null && t.raw.texture != t.slot.Tex) t.raw.texture = t.slot.Tex;
            bool has = t.slot.HasImage && t.slot.Tex != null;
            t.raw.color = has ? Color.white : new Color(0.04f, 0.05f, 0.08f, 1f);
            if (t.status.gameObject.activeSelf == has) t.status.gameObject.SetActive(!has);
            if (!has) t.status.text = wv.SpaceReady ? "Starting view..." : "Waiting for space data...";

            if (Mathf.Abs(factor - t.lastFactor) > 0.005f && t.strip != null)
            {
                t.lastFactor = factor;
                t.strip.sizeDelta = new Vector2(0f, 42f * factor);
                for (int k = 0; k < t.texts.Length; k++) t.texts[k].fontSize = t.baseSizes[k] * factor;
            }

            string name = wv.LabelOf(t.slot);
            if (t.label.text != name) t.label.text = name;

            bool editing = wv.Editing == t.slot;
            bool hover = wv.Hovered == t.slot;
            t.frame.color = editing ? CubeTheme.Accent : (hover ? Color.Lerp(CubeTheme.Border, Color.white, 0.45f) : CubeTheme.Border);
            if (t.hint.gameObject.activeSelf != editing) t.hint.gameObject.SetActive(editing);
            string el = "<cspace=0.08em><b>" + (editing ? "DONE" : "EDIT") + "</b></cspace>";
            if (t.editLabel.text != el) t.editLabel.text = el;
        }

        if (addButton != null)
        {
            bool can = wv.CanAdd;
            addButton.interactable = can;
            string txt = "<cspace=0.08em><b>" + (can ? "+ ADD VIEW" : "MAX 4 VIEWS") + "</b></cspace>";
            if (addLabel.text != txt) addLabel.text = txt;
        }
        if (Time.frameCount % 30 == 0) ControlsHint();
    }
}

/// <summary>On a tile: tells the space view which view the pointer is over (so the Edit button knows which one you mean).</summary>
public class WorkspaceTile : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public ViewSlot slot;

    public void OnPointerEnter(PointerEventData e) { if (WorkspaceViews.Exists) WorkspaceViews.Instance.Hovered = slot; }

    public void OnPointerExit(PointerEventData e)
    {
        if (WorkspaceViews.Exists && WorkspaceViews.Instance.Hovered == slot) WorkspaceViews.Instance.Hovered = null;
    }

    void OnDisable()
    {
        if (WorkspaceViews.Exists) WorkspaceViews.Instance.TileGone(slot);
    }
}
