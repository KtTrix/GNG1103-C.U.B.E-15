using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A button that steps through a short list of choices and remembers the pick (see CubeSettings).
/// Put it on the setting's row; fill in the Key, the default and the labels (first label = value 0, and so on).
/// </summary>
public class SettingChoice : MonoBehaviour
{
    [Tooltip("The setting's name in CubeSettings, e.g. controls.turnstyle")]
    [SerializeField] string key = "controls.turnstyle";
    [SerializeField] int defaultValue = 0;
    [SerializeField] string[] labels = { "Option A", "Option B" };
    [SerializeField] Button button;
    [SerializeField] TMP_Text labelText;

    void OnEnable()
    {
        if (button == null) return;
        button.onClick.AddListener(Next);
        CubeSettings.Changed += OnSettingChanged;
        Show();
    }

    void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(Next);
        CubeSettings.Changed -= OnSettingChanged;
    }

    int Value => Mathf.Clamp(CubeSettings.GetInt(key, CubeSettings.DefaultFor(key, defaultValue)), 0, Mathf.Max(0, labels.Length - 1));

    void Next() => CubeSettings.SetInt(key, (Value + 1) % Mathf.Max(1, labels.Length));
    void OnSettingChanged(string k) { if (k == key) Show(); }

    void Show()
    {
        if (labelText != null && labels.Length > 0)
            labelText.text = "<cspace=0.08em><b>" + labels[Value].ToUpperInvariant() + "</b></cspace>";
    }
}
