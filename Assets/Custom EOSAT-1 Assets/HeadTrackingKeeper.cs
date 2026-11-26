using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Keeps the headset camera moving when the Meta button is pressed.
/// While the system menu is open the app loses input focus and the headset reports its tracking as "not tracked" for a moment.
/// The camera's Tracked Pose Driver then stops applying the head's pose, so the camera freezes and the whole sim looks stuck to
/// your head. This switches on the driver's "Ignore Tracking State" option on the head camera so it keeps following the head.
/// Creates itself when the app starts; nothing to place. (Done through reflection so it compiles whichever version of the driver is in use.)
/// </summary>
public class HeadTrackingKeeper : MonoBehaviour
{
    static HeadTrackingKeeper instance;
    bool logged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("HeadTrackingKeeper");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HeadTrackingKeeper>();
    }

    IEnumerator Start()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            Apply();
            yield return wait;
        }
    }

    void OnApplicationFocus(bool focus) { if (focus) Apply(); }

    void Apply()
    {
        var head = PanelManager.Head;
        if (head == null) return;
        foreach (var b in head.GetComponents<Behaviour>())
        {
            if (b == null || b.GetType().Name != "TrackedPoseDriver") continue;
            var t = b.GetType();
            var prop = t.GetProperty("ignoreTrackingState", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite) { if (!(bool)prop.GetValue(b)) prop.SetValue(b, true); Report(); continue; }
            var field = t.GetField("m_IgnoreTrackingState", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) { field.SetValue(b, true); Report(); }
        }
    }

    void Report()
    {
        if (logged) return;
        logged = true;
        Debug.Log("HeadTrackingKeeper: the head camera now ignores the tracking state (keeps following your head when the Meta menu opens).");
    }
}
