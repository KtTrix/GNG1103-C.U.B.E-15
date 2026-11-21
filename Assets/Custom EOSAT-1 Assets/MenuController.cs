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

    readonly Stack<string> history = new Stack<string>();
    string current = "";

    static string Key(string s) => s.Replace(" ", "").Replace("_", "").ToLowerInvariant();

    void OnEnable()
    {
        PanelManager.PlaceInFront(GetComponent<GrabbablePanel>(), openDistance);
        history.Clear();
        current = "";
        ShowPage(mainPage);
    }

    /// <summary>Show a page by name (for buttons).</summary>
    public void ShowPage(string pageName)
    {
        if (pagesRoot == null) return;
        string want = Key(pageName);
        bool found = false;

        foreach (Transform child in pagesRoot)
        {
            if (!child.name.StartsWith("Page_")) continue;
            bool match = Key(child.name.Substring(5)) == want;
            if (match) found = true;
            child.gameObject.SetActive(match);
        }

        if (!found) { Debug.LogWarning("MenuController: no page called '" + pageName + "'.", this); return; }
        if (current != "" && current != want) history.Push(current);
        current = want;
    }

    /// <summary>Go back one page (Back buttons). From the first page it closes the menu.</summary>
    public void Back()
    {
        if (history.Count == 0) { Close(); return; }
        string previous = history.Pop();
        current = "";               // so ShowPage doesn't push the page we are leaving
        ShowPage(previous);
    }

    public void Close() => gameObject.SetActive(false);

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;     // stops Play mode in Unity
#else
        Application.Quit();                                   // on a Quest this returns to the Quest home
#endif
    }
}
