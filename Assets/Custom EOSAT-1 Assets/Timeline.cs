using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records every frame of telemetry that arrives, so the player can go back and look at what happened, like a video.
///
///   LIVE      the app shows the newest frame as it arrives (the recording carries on quietly behind the scenes).
///   REPLAY    the app shows a recorded frame instead. Play, pause, fast-forward, rewind and dragging the bar all work by
///             moving a playback position through the recording. Reaching the front puts you back to live.
///
/// TelemetryClient hands every frame to Record() and, while replaying, returns ReplayFrame as "Latest", so the rest of the
/// app (globe, map, space view, panels) needs no special handling. Time on the bar is real seconds since the recording began.
/// Markers are the little ticks on the bar (warnings, bad data, mode changes).
///
/// Warnings are switched off while you scrub or play faster/slower than normal (SuppressWarnings), because you are moving
/// time on purpose. Everything is cleared when a new link is made (Connect).
///
/// Starts itself; nothing to place in a scene.
/// </summary>
public class Timeline : MonoBehaviour
{
    public enum MarkerKind { Warning, Data, Event }
    public struct Marker { public float t; public MarkerKind kind; public string text; }

    class Rec { public float t; public StateFrame f; public double sim; }

    [Tooltip("Most frames kept. The oldest are dropped past this. At 10 frames a second, 60000 is about 100 minutes.")]
    [SerializeField] int maxFrames = 60000;
    [Tooltip("Dragging the bar this close to the front (in seconds) goes back to live.")]
    [SerializeField] float snapToLiveSeconds = 1.0f;

    // ── what the rest of the app reads ───────────────────────────────────────
    static Timeline inst;
    static bool replaying;
    static Timeline I
    {
        get
        {
            if (inst == null)
            {
                var go = new GameObject("Timeline");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<Timeline>();
            }
            return inst;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { var _ = I; }

    /// <summary>True while a recorded frame is on show instead of the live one.</summary>
    public static bool Replaying => replaying;
    public static bool Live => !replaying;
    /// <summary>The recorded frame to show while replaying.</summary>
    public static StateFrame ReplayFrame => inst != null ? inst.frame : null;
    /// <summary>Changes whenever the replayed frame changes.</summary>
    public static int Serial => inst != null ? inst.serial : 0;
    /// <summary>Playback speed: 1 = normal, 0 = paused, negative = backwards. Live reads as 1.</summary>
    public static float Rate => inst != null && replaying ? inst.rate : 1f;
    public static bool Scrubbing => inst != null && inst.scrubbing;
    /// <summary>True while time is being moved on purpose (dragging, or any speed other than normal), so warnings stay quiet.</summary>
    public static bool SuppressWarnings => replaying && inst != null &&
        (inst.scrubbing || Mathf.Abs(inst.rate - 1f) > 0.001f || Time.unscaledTime - inst.lastSeekTime < 1.5f);

    public static int Count => inst != null ? inst.recs.Count : 0;
    public static float Start => inst != null && inst.recs.Count > 0 ? inst.recs[0].t : 0f;
    public static float End => inst != null && inst.recs.Count > 0 ? inst.recs[inst.recs.Count - 1].t : 0f;
    /// <summary>Where the playhead is, in seconds of the recording (the end when live).</summary>
    public static float Position => inst != null && replaying ? inst.pos : End;
    /// <summary>0 = start of the recording, 1 = the live front.</summary>
    public static float Fraction => End - Start > 0.01f ? Mathf.Clamp01((Position - Start) / (End - Start)) : 1f;

    public static IReadOnlyList<Marker> Markers => I.markers;
    public static int MarkersVersion => inst != null ? inst.markersVersion : 0;

    /// <summary>The simulation time (seconds since 1 Jan year 1) of the frame on show.</summary>
    public static double CurrentSim
    {
        get
        {
            if (inst == null || inst.recs.Count == 0) return 0;
            int i = CursorIndex;
            return i >= 0 ? inst.recs[i].sim : 0;
        }
    }

    /// <summary>Index of the frame on show (the newest one when live).</summary>
    public static int CursorIndex
    {
        get
        {
            if (inst == null || inst.recs.Count == 0) return -1;
            return replaying ? inst.IndexAt(inst.pos) : inst.recs.Count - 1;
        }
    }
    /// <summary>
    /// While replaying: the recorded frame the playhead is in (i) and how far it is toward the next one (u, 0..1). The playhead
    /// moves every rendered frame, but recorded frames are only about 10 a second, so smooth motion has to blend between them.
    /// </summary>
    public static bool Bracket(out int i, out float u)
    {
        i = 0; u = 0f;
        if (inst == null || !replaying || inst.recs.Count == 0) return false;
        i = inst.IndexAt(inst.pos);
        if (i + 1 >= inst.recs.Count) return true;
        float t0 = inst.recs[i].t, t1 = inst.recs[i + 1].t;
        u = t1 > t0 ? Mathf.Clamp01((inst.pos - t0) / (t1 - t0)) : 0f;
        return true;
    }

    public static StateFrame FrameAt(int i) => inst != null && i >= 0 && i < inst.recs.Count ? inst.recs[i].f : null;
    public static double SimAt(int i) => inst != null && i >= 0 && i < inst.recs.Count ? inst.recs[i].sim : 0;

    /// <summary>Goes up every time time is moved on purpose (seek, live, play/pause, speed change): smoothing should start fresh.</summary>
    public static int Jumps => inst != null ? inst.jumps : 0;
    /// <summary>A key frame is kept after the satellite has moved this far (km). SpaceFrame sets it to its history step.</summary>
    public static float TrailStepKm = 40f;

    /// <summary>List indexes (into FrameAt) of the thinned path points up to the frame <paramref name="cursor"/>, newest last, at most <paramref name="max"/>.</summary>
    public static void CollectTrail(int cursor, int max, List<int> output)
    {
        output.Clear();
        if (inst == null) return;
        inst.CollectKeys(cursor, max, output);
    }

    /// <summary>Raised (a little after) whenever time was moved to somewhere else: the trails rebuild themselves from the recording.</summary>
    public static event Action Seeked;

    // ── state ────────────────────────────────────────────────────────────────
    readonly List<Rec> recs = new List<Rec>();
    readonly List<Marker> markers = new List<Marker>();
    int markersVersion;
    float origin; bool haveOrigin;
    float interval = 0.1f;
    int jumps;
    float lastShow = -10f;
    readonly List<int> keys = new List<int>();     // absolute frame numbers of the thinned path points
    int baseIdx;                                   // absolute number of recs[0]
    Vector3 lastKeyPos;

    void CollectKeys(int cursor, int max, List<int> output)
    {
        int cursorAbs = baseIdx + cursor;
        int lo = 0, hi = keys.Count - 1, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid] <= cursorAbs) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        if (best < 0) return;
        for (int k = Mathf.Max(0, best - max + 1); k <= best; k++) output.Add(keys[k] - baseIdx);
    }
    float pos;
    float rate = 1f;
    bool scrubbing;
    float lastSeekTime = -100f;
    StateFrame frame;
    int serial;
    float shownT = -1f;
    bool seekPending; float nextSeekEvent;
    string lastAocs, lastPhase;

    // ── recording ────────────────────────────────────────────────────────────
    public static void Record(StateFrame f) { if (f != null) I.RecordFrame(f); }

    void RecordFrame(StateFrame f)
    {
        if (!haveOrigin) { origin = Time.unscaledTime; haveOrigin = true; }
        float arrival = Time.unscaledTime - origin;
        float t = arrival;
        if (recs.Count > 0)
        {
            // Packets arrive unevenly (bunched, then a gap). Replaying those exact times would stutter, so the recorded
            // times are evened out: they follow the average spacing and are pulled gently toward the real arrival times.
            float prev = recs[recs.Count - 1].t;
            float dt = arrival - prev;
            if (dt > 0.001f && dt < 1f) interval += (dt - interval) * 0.05f;
            t = prev + interval;
            float err = arrival - t;
            if (Mathf.Abs(err) > 0.5f) t = arrival;               // a real gap (stub paused, link dropped)
            else t += err * 0.1f;
            if (t <= prev) t = prev + 0.001f;
        }
        recs.Add(new Rec { t = t, f = f, sim = SimSeconds(f) });
        if (f.orbit != null && f.orbit.pos_eci_km != null && f.orbit.pos_eci_km.Length >= 3)
        {
            var kp = new Vector3((float)f.orbit.pos_eci_km[0], (float)f.orbit.pos_eci_km[1], (float)f.orbit.pos_eci_km[2]);
            if (keys.Count == 0 || (kp - lastKeyPos).magnitude >= TrailStepKm)
            {
                keys.Add(baseIdx + recs.Count - 1);
                lastKeyPos = kp;
            }
        }

        string a = f.attitude != null && f.attitude.aocs_mode != null ? f.attitude.aocs_mode : "";
        string ph = f.mission != null && f.mission.phase != null ? f.mission.phase : "";
        if (lastAocs != null && a != lastAocs) AddMarker(t, MarkerKind.Event, "AOCS " + lastAocs + " > " + a);
        if (lastPhase != null && ph != lastPhase) AddMarker(t, MarkerKind.Event, "Phase " + lastPhase + " > " + ph);
        lastAocs = a; lastPhase = ph;

        if (recs.Count > maxFrames + 2000)
        {
            recs.RemoveRange(0, 2000);
            baseIdx += 2000;
            int dropKeys = 0;
            while (dropKeys < keys.Count && keys[dropKeys] < baseIdx) dropKeys++;
            if (dropKeys > 0) keys.RemoveRange(0, dropKeys);
            float first = recs[0].t;
            int drop = 0;
            while (drop < markers.Count && markers[drop].t < first) drop++;
            if (drop > 0) { markers.RemoveRange(0, drop); markersVersion++; }
            if (replaying && pos < first) pos = first;
        }
    }

    static double SimSeconds(StateFrame f)
    {
        if (!string.IsNullOrEmpty(f.sim_time) &&
            DateTime.TryParse(f.sim_time, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime dt))
            return dt.Ticks / (double)TimeSpan.TicksPerSecond;
        return f.met_s;
    }

    /// <summary>Put a tick on the bar at the newest recorded moment (a warning that just engaged, say).</summary>
    public static void Mark(MarkerKind kind, string text)
    {
        var t = I;
        if (t.recs.Count == 0) return;
        t.AddMarker(t.recs[t.recs.Count - 1].t, kind, text);
    }

    void AddMarker(float t, MarkerKind kind, string text)
    {
        for (int i = markers.Count - 1; i >= 0 && t - markers[i].t < 1.5f; i--)
            if (markers[i].text == text) return;                     // the same thing a moment ago
        markers.Add(new Marker { t = t, kind = kind, text = text });
        markersVersion++;
    }

    /// <summary>Forget the whole recording (a new link was made).</summary>
    public static void Clear() { if (inst != null) inst.ClearAll(); }

    void ClearAll()
    {
        recs.Clear(); markers.Clear(); keys.Clear(); baseIdx = 0; jumps++; markersVersion++;
        haveOrigin = false; interval = 0.1f; replaying = false; rate = 1f; scrubbing = false;
        frame = null; serial++; shownT = -1f;
        lastAocs = lastPhase = null;
    }

    // ── controls ─────────────────────────────────────────────────────────────
    public static void TogglePlay() => I.DoTogglePlay();
    public static void Faster() => I.DoFaster();
    public static void Rewind() => I.DoRewind();
    public static void GoLive() => I.DoGoLive();
    public static void BeginScrub() => I.DoBeginScrub();
    public static void EndScrub() => I.DoEndScrub();
    /// <summary>Move the playhead to a fraction of the bar (0 = start, 1 = live).</summary>
    public static void SeekFraction(float f) => I.DoSeek(Mathf.Lerp(Start, End, Mathf.Clamp01(f)));

    void EnterReplay(float at)
    {
        replaying = true; jumps++;
        pos = Mathf.Clamp(at, Start, End);
        ShowAt(pos, true);
    }

    void DoTogglePlay()
    {
        if (recs.Count == 0) return;
        jumps++;
        if (!replaying) { EnterReplay(End); rate = 0f; return; }            // pause where we are
        rate = Mathf.Abs(rate) < 0.001f ? 1f : 0f;
    }

    void DoFaster()
    {
        if (!replaying) return;                                             // already at the front
        jumps++;
        rate = rate < 1f ? 2f : Mathf.Min(rate + 1f, 16f);          // 2x, 3x, 4x ... 16x
    }

    void DoRewind()
    {
        if (recs.Count == 0) return;
        jumps++;
        if (!replaying) EnterReplay(End);
        rate = rate > -2f ? -2f : Mathf.Max(rate - 1f, -16f);       // -2x, -3x, -4x ... -16x
    }

    void DoGoLive()
    {
        if (!replaying) return;
        replaying = false; rate = 1f; scrubbing = false; jumps++;
        frame = null; serial++;
        lastSeekTime = Time.unscaledTime; seekPending = true; nextSeekEvent = 0f;
    }

    void DoBeginScrub()
    {
        if (recs.Count == 0) return;
        scrubbing = true;
        if (!replaying) { EnterReplay(End); rate = 1f; }
    }

    void DoEndScrub()
    {
        scrubbing = false;
        if (replaying && End - pos <= snapToLiveSeconds) DoGoLive();
    }

    void DoSeek(float t)
    {
        if (recs.Count == 0) return;
        if (!replaying) { replaying = true; rate = 1f; }
        pos = Mathf.Clamp(t, Start, End);
        lastSeekTime = Time.unscaledTime;
        seekPending = true; jumps++;
    }

    // ── playback ─────────────────────────────────────────────────────────────
    int IndexAt(float t)
    {
        int lo = 0, hi = recs.Count - 1, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (recs[mid].t <= t) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return best;
    }

    void ShowAt(float t, bool force = false)
    {
        if (recs.Count == 0) return;
        var r = recs[IndexAt(t)];
        if (ReferenceEquals(r.f, frame)) return;
        // Show a new frame at most about 12 times a second: every panel and trail redraws per frame, and doing that at the
        // headset's 72 Hz while dragging or fast-forwarding is what made replay laggy.
        if (!force && Time.unscaledTime - lastShow < 0.08f) return;
        lastShow = Time.unscaledTime;
        if (r.t < shownT - 1e-4f) seekPending = true;                       // went backwards: the trails must shrink
        shownT = r.t;
        frame = r.f;
        serial++;
    }

    void Update()
    {
        if (replaying && recs.Count > 0)
        {
            if (!scrubbing && Mathf.Abs(rate) > 0.001f)
            {
                pos += Time.unscaledDeltaTime * rate;
                if (rate > 0f && pos >= End) { pos = End; DoGoLive(); }
                else if (pos <= Start) { pos = Start; rate = 0f; }
            }
            if (replaying) ShowAt(pos);
        }

        if (seekPending && Time.unscaledTime >= nextSeekEvent)
        {
            seekPending = false;
            nextSeekEvent = Time.unscaledTime + (scrubbing ? 0.12f : 0.75f);     // playing back or rewinding: redraw the trails less often (each redraw is a small hitch)
            Seeked?.Invoke();
        }
    }

    void OnDestroy() { if (inst == this) { inst = null; replaying = false; } }
}
