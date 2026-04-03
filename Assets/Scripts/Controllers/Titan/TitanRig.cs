using UnityEngine;

namespace Titan
{
    public class TitanRig : MonoBehaviour
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

        private Animator animator;

        private Quaternion leftShoulderBaseRotation;
        private Quaternion leftElbowBaseRotation;
        private Quaternion rightShoulderBaseRotation;
        private Quaternion rightElbowBaseRotation;
        private Quaternion leftHipBaseRotation;
        private Quaternion leftKneeBaseRotation;
        private Quaternion rightHipBaseRotation;
        private Quaternion rightKneeBaseRotation;

        private bool warnedMissingBones;
        private bool loggedResolvedBones;
        private bool basePoseInitialized;

        public Transform MovementRoot => mechaRoot != null ? mechaRoot : transform;

        private void Awake()
        {
            ResolveAndCacheIfNeeded(forceCache: true);
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
                Debug.LogWarning("[TitanRig] Could not resolve any controllable bones. Assign bone overrides on Trash_titan.", this);
            }

            if (hasAnyDrivenBone && !loggedResolvedBones)
            {
                loggedResolvedBones = true;
                Debug.Log($"[TitanRig] Resolved bones - LS:{NameOrNone(leftShoulder)} LE:{NameOrNone(leftElbow)} RS:{NameOrNone(rightShoulder)} RE:{NameOrNone(rightElbow)} LH:{NameOrNone(leftHip)} LK:{NameOrNone(leftKnee)} RH:{NameOrNone(rightHip)} RK:{NameOrNone(rightKnee)}", this);
            }

            return hasAnyDrivenBone;
        }

        public void ApplyLeftArm(float shoulderPitch, float shoulderYaw, float elbowPitch)
        {
            if (leftShoulder != null)
            {
                leftShoulder.localRotation = leftShoulderBaseRotation * Quaternion.Euler(shoulderPitch, shoulderYaw, 0f);
            }

            if (leftElbow != null)
            {
                leftElbow.localRotation = leftElbowBaseRotation * Quaternion.Euler(0f, 0f, elbowPitch);
            }
        }

        public void ApplyRightArm(float shoulderPitch, float shoulderYaw, float elbowPitch)
        {
            if (rightShoulder != null)
            {
                rightShoulder.localRotation = rightShoulderBaseRotation * Quaternion.Euler(shoulderPitch, shoulderYaw, 0f);
            }

            if (rightElbow != null)
            {
                rightElbow.localRotation = rightElbowBaseRotation * Quaternion.Euler(0f, 0f, elbowPitch);
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
            }

            leftShoulder ??= FindChildByNames(searchRoot,
                "Character1_LeftShoulder", "Character1_LeftArm", "LeftShoulder", "LeftArm",
                "mixamorig:LeftShoulder", "mixamorig:LeftArm", "mixamorigLeftShoulder", "mixamorigLeftArm",
                "J_Bip_L_UpperArm", "LeftUpperArm", "UpperArm_L", "L_UpperArm", "Bip001 L Clavicle", "Bip001 L UpperArm");

            leftElbow ??= FindChildByNames(searchRoot,
                "Character1_LeftForeArm", "Character1_LeftLowerArm", "LeftForeArm", "LeftLowerArm",
                "mixamorig:LeftForeArm", "mixamorigLeftForeArm", "J_Bip_L_LowerArm", "LeftLowerArm",
                "LowerArm_L", "L_ForeArm", "Bip001 L Forearm");

            rightShoulder ??= FindChildByNames(searchRoot,
                "Character1_RightShoulder", "Character1_RightArm", "RightShoulder", "RightArm",
                "mixamorig:RightShoulder", "mixamorig:RightArm", "mixamorigRightShoulder", "mixamorigRightArm",
                "J_Bip_R_UpperArm", "RightUpperArm", "UpperArm_R", "R_UpperArm", "Bip001 R Clavicle", "Bip001 R UpperArm");

            rightElbow ??= FindChildByNames(searchRoot,
                "Character1_RightForeArm", "Character1_RightLowerArm", "RightForeArm", "RightLowerArm",
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

            leftShoulder ??= FindByKeywords(searchRoot, true, "shoulder", "upperarm", "arm", "clavicle");
            leftElbow ??= FindByKeywords(leftShoulder != null ? leftShoulder : searchRoot, true, "lowerarm", "forearm", "elbow");
            rightShoulder ??= FindByKeywords(searchRoot, false, "shoulder", "upperarm", "arm", "clavicle");
            rightElbow ??= FindByKeywords(rightShoulder != null ? rightShoulder : searchRoot, false, "lowerarm", "forearm", "elbow");

            leftHip ??= FindByKeywords(searchRoot, true, "upleg", "upperleg", "thigh", "leg");
            leftKnee ??= FindByKeywords(leftHip != null ? leftHip : searchRoot, true, "lowerleg", "calf", "shin", "leg");
            rightHip ??= FindByKeywords(searchRoot, false, "upleg", "upperleg", "thigh", "leg");
            rightKnee ??= FindByKeywords(rightHip != null ? rightHip : searchRoot, false, "lowerleg", "calf", "shin", "leg");
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
                rightKnee != null;
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
                return signature;
            }
        }

        private static int GetId(Transform value)
        {
            return value != null ? value.GetInstanceID() : 0;
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
        }
    }
}
