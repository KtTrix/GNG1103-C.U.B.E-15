using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A pointer line between two UI objects (a label and a dot on the welcome panel). It follows them when you move either one,
/// in the editor as well as at run time. Both ends must be children of the same object as this one, with anchors at the
/// bottom-left corner (how the welcome guide makes them). Bend = 0 gives a straight line; a bend gives a dashed curve.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class WelcomeLine : MonoBehaviour
{
    public RectTransform from;
    [Tooltip("Which edge of 'from' the line starts at: 1 = right edge, -1 = left edge, 0 = its centre.")]
    public int fromEdge = 1;
    public float fromGap = 6f;
    public RectTransform to;
    public Color color = new Color(0.6f, 0.65f, 0.7f, 0.9f);
    public float thickness = 2.5f;
    [Tooltip("Zero = straight line. Otherwise the line curves towards (end point + Bend) and is dashed, as if running behind something.")]
    public Vector2 bend;

    const int Curved = 28;
    string signature = "";

    void OnEnable() { Apply(); }
    void LateUpdate() { Apply(); }

    public void Apply()
    {
        if (from == null || to == null) return;
        var me = (RectTransform)transform;
        me.anchorMin = me.anchorMax = Vector2.zero; me.pivot = Vector2.zero;
        me.anchoredPosition = Vector2.zero; me.sizeDelta = Vector2.zero;

        Vector2 a = from.anchoredPosition + new Vector2(fromEdge * (from.rect.width * 0.5f + fromGap), 0f);
        Vector2 b = to.anchoredPosition;
        string sig = a + "|" + b + "|" + bend + "|" + thickness + "|" + color;
        if (sig == signature && transform.childCount > 0) return;
        signature = sig;

        int need = bend == Vector2.zero ? 1 : Curved;
        if (transform.childCount != need)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            for (int i = 0; i < need; i++)
            {
                var g = new GameObject("Segment", typeof(RectTransform), typeof(Image));
                g.transform.SetParent(transform, false);
                g.GetComponent<Image>().raycastTarget = false;
            }
        }

        Vector2 c = b + bend;
        Vector2 prev = a;
        for (int i = 0; i < need; i++)
        {
            Vector2 p = b;
            if (need > 1) { float t = (i + 1f) / need, u = 1f - t; p = u * u * a + 2f * u * t * c + t * t * b; }
            var seg = transform.GetChild(i);
            seg.gameObject.SetActive(need == 1 || i % 2 == 0);
            var rt = (RectTransform)seg;
            rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = prev;
            Vector2 d = p - prev;
            rt.sizeDelta = new Vector2(d.magnitude + (need > 1 ? 0.5f : 0f), thickness);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            seg.GetComponent<Image>().color = color;
            prev = p;
        }
    }

    static void Kill(GameObject g)
    {
        if (Application.isPlaying) Destroy(g); else DestroyImmediate(g);
    }
}
