#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Makes sure Assets/Resources/WarningSettings.asset exists, and gives a menu item to jump to it.</summary>
[InitializeOnLoad]
static class WarningSettingsAsset
{
    const string Path = "Assets/Resources/WarningSettings.asset";

    static WarningSettingsAsset() { EditorApplication.delayCall += () => Ensure(); }

    static WarningSettings Ensure()
    {
        var a = AssetDatabase.LoadAssetAtPath<WarningSettings>(Path);
        if (a != null) return a;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        a = ScriptableObject.CreateInstance<WarningSettings>();
        AssetDatabase.CreateAsset(a, Path);
        AssetDatabase.SaveAssets();
        Debug.Log("[CUBE15] Created " + Path + " (the warning system's settings).");
        return a;
    }

    [MenuItem("CUBE15/Select Warning Settings")]
    static void Select()
    {
        var a = Ensure();
        Selection.activeObject = a;
        EditorGUIUtility.PingObject(a);
    }
}
#endif
