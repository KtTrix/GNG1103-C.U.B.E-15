using System.Collections;
using UnityEngine;

/// <summary>
/// Sets up the panels when the app starts: once the headset is tracking, the default layout appears
/// (position panel on the left, map on the right, with the globe in the middle).
/// The welcome panel then pops up in front of you by itself (see "Place In Front On Start" on its Grabbable Panel).
/// Creates itself; nothing to place. Turn it off with "Show Layout On Start" in the PanelSettings asset.
/// </summary>
public class PanelStartup : MonoBehaviour
{
    static PanelStartup instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("PanelStartup");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PanelStartup>();
    }

    IEnumerator Start()
    {
        yield return PanelManager.WaitForHead();
        if (PanelSettings.Get().showLayoutOnStart) PanelManager.ApplyDefaultLayout();
    }
}
