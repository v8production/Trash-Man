using UnityEngine;

public abstract class TitanBaseArmRoleController : TitanBaseController
{
    [Header("Shoulder Mouse Mapping")]
    [SerializeField] private float shoulderRadiusPixels = 260f;
    [SerializeField] private bool useScreenCenterAsOrigin = true;
    [SerializeField] private Vector2 mouseOriginPixels = new(960f, 540f);
    [SerializeField] private float shoulderSpeed = 1f;

    [Header("Elbow Input")]
    [SerializeField] private float elbowSpeed = 120f;
    [SerializeField] private Vector2 shoulderRollLimit = new(-15f, 45f);
    [SerializeField] private Vector2 elbowPitchLimit = new(-130f, 15f);

    [Header("Idle Return")]
    [SerializeField] private float idleReturnSpeed = 10f;

    protected abstract bool IsLeftArm { get; }

    public override void TickRoleInput(in TitanAggregatedInput input, float deltaTime)
    {
        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        Vector2 mousePosition = input.MousePosition;
        Vector2 mouseDelta = input.MouseDelta;
        float sensitivity = Managers.Input.GetTitanMouseSensitivity();
        Vector2 origin = useScreenCenterAsOrigin
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : mouseOriginPixels;
        float maxRollDegrees = Mathf.Max(Mathf.Abs(shoulderRollLimit.x), Mathf.Abs(shoulderRollLimit.y));
        float resolvedBaseRadius = Mathf.Max(0.01f, shoulderRadiusPixels);
        float normalizedX = Mathf.Clamp((mousePosition.x - origin.x) / resolvedBaseRadius, -1f, 1f);
        float pitchDegreesPerPixel = maxRollDegrees > 0f ? maxRollDegrees * sensitivity / resolvedBaseRadius : 0f;

        float targetRoll = normalizedX * maxRollDegrees * sensitivity;
        if (IsLeftArm)
            targetRoll = -targetRoll;

        TitanArmControlState state = Managers.TitanRig.GetArmState(left: IsLeftArm);
        float blend = 1f - Mathf.Exp(-shoulderSpeed * deltaTime);
        state.ShoulderRoll = Mathf.Lerp(state.ShoulderRoll, targetRoll, blend);
        state.ShoulderPitch += mouseDelta.y * pitchDegreesPerPixel;

        float elbowInput = IsLeftArm ? input.LeftArmElbow : -input.RightArmElbow;

        state.ShoulderRoll = Mathf.Clamp(state.ShoulderRoll, shoulderRollLimit.x, shoulderRollLimit.y);
        Vector2 resolvedElbowLimit = GetResolvedElbowPitchLimit();
        state.ElbowPitch = Mathf.Clamp(
            state.ElbowPitch - (elbowInput * elbowSpeed * deltaTime),
            resolvedElbowLimit.x,
            resolvedElbowLimit.y);

        Managers.TitanRig.SetArmState(left: IsLeftArm, state);
        Managers.TitanRig.ApplyArmPose(left: IsLeftArm);
    }

    public void TickIdle(float deltaTime)
    {
        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        TitanArmControlState state = Managers.TitanRig.GetArmState(left: IsLeftArm);
        float blend = 1f - Mathf.Exp(-idleReturnSpeed * deltaTime);

        state.ShoulderRoll = Mathf.Lerp(state.ShoulderRoll, 0f, blend);
        state.ShoulderPitch = Mathf.Lerp(state.ShoulderPitch, 0f, blend);
        state.ElbowPitch = Mathf.Lerp(state.ElbowPitch, 0f, blend);

        state.ShoulderRoll = Mathf.Clamp(state.ShoulderRoll, shoulderRollLimit.x, shoulderRollLimit.y);
        Vector2 resolvedElbowLimit = GetResolvedElbowPitchLimit();
        state.ElbowPitch = Mathf.Clamp(state.ElbowPitch, resolvedElbowLimit.x, resolvedElbowLimit.y);

        Managers.TitanRig.SetArmState(left: IsLeftArm, state);
        Managers.TitanRig.ApplyArmPose(left: IsLeftArm);
    }

    private Vector2 GetResolvedElbowPitchLimit()
    {
        if (IsLeftArm)
            return elbowPitchLimit;

        return new Vector2(-elbowPitchLimit.y, -elbowPitchLimit.x);
    }
}
