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

        public WorldPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static WorldPose Capture(Transform target)
        {
            return new WorldPose(target.position, target.rotation);
        }

    }

    [Header("Attachments")]
    [SerializeField] private FootAttachmentController leftFootAttachment;
    [SerializeField] private FootAttachmentController rightFootAttachment;

    [Header("Anchor Stabilization")]
    [SerializeField] private float singleFootYawCorrectionScale = 1f;
    [SerializeField] private float singleFootYawCorrectionSpeed = 12f;
    [SerializeField] private float singleFootMaxYawCorrectionDegreesPerFrame = 2.5f;
    [SerializeField] private float singleFootYawDeadZoneDegrees = 0.25f;
    [SerializeField] private bool zeroTorsoVelocityWhenLocked = true;

    private bool wasLocked;
    private Vector3 lockedRootPosition;
    private Quaternion lockedRootRotation;
    private bool _skipAnchorStabilizationThisFrame;

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
    }

    public bool HasAnyAttachedFoot()
    {
        return GetAnchorMode() != AnchorMode.Free;
    }

    public bool AreBothFeetAttached()
    {
        return GetAnchorMode() == AnchorMode.Locked;
    }

    public void UpdateAttachState(TitanBaseLegRoleController.LegSide side, bool attachHeld)
    {
        FootAttachmentController controller = GetAttachment(side);
        if (controller == null)
        {
            return;
        }

        controller.SetAttachHeld(attachHeld);
        if (attachHeld)
        {
            controller.RefreshAttachmentState();
        }
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

    public void ApplyAnchoredLegCommand(TitanBaseLegRoleController.LegSide side, in TitanLegInputCommand command, float deltaTime)
    {
        ResolveReferences();

        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        FootAttachmentController anchor = GetAttachment(side);
        if (anchor == null || !anchor.IsAttached)
        {
            return;
        }

        bool isLeft = side == TitanBaseLegRoleController.LegSide.Left;
        TitanLegControlState state = Managers.TitanRig.GetLegState(isLeft);
        bool applied = false;

        if (Mathf.Abs(command.AnkleInput) > 0.001f)
        {
            Transform foot = GetFoot(side);
            Transform contactPoint = anchor.BottomProbe;
            Vector3 anchoredContact = anchor.AttachedWorldPosition;
            Quaternion anchoredFootRotation = anchor.AttachedWorldRotation;

            state.AnkleRoll = Mathf.Clamp(
                state.AnkleRoll + (command.AnkleInput * command.AnkleSpeed * deltaTime),
                command.AnkleRollLimit.x,
                command.AnkleRollLimit.y
            );
            Managers.TitanRig.SetLegState(isLeft, state);
            Managers.TitanRig.ApplyLegPose(isLeft);

            applied |= RestoreRootToMatchContactAndFootRotation(contactPoint, anchoredContact, foot, anchoredFootRotation);
        }

        if (Mathf.Abs(command.KneeInput) > 0.001f)
        {
            Transform knee = GetKnee(side);
            Transform contactPoint = anchor.BottomProbe;
            Vector3 kneePosition = knee != null ? knee.position : Vector3.zero;
            Vector3 footPosition = anchor.AttachedWorldPosition;

            state.KneeRoll = Mathf.Clamp(
                state.KneeRoll + (command.KneeInput * command.KneeSpeed * deltaTime),
                command.KneeRollLimit.x,
                command.KneeRollLimit.y
            );
            Managers.TitanRig.SetLegState(isLeft, state);
            Managers.TitanRig.ApplyLegPose(isLeft);

            applied |= RestoreRootToMatchTwoPoints(knee, kneePosition, contactPoint, footPosition);
        }

        if (Mathf.Abs(command.HipYawDelta) > 0.001f)
        {
            applied |= ApplyAnchoredHipDelta(side, isLeft, ref state, command.HipYawDelta, 0f, command);
        }

        if (Mathf.Abs(command.HipRollDelta) > 0.001f)
        {
            applied |= ApplyAnchoredHipDelta(side, isLeft, ref state, 0f, command.HipRollDelta, command);
        }

        if (applied)
        {
            _skipAnchorStabilizationThisFrame = true;
        }
    }

    private bool ApplyAnchoredHipDelta(
        TitanBaseLegRoleController.LegSide side,
        bool isLeft,
        ref TitanLegControlState state,
        float yawDelta,
        float rollDelta,
        in TitanLegInputCommand command)
    {
        Transform hip = GetHip(side);
        Transform knee = GetKnee(side);
        Vector3 hipPosition = hip != null ? hip.position : Vector3.zero;
        Vector3 kneePosition = knee != null ? knee.position : Vector3.zero;

        state.HipYaw = Mathf.Clamp(state.HipYaw + yawDelta, command.HipYawLimit.x, command.HipYawLimit.y);
        state.HipRoll = Mathf.Clamp(state.HipRoll + rollDelta, command.HipRollLimit.x, command.HipRollLimit.y);
        Managers.TitanRig.SetLegState(isLeft, state);
        Managers.TitanRig.ApplyLegPose(isLeft);

        return RestoreRootToMatchTwoPoints(hip, hipPosition, knee, kneePosition);
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
                ApplySingleAnchorLock(leftFootAttachment, deltaTime, zeroTorsoVelocityWhenLocked: false);
                break;
            case AnchorMode.RightAnchored:
                ApplySingleAnchorLock(rightFootAttachment, deltaTime, zeroTorsoVelocityWhenLocked: false);
                break;
            case AnchorMode.Locked:
                // When both feet are attached, do NOT attempt corrective yaw/translation based on current contact points.
                // Any tiny foot transform jitter (IK/animation) will feedback into root correction and cause drift/spin.
                // Hard-lock the movement root pose instead.
                ApplyLockedRootPose(zeroTorsoVelocityWhenLocked);
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

    private void ApplySingleAnchorLock(FootAttachmentController attachment, float deltaTime, bool zeroTorsoVelocityWhenLocked)
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
                Managers.TitanRig.ApplyMovementRootPose(rotatedPosition, nextRotation, zeroTorsoVelocityWhenLocked);
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

        Managers.TitanRig.ApplyMovementRootPose(movableRoot.position + translationDelta, movableRoot.rotation, zeroTorsoVelocityWhenLocked);
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

    private bool RestoreRootToMatchContactAndFootRotation(Transform contactPoint, Vector3 desiredContactPosition, Transform foot, Quaternion desiredFootRotation)
    {
        Transform root = Managers.TitanRig.MovementRoot;
        if (root == null || contactPoint == null || foot == null)
        {
            return false;
        }

        Quaternion correction = desiredFootRotation * Quaternion.Inverse(foot.rotation);
        Vector3 correctedRootPosition = desiredContactPosition + correction * (root.position - contactPoint.position);
        Quaternion correctedRootRotation = correction * root.rotation;
        Managers.TitanRig.ApplyMovementRootPose(correctedRootPosition, correctedRootRotation, zeroVelocities: false);
        return true;
    }

    private bool RestoreRootToMatchTwoPoints(Transform pointA, Vector3 desiredA, Transform pointB, Vector3 desiredB)
    {
        Transform root = Managers.TitanRig.MovementRoot;
        if (root == null || pointA == null || pointB == null)
        {
            return false;
        }

        Vector3 currentSpan = pointB.position - pointA.position;
        Vector3 desiredSpan = desiredB - desiredA;
        Quaternion correction = Quaternion.identity;
        if (currentSpan.sqrMagnitude > 0.000001f && desiredSpan.sqrMagnitude > 0.000001f)
        {
            correction = Quaternion.FromToRotation(currentSpan, desiredSpan);
        }

        Vector3 correctedRootPosition = desiredA + correction * (root.position - pointA.position);
        Quaternion correctedRootRotation = correction * root.rotation;
        Managers.TitanRig.ApplyMovementRootPose(correctedRootPosition, correctedRootRotation, zeroVelocities: false);
        return true;
    }

    private Transform GetHip(TitanBaseLegRoleController.LegSide side)
    {
        return side == TitanBaseLegRoleController.LegSide.Left ? Managers.TitanRig.LeftHip : Managers.TitanRig.RightHip;
    }

    private Transform GetKnee(TitanBaseLegRoleController.LegSide side)
    {
        return side == TitanBaseLegRoleController.LegSide.Left ? Managers.TitanRig.LeftKnee : Managers.TitanRig.RightKnee;
    }

    private Transform GetFoot(TitanBaseLegRoleController.LegSide side)
    {
        return side == TitanBaseLegRoleController.LegSide.Left ? Managers.TitanRig.LeftFoot : Managers.TitanRig.RightFoot;
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
}
