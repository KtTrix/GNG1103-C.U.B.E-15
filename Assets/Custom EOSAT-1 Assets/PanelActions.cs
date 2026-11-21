using UnityEngine;

/// <summary>
/// Public actions for buttons to call from the Inspector (Button -> On Click ()).
/// Put this on any object (the panel root is fine) and drag it into the button's On Click list.
/// The string versions take the panel's id, e.g. "welcome", "map", "position".
/// </summary>
public class PanelActions : MonoBehaviour
{
    public void ShowPanel(string id) => PanelManager.SetVisible(id, true);
    public void HidePanel(string id) => PanelManager.SetVisible(id, false);
    public void TogglePanel(string id) => PanelManager.Toggle(id);

    /// <summary>The default hub layout: every panel marked "In Default Layout" (plus position left / map right).</summary>
    public void ApplyDefaultLayout() => PanelManager.ApplyDefaultLayout();

    /// <summary>Bring a panel back in front of the player.</summary>
    public void BringToMe(string id) => PanelManager.PlaceInFront(PanelManager.Get(id));
}
