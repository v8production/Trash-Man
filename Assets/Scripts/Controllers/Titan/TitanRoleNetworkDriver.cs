using Unity.Netcode;
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

    private bool _appliedClientPhysicsMode;

    private void Awake()
    {
        ResolveControllers();
    }

    private void FixedUpdate()
    {
        ResolveControllers();

        if (ShouldApplyServerPoseOnly())
        {
            ApplyClientPhysicsMode();
            ApplyLatestServerPose();
            return;
        }

        RestoreServerPhysicsMode();

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
        TickJointGravity(dt);
        ReleaseAnchorsIfGravityFullyLimited();

        PublishAuthoritativePose();
    }

    private static bool ShouldApplyServerPoseOnly()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null
            && networkManager.IsListening
            && networkManager.IsClient
            && !networkManager.IsServer;
    }

    private void ApplyClientPhysicsMode()
    {
        if (_appliedClientPhysicsMode)
            return;

        Rigidbody movementRigidbody = Managers.TitanRig.MovementRigidbody;
        if (movementRigidbody != null)
        {
            movementRigidbody.linearVelocity = Vector3.zero;
            movementRigidbody.angularVelocity = Vector3.zero;
            movementRigidbody.isKinematic = true;
        }

        _appliedClientPhysicsMode = true;
    }

    private void RestoreServerPhysicsMode()
    {
        if (!_appliedClientPhysicsMode)
            return;

        Rigidbody movementRigidbody = Managers.TitanRig.MovementRigidbody;
        if (movementRigidbody != null)
            movementRigidbody.isKinematic = false;

        _appliedClientPhysicsMode = false;
    }

    private static void ApplyLatestServerPose()
    {
        if (!LobbyNetworkPlayer.TryGetLatestTitanPose(out TitanRigPosePayload posePayload))
            return;

        Managers.TitanRig.ApplyPoseSnapshot(posePayload.ToSnapshot());
    }

    private static void PublishAuthoritativePose()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
            return;

        if (!Managers.TitanRig.TryGetPoseSnapshot(out TitanRigPoseSnapshot snapshot))
            return;

        LobbyNetworkPlayer.TryPublishServerTitanPose(new TitanRigPosePayload(snapshot));
    }

    private void TickBodyRole(float dt)
    {
        if (_bodyController == null)
            return;

        bool anchorActive = _legAnchorResolver != null && _legAnchorResolver.HasAnyAttachedFoot();
        _bodyController.SetAnchorPhysicsOverride(anchorActive);
        Managers.TitanRole.TryGetRoleInput(Define.TitanRole.Body, out TitanAggregatedInput input);
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

    private void TickJointGravity(float dt)
    {
        _leftArmController?.TickGravitySag(dt, _legAnchorResolver);
        _rightArmController?.TickGravitySag(dt, _legAnchorResolver);
        _leftLegController?.TickGravitySag(dt);
        _rightLegController?.TickGravitySag(dt);
    }

    private void ReleaseAnchorsIfGravityFullyLimited()
    {
        if (_legAnchorResolver == null || !_legAnchorResolver.HasAnyAttachedFoot())
        {
            return;
        }

        bool armsLimited = (_leftArmController == null || _leftArmController.IsGravityFullyLimited) &&
                           (_rightArmController == null || _rightArmController.IsGravityFullyLimited);
        bool legsLimited = (_leftLegController == null || _leftLegController.IsGravityFullyLimited) &&
                           (_rightLegController == null || _rightLegController.IsGravityFullyLimited);

        if (armsLimited && legsLimited)
        {
            float fallDirection = 0f;
            if (_leftLegController != null && _leftLegController.IsGravityFullyLimited)
            {
                fallDirection -= 1f;
            }
            if (_rightLegController != null && _rightLegController.IsGravityFullyLimited)
            {
                fallDirection += 1f;
            }

            _legAnchorResolver.ReleaseAllFeetForGravityLimit();
            _legAnchorResolver.ApplyGravityLimitFallTorque(Mathf.Abs(fallDirection) > 0.001f ? fallDirection : 1f);
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
