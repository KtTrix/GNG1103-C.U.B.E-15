using System;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Fills the "Position" panel with live numbers, in the style of the client's console pages:
/// small grey labels on the left, values on the right, plus a LIVE / STALE / NO LINK badge.
///
/// Put this on the panel's Canvas (or any object), then assign:
///   Telemetry   - your Telemetry object
///   Values Text - one TextMeshPro text that will hold all the rows
///   Link Text   - a small TextMeshPro text for the status badge (optional)
///
/// The two-column layout uses TextMeshPro's &lt;pos=..%&gt; tag, so ONE text object is enough.
/// Keep the Values Text box wide enough that no row wraps, or the columns will slip.
/// </summary>
public class TelemetryPanel : MonoBehaviour
{
    [SerializeField] TelemetryClient telemetry;
    [SerializeField] TMP_Text valuesText;
    [SerializeField] TMP_Text linkText;

    [Tooltip("The client's brief calls data 'fresh' when the newest frame is under 2 s old.")]
    [SerializeField] float staleAfterSeconds = 2f;
    [Tooltip("Where the value column starts, as a percentage of the text box width.")]
    [Range(15f, 60f)]
    [SerializeField] float valueColumnPercent = 34f;

    [Header("Colours")]
    [SerializeField] Color labelColor   = new Color(0.50f, 0.58f, 0.66f);
    [SerializeField] Color sunlitColor  = new Color(0.85f, 0.65f, 0.18f);
    [SerializeField] Color eclipseColor = new Color(0.31f, 0.55f, 0.95f);
    [SerializeField] Color okColor      = new Color(0.37f, 0.88f, 0.54f);
    [SerializeField] Color warnColor    = new Color(0.91f, 0.64f, 0.24f);
    [SerializeField] Color badColor     = new Color(1.00f, 0.36f, 0.36f);

    enum Link { Unknown, NoLink, Waiting, Stale, Live }

    readonly StringBuilder sb = new StringBuilder(512);
    Link lastLink = Link.Unknown;
    int lastFrame = -1;
    string labelHex, sunlitHex, eclipseHex, okHex;

    void Awake()
    {
        if (telemetry == null) telemetry = FindAnyObjectByType<TelemetryClient>();
        labelHex = Hex(labelColor);
        sunlitHex = Hex(sunlitColor);
        eclipseHex = Hex(eclipseColor);
        okHex = Hex(okColor);
    }

    void Start()
    {
        if (valuesText != null) valuesText.text = "Waiting for data...";
    }

    void Update()
    {
        if (telemetry == null) return;

        UpdateLink();

        if (telemetry.Latest != null && telemetry.FramesReceived != lastFrame)
        {
            lastFrame = telemetry.FramesReceived;
            Rebuild(telemetry.Latest);
        }
    }

    // ── status badge ─────────────────────────────────────────────────────────
    void UpdateLink()
    {
        Link now;
        if (!telemetry.IsConnected) now = Link.NoLink;
        else if (telemetry.FramesReceived == 0) now = Link.Waiting;
        else if (telemetry.SecondsSinceLastFrame > staleAfterSeconds) now = Link.Stale;
        else now = Link.Live;

        if (now == lastLink) return;   // only touch the text when something changed
        lastLink = now;

        if (linkText != null)
        {
            switch (now)
            {
                case Link.Live:    linkText.text = "LIVE";    linkText.color = okColor;   break;
                case Link.Stale:   linkText.text = "STALE";   linkText.color = warnColor; break;
                case Link.Waiting: linkText.text = "WAITING"; linkText.color = warnColor; break;
                default:           linkText.text = "NO LINK"; linkText.color = badColor;  break;
            }
        }

        // Dim the numbers when they are no longer trustworthy, so old data can't pass for live data.
        if (valuesText != null)
            valuesText.alpha = (now == Link.Live || now == Link.Waiting) ? 1f : 0.4f;
    }

    // ── the numbers ──────────────────────────────────────────────────────────
    void Rebuild(StateFrame f)
    {
        sb.Clear();
        OrbitData o = f.orbit;

        Row("UTC", FormatUtc(f.sim_time));
        Row("MET", FormatMet(f.met_s));
        Row("LAT", Hemisphere(o.lat_deg, 'N', 'S'));
        Row("LON", Hemisphere(o.lon_deg, 'E', 'W'));
        Row("ALT", Num(o.alt_km, "F1") + " km");

        if (o.vel_eci_km_s != null && o.vel_eci_km_s.Length == 3)
        {
            double[] v = o.vel_eci_km_s;
            double speed = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            Row("SPEED", Num(speed, "F2") + " km/s");
        }

        double[] p = o.pos_eci_km;
        Row("ECI X", Num(p[0], "F1") + " km");
        Row("ECI Y", Num(p[1], "F1") + " km");
        Row("ECI Z", Num(p[2], "F1") + " km");

        bool eclipse = f.sun != null && f.sun.in_eclipse;
        Row("LIGHT", eclipse
            ? "<color=#" + eclipseHex + ">ECLIPSE</color>"
            : "<color=#" + sunlitHex + ">SUNLIT</color>");

        string contact = (f.mission != null && f.mission.in_contact && !string.IsNullOrEmpty(f.mission.ground_station))
            ? "<color=#" + okHex + ">" + f.mission.ground_station + "</color>"
            : "<color=#" + labelHex + ">none</color>";
        Row("CONTACT", contact);

        if (f.mission != null && !string.IsNullOrEmpty(f.mission.phase))
            Row("PHASE", f.mission.phase);

        if (valuesText != null) valuesText.SetText(sb);
    }

    void Row(string label, string value)
    {
        sb.Append("<color=#").Append(labelHex).Append('>').Append(label).Append("</color>")
          .Append("<pos=").Append(valueColumnPercent.ToString("F0", CultureInfo.InvariantCulture)).Append("%>")
          .Append(value).Append('\n');
    }

    // ── formatting helpers (InvariantCulture so a French-Canadian PC doesn't print "66,83") ──
    static string Num(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

    static string Hemisphere(double deg, char positive, char negative) =>
        Num(Math.Abs(deg), "F2") + "° " + (deg >= 0 ? positive : negative);

    static string FormatUtc(string simTime)
    {
        if (string.IsNullOrEmpty(simTime)) return "--";
        string t = simTime.Replace('T', ' ');
        int dot = t.IndexOf('.');
        if (dot > 0) t = t.Substring(0, dot) + "Z";   // drop the milliseconds
        return t;
    }

    static string FormatMet(double seconds)
    {
        if (seconds <= 0) return "--";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Days.ToString("000") + "/" + ts.Hours.ToString("00") + ":" +
               ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00");
    }

    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}