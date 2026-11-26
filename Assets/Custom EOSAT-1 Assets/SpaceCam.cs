using System;
using UnityEngine;

/// <summary>
/// Shared by everything that draws the space world from more than one camera.
///
/// The space scene's objects are all put on layer 31 (the "space layer"). In the hub the headset camera does not draw that
/// layer, so the space world hanging above the hub is invisible; the Space View panel's view cameras draw ONLY that layer
/// into a texture.
///
/// Things in the space scene that size themselves from the camera (satellite axes, sun arrow, beacon dot, Sun disc ...) ask
/// SpaceCam.Current instead of Camera.main. Just before a view camera draws, Override is set to it and PreRender is raised, so
/// each of those things re-sizes itself for THAT camera.
/// </summary>
public static class SpaceCam
{
    public const int Layer = 31;
    public const int Mask = 1 << 31;

    /// <summary>The space view view camera that is about to draw (null the rest of the time).</summary>
    public static Camera Override;

    /// <summary>The camera that things should size themselves for.</summary>
    public static Camera Current => Override != null ? Override : Camera.main;

    /// <summary>Raised right before a space view view camera draws.</summary>
    public static event Action PreRender;

    public static void RaisePreRender(Camera cam)
    {
        Override = cam;
        PreRender?.Invoke();
    }

    public static void EndRender() { Override = null; }

    /// <summary>Put an object and everything under it on the space layer. Returns how many objects changed.</summary>
    public static int ApplyLayer(Transform root)
    {
        int n = 0;
        if (root.gameObject.layer != Layer) { root.gameObject.layer = Layer; n++; }
        for (int i = 0; i < root.childCount; i++) n += ApplyLayer(root.GetChild(i));
        return n;
    }
}

/// <summary>
/// Keeps every object of the loaded space scene on the space layer, including the ones the scene creates later while it runs
/// (satellite parts, arrows, trails). Added by ViewManager while the space scene is loaded.
/// </summary>
public class SpaceLayerKeeper : MonoBehaviour
{
    public UnityEngine.SceneManagement.Scene Scene;
    float next;

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + (Time.frameCount < 600 ? 0.25f : 1.5f);
        if (!Scene.IsValid() || !Scene.isLoaded) return;
        foreach (var root in Scene.GetRootGameObjects()) SpaceCam.ApplyLayer(root.transform);
    }
}
