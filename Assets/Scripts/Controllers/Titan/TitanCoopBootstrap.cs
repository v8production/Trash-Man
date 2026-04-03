using UnityEngine;

namespace Titan
{
    public static class TitanCoopBootstrap
    {
        private static bool IsBootstrapEnabled
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                return Debug.isDebugBuild;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AttachControllerIfMissing()
        {
            if (!IsBootstrapEnabled)
            {
                return;
            }

            TitanRig existingRig = Object.FindAnyObjectByType<TitanRig>();
            GameObject target = existingRig != null ? existingRig.gameObject : null;

            if (target == null)
            {
                target = GameObject.Find("Trash_titan");
            }

            if (target == null)
            {
                Animator[] animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
                for (int i = 0; i < animators.Length; i++)
                {
                    Animator current = animators[i];
                    if (current == null)
                    {
                        continue;
                    }

                    string lowerName = current.gameObject.name.ToLowerInvariant();
                    if (!lowerName.Contains("trash") && !lowerName.Contains("titan"))
                    {
                        continue;
                    }

                    target = current.gameObject;
                    break;
                }
            }

            if (target == null)
            {
                return;
            }

            EnsurePhysicsComponents(target);

            if (target.GetComponent<TitanRig>() == null)
            {
                target.AddComponent<TitanRig>();
            }

            if (target.GetComponent<TitanBodyRoleController>() == null)
            {
                target.AddComponent<TitanBodyRoleController>();
            }

            if (target.GetComponent<TitanLeftArmRoleController>() == null)
            {
                target.AddComponent<TitanLeftArmRoleController>();
            }

            if (target.GetComponent<TitanRightArmRoleController>() == null)
            {
                target.AddComponent<TitanRightArmRoleController>();
            }

            if (target.GetComponent<TitanLeftLegRoleController>() == null)
            {
                target.AddComponent<TitanLeftLegRoleController>();
            }

            if (target.GetComponent<TitanRightLegRoleController>() == null)
            {
                target.AddComponent<TitanRightLegRoleController>();
            }

            TitanLocalRoleSwitchTester tester = target.GetComponent<TitanLocalRoleSwitchTester>();
            if (tester == null)
            {
                tester = target.AddComponent<TitanLocalRoleSwitchTester>();
            }

            tester.SetHostAuthority(true);
        }

        private static void EnsurePhysicsComponents(GameObject target)
        {
            Rigidbody rigidbody = target.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = target.AddComponent<Rigidbody>();
            }

            rigidbody.useGravity = true;
            rigidbody.isKinematic = false;
            rigidbody.mass = 900f;
            rigidbody.linearDamping = 0.35f;
            rigidbody.angularDamping = 1.2f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                }

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = true;
            }

            SkinnedMeshRenderer[] skinnedMeshRenderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                SkinnedMeshRenderer skinnedMeshRenderer = skinnedMeshRenderers[i];
                if (skinnedMeshRenderer == null || skinnedMeshRenderer.sharedMesh == null)
                {
                    continue;
                }

                MeshCollider meshCollider = skinnedMeshRenderer.GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = skinnedMeshRenderer.gameObject.AddComponent<MeshCollider>();
                }

                meshCollider.convex = true;

                SkinnedMeshColliderSync sync = skinnedMeshRenderer.GetComponent<SkinnedMeshColliderSync>();
                if (sync == null)
                {
                    sync = skinnedMeshRenderer.gameObject.AddComponent<SkinnedMeshColliderSync>();
                }

                sync.Initialize(skinnedMeshRenderer, meshCollider);
            }
        }
    }

    public sealed class SkinnedMeshColliderSync : MonoBehaviour
    {
        private const float SyncIntervalSeconds = 0.05f;

        private SkinnedMeshRenderer sourceRenderer;
        private MeshCollider targetCollider;
        private Mesh bakedMesh;
        private float elapsed;

        public void Initialize(SkinnedMeshRenderer renderer, MeshCollider collider)
        {
            sourceRenderer = renderer;
            targetCollider = collider;
            EnsureBakedMesh();
            SyncNow();
            elapsed = 0f;
        }

        private void LateUpdate()
        {
            elapsed += Time.deltaTime;
            if (elapsed < SyncIntervalSeconds)
            {
                return;
            }

            elapsed = 0f;
            SyncNow();
        }

        private void OnDestroy()
        {
            if (bakedMesh != null)
            {
                Destroy(bakedMesh);
            }
        }

        private void EnsureBakedMesh()
        {
            if (bakedMesh == null)
            {
                bakedMesh = new Mesh
                {
                    name = "SkinnedColliderMesh"
                };
            }
        }

        private void SyncNow()
        {
            if (sourceRenderer == null || targetCollider == null)
            {
                return;
            }

            EnsureBakedMesh();
            sourceRenderer.BakeMesh(bakedMesh, true);
            targetCollider.sharedMesh = null;
            targetCollider.sharedMesh = bakedMesh;
        }
    }
}
