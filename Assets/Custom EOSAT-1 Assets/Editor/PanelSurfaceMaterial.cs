#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Makes sure Assets/Resources/PanelSurface.mat exists (the depth-writing material that panel bodies use).</summary>
public static class PanelSurfaceMaterial
{
    const string Path = "Assets/Resources/PanelSurface.mat";

    [InitializeOnLoadMethod]
    static void Ensure()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(Path) != null) return;
            var shader = Shader.Find("CUBE15/PanelSurface");
            if (shader == null) return;                         // the shader is still importing; the next reload retries
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateAsset(new Material(shader), Path);
            AssetDatabase.SaveAssets();
            Debug.Log("Created " + Path);
        };
    }

    [MenuItem("CUBE15/Panels/Rebuild Panel Material")]
    static void Rebuild() => Ensure();
}
#endif
