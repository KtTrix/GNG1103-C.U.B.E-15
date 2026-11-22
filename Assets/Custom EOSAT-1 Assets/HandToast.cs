using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.UI;

/// <summary>
/// A short message that floats above the controller whose button you just pressed (for example "Space view", "Hub",
/// "Mixed reality", or the name of a space view), then fades away. Call HandToast.Show(text, control). Creates itself.
/// Look and timing: Assets/Resources/ToastSettings.asset.
/// </summary>
public class HandToast : MonoBehaviour
{
    public static HandToast Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("HandToast");
        DontDestroyOnLoad(go);
        go.AddComponent<HandToast>();
    }

    /// <summary>Show a message above the controller the control belongs to (right controller if that is unknown).</summary>
    public static void Show(string text, InputControl control = null)
    {
        if (Instance == null) Boot();
        if (Instance != null) Instance.Display(text, IsLeft(control));
    }

    static bool IsLeft(InputControl c)
    {
        var dev = c != null ? c.device : null;
        if (dev == null) return false;
        foreach (var u in dev.usages)
        {
            if (u == CommonUsages.LeftHand) return true;
            if (u == CommonUsages.RightHand) return false;
        }
        return false;
    }

    const float H = 120f;          // box height in canvas units
    Canvas canvas;
    CanvasGroup group;
    RectTransform box, canvasRt;
    Image border, back;
    TextMeshProUGUI label;
    float timer = 999f;
    bool left;
    Transform leftHand, rightHand;
    float nextFind;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        Application.onBeforeRender += Place;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Application.onBeforeRender -= Place;
    }

    void Build()
    {
        var c = new GameObject("Canvas", typeof(RectTransform));
        c.transform.SetParent(transform, false);
        canvas = c.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        group = c.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        canvasRt = c.GetComponent<RectTransform>();
        canvasRt.pivot = new Vector2(0.5f, 0f);

        var b = new GameObject("Border", typeof(RectTransform), typeof(Image));
        b.transform.SetParent(c.transform, false);
        border = b.GetComponent<Image>(); border.raycastTarget = false;
        box = b.GetComponent<RectTransform>();
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);

        var k = new GameObject("Back", typeof(RectTransform), typeof(Image));
        k.transform.SetParent(b.transform, false);
        back = k.GetComponent<Image>(); back.raycastTarget = false;
        var kr = k.GetComponent<RectTransform>();
        kr.anchorMin = Vector2.zero; kr.anchorMax = Vector2.one; kr.offsetMin = new Vector2(4, 4); kr.offsetMax = new Vector2(-4, -4);

        var t = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        t.transform.SetParent(b.transform, false);
        label = t.GetComponent<TextMeshProUGUI>();
        label.fontSize = 56; label.fontStyle = FontStyles.Bold; label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        var tr = t.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;

        // draw over everything, ignoring depth, so nothing in the scene shows through it
        var baseMat = Canvas.GetDefaultCanvasMaterial();
        foreach (var img in new[] { border, back })
        {
            var m = new Material(baseMat) { renderQueue = 4000 };
            m.SetInt("unity_GUIZTestMode", 8);
            img.material = m;
        }
        var fm = label.fontMaterial; fm.renderQueue = 4001; fm.SetInt("unity_GUIZTestMode", 8);

        group.alpha = 0f;
        canvas.gameObject.SetActive(false);
    }

    void Display(string text, bool isLeft)
    {
        var S = ToastSettings.Instance;
        if (!S.enabled || string.IsNullOrEmpty(text)) return;
        left = isLeft;
        label.text = text;
        border.color = S.borderColor; back.color = S.backColor; label.color = S.textColor;
        float w = Mathf.Max(240f, label.GetPreferredValues(text).x + 90f);
        box.sizeDelta = new Vector2(w, H);
        canvasRt.sizeDelta = new Vector2(w, H);
        timer = 0f;
        group.alpha = 1f;
        if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
    }

    void Update()
    {
        if (!canvas.gameObject.activeSelf) return;
        var S = ToastSettings.Instance;
        timer += Time.unscaledDeltaTime;
        float a = timer < S.holdSeconds ? 1f : 1f - (timer - S.holdSeconds) / Mathf.Max(0.01f, S.fadeSeconds);
        group.alpha = Mathf.Clamp01(a);
        if (a <= 0f) canvas.gameObject.SetActive(false);
    }

    Transform FindHand(bool isLeft, Camera cam)
    {
        var cached = isLeft ? leftHand : rightHand;
        if (cached != null) return cached;
        if (Time.unscaledTime < nextFind) return null;
        nextFind = Time.unscaledTime + 1f;
        string side = isLeft ? "left" : "right";
        Transform best = null; int bestDepth = int.MaxValue;
        foreach (var t in cam.transform.root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!n.Contains(side) || !n.Contains("controller")) continue;
            if (n.Contains("model") || n.Contains("visual") || n.Contains("ray") || n.Contains("teleport") || n.Contains("poke") || n.Contains("grab")) continue;
            int depth = 0; for (var p = t; p != null; p = p.parent) depth++;
            if (depth < bestDepth) { best = t; bestDepth = depth; }
        }
        if (isLeft) leftHand = best; else rightHand = best;
        return best;
    }

    // last thing before drawing, so it sticks to the controller while the rig moves
    void Place()
    {
        if (!canvas.gameObject.activeSelf) return;
        var cam = Camera.main;
        if (cam == null) return;
        var S = ToastSettings.Instance;
        Vector3 up = PanelManager.Up;

        Vector3 pos;
        var hand = FindHand(left, cam);
        if (hand != null) pos = hand.position + up * S.aboveControllerM;
        else
        {
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);
            float side = left ? -Mathf.Abs(S.fallbackOffset.x) : Mathf.Abs(S.fallbackOffset.x);
            pos = cam.transform.position + right * side + up * S.fallbackOffset.y + fwd * S.fallbackOffset.z;
        }

        var t = canvas.transform;
        t.position = pos;
        Vector3 away = pos - cam.transform.position;
        if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, up);
        t.localScale = Vector3.one * (S.heightMm * 0.001f / H);
    }
}
