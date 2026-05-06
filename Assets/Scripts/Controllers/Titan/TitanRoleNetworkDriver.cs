using Unity.Netcode;
using UnityEngine;

public class TitanRoleNetworkDriver : MonoBehaviour
{
    private float _nextDebugLogTime;
    private const float DebugLogIntervalSeconds = 0.50f;
    private bool _shouldLogThisFrame;
    [Header("Role Controllers")]
    [SerializeField] private TitanTorsoRoleController _torsoController;
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

        if (_torsoController == null && _leftArmController == null && _rightArmController == null && _leftLegController == null && _rightLegController == null)
            return;

        _shouldLogThisFrame = InputDebug.Enabled && Time.unscaledTime >= _nextDebugLogTime;
        if (_shouldLogThisFrame)
            _nextDebugLogTime = Time.unscaledTime + DebugLogIntervalSeconds;

        float dt = Time.fixedDeltaTime;

        TitanAggregatedInput leftLegInput = default;
        TitanAggregatedInput rightLegInput = default;
        bool hasLeftLegInput = TryGetLegRoleInput(true, out leftLegInput);
        bool hasRightLegInput = TryGetLegRoleInput(false, out rightLegInput);

        ApplyLegAttachInput(true, in leftLegInput, hasLeftLegInput);
        ApplyLegAttachInput(false, in rightLegInput, hasRightLegInput);

        TickTorsoRole(dt);
        TickArmRole(true, dt);
        TickArmRole(false, dt);
        TickLegRole(true, hasLeftLegInput, in leftLegInput, dt);
        TickLegRole(false, hasRightLegInput, in rightLegInput, dt);

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

    private void TickTorsoRole(float dt)
    {
        if (_torsoController == null)
            return;

        bool anchorActive = _legAnchorResolver != null && _legAnchorResolver.HasAnyAttachedFoot();
        _torsoController.SetAnchorPhysicsOverride(anchorActive);
        Managers.TitanRole.TryGetRoleInput(Define.TitanRole.Torso, out TitanAggregatedInput input);
        _torsoController.SetInputEnabled(true);
        _torsoController.TickRoleInput(input, dt);
        _torsoController.TickPhysics(dt);
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

    private void ApplyLegAttachInput(bool left, in TitanAggregatedInput activeInput, bool hasActiveInput)
    {
        TitanBaseLegRoleController controller = left ? _leftLegController : _rightLegController;
        if (controller == null)
        {
            return;
        }

        if (hasActiveInput)
        {
            controller.TickAttachInput(activeInput);
            return;
        }

        Define.TitanRole role = left ? Define.TitanRole.LeftLeg : Define.TitanRole.RightLeg;
        if (Managers.TitanRole.TryGetSelectedRoleInput(role, out TitanAggregatedInput selectedInput))
        {
            controller.TickAttachInput(selectedInput);
            return;
        }

        controller.TickAttachInput(default);
    }

    private void TickLegRole(bool left, bool hasInput, in TitanAggregatedInput input, float dt)
    {
        TitanBaseLegRoleController controller = left ? _leftLegController : _rightLegController;
        if (controller == null)
        {
            return;
        }

        if (hasInput)
        {
            controller.TickRoleInput(input, dt);
        }
    }

    private void ResolveControllers()
    {
        _torsoController ??= GetComponent<TitanTorsoRoleController>();
        _legAnchorResolver ??= GetComponent<TitanLegAnchorResolver>();
        _leftArmController ??= GetComponent<TitanLeftArmRoleController>();
        _rightArmController ??= GetComponent<TitanRightArmRoleController>();
        _leftLegController ??= GetComponent<TitanLeftLegRoleController>();
        _rightLegController ??= GetComponent<TitanRightLegRoleController>();
    }

}
