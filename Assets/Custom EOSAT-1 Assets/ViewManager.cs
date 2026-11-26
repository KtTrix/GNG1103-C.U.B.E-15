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

    public static ViewManager Instance { get; private set; }

    /// <summary>The space scene's skybox, kept for the space view view cameras (they draw the space world while you are in the hub).</summary>
    public Material SpaceSkybox { get; private set; }

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
        Instance = this;
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

    // ── the space scene in the background (hub space view panels) ─────────────
    // While a Space View panel is open in the hub, the space scene stays loaded (up at spaceOrigin, on layer 31, which the
    // headset camera does not draw) so the panel's cameras can draw it. SpaceViewController is told to leave the headset alone.
    bool bgWanted;
    SpaceLayerKeeper keeper;

    bool SpaceLoaded => spaceScene.IsValid() && spaceScene.isLoaded;

    public void WantBackgroundSpace(bool want)
    {
        bgWanted = want;
        if (Current == View.Hub && !busy) StartCoroutine(RunSync());
    }

    IEnumerator RunSync()
    {
        busy = true;
        for (int i = 0; i < 3 && bgWanted != SpaceLoaded; i++)
        {
            if (bgWanted) yield return LoadBackground();
            else yield return UnloadBackground();
        }
        busy = false;
    }

    IEnumerator LoadBackground()
    {
        SpaceViewController.NextStartInactive = true;
        var op = SceneManager.LoadSceneAsync(spaceSceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            SpaceViewController.NextStartInactive = false;
            Debug.LogError("[ViewManager] Could not load scene '" + spaceSceneName + "' for the space view.");
            bgWanted = false;
            yield break;
        }
        yield return op;
        spaceScene = SceneManager.GetSceneByName(spaceSceneName);

        // Read the space scene's skybox (the active scene's settings are the live ones), then hand "active" back to the hub.
        SceneManager.SetActiveScene(spaceScene);
        SpaceSkybox = RenderSettings.skybox;
        SceneManager.SetActiveScene(hubScene);

        PlaceWorld();
        MainCameraSeesSpace(false);
        StartKeeper();
        Debug.Log("[ViewManager] space scene loaded in the background for the space view");
    }

    IEnumerator UnloadBackground()
    {
        if (!SpaceLoaded) yield break;
        MainCameraSeesSpace(false);
        if (keeper != null) { Destroy(keeper); keeper = null; }
        yield return SceneManager.UnloadSceneAsync(spaceScene);
        Debug.Log("[ViewManager] background space scene unloaded");
    }

    void StartKeeper()
    {
        if (keeper == null) keeper = gameObject.AddComponent<SpaceLayerKeeper>();
        keeper.Scene = spaceScene;
        foreach (var root in spaceScene.GetRootGameObjects()) SpaceCam.ApplyLayer(root.transform);     // at once, so the headset never sees it
    }

    /// <summary>The headset draws the space layer only while you are in the space view.</summary>
    void MainCameraSeesSpace(bool sees)
    {
        if (rig == null) rig = FindAnyObjectByType<XROrigin>();
        var cam = rig != null ? rig.Camera : Camera.main;
        if (cam == null) return;
        cam.cullingMask = sees ? (cam.cullingMask | SpaceCam.Mask) : (cam.cullingMask & ~SpaceCam.Mask);
    }

    // ── switching ────────────────────────────────────────────────────────────
    IEnumerator GoSpace()
    {
        busy = true;
        PanelManager.StoreView("Hub");      // the hub's panels are put away; they come back when you return
        SaveRigAndFreeze();      // before the space scene exists, so nothing moves the rig first

        if (!SpaceLoaded)
        {
            SpaceViewController.NextStartInactive = false;
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
        }

        SceneManager.SetActiveScene(spaceScene);      // so this scene's skybox and lighting are used
        if (SpaceSkybox == null) SpaceSkybox = RenderSettings.skybox;
        if (hubContent != null) hubContent.SetActive(false);
        PlaceWorld();
        MainCameraSeesSpace(true);
        if (keeper != null) { Destroy(keeper); keeper = null; }

        var ctl = FindAnyObjectByType<SpaceViewController>();
        if (ctl != null) ctl.SetRigControl(true);

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
        MainCameraSeesSpace(false);

        var ctl = SpaceLoaded ? FindAnyObjectByType<SpaceViewController>() : null;
        if (ctl != null) ctl.SetRigControl(false);          // the headset is no longer flown by the space view

        RestoreRigAndUnfreeze();
        PanelManager.RestoreView("Hub", false);      // the hub's panels, exactly where you left them (space view panels ask for the space scene again)

        Current = View.Hub;

        // Keep the space scene only if a space view panel is open; otherwise unload it as before.
        if (SpaceLoaded)
        {
            if (bgWanted) StartKeeper();
            else yield return UnloadBackground();
        }
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
