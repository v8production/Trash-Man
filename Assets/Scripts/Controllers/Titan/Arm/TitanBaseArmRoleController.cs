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
    [SerializeField] private Vector2 shoulderPitchLimit = new(-60f, 60f);
    [SerializeField] private Vector2 shoulderRollLimit = new(-15f, 45f);
    [SerializeField] private Vector2 elbowPitchLimit = new(-130f, 15f);

    [Header("Gravity Sag")]
    [SerializeField] private bool gravitySagEnabled = true;
    [SerializeField] private float gravityTorqueToDegrees = 4f;
    [SerializeField] private float maxGravitySagDegreesPerSecond = 45f;
    [SerializeField] private float gravityTorqueDeadZoneDegreesPerSecond = 0.05f;
    [SerializeField] private float minRootUprightForGravitySag = 0.25f;
    [SerializeField] private Vector3 lowerArmCenterOfMassLocalOffset = new(0f, -0.18f, 0f);
    [SerializeField] private float upperArmMass = 1f;
    [SerializeField] private float lowerArmMass = 2f;

    [Header("Idle Return")]
    [SerializeField] private float idleReturnSpeed = 10f;

    protected abstract bool IsLeftArm { get; }

    private float _lastRoleInputTime = -999f;
    private Vector2 _capturedMouseOrigin;
    private bool _hasCapturedMouseOrigin;
    private bool _gravityFullyLimited;

    public bool IsGravityFullyLimited => _gravityFullyLimited;

    public override void TickRoleInput(in TitanAggregatedInput input, float deltaTime)
    {
        if (!Managers.TitanRig.EnsureReady())
        {
            return;
        }

        TitanArmControlState state = Managers.TitanRig.GetArmState(left: IsLeftArm);

        float now = Time.unscaledTime;
        bool roleActivated = (now - _lastRoleInputTime) > 0.25f;
        _lastRoleInputTime = now;

        if (roleActivated)
        {
            _capturedMouseOrigin = input.MousePosition;
            _hasCapturedMouseOrigin = true;
            return;
        }

        Vector2 mousePosition = input.MousePosition;
        Vector2 mouseDelta = input.MouseDelta;
        float sensitivity = Managers.Input.GetTitanMouseSensitivity();

        Vector2 origin = _hasCapturedMouseOrigin
            ? _capturedMouseOrigin
            : useScreenCenterAsOrigin
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : mouseOriginPixels;

        float maxRollDegrees = Mathf.Max(Mathf.Abs(shoulderRollLimit.x), Mathf.Abs(shoulderRollLimit.y));
        float resolvedBaseRadius = Mathf.Max(0.01f, shoulderRadiusPixels);
        float normalizedX = Mathf.Clamp((mousePosition.x - origin.x) / resolvedBaseRadius, -1f, 1f);

        float pitchDegreesPerPixel = maxRollDegrees > 0f
            ? maxRollDegrees * sensitivity / resolvedBaseRadius
            : 0f;

        float targetRoll = normalizedX * maxRollDegrees * sensitivity;

        if (IsLeftArm)
        {
            targetRoll = -targetRoll;
        }

        float blend = 1f - Mathf.Exp(-shoulderSpeed * deltaTime);

        state.ShoulderRoll = Mathf.Lerp(state.ShoulderRoll, targetRoll, blend);
        state.ShoulderPitch += mouseDelta.y * pitchDegreesPerPixel;

        float elbowInput = IsLeftArm ? input.LeftArmElbow : -input.RightArmElbow;

        state.ShoulderRoll = Mathf.Clamp(state.ShoulderRoll, shoulderRollLimit.x, shoulderRollLimit.y);
        state.ShoulderPitch = Mathf.Clamp(state.ShoulderPitch, shoulderPitchLimit.x, shoulderPitchLimit.y);

        Vector2 resolvedElbowLimit = GetResolvedElbowPitchLimit();

        state.ElbowPitch = Mathf.Clamp(
            state.ElbowPitch - (elbowInput * elbowSpeed * deltaTime),
            resolvedElbowLimit.x,
            resolvedElbowLimit.y
        );

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
        state.ShoulderPitch = Mathf.Clamp(state.ShoulderPitch, shoulderPitchLimit.x, shoulderPitchLimit.y);
        Vector2 resolvedElbowLimit = GetResolvedElbowPitchLimit();
        state.ElbowPitch = Mathf.Clamp(state.ElbowPitch, resolvedElbowLimit.x, resolvedElbowLimit.y);

        Managers.TitanRig.SetArmState(left: IsLeftArm, state);
        Managers.TitanRig.ApplyArmPose(left: IsLeftArm);
    }

    public void TickGravitySag(float deltaTime, TitanLegAnchorResolver legAnchorResolver)
    {
        if (!gravitySagEnabled || !Managers.TitanRig.EnsureReady())
        {
            _gravityFullyLimited = false;
            return;
        }

        TitanArmControlState state = Managers.TitanRig.GetArmState(left: IsLeftArm);
        Transform shoulder = IsLeftArm ? Managers.TitanRig.LeftShoulder : Managers.TitanRig.RightShoulder;
        Transform elbow = IsLeftArm ? Managers.TitanRig.LeftElbow : Managers.TitanRig.RightElbow;
        float gravitySagScale = ComputeGravitySagScale(Managers.TitanRig.MovementRoot);
        if (gravitySagScale <= 0f)
        {
            _gravityFullyLimited = false;
            return;
        }

        bool shoulderPitchLimited = false;
        bool shoulderRollLimited = false;
        bool elbowLimited = false;
        WeightedPoint armCenter = WeightedCenter(
            SegmentCenter(shoulder, elbow),
            upperArmMass,
            LowerArmCenter(elbow),
            lowerArmMass);

        state.ShoulderPitch = ApplyGravityTorque(state.ShoulderPitch, shoulderPitchLimit, shoulder, shoulder != null ? shoulder.right : Vector3.right, deltaTime, gravitySagScale, ref shoulderPitchLimited, armCenter);
        state.ShoulderRoll = ApplyGravityTorque(state.ShoulderRoll, shoulderRollLimit, shoulder, shoulder != null ? shoulder.forward : Vector3.forward, deltaTime, gravitySagScale, ref shoulderRollLimited, armCenter);
        state.ElbowPitch = ApplyGravityTorque(state.ElbowPitch, GetResolvedElbowPitchLimit(), elbow, elbow != null ? elbow.up : Vector3.up, deltaTime, gravitySagScale, ref elbowLimited, WeightedCenter(LowerArmCenter(elbow), lowerArmMass));
        _gravityFullyLimited = shoulderPitchLimited && shoulderRollLimited && elbowLimited;

        Managers.TitanRig.SetArmState(left: IsLeftArm, state);
        Managers.TitanRig.ApplyArmPose(left: IsLeftArm);
    }

    private Vector2 GetResolvedElbowPitchLimit()
    {
        if (IsLeftArm)
            return elbowPitchLimit;

        return new Vector2(-elbowPitchLimit.y, -elbowPitchLimit.x);
    }

    private float ApplyGravityTorque(float current, Vector2 limit, Transform joint, Vector3 worldAxis, float deltaTime, float gravitySagScale, ref bool limitedByGravity, WeightedPoint weightedPoint)
    {
        if (joint == null || weightedPoint.Mass <= 0f || worldAxis.sqrMagnitude < 0.0001f || gravitySagScale <= 0f)
        {
            return Mathf.Clamp(current, limit.x, limit.y);
        }

        Vector3 lever = weightedPoint.Position - joint.position;
        Vector3 gravityForce = Physics.gravity * weightedPoint.Mass;
        float torque = Vector3.Dot(worldAxis.normalized, Vector3.Cross(lever, gravityForce));
        float degreesPerSecond = torque * gravityTorqueToDegrees * gravitySagScale;
        if (Mathf.Abs(degreesPerSecond) <= gravityTorqueDeadZoneDegreesPerSecond)
        {
            return Mathf.Clamp(current, limit.x, limit.y);
        }

        float maxStep = maxGravitySagDegreesPerSecond * Mathf.Max(0f, deltaTime);
        float step = Mathf.Clamp(degreesPerSecond * Mathf.Max(0f, deltaTime), -maxStep, maxStep);
        step = ClampToDownwardCenterOfMassMotion(joint, worldAxis, weightedPoint.Position, step);
        if (Mathf.Abs(step) <= 0.0001f)
        {
            return Mathf.Clamp(current, limit.x, limit.y);
        }

        float unclamped = current + step;
        float clamped = Mathf.Clamp(unclamped, limit.x, limit.y);

        if (!Mathf.Approximately(unclamped, clamped))
        {
            limitedByGravity = true;
        }

        return clamped;
    }

    private static float ClampToDownwardCenterOfMassMotion(Transform joint, Vector3 worldAxis, Vector3 centerOfMass, float step)
    {
        Vector3 axis = worldAxis.normalized;
        Vector3 lever = centerOfMass - joint.position;
        float currentHeight = centerOfMass.y;
        float nextHeight = (joint.position + (Quaternion.AngleAxis(step, axis) * lever)).y;

        if (nextHeight <= currentHeight + 0.0001f)
        {
            return step;
        }

        float oppositeStep = -step;
        float oppositeHeight = (joint.position + (Quaternion.AngleAxis(oppositeStep, axis) * lever)).y;
        return oppositeHeight <= currentHeight + 0.0001f ? oppositeStep : 0f;
    }

    private float ComputeGravitySagScale(Transform root)
    {
        if (root == null)
        {
            return 1f;
        }

        float upright = Mathf.Abs(Vector3.Dot(root.up.normalized, Vector3.up));
        return Mathf.InverseLerp(Mathf.Clamp01(minRootUprightForGravitySag), 1f, upright);
    }

    private Vector3 LowerArmCenter(Transform elbow)
    {
        if (elbow == null)
        {
            return Vector3.zero;
        }

        return elbow.TransformPoint(lowerArmCenterOfMassLocalOffset);
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
        massA = Mathf.Max(0f, massA);
        massB = Mathf.Max(0f, massB);
        float mass = massA + massB;
        if (mass <= 0f)
        {
            return default;
        }

        return new WeightedPoint(((a * massA) + (b * massB)) / mass, mass);
    }
}
