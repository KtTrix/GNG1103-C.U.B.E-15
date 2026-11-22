using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Moves panels the way the Quest panels move. Creates itself when the app starts (nothing to place).
/// The numbers live in the PanelSettings asset (Assets/Resources/PanelSettings.asset).
///
///  - Point a controller at a panel's handle bar (it lights up) and squeeze GRIP.
///  - Panels never follow you; they stay where you let go.
///  - At first the panel follows your pointer freely across the floor plane (left, right, nearer, farther) at its
///    current height, always facing you. Push or pull your hand to bring it nearer or send it farther.
///  - Raise or lower your hand a couple of centimetres and it breaks free: height and tilt unlock, and the spot
///    you grabbed stays on your pointer.
///  - Once a panel has been freed it stays free on later grabs (it still turns to face you when you grab it).
///  - Pressing in either thumbstick while holding a panel puts it back on the floor-plane track.
///  - While holding a panel, that hand's thumbstick pushes it away or pulls it nearer, and that hand's walking, turning
///    and teleport are switched off (the other hand keeps its controls).
///  - On the track the panel turns to face the controller holding it. When it breaks free it turns square-on to the
///    pointer, then follows your hand's twist; a freed panel keeps its own tilt on later grabs.
///  - The grip works on any part of the panel, not just the handle bar.
/// </summary>
[DefaultExecutionOrder(200)]   // after the template's own scripts, so our switching-off of inputs has the last word each frame
public class PanelGrabber : MonoBehaviour
{
    class Hand
    {
        public string name;
        public InputAction aimPos, aimRot, devPos, devRot, grip, grab, stick, axis;
        public GrabbablePanel hover, held;
        public bool gripDown, free, loggedPose, restrict;
        public Vector3 startHand, startPanel, grabLocal, startDirFlat;
        public float grabDist;
        public Quaternion freeWr0, baseRot;
    }

    Hand left, right;
    XROrigin xr;
    float nextFind;
    static PanelGrabber instance;
    PanelSettings S => PanelSettings.Get();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("PanelGrabber");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PanelGrabber>();
    }

    void Awake()
    {
        left = MakeHand("LeftHand");
        right = MakeHand("RightHand");
    }

    static Hand MakeHand(string usage)
    {
        string p = "<XRController>{" + usage + "}/";
        var h = new Hand { name = usage };
        h.aimPos = new InputAction("aimPos", InputActionType.Value, p + "pointerPosition");
        h.aimRot = new InputAction("aimRot", InputActionType.Value, p + "pointerRotation");
        h.devPos = new InputAction("devPos", InputActionType.Value, p + "devicePosition");
        h.devRot = new InputAction("devRot", InputActionType.Value, p + "deviceRotation");
        h.grip = new InputAction("grip", InputActionType.Value, p + "grip");
        h.grab = new InputAction("grab", InputActionType.Button);       // used when the player has remapped the grab button
        ControlMap.Apply(h.grab, "grab", usage);
        // pressing the thumbstick in; two spellings of the same button (a path that doesn't exist is ignored)
        h.stick = new InputAction("stick", InputActionType.Button);
        ControlMap.Apply(h.stick, "track", usage);                       // default: press in the thumbstick
        h.axis = new InputAction("axis", InputActionType.Value, p + "{Primary2DAxis}");
        return h;
    }

    void OnEnable()
    {
        PanelManager.RigMoved += OnRigMoved;
        Application.onBeforeRender += PanelManager.SyncRig;      // last look before drawing, after everything has moved the rig
        foreach (var h in new[] { left, right }) { h.aimPos.Enable(); h.aimRot.Enable(); h.devPos.Enable(); h.devRot.Enable(); h.grip.Enable(); h.grab.Enable(); h.stick.Enable(); h.axis.Enable(); }
    }
    void OnDisable() { PanelManager.RigMoved -= OnRigMoved; Application.onBeforeRender -= PanelManager.SyncRig; PanelLocomotionLock.Tick(false, false, S.disableTeleport); foreach (var h in new[] { left, right }) { h.aimPos.Disable(); h.aimRot.Disable(); h.devPos.Disable(); h.devRot.Disable(); h.grip.Disable(); h.grab.Disable(); h.stick.Disable(); h.axis.Disable(); } }

    // The rig moved (space view): move every point the grabber remembers by the same amount, so a panel you are holding
    // doesn't think your hand moved, and "raise your hand to break free" isn't triggered by the rig's own motion.
    void OnRigMoved(Matrix4x4 d)
    {
        Quaternion dr = d.rotation;
        foreach (var h in new[] { left, right })
        {
            h.startHand = d.MultiplyPoint3x4(h.startHand);
            h.startPanel = d.MultiplyPoint3x4(h.startPanel);
            h.startDirFlat = dr * h.startDirFlat;
            h.freeWr0 = dr * h.freeWr0;
            h.baseRot = dr * h.baseRot;
        }
    }

    void Update()
    {
        PanelManager.SyncRig();
        var cam = PanelManager.Head;
        if (cam == null) return;
        if (xr == null && Time.unscaledTime > nextFind) { xr = FindAnyObjectByType<XROrigin>(); nextFind = Time.unscaledTime + 1f; }
        if (xr == null) return;

        // Pressing in EITHER thumbstick puts any panel you are holding back on the sliding track.
        if (left.stick.WasPressedThisFrame() || right.stick.WasPressedThisFrame())
        {
            if (left.held != null) left.restrict = true;
            if (right.held != null) right.restrict = true;
        }

        Step(left, cam);
        Step(right, cam);

        // The hand holding a panel gives up its walking / turning / teleport; the other hand keeps its controls.
        PanelLocomotionLock.Tick(left.held != null, right.held != null, S.disableTeleport);
    }

    // Controller pose in world space (aim pose, falling back to the grip pose). False if the hand isn't tracked.
    bool Pose(Hand h, out Vector3 pos, out Quaternion rot)
    {
        Vector3 p = h.aimPos.ReadValue<Vector3>();
        Quaternion r = h.aimRot.ReadValue<Quaternion>();
        if (p == Vector3.zero) p = h.devPos.ReadValue<Vector3>();
        if (r.x == 0f && r.y == 0f && r.z == 0f && r.w == 0f) r = h.devRot.ReadValue<Quaternion>();

        pos = Vector3.zero; rot = Quaternion.identity;
        if (p == Vector3.zero || (r.x == 0f && r.y == 0f && r.z == 0f && r.w == 0f)) return false;

        Transform space = xr.CameraFloorOffsetObject != null ? xr.CameraFloorOffsetObject.transform : xr.transform;
        pos = space.TransformPoint(p);
        rot = space.rotation * r;
        return true;
    }

    void Step(Hand h, Transform head)
    {
        if (!Pose(h, out Vector3 wp, out Quaternion wr))
        {
            Release(h);
            if (h.hover != null) { h.hover.SetHover(false); h.hover = null; }
            return;
        }

        if (!h.loggedPose) { h.loggedPose = true; Debug.Log("[PanelGrabber] " + h.name + " pose found at " + wp); }

        // Which button grabs: the grip squeeze (default, with the on/off levels from PanelSettings), or any button the player picked.
        float g = ControlMap.IsDefault("grab") ? h.grip.ReadValue<float>() : (h.grab.IsPressed() ? 1f : 0f);
        bool pressed = h.gripDown ? g > S.gripOff : g > S.gripOn;
        bool justPressed = pressed && !h.gripDown;
        h.gripDown = pressed;

        if (h.held == null)
        {
            GrabbablePanel hit = Pick(wp, wr * Vector3.forward, out Vector3 hitPoint, out float hitDist);
            if (hit != h.hover)
            {
                if (h.hover != null) h.hover.SetHover(false);
                h.hover = hit;
                if (hit != null) hit.SetHover(true);
            }

            if (justPressed) Debug.Log("[PanelGrabber] " + h.name + " grip pressed, pointing at " + (hit != null ? hit.PanelId + " handle" : "no handle"));
            if (justPressed && !ControlMap.Busy && hit != null && !hit.IsGrabbed && !hit.Pinned) Begin(h, hit, wp, wr, head, hitPoint, hitDist);
            return;
        }

        if (!pressed) { Release(h); return; }
        Drag(h, head, wp, wr);
    }

    GrabbablePanel Pick(Vector3 origin, Vector3 dir, out Vector3 point, out float dist)
    {
        var hits = Physics.RaycastAll(origin, dir, S.maxRayDistance, ~0, QueryTriggerInteraction.Collide);
        float best = float.MaxValue;
        GrabbablePanel found = null;
        point = origin; dist = 0f;
        foreach (var hit in hits)
        {
            var handle = hit.collider.GetComponent<PanelHandle>();
            if (handle == null || handle.panel == null) continue;
            if (hit.distance < best) { best = hit.distance; found = handle.panel; point = hit.point; dist = hit.distance; }
        }
        return found;
    }

    void Begin(Hand h, GrabbablePanel p, Vector3 handPos, Quaternion handRot, Transform head, Vector3 hitPoint, float hitDist)
    {
        h.held = p;
        p.IsGrabbed = true;
        h.startHand = handPos;
        h.startPanel = p.transform.position;

        // Remember the exact spot of the panel you are holding, and how far along your pointer it was.
        h.grabLocal = p.transform.InverseTransformPoint(hitPoint);
        h.grabDist = Mathf.Max(0.3f, hitDist);
        h.startDirFlat = FlatDir(handRot);

        // A panel that was freed before stays free; the panel still turns to face you.
        h.free = false;
        if (p.Unlocked) EnterFree(h, p.transform, handRot, head, false);
    }

    // Start following the hand from the panel's current place (no jump). When the panel breaks out of the track
    // it turns square-on to the pointer; a panel that was already free keeps its own tilt.
    void EnterFree(Hand h, Transform panel, Quaternion handRot, Transform head, bool squareToPointer)
    {
        h.free = true;
        h.freeWr0 = handRot;
        if (squareToPointer)
        {
            Vector3 dir = handRot * Vector3.forward;                       // readable side faces the controller
            Vector3 rigUp = PanelManager.Up;
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, rigUp)) > 0.98f ? Vector3.ProjectOnPlane(Vector3.forward, rigUp).normalized : rigUp;
            h.baseRot = Quaternion.LookRotation(dir, up);
        }
        else h.baseRot = panel.rotation;
        h.held.Unlocked = true;
    }

    void Release(Hand h)
    {
        if (h.held == null) return;
        var p = h.held;
        h.held = null;
        h.free = false;
        h.restrict = false;
        if (p != null) { p.IsGrabbed = false; p.RaiseReleased(); }
    }

    float StickY(Hand h)
    {
        float y = h.axis.ReadValue<Vector2>().y;
        return Mathf.Abs(y) < S.stickDeadZone ? 0f : y;
    }

    static Vector3 FlatDir(Quaternion rot)
    {
        Vector3 up = PanelManager.Up;
        Vector3 d = Vector3.ProjectOnPlane(rot * Vector3.forward, up);
        if (d.sqrMagnitude < 0.0001f) d = Vector3.ProjectOnPlane(Vector3.forward, up);
        return d.normalized;
    }

    // Panel's readable side points away from the viewer, level with the floor.
    static Quaternion Face(Vector3 panelPos, Transform head) => FaceFrom(panelPos, head.position, head.forward);

    // Same, but turned toward any point (here: the controller holding the panel). Level with the rig's own floor.
    static Quaternion FaceFrom(Vector3 panelPos, Vector3 from, Vector3 fallback)
    {
        Vector3 up = PanelManager.Up;
        Vector3 away = Vector3.ProjectOnPlane(panelPos - from, up);
        if (away.sqrMagnitude < 0.0001f) away = Vector3.ProjectOnPlane(fallback, up);
        if (away.sqrMagnitude < 0.0001f) away = Vector3.ProjectOnPlane(Vector3.forward, up);
        return Quaternion.LookRotation(away.normalized, up);
    }

    void Drag(Hand h, Transform head, Vector3 wp, Quaternion wr)
    {
        var p = h.held.transform;
        float dt = Time.unscaledDeltaTime;

        // The thumbstick of the hand holding the panel, forward/back, pushes it away or pulls it nearer.
        float sy = StickY(h);
        if (Mathf.Abs(sy) > 0f) h.grabDist = Mathf.Clamp(h.grabDist + sy * S.stickPushSpeed * dt, 0.4f, 15f);

        // How far out along the pointer the held spot is: the distance when you grabbed, plus any push or pull of your hand.
        float dist() => Mathf.Max(0.3f, h.grabDist + Vector3.Dot(wp - h.startHand, h.startDirFlat) * S.slideGain);

        if (h.restrict)
        {
            // Back onto the floor-plane track, starting from where the panel and your hand are right now.
            h.grabDist = dist();
            h.restrict = false;
            h.free = false;
            h.held.Unlocked = false;
            h.startHand = wp;
            h.startPanel = p.position;
            h.startDirFlat = FlatDir(wr);
        }

        // Only raising or lowering the hand frees the panel's height and tilt.
        Vector3 rigUp2 = PanelManager.Up;
        if (!h.free && Mathf.Abs(Vector3.Dot(wp - h.startHand, rigUp2)) > S.BreakFree)
            EnterFree(h, p, wr, head, true);

        Vector3 tip = wp + (wr * Vector3.forward) * dist();      // the spot you are holding goes here
        float snap = 1f - Mathf.Exp(-S.snapSpeed / S.Heft * dt);

        if (h.free)
        {
            Quaternion rot = S.faceUserWhileFree
                ? Face(tip, head)
                : (wr * Quaternion.Inverse(h.freeWr0)) * h.baseRot;

            Vector3 pos = tip - rot * Vector3.Scale(h.grabLocal, p.lossyScale);
            p.position = Vector3.Lerp(p.position, pos, snap);
            p.rotation = Quaternion.Slerp(p.rotation, rot, 1f - Mathf.Exp(-S.faceUserSpeed * 2f / S.Heft * dt));
            return;
        }

        // Track: the held spot follows your pointer anywhere on the floor plane; the height stays where it was.
        Quaternion face = FaceFrom(p.position, wp, head.forward);
        Vector3 off = face * Vector3.Scale(h.grabLocal, p.lossyScale);
        // the height along the rig's up stays where it was; everything else follows the pointer
        Vector3 want = tip - off;
        Vector3 target = want - rigUp2 * Vector3.Dot(want, rigUp2) + rigUp2 * Vector3.Dot(h.startPanel, rigUp2);

        Vector3 toPanel = Vector3.ProjectOnPlane(target - head.position, rigUp2);
        if (toPanel.magnitude < S.minDistanceFromHead)
        {
            Vector3 dir = toPanel.sqrMagnitude < 0.0001f ? Vector3.ProjectOnPlane(head.forward, rigUp2).normalized : toPanel.normalized;
            Vector3 headLevel = head.position - rigUp2 * Vector3.Dot(head.position, rigUp2) + rigUp2 * Vector3.Dot(target, rigUp2);
            target = headLevel + dir * S.minDistanceFromHead;
        }

        p.position = Vector3.Lerp(p.position, target, snap);
        p.rotation = Quaternion.Slerp(p.rotation, FaceFrom(target, wp, head.forward), 1f - Mathf.Exp(-S.faceUserSpeed / S.Heft * dt));
    }
}
