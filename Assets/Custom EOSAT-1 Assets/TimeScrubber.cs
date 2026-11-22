using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Goes on the time bar's slider object. Tells the Timeline when you take hold of the bar and when you let go, so warnings
/// stay quiet while you drag and the bar snaps back to live if you let go at the front.
/// </summary>
public class TimeScrubber : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public void OnPointerDown(PointerEventData eventData) { Timeline.BeginScrub(); }
    public void OnPointerUp(PointerEventData eventData) { Timeline.EndScrub(); }
}
