using UnityEngine;

public abstract class TitanBaseArmRoleController : TitanBaseController
{
    [Header("Shoulder Mouse Mapping")]
    [SerializeField] private float maxThetaDegrees = 55f;
    [SerializeField] private float thetaRadiusPixels = 260f;
    [SerializeField] private bool useScreenCenterAsOrigin = true;
    [SerializeField] private Vector2 mouseOriginPixels = new(960f, 540f);
    [SerializeField] private bool useSingleAxisMouseAssist = true;
    [SerializeField] private float singleAxisDeadZonePixels = 2f;
    [SerializeField, Range(0.05f, 1f)] private float mouseSensitivity = 0.35f;
    [SerializeField] private float mouseResponseSpeed = 5f;

    [Header("Elbow Input")]
    [SerializeField] private float elbowSpeed = 120f;
    [SerializeField] private Vector2 shoulderYawLimit = new(-15f, 45f);
    [SerializeField] private Vector2 shoulderPitchLimit = new(-360f, 360f);
    [SerializeField] private Vector2 elbowPitchLimit = new(-130f, 15f);

    protected abstract bool IsLeftArm { get; }

    public override void TickRoleInput(float deltaTime)
    {
        if (Managers.TitanRig == null || !Managers.TitanRig.EnsureReady())
        {
            return;
        }

        TitanAggregatedInput input = GetInputSnapshot();
        Vector2 mousePosition = input.MousePosition;
        Vector2 origin = useScreenCenterAsOrigin
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : mouseOriginPixels;

        if (useSingleAxisMouseAssist)
        {
            Vector2 fromOrigin = mousePosition - origin;
            Vector2 dominant = TitanInputUtility.KeepDominantAxis(fromOrigin, singleAxisDeadZonePixels);
            mousePosition = origin + dominant;
        }

        Vector2 targetAngles = TitanInputUtility.ComputeSphericalAngles(
            mousePosition,
            origin,
            thetaRadiusPixels,
            maxThetaDegrees,
            mouseSensitivity,
            secondaryMaxDegrees: 360f,
            applySensitivityToSecondary: false);

        float normalizedX = thetaRadiusPixels > 0f
            ? Mathf.Clamp((mousePosition.x - origin.x) / thetaRadiusPixels, -1f, 1f)
            : 0f;
        float yawT = Mathf.InverseLerp(-1f, 1f, normalizedX);
        if (IsLeftArm)
        {
            yawT = 1f - yawT;
        }

        float targetYaw = Mathf.Lerp(shoulderYawLimit.x, shoulderYawLimit.y, yawT);
        float targetPitch = targetAngles.y;

        TitanArmControlState state = Managers.TitanRig.GetArmState(left: IsLeftArm);
        float blend = 1f - Mathf.Exp(-mouseResponseSpeed * deltaTime);
        state.ShoulderYaw = Mathf.Lerp(state.ShoulderYaw, targetYaw, blend);
        state.ShoulderPitch = Mathf.Lerp(state.ShoulderPitch, targetPitch, blend);

        float elbowInput = IsLeftArm ? input.LeftArmElbow : input.RightArmElbow;

        state.ShoulderPitch = Mathf.Clamp(state.ShoulderPitch, shoulderPitchLimit.x, shoulderPitchLimit.y);
        state.ShoulderYaw = Mathf.Clamp(state.ShoulderYaw, shoulderYawLimit.x, shoulderYawLimit.y);
        state.ElbowPitch = Mathf.Clamp(
            state.ElbowPitch - (elbowInput * elbowSpeed * deltaTime),
            elbowPitchLimit.x,
            elbowPitchLimit.y);

        Managers.TitanRig.SetArmState(left: IsLeftArm, state);
        Managers.TitanRig.ApplyArmPose(left: IsLeftArm);
    }
}
