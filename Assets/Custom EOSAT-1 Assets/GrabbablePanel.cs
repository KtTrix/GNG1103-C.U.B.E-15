using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Marks a panel as one the player can grab and place, like the Quest panels:
/// point at any part of the panel (or its handle bar) and squeeze the grip to move it.
/// (The moving itself is done by PanelGrabber.)
///
/// Put this on the panel's ROOT object (the one that contains the Canvas). The easiest way to get a ready-made
/// panel is the menu  CUBE15 > Panels > ...  which builds everything as normal objects you can edit.
/// It switches off the old template grab (XRGrabInteractable) and PanelFacePlayer on the same object.
/// </summary>
public class GrabbablePanel : MonoBehaviour
{
    [Tooltip("Short unique name used by the menu and buttons, e.g. welcome, position, map, connection.")]
    [SerializeField] string panelId = "panel";
    [Tooltip("Name shown in the Panels menu.")]
    [SerializeField] string displayName = "Panel";

    [Header("When the app starts")]
    [Tooltip("Move this panel in front of the player once the headset is tracking (the welcome panel does this).")]
    [SerializeField] bool placeInFrontOnStart = false;
    [Tooltip("Wait this long after the headset is tracking before the panel pops up (so the other panels appear first).")]
    [SerializeField] float placeDelay = 1.5f;
    [SerializeField] float startDistance = 1.5f;
    [SerializeField] float startYawDegrees = 0f;
    [Tooltip("Height relative to your eyes, in metres (negative = lower).")]
    [SerializeField] float startHeight = -0.05f;

    [Header("Default layout (after the welcome panel)")]
    [Tooltip("Show this panel and place it when the default layout is applied.")]
    [SerializeField] bool inDefaultLayout = false;
    [SerializeField] float layoutDistance = 1.6f;
    [Tooltip("Negative = to your left, positive = to your right.")]
    [SerializeField] float layoutYawDegrees = 0f;
    [SerializeField] float layoutHeight = -0.05f;

    [Header("Handle bar")]
    [Tooltip("Create the handle automatically if the panel has no object named 'Grab Handle'.")]
    [SerializeField] bool showHandle = true;
    [Range(0.1f, 1f)] [SerializeField] float handleWidth = 0.3f;
    [SerializeField] float handleThickness = 14f;
    [SerializeField] float handleGap = 14f;

    public enum AttachMode { World, Head, HandLeft, HandRight }

    bool pinLoaded, pinned, attachLoaded;
    AttachMode attach;

    /// <summary>A pinned panel cannot be grabbed (so it can't be moved by accident). Remembered between runs.</summary>
    public bool Pinned
    {
        get
        {
            if (!pinLoaded) { pinned = CubeSettings.GetBool("panel." + panelId + ".pin", false); pinLoaded = true; }
            return pinned;
        }
        set
        {
            pinned = value; pinLoaded = true;
            CubeSettings.SetBool("panel." + panelId + ".pin", value);
            PanelManager.NotifyChanged();
        }
    }

    /// <summary>World = stays where you put it, Head = lazy follow, HandLeft / HandRight = held at that controller. Remembered between runs.</summary>
    public AttachMode Attach
    {
        get
        {
            if (!attachLoaded) { attach = (AttachMode)CubeSettings.GetInt("panel." + panelId + ".attach", 0); attachLoaded = true; }
            return attach;
        }
        set
        {
            attach = value; attachLoaded = true;
            CubeSettings.SetInt("panel." + panelId + ".attach", (int)value);
            PanelManager.NotifyChanged();
        }
    }

    /// <summary>True once the player has pulled this panel out of its sliding track; later grabs then start free.</summary>
    public bool Unlocked;

    public string PanelId => panelId;
    public string DisplayName => displayName;
    public bool InDefaultLayout => inDefaultLayout;
    public float LayoutDistance => layoutDistance;
    public float LayoutYaw => layoutYawDegrees;
    public float LayoutHeight => layoutHeight;
    public bool IsGrabbed { get; internal set; }

    /// <summary>Width of the panel in metres.</summary>
    public float WidthMetres => canvasRect != null ? canvasRect.rect.width * canvasRect.lossyScale.x : 1f;

    /// <summary>Raised when the player lets go of the panel.</summary>
    public event Action<GrabbablePanel> Released;
    internal void RaiseReleased() => Released?.Invoke(this);

    RectTransform canvasRect;
    Canvas canvas;
    Image handleImage;
    bool hover;

    public void Configure(string id, string name) { panelId = id; displayName = name; }

    // ── scaling (the L-shaped corner handles; PanelGrabber does the dragging) ──
    public const float MinScale = 0.5f, MaxScale = 2.5f;
    Vector3 baseScale = Vector3.one;

    /// <summary>1 = the panel's own size.</summary>
    public float Scale => baseScale.x > 1e-5f ? transform.localScale.x / baseScale.x : 1f;
    string ScaleKey => "panel.scale." + panelId;

    /// <summary>The middle of the panel's face, in the world.</summary>
    public Vector3 CenterWorld => canvasRect != null ? canvasRect.TransformPoint(canvasRect.rect.center) : transform.position;
    /// <summary>The direction the panel's face points along (its plane's normal).</summary>
    public Vector3 FaceNormal => canvasRect != null ? canvasRect.forward : transform.forward;

    /// <summary>Resize the whole panel (contents, buttons, collision, everything) evenly, keeping its middle where it is.</summary>
    public void ScaleAboutCenter(float k)
    {
        k = Mathf.Clamp(k, MinScale, MaxScale);
        Vector3 c0 = CenterWorld;
        transform.localScale = baseScale * k;
        transform.position += c0 - CenterWorld;
    }

    public void SaveScale()
    {
        if (Mathf.Abs(Scale - 1f) < 0.01f) CubeSettings.Reset(ScaleKey); else CubeSettings.SetFloat(ScaleKey, Scale);
    }

    public void ResetScale()
    {
        ScaleAboutCenter(1f);
        CubeSettings.Reset(ScaleKey);
    }

    void LoadScale()
    {
        float k = CubeSettings.GetFloat(ScaleKey, 1f);
        if (Mathf.Abs(k - 1f) > 0.01f) ScaleAboutCenter(k);
    }

    // A small X in the title strip's top-right corner closes the panel (not on the menu or the welcome panel, which have their
    // own way to close).
    bool hasClose;

    void BuildClose()
    {
        if (canvasRect == null || canvasRect.Find("Close Button") != null) return;
        if (panelId == "menu" || panelId == "welcome") return;
        var btn = UIKit.Button("x", canvasRect, new Vector2(1f, 1f), new Vector2(1f, 1f), 26, CubeTheme.Button, ClosePanel);
        var rt = (RectTransform)btn.transform;
        rt.name = "Close Button";
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(40f, 34f);
        rt.anchoredPosition = new Vector2(-6f, -5f);
        hasClose = true;
    }

    void ClosePanel()
    {
        var ws = GetComponent<WorkspacePanel>();
        if (ws != null) { ws.CloseFromX(); return; }      // a Space View panel decides what closing means
        PanelManager.SetVisible(panelId, false);
    }

    // the note on the right of the title strip makes room for the X
    void ShiftSubtitle()
    {
        if (!hasClose || canvasRect == null) return;
        foreach (var t in canvasRect.GetComponentsInChildren<TMPro.TMP_Text>(true))
            if (t.name == "Subtitle")
            {
                var r = t.rectTransform;
                r.offsetMax = new Vector2(Mathf.Min(r.offsetMax.x, -58f), r.offsetMax.y);
            }
    }

    // Four little L-shaped brackets just outside the panel's corners. Grip on one and drag to make the panel bigger or smaller.
    const float CornerArm = 70f, CornerThick = 9f, CornerGap = 7f;

    void BuildCorners()
    {
        if (canvasRect == null || canvasRect.Find("Scale Corner TL") != null) return;
        string[] names = { "TL", "TR", "BL", "BR" };
        for (int i = 0; i < 4; i++)
        {
            float sx = (i % 2 == 0) ? -1f : 1f;       // -1 = left, 1 = right
            float sy = i < 2 ? 1f : -1f;              // 1 = top, -1 = bottom
            var go = new GameObject("Scale Corner " + names[i], typeof(RectTransform));
            go.layer = canvasRect.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvasRect, false);
            Vector2 corner = new Vector2(sx < 0 ? 0f : 1f, sy > 0 ? 1f : 0f);
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition3D = new Vector3(0f, 0f, 4f);

            // horizontal arm: outside the top / bottom edge, running inward from the corner
            var h = NewBar(rt, "H", new Vector2(sx < 0 ? 0f : 1f, sy > 0 ? 0f : 1f),
                           new Vector2(sx * (CornerGap + CornerThick), sy * CornerGap), new Vector2(CornerArm, CornerThick));
            // vertical arm: outside the left / right edge, running inward from the corner
            var v = NewBar(rt, "V", new Vector2(sx < 0 ? 1f : 0f, sy > 0 ? 1f : 0f),
                           new Vector2(sx * CornerGap, sy * (CornerGap + CornerThick)), new Vector2(CornerThick, CornerArm));

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(CornerArm + 30f, CornerArm + 30f, 14f);
            col.center = new Vector3(-sx * CornerArm * 0.3f + sx * (CornerGap + CornerThick) * 0.5f,
                                     -sy * CornerArm * 0.3f + sy * (CornerGap + CornerThick) * 0.5f, 0f);
            var ch = go.AddComponent<PanelCornerHandle>();
            ch.panel = this;
            ch.images = new[] { h, v };
        }
    }

    static Image NewBar(RectTransform parent, string name, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;         // the VR UI pointer passes through; the corner's own collider is what the grip finds
        return img;
    }

    void Awake()
    {
        baseScale = transform.localScale;
        // Old template behaviour on the same object would drag/turn the panel too. Switch it off (by name, so no package namespaces are needed).
        foreach (var b in GetComponents<Behaviour>())
        {
            string n = b.GetType().Name;
            if (n == "XRGrabInteractable" || n == "PanelFacePlayer") b.enabled = false;
        }

        canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null) canvasRect = (RectTransform)canvas.transform;
        else Debug.LogWarning("GrabbablePanel '" + panelId + "' has no Canvas under it.", this);

        if (canvasRect != null)
        {
            var existing = canvasRect.Find("Grab Handle");
            if (existing != null)
            {
                handleImage = existing.GetComponent<Image>();
                var h = existing.GetComponent<PanelHandle>();
                if (h == null) h = existing.gameObject.AddComponent<PanelHandle>();
                h.panel = this;
            }
        }

        if (canvasRect != null)
        {
            // A thin invisible box over the whole panel, so the grip can grab it anywhere (not just the handle bar).
            if (canvasRect.GetComponent<PanelHandle>() == null)
            {
                var body = canvasRect.GetComponent<BoxCollider>();
                if (body == null) body = canvasRect.gameObject.AddComponent<BoxCollider>();
                body.isTrigger = true;
                body.size = new Vector3(canvasRect.rect.width, canvasRect.rect.height, 10f);
                body.center = new Vector3(canvasRect.rect.center.x, canvasRect.rect.center.y, 0f);
                canvasRect.gameObject.AddComponent<PanelHandle>().panel = this;
            }

            // The panel's background must accept pointer hits, otherwise the VR rays pass straight through it
            // (and don't shorten or show a cursor when you aim at the panel).
            var bg = canvasRect.Find("Background");
            if (bg != null)
            {
                var im = bg.GetComponent<Image>();
                if (im != null)
                {
                    im.raycastTarget = true;

                    // A body that writes depth lets panels cut through each other cleanly where they overlap.
                    // It sits a few millimetres behind the text and buttons so those always draw on top of it.
                    var surface = Resources.Load<Material>("PanelSurface");
                    if (surface != null) im.material = surface;
                    var brt = (RectTransform)bg;
                    brt.anchoredPosition3D = new Vector3(brt.anchoredPosition.x, brt.anchoredPosition.y, 3f);

                    // The Outline effect draws extra copies of the body at exactly the same depth, which flickers once the
                    // body writes depth. Swap it for a plain border frame one millimetre behind the body.
                    var outline = bg.GetComponent<Outline>();
                    if (outline != null && outline.enabled)
                    {
                        float w = Mathf.Max(1f, outline.effectDistance.x);
                        outline.enabled = false;

                        var border = new GameObject("Border", typeof(RectTransform));
                        border.layer = bg.gameObject.layer;
                        var rt = (RectTransform)border.transform;
                        rt.SetParent(bg.parent, false);
                        rt.SetSiblingIndex(bg.GetSiblingIndex());
                        rt.anchorMin = brt.anchorMin; rt.anchorMax = brt.anchorMax; rt.pivot = brt.pivot;
                        rt.offsetMin = brt.offsetMin - new Vector2(w, w);
                        rt.offsetMax = brt.offsetMax + new Vector2(w, w);
                        rt.anchoredPosition3D = new Vector3(rt.anchoredPosition.x, rt.anchoredPosition.y, 4f);

                        var bi = border.AddComponent<Image>();
                        bi.color = outline.effectColor;
                        bi.raycastTarget = false;
                        if (surface != null) bi.material = surface;
                    }
                }
            }

            var headNow = PanelManager.Head;
            if (headNow != null) AssignEventCamera(headNow.GetComponent<Camera>());
        }

        BuildCorners();

        PanelManager.Register(this);

        // A panel that pops up at the start stays invisible until it has been placed in front of the player.
        if (placeInFrontOnStart) SetShown(false);
    }

    // Show or hide the panel's drawing and pointer targets without switching the object off.
    void SetShown(bool on)
    {
        foreach (var c in GetComponentsInChildren<Canvas>(true)) c.enabled = on;
        foreach (var col in GetComponentsInChildren<Collider>(true)) col.enabled = on;
    }

    void AssignEventCamera(Camera cam)
    {
        if (cam == null) return;
        foreach (var c in GetComponentsInChildren<Canvas>(true))
            if (c.renderMode == RenderMode.WorldSpace && c.worldCamera == null) c.worldCamera = cam;
    }

    IEnumerator Start()
    {
        LoadScale();
        BuildClose();
        if (showHandle && canvasRect != null && handleImage == null) BuildHandle();

        // World-space canvases need an event camera, or the VR rays can't click anything on them.
        yield return PanelManager.WaitForHead();
        var head = PanelManager.Head;
        if (head != null) AssignEventCamera(head.GetComponent<Camera>());
        ShiftSubtitle();

        if (placeInFrontOnStart)
        {
            // "Skip forever" on the welcome panel: it stays closed at launch (reopen it from the Panels page).
            // Options > Tutorial > "Welcome at launch" overrides that and shows it every time.
            if (panelId == "welcome" && CubeSettings.GetBool(Tutorial.SkipKey, false) && !CubeSettings.GetBool(Tutorial.AutoKey, false))
            {
                SetShown(true);
                gameObject.SetActive(false);
                PanelManager.NotifyChanged();
                yield break;
            }
            yield return new WaitForSecondsRealtime(placeDelay);
            PanelManager.PlaceInFront(this, startDistance, startYawDegrees, startHeight);
            SetShown(true);
        }
    }

    void OnDestroy() => PanelManager.Unregister(this);

    public void SetHover(bool on) { hover = on; }

    void BuildHandle()
    {
        float w = canvasRect.rect.width * handleWidth;
        var go = new GameObject("Grab Handle", typeof(RectTransform));
        go.layer = canvasRect.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvasRect, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -handleGap);
        rt.sizeDelta = new Vector2(w, handleThickness);

        handleImage = go.AddComponent<Image>();
        handleImage.raycastTarget = false;            // the VR UI pointer should pass through it

        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(w + 60f, handleThickness + 70f, 10f);
        col.center = new Vector3(0f, -35f, 0f);
        go.AddComponent<PanelHandle>().panel = this;
    }

    void Update()
    {
        if (handleImage == null) return;
        handleImage.color = Pinned ? CubeTheme.Border
                          : IsGrabbed ? CubeTheme.Accent
                          : hover ? CubeTheme.Text : CubeTheme.Dim;
    }
}


/// <summary>One of a panel's four L-shaped scale handles. PanelGrabber finds it with its ray and drags it.</summary>
public class PanelCornerHandle : MonoBehaviour
{
    public GrabbablePanel panel;
    public Image[] images;
    public bool Hover, Active;

    void Update()
    {
        if (images == null) return;
        Color c = (panel != null && panel.Pinned) ? new Color(CubeTheme.Dim.r, CubeTheme.Dim.g, CubeTheme.Dim.b, 0.25f)
                : Active ? CubeTheme.Accent
                : Hover ? CubeTheme.Text
                : new Color(CubeTheme.Dim.r, CubeTheme.Dim.g, CubeTheme.Dim.b, 0.8f);
        foreach (var i in images) if (i != null) i.color = c;
    }
}
