using TMPro;
using UnityEngine;

/// <summary>
/// Draws a three-arrow axis triad (X red, Y green, Z blue, each with a letter at the tip) out of simple shapes.
/// Used for the satellite's body axes (SatelliteAxes) and for the Earth's axes (EarthAxes).
/// The arrows are built along Unity's x, z and y for the three letters (see the mapping on each caller), glow a little so
/// they stay visible on the dark side, and the letters always face the viewer.
/// </summary>
public static class AxesGizmo
{
    /// <param name="dirs">Local direction of the X, Y and Z arrows.</param>
    /// <param name="words">The text at the three tips (X, Y, Z when null). An empty string leaves that label out.</param>
    public static GameObject Build(Transform parent, Vector3[] dirs, float length, float thickness, float labelSize, Color[] colors, string[] words = null)
    {
        var root = new GameObject("Axes");
        root.transform.SetParent(parent, false);

        string[] names = { "X", "Y", "Z" };
        Material template = FindTemplate();
        for (int i = 0; i < 3; i++)
        {
            Material m = Bright(template, colors[i]);
            var axis = new GameObject("Axis " + names[i]).transform;
            axis.SetParent(root.transform, false);
            axis.localRotation = Quaternion.FromToRotation(Vector3.up, dirs[i]);      // build along +y, then turn onto the axis

            Part(PrimitiveType.Cylinder, "Shaft", axis, new Vector3(0f, length * 0.5f, 0f),
                 new Vector3(thickness, length * 0.5f, thickness), m);                 // a cylinder is 2 units tall
            Part(PrimitiveType.Sphere, "Tip", axis, new Vector3(0f, length, 0f), Vector3.one * thickness * 3.2f, m);

            string word = words != null && i < words.Length ? words[i] : names[i];
            if (string.IsNullOrEmpty(word)) continue;                                 // no label wanted
            var label = new GameObject("Label " + names[i]);
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = dirs[i] * (length + labelSize * 0.9f);
            var t = label.AddComponent<TextMeshPro>();
            t.text = "<b>" + word + "</b>";
            t.fontSize = 2f;
            t.rectTransform.sizeDelta = new Vector2(2f, 1f);
            t.transform.localScale = Vector3.one * labelSize * 3f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = colors[i];
            t.textWrappingMode = TextWrappingModes.NoWrap;
            label.AddComponent<FaceViewer>();
        }
        return root;
    }

    /// <summary>One arrow along +y (the caller turns the returned object to point it), with a name at the tip that faces the viewer.</summary>
    public static GameObject BuildArrow(Transform parent, string objectName, float length, float thickness, float labelSize, Color color, string label)
    {
        if (string.IsNullOrEmpty(label)) labelSize = 0f;
        var root = new GameObject(objectName);
        root.transform.SetParent(parent, false);
        Material m = Bright(FindTemplate(), color);
        Part(PrimitiveType.Cylinder, "Shaft", root.transform, new Vector3(0f, length * 0.5f, 0f), new Vector3(thickness, length * 0.5f, thickness), m);
        Part(PrimitiveType.Sphere, "Tip", root.transform, new Vector3(0f, length, 0f), Vector3.one * thickness * 3.2f, m);

        if (string.IsNullOrEmpty(label)) return root;
        var lg = new GameObject("Label");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, length + labelSize * 0.9f, 0f);
        var t = lg.AddComponent<TextMeshPro>();
        t.text = "<b>" + label + "</b>";
        t.fontSize = 2f;
        t.rectTransform.sizeDelta = new Vector2(2f, 1f);
        t.transform.localScale = Vector3.one * labelSize * 3f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        lg.AddComponent<FaceViewer>();
        return root;
    }

    static Material FindTemplate()
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        return new Material(sh);
    }

    // lit materials look dark on the side facing away from the sun, so the colour is also emitted
    static Material Bright(Material template, Color c)
    {
        var m = new Material(template);
        m.color = c;
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 1.6f);
        return m;
    }

    static void Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }
}

/// <summary>Turns the object to face the main camera every frame (for the axis letters).</summary>
public class FaceViewer : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;
    }
}
