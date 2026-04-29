using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Attaches a foot to ground by freezing the IK target pose in world space.
/// Does NOT move the foot transform directly.
/// </summary>
public sealed class FootAttachController : MonoBehaviour
{
    [Header("IK")]
    [SerializeField] private TwoBoneIKConstraint twoBoneIK;
    [SerializeField] private Transform foot;
    [SerializeField] private Transform ikTarget;

    [Header("Ground Detection")]
    [SerializeField] private Transform bottomProbe;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float probeStartOffset = 0.05f;
    [SerializeField] private float probeRadius = 0.06f;
    [SerializeField] private float probeDistance = 0.18f;

    [Header("Attach")]
    [SerializeField] private bool autoAttachOnGround = true;
    [SerializeField] private bool keepFootRotationOnAttach = true;
    [SerializeField] private float attachWeight = 1f;
    [SerializeField] private float detachWeight = 0f;
    [SerializeField] private float weightBlendSpeed = 14f;

    [Header("Detach Input")]
    [SerializeField] private bool detachWhileRightMouseHeld = true;

    [Header("Safety")]
    [Tooltip("Clamp target distance from upper-leg/root to avoid over-stretching.")]
    [SerializeField] private float maxTargetDistanceFromRoot = 1.2f;

    [SerializeField] private bool drawDebug;

    private bool detachHeld;
    private bool isAttached;
    private Vector3 attachedPosition;
    private Quaternion attachedRotation;

    public bool IsAttached => isAttached;
    public Vector3 AttachedPosition => attachedPosition;
    public Quaternion AttachedRotation => attachedRotation;

    public void SetDetachHeld(bool held)
    {
        detachHeld = held;
        if (detachHeld)
        {
            Detach();
        }
    }

    private Transform Probe => bottomProbe != null ? bottomProbe : foot;

    private void Reset()
    {
        if (twoBoneIK == null)
        {
            twoBoneIK = GetComponentInChildren<TwoBoneIKConstraint>();
        }
    }

    private void LateUpdate()
    {
        if (detachWhileRightMouseHeld)
        {
            SetDetachHeld(Input.GetMouseButton(1));
        }

        if (autoAttachOnGround)
        {
            RefreshAttachmentState();
        }

        ApplyIKTargetPose();
        BlendIKWeight(Time.deltaTime);
    }

    private void RefreshAttachmentState()
    {
        if (detachHeld || isAttached)
        {
            return;
        }

        if (!TryGetGroundHit(out RaycastHit hit))
        {
            return;
        }

        Attach(hit);
    }

    private bool TryGetGroundHit(out RaycastHit hit)
    {
        hit = default;
        Transform probe = Probe;
        if (probe == null)
        {
            return false;
        }

        Vector3 origin = probe.position + (Vector3.up * probeStartOffset);
        float castDistance = probeStartOffset + probeDistance;
        return Physics.SphereCast(origin, probeRadius, Vector3.down, out hit, castDistance, groundLayers, QueryTriggerInteraction.Ignore)
               && hit.collider != null;
    }

    private void Attach(in RaycastHit hit)
    {
        if (foot == null)
        {
            return;
        }

        isAttached = true;
        attachedPosition = hit.point;
        attachedRotation = keepFootRotationOnAttach ? foot.rotation : Quaternion.LookRotation(Vector3.ProjectOnPlane(foot.forward, hit.normal), hit.normal);
    }

    private void Detach()
    {
        isAttached = false;
    }

    private void ApplyIKTargetPose()
    {
        if (!isAttached || ikTarget == null)
        {
            return;
        }

        Vector3 targetPos = attachedPosition;
        Quaternion targetRot = attachedRotation;

        Transform root = twoBoneIK != null && twoBoneIK.data.root != null ? twoBoneIK.data.root : null;
        if (root != null && maxTargetDistanceFromRoot > 0.01f)
        {
            Vector3 fromRoot = targetPos - root.position;
            float maxDist = maxTargetDistanceFromRoot;
            float sqrDist = fromRoot.sqrMagnitude;
            if (sqrDist > (maxDist * maxDist))
            {
                targetPos = root.position + (fromRoot.normalized * maxDist);
            }
        }

        ikTarget.SetPositionAndRotation(targetPos, targetRot);
    }

    private void BlendIKWeight(float deltaTime)
    {
        if (twoBoneIK == null)
        {
            return;
        }

        float target = isAttached ? attachWeight : detachWeight;
        float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, weightBlendSpeed) * deltaTime);
        twoBoneIK.weight = Mathf.Lerp(twoBoneIK.weight, target, blend);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug)
        {
            return;
        }

        Transform probe = Probe;
        if (probe == null)
        {
            return;
        }

        Vector3 origin = probe.position + (Vector3.up * probeStartOffset);
        Vector3 end = origin + (Vector3.down * (probeStartOffset + probeDistance));

        Gizmos.color = isAttached ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(origin, probeRadius);
        Gizmos.DrawLine(origin, end);
        Gizmos.DrawWireSphere(end, probeRadius);

        if (isAttached)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(attachedPosition, probeRadius);
        }
    }
}
