using UnityEngine;

/// <summary>
/// One point of the ground track. Position is normalised: x = (lon+180)/360, y = (lat+90)/180.
/// Used by GroundTrackMap (which records the points) and UITrackLine (which draws them).
/// </summary>
public struct TrackPoint
{
    public Vector2 pos;
    public bool eclipse;        // spacecraft was in Earth's shadow here
    public bool breakBefore;    // do NOT join to the previous point (the track wrapped around the map edge)
    public double time;         // simulation time in seconds when recorded
}