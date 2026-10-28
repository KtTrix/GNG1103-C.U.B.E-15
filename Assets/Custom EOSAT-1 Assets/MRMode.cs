using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Switches the hub between VR (virtual environment) and mixed reality (passthrough).
///
/// Mixed reality mode:
///   * enables the AR Camera Manager (this is what turns passthrough on)
///   * makes the camera background transparent (required for passthrough)
///   * hides VR-only objects (platform, skybox props, ...)
///   * disables VR-only components (teleport / move providers, ...)
/// VR mode restores everything.
///
/// Put this on any object in the scene (for example the "Hub" object or an empty "Modes" object).
/// </summary>
public class MixedRealityMode : MonoBehaviour
{
    [Header("Passthrough")]
    [Tooltip("The headset camera (Main Camera under XR Origin). Leave empty to use Camera.main.")]
    [SerializeField] Camera xrCamera;
    [Tooltip("The AR Camera Manager on that same camera. Enabling it turns passthrough on.")]
    [SerializeField] ARCameraManager cameraManager;

    [Header("Switched OFF in mixed reality")]
    [Tooltip("GameObjects that only make sense in VR: the Environment object, platform, skybox props...")]
    [SerializeField] GameObject[] vrOnlyObjects;
    [Tooltip("Components to disable in MR, e.g. Teleportation Provider, Continuous Move Provider.")]
    [SerializeField] Behaviour[] vrOnlyComponents;

    [Header("Mode")]
    [SerializeField] bool startInMixedReality = false;
    [Tooltip("Built-in toggle: the RIGHT controller's primary button (A on Quest Touch controllers), " +
             "plus the M key for testing in the editor. Untick to disable.")]
    [SerializeField] bool useDefaultToggleButtons = true;
    [Tooltip("Optional: your own input action. If assigned, it is used instead of the built-in buttons.")]
    [SerializeField] InputActionReference toggleAction;

    CameraClearFlags vrClearFlags;
    Color vrBackground;
    InputAction builtInToggle;

    public bool IsMixedReality { get; private set; }

    void Awake()
    {
        if (xrCamera == null) xrCamera = Camera.main;
        if (xrCamera != null)
        {
            // Remember the VR look so we can restore it when leaving MR.
            vrClearFlags = xrCamera.clearFlags;
            vrBackground = xrCamera.backgroundColor;
        }
    }

    void OnEnable()
    {
        if (toggleAction != null)
        {
            toggleAction.action.performed += OnTogglePressed;
            toggleAction.action.Enable();
        }
        else if (useDefaultToggleButtons)
        {
            builtInToggle = new InputAction("ToggleMixedReality", InputActionType.Button);
            // {PrimaryButton} is the "A" button on the right Quest Touch controller
            // (X on the left). It works with any OpenXR controller profile.
            builtInToggle.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
            builtInToggle.AddBinding("<Keyboard>/m"); // for testing in the editor
            builtInToggle.performed += OnTogglePressed;
            builtInToggle.Enable();
        }
    }

    void OnDisable()
    {
        if (toggleAction != null)
            toggleAction.action.performed -= OnTogglePressed;

        if (builtInToggle != null)
        {
            builtInToggle.performed -= OnTogglePressed;
            builtInToggle.Disable();
            builtInToggle.Dispose();
            builtInToggle = null;
        }
    }

    void Start()
    {
        SetMixedReality(startInMixedReality);
    }

    void OnTogglePressed(InputAction.CallbackContext ctx) => ToggleMixedReality();

    /// <summary>Hook this up to a UI button later (for example on one of the panels).</summary>
    public void ToggleMixedReality() => SetMixedReality(!IsMixedReality);

    public void SetMixedReality(bool on)
    {
        IsMixedReality = on;

        // Enabling the AR Camera Manager enables passthrough; disabling it disables passthrough.
        if (cameraManager != null) cameraManager.enabled = on;

        if (xrCamera != null)
        {
            if (on)
            {
                // Passthrough needs a Solid Color background with alpha = 0.
                xrCamera.clearFlags = CameraClearFlags.SolidColor;
                xrCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }
            else
            {
                xrCamera.clearFlags = vrClearFlags;
                xrCamera.backgroundColor = vrBackground;
            }
        }

        foreach (var go in vrOnlyObjects)
            if (go != null) go.SetActive(!on);

        foreach (var c in vrOnlyComponents)
            if (c != null) c.enabled = !on;

        Debug.Log($"[MixedReality] mode = {(on ? "Mixed Reality (passthrough)" : "VR")}");
    }
}