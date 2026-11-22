using UnityEngine;

/// <summary>
/// Every look-and-feel number for the space view's axes, dot and trail, in one asset you can edit in the Inspector:
/// Assets/Resources/SpaceMarkerSettings.asset (made for you; or CUBE15 > Select Space Marker Settings).
/// The scripts read it every frame, so you can change values while the game is playing and see the result at once.
/// Changes made while playing are kept, because this is an asset.
/// </summary>
[CreateAssetMenu(fileName = "SpaceMarkerSettings", menuName = "CUBE15/Space Marker Settings")]
public class SpaceMarkerSettings : ScriptableObject
{
    static SpaceMarkerSettings instance;

    /// <summary>The asset in Resources, or a default copy if it has not been made yet.</summary>
    public static SpaceMarkerSettings Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<SpaceMarkerSettings>("SpaceMarkerSettings");
                if (instance == null) instance = CreateInstance<SpaceMarkerSettings>();
            }
            return instance;
        }
    }

    [Header("Axes colours (X, Y, Z)")]
    public Color axisX = new Color(0.95f, 0.33f, 0.33f);
    public Color axisY = new Color(0.30f, 0.85f, 0.45f);
    public Color axisZ = new Color(0.35f, 0.60f, 1.00f);

    [Header("Satellite axes")]
    [Tooltip("Length of each axis from the satellite's centre, in model metres (the body is 0.37 m long).")]
    public float satLength = 0.65f;
    [Tooltip("Shaft thickness, in model metres.")]
    public float satThickness = 0.012f;
    [Tooltip("Size of the X, Y, Z letters, in model metres.")]
    public float satLabelSize = 0.16f;
    [Tooltip("The axes never look smaller than this many degrees of your view, so they grow as you zoom out. 0 = they stay the satellite's size.")]
    public float satMinAngleDeg = 4f;

    [Tooltip("The axes are never longer than this fraction of your distance to the satellite, so an arrow pointing at you cannot reach out toward your face. Lower = shorter arrows when you are close.")]
    [Range(0.05f, 1f)] public float satMaxFractionOfDistance = 0.2f;

    [Header("Earth axes")]
    [Tooltip("How far the arrows reach from the Earth's centre, in Earth radii.")]
    public float earthLengthRadii = 1.6f;
    [Tooltip("Shaft thickness, in Earth radii.")]
    public float earthThicknessRadii = 0.012f;
    [Tooltip("Size of the letters, in Earth radii.")]
    public float earthLabelRadii = 0.09f;

    [Header("Dot")]
    public Color dotColor = new Color(1f, 0.72f, 0.92f);
    [Tooltip("Apparent size of the dot's glow, in degrees of your view.")]
    public float dotSizeDeg = 3f;
    [Tooltip("Seconds for one slow pulse.")]
    public float pulseSeconds = 2.4f;
    [Tooltip("How much the dot grows and shrinks as it pulses (0 = not at all).")]
    [Range(0f, 0.6f)] public float pulseSizeDepth = 0.3f;
    [Tooltip("How much its brightness changes as it pulses (0 = not at all).")]
    [Range(0f, 0.8f)] public float pulseBrightnessDepth = 0.35f;

    [Header("Trail")]
    public Color trailColor = new Color(1f, 0.24f, 0.78f);       // the hub's trail colour
    [Tooltip("Apparent width of the trail, in degrees of your view.")]
    public float trailWidthDeg = 0.3f;
    [Tooltip("How far back the trail reaches, in degrees of the orbit (360 = one full lap). Beyond this it is not drawn, and the trail fades out towards this end. 0 = no limit (the whole history).")]
    public float trailLengthDeg = 120f;
    [Tooltip("Most history points drawn.")]
    public int maxTrailPoints = 700;
    [Tooltip("How see-through the oldest end of the trail is (0 = fades out completely).")]
    [Range(0f, 1f)] public float trailTailAlpha = 0f;

    [Header("Fading when you come close (the satellite's apparent size, in degrees)")]
    [Tooltip("Up to this size the dot and trail are at full strength.")]
    public float fullUntilSizeDeg = 1f;
    [Tooltip("From this size the dot is gone completely: the satellite itself can be made out.")]
    public float dotGoneAtSizeDeg = 6f;
    [Tooltip("At this size the trail has dimmed down to its lowest strength.")]
    public float trailDimmestAtSizeDeg = 12f;
    [Tooltip("The trail's strength once you are close (1 = it does not dim).")]
    [Range(0f, 1f)] public float trailLowestStrength = 0.15f;

    public Color[] AxisColors => new[] { axisX, axisY, axisZ };

    /// <summary>A number that changes whenever a value that needs the arrows to be rebuilt changes.</summary>
    public int GeometryStamp()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + satLength.GetHashCode(); h = h * 31 + satThickness.GetHashCode(); h = h * 31 + satLabelSize.GetHashCode();
            h = h * 31 + earthLengthRadii.GetHashCode(); h = h * 31 + earthThicknessRadii.GetHashCode(); h = h * 31 + earthLabelRadii.GetHashCode();
            h = h * 31 + axisX.GetHashCode(); h = h * 31 + axisY.GetHashCode(); h = h * 31 + axisZ.GetHashCode();
            return h;
        }
    }
}
