using System;
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
}

/// <summary>
/// Connects to the VR stub (or the real simulator) over TCP, reads one JSON
/// frame per line on a background thread, and hands the newest valid frame to
/// the main thread. Reconnects automatically.
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

    [Header("Debug")]
    [Tooltip("Log lat/lon/alt every N frames so you can compare with the globe. 0 = off.")]
    [SerializeField] int logEveryNFrames = 0;

    /// <summary>Newest valid state frame, or null if none has arrived yet.</summary>
    public StateFrame Latest { get; private set; }
    /// <summary>Count of valid frames received (changes whenever Latest changes).</summary>
    public int FramesReceived { get; private set; }
    public int BadLines { get; private set; }
    public bool IsConnected => connected;
    /// <summary>Seconds since the last valid frame (useful later for a "data fresh" indicator).</summary>
    public float SecondsSinceLastFrame => FramesReceived == 0 ? float.PositiveInfinity
                                                              : Time.unscaledTime - lastFrameTime;

    Thread worker;
    volatile bool running;
    volatile bool connected;
    volatile string lastError = "";
    volatile TcpClient activeClient;

    readonly object gate = new object();
    string pendingLine;              // only the newest unread line is kept
    float lastFrameTime;
    bool wasConnected;

    void OnEnable()
    {
        running = true;
        string h = host; int p = port; float delay = reconnectDelaySeconds; // copy for the thread
        worker = new Thread(() => RunLoop(h, p, delay)) { IsBackground = true, Name = "TelemetryClient" };
        worker.Start();
    }

    void OnDisable()
    {
        running = false;
        try { activeClient?.Close(); } catch { /* already closed */ }
        worker?.Join(500);
        worker = null;
        connected = false;
    }

    // ── Background thread: never touch Unity objects in here ─────────────────
    void RunLoop(string h, int p, float retryDelay)
    {
        while (running)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    activeClient = client;
                    client.NoDelay = true;

                    var connect = client.ConnectAsync(h, p);
                    if (!connect.Wait(3000)) throw new TimeoutException("connect timed out");

                    connected = true;
                    lastError = "";
                    using (var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false)))
                    {
                        string line;
                        while (running && (line = reader.ReadLine()) != null)
                        {
                            if (line.Length == 0) continue;
                            lock (gate) pendingLine = line;
                        }
                    }
                    lastError = "server closed the connection";
                }
            }
            catch (Exception e)
            {
                lastError = e.InnerException != null ? e.InnerException.Message : e.Message;
            }

            connected = false;
            for (float t = 0f; running && t < retryDelay; t += 0.1f) Thread.Sleep(100);
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
        }

        string line;
        lock (gate) { line = pendingLine; pendingLine = null; }
        if (line == null) return;

        try
        {
            // JsonUtility must run on the main thread, which is why parsing happens here.
            var frame = JsonUtility.FromJson<StateFrame>(line);
            bool ok = frame != null
                      && frame.type == "state"
                      && frame.orbit != null
                      && frame.orbit.pos_eci_km != null
                      && frame.orbit.pos_eci_km.Length == 3;
            if (!ok) return; // e.g. the "hello" greeting: not an error, just not a state frame

            Latest = frame;
            FramesReceived++;
            lastFrameTime = Time.unscaledTime;

            if (logEveryNFrames > 0 && FramesReceived % logEveryNFrames == 0)
                Debug.Log($"[Telemetry] seq {frame.seq}  lat {frame.orbit.lat_deg:F2}  " +
                          $"lon {frame.orbit.lon_deg:F2}  alt {frame.orbit.alt_km:F0} km");
        }
        catch (Exception)
        {
            BadLines++; // truncated/garbled line (the stub's --chaos mode sends these on purpose)
        }
    }
}