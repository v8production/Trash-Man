using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Titan
{
    using TitanRole = global::Define.TitanRole;

    public class TitanBodyRoleController : MonoBehaviour, ITitanRoleController
    {
        [SerializeField] private TitanRig rig;

        [Header("Body Input")]
        [SerializeField] private float moveSpeed = 3.5f;
        [SerializeField] private float strafeSpeed = 1.75f;
        [SerializeField] private float turnSpeed = 75f;
        [SerializeField] private float bodySmoothing = 8f;

        [Header("Physics")]
        [SerializeField] private float gravityScale = 1f;
        [SerializeField] private float groundProbeHeight = 0.35f;
        [SerializeField] private float groundProbeDistance = 0.7f;
        [SerializeField] private float groundSkinWidth = 0.03f;
        [SerializeField] private LayerMask groundLayers = ~0;

        [Header("Balance")]
        [SerializeField] private float balanceTorque = 160f;
        [SerializeField] private float oneLegInstabilityMultiplier = 2.2f;
        [SerializeField] private float uprightRecoverySpeed = 5f;
        [SerializeField] private float gravityRollAcceleration = 120f;
        [SerializeField] private float rollDamping = 6f;
        [SerializeField] private float maxRollAngle = 65f;

        [Header("Foot Anchors")]
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightFoot;

        private Vector3 planarVelocity;
        private Rigidbody movementRigidbody;
        private float verticalVelocity;
        private float forwardInput;
        private float strafeInput;
        private float turnInput;
        private float rollVelocity;
        private float rollAngle;
        private bool inputEnabled = true;

        public TitanRole Role => TitanRole.Body;

        private void Awake()
        {
            rig ??= GetComponent<TitanRig>();
            if (rig != null)
            {
                Transform movementRoot = rig.MovementRoot;
                movementRigidbody = movementRoot.GetComponent<Rigidbody>();
                if (movementRigidbody == null)
                {
                    movementRigidbody = movementRoot.GetComponentInParent<Rigidbody>();
                }

                ResolveFeet(movementRoot);
            }
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

        public void TickRoleInput(float deltaTime)
        {
            if (!inputEnabled || rig == null || !rig.EnsureReady())
            {
                return;
            }

            forwardInput = TitanInputUtility.GetAxis(
                KeyCode.UpArrow,
                KeyCode.DownArrow,
                Key.UpArrow,
                Key.DownArrow);

            strafeInput = TitanInputUtility.GetAxis(
                KeyCode.RightArrow,
                KeyCode.LeftArrow,
                Key.RightArrow,
                Key.LeftArrow);

            turnInput = TitanInputUtility.GetAxis(
                KeyCode.Period,
                KeyCode.Comma,
                Key.Period,
                Key.Comma);
        }

        public void TickPhysics(float deltaTime)
        {
            if (rig == null || !rig.EnsureReady())
            {
                return;
            }

            Transform movementRoot = rig.MovementRoot;
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

            if (movementRigidbody != null)
            {
                if (verticalStep < 0f)
                {
                    float desiredDrop = Mathf.Abs(verticalStep);
                    Vector3 probeOrigin = movementRigidbody.worldCenterOfMass + (Vector3.up * groundProbeHeight);
                    float probeDistance = groundProbeHeight + desiredDrop + groundSkinWidth;
                    if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, probeDistance, groundLayers, QueryTriggerInteraction.Ignore))
                    {
                        float availableDrop = hit.distance - groundProbeHeight - groundSkinWidth;
                        if (availableDrop <= desiredDrop)
                        {
                            verticalStep = -Mathf.Max(availableDrop, 0f);
                            verticalVelocity = 0f;
                            grounded = true;
                        }
                    }
                }

                Vector3 movementStep = (planarVelocity * deltaTime) + (Vector3.up * verticalStep);
                Vector3 nextPosition = movementRigidbody.position + movementStep;
                Quaternion yawRotation = Quaternion.AngleAxis(yawStep, Vector3.up) * movementRigidbody.rotation;
                Quaternion nextRotation = ComputeBalancedRotation(yawRotation, leftGrounded, rightGrounded, grounded, deltaTime);
                movementRigidbody.MovePosition(nextPosition);
                movementRigidbody.MoveRotation(nextRotation);
                return;
            }

            Vector3 movement = (planarVelocity * deltaTime) + (Vector3.up * verticalStep);
            movementRoot.position += movement;
            Quaternion rootYawRotation = Quaternion.AngleAxis(yawStep, Vector3.up) * movementRoot.rotation;
            movementRoot.rotation = ComputeBalancedRotation(rootYawRotation, leftGrounded, rightGrounded, grounded, deltaTime);
        }

        private Quaternion ComputeBalancedRotation(Quaternion currentYawRotation, bool leftGrounded, bool rightGrounded, bool grounded, float deltaTime)
        {
            if (movementRigidbody == null)
            {
                return currentYawRotation;
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
            float comRightBias = Vector3.Dot(planarOffset, currentYawRotation * Vector3.right);
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
            float angularAcceleration = (signedFall * gravityInfluence) + comInfluence;

            if (!grounded)
            {
                angularAcceleration *= 0.6f;
            }

            rollVelocity += angularAcceleration * deltaTime;
            float dampingFactor = 1f - Mathf.Exp(-rollDamping * deltaTime);
            rollVelocity = Mathf.Lerp(rollVelocity, 0f, dampingFactor);

            if (leftGrounded && rightGrounded)
            {
                float recoverT = 1f - Mathf.Exp(-uprightRecoverySpeed * deltaTime);
                rollAngle = Mathf.Lerp(rollAngle, 0f, recoverT);
            }
            else
            {
                rollAngle += rollVelocity * deltaTime;
            }

            rollAngle = Mathf.Clamp(rollAngle, -maxRollAngle, maxRollAngle);
            Vector3 rollAxis = currentYawRotation * Vector3.forward;
            Quaternion rollRotation = Quaternion.AngleAxis(rollAngle, rollAxis);

            return rollRotation * currentYawRotation;
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
    }
}
