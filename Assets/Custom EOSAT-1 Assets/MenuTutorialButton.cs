using UnityEngine;

/// <summary>Options > Tutorial: closes the menu and replays the guided tutorial.</summary>
public class MenuTutorialButton : MonoBehaviour
{
    public void Replay() => Tutorial.Begin();
}
