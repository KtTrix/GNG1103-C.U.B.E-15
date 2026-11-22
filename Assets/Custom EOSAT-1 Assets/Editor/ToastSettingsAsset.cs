#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Makes sure Assets/Resources/ToastSettings.asset exists, and gives a menu item to jump to it.</summary>
[InitializeOnLoad]
static class ToastSettingsAsset
{
    const string Path = "Assets/Resources/ToastSettings.asset";

    static ToastSettingsAsset() { EditorApplication.delayCall += () => Ensure(); }

    static ToastSettings Ensure()
    {
        var a = AssetDatabase.LoadAssetAtPath<ToastSettings>(Path);
        if (a != null) return a;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        a = ScriptableObject.CreateInstance<ToastSettings>();
        AssetDatabase.CreateAsset(a, Path);
        AssetDatabase.SaveAssets();
        Debug.Log("[CUBE15] Created " + Path + " (the controller message's settings).");
        return a;
    }

    [MenuItem("CUBE15/Select Toast Settings")]
    static void Select()
    {
        var a = Ensure();
        Selection.activeObject = a;
        EditorGUIUtility.PingObject(a);
    }
}
#endif
