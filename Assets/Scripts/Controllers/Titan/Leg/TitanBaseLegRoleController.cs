using UnityEngine;

public abstract class TitanBaseLegRoleController : TitanBaseController
{
    public enum LegSide
    {
        Left,
        Right,
    }

    [Header("Input")]
    [SerializeField] private TitanLegAnchorResolver legAnchorResolver;

    [Header("Hip Mouse Mapping")]
    [SerializeField] private float hipRadiusPixels = 260f;
    [SerializeField] private bool useScreenCenterAsOrigin = true;
    [SerializeField] private Vector2 mouseOriginPixels = new(960f, 540f);
    [SerializeField] private Vector2 hipYawLimit = new(-40f, 40f);
    [SerializeField] private Vector2 hipRollLimit = new(-90f, 45f);

    [Header("Hip Response")]
    [SerializeField] private float hipSpeed = 2f;

    [Header("Knee Input")]
    [SerializeField] private float kneeSpeed = 110f;
    [SerializeField] private Vector2 kneeRollLimit = new(-5f, 125f);

    [Header("Idle Return")]
    [SerializeField] private float idleReturnSpeed = 12f;

    protected abstract bool IsLeftLeg { get; }

    // Runtime origin capture to prevent pose jumps when the active role switches.
    private float _lastRoleInputTime = -999f;
    private Vector2 _capturedMouseOrigin;
    private bool _hasCapturedMouseOrigin;

    protected override void Awake()
    {
        base.Awake();
        ResolveDependencies();
    }

    public override void TickRoleInput(in TitanAggregatedInput input, float deltaTime)
    {
        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        ResolveDependencies();

        // If this controller has not received input ticks recently, treat this as a role activation edge
        // and capture the current mouse position as a neutral origin. This prevents an immediate pose
        // snap if the mouse is far from screen center at the moment of role switch.
        float now = Time.unscaledTime;
        bool roleActivated = (now - _lastRoleInputTime) > 0.25f;
        _lastRoleInputTime = now;
        if (roleActivated)
        {
            _capturedMouseOrigin = input.MousePosition;
            _hasCapturedMouseOrigin = true;
        }

        TitanLegInputCommand command = EvaluateLegInput(input, Managers.Input.GetTitanMouseSensitivity());
        legAnchorResolver?.UpdateDetachState(command.Side, command.DetachHeld);

        TitanLegControlState state = Managers.TitanRig.GetLegState(left: IsLeftLeg);
        if (legAnchorResolver != null && legAnchorResolver.TryApplyAnchoredMovement(command.Side, command, state, deltaTime))
        {
            if (legAnchorResolver.AreBothFeetAttached())
            {
                // Both feet attached: leg input must not move either leg pose.
                // Root is hard-locked in TitanLegAnchorResolver.
                return;
            }

            // IMPORTANT:
            // When the foot is anchored, we move the movement root inversely to the commanded hip delta.
            // If we don't also advance the leg state toward the command, the same delta is observed again
            // next frame and the root keeps accumulating rotation, causing runaway spinning.
            state.HipYaw = command.TargetHipYaw;
            state.HipRoll = command.TargetHipRoll;
            Managers.TitanRig.SetLegState(left: IsLeftLeg, state);
            Managers.TitanRig.ApplyLegPose(left: IsLeftLeg);
            return;
        }

        float blend = 1f - Mathf.Exp(-hipSpeed * deltaTime);
        state.HipYaw = Mathf.Lerp(state.HipYaw, command.TargetHipYaw, blend);
        state.HipRoll = Mathf.Lerp(state.HipRoll, command.TargetHipRoll, blend);

        state.HipYaw = Mathf.Clamp(state.HipYaw, hipYawLimit.x, hipYawLimit.y);
        state.HipRoll = Mathf.Clamp(state.HipRoll, hipRollLimit.x, hipRollLimit.y);
        state.KneeRoll = Mathf.Clamp(
            state.KneeRoll + (command.KneeInput * kneeSpeed * deltaTime),
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

        ResolveDependencies();
        state.HipYaw = Mathf.Clamp(state.HipYaw, hipYawLimit.x, hipYawLimit.y);
        state.HipRoll = Mathf.Clamp(state.HipRoll, hipRollLimit.x, hipRollLimit.y);
        state.KneeRoll = Mathf.Clamp(state.KneeRoll, kneeRollLimit.x, kneeRollLimit.y);

        Managers.TitanRig.SetLegState(left: IsLeftLeg, state);
        Managers.TitanRig.ApplyLegPose(left: IsLeftLeg);
    }

    private void ResolveDependencies()
    {
        legAnchorResolver ??= GetComponent<TitanLegAnchorResolver>();
    }

    private TitanLegInputCommand EvaluateLegInput(in TitanAggregatedInput input, float sensitivity)
    {
        Vector2 origin;
        if (_hasCapturedMouseOrigin)
        {
            origin = _capturedMouseOrigin;
        }
        else
        {
            origin = useScreenCenterAsOrigin
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : mouseOriginPixels;
        }

        float resolvedBaseRadius = Mathf.Max(0.01f, hipRadiusPixels);
        float normalizedX = Mathf.Clamp((input.MousePosition.x - origin.x) / resolvedBaseRadius, -1f, 1f);
        float normalizedY = Mathf.Clamp((input.MousePosition.y - origin.y) / resolvedBaseRadius, -1f, 1f);
        float maxYawDegrees = Mathf.Max(Mathf.Abs(hipYawLimit.x), Mathf.Abs(hipYawLimit.y));
        float maxRollDegrees = Mathf.Max(Mathf.Abs(hipRollLimit.x), Mathf.Abs(hipRollLimit.y));

        float targetYaw = Mathf.Clamp(normalizedX * maxYawDegrees * sensitivity, hipYawLimit.x, hipYawLimit.y);
        float targetRoll = Mathf.Clamp(-normalizedY * maxRollDegrees * sensitivity, hipRollLimit.x, hipRollLimit.y);
        float kneeInput = IsLeftLeg ? input.LeftLegKnee : input.RightLegKnee;

        return new TitanLegInputCommand
        {
            Side = IsLeftLeg ? LegSide.Left : LegSide.Right,
            MousePosition = input.MousePosition,
            MouseDelta = input.MouseDelta,
            TargetHipYaw = targetYaw,
            TargetHipRoll = targetRoll,
            KneeInput = kneeInput,
            DetachHeld = input.RightMouseDetachBuffered || input.RightMouseHeld || input.RightMousePressedThisFrame,
        };
    }
}

[System.Serializable]
public struct TitanLegInputCommand
{
    public TitanBaseLegRoleController.LegSide Side;
    public Vector2 MousePosition;
    public Vector2 MouseDelta;
    public float TargetHipYaw;
    public float TargetHipRoll;
    public float KneeInput;
    public bool DetachHeld;
}
