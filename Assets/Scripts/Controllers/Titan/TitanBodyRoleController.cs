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
    private bool anchorPhysicsOverride;

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

    public void SetAnchorPhysicsOverride(bool enabled)
    {
        anchorPhysicsOverride = enabled;
        if (!anchorPhysicsOverride)
        {
            return;
        }

        planarVelocity = Vector3.zero;
        verticalVelocity = 0f;
        forwardInput = 0f;
        strafeInput = 0f;
        turnInput = 0f;

        if (movementRigidbody != null)
        {
            movementRigidbody.linearVelocity = Vector3.zero;
            movementRigidbody.angularVelocity = Vector3.zero;
        }
    }

    public override void TickRoleInput(in TitanAggregatedInput input, float deltaTime)
    {
        if (!inputEnabled || !Managers.TitanRig.EnsureReady())
        {
            return;
        }

        forwardInput = input.BodyForward;
        strafeInput = input.BodyStrafe;
        turnInput = input.BodyTurn;
        UpdateWaistRotation(input.BodyWaist, deltaTime);
    }

    public void TickPhysics(float deltaTime)
    {
        if (!Managers.TitanRig.EnsureReady())
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

        if (anchorPhysicsOverride)
        {
            if (movementRigidbody != null)
            {
                movementRigidbody.linearVelocity = Vector3.zero;
                movementRigidbody.angularVelocity = Vector3.zero;
            }

            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            return;
        }

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
            ApplyRigidbodyPhysics();
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

    private void ApplyRigidbodyPhysics()
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
        if (!Managers.TitanRig.EnsureReady() || Managers.TitanRig.Spine == null)
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
