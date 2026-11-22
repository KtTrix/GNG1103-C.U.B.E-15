using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The warning popup: a small console-styled panel just below and in front of you, with a warning icon and text for each
/// active warning. Shown while any warning is active, in the hub and in space. It stays put while you look around a little,
/// and swings round when you turn far from it. Creates itself.
/// </summary>
public class WarningHud : MonoBehaviour
{
    const int MaxRows = 4;
    const float RowH = 108f, Pad = 18f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<WarningHud>() != null) return;
        var go = new GameObject("WarningHud");
        DontDestroyOnLoad(go);
        go.AddComponent<WarningHud>();
    }

    class Row { public GameObject go; public Image icon; public TextMeshProUGUI title, detail; }

    Canvas canvas;
    RectTransform panel;
    Image border, back;
    readonly List<Row> rows = new List<Row>();
    int shownVersion = -1;
    float shown;               // 0..1 fade
    CanvasGroup group;

    void Awake() { Build(); Application.onBeforeRender += Place; }
    void OnDestroy() { Application.onBeforeRender -= Place; }

    void Build()
    {
        var cg = new GameObject("Canvas", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        group = cg.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(640, 200);

        var b = new GameObject("Border", typeof(RectTransform), typeof(Image));
        b.transform.SetParent(cg.transform, false);
        border = b.GetComponent<Image>(); border.raycastTarget = false;
        panel = b.GetComponent<RectTransform>();
        var k = new GameObject("Back", typeof(RectTransform), typeof(Image));
        k.transform.SetParent(b.transform, false);
        back = k.GetComponent<Image>(); back.raycastTarget = false; back.color = new Color(CubeTheme.Panel.r, CubeTheme.Panel.g, CubeTheme.Panel.b, 1f);
        var kr = k.GetComponent<RectTransform>(); kr.anchorMin = Vector2.zero; kr.anchorMax = Vector2.one; kr.offsetMin = new Vector2(4, 4); kr.offsetMax = new Vector2(-4, -4);

        for (int i = 0; i < MaxRows; i++)
        {
            var r = new Row();
            r.go = new GameObject("Row" + i, typeof(RectTransform));
            r.go.transform.SetParent(b.transform, false);
            var rt = r.go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(640 - 2 * Pad, RowH);
            rt.anchoredPosition = new Vector2(0, -(Pad + i * RowH));

            var ic = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            ic.transform.SetParent(r.go.transform, false);
            r.icon = ic.GetComponent<Image>(); r.icon.sprite = WarningIcon.Sprite; r.icon.raycastTarget = false;
            var irt = ic.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0, 0.5f); irt.pivot = new Vector2(0, 0.5f);
            irt.sizeDelta = new Vector2(84, 84); irt.anchoredPosition = new Vector2(4, 0);

            r.title = Text(r.go.transform, "Title", 40, FontStyles.Bold, new Vector2(104, -8), new Vector2(0, 54));
            r.detail = Text(r.go.transform, "Detail", 28, FontStyles.Normal, new Vector2(104, -58), new Vector2(0, 40));
            r.detail.color = CubeTheme.Text;
            rows.Add(r);
        }
        OnTop();
        group.alpha = 0f;
        canvas.gameObject.SetActive(false);
    }

    // Draw the popup over everything else: last in the queue and ignoring depth, so nothing in the scene shows through it.
    void OnTop()
    {
        var baseMat = Canvas.GetDefaultCanvasMaterial();
        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            var m = new Material(baseMat) { renderQueue = 4000 };
            m.SetInt("unity_GUIZTestMode", 8);               // 8 = always
            img.material = m;
        }
        foreach (var t in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            var m = t.fontMaterial;                           // this text's own copy
            m.renderQueue = 4001;
            m.SetInt("unity_GUIZTestMode", 8);
        }
    }

    static TextMeshProUGUI Text(Transform parent, string name, float size, FontStyles style, Vector2 pos, Vector2 h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size; t.fontStyle = style; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        t.alignment = TextAlignmentOptions.TopLeft;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(-pos.x, h.y);
        return t;
    }

    void Update()
    {
        var m = WarningMonitor.Instance;
        var S = WarningSettings.Instance;
        bool want = m != null && S.popupEnabled && m.Active.Count > 0;
        shown = Mathf.MoveTowards(shown, want ? 1f : 0f, Time.unscaledDeltaTime / 0.2f);
        if (shown <= 0f)
        {
            if (canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
            return;
        }
        if (!canvas.gameObject.activeSelf) { canvas.gameObject.SetActive(true); shownVersion = -1; }
        group.alpha = shown;

        if (m != null && m.Version != shownVersion && m.Active.Count > 0)
        {
            shownVersion = m.Version;
            Refresh(m);
        }
    }

    void Refresh(WarningMonitor m)
    {
        var S = WarningSettings.Instance;
        int n = Mathf.Min(MaxRows, m.Active.Count);
        bool danger = false;
        for (int i = 0; i < rows.Count; i++)
        {
            bool on = i < n;
            rows[i].go.SetActive(on);
            if (!on) continue;
            var w = m.Active[i];
            danger |= w.danger;
            Color c = w.danger ? S.popupDangerColor : S.popupWarnColor;
            rows[i].title.text = w.title + (i == MaxRows - 1 && m.Active.Count > MaxRows ? "  +" + (m.Active.Count - MaxRows + 1) + " more" : "");
            rows[i].title.color = c;
            rows[i].detail.text = w.detail;
        }
        border.color = danger ? S.popupDangerColor : S.popupWarnColor;
        // as wide as the longest line needs, and no wider
        float need = 0f;
        for (int i = 0; i < n; i++)
            need = Mathf.Max(need, rows[i].title.GetPreferredValues(rows[i].title.text).x, rows[i].detail.GetPreferredValues(rows[i].detail.text).x);
        float width = Mathf.Clamp(Pad * 2f + 104f + need + 12f, 340f, 1100f);
        for (int i = 0; i < n; i++) rows[i].go.GetComponent<RectTransform>().sizeDelta = new Vector2(width - 2 * Pad, RowH);

        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f); panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(width, Pad * 2 + n * RowH);
        var crt = canvas.GetComponent<RectTransform>();
        crt.sizeDelta = panel.sizeDelta;
        crt.pivot = new Vector2(0.5f, 0f);   // sits on its bottom edge, above the controller
    }

    Transform leftHand;
    float nextFindHand;

    Transform FindLeftHand(Camera cam)
    {
        if (leftHand != null) return leftHand;
        if (Time.unscaledTime < nextFindHand) return null;
        nextFindHand = Time.unscaledTime + 1f;
        Transform best = null; int bestDepth = int.MaxValue;
        foreach (var t in cam.transform.root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("left") || !n.Contains("controller")) continue;
            if (n.Contains("model") || n.Contains("visual") || n.Contains("ray") || n.Contains("teleport") || n.Contains("poke") || n.Contains("grab")) continue;
            int depth = 0; for (var p = t; p != null; p = p.parent) depth++;
            if (depth < bestDepth) { best = t; bestDepth = depth; }
        }
        leftHand = best;
        return leftHand;
    }

    // Done as late as possible so it is right while the rig is being moved by the space view.
    void Place()
    {
        if (shown <= 0f) return;
        var cam = Camera.main;
        if (cam == null) return;
        var S = WarningSettings.Instance;
        Vector3 up = PanelManager.Up;

        Vector3 pos;
        var hand = FindLeftHand(cam);
        if (hand != null) pos = hand.position + up * S.popupAboveControllerM;
        else
        {
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);
            pos = cam.transform.position + right * S.popupFallbackOffset.x + up * S.popupFallbackOffset.y + fwd * S.popupFallbackOffset.z;
        }

        var t = canvas.transform;
        t.position = pos;
        // faces you, upright; the canvas is 640 units wide, so scale to the chosen width in metres
        Vector3 away = pos - cam.transform.position;
        if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, up);
        t.localScale = Vector3.one * (S.popupWidthMm * 0.001f / 640f);
    }
}
