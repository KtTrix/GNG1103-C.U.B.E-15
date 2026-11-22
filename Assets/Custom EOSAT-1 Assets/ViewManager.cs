using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Switches between the hub and the space view.
///
/// The hub scene is the "core": it keeps the XR rig, the telemetry client, SpaceFrame, the hand menu
/// and any panels you want to carry over. The space view is a separate scene loaded on top of it
/// (additively).
///
/// Hub-only objects (platform, globe, ...) go under one parent object, "HubContent", which is switched off
/// while you are in space.
///
/// While you are in space this also:
///   - remembers where the rig was, and puts it back when you return,
///   - switches off every locomotion provider on the rig (walking, turning, teleport, gravity),
///     because SpaceViewController moves you between views instead.
///
/// Controls: press V on the keyboard, or Y on the LEFT controller, to toggle hub / space.
/// Later, a hand-menu button just calls ShowSpace() / ShowHub() / Toggle().
/// </summary>
public class ViewManager : MonoBehaviour
{
    public enum View { Hub, Space }

    [Tooltip("The parent of everything that should disappear in space (platform, globe, hub panels...).")]
    [SerializeField] GameObject hubContent;
    [Tooltip("Exact name of the space scene. It must be in File > Build Profiles > Scene List.")]
    [SerializeField] string spaceSceneName = "SpaceView";
    [Tooltip("Where the space world sits. Far from the hub so nothing overlaps.")]
    [SerializeField] Vector3 spaceOrigin = new Vector3(0f, 500f, 0f);

    [Header("Start")]
    [Tooltip("Go straight to the space view when the app starts (handy while testing the space view).")]
    [SerializeField] bool startInSpace = false;

    [Header("Test buttons")]
    [SerializeField] bool enableTestButtons = true;

    public View Current { get; private set; } = View.Hub;
    public bool Busy => busy;
    public event Action<View> ViewChanged;

    InputAction toggleAction;
    Scene hubScene, spaceScene;
    bool busy;

    XROrigin rig;
    Vector3 savedRigPosition;
    Quaternion savedRigRotation = Quaternion.identity;
    readonly List<Behaviour> disabledProviders = new List<Behaviour>();
    CharacterController rigBody;      // the rig's collider/mover; switched off in space so it cannot fight us

    void Awake()
    {
        hubScene = gameObject.scene;
    }

    IEnumerator Start()
    {
        if (!startInSpace) yield break;
        yield return null;                 // let the XR rig and telemetry finish starting
        yield return new WaitForSeconds(0.3f);
        ShowSpace();
    }

    void OnEnable()
    {
        if (!enableTestButtons) return;
        toggleAction = new InputAction("ToggleView", InputActionType.Button);
        ControlMap.Apply(toggleAction, "view", null, "<Keyboard>/v");           // default Y on the left controller; remappable
        toggleAction.performed += OnTogglePressed;
        toggleAction.Enable();
    }

    void OnDisable()
    {
        if (toggleAction == null) return;
        toggleAction.performed -= OnTogglePressed;
        ControlMap.Unbind(toggleAction);
        toggleAction.Disable();
        toggleAction.Dispose();
        toggleAction = null;
    }

    void OnTogglePressed(InputAction.CallbackContext ctx)
    {
        if (ControlMap.Busy || busy || PanelManager.WelcomeOpen) return;
        bool toSpace = Current == View.Hub;
        Toggle();
        HandToast.Show(toSpace ? "Space view" : "Hub", ctx.control);      // a short message above the controller you pressed
    }

    // ── public controls ──────────────────────────────────────────────────────
    public void Toggle()
    {
        if (busy) return;
        if (Current == View.Hub) ShowSpace(); else ShowHub();
    }

    public void ShowSpace()
    {
        if (!busy && Current != View.Space) StartCoroutine(GoSpace());
    }

    public void ShowHub()
    {
        if (!busy && Current != View.Hub) StartCoroutine(GoHub());
    }

    // ── switching ────────────────────────────────────────────────────────────
    IEnumerator GoSpace()
    {
        busy = true;
        PanelManager.StoreView("Hub");      // the hub's panels are put away; they come back when you return
        SaveRigAndFreeze();      // before the space scene exists, so nothing moves the rig first

        var op = SceneManager.LoadSceneAsync(spaceSceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError("[ViewManager] Could not load scene '" + spaceSceneName +
                           "'. Is it in File > Build Profiles > Scene List, spelled exactly like that?");
            RestoreRigAndUnfreeze();
            PanelManager.RestoreView("Hub", false);
            busy = false;
            yield break;
        }
        yield return op;

        spaceScene = SceneManager.GetSceneByName(spaceSceneName);
        SceneManager.SetActiveScene(spaceScene);      // so this scene's skybox and lighting are used
        if (hubContent != null) hubContent.SetActive(false);
        PlaceWorld();

        PanelManager.RestoreView("Space", true);     // whatever you had open in space last time (nothing, the first time)
        Current = View.Space;
        ViewChanged?.Invoke(Current);
        Debug.Log("[ViewManager] view = Space");
        busy = false;
    }

    IEnumerator GoHub()
    {
        busy = true;
        PanelManager.StoreView("Space");    // space's panels are put away, relative to the rig
        if (hubContent != null) hubContent.SetActive(true);
        SceneManager.SetActiveScene(hubScene);

        if (spaceScene.IsValid() && spaceScene.isLoaded)
            yield return SceneManager.UnloadSceneAsync(spaceScene);

        RestoreRigAndUnfreeze();
        PanelManager.RestoreView("Hub", false);      // the hub's panels, exactly where you left them

        Current = View.Hub;
        ViewChanged?.Invoke(Current);
        Debug.Log("[ViewManager] view = Hub");
        busy = false;
    }

    void PlaceWorld()
    {
        var world = FindAnyObjectByType<SpaceWorld>();
        if (world == null)
        {
            Debug.LogWarning("[ViewManager] No SpaceWorld found in the space scene.");
            return;
        }
        world.transform.rotation = Quaternion.identity;
        world.SetOrigin(spaceOrigin);
    }

    // ── the rig ──────────────────────────────────────────────────────────────
    static bool IsLocomotionProvider(Component c)
    {
        for (Type t = c.GetType(); t != null; t = t.BaseType)
            if (t.Name == "LocomotionProvider") return true;
        return false;
    }

    void SaveRigAndFreeze()
    {
        rig = FindAnyObjectByType<XROrigin>();
        if (rig == null)
        {
            Debug.LogWarning("[ViewManager] No XR Origin found, so the rig cannot be moved or restored.");
            return;
        }

        savedRigPosition = rig.Origin.transform.position;
        savedRigRotation = rig.Origin.transform.rotation;

        // From here until we are back in the hub, panels travel with the rig (see PanelManager.SyncRig).
        PanelManager.SyncRig();
        PanelManager.FollowRig = true;

        disabledProviders.Clear();
        foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb != null && mb.enabled && IsLocomotionProvider(mb))
            {
                mb.enabled = false;
                disabledProviders.Add(mb);
            }
        }
        rigBody = rig.GetComponentInChildren<CharacterController>(true);
        if (rigBody == null) rigBody = rig.GetComponent<CharacterController>();
        if (rigBody != null && rigBody.enabled) rigBody.enabled = false;
        else rigBody = null;

        Debug.Log("[ViewManager] switched off " + disabledProviders.Count + " locomotion provider(s) for space");
    }

    void RestoreRigAndUnfreeze()
    {
        foreach (var b in disabledProviders)
            if (b != null) b.enabled = true;
        disabledProviders.Clear();

        if (rig != null)
            rig.Origin.transform.SetPositionAndRotation(savedRigPosition, savedRigRotation);
        PanelManager.SyncRig();                  // carry the panels back with the rig
        PanelManager.FollowRig = false;          // in the hub, panels stay where they are when you walk

        if (rigBody != null) { rigBody.enabled = true; rigBody = null; }     // after the rig is back in place
    }
}
