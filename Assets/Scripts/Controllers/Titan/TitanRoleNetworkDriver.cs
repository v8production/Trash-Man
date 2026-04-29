using UnityEngine;

public class TitanRoleNetworkDriver : MonoBehaviour
{
    private float _nextDebugLogTime;
    private const float DebugLogIntervalSeconds = 0.50f;
    private bool _shouldLogThisFrame;
    [Header("Role Controllers")]
    [SerializeField] private TitanBodyRoleController _bodyController;
    [SerializeField] private TitanLegAnchorResolver _legAnchorResolver;
    [SerializeField] private TitanLeftArmRoleController _leftArmController;
    [SerializeField] private TitanRightArmRoleController _rightArmController;
    [SerializeField] private TitanLeftLegRoleController _leftLegController;
    [SerializeField] private TitanRightLegRoleController _rightLegController;

    private void Awake()
    {
        ResolveControllers();
    }

    private void FixedUpdate()
    {
        ResolveControllers();

        if (_bodyController == null && _leftArmController == null && _rightArmController == null && _leftLegController == null && _rightLegController == null)
            return;

        _shouldLogThisFrame = InputDebug.Enabled && Time.unscaledTime >= _nextDebugLogTime;
        if (_shouldLogThisFrame)
            _nextDebugLogTime = Time.unscaledTime + DebugLogIntervalSeconds;

        float dt = Time.fixedDeltaTime;

        TitanAggregatedInput leftLegInput = default;
        TitanAggregatedInput rightLegInput = default;
        bool hasLeftLegInput = TryGetLegRoleInput(true, out leftLegInput);
        bool hasRightLegInput = TryGetLegRoleInput(false, out rightLegInput);

        ApplyLegDetachPrepass(true, hasLeftLegInput, in leftLegInput, dt);
        ApplyLegDetachPrepass(false, hasRightLegInput, in rightLegInput, dt);

        TickBodyRole(dt);
        TickArmRole(true, dt);
        TickArmRole(false, dt);
        TickLegRole(true, hasLeftLegInput, in leftLegInput, dt);
        TickLegRole(false, hasRightLegInput, in rightLegInput, dt);
    }

    private void TickBodyRole(float dt)
    {
        if (_bodyController == null)
            return;

        bool anchorActive = _legAnchorResolver != null && _legAnchorResolver.HasAnyAttachedFoot();
        _bodyController.SetAnchorPhysicsOverride(anchorActive);
        bool ok = Managers.TitanRole.TryGetRoleInput(Define.TitanRole.Body, out TitanAggregatedInput input);
        _bodyController.SetInputEnabled(true);
        _bodyController.TickRoleInput(input, dt);
        _bodyController.TickPhysics(dt);
    }

    private void TickArmRole(bool left, float dt)
    {
        TitanBaseArmRoleController controller = left ? _leftArmController : _rightArmController;
        if (controller == null)
            return;

        Define.TitanRole role = left ? Define.TitanRole.LeftArm : Define.TitanRole.RightArm;
        bool ok = Managers.TitanRole.TryGetRoleInput(role, out TitanAggregatedInput input);

        if (ok)
        {
            controller.TickRoleInput(input, dt);
        }
    }

    private bool TryGetLegRoleInput(bool left, out TitanAggregatedInput input)
    {
        Define.TitanRole role = left ? Define.TitanRole.LeftLeg : Define.TitanRole.RightLeg;
        return Managers.TitanRole.TryGetRoleInput(role, out input);
    }

    private void ApplyLegDetachPrepass(bool left, bool hasInput, in TitanAggregatedInput input, float dt)
    {
        bool detachRequested = hasInput && (input.RightMouseDetachBuffered || input.RightMouseHeld || input.RightMousePressedThisFrame);

        TitanBaseLegRoleController controller = left ? _leftLegController : _rightLegController;

        if (!detachRequested || controller == null)
        {
            return;
        }

        controller.TickRoleInput(input, dt);
    }

    private void TickLegRole(bool left, bool hasInput, in TitanAggregatedInput input, float dt)
    {
        TitanBaseLegRoleController controller = left ? _leftLegController : _rightLegController;
        if (controller == null)
        {
            return;
        }

        bool detachRequested = hasInput && (input.RightMouseDetachBuffered || input.RightMouseHeld || input.RightMousePressedThisFrame);

        if (hasInput && !detachRequested)
        {
            controller.TickRoleInput(input, dt);
        }
    }

    private void ResolveControllers()
    {
        _bodyController ??= GetComponent<TitanBodyRoleController>();
        _legAnchorResolver ??= GetComponent<TitanLegAnchorResolver>();
        _leftArmController ??= GetComponent<TitanLeftArmRoleController>();
        _rightArmController ??= GetComponent<TitanRightArmRoleController>();
        _leftLegController ??= GetComponent<TitanLeftLegRoleController>();
        _rightLegController ??= GetComponent<TitanRightLegRoleController>();
    }

}
