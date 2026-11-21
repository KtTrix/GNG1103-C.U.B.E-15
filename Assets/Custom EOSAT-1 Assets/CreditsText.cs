using TMPro;
using UnityEngine;

/// <summary>
/// Shows a text file (Credits.txt) in a text object every time the page opens, so you can edit the credits
/// in Assets without touching the menu. Put this on the Credits page and fill in both fields.
/// </summary>
public class CreditsText : MonoBehaviour
{
    public TextAsset file;
    public TMP_Text target;

    void OnEnable()
    {
        if (target == null) return;
        target.text = file != null ? file.text : "(No credits file assigned.)";
    }
}
