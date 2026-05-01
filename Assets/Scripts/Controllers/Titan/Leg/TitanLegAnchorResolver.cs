using UnityEngine;

public sealed class TitanLegAnchorResolver : MonoBehaviour
{

    private enum AnchorMode
    {
        Free,
        LeftAnchored,
        RightAnchored,
        Locked,
    }

    private readonly struct WorldPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        private WorldPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static WorldPose Capture(Transform target)
        {
            return new WorldPose(target.position, target.rotation);
        }

        public void ApplyTo(Transform target)
        {
            target.SetPositionAndRotation(Position, Rotation);
        }
    }

    [Header("Attachments")]
    [SerializeField] private FootAttachmentController leftFootAttachment;
    [SerializeField] private FootAttachmentController rightFootAttachment;

    [Header("Anchored Leg Compensation")]
    [SerializeField] private bool preserveAnchoredThighWorldPose = true;

    [Header("Inverse Movement")]
    [SerializeField] private float inverseYawScale = 1f;
    [SerializeField] private float inverseRollScale = 0.75f;

    [Header("Anchor Stabilization")]
    [SerializeField] private float singleFootYawCorrectionScale = 1f;
    [SerializeField] private float singleFootYawCorrectionSpeed = 12f;
    [SerializeField] private float singleFootMaxYawCorrectionDegreesPerFrame = 2.5f;
    [SerializeField] private float singleFootYawDeadZoneDegrees = 0.25f;
    [SerializeField] private bool zeroBodyVelocityWhenLocked = true;

    private bool wasLocked;
    private Vector3 lockedRootPosition;
    private Quaternion lockedRootRotation;
    private bool _skipAnchorStabilizationThisFrame;

    private bool _hasPendingAnchoredLegPose;
    private WorldPose _pendingHipPose;
    private WorldPose _pendingKneePose;
    private WorldPose _pendingFootPose;
    private Transform _pendingHip;
    private Transform _pendingKnee;
    private Transform _pendingFoot;

    private void Awake()
    {
        ResolveReferences();
    }

    private void LateUpdate()
    {
        ResolveReferences();

        bool skipStabilization = _skipAnchorStabilizationThisFrame;
        _skipAnchorStabilizationThisFrame = false;

        if (!skipStabilization)
        {
            StabilizeAnchors(Time.deltaTime);
        }

        ApplyPendingAnchoredLegPose();
    }

    public bool HasAnyAttachedFoot()
    {
        return GetAnchorMode() != AnchorMode.Free;
    }

    public bool AreBothFeetAttached()
    {
        return GetAnchorMode() == AnchorMode.Locked;
    }

    public void UpdateDetachState(TitanBaseLegRoleController.LegSide side, bool detachHeld)
    {
        FootAttachmentController controller = GetAttachment(side);
        controller?.SetDetachHeld(detachHeld);
    }

    public bool TryApplyAnchoredMovement(TitanBaseLegRoleController.LegSide side, in TitanLegInputCommand command, in TitanLegControlState currentState, float deltaTime)
    {
        ResolveReferences();

        AnchorMode mode = GetAnchorMode();
        if (mode == AnchorMode.Locked)
        {
            return true;
        }

        bool leftAnchored = mode == AnchorMode.LeftAnchored;
        bool rightAnchored = mode == AnchorMode.RightAnchored;
        if ((side == TitanBaseLegRoleController.LegSide.Left && !leftAnchored) || (side == TitanBaseLegRoleController.LegSide.Right && !rightAnchored))
        {
            return false;
        }

        Transform movableRoot = Managers.TitanRig.MovementRoot;
        FootAttachmentController anchor = GetAttachment(side);
        if (movableRoot == null || anchor == null || !anchor.IsAttached)
        {
            return false;
        }

        // Keeping this method as a gate for anchored/non-anchored path.
        return true;
    }

    public bool PreserveAnchoredThighWorldPose => preserveAnchoredThighWorldPose;

    public void ApplyCompensatedRootDelta(Transform anchoredThigh, Vector3 beforePos, Quaternion beforeRot)
    {
        if (anchoredThigh == null)
        {
            return;
        }

        Transform root = Managers.TitanRig.MovementRoot;
        if (root == null)
        {
            return;
        }

        Vector3 afterPos = anchoredThigh.position;
        Quaternion afterRot = anchoredThigh.rotation;

        // delta maps 'after' thigh pose back to 'before'.
        Quaternion deltaRot = beforeRot * Quaternion.Inverse(afterRot);
        Vector3 deltaPos = beforePos - (deltaRot * afterPos);

        Vector3 nextRootPos = (deltaRot * root.position) + deltaPos;
        Quaternion nextRootRot = deltaRot * root.rotation;
        Managers.TitanRig.ApplyMovementRootPose(nextRootPos, nextRootRot, zeroVelocities: false);
    }

    public void ApplyInverseRootFromHipDelta(
        TitanBaseLegRoleController.LegSide side,
        float hipYawDelta,
        float hipRollDelta)
    {
        ResolveReferences();

        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        AnchorMode mode = GetAnchorMode();

        if (mode == AnchorMode.Locked)
        {
            return;
        }

        bool isLeft = side == TitanBaseLegRoleController.LegSide.Left;

        if ((isLeft && mode != AnchorMode.LeftAnchored) ||
            (!isLeft && mode != AnchorMode.RightAnchored))
        {
            return;
        }

        Transform movementRoot = Managers.TitanRig.MovementRoot;
        FootAttachmentController anchor = GetAttachment(side);

        if (movementRoot == null || anchor == null || !anchor.IsAttached)
        {
            return;
        }

        Transform anchoredHip = isLeft
            ? Managers.TitanRig.LeftHip
            : Managers.TitanRig.RightHip;

        Transform anchoredKnee = isLeft
            ? Managers.TitanRig.LeftKnee
            : Managers.TitanRig.RightKnee;

        Transform anchoredFoot = isLeft
            ? Managers.TitanRig.LeftFoot
            : Managers.TitanRig.RightFoot;

        if (anchoredHip == null || anchoredKnee == null || anchoredFoot == null)
        {
            return;
        }

        WorldPose hipPose = WorldPose.Capture(anchoredHip);
        WorldPose kneePose = WorldPose.Capture(anchoredKnee);
        WorldPose footPose = WorldPose.Capture(anchoredFoot);

        float inverseYaw = Mathf.Clamp(-hipYawDelta * inverseYawScale, -3f, 3f);
        float inverseRoll = Mathf.Clamp(-hipRollDelta * inverseRollScale, -3f, 3f);

        Vector3 rollAxis = Vector3.ProjectOnPlane(anchoredHip.forward, Vector3.up);

        if (rollAxis.sqrMagnitude < 0.0001f)
        {
            rollAxis = movementRoot.forward;
        }

        Quaternion yawRotation = Quaternion.AngleAxis(inverseYaw, Vector3.up);
        Quaternion rollRotation = Quaternion.AngleAxis(inverseRoll, rollAxis.normalized);
        Quaternion combinedRotation = yawRotation * rollRotation;

        Vector3 pivot = hipPose.Position;
        Vector3 rootOffset = movementRoot.position - pivot;

        Vector3 nextRootPosition = pivot + combinedRotation * rootOffset;
        Quaternion nextRootRotation = combinedRotation * movementRoot.rotation;

        Managers.TitanRig.ApplyMovementRootPose(
            nextRootPosition,
            nextRootRotation,
            zeroVelocities: false
        );

        _pendingHip = anchoredHip;
        _pendingKnee = anchoredKnee;
        _pendingFoot = anchoredFoot;
        _pendingHipPose = hipPose;
        _pendingKneePose = kneePose;
        _pendingFootPose = footPose;
        _hasPendingAnchoredLegPose = true;
        _skipAnchorStabilizationThisFrame = true;
    }

    public void StabilizeNow(float deltaTime)
    {
        StabilizeAnchors(deltaTime);
    }

    private void StabilizeAnchors(float deltaTime)
    {
        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        AnchorMode mode = GetAnchorMode();

        // Capture the root pose when entering locked mode.
        if (mode == AnchorMode.Locked)
        {
            if (!wasLocked)
            {
                Transform movableRoot = Managers.TitanRig.MovementRoot;
                if (movableRoot != null)
                {
                    lockedRootPosition = movableRoot.position;
                    lockedRootRotation = movableRoot.rotation;
                }
            }

            wasLocked = true;
        }
        else
        {
            wasLocked = false;
        }

        switch (mode)
        {
            case AnchorMode.LeftAnchored:
                ApplySingleAnchorLock(leftFootAttachment, deltaTime, zeroBodyVelocityWhenLocked: false);
                break;
            case AnchorMode.RightAnchored:
                ApplySingleAnchorLock(rightFootAttachment, deltaTime, zeroBodyVelocityWhenLocked: false);
                break;
            case AnchorMode.Locked:
                // When both feet are attached, do NOT attempt corrective yaw/translation based on current contact points.
                // Any tiny foot transform jitter (IK/animation) will feedback into root correction and cause drift/spin.
                // Hard-lock the movement root pose instead.
                ApplyLockedRootPose(zeroBodyVelocityWhenLocked);
                break;
        }
    }

    private void ApplyLockedRootPose(bool zeroVelocities)
    {
        Transform movableRoot = Managers.TitanRig.MovementRoot;
        if (movableRoot == null)
        {
            return;
        }

        Managers.TitanRig.ApplyMovementRootPose(lockedRootPosition, lockedRootRotation, zeroVelocities);
    }

    private void ApplySingleAnchorLock(FootAttachmentController attachment, float deltaTime, bool zeroBodyVelocityWhenLocked)
    {
        Transform movableRoot = Managers.TitanRig.MovementRoot;
        if (movableRoot == null || attachment == null || !attachment.IsAttached || attachment.FootTransform == null)
        {
            return;
        }

        Vector3 desiredPivot = attachment.AttachedWorldPosition;
        Vector3 currentPivot = attachment.GetCurrentContactPoint();
        Quaternion nextRotation = movableRoot.rotation;

        Vector3 currentForward = Vector3.ProjectOnPlane(attachment.FootTransform.forward, Vector3.up);
        Vector3 desiredForward = Vector3.ProjectOnPlane(attachment.AttachedWorldRotation * Vector3.forward, Vector3.up);
        if (currentForward.sqrMagnitude > 0.0001f && desiredForward.sqrMagnitude > 0.0001f)
        {
            float yawDelta = Vector3.SignedAngle(currentForward, desiredForward, Vector3.up) * singleFootYawCorrectionScale;
            if (Mathf.Abs(yawDelta) > singleFootYawDeadZoneDegrees)
            {
                float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, singleFootYawCorrectionSpeed) * Mathf.Max(0f, deltaTime));
                float step = yawDelta * blend;
                float maxStep = Mathf.Max(0f, singleFootMaxYawCorrectionDegreesPerFrame);
                if (maxStep > 0.0001f)
                {
                    step = Mathf.Clamp(step, -maxStep, maxStep);
                }

                if (Mathf.Abs(step) < 0.0001f)
                {
                    step = Mathf.Sign(yawDelta) * 0.0001f;
                }

                Quaternion yawRotation = Quaternion.AngleAxis(step, Vector3.up);
                Vector3 rotatedPosition = desiredPivot + (yawRotation * (movableRoot.position - desiredPivot));
                nextRotation = yawRotation * nextRotation;
                Managers.TitanRig.ApplyMovementRootPose(rotatedPosition, nextRotation, zeroBodyVelocityWhenLocked);
                currentPivot = attachment.GetCurrentContactPoint();
            }
        }

        // Only correct planar translation.
        // Vertical correction based on a probe point is unstable (probe may not be at the sole),
        // and can push the whole titan into/under the floor when roles switch or IK jitters.
        Vector3 translationDelta = Vector3.ProjectOnPlane(desiredPivot - currentPivot, Vector3.up);
        if (translationDelta.sqrMagnitude <= 0.0000001f)
        {
            return;
        }

        Managers.TitanRig.ApplyMovementRootPose(movableRoot.position + translationDelta, movableRoot.rotation, zeroBodyVelocityWhenLocked);
    }

    private void ApplyDualAnchorLock(bool zeroVelocities)
    {
        Transform movableRoot = Managers.TitanRig.MovementRoot;
        if (movableRoot == null || leftFootAttachment == null || rightFootAttachment == null)
        {
            return;
        }

        if (!leftFootAttachment.IsAttached || !rightFootAttachment.IsAttached)
        {
            return;
        }

        Vector3 leftCurrent = leftFootAttachment.GetCurrentContactPoint();
        Vector3 rightCurrent = rightFootAttachment.GetCurrentContactPoint();
        Vector3 leftDesired = leftFootAttachment.AttachedWorldPosition;
        Vector3 rightDesired = rightFootAttachment.AttachedWorldPosition;

        Vector3 currentMid = (leftCurrent + rightCurrent) * 0.5f;
        Vector3 desiredMid = (leftDesired + rightDesired) * 0.5f;
        Vector3 currentSpan = Vector3.ProjectOnPlane(rightCurrent - leftCurrent, Vector3.up);
        Vector3 desiredSpan = Vector3.ProjectOnPlane(rightDesired - leftDesired, Vector3.up);

        Vector3 nextPosition = movableRoot.position;
        Quaternion nextRotation = movableRoot.rotation;

        if (currentSpan.sqrMagnitude > 0.0001f && desiredSpan.sqrMagnitude > 0.0001f)
        {
            float yawDelta = Vector3.SignedAngle(currentSpan, desiredSpan, Vector3.up);
            if (Mathf.Abs(yawDelta) > 0.001f)
            {
                Quaternion yawRotation = Quaternion.AngleAxis(yawDelta, Vector3.up);
                nextPosition = currentMid + (yawRotation * (nextPosition - currentMid));
                nextRotation = yawRotation * nextRotation;
            }
        }

        nextPosition += desiredMid - currentMid;
        Managers.TitanRig.ApplyMovementRootPose(nextPosition, nextRotation, zeroVelocities);
    }

    private AnchorMode GetAnchorMode()
    {
        bool leftAttached = leftFootAttachment != null && leftFootAttachment.IsAttached;
        bool rightAttached = rightFootAttachment != null && rightFootAttachment.IsAttached;

        if (leftAttached && rightAttached)
        {
            return AnchorMode.Locked;
        }

        if (leftAttached)
        {
            return AnchorMode.LeftAnchored;
        }

        if (rightAttached)
        {
            return AnchorMode.RightAnchored;
        }

        return AnchorMode.Free;
    }

    private FootAttachmentController GetAttachment(TitanBaseLegRoleController.LegSide side)
    {
        return side == TitanBaseLegRoleController.LegSide.Left ? leftFootAttachment : rightFootAttachment;
    }

    public bool IsFootAttached(TitanBaseLegRoleController.LegSide side)
    {
        FootAttachmentController attachment = side == TitanBaseLegRoleController.LegSide.Left
            ? leftFootAttachment
            : rightFootAttachment;

        return attachment != null && attachment.IsAttached;
    }

    private void ResolveReferences()
    {
        if (leftFootAttachment == null || rightFootAttachment == null)
        {
            FootAttachmentController[] attachments = GetComponents<FootAttachmentController>();
            for (int i = 0; i < attachments.Length; i++)
            {
                FootAttachmentController attachment = attachments[i];
                if (attachment == null)
                {
                    continue;
                }

                if (attachment.Side == TitanBaseLegRoleController.LegSide.Left)
                {
                    leftFootAttachment ??= attachment;
                    continue;
                }

                rightFootAttachment ??= attachment;
            }
        }
    }
    private void ApplyPendingAnchoredLegPose()
    {
        if (!_hasPendingAnchoredLegPose)
        {
            return;
        }

        _hasPendingAnchoredLegPose = false;

        if (_pendingHip != null)
        {
            _pendingHipPose.ApplyTo(_pendingHip);
        }

        if (_pendingKnee != null)
        {
            _pendingKneePose.ApplyTo(_pendingKnee);
        }

        if (_pendingFoot != null)
        {
            _pendingFootPose.ApplyTo(_pendingFoot);
        }

        _pendingHip = null;
        _pendingKnee = null;
        _pendingFoot = null;
    }
}
