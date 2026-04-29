using System.Collections.Generic;
using UnityEngine;

public sealed class TitanRigRuntime : MonoBehaviour
{
    [Header("Optional Bone Overrides")]
    [SerializeField] private Transform mechaRoot;
    [SerializeField] private Transform leftShoulder;
    [SerializeField] private Transform leftElbow;
    [SerializeField] private Transform rightShoulder;
    [SerializeField] private Transform rightElbow;
    [SerializeField] private Transform leftHip;
    [SerializeField] private Transform leftKnee;
    [SerializeField] private Transform rightHip;
    [SerializeField] private Transform rightKnee;
    [SerializeField] private Transform spine;

    [Header("Body")]
    [SerializeField] private float waistYaw;

    [Header("Arm States")]
    [SerializeField] private TitanArmControlState leftArm;
    [SerializeField] private TitanArmControlState rightArm;

    [Header("Leg States")]
    [SerializeField] private TitanLegControlState leftLeg;
    [SerializeField] private TitanLegControlState rightLeg;

    private Animator animator;

    private Quaternion leftShoulderBaseRotation;
    private Quaternion leftElbowBaseRotation;
    private Quaternion rightShoulderBaseRotation;
    private Quaternion rightElbowBaseRotation;
    private Quaternion leftHipBaseRotation;
    private Quaternion leftKneeBaseRotation;
    private Quaternion rightHipBaseRotation;
    private Quaternion rightKneeBaseRotation;
    private Quaternion spineBaseRotation;

    private bool warnedMissingBones;
    private bool loggedResolvedBones;
    private bool basePoseInitialized;
    private Rigidbody movementRigidbody;

    [ContextMenu("TitanRigRuntime/Bake Bone References")]
    private void BakeBoneReferencesContextMenu()
    {
        BakeBoneReferences();
    }

    public bool BakeBoneReferences()
    {
        ResolveAndCacheIfNeeded(forceCache: true);

        // Reset warnings so play mode can log a single clear message if still missing.
        warnedMissingBones = false;
        loggedResolvedBones = false;

        return HasAnyDrivenBone();
    }

    public Transform MovementRoot => mechaRoot != null ? mechaRoot : transform;
    public Transform LeftHip => leftHip;
    public Transform RightHip => rightHip;
    public Rigidbody MovementRigidbody
    {
        get
        {
            EnsureMovementRigidbodyCached();
            return movementRigidbody;
        }
    }
    public Transform Spine => spine;
    public float WaistYaw => waistYaw;

    public void Init()
    {
        ResolveAndCacheIfNeeded(forceCache: true);
        EnsureMovementRigidbodyCached();
    }

    public void Clear()
    {
        warnedMissingBones = false;
        loggedResolvedBones = false;
        basePoseInitialized = false;
        movementRigidbody = null;
    }

    public void ApplyMovementRootPose(Vector3 worldPosition, Quaternion worldRotation, bool zeroVelocities)
    {
        EnsureMovementRigidbodyCached();

        if (movementRigidbody != null)
        {
            movementRigidbody.position = worldPosition;
            movementRigidbody.rotation = worldRotation;

            if (zeroVelocities)
            {
                movementRigidbody.linearVelocity = Vector3.zero;
                movementRigidbody.angularVelocity = Vector3.zero;
            }

            return;
        }

        MovementRoot.SetPositionAndRotation(worldPosition, worldRotation);
    }

    public bool EnsureReady()
    {
        if (!basePoseInitialized || !HasAnyDrivenBone())
        {
            ResolveAndCacheIfNeeded(forceCache: false);
        }

        bool hasAnyDrivenBone = HasAnyDrivenBone();

        if (!hasAnyDrivenBone && !warnedMissingBones)
        {
            warnedMissingBones = true;
            string animatorName = animator != null ? animator.name : "<none>";
            bool isHuman = animator != null && animator.isHuman;
            Debug.LogWarning($"[TitanRigManager] Could not resolve any controllable bones. " +
                             $"Animator={animatorName}, isHuman={isHuman}. " +
                             $"Assign bone overrides on TitanRigRuntime (prefab) or fix the model rig.", this);
        }

        if (hasAnyDrivenBone && !loggedResolvedBones)
        {
            loggedResolvedBones = true;
            Debug.Log($"[TitanRigManager] Resolved bones - LS:{NameOrNone(leftShoulder)} LE:{NameOrNone(leftElbow)} RS:{NameOrNone(rightShoulder)} RE:{NameOrNone(rightElbow)} LH:{NameOrNone(leftHip)} LK:{NameOrNone(leftKnee)} RH:{NameOrNone(rightHip)} RK:{NameOrNone(rightKnee)} SP:{NameOrNone(spine)}", this);
        }

        return hasAnyDrivenBone;
    }

    public void SetWaistYaw(float value)
    {
        waistYaw = value;
    }

    public TitanArmControlState GetArmState(bool left)
    {
        return left ? leftArm : rightArm;
    }

    public void SetArmState(bool left, TitanArmControlState state)
    {
        if (left)
        {
            leftArm = state;
            return;
        }

        rightArm = state;
    }

    public TitanLegControlState GetLegState(bool left)
    {
        return left ? leftLeg : rightLeg;
    }

    public void SetLegState(bool left, TitanLegControlState state)
    {
        if (left)
        {
            leftLeg = state;
            return;
        }

        rightLeg = state;
    }

    public void ApplyBodyPose()
    {
        if (!EnsureReady())
        {
            return;
        }

        ApplySpine(0f, waistYaw, 0f);
    }

    public void ApplyArmPose(bool left)
    {
        if (!EnsureReady())
        {
            return;
        }

        TitanArmControlState state = left ? leftArm : rightArm;
        if (left)
        {
            ApplyLeftArm(state.ShoulderPitch, state.ShoulderRoll, state.ElbowPitch);
            return;
        }

        ApplyRightArm(state.ShoulderPitch, state.ShoulderRoll, state.ElbowPitch);
    }

    public void ApplyLegPose(bool left)
    {
        if (!EnsureReady())
        {
            return;
        }

        TitanLegControlState state = left ? leftLeg : rightLeg;
        if (left)
        {
            ApplyLeftLeg(state.HipYaw, state.HipRoll, state.KneeRoll);
            return;
        }

        ApplyRightLeg(state.HipYaw, state.HipRoll, state.KneeRoll);
    }

    public void ApplyLeftArm(float shoulderPitch, float shoulderRoll, float elbowPitch)
    {
        if (leftShoulder != null)
        {
            leftShoulder.localRotation = ComposeShoulderRotation(leftShoulderBaseRotation, shoulderPitch, shoulderRoll);
        }

        if (leftElbow != null)
        {
            leftElbow.localRotation = leftElbowBaseRotation * Quaternion.Euler(0f, elbowPitch, 0f);
        }
    }

    public void ApplyRightArm(float shoulderPitch, float shoulderRoll, float elbowPitch)
    {
        if (rightShoulder != null)
        {
            rightShoulder.localRotation = ComposeShoulderRotation(rightShoulderBaseRotation, shoulderPitch, shoulderRoll);
        }

        if (rightElbow != null)
        {
            rightElbow.localRotation = rightElbowBaseRotation * Quaternion.Euler(0f, elbowPitch, 0f);
        }
    }

    public void ApplyLeftLeg(float hipYaw, float hipRoll, float kneeRoll)
    {
        if (leftHip != null)
        {
            leftHip.localRotation = leftHipBaseRotation * Quaternion.Euler(0f, hipYaw, hipRoll);
        }

        if (leftKnee != null)
        {
            leftKnee.localRotation = leftKneeBaseRotation * Quaternion.Euler(0f, 0f, kneeRoll);
        }
    }

    public void ApplyRightLeg(float hipYaw, float hipRoll, float kneeRoll)
    {
        if (rightHip != null)
        {
            rightHip.localRotation = rightHipBaseRotation * Quaternion.Euler(0f, hipYaw, hipRoll);
        }

        if (rightKnee != null)
        {
            rightKnee.localRotation = rightKneeBaseRotation * Quaternion.Euler(0f, 0f, kneeRoll);
        }
    }

    public void ApplySpine(float yaw, float pitch = 0f, float roll = 0f)
    {
        if (spine != null)
        {
            spine.localRotation = spineBaseRotation * Quaternion.Euler(pitch, yaw, roll);
        }
    }

    private static Quaternion ComposeShoulderRotation(Quaternion baseRotation, float shoulderPitch, float shoulderRoll)
    {
        Quaternion rollRotation = Quaternion.AngleAxis(shoulderRoll, Vector3.forward);
        Quaternion pitchRotation = Quaternion.AngleAxis(shoulderPitch, Vector3.right);
        return baseRotation * rollRotation * pitchRotation;
    }

    private void EnsureMovementRigidbodyCached()
    {
        if (movementRigidbody != null)
        {
            return;
        }

        Transform movementRoot = MovementRoot;
        if (movementRoot == null)
        {
            return;
        }

        movementRigidbody = movementRoot.GetComponent<Rigidbody>();
        if (movementRigidbody == null)
        {
            movementRigidbody = movementRoot.GetComponentInParent<Rigidbody>();
        }
    }

    public bool TryGetPoseSnapshot(out TitanRigPoseSnapshot snapshot)
    {
        snapshot = default;
        if (!EnsureReady())
        {
            return false;
        }

        Transform movementRoot = MovementRoot;
        snapshot.RootPosition = movementRoot.position;
        snapshot.RootRotation = movementRoot.rotation;

        snapshot.HasLeftShoulder = leftShoulder != null;
        if (snapshot.HasLeftShoulder)
        {
            snapshot.LeftShoulderRotation = leftShoulder.localRotation;
        }

        snapshot.HasLeftElbow = leftElbow != null;
        if (snapshot.HasLeftElbow)
        {
            snapshot.LeftElbowRotation = leftElbow.localRotation;
        }

        snapshot.HasRightShoulder = rightShoulder != null;
        if (snapshot.HasRightShoulder)
        {
            snapshot.RightShoulderRotation = rightShoulder.localRotation;
        }

        snapshot.HasRightElbow = rightElbow != null;
        if (snapshot.HasRightElbow)
        {
            snapshot.RightElbowRotation = rightElbow.localRotation;
        }

        snapshot.HasLeftHip = leftHip != null;
        if (snapshot.HasLeftHip)
        {
            snapshot.LeftHipRotation = leftHip.localRotation;
        }

        snapshot.HasLeftKnee = leftKnee != null;
        if (snapshot.HasLeftKnee)
        {
            snapshot.LeftKneeRotation = leftKnee.localRotation;
        }

        snapshot.HasRightHip = rightHip != null;
        if (snapshot.HasRightHip)
        {
            snapshot.RightHipRotation = rightHip.localRotation;
        }

        snapshot.HasRightKnee = rightKnee != null;
        if (snapshot.HasRightKnee)
        {
            snapshot.RightKneeRotation = rightKnee.localRotation;
        }

        snapshot.HasSpine = spine != null;
        if (snapshot.HasSpine)
        {
            snapshot.SpineRotation = spine.localRotation;
        }

        return true;
    }

    public void ApplyPoseSnapshot(in TitanRigPoseSnapshot snapshot)
    {
        if (!EnsureReady())
        {
            return;
        }

        Transform movementRoot = MovementRoot;
        movementRoot.position = snapshot.RootPosition;
        movementRoot.rotation = snapshot.RootRotation;

        if (snapshot.HasLeftShoulder && leftShoulder != null)
        {
            leftShoulder.localRotation = snapshot.LeftShoulderRotation;
        }

        if (snapshot.HasLeftElbow && leftElbow != null)
        {
            leftElbow.localRotation = snapshot.LeftElbowRotation;
        }

        if (snapshot.HasRightShoulder && rightShoulder != null)
        {
            rightShoulder.localRotation = snapshot.RightShoulderRotation;
        }

        if (snapshot.HasRightElbow && rightElbow != null)
        {
            rightElbow.localRotation = snapshot.RightElbowRotation;
        }

        if (snapshot.HasLeftHip && leftHip != null)
        {
            leftHip.localRotation = snapshot.LeftHipRotation;
        }

        if (snapshot.HasLeftKnee && leftKnee != null)
        {
            leftKnee.localRotation = snapshot.LeftKneeRotation;
        }

        if (snapshot.HasRightHip && rightHip != null)
        {
            rightHip.localRotation = snapshot.RightHipRotation;
        }

        if (snapshot.HasRightKnee && rightKnee != null)
        {
            rightKnee.localRotation = snapshot.RightKneeRotation;
        }

        if (snapshot.HasSpine && spine != null)
        {
            spine.localRotation = snapshot.SpineRotation;
        }
    }

    private void ResolveAndCacheIfNeeded(bool forceCache)
    {
        int before = ComputeBoneSignature();
        ResolveBones();
        int after = ComputeBoneSignature();

        if (forceCache || !basePoseInitialized || before != after)
        {
            CacheBaseRotations();
            basePoseInitialized = true;
        }
    }

    private void ResolveBones()
    {
        mechaRoot ??= transform;

        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        Transform searchRoot = animator != null ? animator.transform : transform;

        if (animator != null && animator.isHuman)
        {
            leftShoulder ??= animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftElbow ??= animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightShoulder ??= animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightElbow ??= animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftHip ??= animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            leftKnee ??= animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightHip ??= animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            rightKnee ??= animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            spine ??= animator.GetBoneTransform(HumanBodyBones.Chest);
            spine ??= animator.GetBoneTransform(HumanBodyBones.UpperChest);
            spine ??= animator.GetBoneTransform(HumanBodyBones.Spine);
        }

        leftShoulder ??= FindChildByNames(searchRoot,
            "Character1_LeftShoulder", "Character1_LeftArm", "LeftShoulder", "LeftArm",
            "mixamorig:LeftShoulder", "mixamorig:LeftArm", "mixamorigLeftShoulder", "mixamorigLeftArm",
            "J_Bip_L_UpperArm", "LeftUpperArm", "UpperArm_L", "L_UpperArm", "Bip001 L Clavicle", "Bip001 L UpperArm");

        leftElbow ??= FindChildByNames(searchRoot,
            "Character1_LeftForeArm", "Character1_LeftLowerArm", "LeftForeArm", "LeftLowerArm", "L_Arm_Lower",
            "mixamorig:LeftForeArm", "mixamorigLeftForeArm", "J_Bip_L_LowerArm", "LeftLowerArm",
            "LowerArm_L", "L_ForeArm", "Bip001 L Forearm");

        rightShoulder ??= FindChildByNames(searchRoot,
            "Character1_RightShoulder", "Character1_RightArm", "RightShoulder", "RightArm",
            "mixamorig:RightShoulder", "mixamorig:RightArm", "mixamorigRightShoulder", "mixamorigRightArm",
            "J_Bip_R_UpperArm", "RightUpperArm", "UpperArm_R", "R_UpperArm", "Bip001 R Clavicle", "Bip001 R UpperArm");

        rightElbow ??= FindChildByNames(searchRoot,
            "Character1_RightForeArm", "Character1_RightLowerArm", "RightForeArm", "RightLowerArm", "R_Arm_Lower",
            "mixamorig:RightForeArm", "mixamorigRightForeArm", "J_Bip_R_LowerArm", "RightLowerArm",
            "LowerArm_R", "R_ForeArm", "Bip001 R Forearm");

        leftHip ??= FindChildByNames(searchRoot,
            "Character1_LeftUpLeg", "LeftUpLeg", "mixamorig:LeftUpLeg", "mixamorigLeftUpLeg",
            "J_Bip_L_UpperLeg", "LeftUpperLeg", "UpperLeg_L", "L_Thigh", "Bip001 L Thigh");

        leftKnee ??= FindChildByNames(searchRoot,
            "Character1_LeftLeg", "LeftLeg", "mixamorig:LeftLeg", "mixamorigLeftLeg",
            "J_Bip_L_LowerLeg", "LeftLowerLeg", "LowerLeg_L", "L_Calf", "Bip001 L Calf");

        rightHip ??= FindChildByNames(searchRoot,
            "Character1_RightUpLeg", "RightUpLeg", "mixamorig:RightUpLeg", "mixamorigRightUpLeg",
            "J_Bip_R_UpperLeg", "RightUpperLeg", "UpperLeg_R", "R_Thigh", "Bip001 R Thigh");

        rightKnee ??= FindChildByNames(searchRoot,
            "Character1_RightLeg", "RightLeg", "mixamorig:RightLeg", "mixamorigRightLeg",
            "J_Bip_R_LowerLeg", "RightLowerLeg", "LowerLeg_R", "R_Calf", "Bip001 R Calf");

        spine ??= FindChildByNames(searchRoot,
            "Character1_Chest", "Character1_UpperChest", "Character1_Spine",
            "UpperChest", "Chest", "Spine",
            "mixamorig:UpperChest", "mixamorig:Chest", "mixamorig:Spine",
            "mixamorigUpperChest", "mixamorigChest", "mixamorigSpine",
            "J_Bip_C_Chest", "J_Bip_C_Spine", "Bip001 Spine1", "Bip001 Spine");

        leftShoulder ??= FindByKeywords(searchRoot, true, "shoulder", "upperarm", "arm", "clavicle");
        leftElbow ??= FindByKeywords(leftShoulder != null ? leftShoulder : searchRoot, true, "lowerarm", "forearm", "elbow");
        rightShoulder ??= FindByKeywords(searchRoot, false, "shoulder", "upperarm", "arm", "clavicle");
        rightElbow ??= FindByKeywords(rightShoulder != null ? rightShoulder : searchRoot, false, "lowerarm", "forearm", "elbow");

        leftHip ??= FindByKeywords(searchRoot, true, "upleg", "upperleg", "thigh", "leg");
        leftKnee ??= FindByKeywords(leftHip != null ? leftHip : searchRoot, true, "lowerleg", "calf", "shin", "leg");
        rightHip ??= FindByKeywords(searchRoot, false, "upleg", "upperleg", "thigh", "leg");
        rightKnee ??= FindByKeywords(rightHip != null ? rightHip : searchRoot, false, "lowerleg", "calf", "shin", "leg");
        spine ??= FindByCenterKeywords(searchRoot, "upperchest", "chest", "spine", "torso", "waist");

        // Last resort: infer bones from skinned mesh bone transforms using spatial heuristics.
        if (!HasAnyDrivenBone())
        {
            ResolveFromSkinnedBoneHeuristics(searchRoot);
        }
    }

    private void ResolveFromSkinnedBoneHeuristics(Transform searchRoot)
    {
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        // Collect candidates.
        List<Transform> candidates = new List<Transform>(256);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer r = renderers[i];
            if (r == null)
                continue;

            if (mechaRoot == null && r.rootBone != null)
                mechaRoot = r.rootBone;

            Transform[] bones = r.bones;
            if (bones == null)
                continue;

            for (int b = 0; b < bones.Length; b++)
            {
                Transform t = bones[b];
                if (t != null)
                    candidates.Add(t);
            }
        }

        if (candidates.Count == 0)
            return;

        Transform space = searchRoot != null ? searchRoot : transform;

        // Compute local bounds in candidate space.
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 p = space.InverseTransformPoint(candidates[i].position);
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        float upperY = Mathf.Lerp(minY, maxY, 0.72f);
        float midY = Mathf.Lerp(minY, maxY, 0.50f);
        float lowY = Mathf.Lerp(minY, maxY, 0.30f);

        // Shoulders: far left/right at upper body.
        leftShoulder ??= PickExtremeX(space, candidates, upperY, true);
        rightShoulder ??= PickExtremeX(space, candidates, upperY, false);

        // Hips: far left/right at lower body.
        leftHip ??= PickExtremeX(space, candidates, lowY, true, below: true);
        rightHip ??= PickExtremeX(space, candidates, lowY, false, below: true);

        // Spine: near center x at mid-high.
        spine ??= PickCenterXHighest(space, candidates, midY);

        // Elbows: between shoulder and mid, closest to shoulder.
        leftElbow ??= PickClosestWithinBand(space, candidates, leftShoulder, midY, upperY, preferLeft: true);
        rightElbow ??= PickClosestWithinBand(space, candidates, rightShoulder, midY, upperY, preferLeft: false);

        // Knees: between low and mid, closest to hip.
        leftKnee ??= PickClosestWithinBand(space, candidates, leftHip, lowY, midY, preferLeft: true);
        rightKnee ??= PickClosestWithinBand(space, candidates, rightHip, lowY, midY, preferLeft: false);
    }

    private static Transform PickExtremeX(Transform space, List<Transform> candidates, float yThreshold, bool left, bool below = false)
    {
        Transform best = null;
        float bestX = left ? float.PositiveInfinity : float.NegativeInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            Transform t = candidates[i];
            Vector3 p = space.InverseTransformPoint(t.position);
            bool yOk = below ? p.y <= yThreshold : p.y >= yThreshold;
            if (!yOk)
                continue;

            if (left)
            {
                if (p.x < bestX)
                {
                    bestX = p.x;
                    best = t;
                }
            }
            else
            {
                if (p.x > bestX)
                {
                    bestX = p.x;
                    best = t;
                }
            }
        }

        return best;
    }

    private static Transform PickCenterXHighest(Transform space, List<Transform> candidates, float minY)
    {
        Transform best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            Transform t = candidates[i];
            Vector3 p = space.InverseTransformPoint(t.position);
            if (p.y < minY)
                continue;

            float score = (-Mathf.Abs(p.x) * 2f) + p.y;
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    private static Transform PickClosestWithinBand(Transform space, List<Transform> candidates, Transform anchor, float minY, float maxY, bool preferLeft)
    {
        if (anchor == null)
            return null;

        Vector3 anchorP = space.InverseTransformPoint(anchor.position);

        Transform best = null;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            Transform t = candidates[i];
            if (t == anchor)
                continue;

            Vector3 p = space.InverseTransformPoint(t.position);
            if (p.y < minY || p.y > maxY)
                continue;

            if (preferLeft && p.x > anchorP.x)
                continue;
            if (!preferLeft && p.x < anchorP.x)
                continue;

            float dist = Vector3.SqrMagnitude(p - anchorP);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = t;
            }
        }

        return best;
    }

    private bool HasAnyDrivenBone()
    {
        return
            leftShoulder != null ||
            leftElbow != null ||
            rightShoulder != null ||
            rightElbow != null ||
            leftHip != null ||
            leftKnee != null ||
            rightHip != null ||
            rightKnee != null ||
            spine != null;
    }

    private static Transform FindByCenterKeywords(Transform root, params string[] keywords)
    {
        if (root == null)
        {
            return null;
        }

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform current = all[i];
            string lower = current.name.ToLowerInvariant();

            if (IsRejectedBoneName(lower))
            {
                continue;
            }

            for (int k = 0; k < keywords.Length; k++)
            {
                if (lower.Contains(keywords[k]))
                {
                    return current;
                }
            }
        }

        return null;
    }

    private static Transform FindByKeywords(Transform root, bool isLeft, params string[] keywords)
    {
        if (root == null)
        {
            return null;
        }

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform current = all[i];
            string lower = current.name.ToLowerInvariant();

            if (IsRejectedBoneName(lower))
            {
                continue;
            }

            if (!IsExpectedSide(lower, isLeft))
            {
                continue;
            }

            for (int k = 0; k < keywords.Length; k++)
            {
                if (lower.Contains(keywords[k]))
                {
                    return current;
                }
            }
        }

        return null;
    }

    private static bool IsExpectedSide(string lower, bool isLeft)
    {
        bool hasLeft =
            lower.Contains("left") ||
            lower.Contains("_l") ||
            lower.Contains("l_") ||
            lower.Contains(".l") ||
            lower.Contains(" l ");

        bool hasRight =
            lower.Contains("right") ||
            lower.Contains("_r") ||
            lower.Contains("r_") ||
            lower.Contains(".r") ||
            lower.Contains(" r ");

        if (isLeft)
        {
            return hasLeft && !hasRight;
        }

        return hasRight && !hasLeft;
    }

    private static bool IsRejectedBoneName(string lowerName)
    {
        return
            lowerName.Contains("nub") ||
            lowerName.Contains("finger") ||
            lowerName.Contains("thumb") ||
            lowerName.Contains("index") ||
            lowerName.Contains("middle") ||
            lowerName.Contains("ring") ||
            lowerName.Contains("pinky") ||
            lowerName.Contains("toe") ||
            lowerName.Contains("head") ||
            lowerName.Contains("neck") ||
            lowerName.Contains("jaw") ||
            lowerName.Contains("eye");
    }

    private static Transform FindChildByNames(Transform root, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            Transform found = FindDeepChildExact(root, names[i]);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindDeepChildExact(Transform parent, string targetName)
    {
        if (parent == null)
        {
            return null;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, targetName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform result = FindDeepChildExact(child, targetName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private int ComputeBoneSignature()
    {
        unchecked
        {
            int signature = 17;
            signature = (signature * 23) + GetId(leftShoulder);
            signature = (signature * 23) + GetId(leftElbow);
            signature = (signature * 23) + GetId(rightShoulder);
            signature = (signature * 23) + GetId(rightElbow);
            signature = (signature * 23) + GetId(leftHip);
            signature = (signature * 23) + GetId(leftKnee);
            signature = (signature * 23) + GetId(rightHip);
            signature = (signature * 23) + GetId(rightKnee);
            signature = (signature * 23) + GetId(spine);
            return signature;
        }
    }

    private static int GetId(Transform value)
    {
        return value != null ? value.GetEntityId().GetHashCode() : 0;
    }

    private static string NameOrNone(Transform value)
    {
        return value != null ? value.name : "None";
    }

    private void CacheBaseRotations()
    {
        if (leftShoulder != null)
        {
            leftShoulderBaseRotation = leftShoulder.localRotation;
        }

        if (leftElbow != null)
        {
            leftElbowBaseRotation = leftElbow.localRotation;
        }

        if (rightShoulder != null)
        {
            rightShoulderBaseRotation = rightShoulder.localRotation;
        }

        if (rightElbow != null)
        {
            rightElbowBaseRotation = rightElbow.localRotation;
        }

        if (leftHip != null)
        {
            leftHipBaseRotation = leftHip.localRotation;
        }

        if (leftKnee != null)
        {
            leftKneeBaseRotation = leftKnee.localRotation;
        }

        if (rightHip != null)
        {
            rightHipBaseRotation = rightHip.localRotation;
        }

        if (rightKnee != null)
        {
            rightKneeBaseRotation = rightKnee.localRotation;
        }

        if (spine != null)
        {
            spineBaseRotation = spine.localRotation;
        }
    }
}
