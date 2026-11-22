using UnityEngine;

/// <summary>Buttons for settings pages (Button > On Click): put the setting's name in the string box.</summary>
public class SettingsActions : MonoBehaviour
{
    public void ResetKey(string key) => CubeSettings.Reset(key);

    /// <summary>Put every button back to its default (Button mapping).</summary>
    public void ResetBindings() => ControlMap.ResetAll();

    /// <summary>Accessibility page: set the raise/lower offset from your height.</summary>
    public void MatchHeight() => AccessibilityApplier.MatchHeightNow();

    /// <summary>Accessibility page: bring the hub back around you.</summary>
    public void Recenter() => AccessibilityApplier.RecenterNow();
}
