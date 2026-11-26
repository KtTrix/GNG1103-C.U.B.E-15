using UnityEngine;

/// <summary>
/// Put this on a menu page (Page_...) to give the menu its own height while that page is showing, so a page with
/// few buttons is not stretched over a tall panel (and a page with many is not squashed). The menu's width stays the same.
/// Pages without it use the menu's built height.
/// </summary>
public class MenuPageSize : MonoBehaviour
{
    [Tooltip("Height of the menu panel for this page, in canvas units (1 unit = 1 mm at full size). The built menu is 840.")]
    public float height = 840f;
}
