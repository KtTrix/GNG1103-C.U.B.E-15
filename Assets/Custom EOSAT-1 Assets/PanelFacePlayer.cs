using UnityEngine;

/// <summary>
/// Keeps a floating panel upright and turned toward the player's head,
/// no matter how the controller is tilted while dragging it.
/// Put this on the root of the panel (the same object as the XRGrabInteractable).
/// Set the grab interactable's Movement Type to Instantaneous so the two don't fight.
/// </summary>
[DefaultExecutionOrder(100)] // run after the XR Interaction Toolkit has moved the panel
public class PanelFacePlayer : MonoBehaviour
{
    [Tooltip("Leave empty to use the main camera (the headset in an XR Origin).")]
    [SerializeField] Transform head;

    [Tooltip("Degrees per second the panel turns toward you. 0 = snap instantly.")]
    [SerializeField] float turnSpeed = 360f;

    [Tooltip("Set true if your panel's readable side faces -Z instead of +Z.")]
    [SerializeField] bool flip = false;

    void LateUpdate()
    {
        if (head == null)
        {
            if (Camera.main == null) return;
            head = Camera.main.transform;
        }

        // Direction from the head to the panel, flattened so the panel never tilts up/down.
        Vector3 away = transform.position - head.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) return;

        if (flip) away = -away;
        Quaternion target = Quaternion.LookRotation(away, Vector3.up);

        transform.rotation = turnSpeed <= 0f
            ? target
            : Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}