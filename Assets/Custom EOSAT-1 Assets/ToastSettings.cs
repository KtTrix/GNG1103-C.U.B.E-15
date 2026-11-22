using UnityEngine;

/// <summary>
/// The small "Space view / Hub / Mixed reality / view name" message that pops up above a controller when you press its
/// button. Edit Assets/Resources/ToastSettings.asset (CUBE15 > Select Toast Settings); changes apply live.
/// </summary>
[CreateAssetMenu(fileName = "ToastSettings", menuName = "CUBE15/Toast Settings")]
public class ToastSettings : ScriptableObject
{
    static ToastSettings instance;
    public static ToastSettings Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<ToastSettings>("ToastSettings");
                if (instance == null) instance = CreateInstance<ToastSettings>();
            }
            return instance;
        }
    }

    public bool enabled = true;
    [Tooltip("How long the message stays fully visible, in seconds.")]
    public float holdSeconds = 0.9f;
    [Tooltip("How long it then takes to fade away.")]
    public float fadeSeconds = 0.35f;
    [Tooltip("How far above the controller it floats, in metres.")]
    public float aboveControllerM = 0.10f;
    [Tooltip("The message's height in millimetres (the text and the box scale with it).")]
    public float heightMm = 36f;
    public Color textColor = new Color(0.94f, 0.96f, 1f);
    public Color backColor = new Color(0.086f, 0.098f, 0.125f, 0.95f);
    public Color borderColor = new Color(0.94f, 0.63f, 0.13f);
    [Tooltip("If the controller cannot be found: metres to the side (left or right), below eye level, and in front of you.")]
    public Vector3 fallbackOffset = new Vector3(0.22f, -0.30f, 0.45f);
}
