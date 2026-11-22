using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Shows a text file (Credits.txt) in a text object every time the page opens, so you can edit the credits
/// in Assets without touching the menu. Put this on the Credits page and fill in both fields.
/// The text scrolls however long it gets: point at it and drag with the trigger, or push a thumbstick up and down.
/// (The scrolling frame is made around the text object the first time the page opens, so the menu needs no changes.)
/// </summary>
public class CreditsText : MonoBehaviour
{
    public TextAsset file;
    public TMP_Text target;
    [Tooltip("How fast a thumbstick scrolls, in UI units per second.")]
    [SerializeField] float stickSpeed = 700f;

    RectTransform view, content, handle;
    ScrollRect scroll;
    bool built;

    void OnEnable()
    {
        if (target == null) return;
        Build();
        target.text = file != null ? file.text : "(No credits file assigned.)";
        if (content != null) content.anchoredPosition = Vector2.zero;       // always start at the top
    }

    // Wrap the text object in a masked, scrollable frame (done once).
    void Build()
    {
        if (built) return;
        built = true;
        if (target.GetComponentInParent<ScrollRect>() != null)               // already scrollable (made by hand): leave it
        {
            scroll = target.GetComponentInParent<ScrollRect>();
            content = scroll.content; view = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            return;
        }

        var tr = target.rectTransform;
        var parent = tr.parent;

        var vg = new GameObject("Credits Scroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        vg.layer = target.gameObject.layer;
        view = (RectTransform)vg.transform;
        view.SetParent(parent, false);
        view.SetSiblingIndex(tr.GetSiblingIndex());
        view.anchorMin = tr.anchorMin; view.anchorMax = tr.anchorMax; view.pivot = new Vector2(0.5f, 0.5f);
        view.offsetMin = tr.offsetMin; view.offsetMax = tr.offsetMax;
        var bg = vg.GetComponent<Image>(); bg.color = new Color(0, 0, 0, 0); bg.raycastTarget = true;   // catches the pointer

        tr.SetParent(view, false);
        content = tr;
        tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
        tr.offsetMin = new Vector2(0f, 0f); tr.offsetMax = new Vector2(-22f, 0f);     // leave room for the bar
        tr.anchoredPosition = Vector2.zero;
        if (target.enableAutoSizing) { target.enableAutoSizing = false; target.fontSize = Mathf.Max(target.fontSizeMax, 24f); }
        target.textWrappingMode = TextWrappingModes.Normal;
        target.overflowMode = TextOverflowModes.Overflow;
        var fit = target.gameObject.GetComponent<ContentSizeFitter>();
        if (fit == null) fit = target.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = vg.GetComponent<ScrollRect>();
        scroll.content = tr;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true; scroll.decelerationRate = 0.12f; scroll.scrollSensitivity = 30f;

        // a slim bar on the right showing where you are
        var bar = new GameObject("Scroll Bar", typeof(RectTransform), typeof(Image));
        bar.layer = vg.layer;
        var brt = (RectTransform)bar.transform;
        brt.SetParent(view, false);
        brt.anchorMin = new Vector2(1f, 0f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(1f, 0.5f);
        brt.sizeDelta = new Vector2(8f, 0f); brt.anchoredPosition = Vector2.zero;
        var bi = bar.GetComponent<Image>(); bi.color = new Color(1f, 1f, 1f, 0.12f); bi.raycastTarget = false;

        var hg = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        hg.layer = vg.layer;
        handle = (RectTransform)hg.transform;
        handle.SetParent(brt, false);
        handle.anchorMin = new Vector2(0f, 1f); handle.anchorMax = new Vector2(1f, 1f); handle.pivot = new Vector2(0.5f, 1f);
        var hi = hg.GetComponent<Image>(); hi.color = CubeTheme.Accent; hi.raycastTarget = false;
    }

    void Update()
    {
        if (view == null || content == null) return;

        // A thumbstick scrolls only while THAT hand's ray is pointing at the credits (otherwise it is walking / turning).
        float y = 0f;
        if (PointedAt(true)) y = Stick(XRNode.LeftHand);
        if (PointedAt(false)) { float y2 = Stick(XRNode.RightHand); if (Mathf.Abs(y2) > Mathf.Abs(y)) y = y2; }
        float max = Mathf.Max(0f, content.rect.height - view.rect.height);
        if (Mathf.Abs(y) > 0.2f && max > 0f)
        {
            if (scroll != null) scroll.velocity = Vector2.zero;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x,
                Mathf.Clamp(content.anchoredPosition.y + y * stickSpeed * Time.unscaledDeltaTime, 0f, max));
        }

        // the bar
        if (handle != null)
        {
            var track = (RectTransform)handle.parent;
            float th = track.rect.height;
            bool need = max > 0.5f && th > 1f;
            if (handle.gameObject.activeSelf != need) handle.gameObject.SetActive(need);
            if (need)
            {
                float hh = Mathf.Max(30f, th * view.rect.height / content.rect.height);
                float t = Mathf.Clamp01(content.anchoredPosition.y / max);
                handle.sizeDelta = new Vector2(0f, hh);
                handle.anchoredPosition = new Vector2(0f, -t * (th - hh));
            }
        }
    }

    XRRayInteractor[] rays = new XRRayInteractor[0];
    float nextFind;

    bool PointedAt(bool left)
    {
        if (Time.unscaledTime >= nextFind) { nextFind = Time.unscaledTime + 1f; rays = FindObjectsByType<XRRayInteractor>(); }
        foreach (var r in rays)
        {
            if (r == null || !r.isActiveAndEnabled) continue;
            bool isLeft = r.handedness == InteractorHandedness.Left, isRight = r.handedness == InteractorHandedness.Right;
            if (left ? !isLeft : !isRight) continue;
            if (r.TryGetCurrentUIRaycastResult(out RaycastResult hit) && hit.gameObject != null && hit.gameObject.transform.IsChildOf(view))
                return true;
        }
        return false;
    }

    static float Stick(XRNode node)
    {
        var d = InputDevices.GetDeviceAtXRNode(node);
        return d.isValid && d.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 a) ? a.y : 0f;
    }
}
