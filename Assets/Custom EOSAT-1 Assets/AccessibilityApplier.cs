using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Makes the Accessibility menu page work. Creates itself; nothing to place.
///   - Your height + Match my height: you enter how tall you are; pressing the button measures where your head is right now
///     (sitting or standing, however you play) and sets the raise/lower offset so your eyes end up at standing eye level for
///     that height. Then the raise/lower slider can fine-tune it.
///   - Raise / lower view: raises or lowers your view (seated, wheelchair, shorter or taller players) by moving the rig's
///     camera-floor-offset object. Saved as "access.height" (metres, 0 = no change).
///   - Recenter: brings the hub back around you. The rig is turned about your head so you face the way the hub started
///     facing, and moved so your head is over the hub's centre. Works in VR and in passthrough. In the space view it makes your
///     current head position and facing the centre of the view (SpaceViewController.Recenter).
/// </summary>
public class AccessibilityApplier : MonoBehaviour
{
    const string HeightKey = "access.height";

    static AccessibilityApplier instance;
    XROrigin xr;
    Transform offsetObj;
    float baseY, ours;
    bool haveBase;
    Vector3 spawnPos;
    Quaternion spawnRot = Quaternion.identity;
    bool haveSpawn;
    ViewManager vm;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("AccessibilityApplier");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AccessibilityApplier>();
    }

    bool FindRig()
    {
        if (xr == null) xr = FindAnyObjectByType<XROrigin>();
        if (xr == null || xr.Origin == null) return false;
        if (!haveSpawn)                                   // where the hub starts: the recenter target
        {
            spawnPos = xr.Origin.transform.position;
            spawnRot = xr.Origin.transform.rotation;
            haveSpawn = true;
        }
        if (offsetObj == null && xr.CameraFloorOffsetObject != null) { offsetObj = xr.CameraFloorOffsetObject.transform; haveBase = false; }
        return offsetObj != null;
    }

    void LateUpdate()
    {
        if (!FindRig()) return;

        // Keep our offset on top of whatever height the rig itself wants. If something else moved the object
        // (the XR Origin re-sets it when the tracking mode changes), that becomes the new base.
        float cur = offsetObj.localPosition.y;
        if (!haveBase || Mathf.Abs(cur - ours) > 0.0005f) { baseY = cur; haveBase = true; }

        float want = baseY + CubeSettings.GetFloat(HeightKey, 0f);
        if (Mathf.Abs(want - cur) > 0.0001f)
        {
            var p = offsetObj.localPosition;
            p.y = want;
            offsetObj.localPosition = p;
        }
        ours = want;
    }

    // ── match my height ──────────────────────────────────────────────────────
    const float EyesBelowTopOfHead = 0.12f;

    /// <summary>Button on the Accessibility page.</summary>
    public static void MatchHeightNow()
    {
        if (instance != null) instance.DoMatchHeight();
    }

    void DoMatchHeight()
    {
        if (!FindRig() || xr.Camera == null) return;

        // How high your head really is above the floor, whatever the current offset is.
        float local = offsetObj.InverseTransformPoint(xr.Camera.transform.position).y;
        bool device = xr.RequestedTrackingOriginMode == XROrigin.TrackingOriginMode.Device;
        float physical = local + (device ? baseY : 0f);

        float eyeTarget = CubeSettings.GetFloat("access.playerheight", 1.75f) - EyesBelowTopOfHead;
        float offset = Mathf.Clamp(eyeTarget - physical, -0.5f, 1f);        // the slider's range
        CubeSettings.SetFloat(HeightKey, offset);
        Debug.Log("[Accessibility] head at " + physical.ToString("0.00") + " m, wanted eyes at " + eyeTarget.ToString("0.00") + " m, offset " + offset.ToString("0.00") + " m");
    }

    // ── recenter ─────────────────────────────────────────────────────────────
    /// <summary>Button on the Accessibility page.</summary>
    public static void RecenterNow()
    {
        if (instance != null) instance.DoRecenter();
    }

    void DoRecenter()
    {
        if (!FindRig() || xr.Camera == null) return;

        if (vm == null) vm = FindAnyObjectByType<ViewManager>();
        if (vm != null && vm.Busy) return;                                               // mid-switch

        if (vm != null && vm.Current == ViewManager.View.Space)
        {
            // The space view places the rig itself, so it re-centres: your head's resting spot and facing become the view's centre.
            var space = FindAnyObjectByType<SpaceViewController>();
            if (space != null) space.Recenter();
            Debug.Log("[Accessibility] recentered (space view)");
            return;
        }

        Transform rig = xr.Origin.transform;
        Transform cam = xr.Camera.transform;
        Vector3 up = rig.up;

        Vector3 target = Vector3.ProjectOnPlane(spawnRot * Vector3.forward, up);
        Vector3 look = Vector3.ProjectOnPlane(cam.forward, up);
        if (target.sqrMagnitude < 1e-4f || look.sqrMagnitude < 1e-4f) return;

        var cc = rig.GetComponentInChildren<CharacterController>(true);
        bool ccWasOn = cc != null && cc.enabled;
        if (ccWasOn) cc.enabled = false;                    // so it doesn't fight the move

        rig.RotateAround(cam.position, up, Vector3.SignedAngle(look, target, up));            // face the hub's front, turning about your head
        rig.position += Vector3.ProjectOnPlane(spawnPos - cam.position, up);                   // head over the hub's centre

        if (ccWasOn) cc.enabled = true;
        Debug.Log("[Accessibility] recentered");
    }
}
