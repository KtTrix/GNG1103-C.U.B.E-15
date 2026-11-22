using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Time panel: a video-style bar over the recorded telemetry, with rewind, play/pause, fast-forward and a LIVE button.
/// Grab the bar to scrub through what happened; let go at the front and it goes back to live. Little ticks on the bar mark
/// warnings (red), bad data (orange) and mode changes (blue); the line under the bar names the latest one before the playhead.
/// Built by CUBE15 > Panels > New Time Panel. All the recording and playback logic is in Timeline.
/// </summary>
public class TimeControlPage : MonoBehaviour
{
    [SerializeField] Slider bar;
    [Tooltip("Empty rectangle laid over the bar's slide area: the ticks are drawn inside it.")]
    [SerializeField] RectTransform markerLayer;
    [SerializeField] TMP_Text timeText;
    [SerializeField] TMP_Text rateText;
    [SerializeField] TMP_Text captionText;
    [SerializeField] TMP_Text playLabel;
    [SerializeField] TMP_Text liveLabel;
    [SerializeField] Image liveDot;
    [SerializeField] float markerWidth = 5f;

    readonly List<Image> ticks = new List<Image>();
    int shownVersion = -1;
    float nextText;
    float shownStart = -1f, shownEnd = -1f, nextRedraw;

    // ── buttons ──────────────────────────────────────────────────────────────
    public void Rewind() => Timeline.Rewind();
    public void TogglePlay() => Timeline.TogglePlay();
    public void Faster() => Timeline.Faster();
    public void GoLive() => Timeline.GoLive();

    /// <summary>The bar was moved by the player (the script moves it without calling this).</summary>
    public void OnBar(float v)
    {
        Timeline.BeginScrub();            // idempotent; the press itself may arrive a moment later
        Timeline.SeekFraction(v);
    }

    // ── what is shown ────────────────────────────────────────────────────────
    void OnEnable() { shownVersion = -1; Refresh(); }

    void Update() { Refresh(); }

    static string Clock(float seconds)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return s >= 3600 ? (s / 3600) + ":" + ((s / 60) % 60).ToString("00") + ":" + (s % 60).ToString("00")
                         : (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
    }

    static string RateLabel(float r)
    {
        if (Mathf.Abs(r) < 0.001f) return "Paused";
        return (r > 0f ? "" : "-") + Mathf.Abs(r).ToString("0.#") + "x";
    }

    void Refresh()
    {
        bool live = Timeline.Live;
        float start = Timeline.Start, end = Timeline.End, pos = Timeline.Position;

        if (bar != null && !Timeline.Scrubbing) bar.SetValueWithoutNotify(Timeline.Fraction);

        bool textsDue = Time.unscaledTime >= nextText;
        if (textsDue) nextText = Time.unscaledTime + 0.1f;
        if (textsDue && timeText != null)
        {
            string sim = "";
            double cs = Timeline.CurrentSim;
            if (cs > 1e8) sim = "\n<size=65%>Sim " + new DateTime((long)(cs * TimeSpan.TicksPerSecond), DateTimeKind.Utc).ToString("HH:mm:ss") + "Z</size>";
            timeText.text = Clock(pos - start) + " / " + Clock(end - start) + sim;
        }

        if (!textsDue) return;
        if (rateText != null) rateText.text = live ? "" : RateLabel(Timeline.Rate);
        if (playLabel != null) playLabel.text = "<cspace=0.08em><b>" + (live || Mathf.Abs(Timeline.Rate) > 0.001f ? "PAUSE" : "PLAY") + "</b></cspace>";

        if (liveLabel != null) liveLabel.color = live ? CubeTheme.Bad : CubeTheme.Dim;
        if (liveDot != null) liveDot.color = live ? CubeTheme.Bad : CubeTheme.Dim;

        // ticks: redraw when a marker was added, the recording grew noticeably, or twice a second
        bool changed = Timeline.MarkersVersion != shownVersion || Mathf.Abs(start - shownStart) > 0.01f;
        if (changed || Time.unscaledTime >= nextRedraw)
        {
            nextRedraw = Time.unscaledTime + 0.5f;
            DrawTicks(start, end);
            shownVersion = Timeline.MarkersVersion; shownStart = start; shownEnd = end;
        }

        if (captionText != null)
        {
            string text = "";
            var ms = Timeline.Markers;
            for (int i = ms.Count - 1; i >= 0; i--)
                if (ms[i].t <= pos + 0.01f)
                {
                    text = "<color=#" + CubeTheme.HexString(KindColor(ms[i].kind)) + ">" + Clock(ms[i].t - start) + "  " + ms[i].text + "</color>";
                    break;
                }
            captionText.text = text;
        }
    }

    static Color KindColor(Timeline.MarkerKind k)
    {
        return k == Timeline.MarkerKind.Warning ? CubeTheme.Bad : k == Timeline.MarkerKind.Data ? CubeTheme.Warn : CubeTheme.Info;
    }

    void DrawTicks(float start, float end)
    {
        if (markerLayer == null) return;
        var ms = Timeline.Markers;
        float span = Mathf.Max(0.01f, end - start);
        int used = 0;
        for (int i = 0; i < ms.Count; i++)
        {
            float f = (ms[i].t - start) / span;
            if (f < 0f || f > 1f) continue;
            if (used >= ticks.Count)
            {
                var g = new GameObject("Tick", typeof(RectTransform), typeof(Image));
                g.transform.SetParent(markerLayer, false);
                var im = g.GetComponent<Image>(); im.raycastTarget = false;
                ticks.Add(im);
            }
            var t = ticks[used++];
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t.color = KindColor(ms[i].kind);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = new Vector2(f, 0f); rt.anchorMax = new Vector2(f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(markerWidth, 0f);
        }
        for (int i = used; i < ticks.Count; i++) if (ticks[i].gameObject.activeSelf) ticks[i].gameObject.SetActive(false);
    }
}
