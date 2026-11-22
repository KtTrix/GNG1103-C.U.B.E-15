using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;

/// <summary>
/// The menu's Button mapping page. One row per action (see ControlMap) in a scrolling list.
///   - Tap a row's button, then press the controller button you want. Squeeze a trigger to cancel.
///   - Press and HOLD a row's button to unassign it.
///   - A button another action was using is taken from it (you are told which).
///   - Changes are only a draft (shown in blue) until you press APPLY. Leaving the menu with a draft asks you to press
///     APPLY first; leaving a second time throws the draft away.
/// Put it on Page_ButtonMapping. "Row Template" is a hidden example row copied once per action (Build Menu sets this up).
/// </summary>
public class ButtonMapPage : MonoBehaviour
{
    [SerializeField] RectTransform listRoot;       // the scroll area's Content
    [SerializeField] GameObject rowTemplate;
    [SerializeField] TMP_Text statusText;
    [SerializeField] Button applyButton;
    [SerializeField] float rowHeight = 64f;

    // controller buttons we listen for while capturing (the trigger is left out: it is the menu's click, and it cancels)
    static readonly string[] Names =
    {
        "primaryButton", "secondaryButton", "gripPressed",
        "thumbstickClicked", "joystickClicked", "primary2DAxisClick", "menu", "menuButton",
    };

    class Row
    {
        public ControlMap.Entry entry;
        public Button change;
        public TMP_Text label;
        public HoldButton hold;
        public string shownLabel;
        public Color shownColor;
    }

    readonly List<Row> rows = new List<Row>();
    readonly Dictionary<string, string> draft = new Dictionary<string, string>();
    string capturingId;
    float captureEnds;
    bool warned;
    Func<bool> guard;
    bool applyShownDirty;

    // ── life cycle ───────────────────────────────────────────────────────────
    void OnEnable()
    {
        draft.Clear();
        foreach (var e in ControlMap.Entries) draft[e.id] = ControlMap.Get(e.id);
        warned = false;
        Say("", CubeTheme.Warn);
        BuildRows();
        guard = TryLeave;
        ControlMap.LeaveCheck = guard;
    }

    void OnDisable()
    {
        if (ControlMap.LeaveCheck == guard) ControlMap.LeaveCheck = null;
        if (capturingId != null) { capturingId = null; ControlMap.EndCapture(); }
    }

    void Say(string s, Color c)
    {
        if (statusText == null) return;
        statusText.text = s;
        statusText.color = c;
    }

    // ── the draft ────────────────────────────────────────────────────────────
    bool IsDirty(string id) => draft[id] != ControlMap.Get(id);

    bool AnyDirty()
    {
        foreach (var e in ControlMap.Entries) if (IsDirty(e.id)) return true;
        return false;
    }

    string LabelOf(string id) => ControlMap.Find(id).label;

    /// <summary>Give an action a button in the draft, taking it from any action that already uses it.</summary>
    void Assign(string id, string path)
    {
        path = ControlMap.Normalise(id, path);
        if (draft[id] == path) { Say("", CubeTheme.Warn); return; }

        string old = draft[id];
        var taken = new List<string>();
        foreach (var o in ControlMap.Entries)
        {
            if (o.id == id || !ControlMap.Conflicts(draft[o.id], path)) continue;

            if (o.id == "menu")
            {
                // the menu button can never be left without a button, or you couldn't get back here: it takes this action's old one
                if (ControlMap.IsNone(old))
                {
                    Say("Open menu needs a button. Give it a different one first.", CubeTheme.Bad);
                    return;
                }
                draft[o.id] = ControlMap.Normalise(o.id, old);
                taken.Add(o.label + " (it now uses " + ControlMap.Friendly(draft[o.id]) + ")");
            }
            else
            {
                draft[o.id] = ControlMap.None;
                taken.Add(o.label);
            }
        }

        draft[id] = path;
        warned = false;
        if (taken.Count == 0)
            Say(ControlMap.Friendly(path) + " set for " + LabelOf(id) + ". Press APPLY to keep it.", CubeTheme.Dim);
        else
            Say(ControlMap.Friendly(path) + " was used by " + string.Join(", ", taken) + ". " + LabelOf(id) + " has it now. Press APPLY to keep this.", CubeTheme.Warn);
    }

    void Unassign(string id)
    {
        if (id == "menu")
        {
            Say("Open menu can't be unassigned, or you couldn't get back here. Press a different button instead.", CubeTheme.Bad);
            return;
        }
        if (ControlMap.IsNone(draft[id])) return;
        draft[id] = ControlMap.None;
        warned = false;
        Say(LabelOf(id) + " is now unassigned. Press APPLY to keep this.", CubeTheme.Warn);
    }

    public void RestoreDefaults()
    {
        foreach (var e in ControlMap.Entries) draft[e.id] = e.defaultPath;
        warned = false;
        Say(AnyDirty() ? "Default buttons loaded. Press APPLY to keep them." : "Buttons are already the defaults.", CubeTheme.Dim);
    }

    public void Apply()
    {
        if (capturingId != null) CancelCapture();
        var none = new List<string>();
        foreach (var e in ControlMap.Entries)
        {
            if (IsDirty(e.id)) ControlMap.Set(e.id, draft[e.id]);
            if (ControlMap.IsNone(draft[e.id])) none.Add(e.label);
        }
        warned = false;
        Say(none.Count == 0 ? "Buttons applied." : "Buttons applied. Unassigned: " + string.Join(", ", none) + ".",
            none.Count == 0 ? CubeTheme.Ok : CubeTheme.Warn);
    }

    /// <summary>Called when the player tries to leave. True = stop them (once).</summary>
    bool TryLeave()
    {
        if (capturingId != null) CancelCapture();
        if (!AnyDirty() || warned) return false;      // nothing to lose, or this is the second try: let them go (the draft is dropped)
        warned = true;
        Say("You have unapplied changes. Press APPLY to keep them. Leave again to discard them.", CubeTheme.Warn);
        return true;
    }

    // ── rows ─────────────────────────────────────────────────────────────────
    void BuildRows()
    {
        if (listRoot == null || rowTemplate == null) return;
        foreach (var r in rows) if (r.change != null) Destroy(r.change.transform.parent.gameObject);
        rows.Clear();

        listRoot.sizeDelta = new Vector2(0f, ControlMap.Entries.Length * rowHeight);
        listRoot.anchoredPosition = Vector2.zero;           // scrolled to the top

        for (int i = 0; i < ControlMap.Entries.Length; i++)
        {
            var e = ControlMap.Entries[i];
            var go = Instantiate(rowTemplate, listRoot);
            go.SetActive(true);
            go.name = "Row " + e.label;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -i * rowHeight);
            rt.sizeDelta = new Vector2(0f, rowHeight - 8f);

            var name = go.transform.Find("Name");
            if (name != null) name.GetComponent<TMP_Text>().text = e.label;

            var row = new Row { entry = e };
            var cb = go.transform.Find("Btn Change");
            row.change = cb.GetComponent<Button>();
            row.label = cb.GetComponentInChildren<TMP_Text>(true);
            row.change.onClick.RemoveAllListeners();            // taps and holds are handled by HoldButton
            row.hold = cb.gameObject.AddComponent<HoldButton>();
            row.hold.onClick = () => Begin(e);
            row.hold.onHold = () => Unassign(e.id);

            var rb = go.transform.Find("Btn Reset").GetComponent<Button>();
            rb.onClick.RemoveAllListeners();
            rb.onClick.AddListener(() => Assign(e.id, e.defaultPath));

            rows.Add(row);
        }
    }

    void RefreshRows()
    {
        foreach (var r in rows)
        {
            string id = r.entry.id;
            bool listening = capturingId == id;
            bool holding = r.hold != null && r.hold.Progress > 0.3f;

            string text = listening ? "Press a button" : holding ? "Keep holding" : ControlMap.Friendly(draft[id]);
            Color col = (listening || IsDirty(id)) ? CubeTheme.ButtonAccent : CubeTheme.Button;

            if (text != r.shownLabel)
            {
                r.shownLabel = text;
                if (r.label != null) r.label.text = "<cspace=0.06em><b>" + text.ToUpperInvariant() + "</b></cspace>";
            }
            if (col != r.shownColor) { r.shownColor = col; Tint(r.change, col); }
        }

        bool dirty = AnyDirty();
        if (applyButton != null && dirty != applyShownDirty)
        {
            applyShownDirty = dirty;
            Tint(applyButton, dirty ? CubeTheme.ButtonAccent : CubeTheme.Button);
        }
    }

    static void Tint(Button b, Color c)
    {
        var cb = b.colors;
        cb.normalColor = c; cb.selectedColor = c;
        cb.highlightedColor = Color.Lerp(c, Color.white, 0.18f);
        cb.pressedColor = Color.Lerp(c, Color.white, 0.45f);
        b.colors = cb;
    }

    // ── capturing a press ────────────────────────────────────────────────────
    void Begin(ControlMap.Entry e)
    {
        if (capturingId == e.id) { CancelCapture(); return; }
        capturingId = e.id;
        captureEnds = Time.unscaledTime + 8f;
        ControlMap.BeginCapture();
        Say("Press the controller button you want for " + e.label + ". Squeeze a trigger to cancel.", CubeTheme.Dim);
    }

    void CancelCapture()
    {
        capturingId = null;
        ControlMap.EndCapture();
        Say("Cancelled.", CubeTheme.Dim);
    }

    void Update()
    {
        if (capturingId != null) PollCapture();
        RefreshRows();
    }

    void PollCapture()
    {
        if (Time.unscaledTime > captureEnds) { CancelCapture(); return; }

        foreach (var dev in InputSystem.devices)
        {
            if (!(dev is XRController)) continue;
            string hand = null;
            foreach (var u in dev.usages)
            {
                if (u == CommonUsages.LeftHand) hand = "LeftHand";
                else if (u == CommonUsages.RightHand) hand = "RightHand";
            }
            if (hand == null) continue;

            var trig = dev.TryGetChildControl<ButtonControl>("triggerPressed");
            if (trig != null && trig.wasPressedThisFrame) { CancelCapture(); return; }

            foreach (string n in Names)
            {
                var c = dev.TryGetChildControl<ButtonControl>(n);
                if (c == null || !c.wasPressedThisFrame) continue;

                string id = capturingId;
                capturingId = null;
                ControlMap.EndCapture();
                Assign(id, ControlMap.MakePath(hand, n));
                return;
            }
        }
    }
}

/// <summary>Tap = onClick, press and hold = onHold. A drag (scrolling the list) or leaving the button cancels both.</summary>
public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public Action onClick, onHold;
    public float holdSeconds = 0.9f;
    /// <summary>0 to 1 while held.</summary>
    public float Progress { get; private set; }

    bool down, fired;
    float t0;
    PointerEventData ev;
    Vector2 p0;

    public void OnPointerDown(PointerEventData e)
    {
        down = true; fired = false; t0 = Time.unscaledTime; ev = e; p0 = e.position; Progress = 0f;
    }

    public void OnPointerUp(PointerEventData e)
    {
        bool click = down && !fired && !e.dragging;
        down = false; Progress = 0f;
        if (click) onClick?.Invoke();
    }

    public void OnPointerExit(PointerEventData e) { down = false; Progress = 0f; }
    void OnDisable() { down = false; Progress = 0f; }

    void Update()
    {
        if (!down || fired) return;
        if (ev != null && (ev.position - p0).sqrMagnitude > 900f) { down = false; Progress = 0f; return; }   // moved: this is a scroll
        Progress = (Time.unscaledTime - t0) / holdSeconds;
        if (Progress >= 1f) { fired = true; Progress = 0f; onHold?.Invoke(); }
    }
}
