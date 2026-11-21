using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Opens and closes the menu with the LEFT controller's menu button (Esc or M on a keyboard, for testing in the editor).
/// Creates itself when the app starts. It lives outside the menu panel because a switched-off panel cannot listen.
/// Works in the hub and in space, since the panel is in the always-loaded hub scene.
/// </summary>
public class MenuInput : MonoBehaviour
{
    [Tooltip("The id of the menu's Grabbable Panel.")]
    [SerializeField] string menuPanelId = "menu";

    static MenuInput instance;
    InputAction toggle;
    float lastPress = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("MenuInput");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MenuInput>();
    }

    void Awake()
    {
        // Several spellings of the same button; paths that don't exist are simply ignored.
        toggle = new InputAction("toggleMenu", InputActionType.Button);
        toggle.AddBinding("<XRController>{LeftHand}/menu");
        toggle.AddBinding("<XRController>{LeftHand}/menuButton");
        toggle.AddBinding("<XRController>{LeftHand}/{Menu}");
        toggle.AddBinding("<Keyboard>/escape");
        toggle.AddBinding("<Keyboard>/m");
        toggle.performed += _ =>
        {
            if (Time.unscaledTime - lastPress < 0.3f) return;     // ignore double reports of one press
            lastPress = Time.unscaledTime;
            PanelManager.Toggle(menuPanelId);
        };
    }

    void OnEnable() => toggle.Enable();
    void OnDisable() => toggle.Disable();
}
