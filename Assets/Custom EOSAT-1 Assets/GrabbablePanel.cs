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

    void Awake()
    {
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
        if (showHandle && canvasRect != null && handleImage == null) BuildHandle();

        // World-space canvases need an event camera, or the VR rays can't click anything on them.
        yield return PanelManager.WaitForHead();
        var head = PanelManager.Head;
        if (head != null) AssignEventCamera(head.GetComponent<Camera>());

        if (placeInFrontOnStart)
        {
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
