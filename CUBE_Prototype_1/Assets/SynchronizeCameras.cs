using UnityEngine;

public class SyncBackgroundCamera : MonoBehaviour
{
    [SerializeField] private Transform vrHeadsetCamera;

    void LateUpdate()
    {
        if (vrHeadsetCamera != null)
        {
            // Sync rotation so Earth rotates correctly with head movement
            transform.rotation = vrHeadsetCamera.rotation;
        }
    }
}