using System;
using UnityEngine;

/// <summary>
/// The one place every saved setting lives. Values are remembered between runs (PlayerPrefs, which also
/// works on the Quest). Menu pages (sliders, toggles) read and write through here, and anything that
/// cares about a setting listens to Changed.
///
///     float sens = CubeSettings.GetFloat("turn.snap", 45f);
///     CubeSettings.SetFloat("turn.snap", 30f);
///     CubeSettings.Changed += key => { if (key == "turn.snap") ... };
///
/// Changes are written to disk a moment after the last one (so dragging a slider doesn't hammer the Quest's storage),
/// and always when the app is paused or quits.
/// </summary>
public static class CubeSettings
{
    const string Prefix = "cube15.";

    /// <summary>Raised after any setting changes, with its key (without the prefix).</summary>
    public static event Action<string> Changed;

    public static float GetFloat(string key, float def) => PlayerPrefs.GetFloat(Prefix + key, def);
    public static int GetInt(string key, int def) => PlayerPrefs.GetInt(Prefix + key, def);
    public static bool GetBool(string key, bool def) => PlayerPrefs.GetInt(Prefix + key, def ? 1 : 0) != 0;
    public static string GetString(string key, string def) => PlayerPrefs.GetString(Prefix + key, def);

    public static void SetFloat(string key, float v) { PlayerPrefs.SetFloat(Prefix + key, v); Done(key); }
    public static void SetInt(string key, int v) { PlayerPrefs.SetInt(Prefix + key, v); Done(key); }
    public static void SetBool(string key, bool v) { PlayerPrefs.SetInt(Prefix + key, v ? 1 : 0); Done(key); }
    public static void SetString(string key, string v) { PlayerPrefs.SetString(Prefix + key, v); Done(key); }

    // Defaults that come from somewhere other than the menu row (e.g. a tick box in an asset). A row shows this when
    // the player has not picked a value yet.
    static readonly System.Collections.Generic.Dictionary<string, int> defaults = new System.Collections.Generic.Dictionary<string, int>();
    public static void SetDefault(string key, int value) { defaults[key] = value; }
    public static int DefaultFor(string key, int fallback) => defaults.TryGetValue(key, out int v) ? v : fallback;

    public static bool Has(string key) => PlayerPrefs.HasKey(Prefix + key);

    /// <summary>Forget one setting so it goes back to its default.</summary>
    public static void Reset(string key) { PlayerPrefs.DeleteKey(Prefix + key); Done(key); }

    static void Done(string key)
    {
        Saver.Mark();
        Changed?.Invoke(key);
    }

    // writes to disk shortly after the last change
    class Saver : MonoBehaviour
    {
        static Saver instance;
        float saveAt = -1f;

        public static void Mark()
        {
            if (!Application.isPlaying) { PlayerPrefs.Save(); return; }
            if (instance == null)
            {
                var go = new GameObject("CubeSettingsSaver") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Saver>();
            }
            instance.saveAt = Time.unscaledTime + 1f;
        }

        void Update()
        {
            if (saveAt >= 0f && Time.unscaledTime >= saveAt) { saveAt = -1f; PlayerPrefs.Save(); }
        }

        void OnApplicationPause(bool paused) { if (paused && saveAt >= 0f) { saveAt = -1f; PlayerPrefs.Save(); } }
        void OnApplicationQuit() { if (saveAt >= 0f) { saveAt = -1f; PlayerPrefs.Save(); } }
    }
}
