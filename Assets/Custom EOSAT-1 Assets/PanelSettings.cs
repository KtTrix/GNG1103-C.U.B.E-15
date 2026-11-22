using UnityEngine;

/// <summary>
/// The feel of panel grabbing, in one place. The asset lives at Assets/Resources/PanelSettings.asset
/// (created automatically). Select it in the Project window and change the numbers; changes apply at once,
/// even while the game is running. Menu shortcut: CUBE15 > Panels > Select Panel Settings.
/// </summary>
[CreateAssetMenu(fileName = "PanelSettings", menuName = "CUBE15/Panel Settings")]
public class PanelSettings : ScriptableObject
{
    [Header("Breaking free of the sliding track")]
    [Tooltip("How far (metres) you raise or lower your hand while holding a panel before it comes free. 0.02 = 2 cm.")]
    public float breakFreeDistance = 0.02f;

    [Header("Floor-plane movement")]
    [Tooltip("Pushing or pulling your hand moves the panel this many times as far, nearer or farther.")]
    public float slideGain = 2f;
    public float minDistanceFromHead = 0.45f;

    [Header("Start of the app")]
    [Tooltip("Show the default layout (position panel left, map right) as soon as the headset is tracking.")]
    public bool showLayoutOnStart = true;

    [Tooltip("Put the start layout beside the globe (instead of close to you). Needs an object called GlobeModel.")]
    public bool layoutBesideGlobe = true;
    [Tooltip("Gap (metres) between the globe's edge and a panel beside it.")]
    public float layoutGlobeGap = 0.25f;

    [Header("Weight")]
    [Tooltip("How heavy panels feel. 0 = light (follows your hand instantly), 1 = very heavy (slow, lazy to move and turn).")]
    [Range(0f, 1f)] public float weight = 0.5f;

    /// <summary>The weight in use: what the player set in the menu (Options > Panels > Panel feel), else the number above.</summary>
    public float Weight => CubeSettings.GetFloat("panel.weight", weight);

    /// <summary>The break-free distance in use (metres): the player's menu setting, else the number at the top.</summary>
    public float BreakFree => CubeSettings.GetFloat("panel.breakfree", breakFreeDistance);

    /// <summary>How much slower things get as the weight goes up (1 = no slowing).</summary>
    public float Heft { get { float w = Weight; return 1f + w * w * 12f; } }

    [Header("Attach to HEAD (lazy follow)")]
    [Tooltip("How quickly the panel glides to its new spot once it starts following (higher = faster).")]
    public float lazyFollowSpeed = 3f;
    [Tooltip("The panel stays put until it is this far from where it should be (metres, plus a turn allowance). Bigger = lazier.")]
    public float lazyFollowDeadZone = 0.3f;

    [Header("Feel")]
    [Tooltip("How quickly a grabbed panel turns to face you (higher = faster).")]
    public float faceUserSpeed = 10f;
    [Tooltip("How quickly the panel snaps onto your pointer when it breaks free (higher = faster).")]
    public float snapSpeed = 35f;
    [Tooltip("Free panels always face you. Off (recommended) = a free panel keeps its own tilt and copies your hand's twist.")]
    public bool faceUserWhileFree = false;

    [Header("Thumbstick while holding a panel")]
    [Tooltip("Thumbstick forward/back moves the held panel away/nearer at this speed (metres per second).")]
    public float stickPushSpeed = 1.5f;
    public float stickDeadZone = 0.15f;

    [Header("Template locomotion")]
    [Tooltip("Switch the template's teleport off completely (no red teleport line when you push a stick forward).")]
    public bool disableTeleport = true;

    [Header("Input")]
    public float maxRayDistance = 8f;
    public float gripOn = 0.6f;
    public float gripOff = 0.4f;

    static PanelSettings cached;

    /// <summary>The shared settings asset (defaults are used if the asset is missing).</summary>
    public static PanelSettings Get()
    {
        if (cached == null)
        {
            cached = Resources.Load<PanelSettings>("PanelSettings");
            if (cached == null) cached = CreateInstance<PanelSettings>();
        }
        return cached;
    }
}
