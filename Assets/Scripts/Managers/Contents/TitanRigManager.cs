using UnityEngine;

public sealed class TitanRigManager
{
    private TitanRigRuntime runtime;

    public TitanRigRuntime Runtime => runtime;
    public bool IsBound => runtime != null;
    public GameObject RuntimeGameObject => runtime != null ? runtime.gameObject : null;
    public Transform MovementRoot => runtime != null ? runtime.MovementRoot : null;
    public Rigidbody MovementRigidbody => runtime != null ? runtime.MovementRigidbody : null;
    public Transform Spine => runtime != null ? runtime.Spine : null;
    public Transform LeftShoulder => runtime != null ? runtime.LeftShoulder : null;
    public Transform LeftElbow => runtime != null ? runtime.LeftElbow : null;
    public Transform RightShoulder => runtime != null ? runtime.RightShoulder : null;
    public Transform RightElbow => runtime != null ? runtime.RightElbow : null;
    public Transform LeftHip => runtime != null ? runtime.LeftHip : null;
    public Transform LeftKnee => runtime != null ? runtime.LeftKnee : null;
    public Transform LeftFoot => runtime != null ? runtime.LeftFoot : null;
    public Transform RightHip => runtime != null ? runtime.RightHip : null;
    public Transform RightKnee => runtime != null ? runtime.RightKnee : null;
    public Transform RightFoot => runtime != null ? runtime.RightFoot : null;
    public float WaistYaw => runtime != null ? runtime.WaistYaw : 0f;


    public TitanRigManager() { }

    public bool EnsureBoundFromScene()
    {
        if (runtime != null)
        {
            return true;
        }

        runtime = Object.FindAnyObjectByType<TitanRigRuntime>();
        return runtime != null;
    }

    public void Init()
    {
        if (EnsureBoundFromScene())
        {
            runtime.Init();
        }
    }

    public bool TryGetRuntime(out TitanRigRuntime value)
    {
        if (runtime == null)
        {
            EnsureBoundFromScene();
        }

        value = runtime;
        return value != null;
    }

    public bool EnsureBoundTo(GameObject target)
    {
        if (target == null)
        {
            return EnsureBoundFromScene();
        }

        if (runtime != null && runtime.gameObject == target)
        {
            return true;
        }

        if (runtime != null && runtime.gameObject != target)
        {
            Debug.LogWarning($"[TitanRigManager] Rebinding runtime from '{runtime.gameObject.name}' to '{target.name}'.");
        }

        TitanRigRuntime found = target.GetComponent<TitanRigRuntime>();
        if (found == null)
        {
            Debug.LogError($"[TitanRigManager] Missing TitanRigRuntime on '{target.name}'. Add it to the Titan prefab.");
            runtime = null;
            return false;
        }

        runtime = found;
        return true;
    }

    public void Bind(TitanRigRuntime value)
    {
        runtime = value;
        runtime?.Init();
    }

    public void UnbindIfOwner(TitanRigRuntime owner)
    {
        if (owner != null && runtime == owner)
        {
            runtime = null;
        }
    }

    public void Clear()
    {
        if (runtime != null)
        {
            runtime.Clear();
        }

        runtime = null;
    }

    public bool EnsureReady()
    {
        return (runtime != null || EnsureBoundFromScene()) && runtime.EnsureReady();
    }

    public void SetWaistYaw(float value)
    {
        if (!EnsureBoundFromScene())
        {
            return;
        }

        runtime.SetWaistYaw(value);
    }

    public TitanArmControlState GetArmState(bool left)
    {
        if (!EnsureBoundFromScene())
        {
            return default;
        }

        return runtime.GetArmState(left);
    }

    public void SetArmState(bool left, TitanArmControlState state)
    {
        if (!EnsureBoundFromScene())
        {
            return;
        }

        runtime.SetArmState(left, state);
    }

    public TitanLegControlState GetLegState(bool left)
    {
        if (!EnsureBoundFromScene())
        {
            return default;
        }

        return runtime.GetLegState(left);
    }

    public void SetLegState(bool left, TitanLegControlState state)
    {
        if (!EnsureBoundFromScene())
        {
            return;
        }

        runtime.SetLegState(left, state);
    }

    public void ApplyBodyPose()
    {
        if (!EnsureBoundFromScene())
        {
            return;
        }

        runtime.ApplyBodyPose();
    }

    public void ApplyArmPose(bool left)
    {
        if (!EnsureBoundFromScene())
        {
            return;
        }

        runtime.ApplyArmPose(left);
    }

    public void ApplyLegPose(bool left)
    {
        if (!EnsureBoundFromScene())
        {
            return;
        }

        runtime.ApplyLegPose(left);
    }

    public bool TryGetPoseSnapshot(out TitanRigPoseSnapshot snapshot)
    {
        snapshot = default;
        return EnsureBoundFromScene() && runtime.TryGetPoseSnapshot(out snapshot);
    }

    public void ApplyMovementRootPose(Vector3 worldPosition, Quaternion worldRotation, bool zeroVelocities)
    {
        if (!EnsureBoundFromScene())
        {
            return;
        }

        runtime.ApplyMovementRootPose(worldPosition, worldRotation, zeroVelocities);
    }

    public void ApplyPoseSnapshot(in TitanRigPoseSnapshot snapshot)
    {
        if (!EnsureBoundFromScene())
        {
            return;
        }

        runtime.ApplyPoseSnapshot(snapshot);
    }
}

[System.Serializable]
public struct TitanRigPoseSnapshot
{
    public Vector3 RootPosition;
    public Quaternion RootRotation;

    public bool HasLeftShoulder;
    public Quaternion LeftShoulderRotation;
    public bool HasLeftElbow;
    public Quaternion LeftElbowRotation;

    public bool HasRightShoulder;
    public Quaternion RightShoulderRotation;
    public bool HasRightElbow;
    public Quaternion RightElbowRotation;

    public bool HasLeftHip;
    public Quaternion LeftHipRotation;
    public bool HasLeftKnee;
    public Quaternion LeftKneeRotation;
    public bool HasLeftFoot;
    public Quaternion LeftFootRotation;

    public bool HasRightHip;
    public Quaternion RightHipRotation;
    public bool HasRightKnee;
    public Quaternion RightKneeRotation;
    public bool HasRightFoot;
    public Quaternion RightFootRotation;

    public bool HasSpine;
    public Quaternion SpineRotation;
}

[System.Serializable]
public struct TitanArmControlState
{
    public float ShoulderRoll;
    public float ShoulderPitch;
    public float ElbowPitch;
}

[System.Serializable]
public struct TitanLegControlState
{
    public float HipYaw;
    public float HipRoll;
    public float KneeRoll;
    public float AnkleRoll;
}
