using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ties a UI Slider to one saved setting (see CubeSettings). Put it on the slider's row; fill in the Key and the range.
/// The slider shows the saved value when its page opens, writes every change straight back, and follows the setting if
/// something else changes it (e.g. a "restore defaults" button).
/// </summary>
public class SettingSlider : MonoBehaviour
{
    [Tooltip("The setting's name in CubeSettings, e.g. panel.weight")]
    [SerializeField] string key = "panel.weight";
    [SerializeField] float defaultValue = 0.5f;
    [SerializeField] float min = 0f;
    [SerializeField] float max = 1f;
    [Tooltip("Shown value = setting x this (e.g. 100 to show metres as centimetres).")]
    [SerializeField] float displayScale = 1f;
    [Tooltip("Snap the value to multiples of this (0 = smooth).")]
    [SerializeField] float step = 0f;
    [SerializeField] string format = "0.00";
    [SerializeField] string suffix = "";
    [SerializeField] Slider slider;
    [SerializeField] TMP_Text valueText;

    void OnEnable()
    {
        if (slider == null) return;
        slider.minValue = min;
        slider.maxValue = max;
        Load();
        slider.onValueChanged.AddListener(OnChanged);
        CubeSettings.Changed += OnSettingChanged;
    }

    void OnDisable()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(OnChanged);
        CubeSettings.Changed -= OnSettingChanged;
    }

    void Load()
    {
        float v = CubeSettings.GetFloat(key, defaultValue);
        slider.SetValueWithoutNotify(Mathf.Clamp(v, min, max));
        Show(v);
    }

    void OnChanged(float v)
    {
        if (step > 0f)
        {
            float q = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
            if (!Mathf.Approximately(q, v)) { slider.SetValueWithoutNotify(q); v = q; }
            if (CubeSettings.Has(key) && Mathf.Approximately(CubeSettings.GetFloat(key, defaultValue), v)) { Show(v); return; }
        }
        CubeSettings.SetFloat(key, v);
        Show(v);
    }

    void OnSettingChanged(string k) { if (k == key && slider != null) Load(); }

    void Show(float v)
    {
        if (valueText != null) valueText.text = (v * displayScale).ToString(format) + suffix;
    }
}
