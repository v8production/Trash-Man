using UnityEngine;

public sealed class FootAttachmentController : MonoBehaviour
{
    private const string LogPrefix = "[TitanFootAttachment]";

    [Header("Role")]
    [SerializeField] private TitanBaseLegRoleController.LegSide side;

    [Header("Foot References")]
    [SerializeField] private Transform footTransform;
    [SerializeField] private Transform bottomProbe;

    [Header("Ground Detection")]
    [SerializeField] private LayerMask attachableGroundLayers = 1 << 6;
    [SerializeField] private float probeRadius = 0.08f;
    [SerializeField] private float probeDistance = 0.18f;
    [SerializeField] private float probeStartOffset = 0.05f;
    [SerializeField] private float maxAttachPlaneAngle = 10f;
    [SerializeField] private bool drawDebugGizmos;
    [SerializeField] private bool logAttachDetachTransitions = true;

    [Header("Filtering")]
    [Tooltip("Colliders under this root are ignored for ground hits (prevents self-hit when using Default layer).")]
    [SerializeField] private Transform characterRoot;

    private bool detachHeld;
    private bool isAttached;
    private float attachmentSuppressedUntil;
    private Vector3 attachedWorldPosition;
    private Quaternion attachedWorldRotation;
    private Collider attachedCollider;

    public TitanBaseLegRoleController.LegSide Side => side;
    public bool IsAttached => isAttached;
    public bool DetachHeld => detachHeld;
    public Vector3 AttachedWorldPosition => attachedWorldPosition;
    public Quaternion AttachedWorldRotation => attachedWorldRotation;
    public Collider AttachedCollider => attachedCollider;
    public Transform FootTransform => footTransform;
    public Transform BottomProbe => bottomProbe != null ? bottomProbe : footTransform;

    private void LateUpdate()
    {
        RefreshAttachmentState();
    }

    public void SetDetachHeld(bool held)
    {
        if (detachHeld == held)
        {
            return;
        }

        detachHeld = held;
        if (detachHeld)
        {
            Detach();
        }
    }

    public void SuppressAttachment(float seconds)
    {
        attachmentSuppressedUntil = Mathf.Max(attachmentSuppressedUntil, Time.time + Mathf.Max(0f, seconds));
    }

    public Vector3 GetCurrentContactPoint()
    {
        Transform probe = BottomProbe;
        return probe != null ? probe.position : Vector3.zero;
    }

    public bool TryGetGroundHit(out RaycastHit hit)
    {
        hit = default;
        Transform probe = BottomProbe;
        if (probe == null)
        {
            return false;
        }

        Vector3 origin = probe.position + (Vector3.up * probeStartOffset);
        float castDistance = probeStartOffset + probeDistance;

        // Use SphereCastAll so we can skip self-colliders reliably.
        RaycastHit[] hits = Physics.SphereCastAll(origin, probeRadius, Vector3.down, castDistance, attachableGroundLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        Transform root = characterRoot != null ? characterRoot : transform.root;
        float bestDist = float.PositiveInfinity;
        int bestIndex = -1;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null)
            {
                continue;
            }

            // Skip any collider that belongs to this character.
            if (root != null && c.transform.IsChildOf(root))
            {
                continue;
            }

            if (!IsValidAttachPlane(hits[i]))
            {
                continue;
            }

            float d = hits[i].distance;
            if (d < bestDist)
            {
                bestDist = d;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            return false;
        }

        hit = hits[bestIndex];
        return hit.collider != null;
    }

    public void RefreshAttachmentState()
    {
        if (detachHeld || Time.time < attachmentSuppressedUntil)
        {
            return;
        }

        if (isAttached)
        {
            return;
        }

        if (!TryGetGroundHit(out RaycastHit hit))
        {
            return;
        }

        Attach(hit);
    }

    public void Attach(in RaycastHit hit)
    {
        Transform probe = BottomProbe;
        Transform foot = footTransform;
        if (probe == null || foot == null)
        {
            return;
        }

        attachedWorldPosition = hit.point;
        attachedWorldRotation = foot.rotation;
        attachedCollider = hit.collider;
        isAttached = true;

        int layer = hit.collider.gameObject.layer;
        string layerName = LayerMask.LayerToName(layer);
        string layerLabel = string.IsNullOrWhiteSpace(layerName) ? layer.ToString() : $"{layerName}({layer})";
        if (logAttachDetachTransitions)
        {
            float planeAngle = GetAttachPlaneAngle(hit);
            Debug.Log($"{InputDebug.Prefix} {LogPrefix} ATTACH_SUCCESS side={side} layer={layerLabel} collider={hit.collider.name} point={hit.point} normal={hit.normal} planeAngle={planeAngle:F2}/{maxAttachPlaneAngle:F2} probe={probe.name} foot={foot.name} storedPos={attachedWorldPosition} storedRot={attachedWorldRotation.eulerAngles}");
        }
    }

    private bool IsValidAttachPlane(in RaycastHit hit)
    {
        if (hit.collider == null || hit.collider.gameObject.layer != 6)
        {
            return false;
        }

        Transform foot = footTransform != null ? footTransform : BottomProbe;
        if (foot == null)
        {
            return false;
        }

        Vector3 footSoleNormal = -foot.up;
        if (footSoleNormal.sqrMagnitude < 0.0001f || hit.normal.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        float alignment = Mathf.Abs(Vector3.Dot(footSoleNormal.normalized, hit.normal.normalized));
        float minAlignment = Mathf.Cos(Mathf.Clamp(maxAttachPlaneAngle, 0f, 90f) * Mathf.Deg2Rad);
        return alignment >= minAlignment;
    }

    private float GetAttachPlaneAngle(in RaycastHit hit)
    {
        Transform foot = footTransform != null ? footTransform : BottomProbe;
        if (foot == null || hit.normal.sqrMagnitude < 0.0001f)
        {
            return 180f;
        }

        Vector3 footSoleNormal = -foot.up;
        if (footSoleNormal.sqrMagnitude < 0.0001f)
        {
            return 180f;
        }

        return Vector3.Angle(footSoleNormal.normalized, hit.normal.normalized);
    }

    public void Detach()
    {
        if (!isAttached && attachedCollider == null)
        {
            return;
        }

        if (logAttachDetachTransitions)
        {
            Debug.Log($"{InputDebug.Prefix} {LogPrefix} side={side} DETACH attached={isAttached} collider={attachedCollider?.name ?? "<none>"} storedPos={attachedWorldPosition} storedRot={attachedWorldRotation.eulerAngles}");
        }
        isAttached = false;
        attachedCollider = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Transform probe = BottomProbe;
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
            Gizmos.DrawWireSphere(attachedWorldPosition, probeRadius);
        }
    }
}
