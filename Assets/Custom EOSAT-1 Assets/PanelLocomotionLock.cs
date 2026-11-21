using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Two jobs, both done by switching the template's own input actions on and off (the template's
/// ControllerInputActionManager, found by name so no package references are needed):
///
///  1. While a hand holds a panel, THAT hand's move / turn / snap-turn / teleport inputs are switched off, so its
///     thumbstick only drives the panel. The other hand keeps working normally. When the hand lets go, the
///     template restores that hand's inputs itself.
///  2. Optionally, teleport is switched off for good (the red teleport line when you push a stick forward).
/// </summary>
public static class PanelLocomotionLock
{
    enum Side { Unknown, Left, Right }

    class Entry
    {
        public MonoBehaviour manager;
        public Side side;
        public bool wasHeld;
    }

    static readonly List<Entry> entries = new List<Entry>();
    static float nextSearch;
    static bool warned;

    const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Tick(bool leftHeld, bool rightHeld, bool disableTeleport)
    {
        Refresh();

        foreach (var e in entries)
        {
            if (e.manager == null) continue;
            bool held = (e.side == Side.Left && leftHeld) || (e.side == Side.Right && rightHeld);

            if (disableTeleport) DisableTeleport(e.manager);

            if (held)
            {
                DisableAction(e.manager, "m_Move");
                DisableAction(e.manager, "m_Turn");
                DisableAction(e.manager, "m_SnapTurn");
                DisableAction(e.manager, "m_TeleportMode");
                DisableAction(e.manager, "m_TeleportModeCancel");
                e.wasHeld = true;
            }
            else if (e.wasHeld)
            {
                e.wasHeld = false;
                // Let the template put this hand's inputs back the way its settings say.
                var m = e.manager.GetType().GetMethod("UpdateLocomotionActions", Fields);
                if (m != null) m.Invoke(e.manager, null);
            }
        }
    }

    static void Refresh()
    {
        bool anyMissing = entries.Count == 0;
        foreach (var e in entries) if (e.manager == null) anyMissing = true;
        if (!anyMissing || Time.unscaledTime < nextSearch) return;
        nextSearch = Time.unscaledTime + 3f;

        entries.Clear();
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null || mb.GetType().Name != "ControllerInputActionManager") continue;
            entries.Add(new Entry { manager = mb, side = SideOf(mb.transform) });
        }

        if (entries.Count == 0 && !warned)
        {
            warned = true;
            Debug.LogWarning("[Panels] No ControllerInputActionManager found - thumbsticks will not be switched off while holding a panel.");
        }
    }

    static Side SideOf(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("left")) return Side.Left;
            if (n.Contains("right")) return Side.Right;
        }
        return Side.Unknown;
    }

    static void DisableTeleport(MonoBehaviour m)
    {
        DisableAction(m, "m_TeleportMode");
        DisableAction(m, "m_TeleportModeCancel");

        var f = m.GetType().GetField("m_TeleportInteractor", Fields);
        if (f != null && f.GetValue(m) is Component teleport && teleport != null && teleport.gameObject.activeSelf)
            teleport.gameObject.SetActive(false);
    }

    static void DisableAction(MonoBehaviour m, string field)
    {
        var f = m.GetType().GetField(field, Fields);
        if (f == null) return;
        var reference = f.GetValue(m) as InputActionReference;
        if (reference != null && reference.action != null && reference.action.enabled) reference.action.Disable();
    }
}
