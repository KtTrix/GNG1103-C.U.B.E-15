using UnityEngine;

/// <summary>The three buttons on the welcome panel (wired up by CUBE15 > Panels > New Welcome Panel).</summary>
public class WelcomeButtons : MonoBehaviour
{
    /// <summary>Blue button: close the welcome panel and begin the guided tutorial.</summary>
    public void StartTutorial() => Tutorial.Begin();

    /// <summary>Grey button: close the welcome panel for now. It comes back next launch.</summary>
    public void Skip() => PanelManager.SetVisible("welcome", false);

    /// <summary>Close it and never show it at launch again (Options > Tutorial still replays the tutorial).</summary>
    public void SkipForever()
    {
        CubeSettings.SetBool(Tutorial.SkipKey, true);
        CubeSettings.SetBool(Tutorial.AutoKey, false);      // "never" wins over "every launch"
        PanelManager.SetVisible("welcome", false);
    }
}
