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

    [Header("Gravity Sag")]
    [SerializeField] private bool gravitySagEnabled = true;
    [SerializeField] private float gravityTorqueToDegrees = 4f;
    [SerializeField] private float maxGravitySagDegreesPerSecond = 45f;
    [SerializeField] private Vector3 footCenterOfMassLocalOffset = new(0f, 0f, 0.18f);
    [SerializeField] private float footMass = 1f;
    [SerializeField] private float calfMass = 1f;
    [SerializeField] private float thighMass = 1f;
    [SerializeField] private float pelvisMass = 1f;
    [SerializeField] private float bodyMass = 2f;

    [Header("Idle Return")]
    [SerializeField] private float idleReturnSpeed = 12f;

    protected abstract bool IsLeftLeg { get; }

    // Runtime origin capture to prevent pose jumps when the active role switches.
    private float _lastRoleInputTime = -999f;
    private Vector2 _capturedMouseOrigin;
    private bool _hasCapturedMouseOrigin;

    private float _activationHipYaw;
    private float _activationHipRoll;
    private bool _gravityFullyLimited;

    public bool IsGravityFullyLimited => _gravityFullyLimited;

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
            Transform anchoredKnee = IsLeftLeg ? Managers.TitanRig.LeftKnee : Managers.TitanRig.RightKnee;
            Transform anchoredFoot = IsLeftLeg ? Managers.TitanRig.LeftFoot : Managers.TitanRig.RightFoot;
            Vector3 kneeBeforePosition = anchoredKnee != null ? anchoredKnee.position : Vector3.zero;
            Quaternion kneeBeforeRotation = anchoredKnee != null ? anchoredKnee.rotation : Quaternion.identity;
            Vector3 footBeforePosition = anchoredFoot != null ? anchoredFoot.position : Vector3.zero;
            Quaternion footBeforeRotation = anchoredFoot != null ? anchoredFoot.rotation : Quaternion.identity;

            ApplyLegJointInputs(ref state, command, deltaTime, out float kneeDelta, out float ankleDelta);
            Managers.TitanRig.SetLegState(left: IsLeftLeg, state);
            Managers.TitanRig.ApplyLegPose(left: IsLeftLeg);

            if (legAnchorResolver.AreBothFeetAttached())
            {
                return;
            }

            legAnchorResolver.ApplyInverseRootFromAnchoredJointDeltas(
                command.Side,
                kneeDelta,
                ankleDelta,
                kneeBeforePosition,
                kneeBeforeRotation,
                footBeforePosition,
                footBeforeRotation
            );

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

        ApplyLegJointInputs(ref state, command, deltaTime, out _, out _);

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

    public void TickGravitySag(float deltaTime)
    {
        if (!gravitySagEnabled || !Managers.TitanRig.EnsureReady())
        {
            _gravityFullyLimited = false;
            return;
        }

        ResolveDependencies();

        TitanLegControlState state = Managers.TitanRig.GetLegState(left: IsLeftLeg);
        Transform hip = IsLeftLeg ? Managers.TitanRig.LeftHip : Managers.TitanRig.RightHip;
        Transform knee = IsLeftLeg ? Managers.TitanRig.LeftKnee : Managers.TitanRig.RightKnee;
        Transform foot = IsLeftLeg ? Managers.TitanRig.LeftFoot : Managers.TitanRig.RightFoot;
        Transform root = Managers.TitanRig.MovementRoot;
        Transform spine = Managers.TitanRig.Spine;
        bool hipLimited = false;
        bool kneeLimited = false;
        bool ankleLimited = false;

        state.HipRoll = ApplyGravityTorque(
            state.HipRoll,
            hipRollLimit,
            hip,
            hip != null ? hip.forward : Vector3.forward,
            deltaTime,
            ref hipLimited,
            WeightedCenter(
                SegmentCenter(hip, knee), thighMass,
                SegmentCenter(knee, foot), calfMass,
                FootCenter(foot), footMass,
                root != null ? root.position : Vector3.zero, pelvisMass,
                spine != null ? spine.position : root != null ? root.position + Vector3.up : Vector3.up, bodyMass));

        state.KneeRoll = ApplyGravityTorque(
            state.KneeRoll,
            kneeRollLimit,
            knee,
            knee != null ? knee.forward : Vector3.forward,
            deltaTime,
            ref kneeLimited,
            WeightedCenter(
                SegmentCenter(knee, foot), calfMass,
                FootCenter(foot), footMass));

        state.AnkleRoll = ApplyGravityTorque(
            state.AnkleRoll,
            ankleRollLimit,
            foot,
            foot != null ? foot.forward : Vector3.forward,
            deltaTime,
            ref ankleLimited,
            WeightedCenter(FootCenter(foot), footMass));

        _gravityFullyLimited = hipLimited && kneeLimited && ankleLimited;

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
        float kneeInput = IsLeftLeg ? input.LeftLegKnee : input.RightLegKnee;
        float ankleInput = IsLeftLeg ? input.LeftLegAnkle : input.RightLegAnkle;

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

    private void ApplyLegJointInputs(ref TitanLegControlState state, in TitanLegInputCommand command, float deltaTime, out float kneeDelta, out float ankleDelta)
    {
        float previousKnee = state.KneeRoll;
        float previousAnkle = state.AnkleRoll;

        float nextKnee = Mathf.Clamp(
            previousKnee + (command.KneeInput * kneeSpeed * deltaTime),
            kneeRollLimit.x,
            kneeRollLimit.y
        );

        float nextAnkle = Mathf.Clamp(
            previousAnkle + (command.AnkleInput * ankleSpeed * deltaTime),
            ankleRollLimit.x,
            ankleRollLimit.y
        );

        state.KneeRoll = nextKnee;
        state.AnkleRoll = nextAnkle;
        kneeDelta = nextKnee - previousKnee;
        ankleDelta = nextAnkle - previousAnkle;
    }

    private float ApplyGravityTorque(
        float current,
        Vector2 limit,
        Transform joint,
        Vector3 worldAxis,
        float deltaTime,
        ref bool limitedByGravity,
        WeightedPoint weightedPoint)
    {
        if (joint == null || weightedPoint.Mass <= 0f || worldAxis.sqrMagnitude < 0.0001f)
        {
            return Mathf.Clamp(current, limit.x, limit.y);
        }

        Vector3 lever = weightedPoint.Position - joint.position;
        Vector3 gravityForce = Physics.gravity * weightedPoint.Mass;
        float torque = Vector3.Dot(worldAxis.normalized, Vector3.Cross(lever, gravityForce));
        float maxStep = maxGravitySagDegreesPerSecond * Mathf.Max(0f, deltaTime);
        float step = Mathf.Clamp(torque * gravityTorqueToDegrees * Mathf.Max(0f, deltaTime), -maxStep, maxStep);
        float unclamped = current + step;
        float clamped = Mathf.Clamp(unclamped, limit.x, limit.y);

        if (!Mathf.Approximately(unclamped, clamped))
        {
            limitedByGravity = true;
        }

        return clamped;
    }

    private Vector3 FootCenter(Transform foot)
    {
        if (foot == null)
        {
            return Vector3.zero;
        }

        return foot.TransformPoint(footCenterOfMassLocalOffset);
    }

    private static Vector3 SegmentCenter(Transform start, Transform end)
    {
        if (start != null && end != null)
        {
            return (start.position + end.position) * 0.5f;
        }

        if (start != null)
        {
            return start.position;
        }

        return end != null ? end.position : Vector3.zero;
    }

    private static WeightedPoint WeightedCenter(Vector3 a, float massA)
    {
        return new WeightedPoint(a, Mathf.Max(0f, massA));
    }

    private static WeightedPoint WeightedCenter(Vector3 a, float massA, Vector3 b, float massB)
    {
        return WeightedCenter(a, massA, b, massB, Vector3.zero, 0f, Vector3.zero, 0f, Vector3.zero, 0f);
    }

    private static WeightedPoint WeightedCenter(Vector3 a, float massA, Vector3 b, float massB, Vector3 c, float massC, Vector3 d, float massD, Vector3 e, float massE)
    {
        massA = Mathf.Max(0f, massA);
        massB = Mathf.Max(0f, massB);
        massC = Mathf.Max(0f, massC);
        massD = Mathf.Max(0f, massD);
        massE = Mathf.Max(0f, massE);
        float mass = massA + massB + massC + massD + massE;
        if (mass <= 0f)
        {
            return default;
        }

        Vector3 position = ((a * massA) + (b * massB) + (c * massC) + (d * massD) + (e * massE)) / mass;
        return new WeightedPoint(position, mass);
    }
}

public readonly struct WeightedPoint
{
    public readonly Vector3 Position;
    public readonly float Mass;

    public WeightedPoint(Vector3 position, float mass)
    {
        Position = position;
        Mass = mass;
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
