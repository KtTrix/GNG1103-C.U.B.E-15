using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Counters for the "parse test": how many frames the stub sent, how many the app parsed and
/// applied, how many errors there were, and how many frames had each of the four fields.
///
/// Frames sent is worked out from the stub's own sequence numbers ("seq"): the stub numbers every
/// frame it sends, so (highest seq seen - lowest seq seen + 1) is how many it sent while we were
/// listening. That includes garbled and dropped lines, because their seq is read from the raw text.
/// </summary>
public class TelemetryStats
{
    // Written by the network thread (always under the client's lock).
    public int LinesReceived;
    public int Dropped;                    // lines thrown away unread
    public int MinSeqSeen = -1, MaxSeqSeen = -1;

    // Written by the main thread.
    public int ParseErrors;                // not valid JSON (garbled / truncated)
    public int Ignored;                    // valid JSON, but not a state frame (for example "hello")
    public int Duplicates;                 // the same seq arrived again
    public int Parsed;                     // distinct state frames parsed
    public int Applied;                    // ... that had a usable position and were handed to the scene
    public int PosOk, QuatOk, SunDirOk, EclipseOk, AllOk;
    public float FirstTime = -1f, LastTime = -1f;

    // Lag test (main thread).
    public double LastSpeed;                          // simulated seconds per real second, from the frames
    public float RateHz;                              // how fast frames arrive, measured
    public int FramesBehindNow, PeakFramesBehind;     // frames received but not yet shown
    public double RealLagNow, PeakRealLag;            // frames behind / frame rate  (real seconds)
    public double SimLagNow, PeakSimLag;              // real lag x sim speed        (simulated seconds)
    public const double LagLimitSimSeconds = 60.0;    // the test's pass line

    readonly HashSet<int> seqs = new HashSet<int>();

    public int FramesSent => MinSeqSeen < 0 ? 0 : MaxSeqSeen - MinSeqSeen + 1;
    public int ValidSent => Math.Max(0, FramesSent - ParseErrors);

    /// <summary>The test's pass rule: every valid frame used, all 4 fields read, no errors.</summary>
    public bool MeetsCriteria =>
        Parsed > 0 && Parsed >= ValidSent && ParseErrors == 0 && Dropped == 0 && AllOk == Parsed;

    public void Reset()
    {
        LinesReceived = Dropped = 0;
        MinSeqSeen = MaxSeqSeen = -1;
        ParseErrors = Ignored = Duplicates = Parsed = Applied = 0;
        PosOk = QuatOk = SunDirOk = EclipseOk = AllOk = 0;
        FirstTime = LastTime = -1f;
        LastSpeed = 0; RateHz = 0;
        FramesBehindNow = PeakFramesBehind = 0;
        RealLagNow = PeakRealLag = SimLagNow = PeakSimLag = 0;
        seqs.Clear();
    }

    // Network thread, under the lock.
    public void NoteSeqSeen(int seq)
    {
        if (MinSeqSeen < 0 || seq < MinSeqSeen) MinSeqSeen = seq;
        if (seq > MaxSeqSeen) MaxSeqSeen = seq;
    }

    /// <summary>False if this seq was already seen (a duplicate line).</summary>
    public bool RegisterSeq(int seq)
    {
        if (seq <= 0) return true;         // no usable seq: cannot check for duplicates
        if (seqs.Add(seq)) return true;
        Duplicates++;
        return false;
    }

    public void NoteFields(bool pos, bool quat, bool sunDir, bool eclipse, float now)
    {
        Parsed++;
        if (pos) PosOk++;
        if (quat) QuatOk++;
        if (sunDir) SunDirOk++;
        if (eclipse) EclipseOk++;
        if (pos && quat && sunDir && eclipse) AllOk++;
        if (FirstTime < 0f) FirstTime = now;
        LastTime = now;
    }

    /// <summary>Record how far behind the display is right now.</summary>
    public void NoteLag(int framesBehind, float rateHz, double speed)
    {
        RateHz = rateHz;
        if (speed > 0) LastSpeed = speed;
        FramesBehindNow = framesBehind;
        RealLagNow = framesBehind / Math.Max(0.1, rateHz);          // frames behind / fps = real seconds
        SimLagNow = RealLagNow * (LastSpeed > 0 ? LastSpeed : 1.0); // x sim speed = simulated seconds
        if (framesBehind > PeakFramesBehind) PeakFramesBehind = framesBehind;
        if (RealLagNow > PeakRealLag) PeakRealLag = RealLagNow;
        if (SimLagNow > PeakSimLag) PeakSimLag = SimLagNow;
    }

    /// <summary>Reads "seq" out of the raw text of a state line, even if the line is garbled.</summary>
    public static bool TryGetSeq(string line, out int seq)
    {
        seq = 0;
        if (line.IndexOf("\"type\":\"state\"", StringComparison.Ordinal) < 0) return false;
        int i = line.IndexOf("\"seq\":", StringComparison.Ordinal);
        if (i < 0) return false;
        i += 6;
        while (i < line.Length && line[i] == ' ') i++;
        int v = 0, digits = 0;
        while (i < line.Length && line[i] >= '0' && line[i] <= '9' && digits < 9)
        {
            v = v * 10 + (line[i] - '0');
            i++; digits++;
        }
        if (digits == 0) return false;
        seq = v;
        return true;
    }

    // ── report ───────────────────────────────────────────────────────────────
    static string Pct(int part, int whole) =>
        whole <= 0 ? "--" : (100.0 * part / whole).ToString("F1", CultureInfo.InvariantCulture) + " %";

    public string ElapsedText()
    {
        if (FirstTime < 0f) return "--";
        return TimeSpan.FromSeconds(Math.Max(0f, LastTime - FirstTime)).ToString(@"hh\:mm\:ss");
    }

    public List<KeyValuePair<string, string>> CountRows()
    {
        var r = new List<KeyValuePair<string, string>>();
        void Add(string k, string v) => r.Add(new KeyValuePair<string, string>(k, v));

        Add("Run time", ElapsedText());
        Add("Seq range", MinSeqSeen < 0 ? "--" : MinSeqSeen + " to " + MaxSeqSeen);
        Add("Frames sent", FramesSent.ToString());
        Add("Lines received", LinesReceived.ToString());
        Add("Parsed OK", Parsed + "  (" + Pct(Parsed, ValidSent) + ")");
        Add("Applied", Applied.ToString());
        Add("Parse errors", ParseErrors.ToString());
        Add("Dropped unread", Dropped.ToString());
        Add("Ignored", Ignored.ToString());
        Add("Duplicates", Duplicates.ToString());
        Add("Unaccounted", (FramesSent - Parsed - ParseErrors - Dropped).ToString());
        return r;
    }

    public List<KeyValuePair<string, string>> FieldRows()
    {
        var r = new List<KeyValuePair<string, string>>();
        void Add(string k, string v) => r.Add(new KeyValuePair<string, string>(k, v));

        Add("orbit.pos_eci_km", Pct(PosOk, Parsed));
        Add("attitude.q_body_to_eci", Pct(QuatOk, Parsed));
        Add("sun.dir_eci", Pct(SunDirOk, Parsed));
        Add("sun.in_eclipse", Pct(EclipseOk, Parsed));
        Add("All four fields", Pct(AllOk, Parsed));
        Add("Meets pass rule", Parsed == 0 ? "--" : (MeetsCriteria ? "YES" : "NO"));
        return r;
    }

    public List<KeyValuePair<string, string>> LagRows()
    {
        var r = new List<KeyValuePair<string, string>>();
        void Add(string k, string v) => r.Add(new KeyValuePair<string, string>(k, v));
        var c = CultureInfo.InvariantCulture;

        Add("Sim speed", LastSpeed > 0 ? "x" + LastSpeed.ToString("0.##", c) : "unknown");
        Add("Frame rate", RateHz.ToString("F1", c) + " Hz");
        Add("Frames behind", FramesBehindNow + "  (peak " + PeakFramesBehind + ")");
        Add("Real lag", RealLagNow.ToString("F2", c) + " s  (peak " + PeakRealLag.ToString("F2", c) + ")");
        Add("Sim lag", SimLagNow.ToString("F1", c) + " s  (peak " + PeakSimLag.ToString("F1", c) + ")");
        Add("Under 60 s", Applied == 0 ? "--" : (PeakSimLag < LagLimitSimSeconds ? "YES" : "NO"));
        return r;
    }

    /// <summary>Plain text for saving to a file or pasting into the test report.</summary>
    public string ReportText(string source)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Telemetry parse test");
        sb.AppendLine("Source: " + source);
        sb.AppendLine("Saved:  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        foreach (var kv in CountRows()) sb.AppendLine(kv.Key.PadRight(24) + kv.Value);
        sb.AppendLine();
        sb.AppendLine("Fields read per parsed frame:");
        foreach (var kv in FieldRows()) sb.AppendLine(kv.Key.PadRight(24) + kv.Value);
        sb.AppendLine();
        sb.AppendLine("Lag (this app's own view):");
        foreach (var kv in LagRows()) sb.AppendLine(kv.Key.PadRight(24) + kv.Value);
        sb.AppendLine();
        sb.AppendLine("Frames sent = highest seq - lowest seq + 1, as numbered by the stub.");
        sb.AppendLine("Parsed OK % = parsed / (frames sent - parse errors).");
        sb.AppendLine("Unaccounted = sent - parsed - parse errors - dropped (frames lost on the network).");
        sb.AppendLine("Real lag = frames behind / frame rate. Sim lag = real lag x sim speed. Pass if peak sim lag < 60 s.");
        return sb.ToString();
    }
}
