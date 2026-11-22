using System;
using UnityEngine;

/// <summary>
/// Every number for the warning system (high spin, bad data), the red warning sphere and the warning popup, in one asset you
/// can edit in the Inspector: Assets/Resources/WarningSettings.asset (made for you; or CUBE15 > Select Warning Settings).
/// Everything is read live, so you can change values while the game is playing. Changes are kept (this is an asset).
///
/// Each kind of warning has its own timing, in the user's real seconds: how long the problem must last before the warning
/// appears (Trigger Seconds) and how long it must stay clear before the warning goes away (Clear Seconds).
/// </summary>
[CreateAssetMenu(fileName = "WarningSettings", menuName = "CUBE15/Warning Settings")]
public class WarningSettings : ScriptableObject
{
    static WarningSettings instance;

    public static WarningSettings Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<WarningSettings>("WarningSettings");
                if (instance == null) instance = CreateInstance<WarningSettings>();
            }
            return instance;
        }
    }

    [Serializable]
    public class Timing
    {
        [Tooltip("Switch this kind of warning off completely.")]
        public bool enabled = true;
        [Tooltip("The problem must last this many real seconds before the warning appears.")]
        public float triggerSeconds = 1f;
        [Tooltip("The problem must be gone for this many real seconds before the warning disappears.")]
        public float clearSeconds = 2f;
        public Timing() { }
        public Timing(float trigger, float clear) { triggerSeconds = trigger; clearSeconds = clear; }
    }

    // ── high spin ───────────────────────────────────────────────────────────
    [Header("High spin (how fast the satellite visibly turns relative to its orbit, per real second as the user sees it)")]
    public Timing spin = new Timing(0f, 0.5f);
    [Tooltip("The warning is on while the spin is above this, in degrees per real second, and off as soon as it is at or below it.")]
    public float spinWarnDegPerSec = 10f;
    [Tooltip("Off (default) = the spin the user actually sees on screen, per real second, so a fast tumble is caught however the stub's speed is set. On = per second of the simulation's own time.")]
    public bool spinInSimTime = false;
    [Tooltip("Measure the spin against the orbit (so the slow turn of a nadir-pointing satellite going round the Earth is ignored, at any simulation speed). Only a real tumble counts.")]
    public bool spinRelativeToOrbit = true;
    [Tooltip("How often the turn is measured, in real seconds (0.05 = 20 times a second).")]
    public float spinSampleSeconds = 0.05f;
    [Tooltip("The middle value of this many recent measurements is used (3 = one stray bad measurement is ignored, and a real tumble is caught within about 0.1 s). 1 = no filtering at all.")]
    public int spinSamples = 3;
    [Tooltip("The reading rises instantly but falls slowly, over about this many real seconds, so the warning does not flicker.")]
    public float spinWindowSeconds = 0.5f;

    // ── the sphere ──────────────────────────────────────────────────────────
    [Header("Warning sphere (around the satellite in every space view)")]
    public Color sphereColor = new Color(1f, 0.1f, 0.1f);
    [Tooltip("How see-through the sphere is at the warning level (0 = invisible, 1 = solid).")]
    [Range(0f, 1f)] public float sphereOpacity = 0.5f;
    [Tooltip("Makes the edge of the sphere stronger than its middle (0 = flat).")]
    [Range(0f, 4f)] public float sphereRim = 1.2f;
    [Tooltip("The sphere's radius around the satellite, in model metres (the satellite is 0.37 m long with 0.4 m wings).")]
    public float sphereRadius = 0.75f;
    [Tooltip("The sphere never looks smaller than this many degrees across, so it can be seen from far away...")]
    public float sphereMinAngleDeg = 5f;
    [Tooltip("...but it never grows past this many times its normal radius, however far you zoom out. 1 = it never grows.")]
    public float sphereMaxSizeMultiplier = 2f;
    public float sphereFadeInSeconds = 0.15f;
    public float sphereFadeOutSeconds = 1f;
    [Tooltip("How much the sphere's opacity pulses (0 = steady).")]
    [Range(0f, 0.5f)] public float spherePulseDepth = 0.12f;
    public float spherePulseSeconds = 1.2f;
    [Tooltip("The warning icon's size as a fraction of the sphere's diameter.")]
    [Range(0.1f, 1f)] public float iconSizeFraction = 0.4f;

    [Header("Covering the axes")]
    [Tooltip("At or above this spin the sphere becomes dense and the satellite's axes are hidden, so they are not read while tumbling. The sphere's opacity rises from the normal level to the dense level between the warning level and this one.")]
    public float axesCoverSpinDegPerSec = 40f;
    [Range(0f, 1f)] public float sphereCoverOpacity = 0.92f;

    [Header("Body-fixed view during high spin")]
    [Tooltip("While the spin is high the body-fixed view stops turning with the satellite (the satellite is still seen, tumbling inside the sphere).")]
    public bool freezeBodyView = true;
    [Tooltip("How long the view takes to swing back to following the satellite once the spin is stable.")]
    public float bodyReturnSeconds = 2f;

    // ── bad data ────────────────────────────────────────────────────────────
    [Header("Bad data: link lost (was receiving, now disconnected)")]
    public Timing noLink = new Timing(1f, 1f);

    [Header("Bad data: stale (connected but no new frame)")]
    public Timing stale = new Timing(0f, 1f);
    [Tooltip("The client's rule: data is fresh when the newest frame is under 2 s old.")]
    public float staleAfterSeconds = 2f;

    [Header("Bad data: invalid or garbled frames")]
    public Timing invalid = new Timing(0f, 5f);
    [Tooltip("Looks at this many recent real seconds...")]
    public float invalidWindowSeconds = 10f;
    [Tooltip("...and warns when at least this many frames were garbled or missing a field.")]
    public int invalidCountLimit = 3;

    [Header("Bad data: implausible values")]
    public Timing implausible = new Timing(1f, 3f);
    public float altitudeMinKm = 100f;
    public float altitudeMaxKm = 3000f;
    [Tooltip("Orbital speed limits in km/s (low Earth orbit is about 7.6).")]
    public float speedMinKmS = 5f;
    public float speedMaxKmS = 10f;
    [Tooltip("The attitude quaternion should have length 1; warn when it is off by more than this.")]
    public float quaternionTolerance = 0.05f;

    [Header("Bad data: dropped, repeated or late frames")]
    public Timing dropped = new Timing(0f, 5f);
    public float droppedWindowSeconds = 10f;
    [Tooltip("Warns when this many frames were dropped or repeated within the window above.")]
    public int droppedCountLimit = 3;
    [Tooltip("...or when the display is this many frames behind the newest data.")]
    public int framesBehindLimit = 30;

    // ── the popup ───────────────────────────────────────────────────────────
    [Header("Popup (a small panel floating above your left controller)")]
    public bool popupEnabled = true;
    [Tooltip("Overall size: the width in millimetres of a standard long popup (text and icon scale with it). The panel itself is only as wide as its text needs.")]
    public float popupWidthMm = 200f;
    [Tooltip("How far above the controller the panel floats, in metres.")]
    public float popupAboveControllerM = 0.12f;
    [Tooltip("If the left controller cannot be found: metres in front of you, to the left, and below eye level.")]
    public Vector3 popupFallbackOffset = new Vector3(-0.22f, -0.30f, 0.45f);
    public Color popupDangerColor = new Color(0.94f, 0.33f, 0.31f);
    public Color popupWarnColor = new Color(0.91f, 0.64f, 0.10f);
}
