#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pressing Play always starts from HubScene, even if SpaceView is the scene you have open.
/// The hub holds the XR rig and the telemetry connection, which the space view needs.
/// Turn it off from the menu: CUBE15 > Always Play From Hub.
/// </summary>
[InitializeOnLoad]
static class PlayFromHub
{
    const string MenuPath = "CUBE15/Always Play From Hub";
    const string PrefKey = "CUBE15_PlayFromHub";
    const string HubPath = "Assets/Scenes/HubScene.unity";

    static PlayFromHub() { Apply(); }

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static void Apply()
    {
        EditorSceneManager.playModeStartScene =
            Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(HubPath) : null;
    }

    [MenuItem(MenuPath)]
    static void Toggle() { Enabled = !Enabled; Apply(); }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return true; }
}
#endif
