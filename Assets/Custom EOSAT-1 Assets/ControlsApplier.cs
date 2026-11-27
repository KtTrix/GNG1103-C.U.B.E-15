using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Makes the Controls menu page work: it pushes the saved settings into the template's locomotion components
/// (snap turn angle, smooth turn speed, move speed, and which of snap / smooth turning is switched on).
/// Creates itself; nothing to place. A setting that has never been changed leaves the template's own value alone, and
/// "Restore defaults" puts the template's original values back.
///
/// It finds the components by type name (SnapTurnProvider, ContinuousTurnProvider, ContinuousMoveProvider) and writes
/// their public setting by reflection, so it doesn't depend on exact XR Interaction Toolkit versions.
/// </summary>
public class ControlsApplier : MonoBehaviour
{
    static ControlsApplier instance;
    ViewManager vm;
    readonly Dictionary<(MonoBehaviour, string), float> originals = new Dictionary<(MonoBehaviour, string), float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("ControlsApplier");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ControlsApplier>();
    }

    void OnEnable() { CubeSettings.Changed += OnChanged; }
    void OnDisable() { CubeSettings.Changed -= OnChanged; }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);                 // let the rig finish loading
        Apply();
        while ((vm = FindAnyObjectByType<ViewManager>()) == null) yield return new WaitForSeconds(1f);
        vm.ViewChanged += v => { if (v == ViewManager.View.Hub) Apply(); };   // ViewManager re-enables what it switched off; apply again after
    }

    void OnChanged(string key) { if (key.StartsWith("controls.")) Apply(); }

    void Apply()
    {
        ApplyNumber("SnapTurnProvider", new[] { "turnAmount", "m_TurnAmount" }, "controls.snapangle");
        ApplyNumber("ContinuousTurnProvider", new[] { "turnSpeed", "m_TurnSpeed" }, "controls.smoothspeed");
        ApplyNumber("ContinuousMoveProvider", new[] { "moveSpeed", "m_MoveSpeed" }, "controls.movespeed");

        // In space the locomotion is switched off on purpose (ViewManager), so only change what is switched on in the hub.
        bool inSpace = vm != null && vm.Current == ViewManager.View.Space;
        if (!inSpace) ApplyStyle();
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    static List<MonoBehaviour> Find(string typeName)
    {
        var list = new List<MonoBehaviour>();
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null) continue;
            for (Type t = mb.GetType(); t != null; t = t.BaseType)
                if (t.Name == typeName) { list.Add(mb); break; }
        }
        return list;
    }

    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static bool TryGet(object o, string[] names, out MemberInfo member, out float value)
    {
        foreach (string n in names)
        {
            var p = o.GetType().GetProperty(n, Flags);
            if (p != null && p.CanRead && p.CanWrite && p.PropertyType == typeof(float)) { member = p; value = (float)p.GetValue(o); return true; }
            var f = o.GetType().GetField(n, Flags);
            if (f != null && f.FieldType == typeof(float)) { member = f; value = (float)f.GetValue(o); return true; }
        }
        member = null; value = 0f;
        return false;
    }

    static void Set(object o, MemberInfo m, float v)
    {
        if (m is PropertyInfo p) p.SetValue(o, v);
        else if (m is FieldInfo f) f.SetValue(o, v);
    }

    void ApplyNumber(string typeName, string[] memberNames, string settingKey)
    {
        foreach (var mb in Find(typeName))
        {
            if (!TryGet(mb, memberNames, out MemberInfo m, out float current)) continue;
            var id = (mb, settingKey);
            if (!originals.ContainsKey(id)) originals[id] = current;       // the template's own value, kept for "Restore defaults"
            float want = CubeSettings.Has(settingKey) ? CubeSettings.GetFloat(settingKey, current) : originals[id];
            if (!Mathf.Approximately(current, want)) Set(mb, m, want);
        }
    }

    // The starter rig keeps BOTH turn providers on. Which one answers the stick is decided by the "smooth turn" switch on each
    // controller's input manager (ControllerInputActionManager), so that switch is what the Turning setting changes.
    static bool TryGetSmooth(object o, out bool value)
    {
        var p = o.GetType().GetProperty("smoothTurnEnabled", Flags);
        if (p != null && p.CanRead && p.PropertyType == typeof(bool)) { value = (bool)p.GetValue(o); return true; }
        var f = o.GetType().GetField("m_SmoothTurnEnabled", Flags);
        if (f != null && f.FieldType == typeof(bool)) { value = (bool)f.GetValue(o); return true; }
        value = false;
        return false;
    }

    static void SetSmooth(object o, bool want)
    {
        if (TryGetSmooth(o, out bool now) && now == want) return;
        var p = o.GetType().GetProperty("smoothTurnEnabled", Flags);
        if (p != null && p.CanWrite) { p.SetValue(o, want); return; }          // the property's setter refreshes the actions itself
        var f = o.GetType().GetField("m_SmoothTurnEnabled", Flags);
        if (f != null) f.SetValue(o, want);
        o.GetType().GetMethod("UpdateLocomotionActions", Flags)?.Invoke(o, null);
    }

    // "Pull the right stick back to turn around" belongs to snap turning only. The snap turn provider has a switch for it
    // (enableTurnAround); with smooth turning it is turned off, and with snap turning it goes back to the rig's own setting.
    readonly Dictionary<MonoBehaviour, bool> turnAroundOriginal = new Dictionary<MonoBehaviour, bool>();

    void ApplyTurnAround(bool smooth)
    {
        foreach (var mb in Find("SnapTurnProvider"))
        {
            var t = mb.GetType();
            PropertyInfo p = null; FieldInfo f = null;
            foreach (Type tt in new[] { t, t.BaseType })
            {
                if (tt == null) continue;
                p = tt.GetProperty("enableTurnAround", Flags);
                f = tt.GetField("m_EnableTurnAround", Flags);
                if (p != null || f != null) break;
            }
            bool cur;
            if (p != null && p.CanRead && p.CanWrite && p.PropertyType == typeof(bool)) cur = (bool)p.GetValue(mb);
            else if (f != null && f.FieldType == typeof(bool)) cur = (bool)f.GetValue(mb);
            else continue;

            if (!turnAroundOriginal.ContainsKey(mb)) turnAroundOriginal[mb] = cur;
            bool want = smooth ? false : turnAroundOriginal[mb];
            if (cur == want) continue;
            if (p != null && p.CanWrite && p.PropertyType == typeof(bool)) p.SetValue(mb, want);
            else f.SetValue(mb, want);
        }
    }

    void ApplyStyle()
    {
        var managers = Find("ControllerInputActionManager");
        if (managers.Count > 0)
        {
            if (!CubeSettings.Has("controls.turnstyle"))
            {
                bool anySmooth = false;
                foreach (var m in managers) if (TryGetSmooth(m, out bool v)) anySmooth |= v;
                CubeSettings.SetInt("controls.turnstyle", anySmooth ? 1 : 0);     // first run: show what the rig really uses
                return;
            }
            bool smooth = CubeSettings.GetInt("controls.turnstyle", 0) == 1;
            foreach (var m in managers) SetSmooth(m, smooth);
            ApplyTurnAround(smooth);
            return;
        }

        // no input managers found: fall back to switching the providers themselves
        var snaps = Find("SnapTurnProvider");
        var smooths = Find("ContinuousTurnProvider");
        if (snaps.Count == 0 && smooths.Count == 0) return;

        if (!CubeSettings.Has("controls.turnstyle"))
        {
            // first run: learn what the template uses, so the menu shows the truth
            bool anySmooth = false, anySnap = false;
            foreach (var s in smooths) anySmooth |= s.enabled;
            foreach (var s in snaps) anySnap |= s.enabled;
            CubeSettings.SetInt("controls.turnstyle", anySmooth && !anySnap ? 1 : 0);   // fires Changed, which applies again
            return;
        }

        int style = CubeSettings.GetInt("controls.turnstyle", 0);      // 0 = snap, 1 = smooth
        foreach (var s in snaps) if (s.enabled != (style == 0)) s.enabled = style == 0;
        foreach (var s in smooths) if (s.enabled != (style == 1)) s.enabled = style == 1;
        ApplyTurnAround(style == 1);
    }
}
