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
    [SerializeField] private float gravitySagSpeed = 10f;
    [SerializeField] private float gravityOverflowTorque = 70f;
    [SerializeField] private float upperArmMass = 1f;
    [SerializeField] private float lowerArmMass = 2f;

    [Header("Idle Return")]
    [SerializeField] private float idleReturnSpeed = 10f;

    protected abstract bool IsLeftArm { get; }

    private float _lastRoleInputTime = -999f;
    private Vector2 _capturedMouseOrigin;
    private bool _hasCapturedMouseOrigin;

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
            return;
        }

        TitanArmControlState state = Managers.TitanRig.GetArmState(left: IsLeftArm);
        float sideSign = IsLeftArm ? -1f : 1f;
        bool overflow = false;

        state.ShoulderPitch = ApplySag(state.ShoulderPitch, shoulderPitchLimit, 1f, upperArmMass + lowerArmMass, deltaTime, ref overflow);
        state.ShoulderRoll = ApplySag(state.ShoulderRoll, shoulderRollLimit, sideSign, upperArmMass + lowerArmMass, deltaTime, ref overflow);
        state.ElbowPitch = ApplySag(state.ElbowPitch, GetResolvedElbowPitchLimit(), -sideSign, lowerArmMass, deltaTime, ref overflow);

        Managers.TitanRig.SetArmState(left: IsLeftArm, state);
        Managers.TitanRig.ApplyArmPose(left: IsLeftArm);

        if (overflow && legAnchorResolver != null)
        {
            legAnchorResolver.ReleaseAllFeetForGravityOverflow();
            Transform root = Managers.TitanRig.MovementRoot;
            Vector3 axis = root != null ? root.right : Vector3.right;
            legAnchorResolver.ApplyGravityOverflowTorque(axis, sideSign, gravityOverflowTorque);
        }
    }

    private Vector2 GetResolvedElbowPitchLimit()
    {
        if (IsLeftArm)
            return elbowPitchLimit;

        return new Vector2(-elbowPitchLimit.y, -elbowPitchLimit.x);
    }

    private float ApplySag(float current, Vector2 limit, float direction, float mass, float deltaTime, ref bool overflow)
    {
        if (mass <= 0f || Mathf.Abs(direction) < 0.001f)
        {
            return Mathf.Clamp(current, limit.x, limit.y);
        }

        float step = Mathf.Sign(direction) * gravitySagSpeed * mass * Mathf.Max(0f, deltaTime);
        float unclamped = current + step;
        float clamped = Mathf.Clamp(unclamped, limit.x, limit.y);

        if (!Mathf.Approximately(unclamped, clamped))
        {
            overflow = true;
        }

        return clamped;
    }
}
