using System.Collections.Generic;
using UnityEngine;

/// <summary>One thing currently wrong.</summary>
public class Warning
{
    public string id, title, detail;
    public bool danger;          // red (high spin) instead of amber
}

/// <summary>
/// Watches the telemetry and the satellite's spin and decides what to warn about. Creates itself; nothing to place.
///
///   - High spin: the angle the satellite turns per REAL second as the user sees it, averaged over about a second.
///   - Bad data: link lost, stale data, garbled or incomplete frames, implausible values, dropped or repeated frames.
///
/// Each kind has its own trigger and clear times (real seconds) and limits, all in Assets/Resources/WarningSettings.asset.
/// The popup (WarningHud) and the red sphere (SpinWarningSphere) read what is decided here.
/// Switched on and off in the menu (Options > Display > Warnings, the setting "display.warnings").
/// </summary>
[DefaultExecutionOrder(100)]
public class WarningMonitor : MonoBehaviour
{
    public const string EnabledKey = "display.warnings";

    public static WarningMonitor Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("WarningMonitor");
        DontDestroyOnLoad(go);
        go.AddComponent<WarningMonitor>();
    }

    // ── what the others read ─────────────────────────────────────────────────
    public WarningSettings Settings => WarningSettings.Instance;
    public IReadOnlyList<Warning> Active => active;
    /// <summary>Changes whenever the list or its text changes.</summary>
    public int Version { get; private set; }
    public float SpinDegPerSec { get; private set; }
    public bool SpinWarningActive => spinGate.active;
    /// <summary>0 to 1: how strongly the red sphere is shown (fades in and out).</summary>
    public float SphereVisibility { get; private set; }
    /// <summary>0 to 1: how dense the sphere is, rising with the spin between the warning level and the cover level.</summary>
    public float CoverAmount { get; private set; }
    /// <summary>True when the spin is so high that the satellite's axes should be hidden.</summary>
    public bool AxesCovered => spinGate.active && SpinDegPerSec >= Settings.axesCoverSpinDegPerSec;

    // ── internals ────────────────────────────────────────────────────────────
    class Gate
    {
        public bool active, cond;
        float since, clearSince;
        public void Tick(bool want, float now, WarningSettings.Timing t)
        {
            if (!t.enabled) { active = false; cond = false; return; }
            if (want)
            {
                if (!cond) { cond = true; since = now; }
                if (!active && now - since >= t.triggerSeconds) active = true;
            }
            else
            {
                if (cond) { cond = false; clearSince = now; }
                if (active && now - clearSince >= t.clearSeconds) active = false;
            }
        }
        public void Off() { active = false; cond = false; }
    }

    // counts something over the last few seconds
    class Window
    {
        readonly Queue<float> times = new Queue<float>();
        readonly Queue<int> totals = new Queue<int>();
        int lastTotal;
        float lastAdd = -1f;

        public int Delta(float now, float window, int total)
        {
            if (total < lastTotal) { times.Clear(); totals.Clear(); }            // the counters were reset (new run)
            lastTotal = total;
            if (times.Count == 0 || now - lastAdd >= 0.1f)
            {
                times.Enqueue(now); totals.Enqueue(total); lastAdd = now;
            }
            while (times.Count > 1 && now - times.Peek() > window) { times.Dequeue(); totals.Dequeue(); }
            return total - totals.Peek();
        }
        public void Clear() { times.Clear(); totals.Clear(); lastTotal = 0; }
    }

    readonly Gate linkGate = new Gate(), staleGate = new Gate(), invalidGate = new Gate(),
                  implausibleGate = new Gate(), droppedGate = new Gate(), spinGate = new Gate();
    readonly Window invalidWin = new Window(), droppedWin = new Window();
    readonly List<Warning> active = new List<Warning>();

    TelemetryClient tel;
    SpaceFrame frame;
    float nextFind, nextBuild;
    int lastMask = -1;

    Quaternion prevQ;
    bool havePrev;
    float spinSmoothed;

    string implausibleWhy = "";
    int invalidCount, droppedCount;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
    void OnEnable() { TelemetryClient.SessionReset += ResetAll; }
    void OnDisable() { TelemetryClient.SessionReset -= ResetAll; }

    /// <summary>A new link was made: clear every warning and every count, as if the app had just started.</summary>
    void ResetAll()
    {
        linkGate.Off(); staleGate.Off(); invalidGate.Off(); implausibleGate.Off(); droppedGate.Off(); spinGate.Off();
        invalidWin.Clear(); droppedWin.Clear();
        rates.Clear();
        spinSmoothed = 0f; SpinDegPerSec = 0f; havePrev = false;
        invalidCount = 0; droppedCount = 0;
        active.Clear(); Version++; lastMask = 0;
    }

    void Update()
    {
        var S = Settings;
        float now = Time.unscaledTime;

        if (now >= nextFind)
        {
            nextFind = now + 1f;
            if (tel == null) tel = FindAnyObjectByType<TelemetryClient>();
            if (frame == null) frame = FindAnyObjectByType<SpaceFrame>();
        }

        if (!CubeSettings.GetBool(EnabledKey, true))
        {
            linkGate.Off(); staleGate.Off(); invalidGate.Off(); implausibleGate.Off(); droppedGate.Off(); spinGate.Off();
            havePrev = false;
            FadeSphere(false, S);
            if (active.Count > 0) { active.Clear(); Version++; lastMask = 0; }
            return;
        }

        MeasureSpin(S);
        TickSpin(now, S);
        TickData(now, S);
        FadeSphere(spinGate.active, S);

        int mask = (linkGate.active ? 1 : 0) | (staleGate.active ? 2 : 0) | (invalidGate.active ? 4 : 0) |
                   (implausibleGate.active ? 8 : 0) | (droppedGate.active ? 16 : 0) | (spinGate.active ? 32 : 0);
        if (mask != lastMask || now >= nextBuild)
        {
            lastMask = mask;
            nextBuild = now + 0.2f;
            Build(S);
        }
    }

    // ── spin ─────────────────────────────────────────────────────────────────
    float sampleT, lastRawRate;
    System.DateTime prevUtc;
    readonly List<float> rates = new List<float>();

    void MeasureSpin(WarningSettings S)
    {
        if (frame == null || !frame.State.Valid || !frame.State.HasSatellite)
        {
            havePrev = false;
            spinSmoothed = Mathf.MoveTowards(spinSmoothed, 0f, 200f * Time.unscaledDeltaTime);
            SpinDegPerSec = spinSmoothed;
            return;
        }

        var st = frame.State;
        Quaternion q = st.SatRotation;
        if (S.spinRelativeToOrbit)
        {
            // Turn measured against the orbit's own frame (up = away from Earth, forward = along the track), so the slow
            // turn of a nadir-pointing satellite as it goes round the Earth does not count; only a real tumble does.
            Vector3 up = st.SatPosKm.normalized;
            Vector3 along = Vector3.ProjectOnPlane(st.SatVelKmS, up);
            if (up.sqrMagnitude > 0.5f && along.sqrMagnitude > 1e-8f)
                q = Quaternion.Inverse(Quaternion.LookRotation(along.normalized, up)) * q;
        }

        float now = Time.unscaledTime;
        if (!havePrev) { prevQ = q; sampleT = now; prevUtc = st.UtcTime; havePrev = true; SpinDegPerSec = spinSmoothed; return; }

        // Sample every few hundredths of a second, not every frame. The middle value (median) of the last few samples is used, so a single
        // jump in the data (the stub restarting its story, a garbled frame) is thrown away instead of counted as a spin,
        // while a real tumble, which is high in every sample, still comes straight through.
        float dt = now - sampleT;
        if (dt >= S.spinSampleSeconds)
        {
            if (dt < 0.5f)
            {
                // degrees per second of the clock chosen in the settings: the simulation's own time (so the stub's speed
                // setting makes no difference), or the real time you see on screen
                double secs = S.spinInSimTime ? (st.UtcTime - prevUtc).TotalSeconds : dt;
                if (secs > 1e-4) rates.Add((float)(Quaternion.Angle(prevQ, q) / secs));
                while (rates.Count > Mathf.Max(1, S.spinSamples)) rates.RemoveAt(0);
                if (rates.Count == 0) { prevQ = q; sampleT = now; prevUtc = st.UtcTime; SpinDegPerSec = spinSmoothed; return; }
                if (rates.Count < Mathf.Max(1, S.spinSamples)) { prevQ = q; sampleT = now; prevUtc = st.UtcTime; SpinDegPerSec = spinSmoothed; return; }
                var sorted = new List<float>(rates);
                sorted.Sort();
                float median = sorted[sorted.Count / 2];
                // Rises at once (so a tumble is caught before it can disorient), eases down slowly (so it does not flicker)
                if (median >= spinSmoothed) spinSmoothed = median;
                else spinSmoothed += (median - spinSmoothed) * (1f - Mathf.Exp(-dt / Mathf.Max(0.05f, S.spinWindowSeconds)));
                lastRawRate = rates[rates.Count - 1];
            }
            else rates.Clear();
            prevQ = q; sampleT = now; prevUtc = st.UtcTime;
        }
        SpinDegPerSec = spinSmoothed;
    }

    void TickSpin(float now, WarningSettings S)
    {
        // once it is on, it stays on until the spin has dropped below the lower line
        // on above the warning level; off again only once below it (the clear level can never be set under the warning level's fair share)
        bool was = spinGate.active;
        bool want = SpinDegPerSec > S.spinWarnDegPerSec;          // on above the limit, off at or below it
        spinGate.Tick(want, now, S.spin);
        if (spinGate.active && !was && frame != null)
            Debug.Log("[Warning] HIGH SPIN " + SpinDegPerSec.ToString("F1") + " deg/s (latest sample " + lastRawRate.ToString("F1") +
                      ") aocs=" + frame.State.AocsMode + " seq=" + frame.State.Seq);
    }

    void FadeSphere(bool on, WarningSettings S)
    {
        float dt = Time.unscaledDeltaTime;
        float target = on ? 1f : 0f;
        float dur = target > SphereVisibility ? S.sphereFadeInSeconds : S.sphereFadeOutSeconds;
        SphereVisibility = Mathf.MoveTowards(SphereVisibility, target, dt / Mathf.Max(0.01f, dur));

        float cover = 0f;
        if (on)
        {
            float t = Mathf.InverseLerp(S.spinWarnDegPerSec, Mathf.Max(S.spinWarnDegPerSec + 0.1f, S.axesCoverSpinDegPerSec), SpinDegPerSec);
            cover = t * t * (3f - 2f * t);
        }
        CoverAmount = Mathf.MoveTowards(CoverAmount, cover, dt * 2f);
    }

    // ── bad data ─────────────────────────────────────────────────────────────
    void TickData(float now, WarningSettings S)
    {
        if (tel == null)
        {
            linkGate.Off(); staleGate.Off(); invalidGate.Off(); implausibleGate.Off(); droppedGate.Off();
            return;
        }

        bool ever = tel.FramesReceived > 0;           // no warnings about a link that was never up
        linkGate.Tick(ever && !tel.IsConnected && !tel.UserDisconnected, now, S.noLink);
        staleGate.Tick(ever && tel.IsConnected && tel.SecondsSinceLastFrame > S.staleAfterSeconds, now, S.stale);

        var st = tel.Stats;
        invalidCount = invalidWin.Delta(now, S.invalidWindowSeconds, st.ParseErrors + Mathf.Max(0, st.Parsed - st.AllOk));
        invalidGate.Tick(invalidCount >= S.invalidCountLimit, now, S.invalid);

        droppedCount = droppedWin.Delta(now, S.droppedWindowSeconds, st.Dropped + st.Duplicates);
        droppedGate.Tick(droppedCount >= S.droppedCountLimit || st.FramesBehindNow >= S.framesBehindLimit, now, S.dropped);

        implausibleWhy = Implausible(tel.Latest, S);
        implausibleGate.Tick(ever && tel.IsConnected && implausibleWhy.Length > 0, now, S.implausible);
    }

    static string Implausible(StateFrame f, WarningSettings S)
    {
        if (f == null) return "";
        if (f.orbit != null)
        {
            if (f.orbit.alt_km < S.altitudeMinKm || f.orbit.alt_km > S.altitudeMaxKm)
                return "altitude " + f.orbit.alt_km.ToString("F0") + " km";
            var v = f.orbit.vel_eci_km_s;
            if (v != null && v.Length == 3)
            {
                double sp = System.Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
                if (sp < S.speedMinKmS || sp > S.speedMaxKmS) return "speed " + sp.ToString("F2") + " km/s";
            }
        }
        if (f.attitude != null && f.attitude.q_body_to_eci != null && f.attitude.q_body_to_eci.Length == 4)
        {
            var q = f.attitude.q_body_to_eci;
            double n = System.Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            if (System.Math.Abs(n - 1.0) > S.quaternionTolerance) return "attitude length " + n.ToString("F2");
        }
        return "";
    }

    // ── the list ─────────────────────────────────────────────────────────────
    void Build(WarningSettings S)
    {
        active.Clear();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        if (spinGate.active)
            Add("spin", "HIGH SPIN", SpinDegPerSec.ToString("F1", inv) + " deg/s  (limit " + S.spinWarnDegPerSec.ToString("0.#", inv) + ")", true);
        if (linkGate.active)
            Add("link", "LINK LOST", string.IsNullOrEmpty(tel != null ? tel.LastError : null) ? "reconnecting..." : tel.LastError, false);
        if (staleGate.active)
            Add("stale", "STALE DATA", "no new frame for " + (tel != null ? tel.SecondsSinceLastFrame.ToString("F1", inv) : "?") + " s", false);
        if (invalidGate.active)
            Add("invalid", "BAD DATA", invalidCount + " garbled or incomplete frames in " + S.invalidWindowSeconds.ToString("0", inv) + " s", false);
        if (implausibleGate.active)
            Add("implausible", "IMPLAUSIBLE DATA", implausibleWhy, false);
        if (droppedGate.active)
            Add("dropped", "FRAMES LOST",
                tel != null && tel.Stats.FramesBehindNow >= S.framesBehindLimit
                    ? tel.Stats.FramesBehindNow + " frames behind"
                    : droppedCount + " dropped or repeated in " + S.droppedWindowSeconds.ToString("0", inv) + " s", false);

        Version++;
    }

    void Add(string id, string title, string detail, bool danger) =>
        active.Add(new Warning { id = id, title = title, detail = detail, danger = danger });
}
