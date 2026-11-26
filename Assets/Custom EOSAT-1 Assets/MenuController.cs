using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The menu panel's brain. Put it on Panel_Menu (the root, next to its Grabbable Panel, id "menu").
///
/// Pages are the objects under "Pages Root" whose names start with "Page_" (Page_Main, Page_Options ...).
/// Only one is shown at a time. Buttons switch pages with  On Click () > MenuController.ShowPage  and the page's
/// name without "Page_" (spaces ignored, any capitals), e.g.  Options  or  SatelliteData.
///
/// The menu opens when the object is switched on (it is placed in front of you and shows Page_Main) and closes when
/// it is switched off. MenuInput (created automatically) switches it with the left controller's menu button.
/// </summary>
public class MenuController : MonoBehaviour
{
    [Tooltip("The object that contains the Page_... objects (the panel's Content).")]
    [SerializeField] RectTransform pagesRoot;
    [Tooltip("The page shown whenever the menu opens.")]
    [SerializeField] string mainPage = "Main";
    [SerializeField] float openDistance = 1.1f;
    [Tooltip("How far below eye level the menu's centre opens, in metres (negative = lower). The panel is tall, so its top is above this.")]
    [SerializeField] float openHeightOffset = -0.22f;

    [Tooltip("How big the menu opens, as a fraction of its built size (1 = full size). Used until the player resizes it with the corner handles; after that their own size is kept.")]
    [Range(0.4f, 1f)] [SerializeField] float defaultScale = 0.7f;

    readonly Stack<string> history = new Stack<string>();
    string current = "";

    static string Key(string s) => s.Replace(" ", "").Replace("_", "").ToLowerInvariant();

    RectTransform canvasRt;
    float baseHeight;

    // Each page can ask for its own menu height (MenuPageSize). The top edge stays where it is; the bottom moves.
    void ApplyPageSize(Transform page, bool keepTop)
    {
        if (canvasRt == null)
        {
            var cv = GetComponentInChildren<Canvas>(true);
            if (cv == null) return;
            canvasRt = (RectTransform)cv.transform;
            baseHeight = canvasRt.rect.height;
        }
        var ps = page != null ? page.GetComponent<MenuPageSize>() : null;
        float h = ps != null ? ps.height : baseHeight;
        float cur = canvasRt.rect.height;
        if (Mathf.Abs(h - cur) < 0.5f) return;

        canvasRt.sizeDelta = new Vector2(canvasRt.sizeDelta.x, h);
        var body = canvasRt.GetComponent<BoxCollider>();      // the invisible box that lets the grip take hold anywhere
        if (body != null)
        {
            body.size = new Vector3(canvasRt.rect.width, h, body.size.z);
            body.center = new Vector3(canvasRt.rect.center.x, canvasRt.rect.center.y, 0f);
        }
        if (keepTop) transform.position -= canvasRt.up * ((h - cur) * 0.5f * canvasRt.lossyScale.y);
    }

    void OnEnable()
    {
        var gp = GetComponent<GrabbablePanel>();
        PanelManager.PlaceInFront(gp, openDistance, 0f, openHeightOffset);
        if (gp != null && !CubeSettings.Has("panel.scale." + gp.PanelId)) gp.ScaleAboutCenter(defaultScale);
        history.Clear();
        current = "";
        ShowPage(mainPage);
    }

    /// <summary>Show a page by name (for buttons).</summary>
    public void ShowPage(string pageName)
    {
        if (pagesRoot == null) return;
        if (ControlMap.BlockLeave()) return;          // unapplied button changes: warn first
        string want = Key(pageName);
        bool found = false;

        foreach (Transform child in pagesRoot)
        {
            if (!child.name.StartsWith("Page_")) continue;
            bool match = Key(child.name.Substring(5)) == want;
            if (match) found = true;
            child.gameObject.SetActive(match);
            if (match) ApplyPageSize(child, current != "");
        }

        if (!found) { Debug.LogWarning("MenuController: no page called '" + pageName + "'.", this); return; }
        if (current != "" && current != want) history.Push(current);
        current = want;
    }

    /// <summary>Go back one page (Back buttons). From the first page it closes the menu.</summary>
    public void Back()
    {
        if (ControlMap.BlockLeave()) return;
        if (history.Count == 0) { Close(); return; }
        string previous = history.Pop();
        current = "";               // so ShowPage doesn't push the page we are leaving
        ShowPage(previous);
    }

    public void Close()
    {
        if (ControlMap.BlockLeave()) return;
        gameObject.SetActive(false);
    }

    public void Quit()
    {
        if (ControlMap.BlockLeave()) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;     // stops Play mode in Unity
#else
        Application.Quit();                                   // on a Quest this returns to the Quest home
#endif
    }
}
