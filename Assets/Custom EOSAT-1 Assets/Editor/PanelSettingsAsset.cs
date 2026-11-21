#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Makes sure Assets/Resources/PanelSettings.asset exists, and adds a menu item to find it.</summary>
public static class PanelSettingsAsset
{
    const string Path = "Assets/Resources/PanelSettings.asset";

    [InitializeOnLoadMethod]
    static void Ensure()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(Path) != null) return;
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<PanelSettings>(), Path);
            AssetDatabase.SaveAssets();
            Debug.Log("Created " + Path);
        };
    }

    [MenuItem("CUBE15/Panels/Select Panel Settings")]
    static void Select()
    {
        Ensure();
        var a = AssetDatabase.LoadAssetAtPath<PanelSettings>(Path);
        if (a != null) { Selection.activeObject = a; EditorGUIUtility.PingObject(a); }
    }
}
#endif
