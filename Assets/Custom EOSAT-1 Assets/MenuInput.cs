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
        // The button comes from ControlMap (Options > Controls > Button mapping); the keyboard keys are for the editor.
        toggle = new InputAction("toggleMenu", InputActionType.Button);
        ControlMap.Apply(toggle, "menu", null, "<Keyboard>/escape", "<Keyboard>/m");
        toggle.performed += _ =>
        {
            if (ControlMap.Busy) return;                          // a button is being captured for remapping
            if (Time.unscaledTime - lastPress < 0.3f) return;     // ignore double reports of one press
            lastPress = Time.unscaledTime;
            var menu = PanelManager.Get(menuPanelId);
            if (menu != null && menu.gameObject.activeSelf && ControlMap.BlockLeave()) return;   // unapplied button changes
            PanelManager.Toggle(menuPanelId);
        };
    }

    void OnEnable() => toggle.Enable();
    void OnDisable() { ControlMap.Unbind(toggle); toggle.Disable(); }
}
