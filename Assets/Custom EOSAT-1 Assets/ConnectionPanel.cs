using System.Net;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// An in-headset "Connection" panel: shows whether the telemetry link is up and lets you type
/// the PC's IP address on a keypad, so you never have to rebuild just to change the address.
///
/// Everything (text, keypad, buttons) is created by this script when the app starts, so there is
/// nothing to lay out by hand. Put it on an EMPTY child of a panel's Canvas whose RectTransform
/// is stretched to the area you want to fill (anchors stretched, all offsets 0).
/// </summary>
public class ConnectionPanel : MonoBehaviour
{
    [SerializeField] TelemetryClient telemetry;
    [Tooltip("The area to fill. Leave empty to use this object.")]
    [SerializeField] RectTransform container;

    [Header("Behaviour")]
    [Tooltip("The address the 'USB cable' button uses (works with: adb reverse tcp:8100 tcp:8100).")]
    [SerializeField] string usbHost = "127.0.0.1";
    [SerializeField] float staleAfterSeconds = 2f;

    [Header("Look")]
    [SerializeField] int statusFontSize = 40;
    [SerializeField] int ipFontSize = 56;
    [SerializeField] int keyFontSize = 46;
    [SerializeField] int buttonFontSize = 34;
    [SerializeField] int hintFontSize = 26;
    [SerializeField] Color panelTextColor = new Color(0.85f, 0.90f, 0.95f);
    [SerializeField] Color dimTextColor = new Color(0.50f, 0.58f, 0.66f);
    [SerializeField] Color keyColor = new Color(0.16f, 0.22f, 0.30f);
    [SerializeField] Color accentColor = new Color(0.12f, 0.45f, 0.30f);
    [SerializeField] Color okColor = new Color(0.37f, 0.88f, 0.54f);
    [SerializeField] Color warnColor = new Color(0.91f, 0.64f, 0.24f);
    [SerializeField] Color badColor = new Color(1.00f, 0.36f, 0.36f);

    const int MaxLength = 15;   // "255.255.255.255"

    TMP_Text statusText, ipText, hintText;
    string typed = "";
    string message = "";
    float nextRefresh;

    void Start()
    {
        if (container == null) container = GetComponent<RectTransform>();
        if (telemetry == null) telemetry = FindAnyObjectByType<TelemetryClient>();
        if (telemetry != null) typed = telemetry.Host;

        Build();
        Refresh();
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        Refresh();
    }

    // ── actions ──────────────────────────────────────────────────────────────
    void Append(string s)
    {
        if (typed.Length >= MaxLength) return;
        typed += s;
        message = "";
        Refresh();
    }

    void Backspace()
    {
        if (typed.Length > 0) typed = typed.Substring(0, typed.Length - 1);
        message = "";
        Refresh();
    }

    void Clear()
    {
        typed = "";
        message = "";
        Refresh();
    }

    void UseUsb()
    {
        typed = usbHost;
        Connect();
    }

    void Connect()
    {
        if (telemetry == null) { message = "No Telemetry object found."; Refresh(); return; }

        if (!IsValidIPv4(typed))
        {
            message = "<color=#" + ColorUtility.ToHtmlStringRGB(badColor) +
                      ">Not a valid address.</color> It should look like 192.168.1.23";
            Refresh();
            return;
        }

        telemetry.SetEndpoint(typed, telemetry.Port, true);
        message = "Connecting to " + typed + " ...";
        Refresh();
    }

    static bool IsValidIPv4(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        string[] parts = s.Split('.');
        if (parts.Length != 4) return false;
        foreach (string p in parts)
            if (p.Length == 0 || p.Length > 3) return false;   // TryParse alone would accept "1.2.3"
        return IPAddress.TryParse(s, out _);
    }

    // ── what is shown ────────────────────────────────────────────────────────
    void Refresh()
    {
        if (statusText != null && telemetry != null)
        {
            string badge; Color c;
            if (!telemetry.IsConnected) { badge = "NO LINK"; c = badColor; }
            else if (telemetry.FramesReceived == 0) { badge = "WAITING"; c = warnColor; }
            else if (telemetry.SecondsSinceLastFrame > staleAfterSeconds) { badge = "STALE"; c = warnColor; }
            else { badge = "LIVE"; c = okColor; }

            string detail = telemetry.IsConnected
                ? "connected, receiving data"
                : (string.IsNullOrEmpty(telemetry.LastError) ? "connecting..." : telemetry.LastError + "  (retrying)");

            statusText.text =
                "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + badge + "</color>   " +
                telemetry.Host + ":" + telemetry.Port +
                "\n<size=60%><color=#" + ColorUtility.ToHtmlStringRGB(dimTextColor) + ">" + detail + "</color></size>";
        }

        if (ipText != null)
        {
            bool caret = (Time.unscaledTime % 1f) < 0.5f;
            ipText.text = typed.Length == 0
                ? "<color=#" + ColorUtility.ToHtmlStringRGB(dimTextColor) + ">type the PC's IP address</color>"
                : typed + (caret ? "_" : " ");
        }

        if (hintText != null)
        {
            hintText.text = message.Length > 0
                ? message
                : "<color=#" + ColorUtility.ToHtmlStringRGB(dimTextColor) +
                  ">On the PC run  ipconfig  and use the IPv4 Address of the Wi-Fi adapter. " +
                  "The PC and headset must be on the same Wi-Fi. Then press CONNECT.</color>";
        }
    }

    // ── building the UI ──────────────────────────────────────────────────────
    void Build()
    {
        // Status / title strip
        statusText = MakeText("Status", new Vector2(0.03f, 0.80f), new Vector2(0.97f, 0.99f),
                              statusFontSize, TextAlignmentOptions.MidlineLeft, panelTextColor);

        // IP box
        var ipBox = MakeRect("IP Box", new Vector2(0.03f, 0.62f), new Vector2(0.60f, 0.78f));
        var ipBg = ipBox.gameObject.AddComponent<Image>();
        ipBg.color = new Color(0.06f, 0.09f, 0.13f, 1f);
        ipBg.raycastTarget = false;
        ipText = MakeText("IP Text", Vector2.zero, Vector2.one, ipFontSize,
                          TextAlignmentOptions.MidlineLeft, panelTextColor, ipBox);
        ipText.rectTransform.offsetMin = new Vector2(16f, 0f);
        ipText.rectTransform.offsetMax = new Vector2(-8f, 0f);

        // Keypad: 4 rows x 3 columns
        string[] labels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "DEL" };
        const float x0 = 0.03f, x1 = 0.60f, yTop = 0.58f, yBottom = 0.04f;
        float cw = (x1 - x0) / 3f, ch = (yTop - yBottom) / 4f;
        for (int i = 0; i < labels.Length; i++)
        {
            int row = i / 3, col = i % 3;
            Vector2 aMin = new Vector2(x0 + col * cw, yTop - (row + 1) * ch);
            Vector2 aMax = new Vector2(x0 + (col + 1) * cw, yTop - row * ch);
            string label = labels[i];
            UnityAction act = label == "DEL" ? (UnityAction)Backspace : () => Append(label);
            MakeButton(label, aMin, aMax, keyFontSize, keyColor, act);
        }

        // Right-hand buttons
        MakeButton("CONNECT", new Vector2(0.64f, 0.62f), new Vector2(0.97f, 0.78f), buttonFontSize, accentColor, Connect);
        MakeButton("USB CABLE", new Vector2(0.64f, 0.44f), new Vector2(0.97f, 0.60f), buttonFontSize, keyColor, UseUsb);
        MakeButton("CLEAR", new Vector2(0.64f, 0.26f), new Vector2(0.97f, 0.42f), buttonFontSize, keyColor, Clear);

        // Hint / messages
        hintText = MakeText("Hint", new Vector2(0.64f, 0.02f), new Vector2(0.99f, 0.25f),
                            hintFontSize, TextAlignmentOptions.TopLeft, dimTextColor);
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

    void MakeButton(string label, Vector2 aMin, Vector2 aMax, int fontSize, Color baseColor, UnityAction onClick)
    {
        var rt = MakeRect("Btn " + label, aMin, aMax);
        // A little gap between neighbouring buttons.
        rt.offsetMin = new Vector2(5f, 5f);
        rt.offsetMax = new Vector2(-5f, -5f);

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
        btn.onClick.AddListener(onClick);

        var t = MakeText("Label", Vector2.zero, Vector2.one, fontSize,
                         TextAlignmentOptions.Center, panelTextColor, rt);
        t.text = label;
    }
}
