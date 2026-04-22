using UnityEngine;

public abstract class TitanBaseLegRoleController : TitanBaseController
{
    [Header("Hip Mouse Mapping")]
    [SerializeField] private float maxThetaDegrees = 55f;
    [SerializeField] private float thetaRadiusPixels = 260f;
    [SerializeField] private bool useScreenCenterAsOrigin = true;
    [SerializeField] private Vector2 mouseOriginPixels = new(960f, 540f);
    [SerializeField] private bool useSingleAxisMouseAssist = true;
    [SerializeField] private float singleAxisDeadZonePixels = 2f;
    [SerializeField, Range(0.05f, 1f)] private float mouseSensitivity = 0.35f;
    [SerializeField] private float mouseResponseSpeed = 12f;

    [Header("Knee Input")]
    [SerializeField] private float kneeSpeed = 110f;
    [SerializeField] private Vector2 hipYawLimit = new(-40f, 40f);
    [SerializeField] private Vector2 hipRollLimit = new(-90f, 45f);
    [SerializeField] private Vector2 kneeRollLimit = new(-5f, 125f);

    [Header("Idle Return")]
    [SerializeField] private float idleReturnSpeed = 12f;

    protected abstract bool IsLeftLeg { get; }

    public override void TickRoleInput(float deltaTime)
    {
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
            secondaryMaxDegrees: Mathf.Max(Mathf.Abs(hipRollLimit.x), Mathf.Abs(hipRollLimit.y)),
            applySensitivityToSecondary: false);

        float targetYaw = targetAngles.x;
        float targetRoll = targetAngles.y;

        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        TitanLegControlState state = Managers.TitanRig.GetLegState(left: IsLeftLeg);
        float blend = 1f - Mathf.Exp(-mouseResponseSpeed * deltaTime);
        state.HipYaw = Mathf.Lerp(state.HipYaw, targetYaw, blend);
        state.HipRoll = Mathf.Lerp(state.HipRoll, targetRoll, blend);

        float kneeInput = IsLeftLeg ? input.LeftLegKnee : input.RightLegKnee;

        state.HipYaw = Mathf.Clamp(state.HipYaw, hipYawLimit.x, hipYawLimit.y);
        state.HipRoll = Mathf.Clamp(state.HipRoll, hipRollLimit.x, hipRollLimit.y);
        state.KneeRoll = Mathf.Clamp(
            state.KneeRoll + (kneeInput * kneeSpeed * deltaTime),
            kneeRollLimit.x,
            kneeRollLimit.y);

        Managers.TitanRig.SetLegState(left: IsLeftLeg, state);
        Managers.TitanRig.ApplyLegPose(left: IsLeftLeg);
    }

    public void TickIdle(float deltaTime)
    {
        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        TitanLegControlState state = Managers.TitanRig.GetLegState(left: IsLeftLeg);
        float blend = 1f - Mathf.Exp(-idleReturnSpeed * deltaTime);

        state.HipYaw = Mathf.Lerp(state.HipYaw, 0f, blend);
        state.HipRoll = Mathf.Lerp(state.HipRoll, 0f, blend);
        state.KneeRoll = Mathf.Lerp(state.KneeRoll, 0f, blend);

        state.HipYaw = Mathf.Clamp(state.HipYaw, hipYawLimit.x, hipYawLimit.y);
        state.HipRoll = Mathf.Clamp(state.HipRoll, hipRollLimit.x, hipRollLimit.y);
        state.KneeRoll = Mathf.Clamp(state.KneeRoll, kneeRollLimit.x, kneeRollLimit.y);

        Managers.TitanRig.SetLegState(left: IsLeftLeg, state);
        Managers.TitanRig.ApplyLegPose(left: IsLeftLeg);
    }
}
