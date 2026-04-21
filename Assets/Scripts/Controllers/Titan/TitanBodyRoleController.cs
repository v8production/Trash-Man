using UnityEngine;

public class TitanBodyRoleController : TitanBaseController
{

    [Header("Body Input")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float strafeSpeed = 1.75f;
    [SerializeField] private float turnSpeed = 75f;
    [SerializeField] private float bodySmoothing = 8f;

    [Header("Waist Rotation")]
    [SerializeField] private float waistTurnSpeed = 120f;
    [SerializeField] private Vector2 waistYawLimit = new Vector2(-180f, 180f);

    [Header("Physics")]
    [SerializeField] private float gravityScale = 1f;
    [SerializeField] private float groundProbeHeight = 0.35f;
    [SerializeField] private float groundProbeDistance = 0.7f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float movementAcceleration = 8f;
    [SerializeField] private float maxPlanarSpeed = 4.5f;
    [SerializeField] private float turnAcceleration = 85f;

    [Header("Balance")]
    [SerializeField] private float balanceTorque = 160f;
    [SerializeField] private float oneLegInstabilityMultiplier = 2.2f;
    [SerializeField] private float uprightRecoverySpeed = 5f;
    [SerializeField] private float gravityRollAcceleration = 120f;
    [SerializeField] private float rollDamping = 6f;
    [SerializeField] private float maxRollAngle = 65f;
    [SerializeField] private float maxBalanceTorque = 240f;

    [Header("Aerodynamic Drag Rotation")]
    [SerializeField] private Transform centerOfPressure;
    [SerializeField] private Vector3 centerOfPressureLocalOffset = new Vector3(0f, 1.1f, -0.6f);
    [SerializeField] private float dragTorqueScale = 0.03f;
    [SerializeField] private float maxDragTorque = 160f;
    [SerializeField] private float minDragSpeed = 0.75f;

    [Header("Foot Anchors")]
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform rightFoot;

    private Vector3 planarVelocity;
    private Rigidbody movementRigidbody;
    private float verticalVelocity;
    private float forwardInput;
    private float strafeInput;
    private float turnInput;
    private bool inputEnabled = true;

    public override Define.TitanRole Role => Define.TitanRole.Body;

    protected override void Awake()
    {
        base.Awake();
        Transform movementRoot = Managers.TitanRig.MovementRoot;
        movementRigidbody = movementRoot.GetComponent<Rigidbody>();
        if (movementRigidbody == null)
        {
            movementRigidbody = movementRoot.GetComponentInParent<Rigidbody>();
        }

        ResolveFeet(movementRoot);
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!inputEnabled)
        {
            forwardInput = 0f;
            strafeInput = 0f;
            turnInput = 0f;
        }
    }

    public override void TickRoleInput(float deltaTime)
    {
        if (!inputEnabled || Managers.TitanRig == null || !Managers.TitanRig.EnsureReady())
        {
            return;
        }

        TitanAggregatedInput input = GetInputSnapshot();
        forwardInput = input.BodyForward;
        strafeInput = input.BodyStrafe;
        turnInput = input.BodyTurn;
        UpdateWaistRotation(input.BodyWaist, deltaTime);
    }

    public void TickPhysics(float deltaTime)
    {
        if (Managers.TitanRig == null || !Managers.TitanRig.EnsureReady())
        {
            return;
        }

        Transform movementRoot = Managers.TitanRig.MovementRoot;
        if (movementRigidbody == null)
        {
            movementRigidbody = movementRoot.GetComponent<Rigidbody>();
            if (movementRigidbody == null)
            {
                movementRigidbody = movementRoot.GetComponentInParent<Rigidbody>();
            }
        }

        ResolveFeet(movementRoot);

        Vector3 flatForward = Vector3.ProjectOnPlane(movementRoot.forward, Vector3.up).normalized;
        Vector3 flatRight = Vector3.ProjectOnPlane(movementRoot.right, Vector3.up).normalized;
        Vector3 desiredPlanarVelocity =
            (flatForward * (forwardInput * moveSpeed)) +
            (flatRight * (strafeInput * strafeSpeed));

        planarVelocity = Vector3.Lerp(planarVelocity, desiredPlanarVelocity, 1f - Mathf.Exp(-bodySmoothing * deltaTime));

        bool leftGrounded = IsFootGrounded(leftFoot);
        bool rightGrounded = IsFootGrounded(rightFoot);
        bool grounded = leftGrounded || rightGrounded;

        if (!grounded)
        {
            grounded = IsBodyGrounded(movementRoot);
        }

        if (movementRigidbody != null)
        {
            ApplyRigidbodyPhysics(movementRoot, leftGrounded, rightGrounded, grounded);
            return;
        }

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -1f;
        }
        else
        {
            verticalVelocity += Physics.gravity.y * gravityScale * deltaTime;
        }

        float yawStep = turnInput * turnSpeed * deltaTime;
        float verticalStep = verticalVelocity * deltaTime;
        Vector3 movement = (planarVelocity * deltaTime) + (Vector3.up * verticalStep);
        movementRoot.position += movement;
        movementRoot.rotation = Quaternion.AngleAxis(yawStep, Vector3.up) * movementRoot.rotation;
    }

    private void ApplyRigidbodyPhysics(Transform movementRoot, bool leftGrounded, bool rightGrounded, bool grounded)
    {
        if (movementRigidbody == null)
        {
            return;
        }

        Vector3 velocity = movementRigidbody.linearVelocity;
        Vector3 currentPlanarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        Vector3 planarDelta = planarVelocity - currentPlanarVelocity;
        Vector3 planarAcceleration = planarDelta * movementAcceleration;
        movementRigidbody.AddForce(planarAcceleration, ForceMode.Acceleration);

        Vector3 clampedPlanarVelocity = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(movementRigidbody.linearVelocity, Vector3.up), maxPlanarSpeed);
        movementRigidbody.linearVelocity = clampedPlanarVelocity + (Vector3.up * movementRigidbody.linearVelocity.y);

        float yawAcceleration = turnInput * turnAcceleration;
        movementRigidbody.AddTorque(Vector3.up * yawAcceleration, ForceMode.Acceleration);

        if (gravityScale > 1f)
        {
            movementRigidbody.AddForce(Physics.gravity * (gravityScale - 1f), ForceMode.Acceleration);
        }

        ApplyBalanceTorque(movementRoot, leftGrounded, rightGrounded, grounded);
        ApplyDragDirectionTorque(movementRoot);
    }

    private void ApplyBalanceTorque(Transform movementRoot, bool leftGrounded, bool rightGrounded, bool grounded)
    {
        if (movementRigidbody == null)
        {
            return;
        }

        Vector3 centerOfMass = movementRigidbody.worldCenterOfMass;
        Vector3 supportCenter = Vector3.zero;
        int supports = 0;

        if (leftGrounded && leftFoot != null)
        {
            supportCenter += leftFoot.position;
            supports++;
        }

        if (rightGrounded && rightFoot != null)
        {
            supportCenter += rightFoot.position;
            supports++;
        }

        if (supports > 0)
        {
            supportCenter /= supports;
        }
        else
        {
            supportCenter = movementRigidbody.position;
        }

        Vector3 planarOffset = Vector3.ProjectOnPlane(centerOfMass - supportCenter, Vector3.up);
        float oneLegFactor = (leftGrounded ^ rightGrounded) ? oneLegInstabilityMultiplier : 1f;
        float comRightBias = Vector3.Dot(planarOffset, movementRoot.right);
        float signedFall = 0f;

        if (leftGrounded && !rightGrounded)
        {
            signedFall = -1f;
        }
        else if (rightGrounded && !leftGrounded)
        {
            signedFall = 1f;
        }
        else if (!leftGrounded && !rightGrounded && Mathf.Abs(comRightBias) > 0.001f)
        {
            signedFall = Mathf.Sign(comRightBias);
        }

        float gravityInfluence = gravityRollAcceleration * oneLegFactor;
        float comInfluence = Mathf.Clamp(comRightBias, -1f, 1f) * balanceTorque;
        float rollRate = Vector3.Dot(movementRigidbody.angularVelocity, movementRoot.forward);
        float dampingInfluence = -rollRate * rollDamping;
        float rollAngle = ComputeSignedRollAngle(movementRoot);
        float recoverInfluence = 0f;

        if (leftGrounded && rightGrounded)
        {
            recoverInfluence = -rollAngle * uprightRecoverySpeed;
        }

        if (Mathf.Abs(rollAngle) > maxRollAngle)
        {
            float overflow = Mathf.Abs(rollAngle) - maxRollAngle;
            recoverInfluence += -Mathf.Sign(rollAngle) * overflow * uprightRecoverySpeed;
        }

        float angularAcceleration = (signedFall * gravityInfluence) + comInfluence + dampingInfluence + recoverInfluence;

        if (!grounded)
        {
            angularAcceleration *= 0.6f;
        }

        angularAcceleration = Mathf.Clamp(angularAcceleration, -maxBalanceTorque, maxBalanceTorque);
        movementRigidbody.AddTorque(movementRoot.forward * angularAcceleration, ForceMode.Acceleration);
    }

    private void ApplyDragDirectionTorque(Transform movementRoot)
    {
        if (movementRigidbody == null)
        {
            return;
        }

        Vector3 velocity = movementRigidbody.linearVelocity;
        float speed = velocity.magnitude;
        if (speed < minDragSpeed)
        {
            return;
        }

        Vector3 airflowDirection = -velocity / speed;
        Vector3 pressurePoint = centerOfPressure != null
            ? centerOfPressure.position
            : movementRoot.TransformPoint(centerOfPressureLocalOffset);
        Vector3 lever = pressurePoint - movementRigidbody.worldCenterOfMass;
        float dragForceMagnitude = speed * speed * dragTorqueScale;
        Vector3 dragForce = airflowDirection * dragForceMagnitude;
        Vector3 dragTorque = Vector3.Cross(lever, dragForce);
        dragTorque = Vector3.ClampMagnitude(dragTorque, maxDragTorque);
        movementRigidbody.AddTorque(dragTorque, ForceMode.Acceleration);
    }

    private static float ComputeSignedRollAngle(Transform movementRoot)
    {
        Vector3 projectedUp = Vector3.ProjectOnPlane(movementRoot.up, movementRoot.forward);
        Vector3 projectedWorldUp = Vector3.ProjectOnPlane(Vector3.up, movementRoot.forward);
        if (projectedUp.sqrMagnitude < 0.0001f || projectedWorldUp.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        return Vector3.SignedAngle(projectedWorldUp, projectedUp, movementRoot.forward);
    }

    private bool IsFootGrounded(Transform foot)
    {
        if (foot == null)
        {
            return false;
        }

        Vector3 origin = foot.position + (Vector3.up * groundProbeHeight);
        return Physics.Raycast(origin, Vector3.down, groundProbeDistance + groundProbeHeight, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void ResolveFeet(Transform movementRoot)
    {
        if (leftFoot != null && rightFoot != null)
        {
            return;
        }

        Animator animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null && animator.isHuman)
        {
            leftFoot ??= animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot ??= animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        Transform root = movementRoot != null ? movementRoot : transform;
        leftFoot ??= FindByName(root, "leftfoot", "l_foot", "foot_l", "mixamorig:leftfoot", "bip001 l foot");
        rightFoot ??= FindByName(root, "rightfoot", "r_foot", "foot_r", "mixamorig:rightfoot", "bip001 r foot");

        leftFoot ??= FindBySideKeywords(root, true, "foot", "ankle", "toe");
        rightFoot ??= FindBySideKeywords(root, false, "foot", "ankle", "toe");
    }

    private static Transform FindByName(Transform root, params string[] candidates)
    {
        if (root == null)
        {
            return null;
        }

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            string lower = all[i].name.ToLowerInvariant();
            for (int c = 0; c < candidates.Length; c++)
            {
                if (lower.Contains(candidates[c]))
                {
                    return all[i];
                }
            }
        }

        return null;
    }

    private bool IsBodyGrounded(Transform movementRoot)
    {
        Vector3 origin = movementRoot.position + (Vector3.up * groundProbeHeight);
        float distance = groundProbeHeight + groundProbeDistance;
        return Physics.Raycast(origin, Vector3.down, distance, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private static Transform FindBySideKeywords(Transform root, bool isLeft, params string[] keywords)
    {
        if (root == null)
        {
            return null;
        }

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            string lower = all[i].name.ToLowerInvariant();
            if (!IsExpectedSide(lower, isLeft))
            {
                continue;
            }

            for (int k = 0; k < keywords.Length; k++)
            {
                if (lower.Contains(keywords[k]))
                {
                    return all[i];
                }
            }
        }

        return null;
    }

    private static bool IsExpectedSide(string lower, bool isLeft)
    {
        bool hasLeft =
            lower.Contains("left") ||
            lower.Contains("_l") ||
            lower.Contains("l_") ||
            lower.Contains(".l") ||
            lower.Contains(" l ");

        bool hasRight =
            lower.Contains("right") ||
            lower.Contains("_r") ||
            lower.Contains("r_") ||
            lower.Contains(".r") ||
            lower.Contains(" r ");

        if (isLeft)
        {
            return hasLeft && !hasRight;
        }

        return hasRight && !hasLeft;
    }

    private void UpdateWaistRotation(float waistInput, float deltaTime)
    {
        if (Managers.TitanRig == null || !Managers.TitanRig.EnsureReady() || Managers.TitanRig.Spine == null)
        {
            return;
        }

        float nextWaistYaw = Mathf.Clamp(
            Managers.TitanRig.WaistYaw + (waistInput * waistTurnSpeed * deltaTime),
            waistYawLimit.x,
            waistYawLimit.y);

        Managers.TitanRig.SetWaistYaw(nextWaistYaw);
        Managers.TitanRig.ApplyBodyPose();
    }
}
