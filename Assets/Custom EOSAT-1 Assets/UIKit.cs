using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Small shared helpers for building the project's panels in code, in the CubeTheme style.
/// One panel = a world-space Canvas where 1 canvas unit = 1 millimetre (so a 1100 x 720 canvas is 1.1 m x 0.72 m).
/// </summary>
public static class UIKit
{
    public const float UnitsToMetres = 0.001f;

    /// <summary>
    /// Creates a world-space panel (root object + Canvas) that VR pointers can click.
    /// Returns the Canvas RectTransform; put your content under it. The root has a GrabbablePanel and a PanelStyler.
    /// </summary>
    public static RectTransform CreatePanel(string id, string title, float widthUnits, float heightUnits,
                                            out GrabbablePanel panel, string subtitle = "")
    {
        var root = new GameObject("Panel_" + id);
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(root.transform, false);
        canvasGo.transform.localScale = Vector3.one * UnitsToMetres;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();   // lets the VR rays click the buttons

        var rt = (RectTransform)canvasGo.transform;
        rt.sizeDelta = new Vector2(widthUnits, heightUnits);

        var styler = canvasGo.AddComponent<PanelStyler>();
        styler.Configure(title, subtitle);

        panel = root.AddComponent<GrabbablePanel>();
        panel.Configure(id, title);
        return rt;
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    public static TMP_Text Text(string name, Transform parent, Vector2 aMin, Vector2 aMax, int size,
                                TextAlignmentOptions align, Color color)
    {
        var rt = Rect(name, parent, aMin, aMax);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.richText = true;
        t.raycastTarget = false;     // text must never block the buttons
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    public static Button Button(string label, Transform parent, Vector2 aMin, Vector2 aMax, int fontSize,
                                Color baseColor, UnityAction onClick)
    {
        var rt = Rect("Btn " + label, parent, aMin, aMax);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;     // the Button colours below do the tinting
        img.raycastTarget = true;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = baseColor;
        cb.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
        cb.selectedColor = baseColor;
        cb.pressedColor = Color.Lerp(baseColor, Color.white, 0.45f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        if (onClick != null) btn.onClick.AddListener(onClick);

        var t = Text("Label", rt, Vector2.zero, Vector2.one, fontSize, TextAlignmentOptions.Center, CubeTheme.Text);
        t.text = "<cspace=0.08em><b>" + label.ToUpperInvariant() + "</b></cspace>";
        return btn;
    }
}
