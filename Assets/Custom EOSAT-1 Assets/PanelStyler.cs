using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives a panel the client's console look: dark body, hairline border, and a small-caps title strip
/// with a thin divider. Put it on a panel's Canvas (or the panel's background object).
///
/// Everything is created when the app starts. If the title strip covers your content, untick Show Title
/// or lower Title Height. Nothing here blocks the pointer.
/// </summary>
[DisallowMultipleComponent]
public class PanelStyler : MonoBehaviour
{
    [Tooltip("The area to dress. Leave empty to use this object's RectTransform.")]
    [SerializeField] RectTransform target;

    [Header("Title strip")]
    [SerializeField] bool showTitle = true;
    [SerializeField] string title = "PANEL";
    [Tooltip("Height of the title strip in canvas units (pixels of the panel's Canvas).")]
    [SerializeField] float titleHeight = 44f;
    [SerializeField] int titleFontSize = 26;
    [Tooltip("Small text on the right of the strip, e.g. a data source or a mode.")]
    [SerializeField] string subtitle = "";

    [Header("Surface")]
    [SerializeField] bool drawBackground = true;
    [SerializeField] bool drawBorder = true;
    [SerializeField] float borderWidth = 2f;

    TMP_Text titleText, subtitleText;

    void Start()
    {
        if (target == null) target = GetComponent<RectTransform>();
        if (target == null) return;
        Build();
    }

    /// <summary>Set the title and note before the panel is first shown (used when a panel is built in code).</summary>
    public void Configure(string newTitle, string newSubtitle = "") { title = newTitle; subtitle = newSubtitle; }

    /// <summary>Change the right-hand note at runtime (e.g. "LIVE · 10 Hz").</summary>
    public void SetSubtitle(string s) { subtitle = s; if (subtitleText != null) subtitleText.text = CubeTheme.Title(s); }

    void Build()
    {
        var root = NewRect("Style", target);
        root.SetAsFirstSibling();                  // behind the existing content
        Stretch(root);

        if (drawBackground)
        {
            var bg = NewRect("Body", root);
            Stretch(bg);
            var img = bg.gameObject.AddComponent<Image>();
            img.color = CubeTheme.Panel;
            img.raycastTarget = true;     // the VR pointer must be able to hit the panel body
            if (drawBorder)
            {
                var o = bg.gameObject.AddComponent<Outline>();
                o.effectColor = CubeTheme.Border;
                o.effectDistance = new Vector2(borderWidth, borderWidth);
            }
        }

        if (!showTitle) return;

        var strip = NewRect("Title Strip", root);
        strip.anchorMin = new Vector2(0f, 1f);
        strip.anchorMax = new Vector2(1f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(0f, titleHeight);
        strip.anchoredPosition = Vector2.zero;

        var divider = NewRect("Divider", strip);
        divider.anchorMin = new Vector2(0f, 0f);
        divider.anchorMax = new Vector2(1f, 0f);
        divider.sizeDelta = new Vector2(0f, borderWidth);
        var dImg = divider.gameObject.AddComponent<Image>();
        dImg.color = CubeTheme.Border;
        dImg.raycastTarget = false;

        titleText = NewText("Title", strip, TextAlignmentOptions.MidlineLeft);
        titleText.text = CubeTheme.Title(title);
        titleText.rectTransform.offsetMin = new Vector2(18f, 0f);

        subtitleText = NewText("Subtitle", strip, TextAlignmentOptions.MidlineRight);
        subtitleText.text = CubeTheme.Title(subtitle);
        subtitleText.rectTransform.offsetMax = new Vector2(-18f, 0f);
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = target.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    TMP_Text NewText(string name, RectTransform parent, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        Stretch(rt);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = titleFontSize;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }
}
