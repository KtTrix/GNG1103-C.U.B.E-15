using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  Data model for the smo-vr/1 "state" frames.
//  Field names MUST match the JSON keys exactly (that's how JsonUtility works).
//  Any JSON key not listed here is ignored, which is what the interface
//  spec requires ("clients must ignore unknown fields").
// ─────────────────────────────────────────────────────────────────────────────
[Serializable] public class OrbitData
{
    public double[] pos_eci_km;      // [x, y, z] km, Earth-centred inertial
    public double[] vel_eci_km_s;
    public double lat_deg;
    public double lon_deg;
    public double alt_km;
}

[Serializable] public class AttitudeData
{
    public double[] q_body_to_eci;   // [x, y, z, w], scalar LAST (used in the next step)
    public string aocs_mode;
}

[Serializable] public class EarthData
{
    public double gmst_deg;          // how far Earth has rotated relative to ECI
    public double radius_km;
}

[Serializable] public class SunData
{
    public double[] dir_eci;         // unit vector from Earth towards the Sun, in ECI
    public bool in_eclipse;          // true = spacecraft is in Earth's shadow
}

[Serializable] public class MissionData
{
    public string phase;             // e.g. "NOMINAL"
    public bool in_contact;          // a ground station can see the spacecraft
    public string ground_station;    // name of that station, or ""
}

[Serializable] public class StateFrame
{
    public string type;              // "hello" or "state"
    public int seq;
    public string sim_time;
    public OrbitData orbit;
    public AttitudeData attitude;
    public EarthData earth;
    public SunData sun;
    public MissionData mission;
    public double met_s;             // mission elapsed time in seconds (0 if the sender omits it)
    public double speed;             // simulated seconds per real second (the stub's --speed)
}

/// <summary>
/// Connects to the VR stub (or the real simulator) over TCP, reads one JSON
/// frame per line on a background thread, and parses every line on the main thread,
/// counting what it parsed (see Stats). Reconnects automatically.
///
/// Put this on an empty GameObject (e.g. "Telemetry" under Hub).
/// </summary>
public class TelemetryClient : MonoBehaviour
{
    [Header("Connection")]
    [Tooltip("127.0.0.1 when the stub runs on this PC and you're in the editor/Quest Link, " +
             "or when using 'adb reverse'. Otherwise the PC's LAN IP address.")]
    [SerializeField] string host = "127.0.0.1";
    [SerializeField] int port = 8100;
    [SerializeField] float reconnectDelaySeconds = 2f;
    [Tooltip("If an address was saved from the Connection panel in the headset, use it instead of the Host above.")]
    [SerializeField] bool useSavedHost = true;

    const string SavedHostKey = "telemetry.host";

    [Header("Reading")]
    [Tooltip("Parse EVERY line that arrives (needed for the parse test). Untick to keep only the newest and skip the rest.")]
    [SerializeField] bool processEveryFrame = true;
    [Tooltip("Safety limit on lines waiting to be parsed. If it is reached the oldest are dropped (and counted).")]
    [SerializeField] int maxQueuedLines = 2000;
    [Tooltip("Most lines parsed in one frame, so a burst can't freeze the headset.")]
    [SerializeField] int maxLinesPerUpdate = 300;
    [Header("Test mode")]
    [Tooltip("Master switch for testing. OFF (normal use): no report or log files are written. " +
             "ON: the options below take effect. Can also be switched in the headset from the Stats panel.")]
    [SerializeField] bool testMode = false;
    [Tooltip("Test mode only: save a report file when the stub disconnects after sending frames.")]
    [SerializeField] bool saveReportOnDisconnect = true;
    [Tooltip("Test mode only: write one line per second (speed, frames behind, lag) to a CSV file.")]
    [SerializeField] bool writeLagLog = true;

    [Header("Status reply and lag test")]
    [Tooltip("Send the stub a small status line (with the number of the newest frame shown). The stub uses it to print how many frames this app is behind.")]
    [SerializeField] bool sendStatusReplies = true;
    [Tooltip("Shown in the stub's link table.")]
    [SerializeField] string teamName = "CUBE-15";
    [Tooltip("At most this many replies per second.")]
    [SerializeField] float maxRepliesPerSecond = 20f;
    [Tooltip("Send a reply at least this often even if no new frame arrived (the stub calls a client silent after 5 s).")]
    [SerializeField] float heartbeatSeconds = 1f;
    [Tooltip("Below this many frames per second the FRAMERATE_OK bit is cleared.")]
    [SerializeField] float minFpsForOk = 55f;

    [Header("Debug")]
    [Tooltip("Log lat/lon/alt every N frames so you can compare with the globe. 0 = off.")]
    [SerializeField] int logEveryNFrames = 0;

    /// <summary>Newest valid state frame, or null if none has arrived yet.</summary>
    public StateFrame Latest { get; private set; }
    /// <summary>Count of valid frames received (changes whenever Latest changes).</summary>
    public int FramesReceived { get; private set; }
    /// <summary>Lines that were not valid JSON.</summary>
    public int BadLines => Stats.ParseErrors;
    /// <summary>All the parse-test counters.</summary>
    public TelemetryStats Stats { get; } = new TelemetryStats();
    public bool IsConnected => connected;
    /// <summary>The address currently in use.</summary>
    public string Host => host;
    public int Port => port;
    /// <summary>Why the last connection attempt failed (empty while connected).</summary>
    public string LastError => lastError;
    /// <summary>Seconds since the last valid frame (useful later for a "data fresh" indicator).</summary>
    public float SecondsSinceLastFrame => FramesReceived == 0 ? float.PositiveInfinity
                                                              : Time.unscaledTime - lastFrameTime;

    /// <summary>Turn the test files on or off while running. Off means no report or CSV files are written.</summary>
    public bool TestMode
    {
        get => testMode;
        set
        {
            testMode = value;
            if (!testMode) CloseLagLog();
        }
    }

    /// <summary>Seq number of the newest frame that was shown.</summary>
    public int AppliedSeq => lastAppliedSeq;
    /// <summary>Frames that have arrived but are not shown yet.</summary>
    public int FramesBehind =>
        (Stats.MaxSeqSeen >= 0 && lastAppliedSeq > 0) ? Math.Max(0, Stats.MaxSeqSeen - lastAppliedSeq) : 0;
    /// <summary>Where the lag CSV is being written (empty if not).</summary>
    public string LagLogPath => lagPath;

    volatile NetworkStream activeStream;
    readonly object sendLock = new object();
    int lastAppliedSeq, lastAckedSeq = -1;
    float lastReplyTime = -1000f;
    float smoothedFps = 60f;
    bool lastLineGood = true;
    float nextRateTime; int prevLines; float rateHz;
    StreamWriter lagWriter; string lagPath = ""; float lagStart, nextLagRow;

    Thread worker;
    volatile bool running;
    volatile bool connected;
    volatile string lastError = "";
    volatile TcpClient activeClient;
    int generation;                  // bumped on every (re)start so an old thread knows to quit

    readonly object gate = new object();
    readonly Queue<string> queue = new Queue<string>();   // lines waiting for the main thread
    int lastSavedParsed;
    float lastFrameTime;
    bool wasConnected;

    void Awake()
    {
        if (useSavedHost && PlayerPrefs.HasKey(SavedHostKey))
            host = PlayerPrefs.GetString(SavedHostKey, host);
    }

    void OnEnable() => StartWorker();
    void OnDisable() { StopWorker(); CloseLagLog(); }

    /// <summary>
    /// Switch to another address while the app is running (used by the Connection panel).
    /// With save = true the address is remembered for next time the app starts.
    /// </summary>
    public void SetEndpoint(string newHost, int newPort, bool save = true)
    {
        StopWorker();
        host = newHost;
        port = newPort;
        if (save)
        {
            PlayerPrefs.SetString(SavedHostKey, newHost);
            PlayerPrefs.Save();
        }
        lastError = "";
        lock (gate) queue.Clear();
        if (isActiveAndEnabled) StartWorker();
    }

    /// <summary>Zero all the parse-test counters and start counting again.</summary>
    public void ResetStats()
    {
        lock (gate)
        {
            Stats.Reset();
            queue.Clear();
        }
        lastSavedParsed = 0;
        lastAppliedSeq = 0;
        lastAckedSeq = -1;
        CloseLagLog();
    }

    /// <summary>Write the test report to a text file and return its full path.</summary>
    public string SaveReport()
    {
        string name = "telemetry_report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
        string path = Path.Combine(Application.persistentDataPath, name);
        try
        {
            File.WriteAllText(path, Stats.ReportText(host + ":" + port));
            lastSavedParsed = Stats.Parsed;
            Debug.Log("[Telemetry] report saved to " + path + "\n" + Stats.ReportText(host + ":" + port));
            return path;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Telemetry] could not save report: " + e.Message);
            return "";
        }
    }

    void StartWorker()
    {
        running = true;
        int gen = Interlocked.Increment(ref generation);
        string h = host; int p = port; float delay = reconnectDelaySeconds; // copy for the thread
        bool every = processEveryFrame; int maxQ = Mathf.Max(1, maxQueuedLines);
        worker = new Thread(() => RunLoop(h, p, delay, gen, every, maxQ)) { IsBackground = true, Name = "TelemetryClient" };
        worker.Start();
    }

    void StopWorker()
    {
        running = false;
        Interlocked.Increment(ref generation);   // any old thread now sees it is out of date
        try { activeClient?.Close(); } catch { /* already closed */ }
        worker?.Join(500);
        worker = null;
        connected = false;
        activeStream = null;
    }

    // ── Background thread: never touch Unity objects in here ─────────────────
    void RunLoop(string h, int p, float retryDelay, int gen, bool every, int maxQ)
    {
        bool Alive() => running && gen == Volatile.Read(ref generation);

        while (Alive())
        {
            try
            {
                using (var client = new TcpClient())
                {
                    activeClient = client;
                    client.NoDelay = true;

                    var connect = client.ConnectAsync(h, p);
                    if (!connect.Wait(3000)) throw new TimeoutException("connect timed out");
                    if (!Alive()) break;

                    connected = true;
                    lastError = "";
                    client.SendTimeout = 200;                     // a stuck reply can't freeze the app for long
                    var stream = client.GetStream();
                    activeStream = stream;
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    {
                        string line;
                        while (Alive() && (line = reader.ReadLine()) != null)
                        {
                            if (line.Length == 0) continue;
                            Enqueue(line, every, maxQ, gen);
                        }
                    }
                    if (Alive()) lastError = "server closed the connection";
                }
            }
            catch (Exception e)
            {
                if (Alive()) lastError = e.InnerException != null ? e.InnerException.Message : e.Message;
            }

            activeStream = null;
            if (Alive()) connected = false;
            for (float t = 0f; Alive() && t < retryDelay; t += 0.1f) Thread.Sleep(100);
        }
    }

    void Enqueue(string line, bool every, int maxQ, int gen)
    {
        bool hasSeq = TelemetryStats.TryGetSeq(line, out int seq);
        lock (gate)
        {
            if (gen != Volatile.Read(ref generation)) return;   // an old connection: ignore
            Stats.LinesReceived++;
            if (hasSeq) Stats.NoteSeqSeen(seq);                  // counted even if the line turns out garbled
            queue.Enqueue(line);

            int limit = every ? maxQ : 1;
            while (queue.Count > limit) { queue.Dequeue(); Stats.Dropped++; }
        }
    }

    // ── Main thread ──────────────────────────────────────────────────────────
    void Update()
    {
        if (connected != wasConnected)
        {
            wasConnected = connected;
            Debug.Log(connected
                ? $"[Telemetry] connected to {host}:{port}"
                : $"[Telemetry] disconnected ({lastError}); retrying...");

            if (connected) lastAckedSeq = -1;      // a new connection: acknowledge from scratch

            // The stub stopped: keep a record of the run while the numbers are final.
            if (!connected)
            {
                CloseLagLog();
                if (testMode && saveReportOnDisconnect && Stats.Parsed > lastSavedParsed) SaveReport();
            }
        }

        float inst = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        smoothedFps = Mathf.Lerp(smoothedFps, inst, 0.05f);

        for (int i = 0; i < maxLinesPerUpdate; i++)
        {
            string line;
            lock (gate)
            {
                if (queue.Count == 0) break;
                line = queue.Dequeue();
            }
            ProcessLine(line);
        }

        UpdateRate();
        if (Stats.Applied > 0 && connected && rateHz > 0f)   // wait until the frame rate has been measured
            Stats.NoteLag(FramesBehind, rateHz, Stats.LastSpeed);
        MaybeReply();
        MaybeWriteLagRow();
    }

    // ── lag test: frame rate, status replies, CSV ────────────────────────────
    void UpdateRate()
    {
        float now = Time.unscaledTime;
        if (now < nextRateTime) return;
        float window = now - (nextRateTime - 2f);
        if (nextRateTime > 0f && window > 0.1f)
        {
            float measured = (Stats.LinesReceived - prevLines) / window;
            // Smooth it, so a short stall doesn't make the frame rate (and so the lag) look wrong.
            rateHz = rateHz <= 0f ? measured : 0.8f * rateHz + 0.2f * measured;
        }
        prevLines = Stats.LinesReceived;
        nextRateTime = now + 2f;
    }

    void MaybeReply()
    {
        if (!sendStatusReplies || !connected) return;
        float now = Time.unscaledTime;
        bool newFrame = lastAppliedSeq > 0 && lastAppliedSeq != lastAckedSeq;
        bool throttleOk = now - lastReplyTime >= 1f / Mathf.Max(1f, maxRepliesPerSecond);
        bool heartbeat = now - lastReplyTime >= heartbeatSeconds;
        if ((newFrame && throttleOk) || heartbeat)
        {
            SendStatus();
            lastAckedSeq = lastAppliedSeq;
            lastReplyTime = now;
        }
    }

    void SendStatus()
    {
        var stream = activeStream;
        if (stream == null) return;

        // Same bits as the stub's STATUS_BITS table.
        int word = 0;
        if (connected && Stats.LinesReceived > 0) word |= 1 << 0;      // RX_OK
        if (lastLineGood && Stats.Applied > 0) word |= 1 << 1;         // PARSE_OK
        word |= 1 << 2;                                                 // RENDERING (the app is running)
        if (SecondsSinceLastFrame < 2f) word |= 1 << 3;                 // DATA_FRESH
        if (smoothedFps >= minFpsForOk) word |= 1 << 5;                 // FRAMERATE_OK
        string state = (word & 0x0F) == 0x0F ? "OK" : "WARN";

        var c = CultureInfo.InvariantCulture;
        string team = (teamName ?? "").Replace("\"", "").Replace("\\", "");
        string json = "{\"type\":\"status\",\"status_word\":" + word +
                      ",\"state\":\"" + state + "\",\"team\":\"" + team + "\"" +
                      ",\"fps\":" + smoothedFps.ToString("F1", c) +
                      (lastAppliedSeq > 0 ? ",\"seq_ack\":" + lastAppliedSeq : "") +
                      ",\"text\":\"behind " + FramesBehind + "\"}\n";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        lock (sendLock)
        {
            try { stream.Write(bytes, 0, bytes.Length); }
            catch { /* the connection dropped; the reader thread will notice */ }
        }
    }

    void MaybeWriteLagRow()
    {
        if (!testMode || !writeLagLog || Stats.Applied == 0 || !connected) return;
        float now = Time.unscaledTime;
        if (now < nextLagRow) return;
        nextLagRow = now + 1f;

        try
        {
            if (lagWriter == null)
            {
                lagPath = Path.Combine(Application.persistentDataPath,
                    "telemetry_lag_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                lagWriter = new StreamWriter(lagPath, false, new UTF8Encoding(false));
                lagWriter.WriteLine("real_time_s,sim_speed,frame_rate_hz,seq_received,seq_shown,frames_behind,real_lag_s,sim_lag_s,fps");
                lagStart = now;
            }
            var c = CultureInfo.InvariantCulture;
            var s = Stats;
            lagWriter.WriteLine(string.Join(",",
                (now - lagStart).ToString("F1", c), s.LastSpeed.ToString("0.##", c), rateHz.ToString("F2", c),
                s.MaxSeqSeen, lastAppliedSeq, s.FramesBehindNow,
                s.RealLagNow.ToString("F3", c), s.SimLagNow.ToString("F2", c), smoothedFps.ToString("F1", c)));
            lagWriter.Flush();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Telemetry] lag log: " + e.Message);
            lagWriter = null;
            writeLagLog = false;
        }
    }

    void CloseLagLog()
    {
        if (lagWriter == null) return;
        try { lagWriter.Dispose(); } catch { }
        Debug.Log("[Telemetry] lag log saved to " + lagPath);
        lagWriter = null;
    }

    void ProcessLine(string line)
    {
        try
        {
            // JsonUtility must run on the main thread, which is why parsing happens here.
            var frame = JsonUtility.FromJson<StateFrame>(line);
            if (frame == null) { Stats.ParseErrors++; lastLineGood = false; return; }

            if (frame.type != "state") { Stats.Ignored++; return; }   // e.g. the "hello" greeting
            if (!Stats.RegisterSeq(frame.seq)) return;                 // the same frame sent twice

            // Which of the four fields came through intact?
            bool pos = Valid(frame.orbit != null ? frame.orbit.pos_eci_km : null, 3);
            bool quat = Valid(frame.attitude != null ? frame.attitude.q_body_to_eci : null, 4);
            bool sunDir = Valid(frame.sun != null ? frame.sun.dir_eci : null, 3);
            // A missing bool would read as false, so check the key is really in the line.
            bool eclipse = frame.sun != null && line.IndexOf("\"in_eclipse\"", StringComparison.Ordinal) >= 0;
            Stats.NoteFields(pos, quat, sunDir, eclipse, Time.realtimeSinceStartup);

            if (!pos) return;   // the scene needs a position; without one the frame can't be applied

            Latest = frame;
            FramesReceived++;
            Stats.Applied++;
            lastLineGood = true;
            if (frame.seq > 0) lastAppliedSeq = frame.seq;
            if (frame.speed > 0) Stats.LastSpeed = frame.speed;
            lastFrameTime = Time.unscaledTime;

            if (logEveryNFrames > 0 && FramesReceived % logEveryNFrames == 0)
                Debug.Log($"[Telemetry] seq {frame.seq}  lat {frame.orbit.lat_deg:F2}  " +
                          $"lon {frame.orbit.lon_deg:F2}  alt {frame.orbit.alt_km:F0} km");
        }
        catch (Exception)
        {
            lastLineGood = false;
            Stats.ParseErrors++;   // truncated/garbled line (the stub's --chaos mode sends these on purpose)
        }
    }

    /// <summary>True if the array has exactly n numbers and none of them is NaN or infinity.</summary>
    static bool Valid(double[] a, int n)
    {
        if (a == null || a.Length != n) return false;
        for (int i = 0; i < n; i++)
            if (double.IsNaN(a[i]) || double.IsInfinity(a[i])) return false;
        return true;
    }
}
