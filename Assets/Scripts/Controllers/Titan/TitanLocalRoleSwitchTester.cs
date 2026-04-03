using UnityEngine;

namespace Titan
{
    using TitanRole = global::Define.TitanRole;

    public class TitanLocalRoleSwitchTester : MonoBehaviour
    {
        [Header("Authority")]
        [SerializeField] private bool isHostAuthority = true;

        [Header("Test Runtime")]
        [SerializeField] private bool disableAnimatorWhileTesting = true;

        [Header("Debug")]
        [SerializeField] private bool logRoleSwitch = true;
        [SerializeField] private TitanRole activeRole = TitanRole.Body;

        [Header("Role Controllers")]
        [SerializeField] private TitanBodyRoleController bodyController;
        [SerializeField] private TitanLeftArmRoleController leftArmController;
        [SerializeField] private TitanRightArmRoleController rightArmController;
        [SerializeField] private TitanLeftLegRoleController leftLegController;
        [SerializeField] private TitanRightLegRoleController rightLegController;

        public TitanRole ActiveRole => activeRole;

        public void SetHostAuthority(bool hostAuthority)
        {
            isHostAuthority = hostAuthority;
        }

        private void Awake()
        {
            bodyController ??= GetComponent<TitanBodyRoleController>();
            leftArmController ??= GetComponent<TitanLeftArmRoleController>();
            rightArmController ??= GetComponent<TitanRightArmRoleController>();
            leftLegController ??= GetComponent<TitanLeftLegRoleController>();
            rightLegController ??= GetComponent<TitanRightLegRoleController>();

            if (disableAnimatorWhileTesting)
            {
                Animator animator = GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = false;
                }
            }
        }

        private void Update()
        {
            if (!isHostAuthority)
            {
                return;
            }

            HandleRoleSwitchInput();
        }

        private void FixedUpdate()
        {
            if (!isHostAuthority)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            if (bodyController != null)
            {
                bool bodyRoleActive = activeRole == TitanRole.Body;
                bodyController.SetInputEnabled(bodyRoleActive);
                if (bodyRoleActive)
                {
                    bodyController.TickRoleInput(deltaTime);
                }

                bodyController.TickPhysics(deltaTime);
            }

            switch (activeRole)
            {
                case TitanRole.LeftArm:
                    leftArmController?.TickRoleInput(deltaTime);
                    break;
                case TitanRole.RightArm:
                    rightArmController?.TickRoleInput(deltaTime);
                    break;
                case TitanRole.LeftLeg:
                    leftLegController?.TickRoleInput(deltaTime);
                    break;
                case TitanRole.RightLeg:
                    rightLegController?.TickRoleInput(deltaTime);
                    break;
            }
        }

        private void HandleRoleSwitchInput()
        {
            if (TitanInputUtility.WasDigitPressedThisFrame(1))
            {
                SetActiveRole(TitanRole.Body);
                return;
            }

            if (TitanInputUtility.WasDigitPressedThisFrame(2))
            {
                SetActiveRole(TitanRole.LeftArm);
                return;
            }

            if (TitanInputUtility.WasDigitPressedThisFrame(3))
            {
                SetActiveRole(TitanRole.RightArm);
                return;
            }

            if (TitanInputUtility.WasDigitPressedThisFrame(4))
            {
                SetActiveRole(TitanRole.LeftLeg);
                return;
            }

            if (TitanInputUtility.WasDigitPressedThisFrame(5))
            {
                SetActiveRole(TitanRole.RightLeg);
            }
        }

        private void SetActiveRole(TitanRole newRole)
        {
            if (activeRole == newRole)
            {
                return;
            }

            activeRole = newRole;
            if (logRoleSwitch)
            {
                Debug.Log($"[TitanLocalRoleSwitchTester] Active role changed to {activeRole}", this);
            }
        }
    }
}
