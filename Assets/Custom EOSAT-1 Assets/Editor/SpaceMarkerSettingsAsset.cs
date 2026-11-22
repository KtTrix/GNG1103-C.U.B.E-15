#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Makes sure Assets/Resources/SpaceMarkerSettings.asset exists, and gives a menu item to jump to it.</summary>
[InitializeOnLoad]
static class SpaceMarkerSettingsAsset
{
    const string Path = "Assets/Resources/SpaceMarkerSettings.asset";

    static SpaceMarkerSettingsAsset() { EditorApplication.delayCall += () => Ensure(); }

    static SpaceMarkerSettings Ensure()
    {
        var a = AssetDatabase.LoadAssetAtPath<SpaceMarkerSettings>(Path);
        if (a != null) return a;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        a = ScriptableObject.CreateInstance<SpaceMarkerSettings>();
        AssetDatabase.CreateAsset(a, Path);
        AssetDatabase.SaveAssets();
        Debug.Log("[CUBE15] Created " + Path + " (the space view's axes, dot and trail settings).");
        return a;
    }

    [MenuItem("CUBE15/Select Space Marker Settings")]
    static void Select()
    {
        var a = Ensure();
        Selection.activeObject = a;
        EditorGUIUtility.PingObject(a);
    }
}
#endif
