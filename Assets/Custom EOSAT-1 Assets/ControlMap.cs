using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Every button the app listens to, in one place, so the player can remap them (Options > Controls > Button mapping).
/// Each action has a default; a remapped choice is saved (CubeSettings, key "bind.<id>") and applied live.
///
/// Scripts that listen for a button call  ControlMap.Apply(action, "id", ...)  instead of adding the binding themselves,
/// and check  ControlMap.Busy  so a press that is being captured for remapping doesn't also trigger its old action.
/// </summary>
public static class ControlMap
{
    public class Entry
    {
        public string id, label, defaultPath;
        public bool bothHands;          // the button works on whichever hand (panel grabbing); the choice stores only the button
        public string[] aliases;        // other spellings of the default button, added only while it is not remapped ({HAND} = the hand)
    }

    public static readonly Entry[] Entries =
    {
        new Entry { id = "menu",        label = "Open menu",         defaultPath = "<XRController>{LeftHand}/{Menu}",
                    aliases = new[] { "<XRController>{LeftHand}/menu", "<XRController>{LeftHand}/menuButton" } },
        new Entry { id = "view",        label = "Hub / space",       defaultPath = "<XRController>{LeftHand}/secondaryButton" },
        new Entry { id = "passthrough", label = "Mixed reality",      defaultPath = None },   // unassigned by default: the main menu has a button for it
        new Entry { id = "prevview",    label = "Previous view",     defaultPath = "<XRController>{RightHand}/primaryButton" },
        new Entry { id = "nextview",    label = "Next view",         defaultPath = "<XRController>{RightHand}/secondaryButton" },
        new Entry { id = "grab",        label = "Grab a panel",      defaultPath = "<XRController>/gripPressed", bothHands = true },
        new Entry { id = "track",       label = "Panel to track",    defaultPath = "<XRController>/thumbstickClicked", bothHands = true,
                    aliases = new[] { "<XRController>{HAND}/joystickClicked" } },
    };

    // ── capture state (the Button mapping page is waiting for a press) ────────
    static bool capturing;
    static float busyUntil;

    /// <summary>True while a button is being captured (and briefly after), so other actions ignore that press.</summary>
    public static bool Busy => capturing || Time.unscaledTime < busyUntil;
    public static void BeginCapture() { capturing = true; }
    public static void EndCapture() { capturing = false; busyUntil = Time.unscaledTime + 0.4f; }

    // ── leaving the menu with unapplied changes ───────────────────────────────
    /// <summary>Set by the Button mapping page while it is open. Returns true to BLOCK the attempt to leave (first time only).</summary>
    public static Func<bool> LeaveCheck;
    public static bool BlockLeave() => LeaveCheck != null && LeaveCheck();

    /// <summary>Stored for an action that has no button.</summary>
    public const string None = "none";
    public static bool IsNone(string p) => string.IsNullOrEmpty(p) || p == None;

    // ── reading the choices ──────────────────────────────────────────────────
    public static Entry Find(string id)
    {
        foreach (var e in Entries) if (e.id == id) return e;
        return null;
    }

    /// <summary>The factory button for an action.</summary>
    public static string Default(string id)
    {
        var e = Find(id);
        return e == null ? None : e.defaultPath;
    }

    public static string Get(string id)
    {
        var e = Find(id);
        return e == null ? "" : CubeSettings.GetString("bind." + id, Default(id));
    }

    public static bool IsDefault(string id) => Get(id) == Default(id);

    static string Ctrl(string path)
    {
        int i = path.LastIndexOf('/');
        string n = (i >= 0 ? path.Substring(i + 1) : path).Trim('{', '}').ToLowerInvariant();
        switch (n)
        {
            case "menubutton": return "menu";
            case "joystickclicked":
            case "primary2daxisclick": return "thumbstickclicked";
            default: return n;
        }
    }

    static string HandOf(string path) => path.Contains("{LeftHand}") ? "Left" : path.Contains("{RightHand}") ? "Right" : "";

    /// <summary>"Left Y", "Right A", "Grip (both)" ... for showing on a button.</summary>
    public static string Friendly(string path)
    {
        if (IsNone(path)) return "Unassigned";
        string h = HandOf(path), c = Ctrl(path), name;
        switch (c)
        {
            case "primarybutton": name = h == "Left" ? "X" : h == "Right" ? "A" : "A / X"; break;
            case "secondarybutton": name = h == "Left" ? "Y" : h == "Right" ? "B" : "B / Y"; break;
            case "menu": name = "Menu"; break;
            case "grippressed": case "grip": name = "Grip"; break;
            case "triggerpressed": case "trigger": name = "Trigger"; break;
            case "thumbstickclicked": name = "Stick click"; break;
            default: name = c; break;
        }
        return h == "" ? name + " (both)" : h + " " + name;
    }

    /// <summary>A binding path for a pressed control: hand is "LeftHand", "RightHand" or null for either hand.</summary>
    public static string MakePath(string hand, string control) =>
        string.IsNullOrEmpty(hand) ? "<XRController>/" + control : "<XRController>{" + hand + "}/" + control;

    // ── changing ─────────────────────────────────────────────────────────────
    /// <summary>An action that works on either hand keeps only which button; others keep the hand too.</summary>
    public static string Normalise(string id, string path)
    {
        var e = Find(id);
        return (e != null && e.bothHands && !IsNone(path)) ? MakePath(null, Ctrl(path)) : path;
    }

    /// <summary>True if two button choices would both fire on the same physical press.</summary>
    public static bool Conflicts(string a, string b)
    {
        if (IsNone(a) || IsNone(b) || Ctrl(a) != Ctrl(b)) return false;
        string ha = HandOf(a), hb = HandOf(b);
        return ha == "" || hb == "" || ha == hb;
    }

    /// <summary>Save a choice (the Button mapping page's Apply). Use None for no button.</summary>
    public static void Set(string id, string path) => CubeSettings.SetString("bind." + id, path);

    public static void Reset(string id) => CubeSettings.Reset("bind." + id);
    public static void ResetAll() { foreach (var e in Entries) CubeSettings.Reset("bind." + e.id); }

    // ── giving an InputAction its bindings (and keeping them up to date) ─────
    class Bound { public InputAction action; public string id, hand; public string[] extra; }
    static readonly List<Bound> bound = new List<Bound>();
    static bool hooked;

    /// <summary>
    /// Give the action the button chosen for this id. hand is "LeftHand" / "RightHand" for actions that work on a given hand
    /// (panel grabbing), otherwise null. extra = bindings that are always added (keyboard keys for testing).
    /// </summary>
    public static void Apply(InputAction action, string id, string hand = null, params string[] extra)
    {
        if (!hooked) { hooked = true; CubeSettings.Changed += OnChanged; }
        bound.Add(new Bound { action = action, id = id, hand = hand, extra = extra });
        Rebuild(action, id, hand, extra);
    }

    /// <summary>Call when the action is disposed.</summary>
    public static void Unbind(InputAction action) => bound.RemoveAll(b => b.action == action);

    static void OnChanged(string key)
    {
        if (!key.StartsWith("bind.")) return;
        for (int i = bound.Count - 1; i >= 0; i--)
        {
            var b = bound[i];
            try { Rebuild(b.action, b.id, b.hand, b.extra); }
            catch (Exception) { bound.RemoveAt(i); }          // the action was disposed
        }
    }

    static void Rebuild(InputAction a, string id, string hand, string[] extra)
    {
        var e = Find(id);
        if (e == null) return;

        bool was = a.enabled;
        if (was) a.Disable();
        for (int i = a.bindings.Count - 1; i >= 0; i--) a.ChangeBinding(i).Erase();

        string cur = Get(id);
        if (!IsNone(cur))
        {
            if (e.bothHands) a.AddBinding(MakePath(hand, Ctrl(cur)));
            else a.AddBinding(cur);
        }

        if (cur == Default(id) && e.aliases != null)
            foreach (string al in e.aliases)
                a.AddBinding(al.Replace("{HAND}", "{" + hand + "}"));
        if (extra != null) foreach (string x in extra) a.AddBinding(x);

        if (was) a.Enable();
    }
}
