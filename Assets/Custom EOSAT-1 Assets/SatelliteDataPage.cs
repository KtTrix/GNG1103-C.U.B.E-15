using System.Net;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The menu's Satellite Data page: shows the link status and lets you type the simulator PC's IP address and port
/// on a keypad. Put it on Page_SatelliteData. Everything it needs is an ordinary object under the page; the buttons call
/// its public methods (Press, Backspace, Clear, Connect, Disconnect, UseUsb, SelectIp, SelectPort), set up by CUBE15 > Panels > Build Menu.
/// The address and port are remembered by TelemetryClient for the next run.
/// </summary>
public class SatelliteDataPage : MonoBehaviour
{
    [SerializeField] TelemetryClient telemetry;
    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text ipText;
    [SerializeField] TMP_Text portText;
    [SerializeField] Image ipBox;
    [SerializeField] Image portBox;
    [Tooltip("Only on the stand-alone panel: the instructions that cover the keypad while the Instructions button is on.")]
    [SerializeField] GameObject helpOverlay;

    [Tooltip("The address the USB CABLE button uses (works with: adb reverse tcp:8100 tcp:8100).")]
    [SerializeField] string usbHost = "127.0.0.1";
    [SerializeField] float staleAfterSeconds = 2f;

    string ip = "", port = "";
    bool editingPort;
    string message = "";
    float nextRefresh;

    void OnEnable()
    {
        EnsureDisconnectButton();
        if (telemetry == null) telemetry = FindAnyObjectByType<TelemetryClient>();
        if (telemetry != null) { ip = telemetry.Host; port = telemetry.Port.ToString(); }
        editingPort = false;
        message = "";
        if (helpOverlay != null) helpOverlay.SetActive(false);
        Refresh();
    }

    // The menu's copy of this page was built before Disconnect existed. If this page has no Disconnect button, make one
    // from the Clear button and lay the five buttons out like the stand-alone panel (Connect, Disconnect, USB, Clear, Instructions).
    void EnsureDisconnectButton()
    {
        Button clear = null, conn = null, usb = null, ins = null;
        foreach (var b in GetComponentsInChildren<Button>(true))
        {
            string n = b.name.ToLowerInvariant();
            if (n.Contains("disconnect")) return;
            if (n.EndsWith("clear")) clear = b;
            else if (n.EndsWith("connect")) conn = b;
            else if (n.Contains("usb")) usb = b;
            else if (n.Contains("instructions")) ins = b;
        }
        if (clear == null || conn == null) return;

        var go = Instantiate(clear.gameObject, clear.transform.parent);
        go.name = clear.name.ToLowerInvariant().Contains("clear") ? clear.name.Replace("Clear", "Disconnect").Replace("clear", "Disconnect") : "Btn Disconnect";
        var disc = go.GetComponent<Button>();
        for (int i = 0; i < disc.onClick.GetPersistentEventCount(); i++)
            disc.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);   // the copy must not also "Clear"
        disc.onClick.AddListener(Disconnect);
        var label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "Disconnect";

        var order = new[] { conn, disc, usb, clear, ins };
        const float top = 0.70f, step = 0.104f;
        for (int i = 0; i < order.Length; i++)
        {
            if (order[i] == null) continue;
            var rt = (RectTransform)order[i].transform;
            rt.anchorMin = new Vector2(0.65f, top - (i + 1) * step + 0.006f);
            rt.anchorMax = new Vector2(0.96f, top - i * step - 0.006f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        Refresh();
    }

    // ── buttons ──────────────────────────────────────────────────────────────
    /// <summary>Show or hide the instructions (stand-alone panel only; the menu has a page for them).</summary>
    public void ToggleHelp() { if (helpOverlay != null) helpOverlay.SetActive(!helpOverlay.activeSelf); }

    public void SelectIp() { editingPort = false; Refresh(); }
    public void SelectPort() { editingPort = true; Refresh(); }

    public void Press(string key)
    {
        message = "";
        if (editingPort)
        {
            if (key != "." && port.Length < 5) port += key;
        }
        else if (ip.Length < 15) ip += key;
        Refresh();
    }

    public void Backspace()
    {
        message = "";
        if (editingPort) { if (port.Length > 0) port = port.Substring(0, port.Length - 1); }
        else if (ip.Length > 0) ip = ip.Substring(0, ip.Length - 1);
        Refresh();
    }

    public void Clear()
    {
        message = "";
        if (editingPort) port = ""; else ip = "";
        Refresh();
    }

    public void UseUsb()
    {
        ip = usbHost;
        Connect();
    }

    /// <summary>Stop the link and leave it stopped until Connect or USB Cable is pressed.</summary>
    public void Disconnect()
    {
        if (telemetry == null) { message = "No Telemetry object found."; Refresh(); return; }
        telemetry.Disconnect();
        message = "Disconnected. Press Connect to link again.";
        Refresh();
    }

    public void Connect()
    {
        if (telemetry == null) { message = "No Telemetry object found."; Refresh(); return; }

        if (!IsValidIPv4(ip))
        {
            message = Tag(CubeTheme.Bad, "Not a valid address.") + " Like 192.168.1.23";
            Refresh();
            return;
        }
        if (!int.TryParse(port, out int p) || p < 1 || p > 65535)
        {
            message = Tag(CubeTheme.Bad, "Not a valid port.") + " 1 to 65535";
            Refresh();
            return;
        }

        telemetry.SetEndpoint(ip, p, true);
        message = "Connecting to " + ip + ":" + p + " ...";
        Refresh();
    }

    static bool IsValidIPv4(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        string[] parts = s.Split('.');
        if (parts.Length != 4) return false;
        foreach (string p in parts)
            if (p.Length == 0 || p.Length > 3) return false;
        return IPAddress.TryParse(s, out _);
    }

    // ── what is shown ────────────────────────────────────────────────────────
    static string Tag(Color c, string s) => "<color=#" + CubeTheme.HexString(c) + ">" + s + "</color>";

    void Refresh()
    {
        if (statusText != null && telemetry != null)
        {
            string badge; Color c;
            if (telemetry.UserDisconnected) { badge = "DISCONNECTED"; c = CubeTheme.Dim; }
            else if (!telemetry.IsConnected) { badge = "NO LINK"; c = CubeTheme.Bad; }
            else if (telemetry.FramesReceived == 0) { badge = "WAITING"; c = CubeTheme.Warn; }
            else if (telemetry.SecondsSinceLastFrame > staleAfterSeconds) { badge = "STALE"; c = CubeTheme.Warn; }
            else { badge = "LIVE"; c = CubeTheme.Ok; }

            string detail = telemetry.UserDisconnected ? "link closed by you"
                : telemetry.IsConnected
                ? "connected, receiving data"
                : (string.IsNullOrEmpty(telemetry.LastError) ? "connecting..." : telemetry.LastError + " (retrying)");

            if (message.Length > 0) detail = message;      // a message from a button press replaces the detail line
            statusText.text = Tag(c, "<b>" + badge + "</b>") + "   " + telemetry.Host + ":" + telemetry.Port +
                              "\n<size=60%>" + (message.Length > 0 ? detail : Tag(CubeTheme.Dim, detail)) + "</size>";
        }

        bool caret = (Time.unscaledTime % 1f) < 0.5f;
        if (ipText != null)
            ipText.text = ip.Length == 0 && editingPort ? Tag(CubeTheme.Dim, "IP address")
                        : ip + (!editingPort && caret ? "_" : " ");
        if (portText != null)
            portText.text = port + (editingPort && caret ? "_" : " ");

        if (ipBox != null) ipBox.color = editingPort ? CubeTheme.Inset : Color.Lerp(CubeTheme.Inset, CubeTheme.Info, 0.35f);
        if (portBox != null) portBox.color = editingPort ? Color.Lerp(CubeTheme.Inset, CubeTheme.Info, 0.35f) : CubeTheme.Inset;
    }
}
