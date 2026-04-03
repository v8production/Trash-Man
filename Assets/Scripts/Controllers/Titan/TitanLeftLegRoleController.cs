using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Titan
{
    using TitanRole = global::Define.TitanRole;

    public class TitanLeftLegRoleController : MonoBehaviour, ITitanRoleController
    {
        [SerializeField] private TitanRig rig;

        [Header("Hip Mouse Mapping")]
        [SerializeField] private float maxThetaDegrees = 55f;
        [SerializeField] private float thetaRadiusPixels = 260f;
        [SerializeField] private bool useScreenCenterAsOrigin = true;
        [SerializeField] private Vector2 mouseOriginPixels = new Vector2(960f, 540f);
        [SerializeField, Range(0.05f, 1f)] private float mouseSensitivity = 0.35f;
        [SerializeField] private float mouseResponseSpeed = 12f;

        [Header("Knee Input")]
        [SerializeField] private float kneeSpeed = 110f;
        [SerializeField] private Vector2 hipYawLimit = new Vector2(-40f, 40f);
        [SerializeField] private Vector2 hipRollLimit = new Vector2(-45f, 45f);
        [SerializeField] private Vector2 kneeRollLimit = new Vector2(-5f, 125f);

        private float hipYaw;
        private float hipRoll;
        private float kneeRoll;

        public TitanRole Role => TitanRole.LeftLeg;

        private void Awake()
        {
            rig ??= GetComponent<TitanRig>();
        }

        public void TickRoleInput(float deltaTime)
        {
            if (rig == null || !rig.EnsureReady())
            {
                return;
            }

            Vector2 mousePosition = TitanInputUtility.ReadMousePosition();
            Vector2 origin = useScreenCenterAsOrigin
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : mouseOriginPixels;
            Vector2 targetAngles = TitanInputUtility.ComputeSphericalAngles(
                mousePosition,
                origin,
                thetaRadiusPixels,
                maxThetaDegrees,
                mouseSensitivity);
            float targetYaw = targetAngles.x;
            float targetRoll = targetAngles.y;

            float blend = 1f - Mathf.Exp(-mouseResponseSpeed * deltaTime);
            hipYaw = Mathf.Lerp(hipYaw, targetYaw, blend);
            hipRoll = Mathf.Lerp(hipRoll, targetRoll, blend);

            float kneeInput = TitanInputUtility.GetAxis(KeyCode.W, KeyCode.S, Key.W, Key.S);

            hipYaw = Mathf.Clamp(hipYaw, hipYawLimit.x, hipYawLimit.y);
            hipRoll = Mathf.Clamp(hipRoll, hipRollLimit.x, hipRollLimit.y);
            kneeRoll = Mathf.Clamp(kneeRoll + (kneeInput * kneeSpeed * deltaTime), kneeRollLimit.x, kneeRollLimit.y);

            rig.ApplyLeftLeg(hipYaw, hipRoll, kneeRoll);
        }
    }
}
