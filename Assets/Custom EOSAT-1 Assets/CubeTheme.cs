using UnityEngine;

/// <summary>
/// The look of the whole project, taken from the client's mission-control console (EOSAT-1 deck, slides 11-15):
/// near-black navy surfaces, hairline borders, tiny letter-spaced small-caps titles, and grey for "nominal".
/// Colour is only used when something deserves attention: green = locked/ok, amber = warning,
/// red = alarm, orange = the track line / highlights, blue = informational.
///
/// Every panel script reads its colours from here when its "Use Theme" box is ticked,
/// so to restyle the whole project you only change the numbers in this file.
/// </summary>
public static class CubeTheme
{
    // surfaces (darkest -> lightest)
    public static readonly Color Background = Hex(0x0E1116);   // behind everything
    public static readonly Color Panel      = Hex(0x161920);   // panel body
    public static readonly Color Card       = Hex(0x1C2029);   // boxes inside a panel
    public static readonly Color Inset      = Hex(0x0F1218);   // text-entry wells
    public static readonly Color Border     = Hex(0x2B313C);   // hairlines

    // text
    public static readonly Color Text = Hex(0xD7DDE6);         // values
    public static readonly Color Dim  = Hex(0x7D8896);         // labels, hints

    // status
    public static readonly Color Ok     = Hex(0x3DDC97);
    public static readonly Color Warn   = Hex(0xE8A21A);
    public static readonly Color Bad    = Hex(0xEF5350);
    public static readonly Color Info   = Hex(0x4C8DF6);
    public static readonly Color Accent = Hex(0xF0A020);       // orange: track line, highlights

    // buttons
    public static readonly Color Button       = Hex(0x232A36);
    public static readonly Color ButtonAccent = Hex(0x285386); // the client's blue badge

    public static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

    public static string HexString(Color c) => ColorUtility.ToHtmlStringRGB(c);

    /// <summary>A small-caps, letter-spaced panel title in the console style (TextMeshPro rich text).</summary>
    public static string Title(string text) =>
        "<cspace=0.14em><size=75%><b><color=#" + HexString(Dim) + ">" + text.ToUpperInvariant() + "</color></b></size></cspace>";
}
