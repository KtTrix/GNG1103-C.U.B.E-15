using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Makes the "Attach to" choice of each panel work. Creates itself; nothing to place.
///   WORLD  the panel stays where you put it (the default).
///   HEAD   lazy follow: the panel stays put while you glance around, then glides after you once you have moved or turned
///          far enough. Grab and let go to choose a new spot; it keeps that spot relative to you.
///   L HAND / R HAND   the panel is held at that controller, like a tablet.
/// The numbers (speed, dead zone) are in the PanelSettings asset.
/// </summary>
public class PanelAttachDriver : MonoBehaviour
{
    class State { public GrabbablePanel.AttachMode mode; public Vector3 lpos; public Quaternion lrot; public bool moving; }

    static PanelAttachDriver instance;
    readonly Dictionary<GrabbablePanel, State> states = new Dictionary<GrabbablePanel, State>();
    Transform handL, handR;
    float nextFindHand;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("PanelAttachDriver");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PanelAttachDriver>();
    }

    // after the tracked head and controllers have their pose for this frame, just before drawing
    void OnEnable() { Application.onBeforeRender += Tick; }
    void OnDisable() { Application.onBeforeRender -= Tick; }

    Transform Controller(bool left)
    {
        Transform have = left ? handL : handR;
        if (have != null) return have;
        if (Time.unscaledTime < nextFindHand) return null;
        nextFindHand = Time.unscaledTime + 1f;
        var xr = FindAnyObjectByType<XROrigin>();
        if (xr == null) return null;
        Transform root = xr.CameraFloorOffsetObject != null ? xr.CameraFloorOffsetObject.transform : xr.transform;
        foreach (Transform t in root)
        {
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("controller")) continue;
            if (n.Contains("left")) handL = t;
            else if (n.Contains("right")) handR = t;
        }
        return left ? handL : handR;
    }

    // The frame a panel is attached to: the head turned only around the rig's up, or the hand's full pose.
    bool Frame(GrabbablePanel.AttachMode mode, Transform head, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        if (mode == GrabbablePanel.AttachMode.Head)
        {
            Vector3 up = PanelManager.Up;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, up);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.ProjectOnPlane(head.up, up);
            if (fwd.sqrMagnitude < 0.0001f) return false;
            pos = head.position;
            rot = Quaternion.LookRotation(fwd.normalized, up);
            return true;
        }
        var h = Controller(mode == GrabbablePanel.AttachMode.HandLeft);
        if (h == null) return false;
        pos = h.position; rot = h.rotation;
        return true;
    }

    void Tick()
    {
        var head = PanelManager.Head;
        if (head == null) return;
        PanelManager.SyncRig();                      // carry first, so the smoothing below starts from the right place
        var S = PanelSettings.Get();
        float dt = Time.unscaledDeltaTime;
        var all = PanelManager.All;

        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            var mode = p.Attach;
            if (mode == GrabbablePanel.AttachMode.World || p.IsGrabbed) { states.Remove(p); continue; }   // while grabbed, forget the spot: it is re-learned on release
            if (!Frame(mode, head, out Vector3 fp, out Quaternion fr)) continue;

            Transform t = p.transform;
            if (!states.TryGetValue(p, out State st) || st.mode != mode)
            {
                Quaternion inv = Quaternion.Inverse(fr);
                states[p] = new State { mode = mode, lpos = inv * (t.position - fp), lrot = inv * t.rotation };
                continue;
            }

            Vector3 tp = fp + fr * st.lpos;
            Quaternion tr = fr * st.lrot;

            if (mode == GrabbablePanel.AttachMode.HandLeft || mode == GrabbablePanel.AttachMode.HandRight)
            {
                t.SetPositionAndRotation(tp, tr);
                continue;
            }

            // lazy follow: wait until you have moved or turned far enough, then glide to the new spot
            float err = (tp - t.position).magnitude + Quaternion.Angle(t.rotation, tr) / 90f * 0.5f;
            if (!st.moving && err > S.lazyFollowDeadZone) st.moving = true;
            if (st.moving)
            {
                float k = 1f - Mathf.Exp(-S.lazyFollowSpeed * dt);
                t.SetPositionAndRotation(Vector3.Lerp(t.position, tp, k), Quaternion.Slerp(t.rotation, tr, k));
                if (err < 0.02f) st.moving = false;
            }
        }
    }
}
