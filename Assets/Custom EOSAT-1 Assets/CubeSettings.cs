using System;
using UnityEngine;

/// <summary>
/// The one place every saved setting lives. Values are remembered between runs (PlayerPrefs, which also
/// works on the Quest). Later menu pages (sliders, toggles) read and write through here, and anything that
/// cares about a setting listens to Changed.
///
///     float sens = CubeSettings.GetFloat("turn.snap", 45f);
///     CubeSettings.SetFloat("turn.snap", 30f);
///     CubeSettings.Changed += key => { if (key == "turn.snap") ... };
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

    public static bool Has(string key) => PlayerPrefs.HasKey(Prefix + key);

    /// <summary>Forget one setting so it goes back to its default.</summary>
    public static void Reset(string key) { PlayerPrefs.DeleteKey(Prefix + key); Done(key); }

    static void Done(string key)
    {
        PlayerPrefs.Save();
        Changed?.Invoke(key);
    }
}
