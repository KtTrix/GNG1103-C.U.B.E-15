using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Shows the REAL controllers of whatever headset is connected, instead of the rig's generic grey controllers.
/// Quest 2 (Oculus Touch), Quest 3 / 3S (Touch Plus) and Quest Pro (Touch Pro) are included. Anything else keeps the generic model.
///
/// It sets itself up: it starts when the app starts, finds the rig's controller visuals, hides them and puts the matching
/// model on each hand. The models are loaded from  Assets/Resources/ControllerModels/  (quest2_left, quest2_right, ...).
/// The buttons, trigger, grip and thumbstick click move when you press them.
///
/// Controller models: immersive-web/webxr-input-profiles (MIT licence, Copyright (c) 2019 Amazon).
/// </summary>
public class ControllerModelSwitcher : MonoBehaviour
{
    [Tooltip("Leave empty to detect the headset. For testing in the editor type: quest2, quest3, questpro or generic.")]
    [SerializeField] string forceProfile = "";
    [Tooltip("With no headset connected (Play mode on the PC), show this model so you can see it. Empty = keep generic.")]
    [SerializeField] string editorFallbackProfile = "quest2";
    [SerializeField] float checkEverySeconds = 0.5f;

    [Header("Model placement (glTF models can arrive turned around)")]
    [Tooltip("Turns the model on each controller. Tuned by hand on a Quest 2: tilted 50 degrees, turned round 180.")]
    [SerializeField] Vector3 modelEulerDegrees = new Vector3(50f, 180f, 0f);
    [Tooltip("Nudges the model on each controller (metres, in the controller's own axes).")]
    [SerializeField] Vector3 modelOffset = new Vector3(0f, 0f, -0.05f);
    [Tooltip("Extra shift for each hand's model, in the controller's own axes (metres). X is MIRRORED: + moves each model toward " +
             "the other hand (left model right, right model left). Y (up) and Z (forward) move both models the same way. " +
             "Use it to close gaps or stop overlap when you touch the controllers together.")]
    [SerializeField] Vector3 handShift = new Vector3(0.005f, -0.015f, -0.005f);
    [Tooltip("Use the left model on the right hand and vice versa, if they come out swapped.")]
    [SerializeField] bool swapLeftRight = false;

    [Header("Per headset (extra to the above)")]
    [Tooltip("Quest Pro controllers: extra shift in millimetres (x = toward the other hand, y = up, z = forward). " +
             "Find the numbers with Options > Controls > Controller alignment, then type them here so every user gets them.")]
    [SerializeField] Vector3 questProShiftMm = Vector3.zero;
    [Tooltip("Quest Pro controllers: extra tilt up (+) or down (-), in degrees.")]
    [SerializeField] float questProTiltDeg = 0f;
    [Tooltip("Quest 3 / 3S controllers: extra shift in millimetres (x = toward the other hand, y = up, z = forward).")]
    [SerializeField] Vector3 quest3ShiftMm = Vector3.zero;
    [SerializeField] float quest3TiltDeg = 0f;

    [Header("Extras")]
    [Tooltip("Move the trigger, grip, buttons and thumbsticks on the model when you use them.")]
    [SerializeField] bool animateButtons = true;
    [Tooltip("Tick if a thumbstick leans the wrong way when you push it left or right.")]
    [SerializeField] bool reverseStickHorizontal = false;
    [Tooltip("Tick if a thumbstick leans the wrong way when you push it up or down. Ticked: the model's vertical tilt came out inverted.")]
    [SerializeField] bool reverseStickVertical = true;
    [Tooltip("Hides the VR template's floating labels (Move, Grab, Turn, Blink, UI Press...) so we can add our own later.")]
    [SerializeField] bool hideTemplateCallouts = true;

    const int Trig = 0, Grip = 1, Stick = 2, StickX = 3, StickY = 4, A = 5, B = 6, Parts = 7;

    class Hand
    {
        public XRNode node;
        public string word;                 // "Left" / "Right"
        public Transform anchor;            // the tracked controller transform that holds the generic model
        public readonly List<Renderer> originals = new List<Renderer>();
        public GameObject model;
        public string profile = "";
        public readonly Transform[] value = new Transform[Parts];
        public readonly Pose[] min = new Pose[Parts], max = new Pose[Parts];
    }
    readonly Hand[] hands =
    {
        new Hand { node = XRNode.LeftHand, word = "Left" },
        new Hand { node = XRNode.RightHand, word = "Right" },
    };
    float nextCheck;

    // The player's own adjustment (Options > Controls > Controller alignment), millimetres and degrees.
    Vector3 userShiftMm; float userTiltDeg, nextAdjustRead;

    void ReadAdjustment()
    {
        if (Time.unscaledTime < nextAdjustRead) return;
        nextAdjustRead = Time.unscaledTime + 0.1f;
        userShiftMm = new Vector3(CubeSettings.GetFloat("ctrl.side", 0f), CubeSettings.GetFloat("ctrl.up", 0f), CubeSettings.GetFloat("ctrl.fwd", 0f));
        userTiltDeg = CubeSettings.GetFloat("ctrl.tilt", 0f);
    }

    void ProfileExtra(Hand h, out Vector3 shiftMm, out float tiltDeg)
    {
        shiftMm = userShiftMm; tiltDeg = userTiltDeg;
        if (h.profile == "questpro") { shiftMm += questProShiftMm; tiltDeg += questProTiltDeg; }
        else if (h.profile == "quest3") { shiftMm += quest3ShiftMm; tiltDeg += quest3TiltDeg; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<ControllerModelSwitcher>() != null) return;
        var go = new GameObject("ControllerModelSwitcher");
        go.AddComponent<ControllerModelSwitcher>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + checkEverySeconds;
            foreach (var h in hands) Refresh(h);
            if (hideTemplateCallouts) HideCallouts();
        }
        // Re-apply the placement every frame, so changing the values in the inspector takes effect straight away.
        ReadAdjustment();
        foreach (var h in hands)
            if (h.model != null)
            {
                h.model.transform.localPosition = PlacementFor(h);
                h.model.transform.localRotation = RotationFor(h);
            }
        if (animateButtons) foreach (var h in hands) if (h.model != null) Animate(h);
    }

    Vector3 PlacementFor(Hand h)
    {
        // X is towards the other hand: +x for the left controller, -x for the right (controller's own axes).
        float mirror = h.word == "Left" ? 1f : -1f;
        ProfileExtra(h, out Vector3 mm, out _);
        return modelOffset + new Vector3(handShift.x * mirror, handShift.y, handShift.z)
               + new Vector3(mm.x * mirror, mm.y, mm.z) * 0.001f;
    }

    Quaternion RotationFor(Hand h)
    {
        ProfileExtra(h, out _, out float tilt);
        return Quaternion.Euler(modelEulerDegrees + new Vector3(tilt, 0f, 0f));
    }

    // ── choosing the model ───────────────────────────────────────────────────
    static string DetectProfile(InputDevice d)
    {
        if (!d.isValid) return null;
        string n = (d.name + " " + d.manufacturer).ToLowerInvariant();
        if (n.Contains("touch pro") || n.Contains("quest pro")) return "questpro";
        if (n.Contains("touch plus") || n.Contains("quest 3")) return "quest3";
        if (n.Contains("oculus") || n.Contains("touch") || n.Contains("quest") || n.Contains("meta")) return "quest2";
        return null;
    }

    void Refresh(Hand h)
    {
        string want;
        if (!string.IsNullOrEmpty(forceProfile)) want = forceProfile.ToLowerInvariant();
        else
        {
            var dev = InputDevices.GetDeviceAtXRNode(h.node);
            want = DetectProfile(dev);
            if (want == null && !dev.isValid && Application.isEditor) want = editorFallbackProfile;
        }
        if (string.IsNullOrEmpty(want) || want == "generic") want = "";

        if (h.anchor == null || h.originals.Count == 0 || h.originals[0] == null) FindOriginal(h);
        if (h.anchor == null) return;

        if (want == h.profile && (want == "" || h.model != null)) return;

        if (h.model != null) Destroy(h.model);
        h.model = null; h.profile = want;

        bool useModel = false;
        if (want != "")
        {
            string side = (h.word == "Left") != swapLeftRight ? "left" : "right";
            var prefab = Resources.Load<GameObject>("ControllerModels/" + want + "_" + side);
            if (prefab != null)
            {
                h.model = Instantiate(prefab, h.anchor);
                h.model.name = "Controller " + h.word + " (" + want + ")";
                h.model.transform.localPosition = PlacementFor(h);
                h.model.transform.localRotation = RotationFor(h);
                foreach (var c in h.model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                Bind(h);
                useModel = true;
            }
            else Debug.LogWarning("[Controllers] no model for '" + want + "' (" + h.word + "): is it in Assets/Resources/ControllerModels?");
        }
        foreach (var r in h.originals) if (r != null) r.enabled = !useModel;
        Debug.Log("[Controllers] " + h.word + " hand: " + (useModel ? want : "generic"));
    }

    // The template's floating instruction labels. They are separate objects ("Affordance Callouts Left/Right").
    void HideCallouts()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            if (t != null && t.name.StartsWith("Affordance Callout") && t.gameObject.activeSelf)
                t.gameObject.SetActive(false);
    }

    // The rig's generic controller: a model root with a ControllerAnimator (XRI starter assets) or one called UniversalController.
    void FindOriginal(Hand h)
    {
        h.originals.Clear(); h.anchor = null;
        Transform root = null;
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null || mb.GetType().Name != "ControllerAnimator") continue;
            if (HandOf(mb.transform) == h.word) { root = mb.transform; break; }
        }
        if (root == null)
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name.Contains("UniversalController") && HandOf(t) == h.word) { root = t; break; }
        if (root == null || root.parent == null) return;

        h.anchor = root.parent;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) h.originals.Add(r);
    }

    static string HandOf(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            string n = t.name;
            if (n.Contains("Left")) return "Left";
            if (n.Contains("Right")) return "Right";
        }
        return "";
    }

    // ── moving the buttons ───────────────────────────────────────────────────
    // Each moving part of the model has three helper nodes: "<name>_pressed_min", "<name>_pressed_max" and
    // "<name>_pressed_value". The part itself hangs under the "_value" node, and the standard way to animate it is to put
    // the "_value" node anywhere between the min and max nodes. (Moving the part itself was why parts went wrong before.)
    static bool Valid(Quaternion q) => !(q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f);

    void Bind(Hand h)
    {
        var all = h.model.GetComponentsInChildren<Transform>(true);
        Transform Find(string n) { foreach (var t in all) if (t.name == n) return t; return null; }
        bool left = h.word == "Left";
        string[] pre = { "xr_standard_trigger", "xr_standard_squeeze", "xr_standard_thumbstick",
                         "xr_standard_thumbstick_xaxis", "xr_standard_thumbstick_yaxis",
                         left ? "x_button" : "a_button", left ? "y_button" : "b_button" };
        for (int i = 0; i < Parts; i++)
        {
            h.value[i] = Find(pre[i] + "_pressed_value");
            var mn = Find(pre[i] + "_pressed_min"); var mx = Find(pre[i] + "_pressed_max");
            if (mn != null) h.min[i] = new Pose(mn.localPosition, mn.localRotation);
            if (mx != null) h.max[i] = new Pose(mx.localPosition, mx.localRotation);
        }
    }

    static void Set(Hand h, int i, float v)
    {
        Transform t = h.value[i];
        if (t == null || !Valid(h.min[i].rotation) || !Valid(h.max[i].rotation)) return;
        v = Mathf.Clamp01(v);
        t.localPosition = Vector3.Lerp(h.min[i].position, h.max[i].position, v);
        t.localRotation = Quaternion.Slerp(h.min[i].rotation, h.max[i].rotation, v);
    }

    void Animate(Hand h)
    {
        var d = InputDevices.GetDeviceAtXRNode(h.node);
        if (!d.isValid) return;
        d.TryGetFeatureValue(CommonUsages.trigger, out float trig);
        d.TryGetFeatureValue(CommonUsages.grip, out float grip);
        d.TryGetFeatureValue(CommonUsages.primaryButton, out bool a);
        d.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b);
        d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool click);
        d.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis);
        float signX = reverseStickHorizontal ? -1f : 1f;
        float signY = reverseStickVertical ? -1f : 1f;
        Set(h, Trig, trig);
        Set(h, Grip, grip);
        Set(h, Stick, click ? 1f : 0f);
        Set(h, StickX, (axis.x * signX + 1f) * 0.5f);       // -1..1 -> 0..1
        Set(h, StickY, (axis.y * signY + 1f) * 0.5f);
        Set(h, A, a ? 1f : 0f);
        Set(h, B, b ? 1f : 0f);
    }
}
