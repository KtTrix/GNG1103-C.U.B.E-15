using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Temporary diagnostic for the "spawn facing the wrong way, then it shifts" problem. For the first 25 seconds it writes the
/// head's compass direction, the rig's direction and any headset re-centre event to the log, with a timestamp.
/// Filter the Android logcat for "[YawLog]". Delete this file when it is no longer needed. It changes nothing in the app.
/// </summary>
public class HeadYawLogger : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("HeadYawLogger");
        DontDestroyOnLoad(go);
        go.AddComponent<HeadYawLogger>();
    }

    IEnumerator Start()
    {
        Debug.Log("[YawLog] start");
        var hooked = new HashSet<XRInputSubsystem>();
        float end = Time.realtimeSinceStartup + 25f;
        float last = -1f;
        while (Time.realtimeSinceStartup < end)
        {
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs)
                if (hooked.Add(s))
                {
                    s.trackingOriginUpdated += _ => Debug.Log("[YawLog] " + Time.realtimeSinceStartup.ToString("F2") + "s  tracking origin updated (headset re-centred)");
                    s.boundaryChanged += _ => Debug.Log("[YawLog] " + Time.realtimeSinceStartup.ToString("F2") + "s  boundary changed");
                }

            float now = Time.realtimeSinceStartup;
            if (now - last >= 0.5f)
            {
                last = now;
                var xr = FindAnyObjectByType<XROrigin>();
                Transform cam = xr != null && xr.Camera != null ? xr.Camera.transform : (Camera.main != null ? Camera.main.transform : null);
                if (cam != null)
                    Debug.Log("[YawLog] " + now.ToString("F2") + "s  head yaw " + cam.eulerAngles.y.ToString("F0") +
                              "  local yaw " + cam.localEulerAngles.y.ToString("F0") +
                              "  rig yaw " + (xr != null ? xr.transform.eulerAngles.y.ToString("F0") : "none") +
                              "  head pos " + cam.position.ToString("F2"));
                else Debug.Log("[YawLog] " + now.ToString("F2") + "s  no camera yet");
            }
            yield return null;
        }
        Debug.Log("[YawLog] done");
        Destroy(gameObject);
    }
}
