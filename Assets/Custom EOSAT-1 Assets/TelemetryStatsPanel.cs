using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Shows the parse-test numbers inside the headset: frames sent, parsed OK, applied, errors,
/// dropped, and how many frames had each of the four fields. Has RESET and SAVE buttons.
///
/// Everything is created when the app starts. Put this on an EMPTY child of a panel's Canvas whose
/// RectTransform is stretched to the area you want to fill (anchors stretched, all offsets 0),
/// the same way as the Connection panel.
/// </summary>
public class TelemetryStatsPanel : MonoBehaviour
{
    [SerializeField] TelemetryClient telemetry;
    [Tooltip("The area to fill. Leave empty to use this object.")]
    [SerializeField] RectTransform container;

    [Header("Look")]
    [SerializeField] int fontSize = 28;
    [SerializeField] int buttonFontSize = 30;
    [SerializeField] int noteFontSize = 18;
    [SerializeField] float valueColumnPercent = 52f;
    [SerializeField] Color labelColor = new Color(0.50f, 0.58f, 0.66f);
    [SerializeField] Color valueColor = new Color(0.85f, 0.90f, 0.95f);
    [SerializeField] Color keyColor = new Color(0.16f, 0.22f, 0.30f);
    [SerializeField] Color okColor = new Color(0.37f, 0.88f, 0.54f);
    [SerializeField] Color badColor = new Color(1.00f, 0.36f, 0.36f);

    TMP_Text leftText, rightText, noteText, testLabel;
    readonly StringBuilder sb = new StringBuilder(512);
    float nextRefresh;
    bool showLag;
    string labelHex, valueHex, okHex, badHex;

    void Start()
    {
        if (container == null) container = GetComponent<RectTransform>();
        if (telemetry == null) telemetry = FindAnyObjectByType<TelemetryClient>();
        labelHex = ColorUtility.ToHtmlStringRGB(labelColor);
        valueHex = ColorUtility.ToHtmlStringRGB(valueColor);
        okHex = ColorUtility.ToHtmlStringRGB(okColor);
        badHex = ColorUtility.ToHtmlStringRGB(badColor);

        Build();
        Refresh();
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        Refresh();
    }

    void Refresh()
    {
        if (telemetry == null || leftText == null) return;
        var s = telemetry.Stats;

        leftText.text = Format("PARSE TEST", s.CountRows());
        rightText.text = showLag
            ? Format("LAG TEST", s.LagRows())
            : Format("FIELDS READ PER FRAME", s.FieldRows());
    }

    string Format(string heading, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> rows)
    {
        sb.Clear();
        sb.Append("<b><color=#").Append(valueHex).Append('>').Append(heading).Append("</color></b>\n");
        foreach (var kv in rows)
        {
            string v = kv.Value;
            if (v == "YES") v = "<color=#" + okHex + ">YES</color>";
            else if (v == "NO") v = "<color=#" + badHex + ">NO</color>";
            sb.Append("<color=#").Append(labelHex).Append('>').Append(kv.Key).Append("</color>")
              .Append("<pos=").Append(valueColumnPercent.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append("%>")
              .Append("<color=#").Append(valueHex).Append('>').Append(v).Append("</color>\n");
        }
        return sb.ToString();
    }

    void OnReset()
    {
        if (telemetry == null) return;
        telemetry.ResetStats();
        noteText.text = "Counters reset.";
        Refresh();
    }

    void OnToggleView()
    {
        showLag = !showLag;
        Refresh();
    }

    void OnToggleTest()
    {
        if (telemetry == null) return;
        telemetry.TestMode = !telemetry.TestMode;
        UpdateTestLabel();
        noteText.text = telemetry.TestMode ? "Test files ON." : "Test files OFF.";
    }

    void UpdateTestLabel()
    {
        if (testLabel == null || telemetry == null) return;
        testLabel.text = telemetry.TestMode ? "TEST FILES: ON" : "TEST FILES: OFF";
    }

    void OnSave()
    {
        if (telemetry == null) return;
        string path = telemetry.SaveReport();
        noteText.text = string.IsNullOrEmpty(path) ? "Could not save." : "Saved:\n" + path;
    }

    // ── building the UI ──────────────────────────────────────────────────────
    void Build()
    {
        leftText = MakeText("Counts", new Vector2(0.03f, 0.16f), new Vector2(0.52f, 0.98f),
                            fontSize, TextAlignmentOptions.TopLeft, valueColor);
        rightText = MakeText("Fields", new Vector2(0.54f, 0.16f), new Vector2(0.98f, 0.98f),
                             fontSize, TextAlignmentOptions.TopLeft, valueColor);

        MakeButton("RESET", new Vector2(0.03f, 0.02f), new Vector2(0.17f, 0.14f), OnReset);
        MakeButton("SAVE", new Vector2(0.18f, 0.02f), new Vector2(0.32f, 0.14f), OnSave);
        MakeButton("FIELDS / LAG", new Vector2(0.33f, 0.02f), new Vector2(0.55f, 0.14f), OnToggleView);
        testLabel = MakeButton("", new Vector2(0.56f, 0.02f), new Vector2(0.80f, 0.14f), OnToggleTest);
        UpdateTestLabel();

        noteText = MakeText("Note", new Vector2(0.81f, 0.01f), new Vector2(0.99f, 0.15f),
                            noteFontSize, TextAlignmentOptions.MidlineLeft, labelColor);
        noteText.text = "";
    }

    RectTransform MakeRect(string name, Vector2 aMin, Vector2 aMax, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = container.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent != null ? parent : container, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    TMP_Text MakeText(string name, Vector2 aMin, Vector2 aMax, int size,
                      TextAlignmentOptions align, Color color, RectTransform parent = null)
    {
        var rt = MakeRect(name, aMin, aMax, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.richText = true;
        t.raycastTarget = false;     // text must never block the buttons
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    TMP_Text MakeButton(string label, Vector2 aMin, Vector2 aMax, UnityAction onClick)
    {
        var rt = MakeRect("Btn " + label, aMin, aMax);
        rt.offsetMin = new Vector2(5f, 5f);
        rt.offsetMax = new Vector2(-5f, -5f);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;     // the Button colours below do the tinting
        img.raycastTarget = true;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = keyColor;
        cb.highlightedColor = Color.Lerp(keyColor, Color.white, 0.18f);
        cb.selectedColor = keyColor;
        cb.pressedColor = Color.Lerp(keyColor, Color.white, 0.45f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        var t = MakeText("Label", Vector2.zero, Vector2.one, buttonFontSize,
                         TextAlignmentOptions.Center, valueColor, rt);
        t.text = label;
        return t;
    }
}
