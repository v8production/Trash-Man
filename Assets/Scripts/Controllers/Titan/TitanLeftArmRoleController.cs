using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Titan
{
    using TitanRole = global::Define.TitanRole;

    public class TitanLeftArmRoleController : MonoBehaviour, ITitanRoleController
    {
        [SerializeField] private TitanRig rig;
        [SerializeField] private TitanInputAggregationManager inputAggregationManager;
        [Header("Shoulder Mouse Mapping")]
        [SerializeField] private float maxThetaDegrees = 55f;
        [SerializeField] private float thetaRadiusPixels = 260f;
        [SerializeField] private bool useScreenCenterAsOrigin = true;
        [SerializeField] private Vector2 mouseOriginPixels = new Vector2(960f, 540f);
        [SerializeField, Range(0.05f, 1f)] private float mouseSensitivity = 0.35f;
        [SerializeField] private float mouseResponseSpeed = 5f;

        [Header("Elbow Input")]
        [SerializeField] private float elbowSpeed = 120f;
        [SerializeField] private Vector2 shoulderYawLimit = new Vector2(-55f, 55f);
        [SerializeField] private Vector2 shoulderPitchLimit = new Vector2(-360f, 360f);
        [SerializeField] private Vector2 elbowPitchLimit = new Vector2(-15f, 130f);

        private float shoulderYaw;
        private float shoulderPitch;
        private float elbowPitch;

        public TitanRole Role => TitanRole.LeftArm;

        private void Awake()
        {
            rig ??= GetComponent<TitanRig>();
            inputAggregationManager ??= GetComponent<TitanInputAggregationManager>();
        }

        public void TickRoleInput(float deltaTime)
        {
            if (rig == null || !rig.EnsureReady())
            {
                return;
            }

            Vector2 mousePosition = inputAggregationManager != null
                ? inputAggregationManager.Current.MousePosition
                : TitanInputUtility.ReadMousePosition();
            Vector2 origin = useScreenCenterAsOrigin
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : mouseOriginPixels;
            Vector2 targetAngles = TitanInputUtility.ComputeSphericalAngles(
                mousePosition,
                origin,
                thetaRadiusPixels,
                maxThetaDegrees,
                mouseSensitivity,
                secondaryMaxDegrees: 360f,
                applySensitivityToSecondary: false);
            float targetYaw = targetAngles.x;
            float targetPitch = targetAngles.y;

            float blend = 1f - Mathf.Exp(-mouseResponseSpeed * deltaTime);
            shoulderYaw = Mathf.Lerp(shoulderYaw, targetYaw, blend);
            shoulderPitch = Mathf.Lerp(shoulderPitch, targetPitch, blend);

            float elbowInput = inputAggregationManager != null
                ? inputAggregationManager.Current.LeftArmElbow
                : TitanInputUtility.GetAxis(KeyCode.W, KeyCode.S, Key.W, Key.S);

            shoulderPitch = Mathf.Clamp(shoulderPitch, shoulderPitchLimit.x, shoulderPitchLimit.y);
            shoulderYaw = Mathf.Clamp(shoulderYaw, shoulderYawLimit.x, shoulderYawLimit.y);
            elbowPitch = Mathf.Clamp(elbowPitch + (elbowInput * elbowSpeed * deltaTime), elbowPitchLimit.x, elbowPitchLimit.y);

            rig.ApplyLeftArm(shoulderPitch, shoulderYaw, elbowPitch);
        }
    }
}
