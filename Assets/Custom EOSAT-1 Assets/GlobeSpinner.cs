using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Lets the player grab the globe and spin it around the world vertical axis.
/// Put this on the same GameObject as an XRSimpleInteractable (and a collider).
/// The GameObject should be the pivot: rotating it rotates all its children
/// (globe model now, satellite/orbit later).
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class GlobeSpinner : MonoBehaviour
{
    [Tooltip("How quickly the globe slows down after release. Higher = stops sooner.")]
    [SerializeField] float friction = 1.5f;

    [Tooltip("How much to smooth the measured spin speed (0 = very smooth, 1 = raw).")]
    [Range(0.05f, 1f)]
    [SerializeField] float velocitySmoothing = 0.3f;

    [Tooltip("Optional constant idle rotation in degrees/second (Earth's slow spin). 0 = off.")]
    [SerializeField] float idleSpin = 0f;

    XRSimpleInteractable interactable;
    IXRSelectInteractor currentInteractor;
    float lastAngle;
    float angularVelocity; // degrees per second

    void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
    }

    void OnEnable()
    {
        interactable.selectEntered.AddListener(OnGrab);
        interactable.selectExited.AddListener(OnRelease);
    }

    void OnDisable()
    {
        interactable.selectEntered.RemoveListener(OnGrab);
        interactable.selectExited.RemoveListener(OnRelease);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        currentInteractor = args.interactorObject;
        lastAngle = GetHandAngle();
        angularVelocity = 0f;
    }

    void OnRelease(SelectExitEventArgs args)
    {
        // Only clear if the interactor that let go is the one we're tracking.
        if (args.interactorObject == currentInteractor)
            currentInteractor = null;
    }

    /// <summary>
    /// Angle (degrees) of the "hand" around the world vertical axis, measured from the globe's centre.
    /// For a ray interactor we use where the ray crosses the horizontal plane through the globe's centre,
    /// so pointing from a distance works. For direct/poke interactors we use the hand position.
    /// </summary>
    float GetHandAngle()
    {
        Transform t = currentInteractor.transform;
        Vector3 point = t.position;

        if (currentInteractor is XRRayInteractor)
        {
            var plane = new Plane(Vector3.up, transform.position);
            var ray = new Ray(t.position, t.forward);
            if (plane.Raycast(ray, out float distance))
                point = ray.GetPoint(distance);
        }

        Vector3 offset = point - transform.position;
        offset.y = 0f; // flatten onto the horizontal plane
        if (offset.sqrMagnitude < 0.0001f) return lastAngle; // hand directly above/below the axis

        // Atan2(x, z) gives the same turning direction as a +Y rotation in Unity.
        return Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (currentInteractor != null)
        {
            float angle = GetHandAngle();
            // DeltaAngle handles the wrap from 179 to -179 correctly.
            float delta = Mathf.DeltaAngle(lastAngle, angle);
            lastAngle = angle;

            transform.Rotate(Vector3.up, delta, Space.World);

            float instantVelocity = delta / dt;
            angularVelocity = Mathf.Lerp(angularVelocity, instantVelocity, velocitySmoothing);
        }
        else
        {
            // Coast: exponential decay is frame-rate independent.
            angularVelocity *= Mathf.Exp(-friction * dt);
            if (Mathf.Abs(angularVelocity) < 0.05f) angularVelocity = 0f;

            transform.Rotate(Vector3.up, (angularVelocity + idleSpin) * dt, Space.World);
        }
    }
}