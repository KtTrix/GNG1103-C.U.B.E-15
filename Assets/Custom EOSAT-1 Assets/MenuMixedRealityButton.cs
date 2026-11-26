using TMPro;
using UnityEngine;

/// <summary>
/// The main menu's "Mixed reality" button: switches passthrough on and off, and shows which it is now.
/// (The controller button for this is unassigned by default; it can still be set in Options > Controls > Button mapping.)
/// Added to the button by CUBE15 > Panels > Build Menu.
/// </summary>
public class MenuMixedRealityButton : MonoBehaviour
{
    TMP_Text label;
    MixedRealityMode mr;
    string shown = "";

    void Awake() { label = GetComponentInChildren<TMP_Text>(true); }

    MixedRealityMode Find()
    {
        if (mr == null) mr = FindAnyObjectByType<MixedRealityMode>();
        return mr;
    }

    public void Toggle()
    {
        var m = Find();
        if (m != null) m.ToggleMixedReality();
    }

    void OnEnable() { shown = ""; }

    void Update()
    {
        if (label == null) return;
        var m = Find();
        string text = m == null ? "MIXED REALITY" : (m.IsMixedReality ? "MIXED REALITY: ON" : "MIXED REALITY: OFF");
        if (text == shown) return;
        shown = text;
        label.text = "<cspace=0.08em><b>" + text + "</b></cspace>";
    }
}
