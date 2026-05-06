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

    [Header("Ankle Input")]
    [SerializeField] private float ankleSpeed = 110f;
    [SerializeField] private Vector2 ankleRollLimit = new(-80f, 80f);

    [Header("Idle Return")]
    [SerializeField] private float idleReturnSpeed = 12f;

    protected abstract bool IsLeftLeg { get; }

    // Runtime origin capture to prevent pose jumps when the active role switches.
    private float _lastRoleInputTime = -999f;
    private Vector2 _capturedMouseOrigin;
    private bool _hasCapturedMouseOrigin;

    private float _activationHipYaw;
    private float _activationHipRoll;

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

        TitanLegControlState state = Managers.TitanRig.GetLegState(left: IsLeftLeg);

        float now = Time.unscaledTime;
        bool roleActivated = (now - _lastRoleInputTime) > 0.25f;
        _lastRoleInputTime = now;

        if (roleActivated)
        {
            _capturedMouseOrigin = input.MousePosition;
            _hasCapturedMouseOrigin = true;

            _activationHipYaw = state.HipYaw;
            _activationHipRoll = state.HipRoll;
        }

        TitanLegInputCommand command = EvaluateLegInput(
            input,
            Managers.Input.GetTitanMouseSensitivity()
        );

        legAnchorResolver?.UpdateDetachState(command.Side, command.DetachHeld);

        if (legAnchorResolver != null &&
            legAnchorResolver.TryApplyAnchoredMovement(command.Side, command, state, deltaTime))
        {
            if (legAnchorResolver.AreBothFeetAttached())
            {
                return;
            }

            if (roleActivated)
            {
                return;
            }

            float sensitivity = Managers.Input.GetTitanMouseSensitivity();

            float yawDelta = command.MouseDelta.x * 0.12f * sensitivity;
            float rollDelta = -command.MouseDelta.y * 0.12f * sensitivity;

            legAnchorResolver.ApplyInverseRootFromHipDelta(
                command.Side,
                yawDelta,
                rollDelta
            );

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
            kneeRollLimit.y
        );

        state.AnkleRoll = Mathf.Clamp(
            state.AnkleRoll + (command.AnkleInput * ankleSpeed * deltaTime),
            ankleRollLimit.x,
            ankleRollLimit.y
        );

        Managers.TitanRig.SetLegState(left: IsLeftLeg, state);
        Managers.TitanRig.ApplyLegPose(left: IsLeftLeg);
    }

    public void TickIdle(float deltaTime)
    {
        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        ResolveDependencies();

        if (legAnchorResolver != null &&
            legAnchorResolver.IsFootAttached(IsLeftLeg ? LegSide.Left : LegSide.Right))
        {
            return;
        }

        TitanLegControlState state = Managers.TitanRig.GetLegState(left: IsLeftLeg);

        float blend = 1f - Mathf.Exp(-idleReturnSpeed * deltaTime);

        state.HipYaw = Mathf.Lerp(state.HipYaw, 0f, blend);
        state.HipRoll = Mathf.Lerp(state.HipRoll, 0f, blend);
        state.KneeRoll = Mathf.Lerp(state.KneeRoll, 0f, blend);
        state.AnkleRoll = Mathf.Lerp(state.AnkleRoll, 0f, blend);

        state.HipYaw = Mathf.Clamp(state.HipYaw, hipYawLimit.x, hipYawLimit.y);
        state.HipRoll = Mathf.Clamp(state.HipRoll, hipRollLimit.x, hipRollLimit.y);
        state.KneeRoll = Mathf.Clamp(state.KneeRoll, kneeRollLimit.x, kneeRollLimit.y);
        state.AnkleRoll = Mathf.Clamp(state.AnkleRoll, ankleRollLimit.x, ankleRollLimit.y);

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

        float targetYaw = _activationHipYaw + (normalizedX * maxYawDegrees * sensitivity);
        float targetRoll = _activationHipRoll + (-normalizedY * maxRollDegrees * sensitivity);

        targetYaw = Mathf.Clamp(targetYaw, hipYawLimit.x, hipYawLimit.y);
        targetRoll = Mathf.Clamp(targetRoll, hipRollLimit.x, hipRollLimit.y);
        float kneeInput = IsLeftLeg ? -input.LeftLegKnee : -input.RightLegKnee;
        float ankleInput = IsLeftLeg ? -input.LeftLegAnkle : -input.RightLegAnkle;

        return new TitanLegInputCommand
        {
            Side = IsLeftLeg ? LegSide.Left : LegSide.Right,
            MousePosition = input.MousePosition,
            MouseDelta = input.MouseDelta,
            TargetHipYaw = targetYaw,
            TargetHipRoll = targetRoll,
            KneeInput = kneeInput,
            AnkleInput = ankleInput,
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
    public float AnkleInput;
    public bool DetachHeld;
}
