using UnityEngine;

/// <summary>
/// Limits root/pelvis movement when both feet are attached.
/// Works with two FootAttachController instances.
/// </summary>
public sealed class DualFootRootLimiter : MonoBehaviour
{
    [SerializeField] private FootAttachController leftFoot;
    [SerializeField] private FootAttachController rightFoot;

    [Header("Root To Limit")]
    [SerializeField] private Transform movementRoot;

    [Header("When Both Attached")]
    [Tooltip("If true, root pose is frozen when both feet are attached.")]
    [SerializeField] private bool hardLock = true;

    [Tooltip("If hardLock=false, allow small drift radius.")]
    [SerializeField] private float softLockRadius = 0.05f;

    private bool wasLocked;
    private Vector3 lockedPosition;
    private Quaternion lockedRotation;

    private void LateUpdate()
    {
        if (movementRoot == null)
        {
            return;
        }

        bool locked = leftFoot != null && rightFoot != null && leftFoot.IsAttached && rightFoot.IsAttached;
        if (locked && !wasLocked)
        {
            lockedPosition = movementRoot.position;
            lockedRotation = movementRoot.rotation;
        }

        wasLocked = locked;
        if (!locked)
        {
            return;
        }

        if (hardLock)
        {
            movementRoot.SetPositionAndRotation(lockedPosition, lockedRotation);
            return;
        }

        Vector3 delta = movementRoot.position - lockedPosition;
        if (delta.sqrMagnitude > softLockRadius * softLockRadius)
        {
            movementRoot.position = lockedPosition + delta.normalized * softLockRadius;
        }
    }
}
