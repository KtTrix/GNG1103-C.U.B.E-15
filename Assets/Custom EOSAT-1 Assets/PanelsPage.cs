using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The menu's Panels page. Lists every panel (except the menu) with three buttons:
///   SHOW   open or close it in the current view (hub and space each keep their own set)
///   ATTACH WORLD / HEAD / L HAND / R HAND (see PanelAttachDriver)
///   PIN    a pinned panel can't be grabbed, so it can't be moved by accident
///   RESET  open it and put it back in its start place (the Reset all button below does this for every panel)
/// Put it on Page_Panels. Row Template is a hidden example row that the script copies once per panel, so the look of a
/// row can be edited in the Inspector. Build Menu sets all this up.
/// </summary>
public class PanelsPage : MonoBehaviour
{
    [SerializeField] RectTransform listRoot;
    [SerializeField] GameObject rowTemplate;
    [SerializeField] int maxRows = 6;

    readonly List<GameObject> rows = new List<GameObject>();

    void OnEnable() { PanelManager.Changed += Rebuild; Rebuild(); }
    void OnDisable() { PanelManager.Changed -= Rebuild; }

    /// <summary>For the Reset panels button.</summary>
    public void ResetPanels() => PanelManager.ResetLayout();

    void Rebuild()
    {
        if (!isActiveAndEnabled || listRoot == null || rowTemplate == null) return;

        foreach (var r in rows) if (r != null) { r.SetActive(false); Destroy(r); }
        rows.Clear();

        PanelManager.Refresh();
        var list = new List<GrabbablePanel>();
        foreach (var p in PanelManager.All) if (p != null && p.PanelId != "menu") list.Add(p);
        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));

        float h = 1f / Mathf.Max(1, maxRows);
        for (int i = 0; i < list.Count && i < maxRows; i++)
        {
            var p = list[i];
            var go = Instantiate(rowTemplate, listRoot);
            go.SetActive(true);
            go.name = "Row " + p.DisplayName;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f - (i + 1) * h + 0.008f);
            rt.anchorMax = new Vector2(1f, 1f - i * h - 0.008f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rows.Add(go);

            var name = go.transform.Find("Name");
            if (name != null) name.GetComponent<TMP_Text>().text = p.DisplayName;

            Setup(go.transform.Find("Btn Show"), p.gameObject.activeSelf ? "On" : "Off", p.gameObject.activeSelf, () => Toggle(p));
            Setup(go.transform.Find("Btn Attach"), AttachLabel(p.Attach), p.Attach != GrabbablePanel.AttachMode.World,
                  () => p.Attach = (GrabbablePanel.AttachMode)(((int)p.Attach + 1) % 4));
            Setup(go.transform.Find("Btn Pin"), p.Pinned ? "Pinned" : "Pin", p.Pinned, () => p.Pinned = !p.Pinned);
            Setup(go.transform.Find("Btn Reset"), "Reset", false, () => PanelManager.ResetPanel(p));
        }
    }

    static string AttachLabel(GrabbablePanel.AttachMode m)
    {
        switch (m)
        {
            case GrabbablePanel.AttachMode.Head: return "Head";
            case GrabbablePanel.AttachMode.HandLeft: return "L hand";
            case GrabbablePanel.AttachMode.HandRight: return "R hand";
            default: return "World";
        }
    }

    static void Toggle(GrabbablePanel p)
    {
        bool open = p.gameObject.activeSelf;
        if (!open)
        {
            // opening: it appears in front of you, to one side of the menu (alternating left and right)
            int visible = 0;
            foreach (var o in PanelManager.All) if (o != null && o != p && o.PanelId != "menu" && o.gameObject.activeSelf) visible++;
            PanelManager.PlaceInFront(p, 1.5f, visible % 2 == 0 ? -30f : 30f, -0.05f);
            p.Unlocked = false;
        }
        PanelManager.SetVisible(p.PanelId, !open);
    }

    static void Setup(Transform button, string label, bool highlighted, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        var b = button.GetComponent<Button>();
        var t = button.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = "<cspace=0.08em><b>" + label.ToUpperInvariant() + "</b></cspace>";

        Color c = highlighted ? CubeTheme.ButtonAccent : CubeTheme.Button;
        var cb = b.colors;
        cb.normalColor = c; cb.selectedColor = c;
        cb.highlightedColor = Color.Lerp(c, Color.white, 0.18f);
        cb.pressedColor = Color.Lerp(c, Color.white, 0.45f);
        b.colors = cb;

        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(action);
    }
}
